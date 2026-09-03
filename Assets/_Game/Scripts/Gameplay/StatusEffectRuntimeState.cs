using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One status effect currently on a character.
    /// </summary>
    /// <remarks>
    /// <b>Flat because it has to persist and to travel.</b> An effect id, what granted it,
    /// how long is left and how many stacks. One row of a future
    /// <c>character_status_effect</c> table maps onto this exactly, and a server sends the
    /// same four values to a client.
    ///
    /// <b>What the effect does is not copied here.</b> Modifiers, category and control type
    /// are read off <see cref="StatusEffectDefinition"/> at resolve time, so re-authoring an
    /// effect changes every character already carrying it and no runtime row holds a stale
    /// duplicate of authored numbers. The same rule <see cref="EquipmentEnchant"/> follows.
    /// </remarks>
    public readonly struct ActiveStatusEffect
    {
        public ActiveStatusEffect(DefinitionId effect, DefinitionId source, float remainingSeconds,
            int stacks)
        {
            Effect = effect;
            Source = source;
            RemainingSeconds = remainingSeconds;
            Stacks = stacks < 1 ? 1 : stacks;
        }

        /// <summary>Reference to a <see cref="StatusEffectDefinition"/>.</summary>
        public DefinitionId Effect { get; }

        /// <summary>
        /// What granted it.
        /// </summary>
        /// <remarks>A fruit, a pet, an item or a skill definition. Kept so a grantor can
        /// take back exactly what it gave without guessing, which is what makes unequipping
        /// a source safe.</remarks>
        public DefinitionId Source { get; }

        /// <summary>
        /// Seconds left, or zero or less for an effect that does not expire.
        /// </summary>
        /// <remarks>Indefinite is the convention <see cref="StatusEffectDefinition.DurationSeconds"/>
        /// already uses for an unauthored duration, kept rather than translated so the two
        /// layers read the same way.</remarks>
        public float RemainingSeconds { get; }

        public int Stacks { get; }

        public bool IsIndefinite => RemainingSeconds <= 0f;

        public bool IsValid => Effect.IsValid;

        public ActiveStatusEffect WithRemaining(float seconds)
        {
            return new ActiveStatusEffect(Effect, Source, seconds, Stacks);
        }

        public ActiveStatusEffect WithStacks(int stacks)
        {
            return new ActiveStatusEffect(Effect, Source, RemainingSeconds, stacks);
        }

        public override string ToString()
        {
            return Effect + (Stacks > 1 ? " x" + Stacks : string.Empty)
                + (IsIndefinite ? string.Empty : " " + RemainingSeconds + "s");
        }
    }

    /// <summary>
    /// A standing refusal to accept some status effect.
    /// </summary>
    /// <remarks>
    /// Either one named effect or a whole <see cref="StatusEffectCategory"/>. The second
    /// form is what "immune to debuffs" means, and expressing it as a category rather than
    /// as an exhaustive list of effect ids is what stops a newly authored debuff quietly
    /// bypassing an immunity nobody remembered to update.
    ///
    /// <see cref="Source"/> is what granted it, so removing the grantor removes exactly its
    /// own immunities and leaves anyone else's alone.
    /// </remarks>
    public readonly struct StatusImmunity
    {
        public StatusImmunity(DefinitionId source, DefinitionId effect,
            StatusEffectCategory category = StatusEffectCategory.None)
        {
            Source = source;
            Effect = effect;
            Category = category;
        }

        public DefinitionId Source { get; }

        /// <summary>The one effect refused. Invalid when this is a category immunity.</summary>
        public DefinitionId Effect { get; }

        /// <summary><see cref="StatusEffectCategory.None"/> when this names a single effect.</summary>
        public StatusEffectCategory Category { get; }

        public bool IsValid => Effect.IsValid || Category != StatusEffectCategory.None;

        /// <summary>Whether this refusal covers a given effect.</summary>
        public bool Covers(DefinitionId effect, StatusEffectCategory category)
        {
            if (Effect.IsValid && Effect == effect) return true;

            return Category != StatusEffectCategory.None && Category == category;
        }
    }

    /// <summary>
    /// Every status effect on one character, and everything it refuses.
    /// </summary>
    /// <remarks>
    /// <b>The status runtime Phase 08.3 and Phase 09 said did not exist.</b> Both phases
    /// resolved and reported buffs without applying them, and both documented that as a
    /// real gap. This closes it, and it is deliberately the smallest thing that does:
    /// a list of effects, a list of immunities, a clock driven by the caller.
    ///
    /// <b>General, not fruit-specific.</b> Nothing below mentions a Devil Fruit, a pet or a
    /// card. <see cref="ActiveStatusEffect.Source"/> is whatever granted the effect, so the
    /// item buffs Phase 08.3 already resolves and the skill effects Phase 07 already
    /// computes plug into this without a second engine -- which is exactly why it is not
    /// called a fruit status engine and does not live next to one.
    ///
    /// <b>Caller-supplied time.</b> <see cref="Tick"/> takes elapsed seconds, the same
    /// contract <c>SkillCooldownState</c> and <c>AttackStateMachine</c> keep. Nothing here
    /// reads a clock, so this assembly stays engine-free and expiry is reproducible in a
    /// test.
    ///
    /// <b>Nothing is polled.</b> Effects change when something applies, removes or ticks
    /// them; <see cref="Revision"/> moves only on a real change, so a panel rebuilds when
    /// the set actually differs rather than every frame.
    /// </remarks>
    public sealed class StatusEffectRuntimeState : IRuntimeState
    {
        private readonly List<ActiveStatusEffect> _active = new List<ActiveStatusEffect>();
        private readonly List<StatusImmunity> _immunities = new List<StatusImmunity>();

        private Revision _revision;

        public StatusEffectRuntimeState(CharacterId characterId = default)
        {
            CharacterId = characterId;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        public Revision Revision => _revision;

        /// <summary>Everything currently applied. Read-only: only this class may change it.</summary>
        public IReadOnlyList<ActiveStatusEffect> Active => _active;

        /// <summary>Every standing refusal.</summary>
        public IReadOnlyList<StatusImmunity> Immunities => _immunities;

        public int ActiveCount => _active.Count;

        public bool Has(DefinitionId effect)
        {
            return IndexOf(effect) >= 0;
        }

        /// <summary>The applied effect, or an invalid one.</summary>
        public ActiveStatusEffect Get(DefinitionId effect)
        {
            int index = IndexOf(effect);
            return index < 0 ? default : _active[index];
        }

        /// <summary>
        /// Whether any control effect of a given kind is currently on the character.
        /// </summary>
        /// <remarks>
        /// How a combat rule asks "is this character silenced" without knowing which effect
        /// silenced them. The <see cref="ControlEffectType"/> is read off each applied
        /// effect's definition, so a second silencing effect authored tomorrow answers this
        /// too, with no code change and no list of effect ids anywhere.
        /// </remarks>
        public bool HasControl(ControlEffectType control,
            IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            if (control == ControlEffectType.None || effects == null) return false;

            for (int i = 0; i < _active.Count; i++)
            {
                StatusEffectDefinition definition;
                if (!effects.TryGet(_active[i].Effect, out definition) || definition == null)
                {
                    continue;
                }

                if (definition.ControlEffect == control) return true;
            }

            return false;
        }

        /// <summary>Whether a refusal covers an effect.</summary>
        public bool IsImmuneTo(DefinitionId effect, StatusEffectCategory category)
        {
            for (int i = 0; i < _immunities.Count; i++)
            {
                if (_immunities[i].Covers(effect, category)) return true;
            }

            return false;
        }

        /// <summary>
        /// Applies an effect, honouring its authored stacking rule.
        /// </summary>
        /// <remarks>
        /// Assignment only: whether the character <em>may</em> receive it, including every
        /// immunity check, is <see cref="StatusEffectService"/>'s decision. Keeping the
        /// rules out of the state is what lets a server apply a decision it made elsewhere.
        ///
        /// Returns false when the authored behaviour is to ignore a re-application, so a
        /// caller can tell "already had it, unchanged" from "applied".
        /// </remarks>
        public bool Apply(ActiveStatusEffect effect, StatusEffectStackBehavior behavior,
            int maxStacks)
        {
            if (!effect.IsValid) return false;

            int index = IndexOf(effect.Effect);

            if (index < 0)
            {
                _active.Add(effect);
                _revision = _revision.Next();
                return true;
            }

            ActiveStatusEffect existing = _active[index];

            switch (behavior)
            {
                case StatusEffectStackBehavior.Ignore:
                    return false;

                case StatusEffectStackBehavior.AddStack:
                {
                    int ceiling = maxStacks < 1 ? 1 : maxStacks;
                    int stacks = existing.Stacks + effect.Stacks;
                    if (stacks > ceiling) stacks = ceiling;

                    // A refresh that changes nothing must not look like a mutation.
                    if (stacks == existing.Stacks
                        && existing.RemainingSeconds >= effect.RemainingSeconds)
                    {
                        return false;
                    }

                    _active[index] = new ActiveStatusEffect(existing.Effect, existing.Source,
                        Longer(existing.RemainingSeconds, effect.RemainingSeconds), stacks);
                    break;
                }

                case StatusEffectStackBehavior.ReplaceIfStronger:
                {
                    if (effect.Stacks < existing.Stacks) return false;
                    if (effect.Stacks == existing.Stacks
                        && effect.RemainingSeconds <= existing.RemainingSeconds
                        && !effect.IsIndefinite)
                    {
                        return false;
                    }

                    _active[index] = effect;
                    break;
                }

                default:
                    // RefreshDuration. The longer of the two wins, so a short re-application
                    // cannot shorten a buff a player already has.
                    if (!existing.IsIndefinite
                        && Longer(existing.RemainingSeconds, effect.RemainingSeconds)
                            == existing.RemainingSeconds)
                    {
                        return false;
                    }

                    _active[index] = existing.WithRemaining(
                        Longer(existing.RemainingSeconds, effect.RemainingSeconds));
                    break;
            }

            _revision = _revision.Next();
            return true;
        }

        /// <summary>Removes one effect. Advances the revision only if it was there.</summary>
        public bool Remove(DefinitionId effect)
        {
            int index = IndexOf(effect);
            if (index < 0) return false;

            _active.RemoveAt(index);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Removes everything one source granted, effects and immunities alike.
        /// </summary>
        /// <remarks>What taking off a source has to do. Matching on the grantor rather than
        /// on a remembered list is what makes it exact: nothing else's effects are touched,
        /// and nothing granted twice by the same source survives.</remarks>
        public int RemoveFrom(DefinitionId source)
        {
            if (!source.IsValid) return 0;

            int removed = 0;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Source != source) continue;

                _active.RemoveAt(i);
                removed++;
            }

            for (int i = _immunities.Count - 1; i >= 0; i--)
            {
                if (_immunities[i].Source != source) continue;

                _immunities.RemoveAt(i);
                removed++;
            }

            if (removed > 0) _revision = _revision.Next();
            return removed;
        }

        /// <summary>Records a standing refusal.</summary>
        public bool AddImmunity(StatusImmunity immunity)
        {
            if (!immunity.IsValid) return false;

            _immunities.Add(immunity);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Advances every timer and drops what expired.
        /// </summary>
        /// <remarks>
        /// Indefinite effects are skipped rather than counted down, so a permanent passive
        /// cannot expire because somebody ticked often enough. Negative or zero elapsed time
        /// changes nothing, which keeps a paused frame from being a mutation.
        /// </remarks>
        public int Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || _active.Count == 0) return 0;

            int expired = 0;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveStatusEffect effect = _active[i];
                if (effect.IsIndefinite) continue;

                float remaining = effect.RemainingSeconds - deltaSeconds;

                if (remaining <= 0f)
                {
                    _active.RemoveAt(i);
                    expired++;
                    continue;
                }

                _active[i] = effect.WithRemaining(remaining);
            }

            // Any tick that moved a timer is a change; expiry is not the only one.
            _revision = _revision.Next();
            return expired;
        }

        /// <summary>
        /// Appends the modifiers every applied effect contributes.
        /// </summary>
        /// <remarks>
        /// Collected, never computed -- the same division <c>EquipmentModifierResolver</c>
        /// keeps. Turning these into numbers is <see cref="DerivedStatsCalculator"/>'s job
        /// and is not duplicated here.
        ///
        /// A stacked effect contributes its flat modifiers once per stack, because that is
        /// what a stack means. Percentages are contributed once, for the reason the enchant
        /// resolver gives: scaling a percentage by a count compounds.
        /// </remarks>
        public void CollectModifiers(IDefinitionRegistry<StatusEffectDefinition> effects,
            List<StatModifier> into)
        {
            if (into == null || effects == null) return;

            for (int i = 0; i < _active.Count; i++)
            {
                ActiveStatusEffect active = _active[i];

                StatusEffectDefinition definition;
                if (!effects.TryGet(active.Effect, out definition) || definition == null) continue;

                StatModifier[] modifiers = definition.StatModifiers;
                if (modifiers == null) continue;

                for (int m = 0; m < modifiers.Length; m++)
                {
                    StatModifier modifier = modifiers[m];

                    if (active.Stacks > 1 && modifier.Kind == StatModifierKind.Flat)
                    {
                        into.Add(new StatModifier(modifier.Stat, modifier.Kind,
                            modifier.Value * active.Stacks));
                        continue;
                    }

                    into.Add(modifier);
                }
            }
        }

        private int IndexOf(DefinitionId effect)
        {
            if (!effect.IsValid) return -1;

            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Effect == effect) return i;
            }

            return -1;
        }

        /// <summary>Indefinite beats any finite duration; otherwise the larger wins.</summary>
        private static float Longer(float a, float b)
        {
            if (a <= 0f || b <= 0f) return 0f;
            return a >= b ? a : b;
        }
    }
}
