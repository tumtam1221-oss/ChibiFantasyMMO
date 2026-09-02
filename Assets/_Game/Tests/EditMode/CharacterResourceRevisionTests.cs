using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterResourceRevisionTests
    {
        private static readonly ResourceLimits Limits = new ResourceLimits(100, 50);

        private static CharacterResourceState Full()
        {
            return CharacterResourceState.CreateFull(CharacterId.New(), Limits);
        }

        [Test]
        public void RealChangeAdvancesRevisionExactlyOnce()
        {
            CharacterResourceState resources = Full();

            resources.ChangeHealth(-10, Limits);
            Assert.AreEqual(1, resources.Revision.Value);

            resources.ChangeMana(-5, Limits);
            Assert.AreEqual(2, resources.Revision.Value);
        }

        [Test]
        public void ChangeThatChangesNothingLeavesRevisionAlone()
        {
            CharacterResourceState resources = Full();

            resources.SetHealth(100, Limits);
            resources.ChangeHealth(0, Limits);
            resources.SetMana(50, Limits);

            Assert.AreEqual(Revision.Initial, resources.Revision,
                "The counter tracks transitions, not call volume.");
        }

        [Test]
        public void ChangeClampedToAnUnchangedValueDoesNotAdvanceRevision()
        {
            CharacterResourceState resources = Full();

            // Already at maximum; adding more clamps back to the same number.
            resources.ChangeHealth(+500, Limits);

            Assert.AreEqual(100, resources.CurrentHealth);
            Assert.AreEqual(Revision.Initial, resources.Revision);
        }

        [Test]
        public void ReadsDoNotAdvanceRevision()
        {
            CharacterResourceState resources = Full();
            resources.ChangeHealth(-10, Limits);
            Revision after = resources.Revision;

            int hp = resources.CurrentHealth;
            int mp = resources.CurrentMana;
            bool fullHp = resources.IsHealthFull(Limits);
            bool fullMp = resources.IsManaFull(Limits);
            CharacterId id = resources.CharacterId;

            Assert.AreEqual(after, resources.Revision);
            Assert.AreEqual(90, hp);
            Assert.AreEqual(50, mp);
            Assert.IsFalse(fullHp);
            Assert.IsTrue(fullMp);
            Assert.IsTrue(id.IsValid);
        }

        [Test]
        public void FailedConstructionCreatesNoStateAtAll()
        {
            // Clamping means value operations cannot fail, so the only failure path is
            // construction, which throws before any state exists.
            Assert.Throws<ArgumentException>(
                () => CharacterResourceState.CreateFull(CharacterId.None, Limits));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits(-1, 0));
        }

        [Test]
        public void IdentityNeverChanges()
        {
            CharacterId id = CharacterId.New();
            var resources = CharacterResourceState.CreateFull(id, Limits);

            resources.ChangeHealth(-50, Limits);
            resources.SetMana(0, Limits);
            resources.ClampTo(new ResourceLimits(10, 10));

            Assert.AreEqual(id, resources.CharacterId);

            foreach (PropertyInfo property in typeof(CharacterResourceState).GetProperties())
            {
                Assert.IsFalse(property.CanWrite, property.Name + " must be read-only.");
            }
        }

        [Test]
        public void LoweredMaximumClampsCurrentValues()
        {
            CharacterResourceState resources = Full();

            resources.ClampTo(new ResourceLimits(30, 20));

            Assert.AreEqual(30, resources.CurrentHealth);
            Assert.AreEqual(20, resources.CurrentMana);
            Assert.AreEqual(1, resources.Revision.Value);
        }

        [Test]
        public void RaisedMaximumDoesNotRefillOrChangeAnything()
        {
            var resources = new CharacterResourceState(CharacterId.New(), Limits, 40, 20);

            resources.ClampTo(new ResourceLimits(500, 500));

            Assert.AreEqual(40, resources.CurrentHealth,
                "A bigger ceiling is not free health.");
            Assert.AreEqual(20, resources.CurrentMana);
            Assert.AreEqual(Revision.Initial, resources.Revision);
        }

        [Test]
        public void MaximaAreNotStoredOnTheResourceState()
        {
            foreach (FieldInfo field in typeof(CharacterResourceState).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.AreNotEqual(typeof(ResourceLimits), field.FieldType,
                    "A stored ceiling would go stale the moment derived stats change.");
                Assert.IsFalse(field.Name.IndexOf("max", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Found " + field.Name + "; maxima belong to the derived-stat layer.");
            }
        }

        [Test]
        public void NoCombatDeathOrRegenerationConcepts()
        {
            // "Heal" is deliberately absent from this list: CurrentHealth legitimately
            // contains it. The concepts, not the letters, are what must not appear.
            string[] forbidden =
            {
                "Damage", "Healing", "Death", "Dead", "Kill", "Revive", "Resurrect",
                "Regen", "Tick", "Potion", "Attack", "Defend"
            };

            foreach (MemberInfo member in typeof(CharacterResourceState).GetMembers())
            {
                foreach (string name in forbidden)
                {
                    Assert.IsFalse(member.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Found " + member.Name + "; those belong to later systems.");
                }
            }
        }

        [Test]
        public void CarriesNoForbiddenDependency()
        {
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(CharacterResourceState)));

            Assembly gameplay = typeof(CharacterResourceState).Assembly;
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
    }
}
