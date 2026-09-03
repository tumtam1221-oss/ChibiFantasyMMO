using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Content for the Devil Fruit, card and pet tests.
    /// </summary>
    /// <remarks>
    /// <b>Every number here is a fixture value.</b> Drop chances, experience curves, level
    /// requirements, immunity categories and stat modifiers are all authored on definitions,
    /// exactly as a designer or a database row would author them. Nothing in the services
    /// under test knows any of them -- which is the property these fixtures exist to prove,
    /// so a test that hard-coded a value would be testing the wrong thing.
    ///
    /// <b>Ten fruits, five cards, five pets.</b> The roster is deliberately larger than any
    /// one test needs, because "the eleventh fruit is content" is only demonstrated by ten
    /// that behave differently while sharing one code path. Darkness and Light carry the two
    /// distinct behaviours the brief names; the remaining eight are clearly marked test
    /// content with no invented lore.
    /// </remarks>
    internal abstract class CollectibleTestBase
    {
        protected DefinitionRegistry<ItemDefinition> Items;
        protected DefinitionRegistry<DevilFruitDefinition> Fruits;
        protected DefinitionRegistry<CardDefinition> Cards;
        protected DefinitionRegistry<PetDefinition> Pets;
        protected DefinitionRegistry<StatusEffectDefinition> Effects;
        protected DefinitionRegistry<SkillDefinition> Skills;
        protected DefinitionRegistry<DropTableDefinition> DropTables;
        protected DefinitionRegistry<MonsterDefinition> Monsters;

        protected OwnerId Owner;
        protected OwnerId Stranger;

        private List<Object> _created;

        // ---- stats and effects ---------------------------------------------------------

        protected const string Str = "stat.str";
        protected const string Vit = "stat.vit";
        protected const string MaxHp = "stat.maxhp";

        protected const string Silence = "effect.silence";      // Control / Silence
        protected const string Poison = "effect.poison";        // Debuff
        protected const string Might = "effect.might";          // Buff, +5 STR
        protected const string PetVigour = "effect.petvigour";  // Buff, +10 max HP
        protected const string PetGuard = "effect.petguard";    // Buff, +3 VIT

        protected const string DarkSkill = "skill.darkness";
        protected const string LightSkill = "skill.light";

        // ---- the ten devil fruits ------------------------------------------------------
        //
        // Fruit01 and Fruit02 are the two the brief names. The rest are test content.

        protected const string Darkness = "fruit.darkness";
        protected const string Light = "fruit.light";
        protected const string Fruit03 = "fruit.test03";
        protected const string Fruit04 = "fruit.test04";
        protected const string Fruit05 = "fruit.test05";
        protected const string Fruit06 = "fruit.test06";
        protected const string Fruit07 = "fruit.test07";
        protected const string Fruit08 = "fruit.test08";
        protected const string Fruit09 = "fruit.test09";
        protected const string Fruit10 = "fruit.test10";

        protected static readonly string[] AllFruits =
        {
            Darkness, Light, Fruit03, Fruit04, Fruit05,
            Fruit06, Fruit07, Fruit08, Fruit09, Fruit10
        };

        /// <summary>The item a player holds. Distinct from the power it grants.</summary>
        protected const string DarknessItem = "item.fruit.darkness";
        protected const string LightItem = "item.fruit.light";

        // ---- cards ---------------------------------------------------------------------

        protected const string StatCard = "card.stat";          // +5 STR, any equipment
        protected const string HpCard = "card.hp";              // +50 max HP, any equipment
        protected const string WeaponCard = "card.weapon";      // weapons only
        protected const string BossCard = "card.boss";          // ultra-low chance
        protected const string RankCard = "card.rank";          // damage vs WorldBoss

        // ---- pets ----------------------------------------------------------------------

        protected const string PetA = "pet.a";                  // grounded follower, +STR buff
        protected const string PetB = "pet.b";                  // floating follower, +VIT buff
        protected const string PetC = "pet.c";                  // evolves into the aura form
        protected const string PetCEvolved = "pet.c.evolved";   // aura form
        protected const string PetD = "pet.d";                  // evolves, costs a material
        protected const string PetDEvolved = "pet.d.evolved";   // follower, not an aura
        protected const string PetE = "pet.e";                  // never evolves

        protected const string EvolutionStone = "item.evolutionstone";

        // ---- equipment -----------------------------------------------------------------

        protected const string Sword = "equip.sword";           // MainHand, 2 card sockets
        protected const string Helm = "equip.helm";             // Head, 1 card socket
        protected const string Ring = "equip.ring";             // Accessory, no card sockets

        // ---- monsters and tables -------------------------------------------------------

        protected const string WorldBoss = "monster.worldboss";
        protected const string NormalMob = "monster.normal";

        protected const string BossTable = "drop.boss";
        protected const string MobTable = "drop.mob";

        /// <summary>0.00001% as the fraction the schema stores.</summary>
        protected const float FruitChance = 0.0000001f;

        /// <summary>0.0001% as the fraction the schema stores.</summary>
        protected const float CardChance = 0.000001f;

        [SetUp]
        public void SetUpCollectibleFixtures()
        {
            Items = new DefinitionRegistry<ItemDefinition>();
            Fruits = new DefinitionRegistry<DevilFruitDefinition>();
            Cards = new DefinitionRegistry<CardDefinition>();
            Pets = new DefinitionRegistry<PetDefinition>();
            Effects = new DefinitionRegistry<StatusEffectDefinition>();
            Skills = new DefinitionRegistry<SkillDefinition>();
            DropTables = new DefinitionRegistry<DropTableDefinition>();
            Monsters = new DefinitionRegistry<MonsterDefinition>();

            _created = new List<Object>();
            Owner = new OwnerId("account:test");
            Stranger = new OwnerId("account:other");

            AddEffect(Silence, StatusEffectCategory.Control, ControlEffectType.Silence, 8f);
            AddEffect(Poison, StatusEffectCategory.Debuff, ControlEffectType.None, 5f);
            AddEffect(Might, StatusEffectCategory.Buff, ControlEffectType.None, 30f,
                new[] { new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f) });
            AddEffect(PetVigour, StatusEffectCategory.Buff, ControlEffectType.None, 0f,
                new[] { new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, 10f) });
            AddEffect(PetGuard, StatusEffectCategory.Buff, ControlEffectType.None, 0f,
                new[] { new StatModifier(new DefinitionId(Vit), StatModifierKind.Flat, 3f) });

            AddSkill(DarkSkill);
            AddSkill(LightSkill);

            AuthorFruits();
            AuthorCards();
            AuthorPets();
            AuthorEquipment();
            AuthorDrops();
        }

        [TearDown]
        public void TearDownCollectibleFixtures()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        // ---- authoring: devil fruits ---------------------------------------------------

        /// <summary>
        /// The ten initial fruits, each a different configuration of the same schema.
        /// </summary>
        /// <remarks>
        /// Darkness inflicts silence; Light refuses every debuff. Neither is expressed as a
        /// name in code -- Darkness points at an effect whose control type is Silence, and
        /// Light lists the Debuff category among its refusals. The other eight vary the same
        /// four fields so that "ten fruits, one code path" is actually exercised.
        /// </remarks>
        private void AuthorFruits()
        {
            // Darkness: a granted control effect. The service never learns the word silence.
            AddFruit(Darkness, activeAbility: DarkSkill,
                grantedEffects: new[] { Silence },
                visual: "vfx/darkness", sound: "sfx/darkness");

            // Light: a category-wide refusal, so debuffs authored tomorrow are covered too.
            AddFruit(Light, activeAbility: LightSkill,
                immuneCategories: new[] { StatusEffectCategory.Debuff },
                visual: "vfx/light", sound: "sfx/light");

            // A passive only.
            AddFruit(Fruit03, passiveAbility: DarkSkill);

            // Stat modifiers only.
            AddFruit(Fruit04, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 12f)
            });

            // A named-effect immunity rather than a category one.
            AddFruit(Fruit05, immunities: new[] { Poison });

            // Grants a buff to its bearer.
            AddFruit(Fruit06, grantedEffects: new[] { Might });

            // Active and passive together.
            AddFruit(Fruit07, passiveAbility: DarkSkill, activeAbility: LightSkill);

            // A percentage modifier.
            AddFruit(Fruit08, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Vit), StatModifierKind.Percent, 0.1f)
            });

            // Two effects at once.
            AddFruit(Fruit09, grantedEffects: new[] { Might, Silence });

            // Turned off by content.
            AddFruit(Fruit10, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 1f)
            }, enabled: false);

            // The items that carry two of them. An item and a power are separate definitions
            // so an uneaten fruit stays an ordinary tradeable item.
            AddFruitItem(DarknessItem, Darkness);
            AddFruitItem(LightItem, Light);
        }

        // ---- authoring: cards ----------------------------------------------------------

        private void AuthorCards()
        {
            AddCard(StatCard, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 5f)
            });

            AddCard(HpCard, modifiers: new[]
            {
                new StatModifier(new DefinitionId(MaxHp), StatModifierKind.Flat, 50f)
            });

            AddCard(WeaponCard, category: EquipmentCategory.Weapon, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Percent, 0.05f)
            });

            AddCard(BossCard, modifiers: new[]
            {
                new StatModifier(new DefinitionId(Str), StatModifierKind.Flat, 20f)
            });

            AddCard(RankCard, effects: new[]
            {
                new CardEffect(CardEffectKind.DamageVersusRank, 0.2f, StatModifierKind.Percent,
                    MonsterRank.WorldBoss)
            });
        }

        // ---- authoring: pets -----------------------------------------------------------

        private void AuthorPets()
        {
            AddPet(PetA, buff: PetVigour, thresholds: new[] { 100, 300, 600 });

            AddPet(PetB, buff: PetGuard, thresholds: new[] { 50, 150 },
                behavior: PetFollowBehavior.Orbit, verticalOffset: 1.5f);

            // C evolves at level 3 into an aura form, free of charge.
            AddPet(PetC, buff: PetVigour, thresholds: new[] { 100, 200, 400 },
                stages: new[] { new PetEvolutionStage(new DefinitionId(PetCEvolved), 3) });

            AddPet(PetCEvolved, buff: PetGuard, thresholds: new[] { 1000 }, auraForm: true);

            // D evolves at level 2 and costs a material, and stays a follower.
            AddPet(PetD, buff: PetGuard, thresholds: new[] { 100, 200 },
                stages: new[]
                {
                    new PetEvolutionStage(new DefinitionId(PetDEvolved), 2,
                        requiredItem: new DefinitionId(EvolutionStone), requiredItemQuantity: 2)
                });

            AddPet(PetDEvolved, buff: PetVigour, thresholds: new[] { 500 });

            AddPet(PetE, buff: Might, thresholds: new[] { 10, 20, 30, 40 });

            AddItem(EvolutionStone, ItemCategory.Material, stackable: true, maxStack: 99);
        }

        private void AuthorEquipment()
        {
            AddEquipment(Sword, EquipmentSlot.MainHand, EquipmentCategory.Weapon,
                EquipmentSubtype.OneHandSword, cardSlots: 2, stoneSlots: 1);

            AddEquipment(Helm, EquipmentSlot.Head, EquipmentCategory.Armor,
                EquipmentSubtype.LightArmor, cardSlots: 1);

            AddEquipment(Ring, EquipmentSlot.Accessory, EquipmentCategory.Accessory,
                EquipmentSubtype.Accessory, cardSlots: 0);
        }

        /// <summary>
        /// The drop configuration.
        /// </summary>
        /// <remarks>
        /// The two ultra-rare chances live here and nowhere else. They are ordinary
        /// <see cref="DropEntry"/> rows on ordinary tables, indistinguishable to
        /// <c>DropResolver</c> from the guaranteed coin beside them.
        /// </remarks>
        private void AuthorDrops()
        {
            AddItem(StatCard, ItemCategory.Card, stackable: false, maxStack: 1);
            AddItem(HpCard, ItemCategory.Card, stackable: false, maxStack: 1);
            AddItem(WeaponCard, ItemCategory.Card, stackable: false, maxStack: 1);
            AddItem(BossCard, ItemCategory.Card, stackable: false, maxStack: 1);
            AddItem(RankCard, ItemCategory.Card, stackable: false, maxStack: 1);

            AddDropTable(BossTable, new[]
            {
                new DropEntry(new DefinitionId(DarknessItem), 1, 1, FruitChance),
                new DropEntry(new DefinitionId(BossCard), 1, 1, CardChance)
            });

            AddDropTable(MobTable, new[]
            {
                new DropEntry(new DefinitionId(StatCard), 1, 1, CardChance)
            });

            AddMonster(WorldBoss, MonsterRank.WorldBoss, BossTable);
            AddMonster(NormalMob, MonsterRank.Normal, MobTable);
        }

        // ---- helpers -------------------------------------------------------------------

        protected DevilFruitDefinition AddFruit(string id, string passiveAbility = null,
            string activeAbility = null, string[] grantedEffects = null, string[] immunities = null,
            StatusEffectCategory[] immuneCategories = null, StatModifier[] modifiers = null,
            string visual = null, string sound = null, bool enabled = true)
        {
            var definition = ScriptableObject.CreateInstance<DevilFruitDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_descriptionKey\":{\"_key\":\"" + id + ".desc\"},"
                + "\"_passiveAbility\":{\"_value\":\"" + (passiveAbility ?? string.Empty) + "\"},"
                + "\"_activeAbility\":{\"_value\":\"" + (activeAbility ?? string.Empty) + "\"},"
                + "\"_visualEffect\":{\"_address\":\"" + (visual ?? string.Empty) + "\"},"
                + "\"_soundEffect\":{\"_address\":\"" + (sound ?? string.Empty) + "\"},"
                + "\"_disabled\":" + (enabled ? "false" : "true") + "}", definition);

            SetPrivate(definition, "_grantedEffects", Ids(grantedEffects));
            SetPrivate(definition, "_immunities", Ids(immunities));
            SetPrivate(definition, "_immuneCategories",
                immuneCategories ?? new StatusEffectCategory[0]);
            SetPrivate(definition, "_statModifiers", modifiers ?? new StatModifier[0]);

            Track(definition);
            Fruits.Register(definition);
            return definition;
        }

        /// <summary>Authors the item that carries a fruit, as a normal usable item.</summary>
        protected ItemDefinition AddFruitItem(string id, string fruit)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.DevilFruit
                + ",\"_usable\":true,\"_tradable\":true"
                + ",\"_useType\":" + (int)ItemUseType.DevilFruit
                + ",\"_useTarget\":" + (int)ItemUseTarget.Self + "}", definition);

            SetPrivate(definition, "_useEffects", new[]
            {
                new ItemUseEffect(ItemEffectKind.ConsumeDevilFruit,
                    devilFruit: new DefinitionId(fruit))
            });

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        /// <summary>Authors an item that turns into a pet when used.</summary>
        protected ItemDefinition AddPetItem(string id, string pet)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":true,\"_maxStackSize\":99,"
                + "\"_category\":" + (int)ItemCategory.Consumable
                + ",\"_usable\":true"
                + ",\"_useType\":" + (int)ItemUseType.PetTame
                + ",\"_useTarget\":" + (int)ItemUseTarget.Self + "}", definition);

            SetPrivate(definition, "_useEffects", new[]
            {
                new ItemUseEffect(ItemEffectKind.GrantPet, pet: new DefinitionId(pet))
            });

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        protected CardDefinition AddCard(string id, StatModifier[] modifiers = null,
            CardEffect[] effects = null, EquipmentCategory category = EquipmentCategory.None,
            EquipmentSlot slot = EquipmentSlot.None, int maxPerEquipment = 1,
            bool enabled = true, string sourceMonster = null)
        {
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_sourceMonster\":{\"_value\":\"" + (sourceMonster ?? string.Empty) + "\"},"
                + "\"_allowedSlot\":" + (int)slot
                + ",\"_allowedCategory\":" + (int)category
                + ",\"_maxPerEquipment\":" + maxPerEquipment
                + ",\"_disabled\":" + (enabled ? "false" : "true") + "}", definition);

            SetPrivate(definition, "_statModifiers", modifiers ?? new StatModifier[0]);
            SetPrivate(definition, "_effects", effects ?? new CardEffect[0]);

            Track(definition);
            Cards.Register(definition);
            return definition;
        }

        protected PetDefinition AddPet(string id, string buff = null, int[] thresholds = null,
            PetEvolutionStage[] stages = null, PetFollowBehavior behavior = PetFollowBehavior.Follow,
            float verticalOffset = 0f, bool auraForm = false, bool enabled = true, int maxLevel = 0)
        {
            var definition = ScriptableObject.CreateInstance<PetDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_baseBuff\":{\"_value\":\"" + (buff ?? string.Empty) + "\"},"
                + "\"_icon\":{\"_address\":\"icon/" + id + "\"},"
                + "\"_model\":{\"_address\":\"model/" + id + "\"},"
                + "\"_followBehavior\":" + (int)behavior
                + ",\"_verticalOffset\":" + F(verticalOffset)
                + ",\"_maxLevel\":" + maxLevel
                + ",\"_auraForm\":" + (auraForm ? "true" : "false")
                + ",\"_disabled\":" + (enabled ? "false" : "true") + "}", definition);

            SetPrivate(definition, "_experienceThresholds", thresholds ?? new int[0]);
            SetPrivate(definition, "_evolutionStages", stages ?? new PetEvolutionStage[0]);

            Track(definition);
            Pets.Register(definition);
            return definition;
        }

        protected ItemDefinition AddItem(string id, ItemCategory category, bool stackable,
            int maxStack)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":" + (stackable ? "true" : "false")
                + ",\"_maxStackSize\":" + maxStack
                + ",\"_tradable\":true"
                + ",\"_category\":" + (int)category + "}", definition);

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        protected EquipmentDefinition AddEquipment(string id, EquipmentSlot slot,
            EquipmentCategory category, EquipmentSubtype subtype, int cardSlots = 0,
            int stoneSlots = 0, StatModifier[] modifiers = null)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.Equipment
                + ",\"_slot\":" + (int)slot
                + ",\"_equipmentCategory\":" + (int)category
                + ",\"_subtype\":" + (int)subtype
                + ",\"_cardSlots\":" + cardSlots
                + ",\"_statusStoneSlots\":" + stoneSlots + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_baseStatModifiers", modifiers);

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        protected StatusEffectDefinition AddEffect(string id, StatusEffectCategory category,
            ControlEffectType control, float duration, StatModifier[] modifiers = null,
            int maxStacks = 1,
            StatusEffectStackBehavior stacking = StatusEffectStackBehavior.RefreshDuration)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_category\":" + (int)category
                + ",\"_controlEffect\":" + (int)control
                + ",\"_durationSeconds\":" + F(duration)
                + ",\"_stackBehavior\":" + (int)stacking
                + ",\"_maxStacks\":" + maxStacks + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_statModifiers", modifiers);

            Track(definition);
            Effects.Register(definition);
            return definition;
        }

        protected SkillDefinition AddSkill(string id)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"}}",
                definition);

            Track(definition);
            Skills.Register(definition);
            return definition;
        }

        protected DropTableDefinition AddDropTable(string id, DropEntry[] entries,
            int maxEntries = 0)
        {
            var definition = ScriptableObject.CreateInstance<DropTableDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxEntries\":" + maxEntries + "}",
                definition);

            SetPrivate(definition, "_entries", entries ?? new DropEntry[0]);

            Track(definition);
            DropTables.Register(definition);
            return definition;
        }

        protected MonsterDefinition AddMonster(string id, MonsterRank rank, string lootTable)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_rank\":" + (int)rank
                + ",\"_lootTable\":{\"_value\":\"" + (lootTable ?? string.Empty) + "\"}}",
                definition);

            Track(definition);
            Monsters.Register(definition);
            return definition;
        }

        // ---- convenience ---------------------------------------------------------------

        protected ItemContainerState Container(int capacity = 10)
        {
            return new ItemContainerState(Owner, capacity);
        }

        protected ItemInstance Stack(string id, int quantity = 1, OwnerId? owner = null)
        {
            return new ItemInstance(InstanceId.New(), new DefinitionId(id),
                owner ?? Owner, quantity);
        }

        protected EquipmentInstance Equipment(string id, OwnerId? owner = null)
        {
            return new EquipmentInstance(InstanceId.New(), new DefinitionId(id), owner ?? Owner);
        }

        protected PetInstance Pet(string id, OwnerId? owner = null)
        {
            return new PetInstance(InstanceId.New(), new DefinitionId(id), owner ?? Owner);
        }

        protected DevilFruitService.Context FruitContext(StatusEffectRuntimeState status = null,
            OwnerId owner = default)
        {
            return new DevilFruitService.Context(Fruits, status, Effects, Skills, owner);
        }

        protected CardSocketService.Context CardContext(OwnerId owner = default)
        {
            return new CardSocketService.Context(Items, Cards, null, owner);
        }

        protected PetService.Context PetContext(StatusEffectRuntimeState status = null,
            OwnerId owner = default)
        {
            return new PetService.Context(Pets, Items, Effects, status, owner);
        }

        protected DropResolver.Context DropContext(IRandomResultSource results = null,
            IRandomRangeSource ranges = null, int killerLevel = 0)
        {
            return new DropResolver.Context(Items, DropTables, results, ranges, killerLevel);
        }

        protected static DefinitionId[] Ids(string[] values)
        {
            if (values == null) return new DefinitionId[0];

            var ids = new DefinitionId[values.Length];
            for (int i = 0; i < values.Length; i++) ids[i] = new DefinitionId(values[i]);
            return ids;
        }

        protected static string F(float value)
        {
            return value.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
        }

        protected void Track(Object created)
        {
            _created.Add(created);
        }

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
    }
}
