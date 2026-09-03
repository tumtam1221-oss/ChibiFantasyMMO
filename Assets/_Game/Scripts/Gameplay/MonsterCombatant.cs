using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Lets a monster take part in a fight.
    /// </summary>
    /// <remarks>
    /// <b>An adapter, exactly like <see cref="CharacterCombatant"/>.</b> It stores no
    /// health, no maximum and no stat; every read forwards to the runtime state or the
    /// authored definition that already owns the answer. Combat gains no privileged path
    /// into monster state, and no combat formula had to change to accept a monster.
    ///
    /// <b>Stats are authored, not derived.</b> A character's combat figures come out of
    /// <see cref="DerivedStatsCalculator"/> because they are built from level, class,
    /// equipment and progression. A monster has none of that: its attack and defence are
    /// numbers a designer typed on the definition. So the lookup reads
    /// <c>MonsterDefinition.TryGetStat</c> and a monster needs no stat pipeline of its own
    /// -- which is also why an unauthored stat is absent rather than zero, and the caller
    /// decides what absence means.
    ///
    /// Team is supplied rather than read from the definition, for the reason the character
    /// adapter gives: faction is a property of a fight, not of a thing.
    /// </remarks>
    public sealed class MonsterCombatant : ICombatant
    {
        private readonly MonsterRuntimeState _monster;

        public MonsterCombatant(MonsterRuntimeState monster)
        {
            _monster = monster ?? throw new ArgumentNullException(nameof(monster));
        }

        /// <summary>The adapted monster. Exposed so callers need not hold it twice.</summary>
        public MonsterRuntimeState Monster => _monster;

        /// <summary>
        /// Runtime identity.
        /// </summary>
        /// <remarks>The monster's own instance id, not a fresh one: minting a second
        /// identity for the same thing would break every comparison combat makes, including
        /// self-targeting.</remarks>
        public InstanceId CombatantId => _monster.InstanceId;

        public CombatTeam Team => _monster.Team;

        public CombatPosition Position => _monster.Position;

        public int CurrentHealth => _monster.CurrentHealth;

        public int MaxHealth => _monster.MaxHealth;

        public bool TryGetCombatStat(DefinitionId stat, out int value)
        {
            return _monster.Definition.TryGetStat(stat, out value);
        }

        public void ApplyHealthDelta(long delta)
        {
            _monster.ApplyHealthDelta(delta);
        }

        public override string ToString()
        {
            return "monster " + _monster;
        }
    }
}
