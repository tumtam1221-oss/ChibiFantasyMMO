using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Proves each effect kind can be described. Every number is a test fixture; no skill
    /// content is authored.
    /// </summary>
    internal sealed class SkillEffectContractTests
    {
        private static readonly DefinitionId Str = new DefinitionId("stat.str");
        private static readonly DefinitionId Burning = new DefinitionId("status.burning");

        [Test]
        public void DamageCarriesAmountElementAndScaling()
        {
            SkillEffect effect = SkillEffect.Damage(50, ElementType.Fire,
                new[] { new StatTerm(Str, 3, 2) });

            Assert.AreEqual(SkillEffectKind.Damage, effect.Kind);
            Assert.AreEqual(50, effect.FlatAmount);
            Assert.AreEqual(ElementType.Fire, effect.Element);
            Assert.AreEqual(1, effect.Scaling.Length);
            Assert.AreEqual(Str, effect.Scaling[0].Source);
            Assert.AreEqual(3, effect.Scaling[0].Numerator);
            Assert.AreEqual(2, effect.Scaling[0].Denominator);
        }

        [Test]
        public void HealCarriesAmountAndResource()
        {
            SkillEffect effect = SkillEffect.Heal(30, SkillResourceType.Health);

            Assert.AreEqual(SkillEffectKind.Heal, effect.Kind);
            Assert.AreEqual(30, effect.FlatAmount);
            Assert.AreEqual(SkillResourceType.Health, effect.Resource);
            Assert.AreEqual(0, effect.Scaling.Length, "Scaling is optional, never null.");
        }

        [Test]
        public void ApplyStatusEffectCarriesOnlyItsReference()
        {
            SkillEffect effect = SkillEffect.ApplyStatusEffect(Burning);

            Assert.AreEqual(SkillEffectKind.ApplyStatusEffect, effect.Kind);
            Assert.AreEqual(Burning, effect.Reference);
            Assert.AreEqual(0, effect.FlatAmount,
                "Duration and stacking are authored on the status effect, not restated here.");
        }

        [Test]
        public void ModifyStatCarriesAnExistingStatModifier()
        {
            var modifier = new StatModifier(Str, StatModifierKind.Percent, 0.25f);
            SkillEffect effect = SkillEffect.ModifyStat(modifier);

            Assert.AreEqual(SkillEffectKind.ModifyStat, effect.Kind);
            Assert.AreEqual(Str, effect.StatModifier.Stat);
            Assert.AreEqual(StatModifierKind.Percent, effect.StatModifier.Kind);
            Assert.AreEqual(0.25f, effect.StatModifier.Value, 0.0001f);
        }

        [Test]
        public void ModifyResourceCarriesResourceAndAmount()
        {
            SkillEffect effect = SkillEffect.ModifyResource(SkillResourceType.Mana, 15);

            Assert.AreEqual(SkillEffectKind.ModifyResource, effect.Kind);
            Assert.AreEqual(SkillResourceType.Mana, effect.Resource);
            Assert.AreEqual(15, effect.FlatAmount);
        }

        [Test]
        public void EverySupportedKindIsRepresentable()
        {
            var effects = new[]
            {
                SkillEffect.Damage(1, ElementType.Neutral),
                SkillEffect.Heal(1, SkillResourceType.Health),
                SkillEffect.ApplyStatusEffect(Burning),
                SkillEffect.ModifyStat(new StatModifier(Str, StatModifierKind.Flat, 1f)),
                SkillEffect.ModifyResource(SkillResourceType.Mana, 1)
            };

            foreach (SkillEffect effect in effects)
            {
                Assert.AreNotEqual(SkillEffectKind.None, effect.Kind);
            }

            Assert.AreEqual(6, Enum.GetValues(typeof(SkillEffectKind)).Length,
                "Five kinds plus None; adding a sixth must not require touching existing data.");
        }

        [Test]
        public void AmountsAreIntegersAndScalingIsRational()
        {
            FieldInfo flat = typeof(SkillEffect).GetField(
                "_flatAmount", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.AreEqual(typeof(int), flat.FieldType,
                "Damage, healing and resource changes are counted, so they must not drift.");
            Assert.AreEqual(typeof(int),
                typeof(StatTerm).GetField("_numerator", BindingFlags.Instance | BindingFlags.NonPublic)
                    .FieldType);
            Assert.AreEqual(typeof(int),
                typeof(StatTerm).GetField("_denominator", BindingFlags.Instance | BindingFlags.NonPublic)
                    .FieldType);
        }

        [Test]
        public void EffectsSurviveSerializationWithReferencesIntact()
        {
            var holder = new SkillEffectHolder
            {
                Effects = new[]
                {
                    SkillEffect.Damage(50, ElementType.Water, new[] { new StatTerm(Str, 3, 2) }),
                    SkillEffect.ApplyStatusEffect(Burning),
                    SkillEffect.ModifyStat(new StatModifier(Str, StatModifierKind.Percent, 0.25f)),
                    SkillEffect.ModifyResource(SkillResourceType.Mana, 15)
                }
            };

            string json = JsonUtility.ToJson(holder);
            SkillEffectHolder restored = JsonUtility.FromJson<SkillEffectHolder>(json);

            Assert.AreEqual(4, restored.Effects.Length);
            Assert.AreEqual(ElementType.Water, restored.Effects[0].Element);
            Assert.AreEqual(50, restored.Effects[0].FlatAmount);
            Assert.AreEqual(Str, restored.Effects[0].Scaling[0].Source);
            Assert.AreEqual(Burning, restored.Effects[1].Reference);
            Assert.AreEqual(Str, restored.Effects[2].StatModifier.Stat);
            Assert.AreEqual(SkillResourceType.Mana, restored.Effects[3].Resource);
            Assert.AreEqual(json, JsonUtility.ToJson(restored), "Serialization must be stable.");
        }

        [Test]
        public void TheContractCarriesNoUnityOrPresentationDependency()
        {
            foreach (FieldInfo field in typeof(SkillEffect).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    field.Name + " must not be a Unity object.");
                Assert.AreNotEqual(typeof(AssetRef), field.FieldType,
                    "Presentation lives on the skill, not on what it does to someone.");
            }

            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(SkillEffect)));
        }

        [Test]
        public void ReusesExistingTypesRatherThanDuplicatingThem()
        {
            Assert.AreEqual(typeof(StatTerm),
                typeof(SkillEffect).GetProperty("Scaling").PropertyType.GetElementType());
            Assert.AreEqual(typeof(StatModifier),
                typeof(SkillEffect).GetProperty("StatModifier").PropertyType);
            Assert.AreEqual(typeof(ElementType),
                typeof(SkillEffect).GetProperty("Element").PropertyType);
            Assert.AreEqual(typeof(SkillResourceType),
                typeof(SkillEffect).GetProperty("Resource").PropertyType);
            Assert.AreEqual(typeof(DefinitionId),
                typeof(SkillEffect).GetProperty("Reference").PropertyType);
        }
    }

    [Serializable]
    internal sealed class SkillEffectHolder
    {
        public SkillEffect[] Effects;
    }
}
