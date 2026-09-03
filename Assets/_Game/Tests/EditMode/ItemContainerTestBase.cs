using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared fixtures for inventory, equipment and storage.
    /// </summary>
    /// <remarks>
    /// Every item and every stack size here is a TEST FIXTURE authored exactly as content
    /// would be. No item id, stack size or slot appears in any runtime type.
    /// </remarks>
    internal abstract class ItemContainerTestBase
    {
        protected DefinitionRegistry<ItemDefinition> Items;
        protected DefinitionRegistry<StatusEffectDefinition> StatusEffects;
        protected DefinitionRegistry<MapDefinition> Maps;
        protected OwnerId Owner;
        private List<Object> _created;

        protected const string Potion = "item.potion";       // stackable, max 99
        protected const string Ore = "item.ore";             // stackable, max 10
        protected const string Rock = "item.rock";           // non-stackable
        protected const string Sword = "equip.sword";        // MainHand, +10 STR
        protected const string Helm = "equip.helm";          // Head, +5 STR
        protected const string Robe = "equip.robe";          // Body, level 20
        protected const string ClassOnly = "equip.classonly";// Body, class-restricted

        protected const string Str = "stat.str";
        protected const string ClassA = "class.a";
        protected const string ClassB = "class.b";

        [SetUp]
        public void SetUp()
        {
            Items = new DefinitionRegistry<ItemDefinition>();
            StatusEffects = new DefinitionRegistry<StatusEffectDefinition>();
            Maps = new DefinitionRegistry<MapDefinition>();
            _created = new List<Object>();
            Owner = new OwnerId("account:test");

            AddItem(Potion, stackable: true, maxStack: 99);
            AddItem(Ore, stackable: true, maxStack: 10);
            AddItem(Rock, stackable: false, maxStack: 1);

            AddEquipment(Sword, EquipmentSlot.MainHand, level: 0,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 10f) });
            AddEquipment(Helm, EquipmentSlot.Head, level: 0,
                modifiers: new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) });
            AddEquipment(Robe, EquipmentSlot.Body, level: 20);
            AddEquipment(ClassOnly, EquipmentSlot.Body, level: 0,
                allowedClasses: new[] { new DefinitionId(ClassA) });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        protected ItemDefinition AddItem(string id, bool stackable, int maxStack)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":" + (stackable ? "true" : "false")
                + ",\"_maxStackSize\":" + maxStack + ",\"_category\":" + (int)ItemCategory.Consumable + "}",
                definition);
            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        protected EquipmentDefinition AddEquipment(string id, EquipmentSlot slot, int level,
            StatModifier[] modifiers = null, DefinitionId[] allowedClasses = null,
            DefinitionId[] allowedJobs = null)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.Equipment + ",\"_slot\":" + (int)slot
                + ",\"_levelRequirement\":" + level + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_baseStatModifiers", modifiers);
            if (allowedClasses != null) SetPrivate(definition, "_allowedClasses", allowedClasses);
            if (allowedJobs != null) SetPrivate(definition, "_allowedJobs", allowedJobs);

            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        /// <summary>
        /// Authors a usable item exactly as content would.
        /// </summary>
        /// <remarks>
        /// Every figure -- the amount, the duration, the destination -- is a FIXTURE VALUE
        /// set on the definition. Nothing in <c>ItemUseService</c> knows any of them, which
        /// is the property these tests exist to hold.
        /// </remarks>
        protected ItemDefinition AddUsable(string id, ItemUseType useType,
            ItemUseEffect[] effects, bool usable = true, bool stackable = true,
            int maxStack = 99, ItemUseTarget target = ItemUseTarget.Self)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":" + (stackable ? "true" : "false")
                + ",\"_maxStackSize\":" + maxStack + ",\"_category\":" + (int)ItemCategory.Consumable
                + ",\"_usable\":" + (usable ? "true" : "false")
                + ",\"_useType\":" + (int)useType
                + ",\"_useTarget\":" + (int)target + "}",
                definition);

            SetPrivate(definition, "_useEffects", effects ?? new ItemUseEffect[0]);

            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        /// <summary>Authors a status effect a buff item can grant.</summary>
        protected StatusEffectDefinition AddStatusEffect(string id, float duration,
            StatModifier[] modifiers = null, int maxStacks = 1)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_category\":" + (int)StatusEffectCategory.Buff
                + ",\"_durationSeconds\":" + duration.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"_maxStacks\":" + maxStacks + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_statModifiers", modifiers);

            _created.Add(definition);
            StatusEffects.Register(definition);
            return definition;
        }

        /// <summary>Authors a map a warp scroll can point at.</summary>
        protected MapDefinition AddMap(string id, MapCategory category, bool isTown,
            bool isBossArea = false, string nameKey = null)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_category\":" + (int)category
                + ",\"_isTown\":" + (isTown ? "true" : "false")
                + ",\"_isBossArea\":" + (isBossArea ? "true" : "false")
                + ",\"_nameKey\":{\"_key\":\"" + (nameKey ?? id + ".name") + "\"}}", definition);

            _created.Add(definition);
            Maps.Register(definition);
            return definition;
        }

        protected static void SetPrivate(Object target, string field, object value)
        {
            target.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        protected ItemContainerState Container(int capacity) =>
            new ItemContainerState(Owner, capacity);

        protected ItemInstance Stack(string id, int quantity) =>
            new ItemInstance(InstanceId.New(), new DefinitionId(id), Owner, quantity);

        protected EquipmentInstance Gear(string id) =>
            new EquipmentInstance(InstanceId.New(), new DefinitionId(id), Owner);

        /// <summary>A compact picture of a container, so a test can assert the whole shape.</summary>
        protected static string Describe(ItemContainerState container)
        {
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < container.Capacity; i++)
            {
                ItemSlot slot = container.GetSlot(i);
                if (i > 0) sb.Append('|');
                sb.Append(slot.IsEmpty ? "-" : slot.DefinitionId + "x" + slot.Quantity);
            }

            return sb.ToString();
        }
    }
}
