namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a target was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, for the same purpose
    /// <see cref="SkillLearnRejection"/> and <see cref="JobChangeRejection"/> serve: each
    /// value needs a different message to a player and a different response from a server.
    /// A client that only knows "invalid" cannot grey out the right thing.
    ///
    /// Named to sit alongside the existing rejection vocabulary so a reader who knows one
    /// enum can read this one.
    /// </remarks>
    public enum TargetRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No attacker was supplied.</summary>
        NoAttacker = 1,

        /// <summary>No target was supplied.</summary>
        NoTarget = 2,

        /// <summary>The attacker is not alive.</summary>
        AttackerDead = 3,

        /// <summary>The target is not alive.</summary>
        TargetDead = 4,

        /// <summary>At least one side has no team, so no relationship exists.</summary>
        UndefinedRelationship = 5,

        /// <summary>The relationship is real but this action does not accept it.</summary>
        RelationshipNotPermitted = 6
    }

    /// <summary>
    /// The answer to whether one combatant may be aimed at by another.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="SkillLearnEligibility"/>. It carries the resolved
    /// <see cref="Relationship"/> whether or not that was the thing that failed, so a
    /// caller can explain "that is an ally" rather than only "no".
    ///
    /// This is not target <em>selection</em>. Nothing here searches, sorts, cycles or
    /// picks; it answers a question about a pair that somebody else already chose.
    /// </remarks>
    public readonly struct TargetEligibility
    {
        private TargetEligibility(bool allowed, TargetRejection reason, CombatRelationship relationship)
        {
            IsAllowed = allowed;
            Reason = reason;
            Relationship = relationship;
        }

        public bool IsAllowed { get; }

        /// <summary><see cref="TargetRejection.None"/> when allowed.</summary>
        public TargetRejection Reason { get; }

        /// <summary>How the two stand to each other, as far as it could be resolved.</summary>
        public CombatRelationship Relationship { get; }

        public static TargetEligibility Allowed(CombatRelationship relationship)
        {
            return new TargetEligibility(true, TargetRejection.None, relationship);
        }

        public static TargetEligibility Rejected(TargetRejection reason, CombatRelationship relationship)
        {
            return new TargetEligibility(false, reason, relationship);
        }

        public override string ToString()
        {
            return IsAllowed
                ? "allowed (" + Relationship + ")"
                : Reason + " (" + Relationship + ")";
        }
    }

    /// <summary>Decides whether a chosen target is a legal one.</summary>
    public static class TargetEvaluator
    {
        /// <summary>
        /// Evaluates a target against the relationships an action accepts.
        /// </summary>
        /// <remarks>
        /// Faults are reported in a fixed order -- missing, dead, then relationship --
        /// and evaluation stops at the first, matching how
        /// <see cref="SkillLearningEvaluator"/> reports the first blocking requirement
        /// rather than a list. The order is what makes the result deterministic when more
        /// than one thing is wrong.
        ///
        /// Aliveness is checked before the relationship deliberately: "that target is
        /// already dead" is more useful to a player than "that target is an ally", and a
        /// corpse's team is rarely what they were trying to find out.
        /// </remarks>
        public static TargetEligibility Evaluate(ICombatant attacker, ICombatant target,
            CombatRelationshipMask permitted)
        {
            if (attacker == null)
            {
                return TargetEligibility.Rejected(TargetRejection.NoAttacker, CombatRelationship.None);
            }

            if (target == null)
            {
                return TargetEligibility.Rejected(TargetRejection.NoTarget, CombatRelationship.None);
            }

            CombatRelationship relationship = CombatTeams.Relate(attacker, target);

            if (!attacker.IsAlive())
            {
                return TargetEligibility.Rejected(TargetRejection.AttackerDead, relationship);
            }

            if (!target.IsAlive())
            {
                return TargetEligibility.Rejected(TargetRejection.TargetDead, relationship);
            }

            if (relationship == CombatRelationship.None)
            {
                return TargetEligibility.Rejected(
                    TargetRejection.UndefinedRelationship, relationship);
            }

            if (!CombatTeams.Permits(permitted, relationship))
            {
                return TargetEligibility.Rejected(
                    TargetRejection.RelationshipNotPermitted, relationship);
            }

            return TargetEligibility.Allowed(relationship);
        }
    }
}
