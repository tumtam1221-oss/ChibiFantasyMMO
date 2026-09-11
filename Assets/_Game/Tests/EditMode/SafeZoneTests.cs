using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A walled town in open country: monsters live outside the wall and nothing the server
    /// does lets one spawn, step or aim inside it.
    /// </summary>
    /// <remarks>
    /// The zone is authored geometry on the map, and every refusal reads it through
    /// <see cref="MonsterSpawnPlacement.AllowsMonstersAt"/>, so these tests pin one rule at
    /// each door it is enforced at: the nest, the configured row, the step, the target.
    /// </remarks>
    public sealed class SafeZoneTests
    {
        private const string WalledTown = "map.test_walled_town";
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

        /// <summary>A town whose wall is a circle of radius 30 at the origin.</summary>
        private MapDefinition WalledTownMap()
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + WalledTown + "\"},\"_isTown\":true,\"_isSafeZone\":false,"
                + "\"_safeZones\":[{\"_center\":{\"x\":0,\"y\":0},\"_radius\":30},"
                + "{\"_center\":{\"x\":-8,\"y\":55},\"_radius\":24}]}", map);
            _created.Add(map);
            return map;
        }

        private DefinitionRegistry<MapDefinition> Registry()
        {
            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(WalledTownMap());
            return maps;
        }

        private static MonsterSpawnPoint Nest(float x, float z)
        {
            return new MonsterSpawnPoint(new DefinitionId(Grunt), new CombatPosition(x, 0f, z),
                0f, 1, 1f, new DefinitionId(WalledTown));
        }

        // ---- the zone itself ------------------------------------------------------------------

        [Test]
        public void A_zone_contains_its_disc_and_nothing_else()
        {
            var zone = new MapSafeZone(new Vector2(10f, -5f), 4f);

            Assert.That(zone.Contains(10f, -5f), Is.True);
            Assert.That(zone.Contains(13.9f, -5f), Is.True);
            Assert.That(zone.Contains(14.1f, -5f), Is.False);
            Assert.That(new MapSafeZone(Vector2.zero, 0f).Contains(0f, 0f), Is.False,
                "a zero radius is an empty zone, not a point");
        }

        [Test]
        public void A_town_with_zones_is_open_beyond_them()
        {
            MapDefinition map = WalledTownMap();

            Assert.That(MonsterSpawnPlacement.AllowsMonsters(map), Is.True,
                "the map as a whole admits monsters");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 0f, 0f), Is.False);
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 29f, 0f), Is.False);
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 31f, 0f), Is.True);
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, -8f, 70f), Is.False,
                "the second zone counts too");
        }

        [Test]
        public void A_town_without_zones_is_still_closed_as_a_whole()
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"map.old_town\"},\"_isTown\":true}", map);
            _created.Add(map);

            Assert.That(MonsterSpawnPlacement.AllowsMonsters(map), Is.False,
                "every town shipped before zones existed keeps its old meaning");
            Assert.That(MonsterSpawnPlacement.AllowsMonstersAt(map, 500f, 500f), Is.False);
        }

        // ---- the nest -----------------------------------------------------------------------------

        [Test]
        public void The_runtime_refuses_a_nest_inside_the_wall_and_accepts_one_outside()
        {
            var runtime = new MonsterWorldRuntime(null, null, new DefinitionId(MaxHp),
                new CombatTeam(2), Registry());

            Assert.That(runtime.AddSpawnPoint(Nest(5f, 5f)), Is.False, "inside the wall");
            Assert.That(runtime.AddSpawnPoint(Nest(0f, -58f)), Is.True, "the meadow outside");
            Assert.That(runtime.SpawnerCount, Is.EqualTo(1));
        }

        // ---- the step ------------------------------------------------------------------------------

        private sealed class Flat : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height) { height = 2.5f; return true; }
        }

        private MonsterRuntimeState Grunt_at(float x, float z)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Grunt + "\"},\"_moveSpeed\":2}", definition);
            _created.Add(definition);

            return new MonsterRuntimeState(InstanceId.New(), definition,
                new CombatPosition(x, 0f, z), 10, new CombatTeam(2));
        }

        [Test]
        public void A_chase_stops_at_the_wall()
        {
            MapDefinition map = WalledTownMap();
            MonsterRuntimeState monster = Grunt_at(31f, 0f);

            // The player is just inside the wall; one step west would cross it.
            MonsterMoveResult result = MonsterMovement.Step(monster, MonsterAiState.Chase,
                new CombatPosition(25f, 0f, 0f), 1f, 0f, map);

            Assert.That(result.Moved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.SafeZone));
            Assert.That(monster.Position.X, Is.EqualTo(31f), "refused means it did not move");
        }

        [Test]
        public void A_step_in_open_country_lands_on_the_ground()
        {
            MapDefinition map = WalledTownMap();
            MonsterRuntimeState monster = Grunt_at(40f, 0f);

            MonsterMoveResult result = MonsterMovement.Step(monster, MonsterAiState.Chase,
                new CombatPosition(50f, 0f, 0f), 1f, 0f, map, new Flat());

            Assert.That(result.Moved, Is.True, result.ToString());
            Assert.That(monster.Position.X, Is.EqualTo(42f).Within(0.001f), "two metres at speed two");
            Assert.That(monster.Position.Y, Is.EqualTo(2.5f).Within(0.001f),
                "the monster stands on the map's ground, exactly as a player does");
        }

        [Test]
        public void Without_a_map_or_ground_the_step_is_what_it_always_was()
        {
            MonsterRuntimeState monster = Grunt_at(0f, 0f);

            MonsterMoveResult result = MonsterMovement.Step(monster, MonsterAiState.Chase,
                new CombatPosition(10f, 0f, 0f), 1f);

            Assert.That(result.Moved, Is.True);
            Assert.That(monster.Position.X, Is.EqualTo(2f).Within(0.001f));
            Assert.That(monster.Position.Y, Is.Zero);
        }

        // ---- the target -----------------------------------------------------------------------------

        [Test]
        public void Nobody_inside_the_wall_can_be_targeted()
        {
            MapDefinition map = WalledTownMap();

            Assert.That(MonsterSpawnPlacement.MayBeTargeted(map, 0f, 0f), Is.False);
            Assert.That(MonsterSpawnPlacement.MayBeTargeted(map, 40f, 0f), Is.True);
            Assert.That(MonsterSpawnPlacement.MayBeTargeted(null, 0f, 0f), Is.True,
                "no map data means no zone to hide in");
        }
    }
}
