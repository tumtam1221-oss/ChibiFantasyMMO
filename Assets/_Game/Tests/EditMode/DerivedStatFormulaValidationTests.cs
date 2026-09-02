using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class DerivedStatFormulaValidationTests : DerivedStatsTestBase
    {
        private ValidationReport Validate(DerivedStatFormulaDefinition formula)
        {
            var validator = new DefinitionValidator(
                new IDefinitionValidationRule[] { new DerivedStatFormulaValidationRule(Stats) });
            return validator.Validate(formula, Stats);
        }

        [Test]
        public void WellFormedFormulaPasses()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            ValidationReport report = Validate(Formula("f.hp", MaxHp, 100, Term(Vit, 10, 1)));

            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, report.Messages.Count);
        }

        [Test]
        public void FormulaProducingAPrimaryAttributeIsRejected()
        {
            AddPrimaries();

            // A formula must not overwrite an attribute the character actually stores.
            ValidationReport report = Validate(Formula("f.bad", Vit, 100));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void FormulaReadingADerivedStatIsRejected()
        {
            AddPrimaries();
            AddStat(MaxHp, false);
            AddStat(PhysicalAttack, false);

            // This is the cycle protection: derived stats may not feed each other.
            ValidationReport report = Validate(
                Formula("f.atk", PhysicalAttack, 0, Term(MaxHp, 1, 1)));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
            StringAssert.Contains("primary", report.Messages[0].Message);
        }

        [Test]
        public void SelfReferenceIsImpossibleBecauseTheTargetIsNotPrimary()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            ValidationReport report = Validate(Formula("f.hp", MaxHp, 0, Term(MaxHp, 1, 1)));

            Assert.IsFalse(report.IsValid, "A derived stat cannot read itself.");
        }

        [Test]
        public void UnknownProducedStatIsRejected()
        {
            AddPrimaries();

            ValidationReport report = Validate(Formula("f.ghost", "stat.nope", 10));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void UnknownSourceStatIsRejected()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            ValidationReport report = Validate(
                Formula("f.hp", MaxHp, 0, Term("stat.nope", 1, 1)));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingReference, report.Messages[0].Code);
        }

        [Test]
        public void MissingProducedStatIsRejected()
        {
            AddPrimaries();

            ValidationReport report = Validate(Formula("f.empty", "", 10));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
        }

        [Test]
        public void ZeroDenominatorIsRejected()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            ValidationReport report = Validate(Formula("f.hp", MaxHp, 0, Term(Vit, 1, 0)));

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(ValidationCode.InvalidConfiguration, report.Messages[0].Code);
        }

        [Test]
        public void ValidationIsDeterministic()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            DerivedStatFormulaDefinition formula =
                Formula("f.bad", MaxHp, 0, Term(Vit, 1, 0), Term("stat.nope", 1, 1));

            ValidationReport first = Validate(formula);
            ValidationReport second = Validate(formula);

            Assert.AreEqual(first.Messages.Count, second.Messages.Count);

            for (int i = 0; i < first.Messages.Count; i++)
            {
                Assert.AreEqual(first.Messages[i].Code, second.Messages[i].Code, "index " + i);
                Assert.AreEqual(first.Messages[i].Message, second.Messages[i].Message, "index " + i);
            }
        }

        [Test]
        public void FormulasWorkWithTheExistingRegistry()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            var registry = new DefinitionRegistry<DerivedStatFormulaDefinition>();
            DerivedStatFormulaDefinition formula = Formula("f.hp", MaxHp, 100);

            registry.Register(formula);

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(new DefinitionId("f.hp"),
                out DerivedStatFormulaDefinition found));
            Assert.AreSame(formula, found);
            Assert.IsFalse(registry.TryRegister(Formula("f.hp", MaxHp, 50)));
        }
    }
}
