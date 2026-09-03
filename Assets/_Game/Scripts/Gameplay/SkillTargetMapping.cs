using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Translates an authored <see cref="SkillTargetType"/> into the combat relationships
    /// that satisfy it.
    /// </summary>
    /// <remarks>
    /// <b>This is the seam between the two phases.</b> Phase 06 authors what a skill may be
    /// aimed at; Phase 07 decides what two combatants are to each other. Neither knows the
    /// other's vocabulary, and this is the single place they meet. No second relationship
    /// system is introduced -- the output is the existing
    /// <see cref="CombatRelationshipMask"/>, consumed by the existing
    /// <see cref="TargetEvaluator"/>.
    ///
    /// <b>Unsupported is a real answer.</b> Area and party targeting have no runtime
    /// implementation, so they are reported as unsupported rather than quietly folded into
    /// "hostile" or "friendly". Guessing would let an area skill silently become a single
    /// target one and look like it worked, which is the worst possible failure: a wrong
    /// answer that never complains. Adding one later is a case here plus the runtime that
    /// backs it.
    ///
    /// <b>Self is not friendly.</b> A self-only skill maps to
    /// <see cref="CombatRelationshipMask.Self"/> alone, and an ally skill excludes the
    /// caster, because <see cref="CombatTeams.Relate"/> already distinguishes the two and
    /// collapsing them would let a single-ally heal target the caster for free.
    /// </remarks>
    public static class SkillTargetMapping
    {
        /// <summary>
        /// Maps a target type to the relationships that satisfy it.
        /// </summary>
        /// <returns>False when the target type has no runtime meaning yet; the mask is
        /// then <see cref="CombatRelationshipMask.None"/>.</returns>
        public static bool TryGetPermittedRelationships(SkillTargetType targetType,
            out CombatRelationshipMask mask)
        {
            switch (targetType)
            {
                case SkillTargetType.Self:
                    mask = CombatRelationshipMask.Self;
                    return true;

                case SkillTargetType.SingleAlly:
                    // Deliberately not Self: an ally is somebody else.
                    mask = CombatRelationshipMask.Friendly;
                    return true;

                case SkillTargetType.SingleEnemy:
                    mask = CombatRelationshipMask.Hostile;
                    return true;

                // No area or party runtime exists. Reported, never assumed.
                case SkillTargetType.AreaAroundSelf:
                case SkillTargetType.AreaAtPoint:
                case SkillTargetType.Party:
                case SkillTargetType.None:
                default:
                    mask = CombatRelationshipMask.None;
                    return false;
            }
        }

        /// <summary>
        /// Whether a target type is aimed at somebody other than the caster.
        /// </summary>
        /// <remarks>Used to decide whether a missing target is a fault. A self skill with
        /// no target supplied is complete, because the caster is the target.</remarks>
        public static bool RequiresExplicitTarget(SkillTargetType targetType)
        {
            return targetType == SkillTargetType.SingleAlly
                || targetType == SkillTargetType.SingleEnemy;
        }

        /// <summary>
        /// Resolves which combatant an effect actually lands on.
        /// </summary>
        /// <remarks>For a self skill this is the caster even when a target was supplied,
        /// so a caller cannot redirect a self-only skill by passing somebody else.</remarks>
        public static ICombatant ResolveTarget(SkillTargetType targetType,
            ICombatant caster, ICombatant requestedTarget)
        {
            return targetType == SkillTargetType.Self ? caster : requestedTarget;
        }
    }
}
