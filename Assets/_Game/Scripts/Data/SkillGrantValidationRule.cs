using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks the skills a class or a job hands out.
    /// </summary>
    /// <remarks>
    /// <b>The gap this closes.</b> <see cref="ClassDefinition.StartingSkills"/> and
    /// <see cref="JobDefinition.Skills"/> have been authored fields since 05.7 and were
    /// read by nothing at all: not a rule, not a test, not the project scan. A class could
    /// name a skill that did not exist, name the same one twice, or hand out a skill that
    /// belongs to a different class entirely, and every validator in the project would
    /// report the content clean. Those are the two edges that join skill content to
    /// character content, and they were the only unchecked references left.
    ///
    /// <b>Why a rule rather than <see cref="IReferencingDefinition"/>.</b> That mechanism
    /// can ask one question -- does this id resolve -- and these references need three
    /// more: is it named twice, does the granted skill agree with who is granting it, and
    /// can a character actually hold it at the moment it is granted. A definition that
    /// merely listed its ids would answer the first and silently pass the rest, and would
    /// also duplicate the resolve error this rule already words better. The rule is the
    /// established mechanism here; five others already plug into
    /// <see cref="DefinitionValidator"/> the same way.
    ///
    /// <b>Direction of authority.</b> A skill states who may hold it, through
    /// <see cref="SkillDefinition.RequiredClass"/> and
    /// <see cref="SkillDefinition.RequiredJob"/>. A class or job listing that skill is the
    /// other half of the same claim, and the two can disagree. The skill's own statement is
    /// authoritative, exactly as a job's level requirement is authoritative over the class
    /// that advertises it; this rule makes the disagreement loud instead of letting a skill
    /// appear on a class that its own definition forbids.
    ///
    /// <b>What is deliberately not checked.</b> Nothing here asks what happens at runtime.
    /// Whether a character can afford a skill, whether it is off cooldown, at what level a
    /// granted skill starts, and whether the starting character level clears a level
    /// table's requirement are all unanswerable from definitions alone -- the starting
    /// level is authored on <see cref="CharacterProgressionDefinition"/>, which a class
    /// does not name -- so they are left to the steps that own them.
    ///
    /// Reports, never repairs. Deterministic: entries are examined in authored order.
    /// </remarks>
    public sealed class SkillGrantValidationRule : IDefinitionValidationRule
    {
        private readonly IDefinitionRegistry<SkillDefinition> _skills;

        public SkillGrantValidationRule(IDefinitionRegistry<SkillDefinition> skills)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            switch (definition)
            {
                case ClassDefinition characterClass:
                    ValidateClassGrants(characterClass, report);
                    break;

                case JobDefinition job:
                    ValidateJobGrants(job, report);
                    break;
            }
        }

        /// <summary>
        /// What a freshly created character of this class knows.
        /// </summary>
        /// <remarks>Stricter than a job's list, because the holder is brand new: they hold
        /// no job and know nothing beyond this list, so a grant that depends on either
        /// cannot be satisfied.</remarks>
        private void ValidateClassGrants(ClassDefinition characterClass, ValidationReport report)
        {
            DefinitionId id = characterClass.Id;
            DefinitionId[] granted = characterClass.StartingSkills;

            if (granted == null || granted.Length == 0)
            {
                // A class that starts with nothing is legal.
                return;
            }

            var seen = new HashSet<DefinitionId>();

            for (int i = 0; i < granted.Length; i++)
            {
                if (!TryResolveGrant(granted[i], i, id, "Starting skill", seen, report,
                        out SkillDefinition skill))
                {
                    continue;
                }

                if (skill.RequiredClass.IsValid && skill.RequiredClass != id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Starts with '" + granted[i] + "', which requires class '"
                        + skill.RequiredClass + "'.");
                }

                if (skill.RequiredJob.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Starts with '" + granted[i] + "', which requires job '"
                        + skill.RequiredJob + "' that a new character cannot yet hold.");
                }

                ValidateStartingPrerequisites(skill, granted[i], id, granted, report);
            }
        }

        /// <summary>
        /// What taking this job unlocks.
        /// </summary>
        /// <remarks>Prerequisites are not checked here, unlike a class's list: a character
        /// reaching a job has had a career in which to learn them, and which skills they
        /// actually took is runtime state no definition can see.</remarks>
        private void ValidateJobGrants(JobDefinition job, ValidationReport report)
        {
            DefinitionId id = job.Id;
            DefinitionId[] granted = job.Skills;

            if (granted == null || granted.Length == 0)
            {
                // A job that unlocks no skill is legal; it may exist for its stats alone.
                return;
            }

            var seen = new HashSet<DefinitionId>();

            for (int i = 0; i < granted.Length; i++)
            {
                if (!TryResolveGrant(granted[i], i, id, "Skill", seen, report,
                        out SkillDefinition skill))
                {
                    continue;
                }

                if (skill.RequiredJob.IsValid && skill.RequiredJob != id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Unlocks '" + granted[i] + "', which requires job '"
                        + skill.RequiredJob + "'.");
                }

                if (skill.RequiredClass.IsValid && job.BaseClass.IsValid
                    && skill.RequiredClass != job.BaseClass)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, id,
                        "Unlocks '" + granted[i] + "', which requires class '"
                        + skill.RequiredClass + "' rather than this job's class '"
                        + job.BaseClass + "'.");
                }
            }
        }

        /// <summary>
        /// The checks every grant shares: it names something, names it once, and it exists.
        /// </summary>
        /// <returns>False when the entry cannot be examined further.</returns>
        private bool TryResolveGrant(DefinitionId granted, int index, DefinitionId owner,
            string what, HashSet<DefinitionId> seen, ValidationReport report,
            out SkillDefinition skill)
        {
            skill = null;

            if (!granted.IsValid)
            {
                report.AddError(ValidationCode.MissingDefinitionId, owner,
                    what + " entry " + index + " names no skill.");
                return false;
            }

            if (!seen.Add(granted))
            {
                report.AddError(ValidationCode.DuplicateDefinitionId, owner,
                    what + " '" + granted + "' is listed more than once.");
                return false;
            }

            if (!_skills.TryGet(granted, out skill))
            {
                report.AddError(ValidationCode.MissingReference, owner,
                    what + " '" + granted + "' does not exist.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// A starting skill's own prerequisites must also be granted at creation.
        /// </summary>
        /// <remarks>
        /// Provable from definitions: a character at creation knows exactly this list, so a
        /// prerequisite outside it can never have been met. The same shape as the existing
        /// class rule rejecting a first job that itself requires a previous job.
        ///
        /// Only the identity is checked, not the level a prerequisite demands. Whether a
        /// granted skill starts at rank one is runtime state that no definition states, and
        /// asserting it here would be a guess about a system that does not exist.
        /// </remarks>
        private static void ValidateStartingPrerequisites(SkillDefinition skill,
            DefinitionId grantedId, DefinitionId owner, DefinitionId[] granted,
            ValidationReport report)
        {
            SkillPrerequisite[] prerequisites = skill.Prerequisites;

            if (prerequisites == null)
            {
                return;
            }

            for (int i = 0; i < prerequisites.Length; i++)
            {
                DefinitionId required = prerequisites[i].Skill;

                if (!required.IsValid || Contains(granted, required))
                {
                    continue;
                }

                report.AddError(ValidationCode.InvalidConfiguration, owner,
                    "Starts with '" + grantedId + "', which requires '" + required
                    + "' that a new character is not also given.");
            }
        }

        private static bool Contains(DefinitionId[] ids, DefinitionId id)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
