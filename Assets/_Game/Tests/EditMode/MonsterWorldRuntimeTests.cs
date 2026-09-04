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
    /// The monsters a world server owns: who exists, what they are doing, and who they are.
    /// </summary>
    /// <remarks>
    /// <b>There is no client in these tests, and that is the point.</b> Every other authority
    /// in Phase 17 resolves something a client asked for. A monster spawns, notices, chases,
    /// swings, dies and comes back without a client being consulted, so the tests drive a
    /// tick and a clock rather than a command.
    ///
    /// Behaviour itself belongs to Phase 10 and already has its own tests. What is checked
    /// here is the layer above: that the runtime composes those services correctly, scopes
    /// them by map, and claims a defeat exactly once.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterWorldRuntimeTests : MonsterTestBase
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

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private DefinitionRegistry<SpawnPointDefinition> _spawnPoints;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpRuntime()
        {
            _store = new FakeStore();

            _spawnPoints = new DefinitionRegistry<SpawnPointDefinition>();
            _spawnPoints.Register(PlayerSpawn("spawn.home", HomeMap, 0f, 0f, 0f));
            _spawnPoints.Register(PlayerSpawn("spawn.other", OtherMap, 0f, 0f, 0f));

            _players = new WorldCharacterRegistry(_store, _spawnPoints);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);
        }

        [TearDown]
        public void TearDownRuntime()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map, float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":" + Invariant(x)
                + ",\"_y\":" + Invariant(y) + ",\"_z\":" + Invariant(z) + "}", spawn);

            _local.Add(spawn);

            return spawn;
        }

        /// <summary>Invariant-culture float, so a comma locale cannot break the JSON.</summary>
        /// <remarks>MonsterTestBase has its own, but it is private; duplicating one line is
        /// better than widening a base class this fixture only borrows from.</remarks>
        private static string Invariant(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>An authored monster spawn point, the Phase 10 type.</summary>
        private static MonsterSpawnPoint Nest(string monster, string map, float x = 0f,
            float z = 0f, int maxAlive = 1, float respawnDelay = 0f, float radius = 0f)
        {
            return new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(x, 0f, z), radius, maxAlive, respawnDelay,
                new DefinitionId(map));
        }

        /// <summary>Puts a player in the world at a position on a map.</summary>
        private LivingCharacter AddPlayer(string character, string map, float x = 0f,
            float z = 0f, string session = null)
        {
            session = session ?? "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(character.GetHashCode(),
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-" + character),
                    new CharacterId(character), new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(map), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            result.Character.Location.Position = new CombatPosition(x, 0f, z);
            result.Character.Combatant.Position = new CombatPosition(x, 0f, z);

            return result.Character;
        }

        private LivingMonster OnlyMonster()
        {
            IReadOnlyList<LivingMonster> all = _runtime.All();

            Assert.That(all.Count, Is.EqualTo(1), "expected exactly one monster");

            return all[0];
        }

        // ---- spawning -------------------------------------------------------------------

        [Test]
        public void PopulatingFillsEverySpawnPointToItsAuthoredCount()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap, maxAlive: 3));

            Assert.That(_runtime.PopulateAll(), Is.EqualTo(3));
            Assert.That(_runtime.AliveCount, Is.EqualTo(3));
        }

        [Test]
        public void ASpawnPointWithNoMonsterIsRefused()
        {
            Assert.That(_runtime.AddSpawnPoint(Nest("", HomeMap)), Is.False);
            Assert.That(_runtime.SpawnerCount, Is.Zero);
        }

        [Test]
        public void ASpawnPointWithNoMapIsRefused()
        {
            // A monster with no map could never be scoped to anybody, so it would be
            // permanently inert -- better refused than silently useless.
            Assert.That(_runtime.AddSpawnPoint(Nest(Grunt, "")), Is.False);
        }

        [Test]
        public void AnUnauthoredMonsterSpawnsNothing()
        {
            _runtime.AddSpawnPoint(Nest("monster.does.not.exist", HomeMap));

            Assert.That(_runtime.PopulateAll(), Is.Zero);
            Assert.That(_runtime.AliveCount, Is.Zero);
        }

        [Test]
        public void ASpawnedMonsterCarriesItsAuthoredDefinitionAndMap()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();

            Assert.That(monster.State.DefinitionId.Value, Is.EqualTo(Grunt));
            Assert.That(monster.Map.Value, Is.EqualTo(HomeMap));
            Assert.That(monster.State.Level, Is.EqualTo(5), "from the definition, not a literal");
            Assert.That(monster.State.MaxHealth, Is.EqualTo(100), "from the authored stat");
            Assert.That(monster.IsAlive, Is.True);
        }

        [Test]
        public void ASpawnedMonsterUsesThePhase10TypesRatherThanNewOnes()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();

            // There is no server-side monster model, for the same reason there is no
            // server-side character model.
            Assert.That(monster.State, Is.TypeOf<MonsterRuntimeState>());
            Assert.That(monster.Ai, Is.TypeOf<MonsterAiController>());
            Assert.That(monster.Combatant, Is.TypeOf<MonsterCombatant>());
        }

        // ---- there is no client command for a monster ---------------------------------------

        [Test]
        public void NothingOnTheRuntimeTakesAConnectionOrACommand()
        {
            // Every other authority in this phase has a Resolve or an Execute taking a
            // connection id. A monster has no inbound command at all, and this is what keeps
            // that true: the absence of a method a client could reach.
            foreach (System.Reflection.MethodInfo method in
                     typeof(MonsterWorldRuntime).GetMethods(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.Name.ToLowerInvariant(),
                        Does.Not.Contain("connection"),
                        method.Name + " must not take a connection");
                    Assert.That(parameter.ParameterType.Name,
                        Does.Not.Contain("Command"),
                        method.Name + " must not take a client command");
                }
            }
        }

        // ---- map scoping ---------------------------------------------------------------------

        [Test]
        public void AMonsterNoticesAPlayerOnItsOwnMap()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            AddPlayer("char-near", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            LivingMonster monster = OnlyMonster();

            Assert.That(monster.State.HasTarget, Is.True);
            Assert.That(monster.Ai.State, Is.Not.EqualTo(MonsterAiState.Idle));
        }

        [Test]
        public void AMonsterIgnoresAPlayerOnAnotherMapAtTheSameCoordinates()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            // Standing exactly where a target would be, but somewhere else entirely.
            AddPlayer("char-elsewhere", OtherMap, x: 1f);

            _runtime.Tick(0.5f);

            Assert.That(OnlyMonster().State.HasTarget, Is.False,
                "coordinates are meaningless across maps");
        }

        [Test]
        public void ADeadPlayerIsNotATarget()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingCharacter player = AddPlayer("char-down", HomeMap, x: 2f);
            player.Combatant.ApplyHealthDelta(-1000);

            _runtime.Tick(0.5f);

            Assert.That(OnlyMonster().State.HasTarget, Is.False,
                "a monster should go home, not stand over a corpse");
        }

        [Test]
        public void MonstersOnDifferentMapsEachSeeOnlyTheirOwn()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.AddSpawnPoint(Nest(Grunt, OtherMap));
            _runtime.PopulateAll();

            AddPlayer("char-home", HomeMap, x: 2f);

            _runtime.Tick(0.5f);

            int withTarget = 0;

            foreach (LivingMonster monster in _runtime.All())
            {
                if (monster.State.HasTarget) withTarget++;
            }

            Assert.That(withTarget, Is.EqualTo(1));
        }

        [Test]
        public void APassiveMonsterDoesNotChaseAPassingPlayer()
        {
            // Behaviour is Phase 10's; this checks the runtime feeds it the right candidates
            // rather than that the rule works.
            _runtime.AddSpawnPoint(Nest(Docile, HomeMap));
            _runtime.PopulateAll();

            AddPlayer("char-passing", HomeMap, x: 3f);

            _runtime.Tick(0.5f);

            Assert.That(OnlyMonster().State.HasTarget, Is.False);
        }

        // ---- attack decisions ------------------------------------------------------------------

        [Test]
        public void AMonsterInRangeReportsThatItWantsToSwing()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            AddPlayer("char-close", HomeMap, x: 1f);

            MonsterTickResult result = _runtime.Tick(0.5f);

            Assert.That(result.Attacking.Count, Is.EqualTo(1));
            Assert.That(OnlyMonster().Ai.State, Is.EqualTo(MonsterAiState.Attack));
        }

        [Test]
        public void TheRuntimeReportsAnAttackRatherThanApplyingOne()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingCharacter player = AddPlayer("char-close", HomeMap, x: 1f);
            int before = player.Combatant.CurrentHealth;

            _runtime.Tick(0.5f);

            // The AI decides; the combat runtime applies. Damage from inside a monster tick
            // would make the AI a second combat path.
            Assert.That(player.Combatant.CurrentHealth, Is.EqualTo(before));
        }

        [Test]
        public void TheAuthoredCooldownStopsASecondSwingOnTheNextTick()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            AddPlayer("char-close", HomeMap, x: 1f);

            Assert.That(_runtime.Tick(0.5f).Attacking.Count, Is.EqualTo(1));

            // The grunt's authored cooldown is two seconds.
            Assert.That(_runtime.Tick(0.5f).Attacking.Count, Is.Zero);
        }

        [Test]
        public void AfterTheCooldownElapsesItSwingsAgain()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            AddPlayer("char-close", HomeMap, x: 1f);

            _runtime.Tick(0.5f);

            Assert.That(_runtime.Tick(2.5f).Attacking.Count, Is.EqualTo(1));
        }

        [Test]
        public void AnEmptyWorldTicksWithoutIncident()
        {
            MonsterTickResult result = _runtime.Tick(1f);

            Assert.That(result.Spawned, Is.Zero);
            Assert.That(result.Retired, Is.Zero);
            Assert.That(result.Attacking, Is.Empty);
        }

        [Test]
        public void ANegativeDeltaIsTreatedAsNoTimePassing()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            Assert.DoesNotThrow(() => _runtime.Tick(-5f));
            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
        }

        // ---- death, retirement and respawn ---------------------------------------------------------

        [Test]
        public void AnUnclaimedCorpseIsNotRetiredAndStaysResolvable()
        {
            // Phase 10 refuses to retire a monster whose defeat has not been claimed, and it
            // is right to: retiring one would destroy the experience and loot it owed
            // somebody. The corpse therefore has to stay findable, or the claim could never
            // happen. The first version of the runtime removed it on death and produced
            // exactly that unclaimable reward.
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap, respawnDelay: 10f));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();
            InstanceId id = monster.Instance;

            monster.State.ApplyHealthDelta(-1000);

            MonsterTickResult result = _runtime.Tick(0.1f);

            Assert.That(result.Retired, Is.Zero, "nobody has collected it yet");
            Assert.That(_runtime.TryResolve(id, out _), Is.True,
                "the claim needs to be able to find it");
        }

        [Test]
        public void AClaimedCorpseIsRetiredAndStopsBeingResolvable()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap, respawnDelay: 10f));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();
            InstanceId id = monster.Instance;

            monster.State.ApplyHealthDelta(-1000);
            _runtime.ClaimDefeat(id, new InstanceId("char-killer"), default, null);

            MonsterTickResult result = _runtime.Tick(0.1f);

            Assert.That(result.Retired, Is.EqualTo(1));
            Assert.That(_runtime.AliveCount, Is.Zero);
            Assert.That(_runtime.TryResolve(id, out _), Is.False,
                "a collected corpse must not remain targetable");
        }

        [Test]
        public void AMonsterRespawnsOnlyAfterItsAuthoredDelay()
        {
            _runtime.AddSpawnPoint(Nest(Slow, HomeMap, respawnDelay: 10f));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();
            monster.State.ApplyHealthDelta(-1000);

            // Claimed, because an unclaimed corpse is deliberately never retired and the
            // respawn timer only starts once it is.
            _runtime.ClaimDefeat(monster.Instance, new InstanceId("char-killer"), default, null);
            _runtime.Tick(0.1f);

            Assert.That(_runtime.AliveCount, Is.Zero);

            // The slow monster's authored delay is ten seconds.
            Assert.That(_runtime.Tick(5f).Spawned, Is.Zero, "not yet");
            Assert.That(_runtime.Tick(6f).Spawned, Is.EqualTo(1), "now");
            Assert.That(_runtime.AliveCount, Is.EqualTo(1));
        }

        [Test]
        public void ARespawnedMonsterIsANewInstanceAtFullHealth()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap, respawnDelay: 1f));
            _runtime.PopulateAll();

            LivingMonster dying = OnlyMonster();
            InstanceId first = dying.Instance;

            dying.State.ApplyHealthDelta(-1000);
            _runtime.ClaimDefeat(first, new InstanceId("char-killer"), default, null);
            _runtime.Tick(0.1f);
            _runtime.Tick(2f);

            LivingMonster second = OnlyMonster();

            Assert.That(second.Instance, Is.Not.EqualTo(first),
                "a fresh life is a fresh identity, so a stale reference cannot address it");
            Assert.That(second.State.CurrentHealth, Is.EqualTo(second.State.MaxHealth));
            Assert.That(second.State.IsDefeatClaimed, Is.False);
        }

        [Test]
        public void ClearingEmptiesTheWorld()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap, maxAlive: 3));
            _runtime.PopulateAll();

            Assert.That(_runtime.Clear(), Is.EqualTo(3));
            Assert.That(_runtime.AliveCount, Is.Zero);
        }

        // ---- the defeat claim, exactly once ------------------------------------------------------------

        [Test]
        public void ADefeatIsClaimedExactlyOnce()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();
            InstanceId id = monster.Instance;
            var killer = new InstanceId("char-killer");

            monster.State.ApplyHealthDelta(-1000);

            MonsterDefeatResult first = _runtime.ClaimDefeat(id, killer, default, null);
            MonsterDefeatResult second = _runtime.ClaimDefeat(id, killer, default, null);

            Assert.That(first.IsClaimed, Is.True);
            Assert.That(first.ExperienceReward, Is.EqualTo(50), "the grunt's authored reward");
            Assert.That(second.IsClaimed, Is.False,
                "two killing blows in one tick must produce one reward");
        }

        [Test]
        public void ALivingMonsterCannotBeClaimed()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            MonsterDefeatResult result = _runtime.ClaimDefeat(OnlyMonster().Instance,
                new InstanceId("char-killer"), default, null);

            Assert.That(result.IsClaimed, Is.False,
                "claiming a defeat that has not happened would mint a reward from nothing");
        }

        [Test]
        public void AnUnknownMonsterCannotBeClaimed()
        {
            Assert.That(_runtime.ClaimDefeat(new InstanceId("no-such-monster"),
                new InstanceId("char-killer"), default, null).IsClaimed, Is.False);

            Assert.That(_runtime.ClaimDefeat(default, default, default, null).IsClaimed,
                Is.False);
        }

        [Test]
        public void ClaimingGrantsNothingToAnybody()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingCharacter player = AddPlayer("char-killer", HomeMap, x: 1f);
            long experienceBefore = player.Domain.Progression.Experience;

            OnlyMonster().State.ApplyHealthDelta(-1000);

            _runtime.ClaimDefeat(OnlyMonster().Instance, player.Combatant.CombatantId,
                default, null);

            // The result says what the kill is worth. Putting experience into a character
            // is a later sub-phase with its own persistence boundary.
            Assert.That(player.Domain.Progression.Experience, Is.EqualTo(experienceBefore));
            Assert.That(player.IsDirty, Is.False);
        }

        // ---- the resolver 17.12 was written against ---------------------------------------------------------

        [Test]
        public void AMonsterResolvesToItsCombatant()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingMonster monster = OnlyMonster();

            Assert.That(_runtime.TryResolve(monster.Instance, out ICombatant combatant), Is.True);
            Assert.That(combatant, Is.SameAs(monster.Combatant));
            Assert.That(_runtime.TryGetMap(monster.Instance, out DefinitionId map), Is.True);
            Assert.That(map.Value, Is.EqualTo(HomeMap));
        }

        [Test]
        public void APlayerResolvesThroughTheSameSeam()
        {
            LivingCharacter player = AddPlayer("char-resolved", HomeMap);

            // A player's combatant id is their character id projected onto InstanceId, so a
            // caller does not have to know which kind of thing it asked about.
            Assert.That(_runtime.TryResolve(player.Combatant.CombatantId,
                out ICombatant combatant), Is.True);
            Assert.That(combatant, Is.SameAs(player.Combatant));

            Assert.That(_runtime.TryGetMap(player.Combatant.CombatantId,
                out DefinitionId map), Is.True);
            Assert.That(map.Value, Is.EqualTo(HomeMap));
        }

        [Test]
        public void AnUnknownInstanceResolvesToNothing()
        {
            Assert.That(_runtime.TryResolve(new InstanceId("nobody"), out _), Is.False);
            Assert.That(_runtime.TryResolve(default, out _), Is.False);
            Assert.That(_runtime.TryGetMap(new InstanceId("nobody"), out _), Is.False);
            Assert.That(_runtime.TryGetMap(default, out _), Is.False);
        }

        [Test]
        public void TheRuntimeSatisfiesTheSeamCombatWasWrittenAgainst()
        {
            // 17.12 defined ICombatantResolver and nothing implemented it, so every combat
            // command refused with UnknownTarget. This is what fills it.
            Assert.That(_runtime, Is.InstanceOf<ICombatantResolver>());
        }

        [Test]
        public void AResolvedMonsterIsAValidCombatTargetForTheCombatAuthority()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, HomeMap));
            _runtime.PopulateAll();

            LivingCharacter player = AddPlayer("char-attacker", HomeMap, x: 1f);
            LivingMonster monster = OnlyMonster();

            var authority = new CombatCommandAuthority(_players, id => true, _runtime);

            CombatCommandResolution resolution = authority.Resolve(player.ConnectionId,
                new CombatCommand(player.Character, monster.Instance, default, 1, 1));

            Assert.That(resolution.IsResolved, Is.True, resolution.Reason.ToString());
            Assert.That(resolution.Target, Is.SameAs(monster.Combatant));
        }

        [Test]
        public void AMonsterOnAnotherMapIsRefusedByTheCombatAuthority()
        {
            _runtime.AddSpawnPoint(Nest(Grunt, OtherMap));
            _runtime.PopulateAll();

            LivingCharacter player = AddPlayer("char-attacker", HomeMap, x: 1f);
            LivingMonster monster = OnlyMonster();

            var authority = new CombatCommandAuthority(_players, id => true, _runtime);

            CombatCommandResolution resolution = authority.Resolve(player.ConnectionId,
                new CombatCommand(player.Character, monster.Instance, default, 1, 1));

            Assert.That(resolution.Reason, Is.EqualTo(CombatCommandRejection.DifferentMap));
        }

        // ---- misconfiguration ---------------------------------------------------------------------------

        [Test]
        public void ARuntimeWithNoPlayersTicksWithoutFindingTargets()
        {
            var lonely = new MonsterWorldRuntime(null, Monsters, new DefinitionId(MaxHp),
                Enemies);

            lonely.AddSpawnPoint(Nest(Grunt, HomeMap));
            lonely.PopulateAll();

            Assert.DoesNotThrow(() => lonely.Tick(1f));
            Assert.That(lonely.AliveCount, Is.EqualTo(1));
        }

        [Test]
        public void ARuntimeWithNoDefinitionsSpawnsNothing()
        {
            var empty = new MonsterWorldRuntime(_players, null, new DefinitionId(MaxHp),
                Enemies);

            empty.AddSpawnPoint(Nest(Grunt, HomeMap));

            Assert.That(empty.PopulateAll(), Is.Zero);
        }

        // ---- nothing is persisted -----------------------------------------------------------------------------

        [Test]
        public void MonsterStateIsRuntimeAndNotPersistent()
        {
            // Phase 15's schema has no monster table and should not gain one: a restart
            // repopulates from authored spawn points, which is the correct behaviour.
            Assert.That(typeof(MonsterRuntimeState), Is.InstanceOf<System.Type>());
            Assert.That(typeof(IRuntimeState).IsAssignableFrom(typeof(MonsterRuntimeState)),
                Is.True);
            Assert.That(typeof(IPersistentState).IsAssignableFrom(typeof(MonsterRuntimeState)),
                Is.False);
        }

        [Test]
        public void TheRuntimeHoldsNoPersistenceSeamAtAll()
        {
            foreach (System.Reflection.FieldInfo field in typeof(MonsterWorldRuntime).GetFields(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance))
            {
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(ICharacterStateStore)),
                    "a monster tick must have no way to write to the database");
            }
        }
    }
}
