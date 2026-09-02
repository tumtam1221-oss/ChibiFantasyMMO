using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class DefinitionValidationTests
    {
        [Test]
        public void ValidDefinition_Passes()
        {
            ValidationReport report = new DefinitionValidator()
                .Validate(new FakeDef("item.potion"), ValidationTestHelper.LookupWith());

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.ErrorCount);
            Assert.AreEqual(0, report.WarningCount);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void MissingDefinitionId_IsReportedAsError()
        {
            ValidationReport report = new DefinitionValidator()
                .Validate(new FakeDef("  "), ValidationTestHelper.LookupWith());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
            Assert.AreEqual(ValidationSeverity.Error, report.Messages[0].Severity);
        }

        [Test]
        public void NullDefinition_IsReportedNotThrown()
        {
            ValidationReport report = new DefinitionValidator()
                .Validate((IDefinition)null, ValidationTestHelper.LookupWith());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.NullDefinition, report.Messages[0].Code);
        }

        [Test]
        public void DuplicateIdsInASet_AreReported()
        {
            var set = new IDefinition[] { new FakeDef("dup"), new FakeDef("dup"), new FakeDef("ok") };

            ValidationReport report = new DefinitionValidator()
                .Validate(set, ValidationTestHelper.LookupWith());

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(ValidationCode.DuplicateDefinitionId, report.Messages[0].Code);
            Assert.AreEqual(new DefinitionId("dup"), report.Messages[0].DefinitionId);
        }

        [Test]
        public void NullSet_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new DefinitionValidator()
                    .Validate((IEnumerable<IDefinition>)null, ValidationTestHelper.LookupWith()));
        }

        [Test]
        public void RegistrySatisfiesTheLookupContract()
        {
            IDefinitionLookup lookup = ValidationTestHelper.LookupWith("a", "b");

            Assert.IsTrue(lookup.Contains(new DefinitionId("a")));
            Assert.IsFalse(lookup.Contains(new DefinitionId("c")));
        }

        [Test]
        public void ReportCountsErrorsAndWarningsSeparately()
        {
            var report = new ValidationReport();

            Assert.IsTrue(report.IsValid);

            report.AddWarning(ValidationCode.MissingReference, new DefinitionId("a"), "w");

            Assert.IsTrue(report.IsValid, "Warnings must not fail validation.");
            Assert.AreEqual(1, report.WarningCount);

            report.AddError(ValidationCode.MissingReference, new DefinitionId("a"), "e");

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(2, report.Messages.Count);
        }

        [Test]
        public void MessageCarriesCodeSeverityAndDefinitionId()
        {
            var message = new ValidationMessage(
                ValidationSeverity.Error, ValidationCode.DuplicateDefinitionId,
                new DefinitionId("item.potion"), "text");

            Assert.AreEqual(ValidationSeverity.Error, message.Severity);
            Assert.AreEqual(ValidationCode.DuplicateDefinitionId, message.Code);
            Assert.AreEqual(new DefinitionId("item.potion"), message.DefinitionId);
            Assert.AreEqual("text", message.Message);
            StringAssert.Contains("item.potion", message.ToString());
        }
    }
}
