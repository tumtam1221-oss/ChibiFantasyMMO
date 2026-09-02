using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class AppearanceDefinitionTests
    {
        private static AppearanceOptionDefinition NewOption(string id, AppearanceSlot slot,
            GenderAvailability availability)
        {
            var option = ScriptableObject.CreateInstance<AppearanceOptionDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_slot\":" + (int)slot
                + ",\"_genderAvailability\":" + (int)availability + "}", option);
            return option;
        }

        [Test]
        public void ImplementsTheExistingDefinitionContract()
        {
            var option = NewOption("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            try
            {
                Assert.IsInstanceOf<GameDefinition>(option);
                Assert.IsInstanceOf<IDefinition>(option);
                Assert.AreEqual(typeof(GameDefinition).Assembly, typeof(AppearanceOptionDefinition).Assembly);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(option);
            }
        }

        [Test]
        public void CarriesIdSlotAndAvailability()
        {
            var option = NewOption("face_001", AppearanceSlot.Face, GenderAvailability.FemaleOnly);
            try
            {
                Assert.AreEqual(new DefinitionId("face_001"), option.Id);
                Assert.IsTrue(option.Id.IsValid);
                Assert.AreEqual(AppearanceSlot.Face, option.Slot);
                Assert.AreEqual(GenderAvailability.FemaleOnly, option.GenderAvailability);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(option);
            }
        }

        [Test]
        public void SerializesSlotAndAvailability()
        {
            var option = NewOption("eye_001", AppearanceSlot.Eyes, GenderAvailability.MaleOnly);
            try
            {
                string json = JsonUtility.ToJson(option);

                StringAssert.Contains("eye_001", json);
                Assert.AreEqual(AppearanceSlot.Eyes, option.Slot);
                Assert.AreEqual(GenderAvailability.MaleOnly, option.GenderAvailability);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(option);
            }
        }

        [Test]
        public void UsesAssetRefForPresentationNotUnityObjects()
        {
            foreach (FieldInfo field in typeof(AppearanceOptionDefinition).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    field.Name + " must be an address, not a direct asset reference.");
            }

            var option = NewOption("hair_002", AppearanceSlot.Hair, GenderAvailability.Any);
            try
            {
                Assert.AreEqual(typeof(AssetRef), option.Asset.GetType());
                Assert.AreEqual(typeof(AssetRef), option.PreviewIcon.GetType());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(option);
            }
        }

        [Test]
        public void WorksWithTheExistingRegistry()
        {
            var hair = NewOption("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            var face = NewOption("face_001", AppearanceSlot.Face, GenderAvailability.Any);
            try
            {
                var registry = new DefinitionRegistry<AppearanceOptionDefinition>();
                registry.Register(hair);
                registry.Register(face);

                Assert.AreEqual(2, registry.Count);
                Assert.IsTrue(registry.Contains(new DefinitionId("hair_001")));
                Assert.IsTrue(registry.TryGet(new DefinitionId("face_001"), out AppearanceOptionDefinition found));
                Assert.AreSame(face, found);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hair);
                UnityEngine.Object.DestroyImmediate(face);
            }
        }

        [Test]
        public void DuplicateIdFollowsExistingRegistryRules()
        {
            var first = NewOption("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            var second = NewOption("hair_001", AppearanceSlot.Hair, GenderAvailability.Any);
            try
            {
                var registry = new DefinitionRegistry<AppearanceOptionDefinition>();
                registry.Register(first);

                Assert.Throws<ArgumentException>(() => registry.Register(second));
                Assert.IsFalse(registry.TryRegister(second));
                Assert.AreEqual(1, registry.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void MissingIdIsDetectedByExistingValidation()
        {
            var option = ScriptableObject.CreateInstance<AppearanceOptionDefinition>();
            try
            {
                ValidationReport report = new DefinitionValidator()
                    .Validate(option, new DefinitionRegistry<AppearanceOptionDefinition>());

                Assert.IsFalse(report.IsValid);
                Assert.AreEqual(ValidationCode.MissingDefinitionId, report.Messages[0].Code);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(option);
            }
        }

        [Test]
        public void NoHardCodedContentCatalogueExists()
        {
            // Options are assets. Nothing in code may enumerate the shipped catalogue.
            string[] forbidden = { "Face01", "Hair01", "Eye01", "Skin01", "HairColor01" };

            foreach (string name in Enum.GetNames(typeof(AppearanceSlot)))
            {
                foreach (string content in forbidden)
                {
                    Assert.AreNotEqual(content, name);
                }
            }

            Assert.AreEqual(6, Enum.GetValues(typeof(AppearanceSlot)).Length,
                "AppearanceSlot must list slots, not content.");
        }
    }
}
