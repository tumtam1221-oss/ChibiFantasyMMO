using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Proves complete skill validation cannot be half-run.
    /// </summary>
    internal sealed class SkillContentValidatorTests : SkillTestBase
    {
        private DefinitionRegistry<StatDefinition> _stats;
        private DefinitionRegistry<StatusEffectDefinition> _statusEffects;

        private const string Str = "stat.str";
        private const string Burning = "status.burning";

        private SkillContentValidator Composed()
        {
            _stats = new DefinitionRegistry<StatDefinition>();
            _statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            var stat = ScriptableObject.CreateInstance<StatDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + Str + "\"}}", stat);
            _stats.Register(stat);

            var status = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + Burning + "\"}}", status);
            _statusEffects.Register(status);

            return new SkillContentValidator(Skills, Classes, Jobs, _stats, _statusEffects);
        }

        private SkillDefinition Usable(string id, SkillEffect[] effects = null)
        {
            SkillDefinition skill = AddSkill(id, 1,
                levels: new[] { Level(1, 1, 0f, 0f, effects) });
            SetPrivate(skill, "_category", SkillCategory.Active);
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            return skill;
        }

        [Test]
        public void AValidSkillWithSeveralValidEffectsPasses()
        {
            SkillContentValidator validator = Composed();

            SkillDefinition skill = Usable("skill.good", new[]
            {
                SkillEffect.Damage(40, ElementType.Fire,
                    new[] { new StatTerm(new DefinitionId(Str), 3, 2) }),
                SkillEffect.ApplyStatusEffect(new DefinitionId(Burning)),
                SkillEffect.ModifyStat(
                    new StatModifier(new DefinitionId(Str), StatModifierKind.Percent, 0.1f))
            });

            ValidationReport report = validator.Validate(skill);

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
        }

        [Test]
        public void OneCallCatchesBothStructuralAndEffectFaults()
        {
            SkillContentValidator validator = Composed();

            // Structural fault: no category. Effect fault: unknown status effect.
            SkillDefinition skill = AddSkill("skill.bothbroken", 1, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.ApplyStatusEffect(new DefinitionId("status.ghost"))
                })
            });
            SetPrivate(skill, "_category", SkillCategory.None);

            ValidationReport report = validator.Validate(skill);

            Assert.IsFalse(report.IsValid);
            Assert.GreaterOrEqual(report.ErrorCount, 2,
                "A caller running one rule would have seen only half the problems.");

            bool sawStructural = false;
            bool sawEffect = false;

            foreach (ValidationMessage message in report.Messages)
            {
                if (message.Message.Contains("no category"))
                {
                    sawStructural = true;
                }

                if (message.Message.Contains("not a known status effect"))
                {
                    sawEffect = true;
                }
            }

            Assert.IsTrue(sawStructural, "Structural rule did not run.");
            Assert.IsTrue(sawEffect, "Effect rule did not run.");
        }

        [Test]
        public void MissingIdIsStillCaughtByTheGenericValidator()
        {
            SkillContentValidator validator = Composed();
            SkillDefinition skill = AddSkill("", register: false);

            ValidationReport report = validator.Validate(skill);

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void DuplicateIdsAcrossASetAreReported()
        {
            SkillContentValidator validator = Composed();

            SkillDefinition first = Usable("skill.dup");
            SkillDefinition second = AddSkill("skill.dup", register: false);
            SetPrivate(second, "_category", SkillCategory.Active);
            SetPrivate(second, "_targetType", SkillTargetType.Self);

            ValidationReport report = validator.Validate(new[] { first, second });

            Assert.IsFalse(report.IsValid);

            bool sawDuplicate = false;
            foreach (ValidationMessage message in report.Messages)
            {
                if (message.Code == ValidationCode.DuplicateDefinitionId)
                {
                    sawDuplicate = true;
                }
            }

            Assert.IsTrue(sawDuplicate);
        }

        [Test]
        public void ValidationIsDeterministicAndDoesNotMutate()
        {
            SkillContentValidator validator = Composed();

            SkillDefinition skill = AddSkill("skill.messy", 1, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[] { SkillEffect.Damage(0, ElementType.Fire) })
            });

            string before = JsonUtility.ToJson(skill);
            ValidationReport first = validator.Validate(skill);
            ValidationReport second = validator.Validate(skill);

            Assert.AreEqual(before, JsonUtility.ToJson(skill));
            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NullRegistriesAndNullSetsAreRejected()
        {
            SkillContentValidator validator = Composed();

            Assert.Throws<ArgumentNullException>(
                () => new SkillContentValidator(null, Classes, Jobs, _stats, _statusEffects));
            Assert.Throws<ArgumentNullException>(
                () => new SkillContentValidator(Skills, null, Jobs, _stats, _statusEffects));
            Assert.Throws<ArgumentNullException>(
                () => new SkillContentValidator(Skills, Classes, null, _stats, _statusEffects));
            Assert.Throws<ArgumentNullException>(
                () => new SkillContentValidator(Skills, Classes, Jobs, null, _statusEffects));
            Assert.Throws<ArgumentNullException>(
                () => new SkillContentValidator(Skills, Classes, Jobs, _stats, null));
            Assert.Throws<ArgumentNullException>(
                () => validator.Validate((System.Collections.Generic.IEnumerable<SkillDefinition>)null));
        }
    }
}
