using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that a level curve is internally consistent.
    /// </summary>
    /// <remarks>
    /// The first use of the <see cref="IDefinitionValidationRule"/> extension point added
    /// in 04.6, so a progression curve is checked by the same validator, in the same
    /// report, as every other piece of content rather than through a parallel mechanism.
    ///
    /// Rules enforced:
    /// <list type="bullet">
    /// <item>the minimum level is at least one;</item>
    /// <item>the maximum level is not below the minimum;</item>
    /// <item>the table holds exactly one cost per level transition;</item>
    /// <item>every cost is positive, so no level can be reached for free or by losing
    /// experience;</item>
    /// <item>the whole curve fits in a long, so a character cannot be asked for more
    /// experience than can be stored.</item>
    /// </list>
    ///
    /// Ascending order is intentionally not required. Per-level costs may fall as well as
    /// rise, and forbidding that would rule out a curve the game has not decided against.
    ///
    /// Reports only. A malformed curve is never silently corrected.
    /// </remarks>
    public sealed class CharacterProgressionValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            var progression = definition as CharacterProgressionDefinition;

            if (progression == null)
            {
                return;
            }

            DefinitionId id = progression.Id;

            if (progression.MinLevel < 1)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Minimum level must be at least one, found " + progression.MinLevel + ".");
            }

            if (progression.MaxLevel < progression.MinLevel)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Maximum level " + progression.MaxLevel + " is below minimum level "
                    + progression.MinLevel + ".");
                return;
            }

            int expectedTransitions = progression.MaxLevel - progression.MinLevel;

            if (progression.TransitionCount != expectedTransitions)
            {
                report.AddError(ValidationCode.InvalidConfiguration, id,
                    "Curve needs " + expectedTransitions + " level costs but has "
                    + progression.TransitionCount + ".");
                return;
            }

            long cumulative = 0;

            for (int level = progression.MinLevel; level < progression.MaxLevel; level++)
            {
                long required = progression.GetExperienceToNextLevel(level);

                if (required <= 0)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Level " + level + " costs " + required
                        + "; every level must cost more than nothing.");
                    continue;
                }

                if (cumulative > long.MaxValue - required)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Total experience across the curve exceeds the range of a long at level "
                        + level + ".");
                    return;
                }

                cumulative += required;
            }
        }
    }
}
