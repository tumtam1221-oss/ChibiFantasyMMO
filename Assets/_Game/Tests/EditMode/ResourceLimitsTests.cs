using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Proves the maxima come from the derived-stat layer and are not recomputed here.
    /// Every number is a test fixture, not game balance.
    /// </summary>
    internal sealed class ResourceLimitsTests : DerivedStatsTestBase
    {
        private const string MaxMp = "stat.max_mp";

        [Test]
        public void LimitsAreReadFromTheDerivedStatResult()
        {
            AddPrimaries();
            AddStat(MaxHp, false);
            AddStat(MaxMp, false);

            // FIXTURE: MaxHP = 100 + VIT x 10, MaxMP = 20 + STR x 2.
            DerivedStatsResult derived = new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, 20), (Str, 5)),
                new[]
                {
                    Formula("f.hp", MaxHp, 100, Term(Vit, 10, 1)),
                    Formula("f.mp", MaxMp, 20, Term(Str, 2, 1))
                },
                Stats, NoModifiers);

            ResourceLimits limits = ResourceLimits.From(
                derived, new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            Assert.AreEqual(300, limits.MaxHealth);
            Assert.AreEqual(30, limits.MaxMana);
        }

        [Test]
        public void ResourcesInitialiseFullFromCalculatedMaxima()
        {
            AddPrimaries();
            AddStat(MaxHp, false);
            AddStat(MaxMp, false);

            DerivedStatsResult derived = new DerivedStatsCalculator().Calculate(
                BaseStats((Vit, 10)),
                new[]
                {
                    Formula("f.hp", MaxHp, 0, Term(Vit, 10, 1)),
                    Formula("f.mp", MaxMp, 25)
                },
                Stats, NoModifiers);

            ResourceLimits limits = ResourceLimits.From(
                derived, new DefinitionId(MaxHp), new DefinitionId(MaxMp));
            CharacterResourceState resources =
                CharacterResourceState.CreateFull(CharacterId.New(), limits);

            Assert.AreEqual(100, resources.CurrentHealth);
            Assert.AreEqual(25, resources.CurrentMana);
        }

        [Test]
        public void AMissingDerivedStatReadsAsZeroCeiling()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            // No maximum-mana formula at all.
            DerivedStatsResult derived = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 80) }, Stats, NoModifiers);

            ResourceLimits limits = ResourceLimits.From(
                derived, new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            Assert.AreEqual(80, limits.MaxHealth);
            Assert.AreEqual(0, limits.MaxMana, "Absent configuration yields an empty pool, not an error.");
        }

        [Test]
        public void RecalculationLowersCeilingsAndResourcesFollow()
        {
            AddPrimaries();
            AddStat(MaxHp, false);
            AddStat(MaxMp, false);

            DerivedStatFormulaDefinition[] formulas =
            {
                Formula("f.hp", MaxHp, 0, Term(Vit, 10, 1)),
                Formula("f.mp", MaxMp, 50)
            };
            var calculator = new DerivedStatsCalculator();

            DerivedStatsResult strong = calculator.Calculate(
                BaseStats((Vit, 30)), formulas, Stats, NoModifiers);
            ResourceLimits before = ResourceLimits.From(
                strong, new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            CharacterResourceState resources =
                CharacterResourceState.CreateFull(CharacterId.New(), before);
            Assert.AreEqual(300, resources.CurrentHealth);

            // Vitality drops, so the ceiling drops with it.
            DerivedStatsResult weakened = calculator.Calculate(
                BaseStats((Vit, 5)), formulas, Stats, NoModifiers);
            ResourceLimits after = ResourceLimits.From(
                weakened, new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            resources.ClampTo(after);

            Assert.AreEqual(50, resources.CurrentHealth,
                "Plain clamping: the surplus is lost, no ratio is preserved.");
            Assert.AreEqual(50, resources.CurrentMana);
        }

        [Test]
        public void CalculatorRemainsTheOnlySourceOfMaxima()
        {
            foreach (var member in typeof(CharacterResourceState).GetMembers())
            {
                Assert.IsFalse(member.Name.IndexOf("Calculate", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Resource state must not compute a maximum itself.");
            }

            foreach (var member in typeof(ResourceLimits).GetMembers())
            {
                Assert.IsFalse(member.Name.IndexOf("Formula", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Limits look maxima up; they never derive them.");
            }
        }

        [Test]
        public void NullDerivedResultIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => ResourceLimits.From(
                null, new DefinitionId(MaxHp), new DefinitionId(MaxMp)));
        }
    }
}
