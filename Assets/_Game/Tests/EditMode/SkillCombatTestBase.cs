using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A combatant with resource pools, for skill tests.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FakeCombatant"/> on purpose. That one deliberately has no
    /// pools, which is what lets the tests prove a skill costing mana against a poolless
    /// combatant is rejected by name rather than silently treated as free. Keeping both
    /// means both branches are exercised.
    ///
    /// Health here still routes through the base combatant, so there is exactly one health
    /// rule in the fixture too.
    /// </remarks>
    internal sealed class FakePooledCombatant : ICombatant, ICombatantResourcePool
    {
        private readonly System.Collections.Generic.Dictionary<DefinitionId, int> _stats
            = new System.Collections.Generic.Dictionary<DefinitionId, int>();

        private int _health;
        private int _mana;

        public FakePooledCombatant(string id, int team, int health, int maxHealth,
            int mana = 0, int maxMana = 0)
        {
            CombatantId = new InstanceId(id);
            Team = new CombatTeam(team);
            _health = health;
            MaxHealth = maxHealth;
            _mana = mana;
            MaxMana = maxMana;
        }

        public InstanceId CombatantId { get; }

        public CombatTeam Team { get; set; }

        public CombatPosition Position { get; set; }

        public int CurrentHealth => _health;

        public int MaxHealth { get; }

        public int CurrentMana => _mana;

        public int MaxMana { get; }

        /// <summary>How many times any pool was written. Proves rejected uses touch nothing.</summary>
        public int WriteCount { get; private set; }

        public FakePooledCombatant WithStat(string stat, int value)
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
            WriteCount++;
            _health = Clamp(_health + delta, MaxHealth);
        }

        public bool HasResource(SkillResourceType resource)
        {
            return resource == SkillResourceType.Health || resource == SkillResourceType.Mana;
        }

        public bool TryGetResource(SkillResourceType resource, out int current, out int max)
        {
            switch (resource)
            {
                case SkillResourceType.Health:
                    current = _health;
                    max = MaxHealth;
                    return true;

                case SkillResourceType.Mana:
                    current = _mana;
                    max = MaxMana;
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
                    ApplyHealthDelta(delta);
                    return true;

                case SkillResourceType.Mana:
                    WriteCount++;
                    _mana = Clamp(_mana + delta, MaxMana);
                    return true;

                default:
                    return false;
            }
        }

        private static int Clamp(long value, int max)
        {
            if (value <= 0) return 0;
            return value >= max ? max : (int)value;
        }
    }

    /// <summary>Shared ids for the skill-combat tests.</summary>
    internal static class SkillCombatIds
    {
        public const string Power = "stat.skill.power";
        public const string Defense = "stat.skill.defense";

        public static DefinitionId PowerStat => new DefinitionId(Power);

        public static DefinitionId DefenseStat => new DefinitionId(Defense);

        /// <summary>Rules with a defending stat and no floor.</summary>
        public static SkillExecutionRules Rules(int minimumDamage = 0)
        {
            return new SkillExecutionRules(DefenseStat, minimumDamage);
        }
    }
}
