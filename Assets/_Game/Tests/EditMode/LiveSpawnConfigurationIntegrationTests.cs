using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Spawn configuration from a real MySQL row to a living monster, over a real socket.
    /// </summary>
    /// <remarks>
    /// <b>The whole point of the gate is that this path works without a rebuild.</b> A
    /// designer edits <c>monster_spawn_point</c>; the server reads it and the map changes.
    /// Every other test here drives a scripted transport, which proves the reader reads what
    /// it was told to expect -- it cannot prove that MySQL's <c>FLOAT</c> survives PHP's
    /// <c>json_encode</c> as <c>12.5</c> rather than <c>"12.5"</c>, that a disabled row is
    /// actually filtered by SQL, or that an absent AI value arrives as absent rather than as
    /// zero. Those are the failures a mock is structurally unable to find.
    ///
    /// <b>How to run it.</b> Seed the fixture and serve the API:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public
    /// </code>
    /// Without them every test here <b>skips with a reason</b> rather than failing, so a
    /// machine with no PHP stays green -- and a skip is never mistaken for a pass.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveSpawnConfigurationIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private const string MaxHp = "stat.maxhp";
        private const string Poring = "monster.poring";
        private const string Lunatic = "monster.lunatic";
        private const string Hidden = "monster.hidden";

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpMonsterSpawnConfigurationSource _source;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _fixture = IntegrationFixture.Load();

            if (!_fixture.IsAvailable)
            {
                Assert.Ignore("no live backend fixture: " + _fixture.Reason);
            }

            _transport = new UnityWebRequestTransport(BaseAddress, 15);

            HttpExchange health = _transport.Send("GET", "/api/health", null, null);

            if (!health.IsSuccess)
            {
                Assert.Ignore("no PHP server on " + BaseAddress + " ("
                    + health.FailureKind + ") -- start it with: "
                    + "php -S 127.0.0.1:8099 -t backend/public");
            }

            _source = new HttpMonsterSpawnConfigurationSource(_transport);
        }

        [TearDown]
        public void TearDown()
        {
            _transport?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private DefinitionId Map => new DefinitionId(_fixture.MapId);

        private static MonsterSpawnConfiguration Find(MapSpawnConfiguration configuration,
            string spawnPointId)
        {
            foreach (MonsterSpawnConfiguration row in configuration.SpawnPoints)
            {
                if (row.SpawnPointId == spawnPointId) return row;
            }

            Assert.Fail("no spawn point '" + spawnPointId + "' in " + configuration);

            return default;
        }

        private static MonsterAiConfiguration FindAi(MapSpawnConfiguration configuration,
            string monster)
        {
            foreach (MonsterAiConfiguration row in configuration.AiConfigurations)
            {
                if (row.Monster.Value == monster) return row;
            }

            Assert.Fail("no ai configuration for '" + monster + "'");

            return default;
        }

        // ---- what the database said is what Unity gets --------------------------------------

        [Test]
        public void TheConfiguredNestsArriveWithEveryNumberIntact()
        {
            MapSpawnConfiguration configuration = _source.Load(Map);

            Assert.That(configuration, Is.Not.Null, "the API returned nothing usable");
            Assert.That(configuration.Map.Value, Is.EqualTo(_fixture.MapId));

            MonsterSpawnConfiguration first = Find(configuration, "itest-spawn-a");

            // Decimals matter: an integer-only reader would turn 12.5 into 12 and put the
            // nest somewhere else entirely.
            Assert.That(first.Monster.Value, Is.EqualTo(Poring));
            Assert.That(first.X, Is.EqualTo(12.5f).Within(0.0001f));
            Assert.That(first.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(first.Z, Is.EqualTo(-7.25f).Within(0.0001f));
            Assert.That(first.Radius, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(first.InitialCount, Is.EqualTo(2));
            Assert.That(first.MaxAlive, Is.EqualTo(5));
            Assert.That(first.RespawnSeconds, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(first.GroupId, Is.EqualTo("itest-group"));

            MonsterSpawnConfiguration second = Find(configuration, "itest-spawn-b");

            Assert.That(second.Monster.Value, Is.EqualTo(Lunatic));
            Assert.That(second.X, Is.EqualTo(-3f).Within(0.0001f));
            Assert.That(second.Y, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(second.MaxAlive, Is.EqualTo(1));
            Assert.That(second.GroupId, Is.Empty, "no group is empty, not the string 'null'");
        }

        [Test]
        public void ADisabledRowNeverLeavesTheDatabase()
        {
            MapSpawnConfiguration configuration = _source.Load(Map);

            foreach (MonsterSpawnConfiguration row in configuration.SpawnPoints)
            {
                Assert.That(row.SpawnPointId, Is.Not.EqualTo("itest-spawn-off"),
                    "unticking Enabled must actually stop a nest");
            }
        }

        [Test]
        public void AnImpossibleRowIsReportedWithAReasonRatherThanDropped()
        {
            _source.Load(Map);

            Assert.That(_source.LastRejected, Has.Count.EqualTo(1));
            Assert.That(_source.LastRejected[0], Does.Contain("itest-spawn-over"));
            Assert.That(_source.LastRejected[0], Does.Contain("initial_spawn_count"),
                "an operator has to be told which number to change");
        }

        [Test]
        public void AbsentAiValuesArriveAbsentRatherThanAsZero()
        {
            MapSpawnConfiguration configuration = _source.Load(Map);

            MonsterAiConfiguration full = FindAi(configuration, Lunatic);

            Assert.That(full.HasAggression, Is.True);
            Assert.That(full.Aggression, Is.EqualTo((int)MonsterAggressionType.Defensive),
                "the behaviour this phase completed survives the round trip");
            Assert.That(full.DetectionRange, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(full.ChaseRange, Is.EqualTo(18f).Within(0.0001f));
            Assert.That(full.AttackRange, Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(full.AttackCooldown, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(full.MoveSpeed, Is.EqualTo(3.25f).Within(0.0001f));

            MonsterAiConfiguration partial = FindAi(configuration, Poring);

            // Zero and absent are different answers, and a detection range of zero is a
            // real setting -- it means "notices nobody".
            Assert.That(partial.HasDetectionRange, Is.True);
            Assert.That(partial.DetectionRange, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(partial.HasChaseRange, Is.False);
            Assert.That(partial.HasAttackRange, Is.False);
            Assert.That(partial.HasMoveSpeed, Is.True);
            Assert.That(partial.MoveSpeed, Is.EqualTo(1.5f).Within(0.0001f));

            MonsterAiConfiguration aggressive = FindAi(configuration, Hidden);

            Assert.That(aggressive.Aggression,
                Is.EqualTo((int)MonsterAggressionType.Aggressive),
                "an override for a disabled nest's monster is still a monster override");
        }

        [Test]
        public void AMapNobodyConfiguredReadsAsEmptyRatherThanAsSomebodyElsesNests()
        {
            MapSpawnConfiguration configuration =
                _source.Load(new DefinitionId("map.no-such-map"));

            // Empty, not null: a map with no configured nests is a legitimate answer, and
            // it is a different fact from "the API could not be reached", which is null.
            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.Map.Value, Is.EqualTo("map.no-such-map"));
            Assert.That(configuration.SpawnPoints, Is.Empty,
                "no map may ever be handed another map's nests");
        }

        // ---- and it reaches the runtime -------------------------------------------------------

        [Test]
        public void ARowInMySqlBecomesALivingMonsterOnTheServer()
        {
            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(MapDefinitionFor(_fixture.MapId));

            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(MonsterDefinitionFor(Poring));
            monsters.Register(MonsterDefinitionFor(Lunatic));

            var runtime = new MonsterWorldRuntime(
                new WorldCharacterRegistry(new IgnoredStore(),
                    new DefinitionRegistry<SpawnPointDefinition>()),
                monsters, new DefinitionId(MaxHp), new CombatTeam(2), maps);

            var loader = new MonsterConfigurationLoader(_source, runtime, maps);

            // Two valid nests: one asked for two of five, the other for one of one.
            Assert.That(loader.Load(Map), Is.EqualTo(3),
                "the initial counts in MySQL are the populations on the server");
            Assert.That(loader.LastResult.Accepted, Is.EqualTo(2));

            // Nothing for Unity to reject: the impossible row was refused by the API before
            // it ever crossed the wire, which is where a database mistake should be caught.
            Assert.That(loader.LastResult.Rejected, Is.Zero);
            Assert.That(_source.LastRejected, Has.Count.EqualTo(1),
                "and it is still reported rather than silently dropped");
            Assert.That(runtime.AliveCount, Is.EqualTo(3));

            var porings = 0;

            foreach (LivingMonster monster in runtime.All())
            {
                Assert.That(monster.Map.Value, Is.EqualTo(_fixture.MapId));

                if (monster.State.DefinitionId.Value != Poring) continue;

                porings++;

                // The position in the database, through PHP, through HTTP, onto a monster.
                Assert.That(monster.State.SpawnPosition.X, Is.EqualTo(12.5f).Within(0.0001f));
                Assert.That(monster.State.SpawnPosition.Z, Is.EqualTo(-7.25f).Within(0.0001f));
            }

            Assert.That(porings, Is.EqualTo(2));

            // And reloading the same configuration does not double the map.
            Assert.That(loader.Load(Map), Is.Zero);
            Assert.That(runtime.AliveCount, Is.EqualTo(3));
        }

        /// <summary>A store nothing asks anything of: this fixture places no players.</summary>
        private sealed class IgnoredStore : ICharacterStateStore
        {
            public CharacterPersistenceResult Load(SessionId session) =>
                CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private MapDefinition MapDefinitionFor(string id)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + id + "\"}}", definition);

            _created.Add(definition);

            return definition;
        }

        /// <summary>
        /// The authored side of a monster the database only references.
        /// </summary>
        /// <remarks>Authored here rather than fetched, which is the property the schema was
        /// designed around: MySQL holds no monster table to go stale, so what a monster
        /// <i>is</i> stays content and only where it stands is configuration.</remarks>
        private MonsterDefinition MonsterDefinitionFor(string id)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_level\":5,\"_aggressionType\":1,"
                + "\"_detectionRange\":10,\"_attackRange\":2,\"_attackCooldownSeconds\":2,"
                + "\"_leashRange\":30,\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}",
                definition);

            SetPrivate(definition, "_baseStats",
                new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            _created.Add(definition);

            return definition;
        }

        /// <summary>Sets a private serialized field, including one on a base type.</summary>
        /// <remarks>MonsterTestBase has one, but this fixture does not derive from it: it
        /// needs a live transport and no monster fixtures, and inheriting a SetUp that
        /// builds a dozen ScriptableObjects to reach one reflection helper would be the
        /// wrong trade.</remarks>
        private static void SetPrivate(Object target, string field, object value)
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

            Assert.Fail("no field '" + field + "' on " + target.GetType().Name);
        }
    }
}
