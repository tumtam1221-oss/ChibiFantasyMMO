using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The tunable part of a basic attack.
    /// </summary>
    /// <remarks>
    /// <b>Everything a designer or a server could want to change lives here, and nothing
    /// else does.</b> <see cref="BasicAttackExecutor"/> holds no numbers at all, so
    /// rebalancing reach, the damage floor or which stats matter is data, not a code edit.
    ///
    /// <b>Stats are named by id.</b> <see cref="AttackPowerStat"/> and
    /// <see cref="DefenseStat"/> are <see cref="DefinitionId"/> values looked up through
    /// <see cref="ICombatant.TryGetCombatStat"/>, exactly as
    /// <see cref="ResourceLimits.From"/> takes the ids of maximum health and mana instead
    /// of knowing their names. No STR, ATK or DEF constant appears anywhere in combat, so
    /// this phase adds no stat and overhauls none.
    ///
    /// <b>A missing stat reads as zero here.</b> The stat layer distinguishes absent from
    /// zero and this deliberately collapses that distinction, because a fight must resolve:
    /// a combatant with no authored defence should take full damage, not throw. Absence is
    /// still visible to anyone who asks the combatant directly.
    ///
    /// <see cref="Range"/> is stored squared so the executor never takes a square root.
    /// </remarks>
    public readonly struct BasicAttackRules
    {
        private BasicAttackRules(DefinitionId attackPowerStat, DefinitionId defenseStat,
            int minimumDamage, float range, CombatRelationshipMask permittedTargets)
        {
            AttackPowerStat = attackPowerStat;
            DefenseStat = defenseStat;
            MinimumDamage = minimumDamage < 0 ? 0 : minimumDamage;
            Range = range < 0f ? 0f : range;
            PermittedTargets = permittedTargets;
        }

        /// <summary>Id of the stat read from the attacker.</summary>
        public DefinitionId AttackPowerStat { get; }

        /// <summary>Id of the stat read from the target.</summary>
        public DefinitionId DefenseStat { get; }

        /// <summary>Floor applied after subtraction. Never negative.</summary>
        public int MinimumDamage { get; }

        /// <summary>Maximum reach in world units. Never negative.</summary>
        public float Range { get; }

        /// <summary>Squared reach, for comparison without a square root.</summary>
        public float RangeSquared => Range * Range;

        /// <summary>Which relationships this attack accepts.</summary>
        public CombatRelationshipMask PermittedTargets { get; }

        /// <summary>
        /// Rules for an ordinary hostile-only melee swing.
        /// </summary>
        /// <remarks>Hostile only, because letting a basic attack strike allies is a PK and
        /// friendly-fire decision that belongs to a later phase, and defaulting it open
        /// would make that decision by omission.</remarks>
        public static BasicAttackRules Melee(DefinitionId attackPowerStat, DefinitionId defenseStat,
            int minimumDamage, float range)
        {
            return new BasicAttackRules(attackPowerStat, defenseStat, minimumDamage, range,
                CombatRelationshipMask.Hostile);
        }

        /// <summary>Rules with an explicit relationship mask.</summary>
        public static BasicAttackRules Create(DefinitionId attackPowerStat, DefinitionId defenseStat,
            int minimumDamage, float range, CombatRelationshipMask permittedTargets)
        {
            return new BasicAttackRules(attackPowerStat, defenseStat, minimumDamage, range,
                permittedTargets);
        }

        /// <summary>Reads a stat, treating absence as zero. See the type remarks for why.</summary>
        public static int ReadStat(ICombatant combatant, DefinitionId stat)
        {
            if (combatant == null)
            {
                return 0;
            }

            return combatant.TryGetCombatStat(stat, out int value) ? value : 0;
        }
    }
}
