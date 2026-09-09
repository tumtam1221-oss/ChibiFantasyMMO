using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A town admits no monsters, and the server is what says so.
    /// </summary>
    /// <remarks>
    /// The rule lives in authored map data -- <c>IsTown</c> / <c>IsSafeZone</c> -- and is
    /// enforced at the two doors every nest comes through, so a client, a scene collider or a
    /// spreadsheet cannot put a monster in a plaza. No monster id and no map id is named in
    /// the production code these tests cover.
    /// </remarks>
    public sealed class SafeTownMonsterTests
    {
        private const string Town = "map.test_town";
        private const string Field = "map.test_field";
        private const string Grunt = "monster.test_grunt";
        private const string MaxHp = "stat.max_health";

        private readonly System.Collections.Generic.List<Object> _created =
            new System.Collections.Generic.List<Object>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        private MapDefinition Map(string id, bool isTown, bool isSafeZone)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"}"
                + ",\"_isTown\":" + (isTown ? "true" : "false")
                + ",\"_isSafeZone\":" + (isSafeZone ? "true" : "false") + "}", map);
            _created.Add(map);
            return map;
        }

        private DefinitionRegistry<MapDefinition> Registry()
        {
            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(Town, isTown: true, isSafeZone: true));
            maps.Register(Map(Field, isTown: false, isSafeZone: false));
            return maps;
        }

        private static MonsterSpawnPoint Nest(string map)
        {
            return new MonsterSpawnPoint(new DefinitionId(Grunt), new CombatPosition(0f, 0f, 0f),
                0f, 1, 1f, new DefinitionId(map));
        }

        // ---- the rule itself -------------------------------------------------------------

        [Test]
        public void A_town_admits_no_monsters_and_a_field_does()
        {
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(Map(Town, true, true)), Is.False,
                "a town is where players stand about; a monster in one is a bug");
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(Map(Field, false, false)), Is.True);
        }

        [Test]
        public void Either_flag_alone_is_enough_to_keep_monsters_out()
        {
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(Map(Town, true, false)), Is.False,
                "IsTown alone closes the map");
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(Map(Town, false, true)), Is.False,
                "IsSafeZone alone closes the map");
        }

        [Test]
        public void An_unresolved_map_is_not_treated_as_a_town()
        {
            // Null means the caller could not resolve it. Refusing every unresolved map would
            // silently empty a world whose registry was wired late.
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(null), Is.True);
        }

        // ---- the server refuses the nest --------------------------------------------------

        [Test]
        public void The_runtime_refuses_a_nest_placed_in_a_town()
        {
            var runtime = new MonsterWorldRuntime(null, null, new DefinitionId(MaxHp),
                new CombatTeam(2), Registry());

            Assert.That(runtime.AddSpawnPoint(Nest(Town)), Is.False,
                "the nest must never exist, so nothing can spawn from it");
            Assert.That(runtime.SpawnerCount, Is.EqualTo(0));
        }

        [Test]
        public void The_runtime_still_accepts_a_nest_on_a_field()
        {
            var runtime = new MonsterWorldRuntime(null, null, new DefinitionId(MaxHp),
                new CombatTeam(2), Registry());

            Assert.That(runtime.AddSpawnPoint(Nest(Field)), Is.True);
            Assert.That(runtime.SpawnerCount, Is.EqualTo(1));
        }

        [Test]
        public void A_runtime_with_no_map_registry_behaves_as_it_always_did()
        {
            var runtime = new MonsterWorldRuntime(null, null, new DefinitionId(MaxHp),
                new CombatTeam(2));

            Assert.That(runtime.AddSpawnPoint(Nest(Town)), Is.True,
                "with no map data there is no town rule to apply");
        }

        // ---- the shipped town is actually safe ---------------------------------------------

        [Test]
        public void Harbor_town_is_a_walled_town_in_open_country()
        {
            var map = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>(
                "Assets/_Game/Data/Production/Maps/map_harbor_town.asset");

            Assert.That(map, Is.Not.Null, "the shipped Harbor Town map must exist");
            Assert.That(map.IsTown, Is.True);
            Assert.That(map.SafeZones.Length, Is.GreaterThan(0),
                "the town authors its wall as safe zones, so the map beyond it stays open");

            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 0f, 0f), Is.False,
                "the plaza, where players spawn, admits no monster");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, -9f, -6f), Is.False,
                "the market street is inside the wall");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 0f, 43f), Is.False,
                "the shrine plateau is inside the wall");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, -20f, 71f), Is.False,
                "the mountain lookout is covered by its own zone");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 0f, -58f), Is.True,
                "the south meadow outside the gate is where the first camp stands");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, -46f, 40f), Is.True,
                "the far knoll is open country");

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SpawnPointDefinition",
                new[] { "Assets/_Game/Data/Production/Spawns" });

            for (int i = 0; i < guids.Length; i++)
            {
                var spawn = UnityEditor.AssetDatabase.LoadAssetAtPath<SpawnPointDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));

                if (spawn == null || spawn.Map != map.Id) continue;

                Assert.That(spawn.SpawnType, Is.Not.EqualTo(SpawnType.Monster),
                    "no monster spawn marker may stand in Harbor Town: " + spawn.Id);
            }
        }

        [Test]
        public void The_outskirts_remain_open_to_monsters()
        {
            var field = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>(
                "Assets/_Game/Data/Production/Maps/map_harbor_outskirts.asset");

            Assert.That(field, Is.Not.Null);
            Assert.That(MonsterSpawnPlacement.AllowsMonsters(field), Is.True,
                "the field is where monsters belong; closing it would empty the game");
        }
    }
}
