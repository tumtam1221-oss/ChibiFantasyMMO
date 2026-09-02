using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class DefinitionReferenceValidationTests
    {
        [Test]
        public void MissingReference_IsDetected()
        {
            var definition = new ReferencingDef("equip.sword", "rarity.rare", "rarity.ghost");

            ValidationReport report = new DefinitionValidator()
                .Validate(definition, ValidationTestHelper.LookupWith("rarity.rare"));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
            Assert.AreEqual(new DefinitionId("equip.sword"), report.Messages[0].DefinitionId,
                "The finding must name the definition holding the broken reference.");
        }

        [Test]
        public void ResolvableReferences_Pass()
        {
            var definition = new ReferencingDef("equip.sword", "rarity.rare");

            ValidationReport report = new DefinitionValidator()
                .Validate(definition, ValidationTestHelper.LookupWith("rarity.rare"));

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void UnsetRequiredReference_IsReported()
        {
            var definition = new ReferencingDef("equip.sword", "");

            ValidationReport report = new DefinitionValidator()
                .Validate(definition, ValidationTestHelper.LookupWith());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void ReferenceCheckingIsOptIn()
        {
            ValidationReport report = new DefinitionValidator()
                .Validate(new FakeDef("plain"), ValidationTestHelper.LookupWith());

            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void PluggableRule_RunsAndChoosesItsOwnSeverity()
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new AlwaysWarnsRule() });

            ValidationReport report = validator.Validate(
                new FakeDef("ok"), ValidationTestHelper.LookupWith());

            Assert.IsTrue(report.IsValid, "A warning must not fail validation.");
            Assert.AreEqual(1, report.WarningCount);
            Assert.AreEqual(ValidationSeverity.Warning, report.Messages[0].Severity);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            var validator = new DefinitionValidator();
            var set = new IDefinition[] { new FakeDef("a"), new FakeDef("a"), new FakeDef(""), null };

            ValidationReport first = validator.Validate(set, ValidationTestHelper.LookupWith());
            ValidationReport second = validator.Validate(set, ValidationTestHelper.LookupWith());

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);
            Assert.AreEqual(first.ErrorCount, second.ErrorCount);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Severity, second.Messages[i].Severity, "index " + i);
                Assert.AreEqual(first.Messages[i].DefinitionId, second.Messages[i].DefinitionId, "index " + i);
            }
        }

        [Test]
        public void ValidatorDoesNotMutateDefinitions()
        {
            var definition = new ReferencingDef("equip.sword", "rarity.ghost");
            DefinitionId idBefore = definition.Id;
            var referencesBefore = new List<DefinitionId>(definition.GetRequiredReferences());

            new DefinitionValidator().Validate(definition, ValidationTestHelper.LookupWith());

            Assert.AreEqual(idBefore, definition.Id);
            CollectionAssert.AreEqual(
                referencesBefore, new List<DefinitionId>(definition.GetRequiredReferences()));
        }

        [Test]
        public void NoLookup_SkipsReferenceCheckingRatherThanFailing()
        {
            var definition = new ReferencingDef("equip.sword", "rarity.ghost");

            ValidationReport report = new DefinitionValidator().Validate(definition, null);

            Assert.IsTrue(report.IsValid,
                "With no lookup supplied there is nothing to resolve against, so references are not judged.");
        }
    }
}
