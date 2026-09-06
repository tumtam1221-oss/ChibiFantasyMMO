#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What one authoritative world tick actually allocates, on a counter that counts.
    /// </summary>
    /// <remarks>
    /// <b>Why this fixture is in PlayMode.</b> The only working allocation-traffic counter
    /// on this runtime is <c>ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In
    /// Frame")</c>, and it samples at frame boundaries -- which EditMode tests never cross.
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> returns zero here (verified), and
    /// <c>GC.GetTotalMemory</c> measures the retained heap rather than allocation traffic,
    /// so neither can answer this question.
    ///
    /// <b>The frame's own cost is subtracted.</b> A PlayMode frame allocates on its own
    /// account whatever the world does, so the measurement is a difference: frames that tick
    /// the simulation against frames that do not, divided by the ticks in between. What is
    /// left is the simulation's own traffic.
    ///
    /// <b>Ceilings, not equalities.</b> The residual noise of an Editor PlayMode frame is
    /// not zero and not perfectly repeatable, so these assert budgets a regression would
    /// break rather than exact byte counts. The measured figure is logged either way.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldSimulationAllocationTests
    {
        private const string HomeMap = "map.home";
        private const string Grunt = "monster.alloc-grunt";
        private const string MaxHp = "stat.max_hp";
        private const string MaxMp = "stat.max_mp";
        private const string Str = "stat.str";
        private const string Vit = "stat.vit";

        /// <summary>Frames measured in each half of the comparison.</summary>
        private const int Frames = 120;

        /// <summary>World ticks per measured frame, so the tick cost outweighs the frame's.</summary>
        private const int TicksPerFrame = 20;

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
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            var stats = new DefinitionRegistry<StatDefinition>();
            stats.Register(Stat(Str, true));
            stats.Register(Stat(Vit, true));
            stats.Register(Stat(MaxHp, false));
            stats.Register(Stat(MaxMp, false));

            var formulas = new List<DerivedStatFormulaDefinition>
            {
                Formula("f.maxhp", MaxHp, 10, new StatTerm(new DefinitionId(Vit), 10, 1)),
                Formula("f.maxmp", MaxMp, 10, new StatTerm(new DefinitionId(Str), 2, 1)),
            };

            var items = new DefinitionRegistry<ItemDefinition>();
            var effects = new DefinitionRegistry<StatusEffectDefinition>();

            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(Monster());

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map());

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, items, 8);

            var status = new CharacterStatusAuthority(_players, effects);

            var statAuthority = new CharacterStatAuthority(_players, formulas, stats,
                effects, new EquipmentModifierResolver.Context(items),
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            var movement = new CharacterMovementAuthority(_players, _ => true, maps, 6f);

            _monsters = new MonsterWorldRuntime(_players, monsters, new DefinitionId(MaxHp),
                new CombatTeam(2), maps);

            var loot = new MonsterLootRegistry(_players, items);

            _world = new WorldSimulation(_players, null, status, statAuthority, movement,
                null, _monsters, loot);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the measurements -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMediumWorldTickAllocatesWithinItsBudget()
        {
            Build(characters: 20, monsters: 200);

            yield return Measure("MEDIUM", 20, 200, 4096L);
        }

        [UnityTest]
        public IEnumerator AStressWorldTickAllocatesWithinItsBudget()
        {
            Build(characters: 50, monsters: 500);

            yield return Measure("STRESS", 50, 500, 8192L);
        }

        [UnityTest]
        public IEnumerator AnEmptyWorldTickAllocatesWithinItsBudget()
        {
            yield return Measure("EMPTY", 0, 0, 512L);
        }

        /// <summary>
        /// Ticks against not ticking, on the profiler's own allocation counter.
        /// </summary>
        /// <param name="budget">
        /// A ceiling a regression would break, not an expected value or a target. The
        /// measured figures when these were written were about 1.9kB a tick at MEDIUM and
        /// 4.1kB at STRESS; the budgets sit at roughly double that, because an Editor
        /// PlayMode frame carries its own variable traffic and only its average is
        /// subtracted here. A tick that started allocating twice what it does today would
        /// fail; ordinary noise will not.
        /// </param>
        private IEnumerator Measure(string fixture, int characters, int monsters,
            long budget)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame", Frames * 2 + 8);

            Assert.That(recorder.Valid, Is.True,
                "this runtime has no GC Allocated In Frame counter");

            // Warmup: first-call costs belong to nobody's budget.
            for (var i = 0; i < 30; i++)
            {
                for (var t = 0; t < TicksPerFrame; t++) _world.Tick(0.05f);

                yield return null;
            }

            // Half one: frames that tick nothing, to learn what a frame costs by itself.
            long idle = 0;

            for (var i = 0; i < Frames; i++)
            {
                yield return null;

                idle += Sample(recorder);
            }

            // Half two: the same frames, each running the production tick.
            long busy = 0;

            for (var i = 0; i < Frames; i++)
            {
                for (var t = 0; t < TicksPerFrame; t++) _world.Tick(0.05f);

                yield return null;

                busy += Sample(recorder);
            }

            recorder.Dispose();

            long perTick = (busy - idle) / (Frames * TicksPerFrame);

            if (perTick < 0L) perTick = 0L;

            Debug.Log("[alloc] " + fixture + " characters=" + characters
                + " monsters=" + monsters
                + " idleFrameTotal=" + idle + "B busyFrameTotal=" + busy + "B"
                + " ticks=" + (Frames * TicksPerFrame)
                + " perTick=" + perTick + "B"
                + " counter=GC Allocated In Frame");

            Assert.That(perTick, Is.LessThanOrEqualTo(budget),
                fixture + " allocates " + perTick + " bytes a tick, over its "
                + budget + " byte budget");
        }

        /// <summary>The counter's most recent frame, or zero when it has not sampled yet.</summary>
        private static long Sample(ProfilerRecorder recorder)
        {
            return recorder.Valid && recorder.Count > 0 ? recorder.LastValue : 0L;
        }

        // ---- the world under measurement -------------------------------------------------------

        private void Build(int characters, int monsters)
        {
            for (var i = 0; i < characters; i++) Admit("char-" + i, i + 1);

            if (monsters <= 0) return;

            const int PerSpawner = 10;

            int spawners = Mathf.Max(1, monsters / PerSpawner);

            for (var i = 0; i < spawners; i++)
            {
                _monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Grunt),
                    new CombatPosition(i * 4f, 0f, i * 4f), 8f, PerSpawner, 30f,
                    new DefinitionId(HomeMap)));
            }

            _monsters.PopulateAll();

            Assert.That(_monsters.All().Count, Is.EqualTo(spawners * PerSpawner));
        }

        private void Admit(string character, int connection)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, 130, 30,
                    new DefinitionId("class.swordsman"), default,
                    new DefinitionId(HomeMap), default, new[]
                    {
                        new PersistedStat(new DefinitionId(Str), 10),
                        new PersistedStat(new DefinitionId(Vit), 8),
                    }, null, null, 1, null, 8);
            }

            WorldSpawnResult spawned = _world.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);
        }

        // ---- fixture content ---------------------------------------------------------------------

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);

            return created;
        }

        private StatDefinition Stat(string id, bool primary)
        {
            var definition = Track(ScriptableObject.CreateInstance<StatDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id
                + "\"},\"_isPrimaryAttribute\":" + (primary ? "true" : "false")
                + ",\"_minimum\":0,\"_maximum\":100000}", definition);

            return definition;
        }

        private DerivedStatFormulaDefinition Formula(string id, string derived, int constant,
            StatTerm term)
        {
            var definition =
                Track(ScriptableObject.CreateInstance<DerivedStatFormulaDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_derivedStat\":{\"_value\":\""
                + derived + "\"},\"_constant\":" + constant + "}", definition);

            typeof(DerivedStatFormulaDefinition)
                .GetField("_terms", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, new[] { term });

            return definition;
        }

        private MonsterDefinition Monster()
        {
            var definition = Track(ScriptableObject.CreateInstance<MonsterDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Grunt + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_experienceReward\":10,\"_attackRange\":2,\"_detectionRange\":12,"
                + "\"_leashRange\":40,\"_moveSpeed\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":30,\"_maxAliveInArea\":10}}",
                definition);

            typeof(MonsterDefinition)
                .GetField("_baseStats", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition,
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

#endif
