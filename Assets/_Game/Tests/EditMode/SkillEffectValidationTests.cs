using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class SkillEffectValidationTests : SkillTestBase
    {
        private DefinitionRegistry<StatDefinition> _stats;
        private DefinitionRegistry<StatusEffectDefinition> _statusEffects;

        private const string Str = "stat.str";
        private const string Burning = "status.burning";

        private void SetUpContent()
        {
            _stats = new DefinitionRegistry<StatDefinition>();
            _statusEffects = new DefinitionRegistry<StatusEffectDefinition>();

            var stat = ScriptableObject.CreateInstance<StatDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + Str + "\"}}", stat);
            _stats.Register(stat);

            var status = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + Burning + "\"}}", status);
            _statusEffects.Register(status);
        }

        private DefinitionValidator Validator()
        {
            return new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new SkillEffectValidationRule(_stats, _statusEffects)
            });
        }

        private int _skillCounter;

        /// <summary>Each call registers a distinct skill, so a test may validate twice.</summary>
        private ValidationReport ValidateEffects(params SkillEffect[] effects)
        {
            SetUpContent();

            SkillDefinition skill = AddSkill("skill.effects." + _skillCounter++, 1,
                levels: new[] { Level(1, 1, 0f, 0f, effects) });

            return Validator().Validate(skill, Skills);
        }

        [Test]
        public void WellFormedEffectsOfEveryKindPass()
        {
            ValidationReport report = ValidateEffects(
                SkillEffect.Damage(50, ElementType.Fire,
                    new[] { new StatTerm(new DefinitionId(Str), 3, 2) }),
                SkillEffect.Heal(30, SkillResourceType.Health),
                SkillEffect.ApplyStatusEffect(new DefinitionId(Burning)),
                SkillEffect.ModifyStat(
                    new StatModifier(new DefinitionId(Str), StatModifierKind.Percent, 0.2f)),
                SkillEffect.ModifyResource(SkillResourceType.Mana, 10));

            Assert.IsTrue(report.IsValid,
                report.Messages.Count > 0 ? report.Messages[0].Message : string.Empty);
        }

        [Test]
        public void AnEffectWithNoKindIsRejected()
        {
            ValidationReport report = ValidateEffects(default(SkillEffect));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("no kind", report.Messages[0].Message);
        }

        [Test]
        public void AnUnknownStatusEffectIsRejected()
        {
            ValidationReport report = ValidateEffects(
                SkillEffect.ApplyStatusEffect(new DefinitionId("status.ghost")));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AStatusApplicationWithNoReferenceIsRejected()
        {
            ValidationReport report = ValidateEffects(
                SkillEffect.ApplyStatusEffect(DefinitionId.None));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void AnUnknownStatIsRejectedForModifyStat()
        {
            ValidationReport report = ValidateEffects(SkillEffect.ModifyStat(
                new StatModifier(new DefinitionId("stat.ghost"), StatModifierKind.Flat, 1f)));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AnUnknownScalingStatIsRejected()
        {
            ValidationReport report = ValidateEffects(SkillEffect.Damage(10, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId("stat.ghost"), 1, 1) }));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void AZeroDenominatorScalingTermIsRejected()
        {
            ValidationReport report = ValidateEffects(SkillEffect.Damage(10, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId(Str), 1, 0) }));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("denominator", report.Messages[0].Message);
        }

        [Test]
        public void AnAmountEffectWithNeitherFlatNorScalingIsRejected()
        {
            ValidationReport report = ValidateEffects(SkillEffect.Damage(0, ElementType.Fire));

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("neither a flat amount nor scaling", report.Messages[0].Message);
        }

        [Test]
        public void ANegativeAmountIsRejected()
        {
            ValidationReport report = ValidateEffects(
                SkillEffect.Heal(-10, SkillResourceType.Health));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.ValueOutOfRange, report.Messages[0].Code);
            StringAssert.Contains("direction belongs", report.Messages[0].Message);
        }

        [Test]
        public void AResourceEffectWithNoResourceIsRejected()
        {
            Assert.IsFalse(ValidateEffects(SkillEffect.Heal(10, SkillResourceType.None)).IsValid);

            ValidationReport change = ValidateEffects(
                SkillEffect.ModifyResource(SkillResourceType.None, 10));

            Assert.IsFalse(change.IsValid);
            StringAssert.Contains("names no resource", change.Messages[0].Message);
        }

        [Test]
        public void ASkillWithNoLevelTableIsNotFaulted()
        {
            SetUpContent();

            Assert.IsTrue(Validator().Validate(AddSkill("skill.plain"), Skills).IsValid);
        }

        [Test]
        public void ValidationIsDeterministicAndDoesNotMutate()
        {
            SetUpContent();

            SkillDefinition skill = AddSkill("skill.messy", 1, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(0, ElementType.Fire),
                    SkillEffect.ApplyStatusEffect(new DefinitionId("status.ghost"))
                })
            });

            string before = JsonUtility.ToJson(skill);
            ValidationReport first = Validator().Validate(skill, Skills);
            ValidationReport second = Validator().Validate(skill, Skills);

            Assert.AreEqual(before, JsonUtility.ToJson(skill));
            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void NullRegistriesAreRejected()
        {
            SetUpContent();

            Assert.Throws<System.ArgumentNullException>(
                () => new SkillEffectValidationRule(null, _statusEffects));
            Assert.Throws<System.ArgumentNullException>(
                () => new SkillEffectValidationRule(_stats, null));
        }
    }
}
