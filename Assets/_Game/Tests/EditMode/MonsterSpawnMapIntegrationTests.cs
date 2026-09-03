using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Monster spawns and the maps they stand on.
    /// </summary>
    /// <remarks>
    /// Phase 10 gave monsters a spawner and Phase 11 gave the world authored places. The
    /// seam between them is <see cref="MonsterSpawnPlacement"/>, and these are the rules it
    /// has to keep: a monster spawn is authored content like any other, it is placed by a
    /// marker rather than by a coordinate in code, and the question "may this monster stand
    /// here" has exactly one answer whether the content pass or the spawner asks it.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterSpawnMapIntegrationTests : MonsterTestBase
    {
        private DefinitionRegistry<SpawnPointDefinition> _spawnPoints;
        private ValidationReport _report;
        private List<Object> _markers;

        [SetUp]
        public void SetUpSpawnFixtures()
        {
            _spawnPoints = new DefinitionRegistry<SpawnPointDefinition>();
            _report = new ValidationReport();
            _markers = new List<Object>();
        }

        [TearDown]
        public void TearDownSpawnFixtures()
        {
            foreach (Object created in _markers) Object.DestroyImmediate(created);
        }

        // ---- placement -----------------------------------------------------------------

        [Test]
        public void A_monster_spawn_point_takes_its_map_and_position_from_the_marker()
        {
            SpawnPointDefinition marker = AddSpawnMarker("spawn.grunt.a", HomeMap,
                SpawnType.Monster, 12f, 1f, -4f);

            MonsterSpawnPoint point = MonsterSpawnPlacement.FromSpawnPoint(marker,
                new DefinitionId(Grunt), maxAlive: 3, respawnDelaySeconds: 4f, radius: 2f);

            Assert.That(point.IsValid, Is.True);
            Assert.That(point.Map, Is.EqualTo(new DefinitionId(HomeMap)));
            Assert.That(point.Monster, Is.EqualTo(new DefinitionId(Grunt)));
            Assert.That(point.Position.X, Is.EqualTo(12f).Within(0.001f));
            Assert.That(point.Position.Y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(point.Position.Z, Is.EqualTo(-4f).Within(0.001f));
            Assert.That(point.MaxAlive, Is.EqualTo(3));
            Assert.That(point.RespawnDelaySeconds, Is.EqualTo(4f).Within(0.001f));
            Assert.That(point.Radius, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void A_player_arrival_marker_cannot_be_used_to_place_a_monster()
        {
            SpawnPointDefinition arrival = AddSpawnMarker("spawn.home.player", HomeMap,
                SpawnType.Player);

            MonsterSpawnPoint point = MonsterSpawnPlacement.FromSpawnPoint(arrival,
                new DefinitionId(Grunt));

            Assert.That(point.IsValid, Is.False,
                "a monster placed on a player's arrival point would greet every traveller");
        }

        [Test]
        public void An_npc_marker_cannot_be_used_to_place_a_monster()
        {
            SpawnPointDefinition npc = AddSpawnMarker("spawn.home.npc", HomeMap, SpawnType.Npc);

            Assert.That(MonsterSpawnPlacement.FromSpawnPoint(npc, new DefinitionId(Grunt)).IsValid,
                Is.False);
        }

        [Test]
        public void A_placement_without_a_monster_is_refused()
        {
            SpawnPointDefinition marker = AddSpawnMarker("spawn.grunt.b", HomeMap,
                SpawnType.Monster);

            Assert.That(MonsterSpawnPlacement.FromSpawnPoint(marker, default).IsValid, Is.False);
        }

        [Test]
        public void Building_a_placement_spawns_nothing()
        {
            SpawnPointDefinition marker = AddSpawnMarker("spawn.grunt.c", HomeMap,
                SpawnType.Monster);

            MonsterSpawnPoint point = MonsterSpawnPlacement.FromSpawnPoint(marker,
                new DefinitionId(Grunt));

            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            Assert.That(spawner.AliveCount, Is.EqualTo(0),
                "placement is authoring; only TrySpawn puts a monster in the world");
        }

        // ---- the map rule --------------------------------------------------------------

        [Test]
        public void A_marker_placed_monster_spawns_on_the_map_it_is_authored_for()
        {
            SpawnPointDefinition marker = AddSpawnMarker("spawn.bound.home", HomeMap,
                SpawnType.Monster, 5f, 0f, 5f);

            var spawner = new MonsterSpawnService(
                MonsterSpawnPlacement.FromSpawnPoint(marker, new DefinitionId(Bound)),
                new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);

            Assert.That(monster, Is.Not.Null);
            Assert.That(spawner.LastRejection, Is.EqualTo(SpawnRejection.None));
            Assert.That(monster.Position.X, Is.EqualTo(5f).Within(0.001f),
                "the monster appears at the authored marker, not at the origin");
        }

        [Test]
        public void A_marker_on_a_map_the_monster_is_not_authored_for_spawns_nothing()
        {
            SpawnPointDefinition marker = AddSpawnMarker("spawn.bound.other", OtherMap,
                SpawnType.Monster);

            var spawner = new MonsterSpawnService(
                MonsterSpawnPlacement.FromSpawnPoint(marker, new DefinitionId(Bound)),
                new DefinitionId(MaxHp));

            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Null);
            Assert.That(spawner.LastRejection, Is.EqualTo(SpawnRejection.MapNotAllowed));
        }

        [Test]
        public void The_content_pass_and_the_spawner_agree_on_the_map_rule()
        {
            MonsterDefinition bound = Monster(Bound);

            // The same question, asked the two ways the codebase asks it.
            Assert.That(MonsterSpawnPlacement.IsMapAllowed(bound, new DefinitionId(HomeMap)),
                Is.True);
            Assert.That(MonsterSpawnPlacement.IsMapAllowed(bound, new DefinitionId(OtherMap)),
                Is.False);

            var allowed = new MonsterSpawnService(
                new MonsterSpawnPoint(new DefinitionId(Bound), default, map: new DefinitionId(HomeMap)),
                new DefinitionId(MaxHp));

            var refused = new MonsterSpawnService(
                new MonsterSpawnPoint(new DefinitionId(Bound), default, map: new DefinitionId(OtherMap)),
                new DefinitionId(MaxHp));

            Assert.That(allowed.TrySpawn(Monsters, Enemies), Is.Not.Null);
            Assert.That(refused.TrySpawn(Monsters, Enemies), Is.Null);
        }

        [Test]
        public void An_unrestricted_monster_may_stand_on_any_map()
        {
            MonsterDefinition grunt = Monster(Grunt);

            Assert.That(MonsterSpawnPlacement.IsMapAllowed(grunt, new DefinitionId(HomeMap)),
                Is.True);
            Assert.That(MonsterSpawnPlacement.IsMapAllowed(grunt, new DefinitionId(OtherMap)),
                Is.True);
        }

        [Test]
        public void A_point_that_names_no_map_cannot_be_judged_and_is_allowed()
        {
            MonsterDefinition bound = Monster(Bound);

            Assert.That(MonsterSpawnPlacement.IsMapAllowed(bound, default), Is.True,
                "content authored before maps carried ids must keep working");
        }

        // ---- validation ----------------------------------------------------------------

        [Test]
        public void A_spawn_point_on_a_map_that_does_not_exist_is_an_error()
        {
            var points = new[]
            {
                new MonsterSpawnPoint(new DefinitionId(Grunt), default,
                    map: new DefinitionId("map.deleted"))
            };

            MonsterSpawnPlacement.Validate(points, Maps, Monsters, _report);

            Assert.That(HasError(), Is.True);
        }

        [Test]
        public void A_spawn_point_a_monster_can_never_appear_at_is_an_error()
        {
            var points = new[]
            {
                new MonsterSpawnPoint(new DefinitionId(Bound), default,
                    map: new DefinitionId(OtherMap))
            };

            MonsterSpawnPlacement.Validate(points, Maps, Monsters, _report);

            Assert.That(HasError(), Is.True,
                "a point that would never spawn anything is an authoring mistake, not content");
        }

        [Test]
        public void A_spawn_point_naming_an_unresolvable_monster_is_an_error()
        {
            var points = new[]
            {
                new MonsterSpawnPoint(new DefinitionId("monster.gone"), default,
                    map: new DefinitionId(HomeMap))
            };

            MonsterSpawnPlacement.Validate(points, Maps, Monsters, _report);

            Assert.That(HasError(), Is.True);
        }

        [Test]
        public void A_spawn_point_naming_no_monster_is_an_error()
        {
            MonsterSpawnPlacement.Validate(new[] { default(MonsterSpawnPoint) }, Maps, Monsters,
                _report);

            Assert.That(HasError(), Is.True);
        }

        [Test]
        public void A_coherent_spawn_point_reports_nothing()
        {
            var points = new[]
            {
                new MonsterSpawnPoint(new DefinitionId(Bound), default,
                    map: new DefinitionId(HomeMap))
            };

            MonsterSpawnPlacement.Validate(points, Maps, Monsters, _report);

            Assert.That(_report.Messages.Count, Is.EqualTo(0));
        }

        [Test]
        public void A_monster_standing_in_town_is_a_warning_not_an_error()
        {
            MapDefinition town = AddMap("map.town");
            SetPrivate(town, "_isTown", true);

            var points = new[]
            {
                new MonsterSpawnPoint(new DefinitionId(Grunt), default, map: town.Id)
            };

            MonsterSpawnPlacement.Validate(points, Maps, Monsters, _report);

            Assert.That(HasError(), Is.False, "event content legitimately puts monsters in town");
            Assert.That(_report.Messages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void A_monster_marker_on_a_map_that_does_not_exist_is_an_error()
        {
            AddSpawnMarker("spawn.orphan", "map.deleted", SpawnType.Monster);

            MonsterSpawnPlacement.ValidateMonsterSpawnMarkers(_spawnPoints, Maps, _report);

            Assert.That(HasError(), Is.True);
        }

        [Test]
        public void A_monster_marker_belonging_to_no_map_is_an_error()
        {
            AddSpawnMarker("spawn.nowhere", string.Empty, SpawnType.Monster);

            MonsterSpawnPlacement.ValidateMonsterSpawnMarkers(_spawnPoints, Maps, _report);

            Assert.That(HasError(), Is.True);
        }

        [Test]
        public void Player_and_npc_markers_are_not_judged_by_the_monster_pass()
        {
            AddSpawnMarker("spawn.player.only", HomeMap, SpawnType.Player);
            AddSpawnMarker("spawn.npc.only", HomeMap, SpawnType.Npc);

            MonsterSpawnPlacement.ValidateMonsterSpawnMarkers(_spawnPoints, Maps, _report);

            Assert.That(_report.Messages.Count, Is.EqualTo(0));
        }

        [Test]
        public void A_well_placed_monster_marker_reports_nothing()
        {
            AddSpawnMarker("spawn.good", HomeMap, SpawnType.Monster, 3f, 0f, 3f);

            MonsterSpawnPlacement.ValidateMonsterSpawnMarkers(_spawnPoints, Maps, _report);

            Assert.That(_report.Messages.Count, Is.EqualTo(0));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void There_is_no_second_monster_runtime_for_placed_monsters()
        {
            // Placement must not have introduced a parallel monster type; Phase 10's runtime
            // state is still the only one.
            System.Type[] types = typeof(MonsterSpawnPlacement).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                if (!type.Name.Contains("Monster")) continue;

                Assert.That(type.Name, Is.Not.EqualTo("PlacedMonster"));
                Assert.That(type.Name, Is.Not.EqualTo("MapMonsterRuntime"));
                Assert.That(type.Name, Is.Not.EqualTo("MonsterSpawnRuntime"));
            }
        }

        [Test]
        public void The_map_rule_exists_exactly_once()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/MonsterSpawnService.cs",
                "Assets/_Game/Scripts/Gameplay/MonsterSpawnPlacement.cs"
            };

            int implementations = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                if (source.Contains("definition.AllowedMaps")) implementations++;
            }

            Assert.That(implementations, Is.EqualTo(1),
                "two copies of the map rule would eventually disagree");
        }

        // ---- helpers -------------------------------------------------------------------

        private bool HasError()
        {
            return _report.ErrorCount > 0;
        }

        private MonsterDefinition Monster(string id)
        {
            MonsterDefinition definition;
            Monsters.TryGet(new DefinitionId(id), out definition);
            return definition;
        }

        private static string F(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        private SpawnPointDefinition AddSpawnMarker(string id, string map,
            SpawnType type = SpawnType.Monster, float x = 0f, float y = 0f, float z = 0f)
        {
            var definition = ScriptableObject.CreateInstance<SpawnPointDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)type
                + ",\"_x\":" + F(x) + ",\"_y\":" + F(y) + ",\"_z\":" + F(z) + "}", definition);

            _markers.Add(definition);
            _spawnPoints.Register(definition);
            return definition;
        }
    }
}
