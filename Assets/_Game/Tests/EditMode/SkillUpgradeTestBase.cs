using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared setup for skill rank-progression tests.
    /// </summary>
    /// <remarks>
    /// Builds on the 06.6 learning fixtures rather than restating them, so both rule layers
    /// are exercised against the same content shapes. Every skill here is a TEST FIXTURE
    /// with a deliberately generic name.
    /// </remarks>
    internal abstract class SkillUpgradeTestBase : SkillLearningTestBase
    {
        /// <summary>
        /// A skill with a full level table, one entry per rank.
        /// </summary>
        /// <param name="requiredLevels">Character level demanded by each rank, rank one
        /// first. Its length is the skill's maximum rank, so the table and the maximum
        /// always agree.</param>
        protected SkillDefinition AddRankedSkill(string id, int[] requiredLevels,
            string requiredClass = null, string requiredJob = null,
            SkillPrerequisite[] prerequisites = null)
        {
            SkillDefinition definition = AddSkill(id, requiredClass: requiredClass,
                requiredJob: requiredJob, prerequisites: prerequisites,
                maxLevel: requiredLevels.Length);

            var levels = new SkillLevelEntry[requiredLevels.Length];

            for (int i = 0; i < requiredLevels.Length; i++)
            {
                levels[i] = new SkillLevelEntry(i + 1, requiredLevels[i], 0f, 0f,
                    new SkillEffect[0]);
            }

            SetPrivate(definition, "_levels", levels);
            return definition;
        }

        /// <summary>A character already holding a skill at a given rank.</summary>
        protected static CharacterSkillsState Holding(string skill, int rank)
        {
            CharacterSkillsState learned = NewSkills();
            learned.SetRank(new DefinitionId(skill), rank);
            return learned;
        }

        protected SkillUpgradeEligibility EvaluateUpgrade(CharacterSkillsState learned,
            CharacterClassState classState, int level, string target)
        {
            return new SkillUpgradeEvaluator()
                .Evaluate(learned, classState, level, new DefinitionId(target), Skills);
        }

        protected bool TryUpgrade(CharacterSkillsState learned, CharacterClassState classState,
            int level, string target, out SkillUpgradeEligibility eligibility)
        {
            return new SkillUpgradeEvaluator()
                .TryUpgrade(learned, classState, level, new DefinitionId(target), Skills,
                    out eligibility);
        }
    }
}
