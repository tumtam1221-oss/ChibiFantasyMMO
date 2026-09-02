using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class DerivedStatsPurityTests : DerivedStatsTestBase
    {
        [Test]
        public void CalculationLeavesBaseStatsAndRevisionUntouched()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            CharacterStatsState stats = BaseStats((Vit, 20));
            Revision before = stats.Revision;
            CharacterId id = stats.CharacterId;
            int vitBefore = stats.GetOrDefault(new DefinitionId(Vit), -1);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                stats, new[] { Formula("f.hp", MaxHp, 100, Term(Vit, 10, 1)) }, Stats, NoModifiers);

            Assert.AreEqual(before, stats.Revision, "Calculation must not look like a change.");
            Assert.AreEqual(id, stats.CharacterId);
            Assert.AreEqual(vitBefore, stats.GetOrDefault(new DefinitionId(Vit), -1));
            Assert.AreEqual(id, result.CharacterId);
        }

        [Test]
        public void ResultIsNotPersistentOrRuntimeState()
        {
            Assert.IsFalse(typeof(IPersistentState).IsAssignableFrom(typeof(DerivedStatsResult)),
                "Derived stats are recomputed, never a second source of truth.");
            Assert.IsFalse(typeof(IRuntimeState).IsAssignableFrom(typeof(DerivedStatsResult)));
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(DerivedStatsResult)));
        }

        [Test]
        public void ResultCannotBeMutatedByCallers()
        {
            AddPrimaries();
            AddStat(MaxHp, false);

            DerivedStatsResult result = new DerivedStatsCalculator().Calculate(
                BaseStats(), new[] { Formula("f.hp", MaxHp, 100) }, Stats, NoModifiers);

            Assert.IsFalse(result.Stats is CharacterStatEntry[], "The backing array must not escape.");

            foreach (PropertyInfo property in typeof(DerivedStatsResult).GetProperties())
            {
                Assert.IsFalse(property.CanWrite, property.Name + " must be read-only.");
            }
        }

        [Test]
        public void NullArgumentsThrow()
        {
            var calculator = new DerivedStatsCalculator();
            var formulas = new DerivedStatFormulaDefinition[0];

            Assert.Throws<ArgumentNullException>(
                () => calculator.Calculate(null, formulas, Stats, NoModifiers));
            Assert.Throws<ArgumentNullException>(
                () => calculator.Calculate(BaseStats(), null, Stats, NoModifiers));
            Assert.Throws<ArgumentNullException>(
                () => calculator.Calculate(BaseStats(), formulas, null, NoModifiers));
        }

        [Test]
        public void GameplayAssemblyCarriesNoForbiddenDependency()
        {
            Assembly gameplay = typeof(DerivedStatsCalculator).Assembly;

            Assert.AreEqual("ChibiFantasy.Gameplay", gameplay.GetName().Name);

            string[] forbidden =
            {
                "FishNet", "UnityEditor", "ChibiFantasy.Client", "ChibiFantasy.Server",
                "ChibiFantasy.Backend", "ChibiFantasy.UI", "ChibiFantasy.Network"
            };

            foreach (AssemblyName referenced in gameplay.GetReferencedAssemblies())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(referenced.Name.StartsWith(name, StringComparison.Ordinal),
                        "Gameplay must not reference " + referenced.Name);
                }
            }
        }

        [Test]
        public void NoClassJobEquipmentCardOrPetDependencyLeaked()
        {
            string[] forbidden = { "Class", "Job", "Equipment", "Card", "Pet", "DevilFruit", "Combat" };

            foreach (MemberInfo member in typeof(DerivedStatsCalculator).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Found " + member.Name + "; those systems only contribute StatModifiers later.");
                }
            }
        }

        [Test]
        public void DerivedStatsUseTheExistingStatDefinitionIdentity()
        {
            AddPrimaries();
            StatDefinition derived = AddStat(MaxHp, false);

            Assert.IsInstanceOf<GameDefinition>(derived);
            Assert.IsFalse(derived.IsPrimary, "Derived stats are stat definitions that are not primary.");
            Assert.AreEqual(new DefinitionId(MaxHp), derived.Id);
            Assert.IsTrue(Stats.Contains(new DefinitionId(MaxHp)));
        }
    }
}
