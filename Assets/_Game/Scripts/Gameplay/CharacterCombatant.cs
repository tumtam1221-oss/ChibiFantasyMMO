using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Lets an existing <see cref="Character"/> take part in a fight.
    /// </summary>
    /// <remarks>
    /// <b>An adapter, and nothing more.</b> It stores no health, no maximum and no stat.
    /// Every read forwards to the aggregate that already owns the answer and
    /// <see cref="ApplyHealthDelta"/> forwards to
    /// <see cref="CharacterResourceState.ChangeHealth"/>, so clamping at zero, the ceiling,
    /// the revision bump and the rule that a change of nothing is not a change all behave
    /// exactly as they do for any other caller. Combat gains no privileged path into
    /// character state.
    ///
    /// <b>Limits are held, not derived here.</b> <see cref="ResourceLimits"/> is passed in
    /// because the resource state deliberately refuses to store a ceiling, and the
    /// calculator remains the only thing that decides what a maximum is. When stats change,
    /// the owner recomputes and calls <see cref="SetLimits"/>; a combatant carrying a stale
    /// ceiling is exactly the failure the resource design warns about, so this cannot
    /// compute one for itself.
    ///
    /// <b>Position is the one piece of genuinely new state.</b> A character aggregate has no
    /// place in the world -- it is persistent identity, class, progression and stats -- so
    /// something has to hold it for range checks. It lives here as runtime data the
    /// presentation layer writes each frame, and it is deliberately not pushed down into
    /// the persistent aggregate.
    ///
    /// Team is supplied rather than read from the character, because faction is a property
    /// of a fight, not of a person: the same character is an ally in a party and an enemy
    /// in a duel.
    /// </remarks>
    public sealed class CharacterCombatant : ICombatant, ICombatantResourcePool,
        IStatusEffectTarget
    {
        private readonly Character _character;
        private readonly InstanceId _combatantId;
        private DerivedStatsResult _derived;
        private ResourceLimits _limits;

        public CharacterCombatant(Character character, DerivedStatsResult derived,
            ResourceLimits limits, CombatTeam team)
        {
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _derived = derived;
            _limits = limits;
            Team = team;

            // The character id already identifies this entity uniquely; minting a fresh
            // InstanceId would create a second identity for the same thing and break
            // self-targeting, which compares ids.
            _combatantId = new InstanceId(character.Identity.CharacterId.Value);
        }

        /// <summary>The adapted character. Exposed so callers need not hold it twice.</summary>
        public Character Character => _character;

        public InstanceId CombatantId => _combatantId;

        public CombatTeam Team { get; set; }

        public CombatPosition Position { get; set; }

        /// <summary>
        /// The status effects on this character, or null when nothing tracks them.
        /// </summary>
        /// <remarks>
        /// <b>A reference, not a copy.</b> The world holds one
        /// <see cref="StatusEffectRuntimeState"/> per character and points this at it, so a
        /// skill that applies a debuff and a validator that asks about silence are looking
        /// at the same list. A combatant that owned its own would be a second status
        /// container, and the two would disagree the first time one was ticked.
        ///
        /// Settable because the combatant is built before the world decides whether it
        /// tracks status at all -- a combat sandbox with no status runtime leaves it null
        /// and every status effect reports itself unsupported rather than silently landing
        /// nowhere.
        /// </remarks>
        public StatusEffectRuntimeState Status { get; set; }

        public int CurrentHealth => _character.Resources.CurrentHealth;

        public int MaxHealth => _limits.MaxHealth;

        /// <summary>The ceilings currently in force.</summary>
        public ResourceLimits Limits => _limits;

        /// <summary>
        /// Supplies freshly recomputed ceilings and stats.
        /// </summary>
        /// <remarks>
        /// Clamps the resources into the new range through the existing
        /// <see cref="CharacterResourceState.ClampTo"/>, so a dropped maximum behaves here
        /// exactly as it does everywhere else rather than leaving health above its ceiling.
        ///
        /// <b>An unspecified ceiling clamps nothing.</b> All-zero limits mean the derived
        /// stats have not been computed yet, not that this character may have no health --
        /// see <see cref="ResourceLimits.IsSpecified"/>. Clamping against them would take a
        /// player who loaded with 75 health and put them into the world dead, and nothing
        /// downstream could tell that from a real death. The limits are still recorded, so
        /// the combatant reports honestly that it does not know them yet.
        /// </remarks>
        public void SetLimits(ResourceLimits limits, DerivedStatsResult derived = null)
        {
            _limits = limits;

            if (derived != null)
            {
                _derived = derived;
            }

            if (!_limits.IsSpecified) return;

            _character.Resources.ClampTo(_limits);
        }

        /// <summary>
        /// Reads a combat stat, preferring the computed figure.
        /// </summary>
        /// <remarks>
        /// Derived stats are consulted first because they are the ones that account for
        /// level, equipment and buffs; the base stats behind them are the fallback for a
        /// figure no formula produced. Absence is reported honestly as false rather than as
        /// zero, leaving the caller to decide -- <see cref="BasicAttackRules.ReadStat"/>
        /// treats it as zero so a fight can resolve, but nothing here forces that on
        /// everybody.
        /// </remarks>
        public bool TryGetCombatStat(DefinitionId stat, out int value)
        {
            if (_derived != null && _derived.TryGet(stat, out value))
            {
                return true;
            }

            return _character.Stats.TryGet(stat, out value);
        }

        /// <summary>Forwards to the character's resource state. No health is stored here.</summary>
        public void ApplyHealthDelta(long delta)
        {
            _character.Resources.ChangeHealth(delta, _limits);
        }

        // ---- ICombatantResourcePool -------------------------------------------------
        // A character has the two pools CharacterResourceState owns and no others.
        // Stamina and Rage are named by SkillResourceType but nothing implements them,
        // so they are reported absent rather than silently treated as empty.

        public bool HasResource(SkillResourceType resource)
        {
            return resource == SkillResourceType.Health || resource == SkillResourceType.Mana;
        }

        public bool TryGetResource(SkillResourceType resource, out int current, out int max)
        {
            switch (resource)
            {
                case SkillResourceType.Health:
                    current = _character.Resources.CurrentHealth;
                    max = _limits.MaxHealth;
                    return true;

                case SkillResourceType.Mana:
                    current = _character.Resources.CurrentMana;
                    max = _limits.MaxMana;
                    return true;

                default:
                    current = 0;
                    max = 0;
                    return false;
            }
        }

        public bool TryApplyResourceDelta(SkillResourceType resource, long delta)
        {
            switch (resource)
            {
                case SkillResourceType.Health:
                    _character.Resources.ChangeHealth(delta, _limits);
                    return true;

                case SkillResourceType.Mana:
                    _character.Resources.ChangeMana(delta, _limits);
                    return true;

                default:
                    return false;
            }
        }
    }
}
