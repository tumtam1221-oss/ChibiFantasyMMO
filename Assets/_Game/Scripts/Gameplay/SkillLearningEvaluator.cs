using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides whether a character may learn a skill, and is the only thing that teaches one.
    /// </summary>
    /// <remarks>
    /// <b>The sibling of <see cref="JobChangeEvaluator"/>, deliberately.</b> Same shape,
    /// same conventions: a pure <see cref="Evaluate"/> that reads and changes nothing, and
    /// a <see cref="TryLearn"/> that is the only mutation path and evaluates first, so a
    /// skill cannot be learned without the rules having run. A server remains free to call
    /// it and refuse anything a client claims.
    ///
    /// <b>Requirements are data.</b> No skill, class or job is named here and no switch
    /// enumerates content. The rules read whatever
    /// <see cref="SkillDefinition.RequiredClass"/>, <see cref="SkillDefinition.RequiredJob"/>,
    /// <see cref="SkillDefinition.Prerequisites"/> and the skill's level table contain, so a
    /// designer changes who may learn what by authoring assets.
    ///
    /// <b>Nothing is duplicated.</b> Level arrives as a parameter from
    /// <see cref="CharacterProgressionState"/>, class and job are read from
    /// <see cref="CharacterClassState"/>, and what is already known is read from
    /// <see cref="CharacterSkillsState"/>. This type owns no state of its own and holds no
    /// second copy of a level, a class, a job or a learned skill.
    ///
    /// <b>First fault, not all faults.</b> The project already draws this line: content
    /// validation accumulates every finding into a <see cref="ValidationReport"/> because a
    /// designer fixing a broken asset wants the whole list, while a domain rule answers one
    /// question about one attempt and returns the first reason it failed, as job change
    /// does. Those are different concepts and are kept apart; this is a domain rule.
    ///
    /// <b>The level gate lives on the rank.</b> A skill's character-level requirement is
    /// authored on <see cref="SkillLevelEntry.RequiredCharacterLevel"/>, not on the skill,
    /// so the gate for learning is the rank-one entry's. A skill with no level table has no
    /// level gate; that is what the schema says, and inventing a default here would be a
    /// rule no designer wrote.
    ///
    /// <b>Prerequisites are checked one level deep, and that is correct.</b> The question
    /// is whether the character knows the prerequisite now, which
    /// <see cref="CharacterSkillsState"/> answers directly; how they came to know it is
    /// already settled history. Nothing recurses into a prerequisite's own prerequisites,
    /// so a cycle in authored content -- A requiring B requiring A -- cannot recurse
    /// indefinitely here. It simply leaves both skills unlearnable, which is a content
    /// fault to be reported by content validation, not a runtime hazard.
    ///
    /// <b>Not implemented because nothing represents it.</b> No skill point, skill cost or
    /// learning currency exists anywhere in the project, so no such requirement is checked
    /// and none was invented; adding a parallel resource system here would commit the game
    /// to a design nobody has made. Class and job availability, and prerequisites, are
    /// checked because the schema states them.
    /// </remarks>
    public sealed class SkillLearningEvaluator
    {
        /// <summary>
        /// Answers whether a skill may be learned, without changing anything.
        /// </summary>
        /// <param name="learned">What the character already knows.</param>
        /// <param name="classState">The character's class and current job.</param>
        /// <param name="characterLevel">Level from the character's progression state.</param>
        /// <param name="targetSkill">The skill being sought.</param>
        /// <param name="skills">Skill content.</param>
        public SkillLearnEligibility Evaluate(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetSkill,
            IDefinitionRegistry<SkillDefinition> skills)
        {
            if (learned == null)
            {
                throw new ArgumentNullException(nameof(learned));
            }

            if (classState == null)
            {
                throw new ArgumentNullException(nameof(classState));
            }

            if (skills == null)
            {
                throw new ArgumentNullException(nameof(skills));
            }

            if (!targetSkill.IsValid || !skills.TryGet(targetSkill, out SkillDefinition target))
            {
                return SkillLearnEligibility.Rejected(SkillLearnRejection.UnknownSkill, 0);
            }

            int required = RequiredLevelFor(target);

            if (learned.Knows(targetSkill))
            {
                return SkillLearnEligibility.Rejected(SkillLearnRejection.AlreadyLearned, required);
            }

            if (target.RequiredClass.IsValid && target.RequiredClass != classState.BaseClass)
            {
                return SkillLearnEligibility.Rejected(
                    SkillLearnRejection.ClassRequirementNotMet, required);
            }

            if (target.RequiredJob.IsValid && target.RequiredJob != classState.CurrentJob)
            {
                return SkillLearnEligibility.Rejected(
                    SkillLearnRejection.JobRequirementNotMet, required);
            }

            SkillLearnEligibility prerequisiteFault = CheckPrerequisites(target, learned, required);

            if (!prerequisiteFault.IsAllowed)
            {
                return prerequisiteFault;
            }

            if (characterLevel < required)
            {
                return SkillLearnEligibility.Rejected(SkillLearnRejection.LevelTooLow, required);
            }

            return SkillLearnEligibility.Allowed(required);
        }

        /// <summary>
        /// Learns a skill if it is permitted, and reports what was decided.
        /// </summary>
        /// <remarks>
        /// The only route to <see cref="CharacterSkillsState.Learn"/> that runs the rules.
        /// A refusal leaves the learned skills and their revision untouched, and an
        /// unresolvable id is refused before any state is reached, so no phantom skill can
        /// be recorded.
        ///
        /// Evaluation completes before anything is written, so there is no partial
        /// mutation to undo. A success adds exactly one skill at rank one and advances the
        /// revision exactly once; because <see cref="SkillLearnRejection.AlreadyLearned"/>
        /// is refused earlier, a repeat attempt never reaches the state and never
        /// duplicates.
        /// </remarks>
        /// <returns>True when the skill was learned.</returns>
        public bool TryLearn(
            CharacterSkillsState learned,
            CharacterClassState classState,
            int characterLevel,
            DefinitionId targetSkill,
            IDefinitionRegistry<SkillDefinition> skills,
            out SkillLearnEligibility eligibility)
        {
            eligibility = Evaluate(learned, classState, characterLevel, targetSkill, skills);

            if (!eligibility.IsAllowed)
            {
                return false;
            }

            return learned.Learn(targetSkill);
        }

        /// <summary>
        /// The character level needed to hold the skill at rank one.
        /// </summary>
        /// <remarks>Zero when the skill authors no level table, which the schema treats as
        /// a single-rank skill described by its own fields, none of which is a level
        /// gate.</remarks>
        private static int RequiredLevelFor(SkillDefinition skill)
        {
            return skill.TryGetLevel(1, out SkillLevelEntry entry)
                ? entry.RequiredCharacterLevel
                : 0;
        }

        /// <summary>
        /// Checks every skill that must be known first, in authored order.
        /// </summary>
        /// <remarks>A prerequisite naming no skill is a content fault reported by skill
        /// validation, not a reason to refuse a player here, so it is skipped rather than
        /// treated as unmet.</remarks>
        private static SkillLearnEligibility CheckPrerequisites(SkillDefinition target,
            CharacterSkillsState learned, int requiredLevel)
        {
            SkillPrerequisite[] prerequisites = target.Prerequisites;

            if (prerequisites == null)
            {
                return SkillLearnEligibility.Allowed(requiredLevel);
            }

            for (int i = 0; i < prerequisites.Length; i++)
            {
                SkillPrerequisite prerequisite = prerequisites[i];

                if (!prerequisite.Skill.IsValid)
                {
                    continue;
                }

                if (!learned.TryGetRank(prerequisite.Skill, out int rank))
                {
                    return SkillLearnEligibility.RejectedByPrerequisite(
                        SkillLearnRejection.PrerequisiteNotLearned, requiredLevel,
                        prerequisite.Skill, prerequisite.Level);
                }

                if (rank < prerequisite.Level)
                {
                    return SkillLearnEligibility.RejectedByPrerequisite(
                        SkillLearnRejection.PrerequisiteRankTooLow, requiredLevel,
                        prerequisite.Skill, prerequisite.Level);
                }
            }

            return SkillLearnEligibility.Allowed(requiredLevel);
        }
    }
}
