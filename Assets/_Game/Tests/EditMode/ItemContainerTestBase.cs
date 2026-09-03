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
        protected DefinitionRegistry<SpawnPointDefinition> SpawnPoints;
        protected DefinitionRegistry<RarityDefinition> Rarities;
        protected DefinitionRegistry<EnhancementDefinition> Enhancements;
        protected DefinitionRegistry<StoneFusionDefinition> FusionRecipes;
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
            SpawnPoints = new DefinitionRegistry<SpawnPointDefinition>();
            Rarities = new DefinitionRegistry<RarityDefinition>();
            Enhancements = new DefinitionRegistry<EnhancementDefinition>();
            FusionRecipes = new DefinitionRegistry<StoneFusionDefinition>();
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

        /// <summary>
        /// Authors a place on a map.
        /// </summary>
        /// <remarks>A warp destination needs one: a town with nowhere to stand is refused
        /// rather than warped to, so the town fixtures author theirs explicitly.</remarks>
        protected SpawnPointDefinition AddSpawnPoint(string id, string map,
            SpawnType type = SpawnType.Player, float x = 0f, float y = 0f, float z = 0f)
        {
            var definition = ScriptableObject.CreateInstance<SpawnPointDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)type
                + ",\"_x\":" + F(x) + ",\"_y\":" + F(y) + ",\"_z\":" + F(z) + "}", definition);

            _created.Add(definition);
            SpawnPoints.Register(definition);
            return definition;
        }

        /// <summary>Invariant-culture float, so a comma locale cannot break the JSON.</summary>
        protected static string F(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        // ---- PHASE 09 fixtures: equipment progression ----------------------------------
        // Every number below is authored on a definition. No service knows any of them.

        /// <summary>Authors a rarity tier.</summary>
        protected RarityDefinition AddRarity(string id, int order,
            StatModifier[] modifiers = null, int bonusSlots = 0, int maxEnhancement = 0)
        {
            var definition = ScriptableObject.CreateInstance<RarityDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_order\":" + order
                + ",\"_bonusEnchantSlots\":" + bonusSlots
                + ",\"_maxEnhancementLevel\":" + maxEnhancement + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_statModifiers", modifiers);

            _created.Add(definition);
            Rarities.Register(definition);
            return definition;
        }

        /// <summary>Authors an enhancement track from a list of steps.</summary>
        protected EnhancementDefinition AddEnhancementRule(string id, int maxLevel,
            EnhancementStep[] steps)
        {
            var definition = ScriptableObject.CreateInstance<EnhancementDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_minLevel\":0,\"_maxLevel\":" + maxLevel + "}", definition);

            SetPrivate(definition, "_steps", steps ?? new EnhancementStep[0]);

            _created.Add(definition);
            Enhancements.Register(definition);
            return definition;
        }

        /// <summary>
        /// Builds one authored enhancement step.
        /// </summary>
        /// <remarks><see cref="EnhancementStep"/> has only private serialized fields and no
        /// constructor, so a fixture is filled the same way Unity would fill it.</remarks>
        protected static EnhancementStep Step(int fromLevel, float successChance,
            StatModifier[] granted = null, string materialItem = null, int materialAmount = 0,
            int currencyCost = 0,
            EnhancementFailureBehavior failure = EnhancementFailureBehavior.LoseMaterials)
        {
            object boxed = new EnhancementStep();
            SetStructField(ref boxed, "_fromLevel", fromLevel);
            SetStructField(ref boxed, "_successChance", successChance);
            SetStructField(ref boxed, "_failureBehavior", failure);
            SetStructField(ref boxed, "_grantedModifiers", granted ?? new StatModifier[0]);
            SetStructField(ref boxed, "_materialItem",
                materialItem == null ? default(DefinitionId) : new DefinitionId(materialItem));
            SetStructField(ref boxed, "_materialAmount", materialAmount);
            SetStructField(ref boxed, "_currencyCost", currencyCost);
            return (EnhancementStep)boxed;
        }

        /// <summary>Authors a status stone as an ordinary inventory item.</summary>
        protected ItemDefinition AddStone(string id, StatModifier[] modifiers,
            float successChance = 0f, int maxPerEquipment = 1, bool fusable = true,
            EquipmentCategory category = EquipmentCategory.None,
            EquipmentSlot[] slots = null, DefinitionId[] rarities = null,
            EnchantFailureBehavior failure = EnchantFailureBehavior.LoseStone,
            int maxStack = 99)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":true,\"_maxStackSize\":" + maxStack
                + ",\"_category\":" + (int)ItemCategory.StatusStone + "}", definition);

            object config = new StatusStoneConfig();
            SetStructField(ref config, "_statModifiers", modifiers ?? new StatModifier[0]);
            SetStructField(ref config, "_successChance", successChance);
            SetStructField(ref config, "_failureBehavior", failure);
            SetStructField(ref config, "_allowedCategory", category);
            SetStructField(ref config, "_allowedSubtypes", new EquipmentSubtype[0]);
            SetStructField(ref config, "_allowedSlots", slots ?? new EquipmentSlot[0]);
            SetStructField(ref config, "_allowedRarities", rarities ?? new DefinitionId[0]);
            SetStructField(ref config, "_minimumItemLevel", 0);
            SetStructField(ref config, "_maxPerEquipment", maxPerEquipment);
            SetStructField(ref config, "_fusable", fusable);

            SetPrivate(definition, "_stoneConfig", config);

            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        /// <summary>Authors a fusion recipe.</summary>
        protected StoneFusionDefinition AddFusionRecipe(string id, FusionIngredient[] inputs,
            string result, int resultQuantity = 1, float successChance = 0f,
            string failureResult = null, int failureResultQuantity = 1,
            bool consumeOnFailure = true, int currencyCost = 0, string currencyItem = null)
        {
            var definition = ScriptableObject.CreateInstance<StoneFusionDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_result\":{\"_value\":\"" + result + "\"}"
                + ",\"_resultQuantity\":" + resultQuantity
                + ",\"_successChance\":" + successChance.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"_failureResult\":{\"_value\":\"" + (failureResult ?? string.Empty) + "\"}"
                + ",\"_failureResultQuantity\":" + failureResultQuantity
                + ",\"_consumeInputsOnFailure\":" + (consumeOnFailure ? "true" : "false")
                + ",\"_currencyCost\":" + currencyCost
                + ",\"_currencyItem\":{\"_value\":\"" + (currencyItem ?? string.Empty) + "\"}}",
                definition);

            SetPrivate(definition, "_inputs", inputs ?? new FusionIngredient[0]);

            _created.Add(definition);
            FusionRecipes.Register(definition);
            return definition;
        }

        /// <summary>Sets a private serialized field on a boxed struct.</summary>
        protected static void SetStructField(ref object boxed, string field, object value)
        {
            boxed.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic)
                .SetValue(boxed, value);
        }

        /// <summary>The registries every equipment-progression service needs.</summary>
        protected EquipmentModifierResolver.Context ResolverContext()
        {
            return new EquipmentModifierResolver.Context(Items, Rarities, Enhancements);
        }

        /// <summary>
        /// Sets a private serialized field, including one declared on a base type.
        /// </summary>
        /// <remarks>Walks the hierarchy because <c>GetField</c> with <c>NonPublic</c> does
        /// not see a base class's private fields -- and <see cref="EquipmentDefinition"/>
        /// inherits several from <see cref="ItemDefinition"/>, rarity among them.</remarks>
        protected static void SetPrivate(Object target, string field, object value)
        {
            System.Type type = target.GetType();

            while (type != null)
            {
                System.Reflection.FieldInfo info = type.GetField(field,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);

                if (info != null)
                {
                    info.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new System.ArgumentException(
                "No field '" + field + "' on " + target.GetType().Name, "field");
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
