using System;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    internal sealed class CharacterResourceStateTests
    {
        private static readonly ResourceLimits Limits = new ResourceLimits(100, 50);

        private static CharacterResourceState Full()
        {
            return CharacterResourceState.CreateFull(CharacterId.New(), Limits);
        }

        [Test]
        public void CreateFullStartsAtBothCeilings()
        {
            CharacterResourceState resources = Full();

            Assert.AreEqual(100, resources.CurrentHealth);
            Assert.AreEqual(50, resources.CurrentMana);
            Assert.AreEqual(Revision.Initial, resources.Revision);
            Assert.IsTrue(resources.IsHealthFull(Limits));
            Assert.IsTrue(resources.IsManaFull(Limits));
        }

        [Test]
        public void ExplicitConstructionClampsIntoRange()
        {
            var overFull = new CharacterResourceState(CharacterId.New(), Limits, 9999, 9999);
            var underEmpty = new CharacterResourceState(CharacterId.New(), Limits, -50, -50);

            Assert.AreEqual(100, overFull.CurrentHealth);
            Assert.AreEqual(50, overFull.CurrentMana);
            Assert.AreEqual(0, underEmpty.CurrentHealth);
            Assert.AreEqual(0, underEmpty.CurrentMana);
        }

        [Test]
        public void IsRuntimeStateNotPersistentState()
        {
            CharacterResourceState resources = Full();

            Assert.IsInstanceOf<IRuntimeState>(resources);
            Assert.IsInstanceOf<IVersionedState>(resources);
            Assert.IsNotInstanceOf<IPersistentState>(resources);
        }

        [Test]
        public void IsNotSerializable()
        {
            // Runtime state carries no persistence obligation; marking it serializable
            // would invite it becoming a second truth alongside the derived stats.
            Assert.AreEqual(0,
                typeof(CharacterResourceState)
                    .GetCustomAttributes(typeof(SerializableAttribute), false).Length,
                "Runtime resource state must not be marked serializable.");
        }

        [Test]
        public void UsesTheExistingCharacterIdentity()
        {
            CharacterId id = CharacterId.New();
            var resources = CharacterResourceState.CreateFull(id, Limits);

            Assert.AreEqual(id, resources.CharacterId);
            Assert.AreEqual(typeof(CharacterId), resources.CharacterId.GetType());
        }

        [Test]
        public void RequiresAValidCharacter()
        {
            Assert.Throws<ArgumentException>(
                () => CharacterResourceState.CreateFull(CharacterId.None, Limits));
            Assert.Throws<ArgumentException>(
                () => new CharacterResourceState(CharacterId.None, Limits, 1, 1));
        }

        [Test]
        public void SetHealthAndSetMana()
        {
            CharacterResourceState resources = Full();

            resources.SetHealth(40, Limits);
            resources.SetMana(10, Limits);

            Assert.AreEqual(40, resources.CurrentHealth);
            Assert.AreEqual(10, resources.CurrentMana);
        }

        [Test]
        public void ChangeAppliesPositiveAndNegativeDeltas()
        {
            CharacterResourceState resources = Full();

            resources.ChangeHealth(-30, Limits);
            Assert.AreEqual(70, resources.CurrentHealth);

            resources.ChangeHealth(+20, Limits);
            Assert.AreEqual(90, resources.CurrentHealth);

            resources.ChangeMana(-15, Limits);
            Assert.AreEqual(35, resources.CurrentMana);

            resources.ChangeMana(+5, Limits);
            Assert.AreEqual(40, resources.CurrentMana);
        }

        [Test]
        public void ClampsAtZero()
        {
            CharacterResourceState resources = Full();

            resources.ChangeHealth(-1000000, Limits);
            resources.ChangeMana(-1000000, Limits);

            Assert.AreEqual(0, resources.CurrentHealth);
            Assert.AreEqual(0, resources.CurrentMana);
        }

        [Test]
        public void ClampsAtMaximum()
        {
            var resources = new CharacterResourceState(CharacterId.New(), Limits, 1, 1);

            resources.ChangeHealth(+1000000, Limits);
            resources.ChangeMana(+1000000, Limits);

            Assert.AreEqual(100, resources.CurrentHealth);
            Assert.AreEqual(50, resources.CurrentMana);
        }

        [Test]
        public void ExtremeDeltasDoNotOverflow()
        {
            var resources = new CharacterResourceState(CharacterId.New(), Limits, 100, 50);

            resources.ChangeHealth(long.MaxValue, Limits);
            Assert.AreEqual(100, resources.CurrentHealth);

            resources.ChangeHealth(long.MinValue, Limits);
            Assert.AreEqual(0, resources.CurrentHealth, "Must clamp, never wrap to a positive.");

            resources.ChangeMana(long.MinValue, Limits);
            Assert.AreEqual(0, resources.CurrentMana);
        }

        [Test]
        public void MutationIsAtomicAcrossBothResources()
        {
            CharacterResourceState resources = Full();

            resources.ChangeHealth(-1000000, Limits);

            Assert.AreEqual(0, resources.CurrentHealth);
            Assert.AreEqual(50, resources.CurrentMana, "Mana must be untouched by a health change.");
        }

        [Test]
        public void ZeroMaximumsAreValid()
        {
            var empty = new ResourceLimits(0, 0);
            CharacterResourceState resources = CharacterResourceState.CreateFull(CharacterId.New(), empty);

            Assert.AreEqual(0, resources.CurrentHealth);
            Assert.AreEqual(0, resources.CurrentMana);
            Assert.IsTrue(resources.IsHealthFull(empty));

            resources.ChangeHealth(+500, empty);
            resources.SetMana(500, empty);

            Assert.AreEqual(0, resources.CurrentHealth);
            Assert.AreEqual(0, resources.CurrentMana);
        }

        [Test]
        public void NegativeMaximumsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits(0, -1));
            Assert.AreEqual(0, ResourceLimits.None.MaxHealth);
            Assert.AreEqual(0, ResourceLimits.None.MaxMana);
        }
    }
}
