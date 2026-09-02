using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Combinations of skill settings that contradict one another.
    /// </summary>
    /// <remarks>
    /// Each field on a skill can be individually valid while the set of them describes
    /// something that cannot exist. An active skill with no target type says cast this at
    /// nothing; a cost with no resource type says spend from no pool. Those are authoring
    /// mistakes that structure checks alone would pass.
    ///
    /// Where a combination is impossible it is an error. Where it is merely suspect -- a
    /// passive skill given a cast time it will never use -- it is a warning, because the
    /// content is usable and a designer may have a reason. This is the first production use
    /// of the severity split that has existed since 04.6.
    ///
    /// Everything here is provable from the definition alone. Nothing asks what happens at
    /// runtime.
    /// </remarks>
    public sealed partial class SkillValidationRule
    {
        private static void ValidateCombinations(SkillDefinition skill, DefinitionId id,
            ValidationReport report)
        {
            if (skill.Category == SkillCategory.None)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "The skill has no category, so nothing can tell how it is meant to be used.");
            }

            bool isCast = skill.Category == SkillCategory.Active
                || skill.Category == SkillCategory.Toggle;

            if (isCast && skill.TargetType == SkillTargetType.None)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "A " + skill.Category + " skill has no target type, so there is nothing to cast it at.");
            }

            if (skill.Category == SkillCategory.Passive)
            {
                ValidatePassive(skill, id, report);
            }

            ValidateCostAgainstResource(skill, id, report);
        }

        private static void ValidatePassive(SkillDefinition skill, DefinitionId id,
            ValidationReport report)
        {
            if (skill.TargetType != SkillTargetType.None)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, id,
                    "A passive skill names target type " + skill.TargetType
                    + ", which nothing will read.");
            }

            if (skill.CastTimeSeconds > 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, id,
                    "A passive skill has a cast time, which it will never spend.");
            }
        }

        /// <summary>A cost has to come from somewhere.</summary>
        private static void ValidateCostAgainstResource(SkillDefinition skill, DefinitionId id,
            ValidationReport report)
        {
            if (skill.ResourceType != SkillResourceType.None)
            {
                return;
            }

            if (skill.BaseResourceCost > 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "The skill costs " + skill.BaseResourceCost + " but names no resource type.");
                return;
            }

            SkillLevelEntry[] levels = skill.Levels;

            if (levels == null)
            {
                return;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].ResourceCost > 0)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Level " + levels[i].Level + " costs " + levels[i].ResourceCost
                        + " but the skill names no resource type.");
                    return;
                }
            }
        }
    }
}
