using System;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Which side of a fight a combatant is on.
    /// </summary>
    /// <remarks>
    /// An opaque integer rather than an enum of Players/Monsters, because factions are
    /// content: an enum would have to be edited and recompiled to add a rival guild, a
    /// neutral wildlife group or a PK free-for-all side. Nothing here knows what any
    /// particular team means.
    ///
    /// <see cref="None"/> is not a team that fights badly, it is the absence of one. A
    /// combatant without a team has no defined relationship to anybody, and
    /// <see cref="CombatTeams.Relate"/> answers <see cref="CombatRelationship.None"/> for
    /// it rather than guessing hostile or friendly. Guessing either way would be a
    /// balance decision made by accident.
    /// </remarks>
    public readonly struct CombatTeam : IEquatable<CombatTeam>
    {
        private readonly int _value;

        public CombatTeam(int value)
        {
            _value = value;
        }

        public int Value => _value;

        /// <summary>False only for <see cref="None"/>.</summary>
        public bool IsValid => _value != 0;

        /// <summary>No team. A combatant in this state relates to nobody.</summary>
        public static CombatTeam None => default;

        public bool Equals(CombatTeam other) => _value == other._value;

        public override bool Equals(object obj) => obj is CombatTeam other && Equals(other);

        public override int GetHashCode() => _value;

        public override string ToString() => _value.ToString();

        public static bool operator ==(CombatTeam left, CombatTeam right) => left.Equals(right);

        public static bool operator !=(CombatTeam left, CombatTeam right) => !left.Equals(right);
    }

    /// <summary>How two combatants stand to one another.</summary>
    /// <remarks>
    /// Deliberately not the same thing as <c>SkillTargetType</c> in the Data assembly.
    /// That enum is authoring: it records what a skill <em>may be aimed at</em> when a
    /// designer writes the skill. This one is runtime: it answers what two specific
    /// combatants actually are to each other at the moment of an attack. Skill code will
    /// later map the former onto the latter; neither replaces the other.
    /// </remarks>
    public enum CombatRelationship
    {
        /// <summary>Undetermined, because at least one side has no team.</summary>
        None = 0,

        /// <summary>The same combatant on both sides.</summary>
        Self = 1,

        /// <summary>Different combatants sharing a team.</summary>
        Friendly = 2,

        /// <summary>Different combatants on different teams.</summary>
        Hostile = 3
    }

    /// <summary>
    /// Which relationships a particular action accepts.
    /// </summary>
    /// <remarks>
    /// Flags rather than a single relationship, because "who may this be aimed at" is
    /// genuinely a set: a basic attack takes hostiles only, a heal takes self and
    /// friendlies, a resurrection takes friendlies only. Passing the set in keeps the
    /// rule with the action instead of hard-coding one policy into the evaluator.
    /// </remarks>
    [Flags]
    public enum CombatRelationshipMask
    {
        None = 0,
        Self = 1,
        Friendly = 2,
        Hostile = 4
    }

    /// <summary>Relationship rules. The single place the question is answered.</summary>
    public static class CombatTeams
    {
        /// <summary>
        /// Works out how <paramref name="target"/> stands to <paramref name="attacker"/>.
        /// </summary>
        /// <remarks>
        /// Identity is checked before teams, so a combatant targeting itself is
        /// <see cref="CombatRelationship.Self"/> and never merely "friendly", even though
        /// it does share its own team. Callers that allow one and not the other depend on
        /// that distinction.
        /// </remarks>
        public static CombatRelationship Relate(ICombatant attacker, ICombatant target)
        {
            if (attacker == null || target == null)
            {
                return CombatRelationship.None;
            }

            if (attacker.CombatantId.IsValid && attacker.CombatantId == target.CombatantId)
            {
                return CombatRelationship.Self;
            }

            if (!attacker.Team.IsValid || !target.Team.IsValid)
            {
                return CombatRelationship.None;
            }

            return attacker.Team == target.Team
                ? CombatRelationship.Friendly
                : CombatRelationship.Hostile;
        }

        /// <summary>Whether a mask accepts a relationship. <see cref="CombatRelationship.None"/> is never accepted.</summary>
        public static bool Permits(CombatRelationshipMask mask, CombatRelationship relationship)
        {
            switch (relationship)
            {
                case CombatRelationship.Self:
                    return (mask & CombatRelationshipMask.Self) != 0;
                case CombatRelationship.Friendly:
                    return (mask & CombatRelationshipMask.Friendly) != 0;
                case CombatRelationship.Hostile:
                    return (mask & CombatRelationshipMask.Hostile) != 0;
                default:
                    return false;
            }
        }
    }
}
