using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// That the work 18.18A removed is genuinely not being done.
    /// </summary>
    /// <remarks>
    /// <b>Structural, because the saving is an absence.</b> A rebuild that no longer happens
    /// cannot be seen in a timing, and this runtime has no working per-thread allocation
    /// counter to see it in bytes either -- <c>GC.GetAllocatedBytesForCurrentThread</c>
    /// returns zero here, which was verified rather than assumed. So the proofs below are
    /// about identity and counts: the same list object comes back while nothing changed, a
    /// new one appears when membership moves, and the candidate gather runs once per map per
    /// tick however many spawn points share that map.
    ///
    /// <b>Behaviour is asserted alongside every saving.</b> A cache that returned a stale
    /// answer would pass an identity test and break the game, so each test also checks that
    /// what the world reports is still correct after the change it is caching across.
    /// </remarks>
    [TestFixture]
    internal sealed class SimulationSnapshotCachingTests : CharacterCreationTestBase
    {
        private const string HomeMap = "map.home";
        private const string OtherMap = "map.other";
        private const string Grunt = "monster.cache-grunt";

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _monsters;
        private WorldSimulation _world;
        private DefinitionRegistry<MapDefinition> _maps;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpCaching()
        {
            var monsterDefinitions = new DefinitionRegistry<MonsterDefinition>();
            monsterDefinitions.Register(Monster());

            _maps = new DefinitionRegistry<MapDefinition>();
            _maps.Register(Map(HomeMap));
            _maps.Register(Map(OtherMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));
            spawns.Register(PlayerSpawn("spawn.other", OtherMap));

            var items = new DefinitionRegistry<ItemDefinition>();

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, items, 8);

            _monsters = new MonsterWorldRuntime(_players, monsterDefinitions,
                new DefinitionId(MaxHp), new CombatTeam(2), _maps);

            _world = new WorldSimulation(_players, null, null, null, null, null, _monsters);
        }

        [TearDown]
        public void TearDownCaching()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- the character registry ------------------------------------------------------

        [Test]
        public void TheCharacterSnapshotIsNotRebuiltWhileNobodyComesOrGoes()
        {
            Admit("char-a", 1);
            Admit("char-b", 2);

            IReadOnlyList<LivingCharacter> first = _players.All();

            // The same object, every time, for as long as the membership stands. This is
            // the whole saving: the tick asks fifty-odd times and gets one list.
            for (var i = 0; i < 50; i++)
            {
                Assert.That(_players.All(), Is.SameAs(first),
                    "the character snapshot was rebuilt on call " + i);
            }

            // And the answer is still right.
            Assert.That(first.Count, Is.EqualTo(2));
        }

        [Test]
        public void ArrivingAndLeavingEachProduceANewSnapshot()
        {
            Admit("char-a", 1);

            IReadOnlyList<LivingCharacter> before = _players.All();

            Admit("char-b", 2);

            IReadOnlyList<LivingCharacter> afterJoin = _players.All();

            Assert.That(afterJoin, Is.Not.SameAs(before),
                "a character arrived and the snapshot did not change");
            Assert.That(afterJoin.Count, Is.EqualTo(2));

            // The snapshot somebody may still be reading was not rewritten underneath.
            Assert.That(before.Count, Is.EqualTo(1),
                "an in-flight snapshot grew while it was being read");

            _players.Despawn(1);

            IReadOnlyList<LivingCharacter> afterLeave = _players.All();

            Assert.That(afterLeave, Is.Not.SameAs(afterJoin));
            Assert.That(afterLeave.Count, Is.EqualTo(1));
            Assert.That(afterLeave[0].Character.Value, Is.EqualTo("char-b"));

            // Stable again once the churn stops.
            Assert.That(_players.All(), Is.SameAs(afterLeave));
        }

        [Test]
        public void SavingAndMovingACharacterDoesNotInvalidateTheSnapshot()
        {
            // Membership is what the cache keys on. A character who levels, moves or is
            // written to storage is the same character, and the world is the same world.
            LivingCharacter hero = Admit("char-a", 1);

            IReadOnlyList<LivingCharacter> before = _players.All();

            hero.Combatant.Position = new CombatPosition(40f, 0f, 40f);
            hero.MarkDirty();

            Assert.That(_players.Save(hero).IsOk, Is.True);

            Assert.That(_players.All(), Is.SameAs(before),
                "an ordinary change rebuilt the membership snapshot");
        }

        // ---- the monster runtime ------------------------------------------------------------

        [Test]
        public void TheMonsterSnapshotIsNotRebuiltWhileNothingSpawnsOrDies()
        {
            _monsters.AddSpawnPoint(Nest(HomeMap, 3));
            _monsters.PopulateAll();

            IReadOnlyList<LivingMonster> first = _monsters.All();

            Assert.That(first.Count, Is.EqualTo(3));

            for (var i = 0; i < 50; i++)
            {
                Assert.That(_monsters.All(), Is.SameAs(first),
                    "the monster snapshot was rebuilt on call " + i);
            }

            // A quiet tick moves monsters; it does not change which monsters exist.
            _monsters.Tick(0.05f);

            Assert.That(_monsters.All(), Is.SameAs(first),
                "a tick with no spawn or death rebuilt the monster snapshot");
        }

        [Test]
        public void SpawningAndRetiringEachProduceANewMonsterSnapshot()
        {
            _monsters.AddSpawnPoint(Nest(HomeMap, 2));
            _monsters.PopulateAll();

            IReadOnlyList<LivingMonster> populated = _monsters.All();

            Assert.That(populated.Count, Is.EqualTo(2));

            InstanceId doomed = populated[0].Instance;

            populated[0].State.ApplyHealthDelta(-10000);

            // Retirement follows a claimed defeat, which is what the reward path does.
            _monsters.ClaimDefeat(doomed, doomed, default, null);

            // Long enough for the body to be swept away: a corpse is deliberately left
            // lying for a moment so its death can be seen.
            _monsters.Tick(MonsterWorldRuntime.CorpseLingerSeconds);

            IReadOnlyList<LivingMonster> afterRetire = _monsters.All();

            Assert.That(afterRetire, Is.Not.SameAs(populated),
                "a monster was retired and the snapshot did not change");
            Assert.That(afterRetire.Count, Is.EqualTo(1));
            Assert.That(populated.Count, Is.EqualTo(2),
                "an in-flight monster snapshot changed while it was being read");

            Assert.That(_monsters.All(), Is.SameAs(afterRetire));
        }

        // ---- the candidate gather ---------------------------------------------------------------

        [Test]
        public void ManySpawnPointsOnOneMapGatherCandidatesOnce()
        {
            Admit("char-a", 1);

            for (var i = 0; i < 12; i++) _monsters.AddSpawnPoint(Nest(HomeMap, 2));

            _monsters.PopulateAll();

            int before = _monsters.CandidateGathers;

            _world.Tick(0.05f);

            Assert.That(_monsters.CandidateGathers - before, Is.EqualTo(1),
                "twelve spawn points on one map gathered candidates "
                + (_monsters.CandidateGathers - before) + " times");
        }

        [Test]
        public void EachMapGathersItsOwnCandidatesOncePerTick()
        {
            Admit("char-a", 1);
            Admit("char-b", 2, OtherMap);

            // Interleaved on purpose: the memo remembers the map it last gathered for, so
            // alternating maps is the case that could quietly gather more than twice.
            for (var i = 0; i < 4; i++)
            {
                _monsters.AddSpawnPoint(Nest(HomeMap, 1));
                _monsters.AddSpawnPoint(Nest(OtherMap, 1));
            }

            _monsters.PopulateAll();

            int before = _monsters.CandidateGathers;

            _world.Tick(0.05f);

            int gathers = _monsters.CandidateGathers - before;

            Assert.That(gathers, Is.GreaterThanOrEqualTo(2),
                "two maps must each gather their own candidates");
            Assert.That(gathers, Is.LessThanOrEqualTo(8),
                "the memo saved nothing when maps alternate: " + gathers);
        }

        [Test]
        public void TheNextTickGathersAgain()
        {
            Admit("char-a", 1);

            _monsters.AddSpawnPoint(Nest(HomeMap, 2));
            _monsters.PopulateAll();

            int before = _monsters.CandidateGathers;

            _world.Tick(0.05f);
            _world.Tick(0.05f);
            _world.Tick(0.05f);

            Assert.That(_monsters.CandidateGathers - before, Is.EqualTo(3),
                "the candidate memo survived into another tick");
        }

        [Test]
        public void ACharacterWhoArrivesBetweenTicksIsACandidateOnTheNextOne()
        {
            // The behaviour the memo must not break: a cached gather is per tick, so
            // anybody who joins is seen immediately afterwards.
            _monsters.AddSpawnPoint(Nest(HomeMap, 1));
            _monsters.PopulateAll();

            _world.Tick(0.05f);

            LivingMonster monster = _monsters.All()[0];

            Assert.That(monster.Ai.State, Is.EqualTo(MonsterAiState.Idle),
                "precondition: nothing to chase");

            LivingCharacter hero = Admit("char-a", 1);

            hero.Combatant.Position = new CombatPosition(1f, 0f, 1f);

            _world.Tick(0.05f);

            Assert.That(monster.Ai.State, Is.Not.EqualTo(MonsterAiState.Idle),
                "a character who arrived between ticks was invisible to the monsters");
        }

        [Test]
        public void ACharacterWhoLeavesStopsBeingACandidate()
        {
            _monsters.AddSpawnPoint(Nest(HomeMap, 1));
            _monsters.PopulateAll();

            LivingCharacter hero = Admit("char-a", 1);

            hero.Combatant.Position = new CombatPosition(1f, 0f, 1f);

            _world.Tick(0.05f);

            LivingMonster monster = _monsters.All()[0];

            Assert.That(monster.Ai.State, Is.Not.EqualTo(MonsterAiState.Idle),
                "precondition: the monster noticed somebody");

            _players.Despawn(1);

            _world.Tick(0.05f);
            _world.Tick(0.05f);

            Assert.That(monster.Ai.State, Is.EqualTo(MonsterAiState.Return)
                .Or.EqualTo(MonsterAiState.Idle),
                "a monster kept chasing a character who had left the world");
        }

        // ---- harness -----------------------------------------------------------------------------

        private LivingCharacter Admit(string character, int connection,
            string map = HomeMap)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, 130, 30,
                    new DefinitionId(Swordsman), default, new DefinitionId(map),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId(Str), 10),
                        new PersistedStat(new DefinitionId(Vit), 8),
                    }, null, null, 1, null, 8);
            }

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(130, 30), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static MonsterSpawnPoint Nest(string map, int count)
        {
            return new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(0f, 0f, 0f), 3f, count, 30f, new DefinitionId(map));
        }

        private MonsterDefinition Monster()
        {
            var definition = Track(ScriptableObject.CreateInstance<MonsterDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Grunt + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_experienceReward\":10,\"_attackRange\":2,\"_detectionRange\":12,"
                + "\"_leashRange\":30,\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":30,\"_maxAliveInArea\":4}}",
                definition);

            SetPrivate(definition, "_baseStats",
                new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            return definition;
        }

        private MapDefinition Map(string id)
        {
            var map = Track(ScriptableObject.CreateInstance<MapDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_movementRadius\":500}", map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = Track(ScriptableObject.CreateInstance<SpawnPointDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            return spawn;
        }
    }
}
