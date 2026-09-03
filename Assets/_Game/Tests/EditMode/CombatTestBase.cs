using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A combatant that exists only for tests.
    /// </summary>
    /// <remarks>
    /// Deliberately a second, trivial <see cref="ICombatant"/> implementation. Testing
    /// combat rules only through <see cref="CharacterCombatant"/> would prove the rules
    /// work for characters and leave the claim that a monster could use the same
    /// interface completely untested. This is the stand-in for that future monster, and it
    /// keeps its own health precisely because it is not a character.
    /// </remarks>
    internal sealed class FakeCombatant : ICombatant
    {
        private readonly System.Collections.Generic.Dictionary<DefinitionId, int> _stats
            = new System.Collections.Generic.Dictionary<DefinitionId, int>();

        private int _health;

        public FakeCombatant(string id, int team, int health, int maxHealth)
        {
            CombatantId = new InstanceId(id);
            Team = new CombatTeam(team);
            _health = health;
            MaxHealth = maxHealth;
        }

        public InstanceId CombatantId { get; }

        public CombatTeam Team { get; set; }

        public CombatPosition Position { get; set; }

        public int CurrentHealth => _health;

        public int MaxHealth { get; }

        /// <summary>How many times health was written. Proves rejected attacks touch nothing.</summary>
        public int ApplyCallCount { get; private set; }

        public FakeCombatant WithStat(string stat, int value)
        {
            _stats[new DefinitionId(stat)] = value;
            return this;
        }

        public bool TryGetCombatStat(DefinitionId stat, out int value)
        {
            return _stats.TryGetValue(stat, out value);
        }

        public void ApplyHealthDelta(long delta)
        {
            ApplyCallCount++;

            long next = _health + delta;

            if (next < 0) next = 0;
            if (next > MaxHealth) next = MaxHealth;

            _health = (int)next;
        }
    }

    /// <summary>Shared ids and helpers for the combat tests.</summary>
    internal static class CombatTestIds
    {
        public const string AttackPower = "stat.combat.attack_power";
        public const string Defense = "stat.combat.defense";

        public static DefinitionId AttackPowerStat => new DefinitionId(AttackPower);

        public static DefinitionId DefenseStat => new DefinitionId(Defense);

        /// <summary>Melee rules with a generous reach, so range never accidentally decides a test.</summary>
        public static BasicAttackRules MeleeRules(int minimumDamage = 1, float range = 100f)
        {
            return BasicAttackRules.Melee(AttackPowerStat, DefenseStat, minimumDamage, range);
        }
    }
}
