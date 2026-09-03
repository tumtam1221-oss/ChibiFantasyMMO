using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared fixtures for maps, spawn points, portals and NPCs.
    /// </summary>
    /// <remarks>
    /// A small world: one town, one field, one boss area, one dungeon, the spawns they need
    /// and a gate between the first two. Every id, radius and requirement is a TEST FIXTURE
    /// authored as content would be -- nothing in travel or NPC interaction knows any of
    /// them.
    /// </remarks>
    internal abstract class WorldTestBase
    {
        protected DefinitionRegistry<MapDefinition> Maps;
        protected DefinitionRegistry<SpawnPointDefinition> SpawnPoints;
        protected DefinitionRegistry<PortalDefinition> Portals;
        protected DefinitionRegistry<NPCDefinition> Npcs;
        protected DefinitionRegistry<ItemDefinition> Items;
        protected DefinitionRegistry<QuestDefinition> Quests;
        protected DefinitionRegistry<ShopDefinition> Shops;

        protected OwnerId Owner;
        protected CharacterId Character;

        private List<Object> _created;

        protected const string TownA = "map.town.a";
        protected const string FieldA = "map.field.a";
        protected const string BossA = "map.boss.a";
        protected const string DungeonA = "map.dungeon.a";

        protected const string TownASpawn = "spawn.town.a";
        protected const string FieldASpawn = "spawn.field.a";
        protected const string BossASpawn = "spawn.boss.a";

        protected const string GateToField = "portal.town.to.field";
        protected const string ClosedGate = "portal.closed";

        protected const string Key = "item.key";
        protected const string Potion = "item.potion";

        [SetUp]
        public void SetUpWorldFixtures()
        {
            Maps = new DefinitionRegistry<MapDefinition>();
            SpawnPoints = new DefinitionRegistry<SpawnPointDefinition>();
            Portals = new DefinitionRegistry<PortalDefinition>();
            Npcs = new DefinitionRegistry<NPCDefinition>();
            Items = new DefinitionRegistry<ItemDefinition>();
            Quests = new DefinitionRegistry<QuestDefinition>();
            Shops = new DefinitionRegistry<ShopDefinition>();

            _created = new List<Object>();
            Owner = new OwnerId("account:test");
            Character = new CharacterId("char:test");

            AddMapDefinition(TownA, MapCategory.Town, isTown: true);
            AddMapDefinition(FieldA, MapCategory.Field);
            AddMapDefinition(BossA, MapCategory.BossArena, isBossArea: true);
            AddMapDefinition(DungeonA, MapCategory.Dungeon);

            AddSpawn(TownASpawn, TownA, SpawnType.Player, 10f, 0f, 10f);
            AddSpawn(FieldASpawn, FieldA, SpawnType.Player, 50f, 0f, 0f);
            AddSpawn(BossASpawn, BossA, SpawnType.Player, 5f, 0f, 5f);

            AddItem(Key);
            AddItem(Potion);

            // The gate stands next to the town's arrival point, so a traveller who just
            // arrived is already in range.
            AddPortal(GateToField, TownA, FieldA, FieldASpawn, x: 10f, z: 10f, radius: 5f);
            AddPortal(ClosedGate, TownA, DungeonA, TownASpawn, enabled: false);
        }

        [TearDown]
        public void TearDownWorldFixtures()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        // ---- authoring -----------------------------------------------------------------

        protected MapDefinition AddMapDefinition(string id, MapCategory category,
            bool isTown = false, bool isBossArea = false, string scene = null)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_category\":" + (int)category
                + ",\"_isTown\":" + (isTown ? "true" : "false")
                + ",\"_isBossArea\":" + (isBossArea ? "true" : "false")
                + ",\"_scene\":{\"_address\":\"" + (scene ?? "scenes/" + id) + "\"}}", definition);

            _created.Add(definition);
            Maps.Register(definition);
            return definition;
        }

        protected SpawnPointDefinition AddSpawn(string id, string map,
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

        /// <summary>
        /// Authors a portal.
        /// </summary>
        /// <remarks>The radius defaults to zero -- usable from anywhere on its map -- so a
        /// test that is not about proximity does not have to place the traveller. The one
        /// test that IS about proximity authors a radius explicitly.</remarks>
        protected PortalDefinition AddPortal(string id, string sourceMap, string destinationMap,
            string destinationSpawn, float x = 0f, float y = 0f, float z = 0f,
            float radius = 0f, bool enabled = true, int levelRequirement = 0,
            string requiredItem = null)
        {
            var definition = ScriptableObject.CreateInstance<PortalDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_sourceMap\":{\"_value\":\"" + sourceMap + "\"},"
                + "\"_destinationMap\":{\"_value\":\"" + (destinationMap ?? string.Empty) + "\"},"
                + "\"_destinationSpawn\":{\"_value\":\"" + (destinationSpawn ?? string.Empty) + "\"},"
                + "\"_entryX\":" + F(x) + ",\"_entryY\":" + F(y) + ",\"_entryZ\":" + F(z)
                + ",\"_entryRadius\":" + F(radius)
                + ",\"_enabled\":" + (enabled ? "true" : "false")
                + ",\"_levelRequirement\":" + levelRequirement
                + ",\"_requiredItem\":{\"_value\":\"" + (requiredItem ?? string.Empty) + "\"}}",
                definition);

            _created.Add(definition);
            Portals.Register(definition);
            return definition;
        }

        protected ItemDefinition AddItem(string id, int maxStack = 99)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":true,\"_maxStackSize\":" + maxStack + "}", definition);

            _created.Add(definition);
            Items.Register(definition);
            return definition;
        }

        // ---- helpers -------------------------------------------------------------------

        /// <summary>A character standing at an authored spawn.</summary>
        protected CharacterLocationState StartAt(string spawnId)
        {
            SpawnPointDefinition spawn;
            SpawnPoints.TryGet(new DefinitionId(spawnId), out spawn);

            var location = new CharacterLocationState(Character);
            location.ArriveAt(spawn);
            return location;
        }

        protected TravelService.Context Context(ItemContainerState inventory = null,
            int level = 1)
        {
            return new TravelService.Context(Maps, SpawnPoints, Portals, inventory, Items, level);
        }

        protected ItemContainerState Container(int capacity)
        {
            return new ItemContainerState(Owner, capacity);
        }

        protected ItemInstance Stack(string id, int quantity)
        {
            return new ItemInstance(InstanceId.New(), new DefinitionId(id), Owner, quantity);
        }

        /// <summary>Everything the fixtures registered, for reference checking.</summary>
        protected IDefinitionLookup Lookup()
        {
            return new CompositeTestLookup(Maps, SpawnPoints, Portals, Npcs, Items, Quests, Shops);
        }

        protected ValidationReport Validate(IDefinition definition)
        {
            var validator = new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new MapContentValidationRule(),
                new NpcContentValidationRule()
            });

            return validator.Validate(definition, Lookup());
        }

        protected static bool HasError(ValidationReport report, string fragment)
        {
            for (int i = 0; i < report.Messages.Count; i++)
            {
                if (report.Messages[i].Severity != ValidationSeverity.Error) continue;
                if (report.Messages[i].Message.Contains(fragment)) return true;
            }

            return false;
        }

        protected static string F(float value)
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

        /// <summary>Registers a definition for teardown without adding it to a registry.</summary>
        protected T Track<T>(T definition) where T : Object
        {
            _created.Add(definition);
            return definition;
        }

        /// <summary>
        /// A lookup spanning every fixture registry.
        /// </summary>
        /// <remarks>Reference checking is inherently cross-type -- a portal names a map and
        /// a spawn -- which is exactly why <see cref="IDefinitionLookup"/> exists separately
        /// from a typed registry.</remarks>
        private sealed class CompositeTestLookup : IDefinitionLookup
        {
            private readonly IDefinitionLookup[] _sources;

            public CompositeTestLookup(params IDefinitionLookup[] sources)
            {
                _sources = sources ?? new IDefinitionLookup[0];
            }

            public bool Contains(DefinitionId id)
            {
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null && _sources[i].Contains(id)) return true;
                }

                return false;
            }
        }
    }
}
