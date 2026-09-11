using System.Collections.Generic;
using System.Diagnostics;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What one authoritative world tick costs, measured on the production simulation.
    /// </summary>
    /// <remarks>
    /// <b>The production systems, not stand-ins.</b> Every fixture below composes the real
    /// <see cref="WorldSimulation"/> over the real registries, authorities and monster
    /// runtime. A benchmark against a toy loop would measure the toy.
    ///
    /// <b>Two different things are measured here, and neither is allocation traffic.</b>
    /// The first is wall-clock cost per tick, recorded and reported but asserted only
    /// against deliberately generous ceilings, because a test runner sharing a machine with
    /// a compiler is not a measurement instrument. The second is <em>retained</em> managed
    /// heap growth across a measured run: what the world was still holding at the end that
    /// it was not holding at the start.
    ///
    /// <b>Retained growth is not bytes allocated.</b> A tick that allocates a megabyte and
    /// drops it is invisible to <c>GC.GetTotalMemory</c>, and a collection inside the run
    /// makes the figure smaller still. Reading it as "bytes allocated per tick" would be
    /// wrong in the direction that flatters the code, so it is named for what it is and
    /// asserted only as what it can honestly carry: a leak check. How much a tick actually
    /// allocates is a different measurement, taken on a real allocation counter in
    /// <c>WorldSimulationAllocationTests</c>, which needs frame boundaries and therefore
    /// PlayMode.
    ///
    /// <b>What is missing here, honestly.</b> Replication is not composed: it needs a live
    /// FishNet <c>NetworkManager</c>, so snapshot preparation is measured in PlayMode
    /// instead. These numbers are therefore simulation cost, not total server cost.
    ///
    /// <b>Numbers are logged, not just asserted.</b> Each run prints one line per fixture so
    /// a baseline can be captured from the run itself rather than from somebody's memory.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldSimulationPerformanceTests : CharacterCreationTestBase
    {
        private const string HomeMap = "map.home";
        private const string Atk = "stat.atk";
        private const string Grunt = "monster.perf-grunt";

        /// <summary>Ticks measured after the warmup, per fixture.</summary>
        private const int Samples = 240;

        /// <summary>Ticks run before measuring, so nothing first-call is counted.</summary>
        private const int Warmup = 60;

        private const float Delta = 0.05f;

        /// <summary>
        /// What a run may still be holding, per tick, before it counts as a leak.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately loose, because the counter is loose.</b> <c>GC.GetTotalMemory</c>
        /// reports the whole process's managed heap, not this world's, so anything else the
        /// editor does during a run lands in the same number. Measured alone these fixtures
        /// read 0-17 bytes a tick; measured at the end of a three-thousand-test suite an
        /// empty world once read 51, which is the test runner's heap and not the world's.
        ///
        /// A ceiling tight enough to catch that noise fails for reasons that have nothing to
        /// do with the code under test. This one still fails the thing it exists for: a tick
        /// that retains anything real -- a list appended, a subscription never dropped -- is
        /// orders of magnitude above it, because it is retained every tick for two hundred
        /// and forty of them.
        /// </remarks>
        private const long Ceiling = 1024L;

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

        /// <summary>What one fixture's ticks cost.</summary>
        private readonly struct Measurement
        {
            public Measurement(string fixture, int characters, int monsters,
                double medianMicroseconds, double p95Microseconds, long retainedBytesPerTick)
            {
                Fixture = fixture;
                Characters = characters;
                Monsters = monsters;
                MedianMicroseconds = medianMicroseconds;
                P95Microseconds = p95Microseconds;
                RetainedBytesPerTick = retainedBytesPerTick;
            }

            public string Fixture { get; }

            public int Characters { get; }

            public int Monsters { get; }

            public double MedianMicroseconds { get; }

            public double P95Microseconds { get; }

            /// <summary>Heap still held at the end of the run, per tick. Not allocation.</summary>
            public long RetainedBytesPerTick { get; }

            public override string ToString()
            {
                return "[perf] " + Fixture + " characters=" + Characters
                    + " monsters=" + Monsters
                    + " median=" + MedianMicroseconds.ToString("0.00") + "us"
                    + " p95=" + P95Microseconds.ToString("0.00") + "us"
                    + " retainedPerTick=" + RetainedBytesPerTick + "B";
            }
        }

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _monsters;
        private MonsterLootRegistry _loot;
        private WorldSimulation _world;
        private DefinitionRegistry<ItemDefinition> _items;
        private DefinitionRegistry<StatusEffectDefinition> _effects;
        private DefinitionRegistry<MonsterDefinition> _monsterDefinitions;
        private DefinitionRegistry<MapDefinition> _maps;

        [SetUp]
        public void SetUpWorld()
        {
            AddStat(Atk, false);
            Formulas.Add(Formula("f.atk", Atk, 0, new StatTerm(new DefinitionId(Str), 1, 1)));

            _items = new DefinitionRegistry<ItemDefinition>();
            _effects = new DefinitionRegistry<StatusEffectDefinition>();

            _monsterDefinitions = new DefinitionRegistry<MonsterDefinition>();
            _monsterDefinitions.Register(Monster());

            _maps = new DefinitionRegistry<MapDefinition>();
            _maps.Register(Map());

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, _items, 8);

            var status = new CharacterStatusAuthority(_players, _effects);

            var stats = new CharacterStatAuthority(_players, Formulas, Stats, _effects,
                new EquipmentModifierResolver.Context(_items),
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            var movement = new CharacterMovementAuthority(_players, _ => true, _maps, 6f);

            _monsters = new MonsterWorldRuntime(_players, _monsterDefinitions,
                new DefinitionId(MaxHp), new CombatTeam(2), _maps);

            _loot = new MonsterLootRegistry(_players, _items);

            _world = new WorldSimulation(_players, null, status, stats, movement, null,
                _monsters, _loot);
        }

        // ---- the fixtures -------------------------------------------------------------------

        [Test]
        public void SmallWorldTickCost()
        {
            Measurement measured = Measure("SMALL", characters: 1, monsters: 20);

            Report(measured);

            // Deliberately generous: a ceiling that catches an order-of-magnitude
            // regression without failing because a compiler ran on the same machine.
            Assert.That(measured.MedianMicroseconds, Is.LessThan(2000d),
                "a one-character world costs more than two milliseconds a tick");
        }

        [Test]
        public void MediumWorldTickCost()
        {
            Measurement measured = Measure("MEDIUM", characters: 20, monsters: 200);

            Report(measured);

            Assert.That(measured.MedianMicroseconds, Is.LessThan(20000d),
                "a twenty-character world costs more than twenty milliseconds a tick");
        }

        [Test]
        public void StressWorldTickCost()
        {
            Measurement measured = Measure("STRESS", characters: 50, monsters: 500);

            Report(measured);

            Assert.That(measured.MedianMicroseconds, Is.LessThan(60000d),
                "a fifty-character world costs more than sixty milliseconds a tick");
        }

        // ---- what a steady-state run may still be holding afterwards ---------------------------

        [Test]
        public void AnIdleWorldRetainsNothingPerTick()
        {
            // A world where nothing changed -- nobody moved, nothing expired, no reward is
            // owed -- must not be holding more at the end of two hundred and forty ticks
            // than it was at the start. That is a leak check, and all this counter can say.
            Measurement measured = Measure("IDLE", characters: 20, monsters: 200);

            Report(measured);

            Assert.That(measured.RetainedBytesPerTick, Is.LessThanOrEqualTo(Ceiling),
                "a steady-state run still held " + measured.RetainedBytesPerTick
                    + " bytes a tick at the end of it");
        }

        [Test]
        public void AStressWorldRetainsNothingPerTick()
        {
            Measurement measured = Measure("STRESS-RETAINED", characters: 50, monsters: 500);

            Report(measured);

            Assert.That(measured.RetainedBytesPerTick, Is.LessThanOrEqualTo(Ceiling),
                "a stress run still held " + measured.RetainedBytesPerTick
                    + " bytes a tick at the end of it");
        }

        [Test]
        public void AnEmptyWorldRetainsNothingPerTick()
        {
            Measurement measured = Measure("EMPTY", characters: 0, monsters: 0);

            Report(measured);

            Assert.That(measured.RetainedBytesPerTick, Is.LessThanOrEqualTo(Ceiling),
                "an empty world still held " + measured.RetainedBytesPerTick
                    + " bytes a tick at the end of the run");
        }

        // ---- what the caching may not change ------------------------------------------------------

        [Test]
        public void TheWorldStillKnowsExactlyWhoIsInIt()
        {
            // The snapshot is cached on a membership version now. Everything below is the
            // behaviour that was true when it was rebuilt on every call, and has to stay
            // true: what All() answers is who is here, at the moment it is asked.
            Assert.That(_players.All().Count, Is.Zero);

            LivingCharacter first = Admit("char-a", 1);

            Assert.That(_players.All().Count, Is.EqualTo(1));
            Assert.That(_players.All()[0], Is.SameAs(first));

            LivingCharacter second = Admit("char-b", 2);

            Assert.That(_players.All().Count, Is.EqualTo(2),
                "a character who arrived is missing from the world");

            _players.Despawn(1);

            Assert.That(_players.All().Count, Is.EqualTo(1),
                "a character who left is still in the world");
            Assert.That(_players.All()[0], Is.SameAs(second));

            _players.Despawn(2);

            Assert.That(_players.All().Count, Is.Zero);
        }

        [Test]
        public void ASnapshotAlreadyBeingReadIsNotRewrittenUnderneath()
        {
            // Why the cache builds a new list rather than clearing the old one: a caller
            // part way through the world -- the reward authority looking for an owner, say
            // -- must not have its list mutated because somebody logged in.
            Admit("char-a", 1);
            Admit("char-b", 2);

            IReadOnlyList<LivingCharacter> held = _players.All();

            Assert.That(held.Count, Is.EqualTo(2));

            Admit("char-c", 3);

            Assert.That(held.Count, Is.EqualTo(2),
                "a snapshot somebody was reading grew while they read it");
            Assert.That(_players.All().Count, Is.EqualTo(3),
                "the next reader did not see the new character");
        }

        [Test]
        public void TheMonsterWorldStillKnowsExactlyWhichMonstersExist()
        {
            Assert.That(_monsters.All().Count, Is.Zero);

            _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(0f, 0f, 0f), 4f, 3, 30f, new DefinitionId(HomeMap)));

            _monsters.PopulateAll();

            Assert.That(_monsters.All().Count, Is.EqualTo(3));

            // Killing one and letting the runtime retire it must remove it from the answer.
            IReadOnlyList<LivingMonster> alive = _monsters.All();

            InstanceId killed = alive[0].Instance;

            alive[0].State.ApplyHealthDelta(-10000);

            // A corpse is retired once its defeat has been claimed, which is what the
            // reward path does. Killing alone leaves it lying there, by design.
            _monsters.ClaimDefeat(killed, killed, default, null);

            // And the body is left lying for a moment after that, so its death can be seen.
            _monsters.Tick(MonsterWorldRuntime.CorpseLingerSeconds);

            Assert.That(_monsters.All().Count, Is.EqualTo(2),
                "a retired monster is still in the world");

            // And a respawn puts one back.
            _monsters.Tick(31f);

            Assert.That(_monsters.All().Count, Is.EqualTo(3),
                "a respawned monster is missing from the world");
        }

        [Test]
        public void MonsterTargetingSeesCharactersThatArriveMidSimulation()
        {
            // The candidate gather reads the cached snapshot once per spawner. A character
            // who logs in between ticks must be visible to the monsters on the next one.
            _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(0f, 0f, 0f), 1f, 1, 30f, new DefinitionId(HomeMap)));

            _monsters.PopulateAll();

            _world.Tick(0.05f);

            LivingMonster monster = _monsters.All()[0];

            Assert.That(monster.Ai.State, Is.EqualTo(MonsterAiState.Idle),
                "precondition: nothing to chase");

            // Somebody walks in, inside the authored detection radius.
            LivingCharacter hero = Admit("char-a", 1);

            hero.Combatant.Position = new CombatPosition(1f, 0f, 1f);

            _world.Tick(0.05f);

            Assert.That(monster.Ai.State, Is.Not.EqualTo(MonsterAiState.Idle),
                "an aggressive monster never noticed a character who arrived");
        }

        // ---- the measurement itself --------------------------------------------------------------

        private Measurement Measure(string fixture, int characters, int monsters)
        {
            Build(characters, monsters);

            for (var i = 0; i < Warmup; i++) _world.Tick(Delta);

            var samples = new double[Samples];

            var watch = new Stopwatch();

            // RETAINED heap growth across the whole measured run, divided by the tick
            // count. This is not allocation traffic and must never be reported as such:
            // garbage the run created and dropped never appears here, and a collection
            // inside the run pushes the figure down further.
            //
            // What it can honestly say is whether the world ended the run holding more than
            // it began with, which is a leak check -- and that is the only thing asserted
            // against it. The two other instruments were considered and rejected on this
            // runtime: GC.GetAllocatedBytesForCurrentThread returns zero here (verified by
            // allocating ten thousand objects and reading a delta of zero), and
            // ProfilerRecorder's "GC Allocated In Frame" samples at frame boundaries, of
            // which EditMode has none -- so allocation traffic is measured in PlayMode by
            // WorldSimulationAllocationTests instead.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();

            long before = System.GC.GetTotalMemory(true);

            for (var i = 0; i < Samples; i++)
            {
                watch.Restart();

                _world.Tick(Delta);

                watch.Stop();

                samples[i] = watch.Elapsed.TotalMilliseconds * 1000d;
            }

            long retained = System.GC.GetTotalMemory(false) - before;

            if (retained < 0L) retained = 0L;

            System.Array.Sort(samples);

            return new Measurement(fixture, characters, monsters,
                samples[samples.Length / 2],
                samples[(int)(samples.Length * 0.95f)],
                retained / Samples);
        }

        private static void Report(Measurement measured)
        {
            // Logged rather than only asserted, so a baseline is captured from the run.
            Debug.Log(measured.ToString());
        }

        // ---- the world under measurement ------------------------------------------------------------

        private void Build(int characters, int monsters)
        {
            for (var i = 0; i < characters; i++) Admit("char-" + i, i + 1);

            if (monsters <= 0) return;

            // Spread across spawn points the way a real map is, rather than one spawner
            // holding every monster: the per-spawner candidate gather is part of what is
            // being measured.
            const int PerSpawner = 10;

            int spawners = Mathf.Max(1, monsters / PerSpawner);

            for (var i = 0; i < spawners; i++)
            {
                _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                    new CombatPosition(i * 4f, 0f, i * 4f), 8f, PerSpawner, 30f,
                    new DefinitionId(HomeMap)));
            }

            _monsters.PopulateAll();

            Assert.That(_monsters.All().Count, Is.EqualTo(spawners * PerSpawner),
                "the fixture did not populate the monsters it asked for");
        }

        private LivingCharacter Admit(string character, int connection)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, 130, 30,
                    new DefinitionId(Swordsman), default, new DefinitionId(HomeMap),
                    default, Attributes(), null, null, 1, null, 8);
            }

            WorldSpawnResult spawned = _world.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static PersistedStat[] Attributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId(Str), 10),
                new PersistedStat(new DefinitionId(Vit), 8),
            };
        }

        private MonsterDefinition Monster()
        {
            var definition = Track(ScriptableObject.CreateInstance<MonsterDefinition>());

            // Aggressive, with a detection radius, so the target scan is exercised rather
            // than short-circuited by a passive monster that never looks.
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Grunt + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_experienceReward\":10,\"_attackRange\":2,\"_detectionRange\":12,"
                + "\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":30,\"_maxAliveInArea\":10}}",
                definition);

            SetPrivate(definition, "_baseStats",
                new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            return definition;
        }

        private MapDefinition Map()
        {
            var map = Track(ScriptableObject.CreateInstance<MapDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + HomeMap + "\"},\"_movementRadius\":500}", map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = Track(ScriptableObject.CreateInstance<SpawnPointDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            return spawn;
        }
    }
}
