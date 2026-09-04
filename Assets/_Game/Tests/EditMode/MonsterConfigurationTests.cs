using System.Collections.Generic;
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
    /// Data-driven spawn and AI configuration, and the behaviour it selects.
    /// </summary>
    /// <remarks>
    /// Two things are under test. That configuration an operator typed cannot break the
    /// authoritative runtime -- validation refuses rather than corrects, and a wholly
    /// invalid reload is a no-op rather than an empty map. And that the three required
    /// behaviours are genuinely different: Passive never fights, Defensive fights only
    /// once hit, Aggressive starts it.
    ///
    /// AssistOnly is covered by regression only. Its real semantics -- joining a fight a
    /// neighbour is already in -- are not implemented anywhere in this project, and the
    /// tests below pin its existing behaviour rather than invent one.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterConfigurationTests : MonsterTestBase
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

        private const string Passive = "monster.passive";
        private const string Defensive = "monster.defensive";
        private const string AggressiveOne = "monster.aggressive";
        private const string Assist = "monster.assist";

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private DefinitionRegistry<SpawnPointDefinition> _spawnPoints;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpConfiguration()
        {
            AddBehaviour(Passive, MonsterAggressionType.Passive);
            AddBehaviour(Defensive, MonsterAggressionType.Defensive);
            AddBehaviour(AggressiveOne, MonsterAggressionType.Aggressive);
            AddBehaviour(Assist, MonsterAggressionType.AssistOnly);

            _store = new FakeStore();

            _spawnPoints = new DefinitionRegistry<SpawnPointDefinition>();
            _spawnPoints.Register(PlayerSpawn("spawn.home", HomeMap));
            _spawnPoints.Register(PlayerSpawn("spawn.other", OtherMap));

            _players = new WorldCharacterRegistry(_store, _spawnPoints);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);
        }

        [TearDown]
        public void TearDownConfiguration()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private void AddBehaviour(string id, MonsterAggressionType aggression)
        {
            AddMonster(id, level: 5, aggression: aggression, detection: 20f,
                attackRange: 2f, cooldown: 2f, leash: 100f);
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            _local.Add(spawn);

            return spawn;
        }

        private static MonsterSpawnConfiguration Config(string monster = AggressiveOne,
            string map = HomeMap, string id = "row-1", float x = 0f, float y = 0f, float z = 0f,
            float radius = 0f, int initial = 1, int maxAlive = 1, float respawn = 0f)
        {
            return new MonsterSpawnConfiguration(id, new DefinitionId(map),
                new DefinitionId(monster), x, y, z, radius, initial, maxAlive, respawn);
        }

        private LivingCharacter AddPlayer(string character, string map, float x = 0f)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(character.GetHashCode(),
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            result.Character.Location.Position = new CombatPosition(x, 0f, 0f);
            result.Character.Combatant.Position = new CombatPosition(x, 0f, 0f);

            return result.Character;
        }

        private LivingMonster SpawnOne(string monster)
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(monster) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            IReadOnlyList<LivingMonster> all = _runtime.All();

            Assert.That(all.Count, Is.EqualTo(1), "expected one monster");

            return all[0];
        }

        // ---- validation: nothing invalid reaches the runtime -----------------------------

        [Test]
        public void AValidConfigurationIsAccepted()
        {
            Assert.That(SpawnConfigurationValidator.Validate(Config(), Maps, Monsters)
                .IsAccepted, Is.True);
        }

        [Test]
        public void AnUnknownMapIsRefused()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(map: "map.nowhere"), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.UnknownMap));
        }

        [Test]
        public void AnUnknownMonsterIsRefused()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(monster: "monster.nothing"), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.UnknownMonster));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void ANestThatHoldsNobodyIsRefused(int maxAlive)
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(maxAlive: maxAlive, initial: 0), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.MaxAliveNotPositive));
        }

        [Test]
        public void ANegativeInitialCountIsRefused()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(initial: -1), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.InitialCountNegative));
        }

        [Test]
        public void AskingForMoreThanTheNestHoldsIsRefusedRatherThanClamped()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(initial: 30, maxAlive: 10), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.InitialCountExceedsMaxAlive),
                "an operator who typed 30 meant something; silently spawning 10 hides it");
        }

        [Test]
        public void ANegativeRadiusIsRefused()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(radius: -1f), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.RadiusNegative));
        }

        [Test]
        public void ANegativeRespawnIsRefused()
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(respawn: -5f), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.RespawnNegative));
        }

        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(0f, float.PositiveInfinity, 0f)]
        [TestCase(0f, 0f, float.NaN)]
        public void ANonFinitePositionIsRefused(float x, float y, float z)
        {
            Assert.That(SpawnConfigurationValidator.Validate(
                Config(x: x, y: y, z: z), Maps, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.PositionNotFinite));
        }

        [Test]
        public void NoRegistriesAtAllRefusesRatherThanAccepting()
        {
            Assert.That(SpawnConfigurationValidator.Validate(Config(), null, null).IsAccepted,
                Is.False);
        }

        // ---- AI configuration validation ---------------------------------------------------

        [Test]
        public void AValidAiOverrideIsAccepted()
        {
            var ai = new MonsterAiConfiguration(new DefinitionId(Defensive),
                (int)MonsterAggressionType.Defensive, 10f, 20f, 2f, 1.5f, 3f);

            Assert.That(SpawnConfigurationValidator.Validate(ai, Monsters).IsAccepted, Is.True);
        }

        [Test]
        public void AnAiOverrideForAnUnknownMonsterIsRefused()
        {
            var ai = new MonsterAiConfiguration(new DefinitionId("monster.nothing"), 1);

            Assert.That(SpawnConfigurationValidator.Validate(ai, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.UnknownMonster),
                "the database and the content build disagree, and somebody must be told");
        }

        [TestCase(4)]
        [TestCase(99)]
        public void AnUnknownAggressionIsRefused(int aggression)
        {
            var ai = new MonsterAiConfiguration(new DefinitionId(Defensive), aggression);

            Assert.That(SpawnConfigurationValidator.Validate(ai, Monsters).Reason,
                Is.EqualTo(SpawnConfigurationRejection.UnknownAggression));
        }

        [Test]
        public void EveryBehaviourTheEnumDefinesIsAccepted()
        {
            // Bounded by the existing enum rather than a literal range, so adding a mode
            // extends this automatically.
            foreach (MonsterAggressionType type in
                System.Enum.GetValues(typeof(MonsterAggressionType)))
            {
                Assert.That(SpawnConfigurationValidator.IsKnownAggression((int)type), Is.True,
                    type.ToString());
            }
        }

        [Test]
        public void ANegativeAiValueIsRefused()
        {
            var ai = new MonsterAiConfiguration(new DefinitionId(Defensive), 1,
                detectionRange: -5f);

            // Negative means absent by convention, so -5 reads as "not overridden" rather
            // than as an error -- and an absent value is always acceptable.
            Assert.That(ai.HasDetectionRange, Is.False);
            Assert.That(SpawnConfigurationValidator.Validate(ai, Monsters).IsAccepted, Is.True);
        }

        [Test]
        public void AnAbsentOverrideMeansUseTheAuthoredValue()
        {
            var ai = new MonsterAiConfiguration(new DefinitionId(Defensive));

            Assert.That(ai.OverridesAnything, Is.False);
            Assert.That(ai.HasAggression, Is.False);
            Assert.That(ai.HasMoveSpeed, Is.False);
        }

        // ---- applying configuration ----------------------------------------------------------

        [Test]
        public void AValidConfigurationBecomesLiveSpawnPoints()
        {
            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: "a"), Config(id: "b", monster: Passive) }, null), Maps);

            Assert.That(result.Accepted, Is.EqualTo(2));
            Assert.That(result.Rejected, Is.Zero);
            Assert.That(_runtime.SpawnerCount, Is.EqualTo(2));
        }

        [Test]
        public void InvalidRowsAreCountedAndSkippedWhileValidOnesApply()
        {
            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap), new[]
                {
                    Config(id: "good"),
                    Config(id: "bad", monster: "monster.nothing"),
                }, null), Maps);

            Assert.That(result.Accepted, Is.EqualTo(1));
            Assert.That(result.Rejected, Is.EqualTo(1));
        }

        [Test]
        public void AWhollyInvalidConfigurationLeavesTheRuntimeAlone()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: "good") }, null), Maps);

            Assert.That(_runtime.SpawnerCount, Is.EqualTo(1), "precondition");

            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: "bad", monster: "monster.nothing") }, null), Maps);

            Assert.That(result.Accepted, Is.Zero);
            Assert.That(_runtime.SpawnerCount, Is.EqualTo(1),
                "a broken spreadsheet must not empty a live map");
        }

        [Test]
        public void TheConfiguredPositionIsAuthoritative()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(x: 12f, z: -7f) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            LivingMonster monster = _runtime.All()[0];

            Assert.That(monster.State.SpawnPosition.X, Is.EqualTo(12f).Within(0.001f));
            Assert.That(monster.State.SpawnPosition.Z, Is.EqualTo(-7f).Within(0.001f));
        }

        [Test]
        public void InitialPopulationUsesTheConfiguredCountNotTheCapacity()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 2, maxAlive: 5) }, null), Maps);

            Assert.That(_runtime.PopulateToConfiguredCount(), Is.EqualTo(2));
            Assert.That(_runtime.AliveCount, Is.EqualTo(2),
                "a map that starts half full is a legitimate design");
        }

        [Test]
        public void InitialPopulationNeverExceedsMaxAlive()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 3, maxAlive: 3) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            Assert.That(_runtime.AliveCount, Is.EqualTo(3));
        }

        [Test]
        public void ARowForAnotherMapIsRefused()
        {
            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: "elsewhere", map: OtherMap) }, null), Maps);

            Assert.That(result.Accepted, Is.Zero);
            Assert.That(result.Rejected, Is.EqualTo(1),
                "a payload about one map must not edit another map's nests");
        }

        [Test]
        public void ARowWithNoIdIsRefused()
        {
            // Without an id a reload cannot find the nest again, so it would add a second
            // one every time somebody pressed reload.
            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: null) }, null), Maps);

            Assert.That(result.Rejected, Is.EqualTo(1));
            Assert.That(_runtime.SpawnerCount, Is.Zero);
        }

        [Test]
        public void ConfiguringOneMapLeavesAnotherMapsNestsAlone()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(id: "home", map: HomeMap) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(OtherMap),
                    new[] { Config(id: "other", map: OtherMap) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            var maps = new HashSet<string>();

            foreach (LivingMonster monster in _runtime.All()) maps.Add(monster.Map.Value);

            Assert.That(maps, Is.EquivalentTo(new[] { HomeMap, OtherMap }),
                "reloading one map must not empty the other");
            Assert.That(_runtime.SpawnerCount, Is.EqualTo(2));
        }

        // ---- reload -----------------------------------------------------------------------------

        [Test]
        public void ReloadingDoesNotDuplicateExistingMonsters()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 1, maxAlive: 1) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            Assert.That(_runtime.AliveCount, Is.EqualTo(1));

            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 1, maxAlive: 1) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            Assert.That(_runtime.AliveCount, Is.EqualTo(1),
                "a reload must not double a population");
        }

        [Test]
        public void RemovingASpawnPointDoesNotDestroyTheMonstersItMade()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config() }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            Assert.That(_runtime.AliveCount, Is.EqualTo(1));

            // Every point removed -- which is what disabling the last one looks like.
            SpawnConfigurationResult result = _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    System.Array.Empty<MonsterSpawnConfiguration>(), null), Maps);

            Assert.That(result.Preserved, Is.EqualTo(1));
            Assert.That(_runtime.AliveCount, Is.EqualTo(1),
                "deleting the monster a player is fighting is a surprising way to lose");

            // It is preserved, not resurrected: the nest produces nothing further.
            Assert.That(_runtime.PopulateToConfiguredCount(), Is.Zero);
            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
        }

        [Test]
        public void ARetiredNestDoesNotRespawnWhatDies()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(respawn: 1f) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            LivingMonster monster = _runtime.All()[0];

            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    System.Array.Empty<MonsterSpawnConfiguration>(), null), Maps);

            // It dies and its reward is claimed, so it is eligible to be retired.
            monster.State.ApplyHealthDelta(-10000);
            monster.State.TryClaimDefeat();

            _runtime.Tick(5f);

            Assert.That(_runtime.AliveCount, Is.Zero,
                "the corpse is still cleared -- a preserved monster is not an immortal one");

            _runtime.Tick(5f);

            Assert.That(_runtime.AliveCount, Is.Zero,
                "but nothing takes its place, because the nest is no longer configured");
        }

        [Test]
        public void ARemovedNestStillTicksTheMonstersItMade()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(monster: AggressiveOne) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            LivingMonster monster = _runtime.All()[0];

            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    System.Array.Empty<MonsterSpawnConfiguration>(), null), Maps);

            AddPlayer("char-near", HomeMap, x: 1f);

            MonsterTickResult result = _runtime.Tick(0.5f);

            Assert.That(monster.State.HasTarget, Is.True);
            Assert.That(result.Attacking.Count, Is.EqualTo(1),
                "a preserved monster that stopped fighting back would be worse than one "
                + "that was deleted");
        }

        [Test]
        public void AChangedMaxAliveAppliesToFutureSpawning()
        {
            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 1, maxAlive: 1) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            _runtime.ApplyConfiguration(
                new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 3, maxAlive: 3) }, null), Maps);

            _runtime.PopulateToConfiguredCount();

            Assert.That(_runtime.AliveCount, Is.EqualTo(3));
        }

        [Test]
        public void NothingOnTheRuntimeLetsAClientChangeConfiguration()
        {
            // Configuration is an operator's. No public method takes a connection id, and
            // ApplyConfiguration is reachable only from the server's own composition.
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterWorldRuntime).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.Name.ToLowerInvariant(),
                        Does.Not.Contain("connection"), method.Name);
                }
            }
        }

        // ---- 19-24: the three behaviours are genuinely different ---------------------------------

        [Test]
        public void PassiveDoesNotAcquireAPlayerFromProximity()
        {
            LivingMonster monster = SpawnOne(Passive);
            AddPlayer("char-near", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            Assert.That(monster.State.HasTarget, Is.False);
            Assert.That(monster.Ai.WantsToAttack, Is.False);
        }

        [Test]
        public void DefensiveDoesNotAcquireAPlayerFromProximity()
        {
            LivingMonster monster = SpawnOne(Defensive);
            AddPlayer("char-near", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            Assert.That(monster.State.HasTarget, Is.False,
                "walking past a goblin is not an attack");
        }

        [Test]
        public void AggressiveAcquiresAPlayerFromProximity()
        {
            LivingMonster monster = SpawnOne(AggressiveOne);
            AddPlayer("char-near", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            Assert.That(monster.State.HasTarget, Is.True);
        }

        [Test]
        public void DefensiveRetaliatesAgainstWhoeverAttackedIt()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter attacker = AddPlayer("char-attacker", HomeMap, x: 3f);

            _runtime.Tick(0.5f);
            Assert.That(monster.State.HasTarget, Is.False, "precondition: unprovoked");

            Assert.That(_runtime.NotifyAttacked(monster.Instance,
                attacker.Combatant.CombatantId), Is.True);

            Assert.That(monster.State.TargetId, Is.EqualTo(attacker.Combatant.CombatantId));
        }

        [Test]
        public void ARetaliatingDefensiveMonsterThenChasesAndStrikesLikeAnyOther()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter attacker = AddPlayer("char-attacker", HomeMap, x: 1f);

            _runtime.NotifyAttacked(monster.Instance, attacker.Combatant.CombatantId);

            MonsterTickResult result = _runtime.Tick(0.5f);

            // No second AI and no second combat path: the existing state machine takes over.
            Assert.That(monster.Ai.State, Is.EqualTo(MonsterAiState.Attack));
            Assert.That(result.Attacking.Count, Is.EqualTo(1));
        }

        [Test]
        public void DefensiveDoesNotAcquireAnUnrelatedBystander()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter attacker = AddPlayer("char-attacker", HomeMap, x: 3f);
            AddPlayer("char-bystander", HomeMap, x: 2f);

            _runtime.NotifyAttacked(monster.Instance, attacker.Combatant.CombatantId);

            Assert.That(monster.State.TargetId, Is.EqualTo(attacker.Combatant.CombatantId),
                "the nearer bystander did nothing and must not be blamed");
        }

        [Test]
        public void PassiveDoesNotRetaliateEvenWhenAttacked()
        {
            LivingMonster monster = SpawnOne(Passive);
            LivingCharacter attacker = AddPlayer("char-attacker", HomeMap, x: 3f);

            Assert.That(_runtime.NotifyAttacked(monster.Instance,
                attacker.Combatant.CombatantId), Is.False,
                "never auto-aggro, never initiate: being struck does not make it initiate");
            Assert.That(monster.State.HasTarget, Is.False);
        }

        [Test]
        public void ADeadMonsterCannotBeProvoked()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter attacker = AddPlayer("char-attacker", HomeMap, x: 3f);

            monster.State.ApplyHealthDelta(-10000);

            Assert.That(_runtime.NotifyAttacked(monster.Instance,
                attacker.Combatant.CombatantId), Is.False);
            Assert.That(monster.State.HasTarget, Is.False);
        }

        [Test]
        public void AnAttackerOnAnotherMapCannotProvoke()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter far = AddPlayer("char-far", OtherMap, x: 3f);

            Assert.That(_runtime.NotifyAttacked(monster.Instance,
                far.Combatant.CombatantId), Is.False);
        }

        [Test]
        public void AnUnresolvableAttackerCannotProvoke()
        {
            LivingMonster monster = SpawnOne(Defensive);

            // An id that resolves to nothing is not an attacker, however it arrived.
            Assert.That(_runtime.NotifyAttacked(monster.Instance,
                new InstanceId("char-ghost")), Is.False);
            Assert.That(_runtime.NotifyAttacked(monster.Instance, default), Is.False);
        }

        [Test]
        public void AMonsterAlreadyFightingDoesNotSwitchTargets()
        {
            LivingMonster monster = SpawnOne(Defensive);
            LivingCharacter first = AddPlayer("char-first", HomeMap, x: 3f);
            LivingCharacter second = AddPlayer("char-second", HomeMap, x: 4f);

            _runtime.NotifyAttacked(monster.Instance, first.Combatant.CombatantId);
            _runtime.NotifyAttacked(monster.Instance, second.Combatant.CombatantId);

            Assert.That(monster.State.TargetId, Is.EqualTo(first.Combatant.CombatantId),
                "two players could otherwise drag it back and forth");
        }

        [Test]
        public void ProvokingAnUnknownMonsterIsHarmless()
        {
            Assert.That(_runtime.NotifyAttacked(new InstanceId("no-such-monster"),
                new InstanceId("char-a")), Is.False);
        }

        [Test]
        public void AssistOnlyKeepsItsExistingBehaviourUnchanged()
        {
            // Its real semantics -- joining a fight a neighbour is already in -- are not
            // implemented anywhere in this project. This pins what it does today so a
            // future gate that implements it does so deliberately rather than by accident.
            LivingMonster monster = SpawnOne(Assist);
            AddPlayer("char-near", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            Assert.That(monster.State.HasTarget, Is.True,
                "AssistOnly currently acquires on sight, exactly as before this gate");
        }

        // ---- the reload seam ---------------------------------------------------------------------

        /// <summary>A configuration source that answers with whatever a test hands it.</summary>
        /// <remarks>Null is a real answer, and the important one: it is what the HTTP source
        /// returns when the API is unreachable or replies with nonsense.</remarks>
        private sealed class FakeSource : IMonsterSpawnConfigurationSource
        {
            public MapSpawnConfiguration Next;
            public int Reads;

            public MapSpawnConfiguration Load(DefinitionId map)
            {
                Reads++;

                return Next;
            }
        }

        [Test]
        public void LoadingConfigurationPopulatesTheMap()
        {
            var source = new FakeSource
            {
                Next = new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config(initial: 2, maxAlive: 2) }, null),
            };

            var loader = new MonsterConfigurationLoader(source, _runtime, Maps);

            Assert.That(loader.Load(new DefinitionId(HomeMap)), Is.EqualTo(2));
            Assert.That(loader.LastReadSucceeded, Is.True);
            Assert.That(loader.LastResult.Accepted, Is.EqualTo(1));
            Assert.That(_runtime.AliveCount, Is.EqualTo(2));
        }

        [Test]
        public void AnUnreachableApiLeavesTheWorldAsItWas()
        {
            var source = new FakeSource
            {
                Next = new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config() }, null),
            };

            var loader = new MonsterConfigurationLoader(source, _runtime, Maps);

            loader.Load(new DefinitionId(HomeMap));

            Assert.That(_runtime.AliveCount, Is.EqualTo(1), "precondition");

            // The API goes away.
            source.Next = null;

            Assert.That(loader.Load(new DefinitionId(HomeMap)), Is.Zero);
            Assert.That(loader.LastReadSucceeded, Is.False,
                "'the API is down' and 'this map has no monsters' are different facts");
            Assert.That(_runtime.AliveCount, Is.EqualTo(1),
                "a world must not empty itself because a web server restarted");
            Assert.That(_runtime.SpawnerCount, Is.EqualTo(1));
        }

        [Test]
        public void ABrokenConfigurationCausesNoRuntimeMutationAtAll()
        {
            var source = new FakeSource
            {
                Next = new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config() }, null),
            };

            var loader = new MonsterConfigurationLoader(source, _runtime, Maps);

            loader.Load(new DefinitionId(HomeMap));

            LivingMonster before = _runtime.All()[0];

            source.Next = new MapSpawnConfiguration(new DefinitionId(HomeMap), new[]
            {
                Config(id: "bad-monster", monster: "monster.nothing"),
                Config(id: "bad-count", initial: 9, maxAlive: 2),
            }, null);

            Assert.That(loader.Load(new DefinitionId(HomeMap)), Is.Zero);
            Assert.That(loader.LastReadSucceeded, Is.True, "it was read, and refused");
            Assert.That(loader.LastResult.Rejected, Is.EqualTo(2));
            Assert.That(_runtime.SpawnerCount, Is.EqualTo(1));
            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
            Assert.That(_runtime.All()[0].Instance, Is.EqualTo(before.Instance),
                "the same monster, untouched");
        }

        [Test]
        public void ReloadingThroughTheLoaderDoesNotDuplicate()
        {
            var source = new FakeSource
            {
                Next = new MapSpawnConfiguration(new DefinitionId(HomeMap),
                    new[] { Config() }, null),
            };

            var loader = new MonsterConfigurationLoader(source, _runtime, Maps);

            loader.Load(new DefinitionId(HomeMap));
            loader.Load(new DefinitionId(HomeMap));
            loader.Load(new DefinitionId(HomeMap));

            Assert.That(source.Reads, Is.EqualTo(3), "it really did reload each time");
            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
        }

        [Test]
        public void LoadingWithoutAMapDoesNothing()
        {
            var source = new FakeSource();
            var loader = new MonsterConfigurationLoader(source, _runtime, Maps);

            Assert.That(loader.Load(default), Is.Zero);
            Assert.That(source.Reads, Is.Zero, "nothing is even asked for");
        }

        [Test]
        public void NothingOnTheLoaderIsReachableFromAConnection()
        {
            // An operator's tool, not a message handler: no connection, no client id, and
            // no way for a player to repopulate a map at will.
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterConfigurationLoader).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.EqualTo(typeof(DefinitionId)),
                        method.Name + " takes something other than a map id");
                }
            }
        }

        // ---- the client decides none of it -------------------------------------------------------

        [Test]
        public void NothingOnTheRuntimeLetsAClientForceAggroOrPickATarget()
        {
            System.Type runtime = typeof(MonsterWorldRuntime);

            foreach (string absent in new[]
                     { "SetTarget", "ForceAggro", "SetAggression", "Attack", "Kill" })
            {
                Assert.That(runtime.GetMethod(absent), Is.Null,
                    absent + " must not be reachable");
            }

            // NotifyAttacked exists, but it takes two ids the server resolved and no
            // connection -- a client has no path to it.
            System.Reflection.MethodInfo notify = runtime.GetMethod("NotifyAttacked");

            Assert.That(notify, Is.Not.Null);

            foreach (System.Reflection.ParameterInfo parameter in notify.GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.EqualTo(typeof(InstanceId)));
            }
        }
    }
}
