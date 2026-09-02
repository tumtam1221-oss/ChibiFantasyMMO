using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class DerivedStatsCalculatorTests : DerivedStatsTestBase
    {
        [Test]
        public void ConstantOnlyFormulaProducesTheConstant()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, NoModifiers);

            Assert.IsTrue(result.TryGet(new DefinitionId(MaxHp), out int hp));
            Assert.AreEqual(100, hp);
        }

        [Test]
        public void BaseStatsFeedTheFormula()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            // FIXTURE, not balance: MaxHP = 100 + VIT x 10.
            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, 20)),
                new[] { Formula("f.hp", MaxHp, 100, Term(Vit, 10, 1)) },
                Stats, NoModifiers);

            Assert.AreEqual(300, GetValue(result, MaxHp));
        }

        [Test]
        public void FractionalCoefficientsUseIntegerArithmetic()
        {
            AddPrimaries();
            AddStat(PhysicalAttack, false);

            // FIXTURE: attack = STR + AGI/2, with AGI = 7 so the half truncates.
            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats((Str, 10), (Agi, 7)),
                new[] { Formula("f.atk", PhysicalAttack, 0, Term(Str, 1, 1), Term(Agi, 1, 2)) },
                Stats, NoModifiers);

            Assert.AreEqual(13, GetValue(result, PhysicalAttack),
                "7/2 truncates to 3, so 10 + 3.");
        }

        [Test]
        public void SameInputsAlwaysProduceTheSameResult()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            var calculator = new DerivedStatsCalculator();
            CharacterStatsState stats = BaseStats((Vit, 13));
            DerivedStatFormulaDefinition[] formulas = { Formula("f.hp", MaxHp, 100, Term(Vit, 7, 3)) };

            int first = GetValue(calculator.Calculate(stats, formulas, Stats, NoModifiers), MaxHp);
            int second = GetValue(calculator.Calculate(stats, formulas, Stats, NoModifiers), MaxHp);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void EmptyModifierSetIsDeterministic()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            DerivedStatFormulaDefinition[] formulas = { Formula("f.hp", MaxHp, 50) };
            var calculator = new DerivedStatsCalculator();

            Assert.AreEqual(50, GetValue(calculator.Calculate(BaseStats(), formulas, Stats, NoModifiers), MaxHp));
            Assert.AreEqual(50, GetValue(calculator.Calculate(BaseStats(), formulas, Stats, null), MaxHp));
        }

        [Test]
        public void FlatModifiersAddAfterTheFormula()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, 25f),
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, 5f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, modifiers);

            Assert.AreEqual(130, GetValue(result, MaxHp));
        }

        [Test]
        public void PercentModifiersAreAdditiveWithEachOther()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Percent, 0.2f),
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Percent, 0.2f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, modifiers);

            Assert.AreEqual(140, GetValue(result, MaxHp),
                "Two twenty percent bonuses give forty percent, not forty-four.");
        }

        [Test]
        public void FlatAppliesBeforePercent()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Percent, 1.0f),
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, 100f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, modifiers);

            // (100 + 100) x 2 = 400. Percent first would have given 300.
            Assert.AreEqual(400, GetValue(result, MaxHp),
                "Order is constant, terms, flat, percent, clamp, regardless of modifier order.");
        }

        [Test]
        public void ModifiersForOtherStatsAreIgnored()
        {
            AddPrimaries();
            AddStat(MaxHp, false);
            AddStat(PhysicalAttack, false);

            var modifiers = new[]
            {
                new StatModifier(new DefinitionId(PhysicalAttack), StatModifierKind.Flat, 999f)
            };

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, modifiers);

            Assert.AreEqual(100, GetValue(result, MaxHp));
        }

        private static int GetValue(DerivedStatsResult result, string stat)
        {
            Assert.IsTrue(result.TryGet(new DefinitionId(stat), out int value),
                stat + " should have been computed.");
            return value;
        }
    }
}
