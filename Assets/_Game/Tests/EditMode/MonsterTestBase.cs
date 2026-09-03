using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared fixtures for monsters, drops, loot and quests.
    /// </summary>
    /// <remarks>
    /// Every range, stat, chance and reward here is a TEST FIXTURE authored exactly as
    /// content would be. No monster id, drop rate or quest target appears in any runtime
    /// type -- which is the property most of these suites exist to hold.
    /// </remarks>
    internal abstract class MonsterTestBase
    {
        protected DefinitionRegistry<MonsterDefinition> Monsters;
        protected DefinitionRegistry<ItemDefinition> Items;
        protected DefinitionRegistry<DropTableDefinition> DropTables;
        protected DefinitionRegistry<QuestDefinition> Quests;
        protected DefinitionRegistry<MapDefinition> Maps;

        protected OwnerId Owner;
        protected CharacterId Character;
        protected CombatTeam Players;
        protected CombatTeam Enemies;

        private List<Object> _created;

        protected const string MaxHp = "stat.maxhp";
        protected const string Atk = "stat.atk";
        protected const string Def = "stat.def";

        protected const string Grunt = "monster.grunt";      // aggressive, detect 10, reach 2
        protected const string Docile = "monster.docile";    // passive
        protected const string Bound = "monster.bound";      // map-restricted
        protected const string Slow = "monster.slow";        // ten-second respawn
        protected const string Hollow = "monster.hollow";    // no authored health

        protected const string HomeMap = "map.home";
        protected const string OtherMap = "map.other";

        protected const string Coin = "item.coin";
        protected const string Hide = "item.hide";
        protected const string Relic = "item.relic";

        [SetUp]
        public void SetUpMonsterFixtures()
        {
            Monsters = new DefinitionRegistry<MonsterDefinition>();
            Items = new DefinitionRegistry<ItemDefinition>();
            DropTables = new DefinitionRegistry<DropTableDefinition>();
            Quests = new DefinitionRegistry<QuestDefinition>();
            Maps = new DefinitionRegistry<MapDefinition>();

            _created = new List<Object>();
            Owner = new OwnerId("account:test");
            Character = new CharacterId("char:test");
            Players = new CombatTeam(1);
            Enemies = new CombatTeam(2);

            AddMap(HomeMap);
            AddMap(OtherMap);

            AddItem(Coin, maxStack: 999);
            AddItem(Hide, maxStack: 99);
            AddItem(Relic, maxStack: 1);

            AddMonster(Grunt, level: 5, experience: 50,
                aggression: MonsterAggressionType.Aggressive,
                detection: 10f, attackRange: 2f, cooldown: 2f, leash: 15f);

            AddMonster(Docile, level: 3, experience: 10,
                aggression: MonsterAggressionType.Passive,
                detection: 10f, attackRange: 2f);

            AddMonster(Bound, level: 5, aggression: MonsterAggressionType.Aggressive,
                allowedMaps: new[] { new DefinitionId(HomeMap) });

            AddMonster(Slow, level: 5, respawnDelay: 10f);
        }

        [TearDown]
        public void TearDownMonsterFixtures()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        // ---- authoring -----------------------------------------------------------------

        protected MonsterDefinition AddMonster(string id, int level = 1, int experience = 0,
            int currency = 0, MonsterAggressionType aggression = MonsterAggressionType.Passive,
            float detection = 0f, float attackRange = 1.5f, float cooldown = 2f,
            float leash = 0f, float respawnDelay = 0f, string lootTable = null,
            DefinitionId[] allowedMaps = null, StatValue[] stats = null,
            MonsterRank rank = MonsterRank.Normal)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_level\":" + level
                + ",\"_rank\":" + (int)rank
                + ",\"_aggressionType\":" + (int)aggression
                + ",\"_experienceReward\":" + experience
                + ",\"_currencyReward\":" + currency
                + ",\"_detectionRange\":" + F(detection)
                + ",\"_attackRange\":" + F(attackRange)
                + ",\"_attackCooldownSeconds\":" + F(cooldown)
                + ",\"_leashRange\":" + F(leash)
                + ",\"_lootTable\":{\"_value\":\"" + (lootTable ?? string.Empty) + "\"}"
                + ",\"_respawn\":{\"_respawnDelaySeconds\":" + F(respawnDelay)
                + ",\"_maxAliveInArea\":1}}", definition);

            SetPrivate(definition, "_baseStats", stats ?? new[]
            {
                new StatValue(new DefinitionId(MaxHp), 100f),
                new StatValue(new DefinitionId(Atk), 20f),
                new StatValue(new DefinitionId(Def), 5f)
            });

            if (allowedMaps != null) SetPrivate(definition, "_allowedMaps", allowedMaps);

            _created.Add(definition);
            Monsters.Register(definition);
            return definition;
        }

        protected ItemDefinition AddItem(string id, int maxStack = 99)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":" + (maxStack > 1 ? "true" : "false")
                + ",\"_maxStackSize\":" + maxStack + "}", definition);

            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        protected MapDefinition AddMap(string id)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"}}",
                definition);

            _created.Add(definition);
            Maps.Register(definition);
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

            _created.Add(definition);
            DropTables.Register(definition);
            return definition;
        }

        protected QuestDefinition AddQuest(string id, QuestObjective[] objectives,
            QuestReward[] rewards = null, int levelRequirement = 0,
            DefinitionId[] prerequisites = null, bool repeatable = false)
        {
            var definition = ScriptableObject.CreateInstance<QuestDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_descriptionKey\":{\"_key\":\"" + id + ".desc\"},"
                + "\"_levelRequirement\":" + levelRequirement
                + ",\"_repeatable\":" + (repeatable ? "true" : "false") + "}", definition);

            SetPrivate(definition, "_objectives", objectives ?? new QuestObjective[0]);
            SetPrivate(definition, "_rewards", rewards ?? new QuestReward[0]);
            if (prerequisites != null) SetPrivate(definition, "_prerequisiteQuests", prerequisites);

            _created.Add(definition);
            Quests.Register(definition);
            return definition;
        }

        // ---- helpers -------------------------------------------------------------------

        /// <summary>Spawns one monster directly, without a spawn point.</summary>
        protected MonsterRuntimeState Spawn(string monsterId,
            CombatPosition position = default)
        {
            MonsterDefinition definition;
            Monsters.TryGet(new DefinitionId(monsterId), out definition);

            int maxHealth;
            definition.TryGetStat(new DefinitionId(MaxHp), out maxHealth);

            return new MonsterRuntimeState(InstanceId.New(), definition, position,
                maxHealth, Enemies);
        }

        protected ItemContainerState Container(int capacity)
        {
            return new ItemContainerState(Owner, capacity);
        }

        protected ItemInstance Stack(string id, int quantity)
        {
            return new ItemInstance(InstanceId.New(), new DefinitionId(id), Owner, quantity);
        }

        /// <summary>
        /// A stand-in player at a position.
        /// </summary>
        /// <remarks>Reuses the <see cref="FakeCombatant"/> the combat tests already have
        /// rather than adding a second one: it exists precisely to prove the combat
        /// interface works for something that is not a character, which is what a monster
        /// needs it for.</remarks>
        protected FakeCombatant Player(float x, float y, float z)
        {
            return new FakeCombatant("player:" + (++_fakeCombatants), Players.Value, 100, 100)
            {
                Position = new CombatPosition(x, y, z)
            };
        }

        /// <summary>A stand-in on the monster's own side.</summary>
        protected FakeCombatant Ally(float x, float y, float z)
        {
            return new FakeCombatant("ally:" + (++_fakeCombatants), Enemies.Value, 100, 100)
            {
                Position = new CombatPosition(x, y, z)
            };
        }

        private int _fakeCombatants;

        /// <summary>Invariant-culture float, so a comma locale cannot break the JSON.</summary>
        private static string F(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Sets a private serialized field, including one on a base type.</summary>
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
