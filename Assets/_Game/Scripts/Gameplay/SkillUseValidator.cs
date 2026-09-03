using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Everything a skill use needs to know that is not in the request.
    /// </summary>
    /// <remarks>
    /// <b>Passed in rather than reached for.</b> The learned skills, the character level
    /// and the cooldowns all belong to the caster, but <see cref="ICombatant"/> does not
    /// expose them and deliberately should not: a monster has no
    /// <see cref="CharacterSkillsState"/> and no character level, and widening the combat
    /// contract to suit players would make every other combatant answer questions that
    /// have no meaning for it. Supplying a context keeps the validator pure and leaves
    /// <see cref="ICombatant"/> alone.
    ///
    /// <see cref="Cooldowns"/> may be null, which means cooldowns are not being tracked and
    /// every skill is off cooldown. That is a legitimate configuration for a test or a
    /// server that tracks them elsewhere, not a missing dependency.
    /// </remarks>
    public readonly struct SkillUseContext
    {
        public SkillUseContext(IDefinitionRegistry<SkillDefinition> skills,
            CharacterSkillsState learnedSkills, int casterLevel,
            SkillCooldownState cooldowns = null)
        {
            Skills = skills;
            LearnedSkills = learnedSkills;
            CasterLevel = casterLevel;
            Cooldowns = cooldowns;
        }

        /// <summary>Where skill definitions are resolved from.</summary>
        public IDefinitionRegistry<SkillDefinition> Skills { get; }

        /// <summary>The caster's Phase 06 learned-skill state. Never copied, only read.</summary>
        public CharacterSkillsState LearnedSkills { get; }

        /// <summary>The caster's character level, read from their progression state by the caller.</summary>
        public int CasterLevel { get; }

        /// <summary>Runtime cooldowns, or null when they are not tracked.</summary>
        public SkillCooldownState Cooldowns { get; }
    }

    /// <summary>
    /// Decides whether a skill may be used right now.
    /// </summary>
    /// <remarks>
    /// <b>It duplicates no Phase 06 rule.</b> Whether a skill <em>may be learned</em> is
    /// <see cref="SkillLearningEvaluator"/>'s question and is not asked again here; this
    /// only reads the outcome of it, which is the entry in
    /// <see cref="CharacterSkillsState"/>. Whether a target is legal is
    /// <see cref="TargetEvaluator"/>'s question and is delegated to it rather than
    /// reimplemented.
    ///
    /// <b>Faults are reported in a fixed order</b> -- structure, definition, learned state,
    /// rank, level, target type, target, range, resource, cooldown -- and evaluation stops
    /// at the first. The order is what makes the result deterministic when several things
    /// are wrong at once, and it runs cheapest-first so a malformed request never reaches
    /// a registry lookup.
    ///
    /// <b>It changes nothing.</b> No cost is spent, no cooldown begun and no health
    /// touched; it answers a question. <see cref="SkillExecutor"/> is the only writer.
    /// </remarks>
    public static class SkillUseValidator
    {
        /// <summary>
        /// Converts an authored cost into whole units.
        /// </summary>
        /// <remarks>
        /// The schema authors cost as a float while every pool is an integer, so something
        /// must round. Rounding up is chosen so a skill never costs less than authored:
        /// a 2.5 cost charges 3. Stated here rather than buried, because it is a balance
        /// decision and a silent floor would make every fractional skill cheaper than
        /// intended.
        /// </remarks>
        public static int ToWholeCost(float cost)
        {
            if (float.IsNaN(cost) || cost <= 0f) return 0;
            if (float.IsPositiveInfinity(cost) || cost >= int.MaxValue) return int.MaxValue;
            return (int)Math.Ceiling(cost);
        }

        public static SkillUseEligibility Evaluate(in SkillUseRequest request, in SkillUseContext context)
        {
            ICombatant caster = request.Caster;

            if (caster == null)
            {
                return SkillUseEligibility.Rejected(SkillUseRejection.NoCaster);
            }

            if (!request.Skill.IsValid)
            {
                return SkillUseEligibility.Rejected(SkillUseRejection.NoSkill);
            }

            if (context.Skills == null
                || !context.Skills.TryGet(request.Skill, out SkillDefinition definition)
                || definition == null)
            {
                return SkillUseEligibility.Rejected(SkillUseRejection.UnknownSkill);
            }

            // --- learned state: Phase 06 owns this, we only read it ---------------
            if (context.LearnedSkills == null
                || !context.LearnedSkills.TryGetRank(request.Skill, out int learnedRank))
            {
                return SkillUseEligibility.Rejected(SkillUseRejection.NotLearned, definition);
            }

            int rank = request.Rank;

            if (rank < 1 || rank > learnedRank)
            {
                return SkillUseEligibility.Rejected(
                    SkillUseRejection.RankNotAvailable, definition, rank);
            }

            if (!definition.TryGetLevel(rank, out SkillLevelEntry level))
            {
                return SkillUseEligibility.Rejected(
                    SkillUseRejection.RankNotDefined, definition, rank);
            }

            if (context.CasterLevel < level.RequiredCharacterLevel)
            {
                return SkillUseEligibility.Rejected(
                    SkillUseRejection.LevelTooLow, definition, rank);
            }

            // --- target type -> combat relationship -------------------------------
            if (!SkillTargetMapping.TryGetPermittedRelationships(
                    definition.TargetType, out CombatRelationshipMask permitted))
            {
                return SkillUseEligibility.Rejected(
                    SkillUseRejection.TargetTypeUnsupported, definition, rank);
            }

            ICombatant target = SkillTargetMapping.ResolveTarget(
                definition.TargetType, caster, request.Target);

            if (target == null)
            {
                return SkillUseEligibility.Rejected(SkillUseRejection.NoTarget, definition, rank);
            }

            // --- target legality: delegated, never reimplemented -------------------
            TargetEligibility targeting = TargetEvaluator.Evaluate(caster, target, permitted);

            if (!targeting.IsAllowed)
            {
                return SkillUseEligibility.Rejected(
                    SkillUseEligibility.FromTarget(targeting.Reason),
                    definition, rank, targeting.Relationship);
            }

            // --- range ------------------------------------------------------------
            // A self skill is never out of range of itself, and a zero authored range
            // means "unspecified" rather than "touching only", so it is not enforced.
            if (definition.TargetType != SkillTargetType.Self && definition.Range > 0f)
            {
                CombatPosition from = caster.Position;
                CombatPosition to = target.Position;

                if (!from.IsFinite || !to.IsFinite)
                {
                    return SkillUseEligibility.Rejected(
                        SkillUseRejection.InvalidPosition, definition, rank, targeting.Relationship);
                }

                if (from.SqrDistanceTo(to) > definition.Range * definition.Range)
                {
                    return SkillUseEligibility.Rejected(
                        SkillUseRejection.OutOfRange, definition, rank, targeting.Relationship);
                }
            }

            // --- resource cost ------------------------------------------------------
            int cost = ToWholeCost(level.ResourceCost);

            if (cost > 0 && definition.ResourceType != SkillResourceType.None)
            {
                var pool = caster as ICombatantResourcePool;

                if (pool == null || !pool.TryGetResource(
                        definition.ResourceType, out int current, out _))
                {
                    return SkillUseEligibility.Rejected(
                        SkillUseRejection.ResourcePoolUnavailable, definition, rank,
                        targeting.Relationship);
                }

                if (current < cost)
                {
                    return SkillUseEligibility.Rejected(
                        SkillUseRejection.InsufficientResource, definition, rank,
                        targeting.Relationship);
                }
            }
            else
            {
                cost = 0;
            }

            // --- cooldown -----------------------------------------------------------
            if (context.Cooldowns != null && !context.Cooldowns.IsReady(request.Skill))
            {
                return SkillUseEligibility.Rejected(
                    SkillUseRejection.OnCooldown, definition, rank, targeting.Relationship);
            }

            return SkillUseEligibility.Allowed(
                definition, level, rank, target, targeting.Relationship, cost);
        }
    }
}
