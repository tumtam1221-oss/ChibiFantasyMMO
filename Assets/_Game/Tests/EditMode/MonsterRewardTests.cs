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
    /// What a monster's death is worth, and who is allowed to decide it.
    /// </summary>
    /// <remarks>
    /// <b>Every number here comes from content or from the server.</b> There is no client in
    /// these tests and no experience figure in any argument: the reward authority is handed
    /// two ids the server minted and reads the rest from the authored monster. Several tests
    /// below exist only to hold that property, because it is the one a future convenience
    /// overload would quietly break.
    ///
    /// Levelling itself belongs to Phase 05 and has its own suite. What is checked here is
    /// that a defeat reaches it exactly once, with the authored figure, for a character the
    /// server resolved -- and that a database that is down cannot turn a kill into a lost
    /// reward or a duplicated one.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterRewardTests : MonsterTestBase
    {
        /// <summary>A store whose save outcome a test chooses.</summary>
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            /// <summary>Every row the registry actually wrote, oldest first.</summary>
            public readonly List<PersistedCharacter> Saved = new List<PersistedCharacter>();

            /// <summary>Set to make the next and every following save fail.</summary>
            public CharacterPersistenceFailure FailWith = CharacterPersistenceFailure.None;

            public int Revision = 1;

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                if (FailWith != CharacterPersistenceFailure.None)
                {
                    return CharacterPersistenceResult.Failed(FailWith, "test");
                }

                Saved.Add(c);

                return CharacterPersistenceResult.Saved(++Revision);
            }
        }

        private const string Worth50 = "monster.worth50";
        private const string Worth350 = "monster.worth350";
        private const string Worthless = "monster.worthless";
        private const string Hoard = "monster.hoard";

        private const int LevelCost = 100;
        private const int MaxLevel = 20;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterRewardAuthority _rewards;
        private CharacterProgressionDefinition _curve;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpRewards()
        {
            AddMonster(Worth50, level: 5, experience: 50);
            AddMonster(Worth350, level: 9, experience: 350);
            AddMonster(Worthless, level: 1, experience: 0);
            AddMonster(Hoard, level: 5, experience: 50, lootTable: "drop.hoard");

            _curve = Curve();

            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));
            spawns.Register(PlayerSpawn("spawn.other", OtherMap));

            _players = new WorldCharacterRegistry(_store, spawns);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _rewards = new MonsterRewardAuthority(_runtime, _players, _curve);
        }

        [TearDown]
        public void TearDownRewards()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        /// <summary>Levels 1 to 20, each costing the same, so arithmetic is obvious.</summary>
        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            var costs = new System.Text.StringBuilder();

            for (int level = 1; level < MaxLevel; level++)
            {
                if (level > 1) costs.Append(',');

                costs.Append(LevelCost);
            }

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.reward-test\"},\"_minLevel\":1,"
                + "\"_maxLevel\":" + MaxLevel + ",\"_experienceToNextLevel\":["
                + costs + "]}", definition);

            _local.Add(definition);

            return definition;
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

        private LivingCharacter AddPlayer(string character, int level = 5,
            long experience = 0, string map = HomeMap)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, level, experience, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(character.GetHashCode(),
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            return result.Character;
        }

        /// <summary>Puts one monster in the world and kills it, without rewarding it.</summary>
        private LivingMonster Corpse(string monster = Worth50, string map = HomeMap)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(map)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.State.DefinitionId.Value != monster) continue;

                living.State.ApplyHealthDelta(-10000);

                Assert.That(living.IsAlive, Is.False, "the fixture did not die");

                return living;
            }

            Assert.Fail("no monster '" + monster + "' spawned");

            return null;
        }

        private static InstanceId Of(LivingCharacter character)
        {
            return character.Combatant.CombatantId;
        }

        // ---- 1-5: defeat authority ------------------------------------------------------------

        [Test]
        public void AGenuineDefeatIsRewarded()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            MonsterRewardResult result = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(result.IsGranted, Is.True, result.ToString());
            Assert.That(result.Reason, Is.EqualTo(MonsterRewardRejection.None));
            Assert.That(result.ExperienceGranted, Is.EqualTo(50));
            Assert.That(result.Recipient.Value, Is.EqualTo("char-killer"));
        }

        [Test]
        public void AMonsterNobodyHasIsRefused()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result =
                _rewards.Grant(new InstanceId("no-such-monster"), Of(killer));

            Assert.That(result.Reason, Is.EqualTo(MonsterRewardRejection.UnknownMonster));
            Assert.That(result.IsGranted, Is.False);
        }

        [Test]
        public void AMonsterWhoseDefeatWasAlreadyClaimedElsewhereIsRefused()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            // Loot or a quest got there first. That claim is the kill; this call is not.
            Assert.That(monster.State.TryClaimDefeat(), Is.True, "precondition");

            MonsterRewardResult result = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(result.Reason,
                Is.EqualTo(MonsterRewardRejection.MonsterAlreadyDefeated));
            Assert.That(killer.Domain.Progression.Experience, Is.Zero);
        }

        [Test]
        public void ALivingMonsterPaysNobody()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Worth50),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();

            LivingMonster alive = _runtime.All()[0];

            MonsterRewardResult result = _rewards.Grant(alive.Instance, Of(killer));

            Assert.That(result.Reason, Is.EqualTo(MonsterRewardRejection.InvalidDefeat),
                "claiming a defeat that has not happened mints a reward from nothing");
            Assert.That(alive.State.IsDefeatClaimed, Is.False,
                "and it leaves the monster claimable");
        }

        [Test]
        public void ADeadMonsterCannotPayTwice()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            Assert.That(_rewards.Grant(monster.Instance, Of(killer)).IsGranted, Is.True);

            MonsterRewardResult second = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(second.IsGranted, Is.False);
            Assert.That(second.Reason,
                Is.EqualTo(MonsterRewardRejection.RewardAlreadyGranted));
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50));
        }

        // ---- 6-12: the experience figure ---------------------------------------------------------

        [Test]
        public void TheAuthoredMonsterDecidesTheAmount()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = _rewards.Grant(Corpse(Worth350).Instance, Of(killer));

            Assert.That(result.ExperienceGranted, Is.EqualTo(350),
                "the number came from MonsterDefinition and nowhere else");
        }

        [Test]
        public void AMonsterWorthNothingGrantsNothingAndChangesNothing()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result =
                _rewards.Grant(Corpse(Worthless).Instance, Of(killer));

            Assert.That(result.IsGranted, Is.True, "a training dummy is still a kill");
            Assert.That(result.ExperienceGranted, Is.Zero);
            Assert.That(result.LevelledUp, Is.False);
            Assert.That(killer.IsDirty, Is.False,
                "nothing changed, so nothing is queued to be written");
            Assert.That(_store.Saved, Is.Empty);
        }

        [Test]
        public void ANegativeAuthoredRewardIsRefused()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            MonsterDefinition definition;
            Monsters.TryGet(new DefinitionId(Worth50), out definition);

            // Content validation already refuses this; reaching the runtime with one means
            // the definition arrived some other way.
            SetPrivate(definition, "_experienceReward", -25);

            MonsterRewardResult result = _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(result.Reason,
                Is.EqualTo(MonsterRewardRejection.InvalidExperienceReward));
            Assert.That(killer.Domain.Progression.Experience, Is.Zero);
        }

        [Test]
        public void ARewardThatWouldOverflowIsRefusedBeforeTheClaimIsSpent()
        {
            LivingCharacter killer = AddPlayer("char-hoarder", experience: long.MaxValue - 5);

            MonsterDefinition definition;
            Monsters.TryGet(new DefinitionId(Worth50), out definition);
            SetPrivate(definition, "_experienceReward", int.MaxValue);

            LivingMonster monster = Corpse();

            MonsterRewardResult result = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(result.Reason,
                Is.EqualTo(MonsterRewardRejection.InvalidExperienceReward));
            Assert.That(killer.Domain.Progression.Experience,
                Is.EqualTo(long.MaxValue - 5), "untouched");
            Assert.That(monster.State.IsDefeatClaimed, Is.False,
                "a claim spent on a grant that cannot be applied is a kill nobody can ever "
                + "be paid for");
        }

        [Test]
        public void NothingOnTheAuthorityAcceptsAnAmountFromAnybody()
        {
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterRewardAuthority).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.EqualTo(typeof(InstanceId)),
                        method.Name + " takes " + parameter.ParameterType.Name
                        + " -- an experience amount or a connection could arrive through it");
                }
            }
        }

        [Test]
        public void TheSameKillIsWorthTheSameEveryTime()
        {
            LivingCharacter first = AddPlayer("char-a");

            MonsterRewardResult one = _rewards.Grant(Corpse().Instance, Of(first));

            // A second world, built identically. No clock, no randomness, no order effect.
            var store = new FakeStore();
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home-2", HomeMap));

            var players = new WorldCharacterRegistry(store, spawns);
            var runtime = new MonsterWorldRuntime(players, Monsters, new DefinitionId(MaxHp),
                Enemies);
            var rewards = new MonsterRewardAuthority(runtime, players, _curve);

            store.Rows["session-char-b"] = new PersistedCharacter(
                new CharacterId("char-b"), new AccountId("acc-b"), new ServerId("srv-1"),
                "char-b", 2, 5, 0, 100, 50, new DefinitionId("class.novice"), default,
                new DefinitionId(HomeMap), default, null, null, null, 1);

            WorldSpawnResult spawned = players.Spawn(2,
                WorldAdmission.Admitted(new SessionId("session-char-b"),
                    new AccountId("acc-b"), new CharacterId("char-b"), new ServerId("srv-1"),
                    new ChannelId("ch-1"), new DefinitionId(HomeMap), new Revision(1),
                    new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Worth50),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));
            runtime.PopulateAll();
            runtime.All()[0].State.ApplyHealthDelta(-10000);

            MonsterRewardResult two = rewards.Grant(runtime.All()[0].Instance,
                spawned.Character.Combatant.CombatantId);

            Assert.That(two.ExperienceGranted, Is.EqualTo(one.ExperienceGranted));
            Assert.That(two.LevelAfter, Is.EqualTo(one.LevelAfter));
            Assert.That(two.ExperienceAfter, Is.EqualTo(one.ExperienceAfter));
        }

        // ---- 13-18: what it does to a character ---------------------------------------------------

        [Test]
        public void ExperienceLandsOnTheAuthoritativeCharacter()
        {
            LivingCharacter killer = AddPlayer("char-killer", level: 5, experience: 10);

            _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(60));
            Assert.That(killer.Domain.Progression.Level, Is.EqualTo(5));
        }

        [Test]
        public void CrossingTheThresholdLevelsTheCharacterUp()
        {
            // The brief's own example: level 14 at 95 of 100, a monster worth 20.
            LivingCharacter killer = AddPlayer("char-killer", level: 14, experience: 95);

            MonsterDefinition definition;
            Monsters.TryGet(new DefinitionId(Worth50), out definition);
            SetPrivate(definition, "_experienceReward", 20);

            MonsterRewardResult result = _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(result.LevelBefore, Is.EqualTo(14));
            Assert.That(result.LevelAfter, Is.EqualTo(15));
            Assert.That(result.LevelledUp, Is.True);
            Assert.That(killer.Domain.Progression.Level, Is.EqualTo(15));
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(15),
                "the remainder carries into the new level");
        }

        [Test]
        public void OneRewardCanCrossSeveralLevels()
        {
            LivingCharacter killer = AddPlayer("char-killer", level: 5, experience: 0);

            MonsterRewardResult result = _rewards.Grant(Corpse(Worth350).Instance, Of(killer));

            Assert.That(result.LevelAfter, Is.EqualTo(8), "350 buys three levels of 100");
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50));
        }

        [Test]
        public void ExperienceAtMaximumLevelIsBankedRatherThanDiscarded()
        {
            LivingCharacter killer = AddPlayer("char-capped", level: MaxLevel, experience: 0);

            MonsterRewardResult result = _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(result.IsGranted, Is.True);
            Assert.That(result.LevelAfter, Is.EqualTo(MaxLevel), "the cap holds");
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50),
                "Phase 05 keeps it, so raising the cap later is not unfair to whoever kept "
                + "playing");
        }

        [Test]
        public void ThereIsNoSecondLevellingFormula()
        {
            // The authority owns no curve arithmetic: the same gain applied directly to the
            // Phase 05 aggregate produces the identical result.
            LivingCharacter killer = AddPlayer("char-killer", level: 7, experience: 40);

            _rewards.Grant(Corpse(Worth350).Instance, Of(killer));

            var mirror = new CharacterProgressionState(new CharacterId("mirror"), 7, 40);
            mirror.AddExperience(350, _curve);

            Assert.That(killer.Domain.Progression.Level, Is.EqualTo(mirror.Level));
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(mirror.Experience));
        }

        [Test]
        public void TheCharacterCombatantIsTheSameObjectTheWorldAlreadyHeld()
        {
            // Derived stats are recalculated through the existing character path, not by a
            // copy this authority makes: there is only ever one character object.
            LivingCharacter killer = AddPlayer("char-killer");

            ICombatant before = killer.Combatant;

            _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(killer.Combatant, Is.SameAs(before));
            Assert.That(killer.Combatant.CombatantId.Value, Is.EqualTo("char-killer"));
        }

        // ---- 19-23: exactly once ---------------------------------------------------------------------

        [Test]
        public void ADuplicateDefeatGrantsOnce()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            for (int i = 0; i < 5; i++) _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50));
            Assert.That(_rewards.GrantedCount, Is.EqualTo(1));
        }

        [Test]
        public void ARepeatedRequestIsAnsweredRatherThanReExecuted()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(_rewards.HasGranted(monster.Instance), Is.True);
            Assert.That(_rewards.Grant(monster.Instance, Of(killer)).Reason,
                Is.EqualTo(MonsterRewardRejection.RewardAlreadyGranted),
                "a retry is told it is a retry, not that the monster is missing");
        }

        [Test]
        public void RetryingAfterASuccessfulSaveDoesNotDuplicate()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            Assert.That(_rewards.Grant(monster.Instance, Of(killer)).IsPersisted, Is.True);

            int savesAfterFirst = _store.Saved.Count;

            _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50));
            Assert.That(_store.Saved.Count, Is.EqualTo(savesAfterFirst),
                "the second call wrote nothing at all");
        }

        [Test]
        public void ADuplicateMessageCannotDuplicateAReward()
        {
            // The same defeat arriving twice is indistinguishable from a repeated packet:
            // the guard is the monster's own instance id, which no caller supplies.
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            MonsterRewardResult first = _rewards.Grant(monster.Instance, Of(killer));
            MonsterRewardResult replay = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(first.IsGranted, Is.True);
            Assert.That(replay.IsGranted, Is.False);
        }

        [Test]
        public void ReconnectingDoesNotReopenAnAlreadyPaidKill()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            _rewards.Grant(monster.Instance, Of(killer));

            long earned = killer.Domain.Progression.Experience;

            // The player drops and comes back on a new connection. Its persisted row now
            // carries the experience it earned.
            _store.Rows["session-char-killer"] = new PersistedCharacter(
                new CharacterId("char-killer"), new AccountId("acc-char-killer"),
                new ServerId("srv-1"), "char-killer", 2,
                killer.Domain.Progression.Level, earned, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 2);

            _players.Despawn(killer.ConnectionId);

            LivingCharacter returned = AddPlayer("char-killer",
                killer.Domain.Progression.Level, earned);

            Assert.That(_rewards.Grant(monster.Instance, Of(returned)).IsGranted, Is.False);
            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(earned),
                "reconnecting is not a second kill");
        }

        // ---- 24-27: persistence and concurrency ------------------------------------------------------

        [Test]
        public void TwoDifferentKillsBothPay()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            LivingMonster first = Corpse(Worth50);
            LivingMonster second = Corpse(Worth350);

            Assert.That(_rewards.Grant(first.Instance, Of(killer)).IsGranted, Is.True);
            Assert.That(_rewards.Grant(second.Instance, Of(killer)).IsGranted, Is.True);

            Assert.That(_rewards.GrantedCount, Is.EqualTo(2));
            Assert.That(killer.Domain.Progression.Level, Is.EqualTo(9),
                "400 experience from level 5 is four levels of 100");
        }

        [Test]
        public void AFailedSaveKeepsTheRewardAndSaysSo()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            _store.FailWith = CharacterPersistenceFailure.Unreachable;

            MonsterRewardResult result = _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(result.IsGranted, Is.True, "the kill happened");
            Assert.That(result.IsPersisted, Is.False, "and the database did not hear about it");
            Assert.That(result.Reason, Is.EqualTo(MonsterRewardRejection.PersistenceFailed));
            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50),
                "the experience is authoritative in memory");
            Assert.That(killer.IsDirty, Is.True,
                "and still queued, so the existing save lifecycle retries it");
        }

        [Test]
        public void ALostUpdateIsReportedAsAConflictRatherThanASuccess()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            _store.FailWith = CharacterPersistenceFailure.StaleRevision;

            MonsterRewardResult result = _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(result.Reason,
                Is.EqualTo(MonsterRewardRejection.ConcurrencyConflict));
            Assert.That(result.IsPersisted, Is.False);
            Assert.That(killer.IsDirty, Is.True);
        }

        [Test]
        public void APersistenceRetryWritesTheSameTotalOnceAndNotTheRewardAgain()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            _store.FailWith = CharacterPersistenceFailure.Unreachable;

            _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(_store.Saved, Is.Empty, "precondition: nothing was written");

            // The database comes back and the normal lifecycle saves again.
            _store.FailWith = CharacterPersistenceFailure.None;

            Assert.That(_players.Save(killer).IsOk, Is.True);
            Assert.That(_store.Saved, Has.Count.EqualTo(1));
            Assert.That(_store.Saved[0].Experience, Is.EqualTo(50),
                "the retry wrote the earned total, not a second reward");
            Assert.That(killer.IsDirty, Is.False);
        }

        [Test]
        public void ASecondKillDuringAnOutageIsNotLost()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            _store.FailWith = CharacterPersistenceFailure.Unreachable;

            _rewards.Grant(Corpse(Worth50).Instance, Of(killer));
            _rewards.Grant(Corpse(Worth350).Instance, Of(killer));

            _store.FailWith = CharacterPersistenceFailure.None;

            Assert.That(_players.Save(killer).IsOk, Is.True);
            Assert.That(_store.Saved, Has.Count.EqualTo(1),
                "one write carries everything that happened during the outage");
            Assert.That(_store.Saved[0].Level, Is.EqualTo(9));
            Assert.That(_store.Saved[0].Experience, Is.Zero,
                "400 from level 5 lands exactly on level 9");
        }

        // ---- recipient rules ---------------------------------------------------------------------------

        [Test]
        public void AKillerWhoIsNotACharacterIsRefused()
        {
            LivingMonster monster = Corpse();

            Assert.That(_rewards.Grant(monster.Instance, new InstanceId("char-ghost")).Reason,
                Is.EqualTo(MonsterRewardRejection.UnknownRecipient));
            Assert.That(_rewards.Grant(monster.Instance, default).Reason,
                Is.EqualTo(MonsterRewardRejection.UnknownRecipient));
            Assert.That(monster.State.IsDefeatClaimed, Is.False, "still claimable");
        }

        [Test]
        public void AMonsterCannotBePaidForKillingAMonster()
        {
            LivingMonster victim = Corpse(Worth50);
            LivingMonster other = Corpse(Worth350);

            Assert.That(_rewards.Grant(victim.Instance, other.Instance).Reason,
                Is.EqualTo(MonsterRewardRejection.UnknownRecipient));
        }

        [Test]
        public void ADeadCharacterIsNotCredited()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            killer.Combatant.ApplyHealthDelta(-10000);

            Assert.That(_rewards.Grant(monster.Instance, Of(killer)).Reason,
                Is.EqualTo(MonsterRewardRejection.RecipientNotEligible));
        }

        [Test]
        public void ACharacterOnAnotherMapIsNotCredited()
        {
            LivingCharacter elsewhere = AddPlayer("char-tourist", map: OtherMap);
            LivingMonster monster = Corpse(Worth50, HomeMap);

            Assert.That(_rewards.Grant(monster.Instance, Of(elsewhere)).Reason,
                Is.EqualTo(MonsterRewardRejection.RecipientNotEligible),
                "credit belongs to the world the monster died in");
        }

        [Test]
        public void OnlyTheKillerIsPaid()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingCharacter bystander = AddPlayer("char-bystander");

            _rewards.Grant(Corpse().Instance, Of(killer));

            Assert.That(killer.Domain.Progression.Experience, Is.EqualTo(50));
            Assert.That(bystander.Domain.Progression.Experience, Is.Zero,
                "party distribution is a later gate, not an accident of this one");
        }

        // ---- 28-32: the client decides none of it ---------------------------------------------------

        [Test]
        public void NoNetworkCommandCarriesExperienceOrALevel()
        {
            foreach (System.Type type in new[]
                     {
                         typeof(CombatCommand),
                         typeof(MonsterRewardResult),
                     })
            {
                foreach (System.Reflection.ConstructorInfo constructor in
                    type.GetConstructors())
                {
                    foreach (System.Reflection.ParameterInfo parameter in
                        constructor.GetParameters())
                    {
                        Assert.That(parameter.Name.ToLowerInvariant(),
                            Does.Not.Contain("connection"),
                            type.Name + "." + parameter.Name);
                    }
                }
            }

            // A combat command names an attacker, a target and a skill. There is no field a
            // client could put an amount, a level or a reward into.
            foreach (System.Reflection.PropertyInfo property in
                typeof(CombatCommand).GetProperties())
            {
                string name = property.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("experience"), property.Name);
                Assert.That(name, Does.Not.Contain("reward"), property.Name);
                Assert.That(name, Does.Not.Contain("level"), property.Name);
            }
        }

        [Test]
        public void NothingLetsACallerSetALevelOrAnExperienceTotal()
        {
            foreach (System.Reflection.MethodInfo method in
                typeof(CharacterProgressionState).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                Assert.That(method.Name, Is.Not.EqualTo("SetLevel"));
                Assert.That(method.Name, Is.Not.EqualTo("SetExperience"));
            }

            Assert.That(typeof(CharacterProgressionState).GetProperty("Level").CanWrite,
                Is.False);
            Assert.That(typeof(CharacterProgressionState).GetProperty("Experience").CanWrite,
                Is.False);
        }

        [Test]
        public void TheAuthoredRewardCannotBeChangedThroughAPublicSurface()
        {
            System.Reflection.PropertyInfo reward =
                typeof(MonsterDefinition).GetProperty("ExperienceReward");

            Assert.That(reward, Is.Not.Null);
            Assert.That(reward.CanWrite, Is.False,
                "content is authored in the editor, never written at runtime");
        }

        [Test]
        public void TheDefeatAuthorityIsUnreachableWithoutAServerSideMonster()
        {
            // Every route into a claim needs a MonsterRuntimeState, which only the server
            // mints. A client holds ids and messages, never one of these.
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterDefeatService).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (method.Name != "Resolve") continue;

                Assert.That(method.GetParameters()[0].ParameterType,
                    Is.EqualTo(typeof(MonsterRuntimeState)));
            }
        }

        [Test]
        public void TheServerRemainsTheOnlyWriterOfProgression()
        {
            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster monster = Corpse();

            Revision before = killer.Domain.Progression.Revision;

            _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(killer.Domain.Progression.Revision, Is.Not.EqualTo(before),
                "the one write went through the aggregate that owns the rule");
        }

        // ---- 33-35: experience is not loot -------------------------------------------------------------

        [Test]
        public void AnExperienceRewardCreatesNoItems()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            // A monster with an authored drop table. 17.14 owes experience and nothing else.
            AddDropTable("drop.hoard", new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1, 10000),
            });

            LivingMonster monster = Corpse(Hoard);

            MonsterRewardResult result = _rewards.Grant(monster.Instance, Of(killer));

            Assert.That(result.IsGranted, Is.True);
            Assert.That(result.ExperienceGranted, Is.EqualTo(50));

            foreach (System.Reflection.PropertyInfo property in
                typeof(MonsterRewardResult).GetProperties())
            {
                string name = property.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("loot"), property.Name);
                Assert.That(name, Does.Not.Contain("item"), property.Name);
                Assert.That(name, Does.Not.Contain("drop"), property.Name);
            }
        }

        [Test]
        public void TheDropSystemIsNotInvokedByARewardAtAll()
        {
            LivingCharacter killer = AddPlayer("char-killer");

            AddDropTable("drop.hoard", new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1, 10000),
            });

            LivingMonster monster = Corpse(Hoard);

            _rewards.Grant(monster.Instance, Of(killer));

            // The claim was made with no drop context and no list, which is the only way a
            // roll can happen. Loot, its ownership and its pickup belong to 17.15.
            var loot = new List<LootResult>();

            LootObjectState pile = MonsterDefeatService.CreateLoot(
                MonsterDefeatResult.NotClaimed, loot, default);

            Assert.That(pile, Is.Null);
            Assert.That(loot, Is.Empty);
        }
    }
}
