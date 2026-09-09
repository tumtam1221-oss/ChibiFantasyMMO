using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The world stops being flat: a map that authors ground puts a walking character on it,
    /// and a map that authors none behaves exactly as it always did.
    /// </summary>
    [TestFixture]
    internal sealed class MapHeightFieldTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        /// <summary>Ground that rises half a metre for every metre of X.</summary>
        private sealed class Slope : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height)
            {
                height = 0.5f * x;
                return true;
            }
        }

        /// <summary>Ground that ends at X = 1: a shoreline.</summary>
        private sealed class Shore : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height)
            {
                height = 0f;
                return x <= 1f;
            }
        }

        private readonly List<Object> _local = new List<Object>();

        [TearDown]
        public void TearDownFields()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- the sampler -------------------------------------------------------------------

        [Test]
        public void A_point_between_samples_is_interpolated()
        {
            // 2x2 grid, one metre apart: the east edge is one metre higher than the west.
            float[] heights = { 0f, 1f, 0f, 1f };
            byte[] walkable = { 1, 1, 1, 1 };

            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                0.5f, 0.5f, out float mid), Is.True);
            Assert.That(mid, Is.EqualTo(0.5f).Within(0.0001f));

            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                1f, 0f, out float corner), Is.True);
            Assert.That(corner, Is.EqualTo(1f).Within(0.0001f), "the far corner itself");
        }

        [Test]
        public void Outside_the_grid_there_is_no_ground()
        {
            float[] heights = { 0f, 0f, 0f, 0f };
            byte[] walkable = { 1, 1, 1, 1 };

            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                -0.1f, 0.5f, out _), Is.False);
            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                0.5f, 1.1f, out _), Is.False);
            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                float.NaN, 0.5f, out _), Is.False, "NaN must not slip through as ground");
        }

        [Test]
        public void One_unwalkable_corner_refuses_the_whole_cell()
        {
            float[] heights = { 0f, 0f, 0f, 0f };
            byte[] walkable = { 1, 1, 1, 0 };

            Assert.That(MapHeightField.Sample(0f, 0f, 1f, 2, 2, heights, walkable,
                0.5f, 0.5f, out _), Is.False,
                "interpolating towards 'no floor' would invent a floor");
        }

        // ---- the simulator -----------------------------------------------------------------

        private static CharacterLocationState At(float x, float y, float z)
        {
            var location = new CharacterLocationState(new CharacterId("char-hill"),
                new DefinitionId("map.hill"));
            location.Position = new CombatPosition(x, y, z);
            return location;
        }

        [Test]
        public void With_no_ground_the_step_carries_Y_through_unchanged()
        {
            CharacterLocationState location = At(0f, 3f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(1f, 0f, 1), location, new MovementBudget(2f),
                0, 0, 1000);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Position.X, Is.EqualTo(2f).Within(0.001f));
            Assert.That(result.Position.Y, Is.EqualTo(3f), "flat world: Y is not touched");
        }

        /// <summary>Ground with a step: flat, then a cliff edge up to +0.5 at x = 1.</summary>
        private sealed class Ledge : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height)
            {
                height = x >= 1f ? 0.5f : 0f;
                return true;
            }
        }

        [Test]
        public void A_step_up_onto_a_ledge_is_not_charged_to_the_movement_budget()
        {
            // The bridge deck: half a metre of rise inside one small horizontal step. The
            // client asked to move 0.04 m east; the height is the server's own doing, so
            // the step must be accepted rather than refused as impossibly far.
            CharacterLocationState location = At(0.98f, 0f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(1f, 0f, 1), location, new MovementBudget(2f),
                0, 0, 20, true, new Ledge());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Position.X, Is.EqualTo(1.02f).Within(0.001f));
            Assert.That(result.Position.Y, Is.EqualTo(0.5f).Within(0.001f),
                "the character is standing on the ledge, not stuck at its foot");
        }

        [Test]
        public void Moving_too_far_across_the_ground_is_still_refused()
        {
            CharacterLocationState location = At(0f, 0f, 0f);

            // A tenth of a second buys a quarter metre; this claims ten.
            var request = new MovementRequest(location.CharacterId, new DefinitionId("map.hill"),
                new CombatPosition(10f, 0f, 0f), 1, 100);

            MovementResult result = MovementValidator.Validate(request, location,
                new MovementBudget(2f), 0, 0);

            Assert.That(result.IsAccepted, Is.False, "a ten-metre stride is still a cheat");
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.TooFar));
        }

        [Test]
        public void Height_alone_never_makes_a_step_too_far()
        {
            CharacterLocationState location = At(0f, 0f, 0f);

            // Standing still horizontally, five metres higher: the ground the server read.
            var request = new MovementRequest(location.CharacterId, new DefinitionId("map.hill"),
                new CombatPosition(0f, 5f, 0f), 1, 100);

            MovementResult result = MovementValidator.Validate(request, location,
                new MovementBudget(2f), 0, 0);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.DistanceTravelled, Is.Zero,
                "nothing moved across the map, so nothing was travelled");
        }

        [Test]
        public void With_ground_the_step_lands_on_it()
        {
            CharacterLocationState location = At(0f, 0f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(1f, 0f, 1), location, new MovementBudget(2f),
                0, 0, 1000, true, new Slope());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Position.X, Is.EqualTo(2f).Within(0.001f));
            Assert.That(result.Position.Y, Is.EqualTo(1f).Within(0.001f),
                "two metres east up a 1:2 slope is one metre higher");
            Assert.That(location.Position.Y, Is.EqualTo(1f).Within(0.001f),
                "and the authoritative location was moved there");
        }

        /// <summary>Ground everywhere except a wall filling x > 1: a pond rim, a building.</summary>
        private sealed class Wall : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height)
            {
                height = 0f;
                return x <= 1f;
            }
        }

        [Test]
        public void Walking_into_a_wall_slides_along_it_instead_of_stopping()
        {
            // Heading north-east into a wall that blocks east. The character should keep
            // going north along the wall rather than freezing the moment it touches it.
            CharacterLocationState location = At(1f, 0f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(0.707f, 0.707f, 1), location, new MovementBudget(2f),
                0, 0, 1000, true, new Wall());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Position.X, Is.EqualTo(1f).Within(0.001f),
                "the wall stopped the eastward half");
            Assert.That(result.Position.Z, Is.GreaterThan(1.3f),
                "and the northward half carried on");
        }

        [Test]
        public void Sliding_never_travels_further_than_the_step_allowed()
        {
            CharacterLocationState location = At(1f, 0f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(0.707f, 0.707f, 1), location, new MovementBudget(2f),
                0, 0, 1000, true, new Wall());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DistanceTravelled, Is.LessThanOrEqualTo(2f * 1.0f + 0.001f),
                "a slide is a shorter step, never a longer one");
        }

        [Test]
        public void A_step_onto_no_ground_is_refused_and_nothing_moves()
        {
            CharacterLocationState location = At(0f, 0f, 0f);

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(1f, 0f, 1), location, new MovementBudget(2f),
                0, 0, 1000, true, new Shore());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.Unwalkable));
            Assert.That(location.Position.X, Is.Zero, "refused means the server did not move it");
        }

        // ---- the authority reads the map's field -------------------------------------------

        private MapHeightField Field(float originX, float originZ, float cell, int columns,
            int rows, System.Func<float, float, float> heightAt, bool walkable)
        {
            var field = ScriptableObject.CreateInstance<MapHeightField>();
            var heights = new float[columns * rows];
            var flags = new byte[columns * rows];

            for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                heights[j * columns + i] = heightAt(originX + i * cell, originZ + j * cell);
                flags[j * columns + i] = walkable ? (byte)1 : (byte)0;
            }

            field.Initialize(originX, originZ, cell, columns, rows, heights, flags);
            _local.Add(field);
            return field;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo info = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, "field " + name);
            info.SetValue(target, value);
        }

        private LivingCharacter Spawn(WorldCharacterRegistry players, FakeStore store)
        {
            const string character = "char-hill";
            string session = "session-" + character;

            store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1);

            WorldSpawnResult result = players.Spawn(7,
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-" + character),
                    new CharacterId(character), new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, result.Detail);
            return result.Character;
        }

        private CharacterMovementAuthority Authority(FakeStore store,
            out WorldCharacterRegistry players)
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.hill\"},\"_map\":{\"_value\":\"" + HomeMap + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);
            _local.Add(spawn);
            spawns.Register(spawn);

            players = new WorldCharacterRegistry(store, spawns);
            return new CharacterMovementAuthority(players, _ => true, Maps, 4f);
        }

        [Test]
        public void The_authority_puts_a_walking_character_on_the_maps_ground()
        {
            Maps.TryGet(new DefinitionId(HomeMap), out MapDefinition map);
            SetField(map, "_heightField",
                Field(-10f, -10f, 10f, 3, 3, (x, z) => 0.5f * z, walkable: true));

            try
            {
                var store = new FakeStore();
                CharacterMovementAuthority movement = Authority(store, out WorldCharacterRegistry players);
                LivingCharacter character = Spawn(players, store);

                movement.Tick(0.25f);
                movement.Submit(7, 0f, 1f, 1);

                Assert.That(movement.LastResult.IsAccepted, Is.True, movement.LastResult.ToString());
                Assert.That(character.Location.Position.Z, Is.EqualTo(1f).Within(0.001f));
                Assert.That(character.Location.Position.Y, Is.EqualTo(0.5f).Within(0.001f),
                    "one metre north up a 1:2 slope");
            }
            finally
            {
                SetField(map, "_heightField", null);
            }
        }

        [Test]
        public void The_authority_refuses_a_step_where_the_map_has_no_floor()
        {
            Maps.TryGet(new DefinitionId(HomeMap), out MapDefinition map);
            SetField(map, "_heightField",
                Field(-10f, -10f, 10f, 3, 3, (x, z) => 0f, walkable: false));

            try
            {
                var store = new FakeStore();
                CharacterMovementAuthority movement = Authority(store, out WorldCharacterRegistry players);
                LivingCharacter character = Spawn(players, store);

                movement.Tick(0.25f);
                movement.Submit(7, 0f, 1f, 1);

                Assert.That(movement.LastResult.IsAccepted, Is.False);
                Assert.That(movement.LastResult.Reason, Is.EqualTo(MovementRejection.Unwalkable));
                Assert.That(character.Location.Position.Z, Is.Zero);
            }
            finally
            {
                SetField(map, "_heightField", null);
            }
        }

        // ---- the shipped town ---------------------------------------------------------------

        [Test]
        public void Harbor_town_ships_a_baked_ground_that_matches_its_spawn()
        {
            var map = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>(
                "Assets/_Game/Data/Production/Maps/map_harbor_town.asset");

            Assert.That(map, Is.Not.Null);
            Assert.That(map.HeightField, Is.Not.Null, "Harbor Town must reference its baked ground");
            Assert.That(map.HeightField.IsValid, Is.True);

            // Read where players actually arrive rather than assuming the origin: the town
            // square has a koi pond in the middle of it, and the arrival marker stands on
            // the approach in front of it.
            var spawn = UnityEditor.AssetDatabase.LoadAssetAtPath<SpawnPointDefinition>(
                "Assets/_Game/Data/Production/Spawns/spawn_harbor_town_plaza.asset");

            Assert.That(spawn, Is.Not.Null, "the shipped arrival point must exist");
            Assert.That(spawn.Map, Is.EqualTo(map.Id));

            Assert.That(map.HeightField.TrySample(spawn.X, spawn.Z, out float atSpawn), Is.True,
                "players arrive here; there must be floor under them");
            Assert.That(atSpawn, Is.EqualTo(spawn.Y).Within(0.5f),
                "the authored arrival height matches the ground the server will put them on");

            Assert.That(map.HeightField.TrySample(-48f, -48f, out _), Is.False,
                "the middle of the bay is water, not floor");
            Assert.That(map.HeightField.TrySample(400f, 400f, out _), Is.False,
                "past the terrain there is nothing to stand on");
        }

        [Test]
        public void The_outskirts_remain_a_flat_map()
        {
            var field = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>(
                "Assets/_Game/Data/Production/Maps/map_harbor_outskirts.asset");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.HeightField, Is.Null,
                "no ground authored, so movement there is exactly what it was before hills");
        }
    }
}
