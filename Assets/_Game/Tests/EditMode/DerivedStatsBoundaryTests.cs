using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class DerivedStatsBoundaryTests : DerivedStatsTestBase
    {
        [Test]
        public void MinimumClampIsApplied()
        {
            AddPrimaries();
            AddStat(MaxHp, false, 1, int.MaxValue);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, -500f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, modifiers);

            result.TryGet(new DefinitionId(MaxHp), out int hp);
            Assert.AreEqual(1, hp, "The floor comes from the stat definition, not from code.");
        }

        [Test]
        public void MaximumClampIsApplied()
        {
            AddPrimaries();
            AddStat(MaxHp, false, 0, 250);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, 1000)),
                new[] { Formula("f.hp", MaxHp, 0, Term(Vit, 10, 1)) },
                Stats, NoModifiers);

            result.TryGet(new DefinitionId(MaxHp), out int hp);
            Assert.AreEqual(250, hp);
        }

        [Test]
        public void NegativeResultsAreClampedNotWrapped()
        {
            AddPrimaries();
            AddStat(PhysicalAttack, false, 0, int.MaxValue);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(PhysicalAttack), StatModifierKind.Flat, -9999f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.atk", PhysicalAttack, 10) }, Stats, modifiers);

            result.TryGet(new DefinitionId(PhysicalAttack), out int attack);
            Assert.AreEqual(0, attack);
        }

        [Test]
        public void LargeValuesDoNotOverflowIntoNegatives()
        {
            AddPrimaries();
            AddStat(MaxHp, false, 0, int.MaxValue);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, int.MaxValue)),
                new[] { Formula("f.hp", MaxHp, int.MaxValue, Term(Vit, 1000, 1)) },
                Stats, NoModifiers);

            result.TryGet(new DefinitionId(MaxHp), out int hp);
            Assert.AreEqual(int.MaxValue, hp, "Accumulated in long, then clamped into int range.");
            Assert.Greater(hp, 0);
        }

        [Test]
        public void MissingBaseStatReadsAsZeroRatherThanFailing()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats((Str, 5)),
                new[] { Formula("f.hp", MaxHp, 100, Term(Vit, 10, 1)) },
                Stats, NoModifiers);

            result.TryGet(new DefinitionId(MaxHp), out int hp);
            Assert.AreEqual(100, hp);
        }

        [Test]
        public void FormulaForAnUnknownStatProducesNothingRatherThanZero()
        {
            AddPrimaries();

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.ghost", "stat.does_not_exist", 100) },
                Stats, NoModifiers);

            Assert.AreEqual(0, result.Count);
            Assert.IsFalse(result.Contains(new DefinitionId("stat.does_not_exist")),
                "Missing configuration must be distinguishable from a computed zero.");
        }

        [Test]
        public void ComputedZeroIsPresentAndDistinctFromMissing()
        {
            AddPrimaries();
            AddStat(PhysicalAttack, false, 0, int.MaxValue);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.atk", PhysicalAttack, 0) }, Stats, NoModifiers);

            Assert.IsTrue(result.Contains(new DefinitionId(PhysicalAttack)));
            Assert.IsTrue(result.TryGet(new DefinitionId(PhysicalAttack), out int attack));
            Assert.AreEqual(0, attack);
            Assert.IsFalse(result.TryGet(new DefinitionId(MaxHp), out _));
        }

        [Test]
        public void ZeroDenominatorThrowsRatherThanSilentlyContributing()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            Assert.Throws<InvalidOperationException>(() => new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, 10)),
                new[] { Formula("f.bad", MaxHp, 0, Term(Vit, 1, 0)) },
                Stats, NoModifiers));
        }
    }
}
