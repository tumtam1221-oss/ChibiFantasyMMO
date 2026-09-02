using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The progression and class/job half of the consistency check.
    /// </summary>
    /// <remarks>
    /// Split out because these two answer a different question from the rest: not "is this
    /// value in range" but "could this character legitimately be in this position".
    ///
    /// The rules mirror 05.7 rather than restating them loosely. A job must exist, belong
    /// to the character's own class tree, and demand no more level than the character has.
    /// Together those rule out the impossible states that matter: a job from another class,
    /// a job reached below its requirement, and a job that no longer ships.
    ///
    /// No job change happens here and no eligibility path is consulted, because this asks
    /// whether a character is currently coherent, not whether they may advance.
    /// </remarks>
    public sealed partial class CharacterConsistencyValidator
    {
        private static void ValidateProgression(Character character, CharacterCreationContent content,
            ValidationReport report)
        {
            CharacterProgressionState progression = character.Progression;

            if (!content.Progression.IsLevelInRange(progression.Level))
            {
                report.AddError(ValidationCode.ValueOutOfRange, content.Progression.Id,
                    "Level " + progression.Level + " is outside the curve's range of "
                    + content.Progression.MinLevel + " to " + content.Progression.MaxLevel + ".");
            }

            if (progression.Experience < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, content.Progression.Id,
                    "Experience is negative.");
            }
        }

        private static void ValidateClassAndJob(Character character, CharacterCreationContent content,
            IDefinitionRegistry<JobDefinition> jobs, ValidationReport report)
        {
            CharacterClassState classState = character.Class;

            if (!content.Classes.TryGet(classState.BaseClass, out ClassDefinition baseClass))
            {
                report.AddError(ValidationCode.MissingReference, classState.BaseClass,
                    "The character's class does not exist.");
                return;
            }

            if (!CharacterAppearanceValidator.IsAllowedFor(
                baseClass.GenderAvailability, character.Identity.Gender))
            {
                report.AddError(ValidationCode.GenderIncompatible, classState.BaseClass,
                    "The class is restricted to " + baseClass.GenderAvailability
                    + " and this character is " + character.Identity.Gender + ".");
            }

            if (!classState.HasChangedJob)
            {
                // A character who has never changed job is complete as they are.
                return;
            }

            if (!jobs.TryGet(classState.CurrentJob, out JobDefinition job))
            {
                report.AddError(ValidationCode.MissingReference, classState.CurrentJob,
                    "The character's job does not exist.");
                return;
            }

            if (job.BaseClass != baseClass.Id)
            {
                report.AddError(ValidationCode.InvalidConfiguration, classState.CurrentJob,
                    "The job belongs to class '" + job.BaseClass
                    + "' but the character is '" + baseClass.Id + "'.");
            }

            if (character.Progression.Level < job.LevelRequirement)
            {
                report.AddError(ValidationCode.ValueOutOfRange, classState.CurrentJob,
                    "The job requires level " + job.LevelRequirement
                    + " but the character is level " + character.Progression.Level + ".");
            }
        }
    }
}
