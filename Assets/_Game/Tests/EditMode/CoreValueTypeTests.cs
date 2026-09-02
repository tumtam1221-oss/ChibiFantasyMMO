using System;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class CoreValueTypeTests
    {
        [Test]
        public void LocalizationKey_EqualityAndValidity()
        {
            Assert.AreEqual(new LocalizationKey("ui.ok"), new LocalizationKey("ui.ok"));
            Assert.AreNotEqual(new LocalizationKey("ui.ok"), new LocalizationKey("ui.cancel"));
            Assert.IsTrue(new LocalizationKey("ui.ok") == new LocalizationKey("ui.ok"));
            Assert.IsTrue(new LocalizationKey("ui.ok") != new LocalizationKey("ui.no"));
            Assert.IsFalse(LocalizationKey.None.IsValid);
            Assert.IsFalse(new LocalizationKey("  ").IsValid);
            Assert.IsTrue(new LocalizationKey("item.potion.name").IsValid);
        }

        [Test]
        public void AssetRef_EqualityAndValidity()
        {
            Assert.AreEqual(new AssetRef("icons/potion"), new AssetRef("icons/potion"));
            Assert.AreNotEqual(new AssetRef("icons/potion"), new AssetRef("icons/elixir"));
            Assert.IsFalse(AssetRef.None.IsValid);
            Assert.IsTrue(new AssetRef("models/slime").IsValid);
            Assert.AreEqual("models/slime", new AssetRef("models/slime").Address);
        }

        [Test]
        public void ValueTypes_SurviveUnitySerializationRoundTrip()
        {
            var original = new ValueTypeHolder
            {
                Key = new LocalizationKey("class.mage.name"),
                Asset = new AssetRef("icons/mage"),
                Stat = new StatValue(new DefinitionId("stat.int"), 12f),
                Modifier = new StatModifier(new DefinitionId("stat.str"), StatModifierKind.Percent, 0.15f)
            };

            string json = JsonUtility.ToJson(original);
            ValueTypeHolder restored = JsonUtility.FromJson<ValueTypeHolder>(json);

            Assert.AreEqual(original.Key, restored.Key);
            Assert.AreEqual(original.Asset, restored.Asset);
            Assert.AreEqual(new DefinitionId("stat.int"), restored.Stat.Stat);
            Assert.AreEqual(12f, restored.Stat.Value);
            Assert.AreEqual(new DefinitionId("stat.str"), restored.Modifier.Stat);
            Assert.AreEqual(StatModifierKind.Percent, restored.Modifier.Kind);
            Assert.AreEqual(0.15f, restored.Modifier.Value, 0.0001f);
        }

        [Test]
        public void StatModifier_DefaultKindIsFlat()
        {
            var modifier = new StatModifier(new DefinitionId("stat.vit"), StatModifierKind.Flat, 5f);

            Assert.AreEqual(StatModifierKind.Flat, modifier.Kind);
            Assert.AreEqual(0, (int)StatModifierKind.Flat);
        }
    }

    [Serializable]
    internal sealed class ValueTypeHolder
    {
        public LocalizationKey Key;
        public AssetRef Asset;
        public StatValue Stat;
        public StatModifier Modifier;
    }
}
