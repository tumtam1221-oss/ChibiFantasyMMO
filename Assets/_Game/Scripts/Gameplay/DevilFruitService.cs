using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a Devil Fruit could not be taken on.</summary>
    public enum DevilFruitRejection
    {
        None = 0,

        /// <summary>No state or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such fruit could be resolved.</summary>
        UnknownFruit = 2,

        /// <summary>Content turned the fruit off.</summary>
        FruitDisabled = 3,

        /// <summary>The character already carries a fruit's power.</summary>
        AlreadyHasFruit = 4,

        /// <summary>The fruit references a skill that does not resolve.</summary>
        UnknownAbility = 5,

        /// <summary>The fruit references a status effect that does not resolve.</summary>
        UnknownEffect = 6,

        /// <summary>The activation is not this character's to make.</summary>
        NotOwned = 7
    }

    /// <summary>
    /// What activating a Devil Fruit granted.
    /// </summary>
    /// <remarks>
    /// <b>Ids, never objects.</b> A skill id, effect ids and two asset references. A
    /// presenter can find its VFX from this and a server can log it, and neither can reach
    /// through it into gameplay state.
    /// </remarks>
    public readonly struct DevilFruitResult
    {
        private DevilFruitResult(bool accepted, DevilFruitRejection reason, DefinitionId fruit,
            DefinitionId passive, DefinitionId active, int effectsApplied, int immunitiesGranted,
            AssetRef visual, AssetRef sound)
        {
            IsAccepted = accepted;
            Reason = reason;
            Fruit = fruit;
            PassiveAbility = passive;
            ActiveAbility = active;
            EffectsApplied = effectsApplied;
            ImmunitiesGranted = immunitiesGranted;
            VisualEffect = visual;
            SoundEffect = sound;
        }

        public bool IsAccepted { get; }

        public DevilFruitRejection Reason { get; }

        public DefinitionId Fruit { get; }

        /// <summary>Reference to the passive <see cref="SkillDefinition"/>, if the fruit has one.</summary>
        public DefinitionId PassiveAbility { get; }

        /// <summary>
        /// Reference to the active <see cref="SkillDefinition"/> the bearer may now use.
        /// </summary>
        /// <remarks>Granted, not executed. Using it goes through <c>SkillUseValidator</c> and
        /// <c>SkillExecutor</c> exactly as any other skill does; there is no fruit skill
        /// path.</remarks>
        public DefinitionId ActiveAbility { get; }

        /// <summary>How many authored status effects actually landed.</summary>
        public int EffectsApplied { get; }

        /// <summary>How many standing refusals the fruit installed.</summary>
        public int ImmunitiesGranted { get; }

        public AssetRef VisualEffect { get; }

        public AssetRef SoundEffect { get; }

        public static DevilFruitResult Accepted(DefinitionId fruit, DefinitionId passive,
            DefinitionId active, int effects, int immunities, AssetRef visual, AssetRef sound)
        {
            return new DevilFruitResult(true, DevilFruitRejection.None, fruit, passive, active,
                effects, immunities, visual, sound);
        }

        public static DevilFruitResult Rejected(DevilFruitRejection reason,
            DefinitionId fruit = default)
        {
            return new DevilFruitResult(false, reason, fruit, default, default, 0, 0,
                default, default);
        }

        public override string ToString()
        {
            return IsAccepted ? Fruit + " activated" : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Taking on the power of a Devil Fruit.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here knows a fruit.</b> There is no darkness path and no light path, and
    /// no <see cref="DefinitionId"/> is compared to a literal. Darkness silences because a
    /// <see cref="DevilFruitDefinition"/> asset points at a
    /// <see cref="StatusEffectDefinition"/> whose <see cref="ControlEffectType"/> is
    /// silence; Light shrugs off debuffs because another asset lists
    /// <see cref="StatusEffectCategory.Debuff"/> among its immune categories. Both walk the
    /// same six lines of code, and an eleventh fruit is an asset.
    ///
    /// <b>It grants; it does not execute.</b> An active ability is reported as a skill id
    /// the bearer may now use. Casting it goes through <c>SkillUseValidator</c> and
    /// <c>SkillExecutor</c> like every other skill -- there is no fruit skill executor and
    /// no fruit combat path, so a fruit ability obeys cooldowns, costs and silence for free.
    ///
    /// <b>Validate fully, then mutate.</b> Every reference is resolved before the status
    /// state is touched, so a fruit whose effect was deleted by a patch is refused with the
    /// character untouched rather than half-applied. The item is spent by
    /// <see cref="ItemUseService"/> only after this has already accepted.
    /// </remarks>
    public static class DevilFruitService
    {
        /// <summary>Everything an activation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<DevilFruitDefinition> fruits,
                StatusEffectRuntimeState status = null,
                IDefinitionRegistry<StatusEffectDefinition> effects = null,
                IDefinitionRegistry<SkillDefinition> skills = null,
                OwnerId owner = default)
            {
                Fruits = fruits;
                Status = status;
                Effects = effects;
                Skills = skills;
                Owner = owner;
            }

            public IDefinitionRegistry<DevilFruitDefinition> Fruits { get; }

            /// <summary>Where granted effects and immunities land. Optional.</summary>
            public StatusEffectRuntimeState Status { get; }

            /// <summary>Needed to resolve granted effects and immunities.</summary>
            public IDefinitionRegistry<StatusEffectDefinition> Effects { get; }

            /// <summary>Needed only to confirm an authored ability actually exists.</summary>
            public IDefinitionRegistry<SkillDefinition> Skills { get; }

            /// <summary>
            /// Who is acting.
            /// </summary>
            /// <remarks>Left invalid when a caller has no ownership to assert. A server
            /// always supplies it; this is the seam that refuses somebody else's character.</remarks>
            public OwnerId Owner { get; }

            public bool IsUsable => Fruits != null;
        }

        /// <summary>
        /// Whether a fruit could be taken on, without changing anything.
        /// </summary>
        /// <remarks>
        /// Every check <see cref="TryActivate"/> makes, and no writes. Exposed because
        /// <see cref="ItemUseService"/> has to know the activation will succeed <em>before</em>
        /// it spends the item: asking afterwards would mean eating a fruit that turned out to
        /// be unusable. It is also what a UI asks to grey out a button, so the panel and the
        /// service cannot disagree.
        /// </remarks>
        public static DevilFruitRejection CanActivate(CharacterDevilFruitState state,
            DefinitionId fruitId, in Context context)
        {
            if (state == null || !context.IsUsable) return DevilFruitRejection.MissingContext;

            if (context.Owner.IsValid && state.Owner.IsValid && state.Owner != context.Owner)
                return DevilFruitRejection.NotOwned;

            DevilFruitDefinition fruit;
            if (!fruitId.IsValid || !context.Fruits.TryGet(fruitId, out fruit) || fruit == null)
                return DevilFruitRejection.UnknownFruit;

            if (!fruit.Enabled) return DevilFruitRejection.FruitDisabled;

            // Refused, never replaced. Swapping would destroy a power the player already
            // spent an ultra-rare drop on, and no authored mechanic says it may.
            if (state.HasActiveFruit) return DevilFruitRejection.AlreadyHasFruit;

            if (context.Skills != null)
            {
                if (!AbilityResolves(fruit.PassiveAbility, context.Skills))
                    return DevilFruitRejection.UnknownAbility;

                if (!AbilityResolves(fruit.ActiveAbility, context.Skills))
                    return DevilFruitRejection.UnknownAbility;
            }

            if (context.Effects != null)
            {
                DefinitionId[] granted = fruit.GrantedEffects;

                for (int i = 0; i < granted.Length; i++)
                {
                    if (!EffectResolves(granted[i], context.Effects))
                        return DevilFruitRejection.UnknownEffect;
                }

                DefinitionId[] immunities = fruit.Immunities;

                for (int i = 0; i < immunities.Length; i++)
                {
                    if (!EffectResolves(immunities[i], context.Effects))
                        return DevilFruitRejection.UnknownEffect;
                }
            }

            return DevilFruitRejection.None;
        }

        /// <summary>
        /// Takes on a fruit's power.
        /// </summary>
        /// <param name="state">The character's active-fruit record.</param>
        /// <param name="fruitId">Reference to a <see cref="DevilFruitDefinition"/>.</param>
        /// <param name="source">The item copy being spent, for audit.</param>
        /// <param name="context">Registries and the status runtime.</param>
        /// <remarks>
        /// The item is not touched here. <see cref="ItemUseService"/> owns spending, and it
        /// spends only after this returns an acceptance -- one consumption, decided in one
        /// place.
        /// </remarks>
        public static DevilFruitResult TryActivate(CharacterDevilFruitState state,
            DefinitionId fruitId, InstanceId source, in Context context)
        {
            DevilFruitRejection refusal = CanActivate(state, fruitId, context);
            if (refusal != DevilFruitRejection.None)
                return DevilFruitResult.Rejected(refusal, fruitId);

            DevilFruitDefinition fruit;
            context.Fruits.TryGet(fruitId, out fruit);

            // ---- everything is resolved and nothing below can fail ---------------------

            state.Activate(fruitId, source);

            int applied = 0;
            int immunitiesGranted = 0;

            if (context.Status != null && context.Effects != null)
            {
                // Immunities are installed before effects, so a fruit that refuses a
                // category cannot be handed one of its own effects in that category by
                // ordering accident.
                immunitiesGranted = GrantImmunities(fruit, context.Status);
                applied = ApplyGrantedEffects(fruit, context);
            }

            return DevilFruitResult.Accepted(fruitId, fruit.PassiveAbility, fruit.ActiveAbility,
                applied, immunitiesGranted, fruit.VisualEffect, fruit.SoundEffect);
        }

        /// <summary>
        /// Appends what an active fruit contributes to a character's stats.
        /// </summary>
        /// <remarks>
        /// Collected, never computed -- the same division
        /// <see cref="EquipmentModifierResolver"/> keeps, so a fruit's numbers reach
        /// <see cref="DerivedStatsCalculator"/> through the one arithmetic path that already
        /// exists. A fruit's effects contribute separately through
        /// <see cref="StatusEffectRuntimeState.CollectModifiers"/>, because those expire and
        /// these do not.
        /// </remarks>
        public static void CollectModifiers(CharacterDevilFruitState state, in Context context,
            List<StatModifier> into)
        {
            if (into == null || state == null || !context.IsUsable) return;
            if (!state.HasActiveFruit) return;

            DevilFruitDefinition fruit;
            if (!context.Fruits.TryGet(state.ActiveFruit, out fruit) || fruit == null) return;

            StatModifier[] modifiers = fruit.StatModifiers;

            for (int i = 0; i < modifiers.Length; i++) into.Add(modifiers[i]);
        }

        /// <summary>
        /// The skill an active fruit lets its bearer use, or none.
        /// </summary>
        /// <remarks>What a skill bar asks. It returns an id, so the answer goes through the
        /// existing skill pipeline rather than around it.</remarks>
        public static DefinitionId ActiveAbilityOf(CharacterDevilFruitState state,
            in Context context)
        {
            if (state == null || !context.IsUsable || !state.HasActiveFruit)
                return DefinitionId.None;

            DevilFruitDefinition fruit;
            if (!context.Fruits.TryGet(state.ActiveFruit, out fruit) || fruit == null)
                return DefinitionId.None;

            return fruit.ActiveAbility;
        }

        private static int GrantImmunities(DevilFruitDefinition fruit, StatusEffectRuntimeState status)
        {
            int granted = 0;

            DefinitionId[] byEffect = fruit.Immunities;

            for (int i = 0; i < byEffect.Length; i++)
            {
                if (!byEffect[i].IsValid) continue;
                if (status.AddImmunity(new StatusImmunity(fruit.Id, byEffect[i]))) granted++;
            }

            StatusEffectCategory[] byCategory = fruit.ImmuneCategories;

            for (int i = 0; i < byCategory.Length; i++)
            {
                if (byCategory[i] == StatusEffectCategory.None) continue;

                if (status.AddImmunity(new StatusImmunity(fruit.Id, default, byCategory[i])))
                {
                    granted++;
                }
            }

            return granted;
        }

        private static int ApplyGrantedEffects(DevilFruitDefinition fruit, in Context context)
        {
            int applied = 0;

            DefinitionId[] granted = fruit.GrantedEffects;

            for (int i = 0; i < granted.Length; i++)
            {
                StatusApplyResult result = StatusEffectService.TryApply(context.Status,
                    granted[i], fruit.Id, context.Effects);

                if (result.IsAccepted) applied++;
            }

            return applied;
        }

        private static bool AbilityResolves(DefinitionId ability,
            IDefinitionRegistry<SkillDefinition> skills)
        {
            if (!ability.IsValid) return true;   // authoring none is not an error

            SkillDefinition skill;
            return skills.TryGet(ability, out skill) && skill != null;
        }

        private static bool EffectResolves(DefinitionId effect,
            IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            if (!effect.IsValid) return true;

            StatusEffectDefinition definition;
            return effects.TryGet(effect, out definition) && definition != null;
        }
    }
}
