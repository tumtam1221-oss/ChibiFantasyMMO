using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A defeat decided once, and paid once, however many times the world stops.
    /// </summary>
    /// <remarks>
    /// <b>The thing that must never happen is a second decision.</b> A defeat rolls the drop
    /// tables and spends a one in ten million chance, and neither can honestly be done again.
    /// So the shape every test here checks is the same: decide, write it down, and only then
    /// hand anything over -- and after a restart, resume the decision that was written rather
    /// than making a new one.
    ///
    /// <b>Nothing here re-decides a reward rule.</b> The split is Phase 13's, the drops are
    /// the drop tables', and the claimant is the party policy's. These tests are about the
    /// journey from a decision to a delivery and back again.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterRewardOutboxTests : MonsterTestBase
    {
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

        /// <summary>A roll source that always succeeds and counts how often it was asked.</summary>
        /// <remarks>The count is the whole point: a retry that consulted the drop tables
        /// again would show up here as a second request.</remarks>
        private sealed class CountingRolls : IRandomResultSource, IRandomRangeSource
        {
            public int Requests { get; private set; }

            public bool Succeeds(float chance)
            {
                Requests++;

                return true;
            }

            public int Range(int min, int max) => min;
        }

        private const string Worth50 = "monster.worth50";
        private const string Hoard = "monster.hoard";
        private const string HoardTable = "drop.hoard";

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterLootRegistry _loot;
        private CharacterProgressionDefinition _curve;
        private readonly List<Object> _local = new List<Object>();

        private MonsterLootRegistry Loot => _loot;

        private CountingRolls Rolls;

        [SetUp]
        public void SetUpWorld()
        {
            // Coin is authored by the base fixture; registering it again would be a
            // second definition of one item.
            AddDropTable(HoardTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 2, 2, 0.5f),
            });

            AddMonster(Worth50, level: 5, experience: 50);
            AddMonster(Hoard, level: 5, experience: 50, lootTable: HoardTable);

            _curve = Curve();
            _store = new FakeStore();
            Rolls = new CountingRolls();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, 8);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);
        }

        [TearDown]
        public void TearDownWorld()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.outbox-test\"},\"_minLevel\":1,"
                + "\"_maxLevel\":20,\"_experienceToNextLevel\":[100,100,100,100,100,100,100,"
                + "100,100,100,100,100,100,100,100,100,100,100,100]}", definition);

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

        private LivingCharacter AddPlayer(string character, string map = HomeMap,
            int level = 5)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, level, 0, 100, 50,
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

        /// <summary>An authority wired to this world, with or without durable rewards.</summary>
        private MonsterRewardAuthority Authority(IMonsterRewardOutbox outbox,
            bool drops = true)
        {
            var authority = new MonsterRewardAuthority(_runtime, _players, _curve,
                drops ? _loot : null, drops ? Items : null,
                drops ? DropTables : null, Rolls, Rolls, 300f, 0f, null, 0f, outbox);

            _loot.Observe(authority);

            return authority;
        }

        private LivingMonster Corpse(string monster = Worth50, string map = HomeMap)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(map)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.State.DefinitionId.Value != monster || !living.IsAlive) continue;

                living.State.ApplyHealthDelta(-10000);

                return living;
            }

            Assert.Fail("no living monster '" + monster + "'");

            return null;
        }

        private static InstanceId Of(LivingCharacter character)
        {
            return character.Combatant.CombatantId;
        }

        /// <summary>
        /// Storage that outlives a world, and can be taken away.
        /// </summary>
        /// <remarks>Keyed by defeat, exactly as the real table's UNIQUE is, so recording the
        /// same defeat twice hands back the first reward rather than minting a second.</remarks>
        private sealed class FakeOutbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            public bool Broken { get; set; }

            public int Records { get; private set; }

            public int Progresses { get; private set; }

            public IReadOnlyList<PersistedMonsterReward> All()
            {
                return _byDefeat.Values.ToList();
            }

            public PersistedMonsterReward Of(InstanceId defeat)
            {
                return _byDefeat.TryGetValue(defeat.Value, out PersistedMonsterReward stored)
                    ? stored
                    : default;
            }

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
                Records++;

                if (Broken)
                {
                    return MonsterRewardOutboxResult.Failed(
                        MonsterRewardOutboxFailure.Unreachable, "backend down");
                }

                if (_byDefeat.TryGetValue(reward.Defeat.Value,
                    out PersistedMonsterReward already))
                {
                    return MonsterRewardOutboxResult.Recorded(already.RewardId,
                        already.Revision, true);
                }

                var stored = Rebuilt(reward, reward.RewardId, 1);

                _byDefeat[reward.Defeat.Value] = stored;

                return MonsterRewardOutboxResult.Recorded(stored.RewardId, 1, false);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                var pending = new List<PersistedMonsterReward>();

                if (Broken) return pending;

                foreach (PersistedMonsterReward stored in _byDefeat.Values)
                {
                    if (!stored.IsComplete) pending.Add(stored);
                }

                return pending;
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete)
            {
                Progresses++;

                if (Broken)
                {
                    return MonsterRewardOutboxResult.Failed(
                        MonsterRewardOutboxFailure.Unreachable, "backend down");
                }

                foreach (string key in _byDefeat.Keys.ToList())
                {
                    PersistedMonsterReward stored = _byDefeat[key];

                    if (stored.RewardId != rewardId) continue;

                    if (stored.Revision != revision)
                    {
                        return MonsterRewardOutboxResult.Failed(
                            MonsterRewardOutboxFailure.StaleRevision, "somebody wrote first");
                    }

                    var grants = new List<MonsterRewardGrant>();

                    foreach (MonsterRewardGrant grant in stored.Experience)
                    {
                        bool paid = grant.IsDelivered || (experienceDelivered != null
                            && experienceDelivered.Contains(grant.Character));

                        grants.Add(new MonsterRewardGrant(grant.Character,
                            grant.Experience, paid));
                    }

                    var entries = new List<MonsterRewardLootEntry>();

                    foreach (MonsterRewardLootEntry entry in stored.Entries)
                    {
                        MonsterRewardLootEntry updated = entry;

                        if (!entry.IsClaimed && lootClaimed != null)
                        {
                            foreach (MonsterRewardLootEntry taken in lootClaimed)
                            {
                                if (taken.Index != entry.Index) continue;

                                updated = new MonsterRewardLootEntry(entry.Index,
                                    entry.Item, entry.Quantity, entry.Rarity, true,
                                    taken.ClaimedBy);
                            }
                        }

                        entries.Add(updated);
                    }

                    _byDefeat[key] = new PersistedMonsterReward(stored.RewardId,
                        stored.Defeat, stored.Monster, stored.Map, stored.Killer,
                        stored.Loot, stored.LootPolicy, stored.Claimant,
                        stored.X, stored.Y, stored.Z, stored.Party, stored.Cursor,
                        stored.HasCursor, grants, entries,
                        cursorCommitted ?? stored.IsCursorCommitted,
                        lootPublished ?? stored.IsLootPublished,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            /// <summary>Seeds storage directly, as a world that stopped would have left it.</summary>
            public void Seed(PersistedMonsterReward reward)
            {
                _byDefeat[reward.Defeat.Value] = reward;
            }

            private static PersistedMonsterReward Rebuilt(PersistedMonsterReward reward,
                string rewardId, int revision)
            {
                return new PersistedMonsterReward(rewardId, reward.Defeat, reward.Monster,
                    reward.Map, reward.Killer, reward.Loot, reward.LootPolicy,
                    reward.Claimant, reward.X, reward.Y, reward.Z, reward.Party,
                    reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(reward.Experience),
                    new List<MonsterRewardLootEntry>(reward.Entries),
                    reward.IsCursorCommitted, reward.IsLootPublished, false, revision);
            }
        }

        private FakeOutbox _outbox;

        [SetUp]
        public void SetUpOutbox()
        {
            _outbox = new FakeOutbox();
        }

        // ---- the decision is written before anything is handed over ---------------------

        [Test]
        public void ADefeatIsWrittenDownBeforeAnyOfItIsHandedOver()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            LivingMonster corpse = Corpse(Hoard);

            MonsterRewardResult granted = rewards.Grant(corpse.Instance, Of(hero));

            Assert.That(granted.IsGranted, Is.True, granted.ToString());
            Assert.That(_outbox.Records, Is.EqualTo(1), "the decision was never recorded");

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.Exists, Is.True);
            Assert.That(stored.Defeat, Is.EqualTo(corpse.Instance),
                "the reward is not keyed by this monster's life");
            Assert.That(stored.Killer, Is.EqualTo(hero.Character));
        }

        [Test]
        public void NothingIsPaidOrPublishedWhenTheDecisionCannotBeWrittenDown()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            long before = hero.Domain.Progression.Experience;

            _outbox.Broken = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("holding the reward"));

            MonsterRewardResult refused = rewards.Grant(Corpse(Hoard).Instance, Of(hero));

            Assert.That(refused.IsGranted, Is.False);
            Assert.That(refused.Reason,
                Is.EqualTo(MonsterRewardRejection.RewardNotRecorded));

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(before),
                "experience was paid for a decision that was never written down");

            Assert.That(_loot.All().Count, Is.Zero,
                "a pile reached the world before its decision was durable");

            Assert.That(rewards.HeldCount, Is.EqualTo(1), "the decision was discarded");
        }

        [Test]
        public void TheDecisionIsMadeOnceAndTheRollIsNotRepeatedOnRetry()
        {
            // The whole point of the outbox: the drop tables were already consulted, so a
            // retry must reuse what they said rather than asking them again.
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            int rollsBefore = Rolls.Requests;

            _outbox.Broken = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("holding the reward"));

            rewards.Grant(Corpse(Hoard).Instance, Of(hero));

            int rollsAfterDecision = Rolls.Requests;

            Assert.That(rollsAfterDecision, Is.GreaterThan(rollsBefore),
                "the fixture never rolled anything, so this proves nothing");

            _outbox.Broken = false;

            // Several retries, as a world coming back would make.
            for (var i = 0; i < 5; i++) rewards.RetryHeld();

            Assert.That(Rolls.Requests, Is.EqualTo(rollsAfterDecision),
                "the drop tables were consulted again on retry");

            // Recovered and delivered: the pile the first attempt decided on is now in the
            // world, and there is exactly one of it.
            Assert.That(_loot.All().Count, Is.EqualTo(1),
                "the retry never published the decided pile");

            Assert.That(_outbox.All().Count, Is.EqualTo(1),
                "the retries recorded more than one decision");
        }

        [Test]
        public void OneDefeatProducesOneRewardHoweverManyTimesItIsRecorded()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            LivingMonster corpse = Corpse(Hoard);

            rewards.Grant(corpse.Instance, Of(hero));

            long afterFirst = hero.Domain.Progression.Experience;
            int recordsAfterFirst = _outbox.Records;

            // The same defeat offered again. Resuming it is legitimate -- that is how a
            // retry finishes one -- but it must not decide, record or pay a second time.
            rewards.Grant(corpse.Instance, Of(hero));

            Assert.That(_outbox.All().Count, Is.EqualTo(1),
                "one monster's death produced " + _outbox.All().Count + " rewards");

            Assert.That(_outbox.Records, Is.EqualTo(recordsAfterFirst),
                "the decision was written down twice");

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(afterFirst),
                "experience was paid twice for one defeat");
        }

        // ---- what is stored, and what is not --------------------------------------------

        [Test]
        public void AStoredRewardCarriesDecidedFactsAndNoRuntimeState()
        {
            string[] fields = typeof(PersistedMonsterReward).GetProperties()
                .Select(p => p.Name.ToLowerInvariant()).ToArray();

            foreach (string forbidden in new[]
            {
                "connection", "networkobject", "seed", "random", "rng", "damage",
                "health", "ai", "prefab", "state",
            })
            {
                Assert.That(fields.Any(f => f == forbidden), Is.False,
                    "a stored reward carries '" + forbidden + "'");
            }

            // The decided facts, and the delivery bookkeeping that makes them idempotent.
            Assert.That(fields, Does.Contain("defeat"));
            Assert.That(fields, Does.Contain("claimant"));
            Assert.That(fields, Does.Contain("experience"));
            Assert.That(fields, Does.Contain("entries"));
        }

        [Test]
        public void ARareSuccessIsStoredAsTheItemThatActuallyDropped()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            LivingMonster corpse = Corpse(Hoard);

            rewards.Grant(corpse.Instance, Of(hero));

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.Entries.Count, Is.GreaterThan(0),
                "a drop that happened was not written down");

            Assert.That(stored.Loot.IsValid, Is.True,
                "the pile has no durable identity to come back as");

            for (var i = 0; i < stored.Entries.Count; i++)
            {
                Assert.That(stored.Entries[i].Item.IsValid, Is.True);
                Assert.That(stored.Entries[i].Quantity, Is.GreaterThan(0));
            }
        }

        [Test]
        public void AFailedRollIsStoredJustAsFirmlyAsASuccessfulOne()
        {
            // Without a row, a restart would resolve this defeat again and hand out a
            // second chance at whatever did not drop.
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox, drops: false);

            LivingMonster corpse = Corpse();

            rewards.Grant(corpse.Instance, Of(hero));

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.Exists, Is.True,
                "a defeat that dropped nothing was not written down at all");

            Assert.That(stored.Entries.Count, Is.Zero);
            Assert.That(stored.Loot.IsValid, Is.False);
            Assert.That(stored.Experience.Count, Is.GreaterThan(0),
                "a defeat that dropped nothing still owes experience");
        }

        // ---- recovery -------------------------------------------------------------------

        [Test]
        public void AFreshWorldPicksUpWhatTheLastOneNeverFinished()
        {
            LivingCharacter hero = AddPlayer("char-a");

            // A world that decided, wrote it down, and stopped without paying.
            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1"));

            MonsterRewardAuthority rewards = Authority(_outbox);

            Assert.That(rewards.RecoverPending(), Is.EqualTo(1),
                "the new world did not pick up the unfinished reward");

            Assert.That(rewards.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void ARecoveredRewardPaysTheSameSplitToTheSamePeople()
        {
            LivingCharacter hero = AddPlayer("char-a");

            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1"));

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();

            long before = hero.Domain.Progression.Experience;

            rewards.RetryHeld();

            Assert.That(hero.Domain.Progression.Experience - before, Is.EqualTo(50),
                "the recovered reward paid a different amount than was decided");

            // Storage agrees the share is paid, which is what stops a later recovery
            // paying it again.
            Assert.That(_outbox.Of(new InstanceId("defeat-1")).Experience[0].IsDelivered,
                Is.True, "the payment was made but never written down");

            // Still held, and rightly: its pile is on the ground and nobody has taken it.
            // A reward is not finished while one of its side effects can still be lost.
            Assert.That(rewards.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void ARecoveredPileComesBackWithTheIdentityItWasDecidedWith()
        {
            LivingCharacter hero = AddPlayer("char-a");

            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1"));

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            Assert.That(_loot.All().Count, Is.EqualTo(1),
                "the recovered pile did not come back");

            Assert.That(_loot.All()[0].LootId.Value, Is.EqualTo("loot-1"),
                "recovery minted a new pile instead of restoring the decided one");
        }

        [Test]
        public void AnItemSomebodyAlreadyCarriedIsNotPutBackOnTheGround()
        {
            // The pickup crash window: the item reached a bag, the world stopped before it
            // could say so, and a restart must not respawn it.
            LivingCharacter hero = AddPlayer("char-a");

            PersistedMonsterReward decided = Decided("defeat-1", hero.Character,
                Loot: "loot-1", claimed: true);

            _outbox.Seed(decided);

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            Assert.That(_loot.All().Count, Is.Zero,
                "an item already in somebody's bag was put back on the floor");
        }

        [Test]
        public void ExperienceAlreadyPaidIsNotPaidAgainOnRecovery()
        {
            LivingCharacter hero = AddPlayer("char-a");

            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1", paid: true,
                claimed: true));

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();

            long before = hero.Domain.Progression.Experience;

            rewards.RetryHeld();

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(before),
                "a recovered reward paid experience that had already been paid");
        }

        [Test]
        public void ARecoveredRewardIsNotPickedUpTwiceByTheSameWorld()
        {
            LivingCharacter hero = AddPlayer("char-a");

            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1"));

            MonsterRewardAuthority rewards = Authority(_outbox);

            Assert.That(rewards.RecoverPending(), Is.EqualTo(1));

            // Every other member logging in must not re-read and re-hold the same work.
            Assert.That(rewards.RecoverPending(), Is.Zero);
            Assert.That(rewards.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void ARewardNamingAnItemThisBuildDoesNotHaveIsRefusedRatherThanRepaired()
        {
            // Substituting another item would hand out something nobody rolled, and
            // dropping the entry would swallow a rare drop and call the reward finished.
            LivingCharacter hero = AddPlayer("char-a");

            PersistedMonsterReward corrupt = new PersistedMonsterReward("reward-x",
                new InstanceId("defeat-x"), new DefinitionId(Worth50),
                new DefinitionId(HomeMap), hero.Character, new InstanceId("loot-x"), 0,
                hero.Character, 0f, 0f, 0f, default, 0, false,
                new[] { new MonsterRewardGrant(hero.Character, 10) },
                new[] { new MonsterRewardLootEntry(0,
                    new DefinitionId("item.nobody.authored.this"), 1) });

            _outbox.Seed(corrupt);

            MonsterRewardAuthority rewards = Authority(_outbox);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("cannot recover"));

            Assert.That(rewards.RecoverPending(), Is.Zero,
                "a reward naming content this build does not have was recovered anyway");

            Assert.That(rewards.HeldCount, Is.Zero);
            Assert.That(_loot.All().Count, Is.Zero);
        }

        [Test]
        public void TheClaimantAReconstructedRewardUsesIsTheOneItWasDecidedWith()
        {
            // Party membership moves. The historic owner of a drop does not.
            LivingCharacter hero = AddPlayer("char-a");
            LivingCharacter other = AddPlayer("char-b");

            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1",
                claimant: other.Character));

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            Assert.That(_loot.All().Count, Is.EqualTo(1));
            Assert.That(_loot.All()[0].EligibleCharacter, Is.EqualTo(other.Character),
                "recovery re-derived the claimant instead of using the decided one");
        }

        // ---- a world with no outbox is the world that came before -----------------------

        [Test]
        public void AWorldComposedWithoutAnOutboxBehavesExactlyAsItDidBefore()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(null);

            MonsterRewardResult granted = rewards.Grant(Corpse(Hoard).Instance, Of(hero));

            Assert.That(granted.IsGranted, Is.True, granted.ToString());
            Assert.That(rewards.HeldCount, Is.Zero);
            Assert.That(rewards.RecoverPending(), Is.Zero);
        }

        // ---- one authority, one loot system, no client seam -----------------------------

        [Test]
        public void ThereIsStillOneRewardAuthorityAndOneLootRegistry()
        {
            string[] server = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(Application.dataPath, "_Game/Scripts"),
                "*.cs", System.IO.SearchOption.AllDirectories);

            var authorities = 0;
            var registries = 0;

            foreach (string file in server)
            {
                string code = System.IO.File.ReadAllText(file);

                if (code.Contains("class MonsterRewardAuthority")) authorities++;
                if (code.Contains("class MonsterLootRegistry")) registries++;
            }

            Assert.That(authorities, Is.EqualTo(1),
                "a second monster reward authority appeared");
            Assert.That(registries, Is.EqualTo(1),
                "a second loot registry appeared");
        }

        [Test]
        public void NoClientMessageCanNameARewardOrDriveRecovery()
        {
            // Recovery is the server's alone. A client that could name a reward id could
            // ask for somebody else's drop to be published, or a completed one replayed.
            string[] network = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(Application.dataPath, "_Game/Scripts/Network"),
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in network)
            {
                string code = System.IO.File.ReadAllText(file);

                foreach (string forbidden in new[]
                {
                    "RewardId", "RecoverPending", "MonsterRewardOutbox",
                    "PersistedMonsterReward",
                })
                {
                    Assert.That(code, Does.Not.Contain(forbidden),
                        System.IO.Path.GetFileName(file) + " lets a client name '"
                        + forbidden + "'");
                }
            }
        }

        [Test]
        public void RecoveryTakesNothingFromACaller()
        {
            // Same rule the rest of this authority already keeps: nothing public accepts a
            // figure or an identity from anybody, so no caller can steer a reward.
            foreach (MethodInfo method in typeof(MonsterRewardAuthority).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.EqualTo(typeof(InstanceId)),
                        method.Name + " takes " + parameter.ParameterType.Name);
                }
            }
        }

        /// <summary>A decision as a world that stopped would have left it in storage.</summary>
        private PersistedMonsterReward Decided(string defeat, CharacterId killer,
            string Loot = "", bool paid = false, bool claimed = false,
            CharacterId claimant = default, InstanceId instance = default)
        {
            var entries = new List<MonsterRewardLootEntry>();

            if (!string.IsNullOrEmpty(Loot))
            {
                entries.Add(new MonsterRewardLootEntry(0, new DefinitionId(Coin), 2,
                    default, claimed, claimed ? killer : default,
                    instance.IsValid ? instance : new InstanceId("item-" + defeat)));
            }

            return new PersistedMonsterReward("reward-" + defeat,
                new InstanceId(defeat), new DefinitionId(Worth50),
                new DefinitionId(HomeMap), killer,
                string.IsNullOrEmpty(Loot) ? default : new InstanceId(Loot),
                (int)LootPolicy.OwnerOnly, claimant.IsValid ? claimant : killer,
                1f, 2f, 3f, default, 0, false,
                new[] { new MonsterRewardGrant(killer, 50, paid) },
                entries, false, false, false, 1);
        }
    

        // ---- 18.15A: one entry, one item, however often it is delivered ------------------

        [Test]
        public void ADecidedDropIsGivenTheIdentityItWillHaveBeforeAnybodyPicksItUp()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            LivingMonster corpse = Corpse(Hoard);

            rewards.Grant(corpse.Instance, Of(hero));

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.Entries.Count, Is.EqualTo(1));
            Assert.That(stored.Entries[0].Instance.IsValid, Is.True,
                "the drop was written down with no identity to deliver it by");

            // And the pile in the world carries the same one, so the pickup uses it.
            Assert.That(_loot.All()[0].Contents[0].Instance,
                Is.EqualTo(stored.Entries[0].Instance),
                "the pile and the record disagree about what the item will be");
        }

        [Test]
        public void ThePickedUpItemHasExactlyTheIdentityTheRewardDecided()
        {
            LivingCharacter hero = AddPlayer("char-a");
            MonsterRewardAuthority rewards = Authority(_outbox);

            LivingMonster corpse = Corpse(Hoard);

            rewards.Grant(corpse.Instance, Of(hero));

            InstanceId decided = _outbox.Of(corpse.Instance).Entries[0].Instance;

            LootObjectState pile = _loot.All()[0];

            StandOn(hero, pile);

            Assert.That(_loot.Pickup(pile.LootId, 0, hero.Character).IsAccepted, Is.True);

            Assert.That(hero.Inventory.IndexOf(decided), Is.GreaterThanOrEqualTo(0),
                "the item that arrived is not the one the reward decided on");
        }

        [Test]
        public void TheIdentityDoesNotChangeHoweverOftenTheWorldRestartsBeforePickup()
        {
            LivingCharacter hero = AddPlayer("char-a");

            var seeded = Decided("defeat-1", hero.Character, Loot: "loot-1");

            _outbox.Seed(seeded);

            InstanceId first = seeded.Entries[0].Instance;

            // Four restarts, none of which delivers anything.
            for (var restart = 0; restart < 4; restart++)
            {
                MonsterRewardAuthority rewards = Authority(_outbox);

                rewards.RecoverPending();
                rewards.RetryHeld();

                Assert.That(_loot.All().Count, Is.EqualTo(1),
                    "restart " + restart + " lost the pile");

                Assert.That(_loot.All()[0].Contents[0].Instance, Is.EqualTo(first),
                    "the item identity changed on restart " + restart);

                _loot.Remove(_loot.All()[0].LootId);
            }
        }

        [Test]
        public void AnItemAlreadyCarriedIsReconciledRatherThanPutBackOnTheGround()
        {
            // The crash window this gate closes: the bag was written, the delivery stamp
            // was not, and the world stopped in between.
            LivingCharacter hero = AddPlayer("char-a");

            var decided = Decided("defeat-1", hero.Character, Loot: "loot-1");

            _outbox.Seed(decided);

            // Durable ownership, with no delivery stamp anywhere.
            hero.Inventory.Add(new ItemInstance(decided.Entries[0].Instance,
                new DefinitionId(Coin), hero.Owner, 2), Items);

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            Assert.That(_loot.All().Count, Is.Zero,
                "an item already in the bag was put back on the floor");

            Assert.That(Carried(hero, Coin), Is.EqualTo(2),
                "the reconciliation duplicated or removed the item");

            Assert.That(_outbox.Of(new InstanceId("defeat-1")).Entries[0].IsClaimed,
                Is.True, "the delivery was never reconciled in storage");
        }

        [Test]
        public void AnItemHeldByTheWrongCharacterIsAConflictAndNotADelivery()
        {
            // Moving it would move an item nobody asked to move; republishing it would
            // make a second one. Neither is acceptable, so it is reported and left.
            LivingCharacter owner = AddPlayer("char-a");
            LivingCharacter stranger = AddPlayer("char-b");

            var decided = Decided("defeat-1", owner.Character, Loot: "loot-1");

            _outbox.Seed(decided);

            stranger.Inventory.Add(new ItemInstance(decided.Entries[0].Instance,
                new DefinitionId(Coin), stranger.Owner, 2), Items);

            MonsterRewardAuthority rewards = Authority(_outbox);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("is held by"));

            rewards.RecoverPending();

            Assert.That(_outbox.Of(new InstanceId("defeat-1")).Entries[0].IsClaimed,
                Is.False, "somebody else's copy was accepted as delivery");

            Assert.That(Carried(stranger, Coin), Is.EqualTo(2),
                "the conflicting item was moved");

            Assert.That(Carried(owner, Coin), Is.Zero,
                "the item was silently transferred to the decided claimant");
        }

        [Test]
        public void PickingUpTheSameEntryTwiceCannotProduceTwoItems()
        {
            // Replay, a new sequence, or a recovered pile a second time: all the same
            // question, and the decided identity is what answers it.
            LivingCharacter hero = AddPlayer("char-a");

            var decided = Decided("defeat-1", hero.Character, Loot: "loot-1");

            _outbox.Seed(decided);

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            LootObjectState pile = _loot.All()[0];

            StandOn(hero, pile);

            Assert.That(_loot.Pickup(pile.LootId, 0, hero.Character).IsAccepted, Is.True);
            Assert.That(Carried(hero, Coin), Is.EqualTo(2));

            // The pile is put back by a world that never saw the delivery land.
            var second = new MonsterRewardAuthority(_runtime, _players, _curve, _loot,
                Items, DropTables, Rolls, Rolls, 300f, 0f, null, 0f, _outbox);

            _loot.Observe(second);

            // Storage still says unclaimed only if the stamp never landed; force that
            // reading by rebuilding from a record whose entry is unclaimed.
            _outbox.Seed(Decided("defeat-1", hero.Character, Loot: "loot-1"));

            second.RecoverPending();
            second.RetryHeld();

            Assert.That(Carried(hero, Coin), Is.EqualTo(2),
                "a second delivery of one entry produced a second item");

            Assert.That(_loot.All().Count, Is.Zero,
                "the already-carried entry was published again");
        }

        [Test]
        public void AFullBagLeavesTheEntryUndeliveredAndKeepsItsIdentity()
        {
            LivingCharacter hero = AddPlayer("char-a");

            var decided = Decided("defeat-1", hero.Character, Loot: "loot-1");

            _outbox.Seed(decided);

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            LootObjectState pile = _loot.All()[0];

            StandOn(hero, pile);

            // Fill every slot with something that will not stack with a coin.
            for (var i = 0; i < hero.Inventory.Capacity; i++)
            {
                hero.Inventory.Add(new ItemInstance(InstanceId.New(),
                    new DefinitionId(Relic), hero.Owner, 1), Items);
            }

            Assert.That(hero.Inventory.IsFull, Is.True, "the bag fixture is not full");

            Assert.That(_loot.Pickup(pile.LootId, 0, hero.Character).IsAccepted, Is.False,
                "a full bag accepted the item anyway");

            Assert.That(_outbox.Of(new InstanceId("defeat-1")).Entries[0].IsClaimed,
                Is.False, "a refused delivery was marked as delivered");

            // Still the same identity waiting, not a new one.
            Assert.That(_loot.All()[0].Contents[0].Instance,
                Is.EqualTo(decided.Entries[0].Instance),
                "a refused delivery changed the item's identity");
        }

        [Test]
        public void TwoCharactersRacingForOneEntryProduceOneItem()
        {
            LivingCharacter winner = AddPlayer("char-a");
            LivingCharacter loser = AddPlayer("char-b");

            var decided = Decided("defeat-1", winner.Character, Loot: "loot-1");

            _outbox.Seed(decided);

            MonsterRewardAuthority rewards = Authority(_outbox);

            rewards.RecoverPending();
            rewards.RetryHeld();

            LootObjectState pile = _loot.All()[0];

            StandOn(winner, pile);
            StandOn(loser, pile);

            Assert.That(_loot.Pickup(pile.LootId, 0, loser.Character).IsAccepted, Is.False,
                "a character the drop was not decided for took it");

            Assert.That(_loot.Pickup(pile.LootId, 0, winner.Character).IsAccepted, Is.True);

            Assert.That(Carried(winner, Coin), Is.EqualTo(2));
            Assert.That(Carried(loser, Coin), Is.Zero,
                "the loser of the race got a copy");
        }

        [Test]
        public void ARewardWhoseDropHasNoDecidedIdentityIsRefusedRatherThanGivenOne()
        {
            LivingCharacter hero = AddPlayer("char-a");

            // A record written before identities were decided, or a corrupt one.
            _outbox.Seed(new PersistedMonsterReward("reward-x",
                new InstanceId("defeat-x"), new DefinitionId(Worth50),
                new DefinitionId(HomeMap), hero.Character, new InstanceId("loot-x"), 0,
                hero.Character, 0f, 0f, 0f, default, 0, false,
                new[] { new MonsterRewardGrant(hero.Character, 10) },
                new[] { new MonsterRewardLootEntry(0, new DefinitionId(Coin), 1) }));

            MonsterRewardAuthority rewards = Authority(_outbox);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("cannot recover"));

            Assert.That(rewards.RecoverPending(), Is.Zero,
                "a drop with no identity was recovered and would have been minted one");

            Assert.That(_loot.All().Count, Is.Zero);
        }

        [Test]
        public void TheDecidedIdentityIsAnOrdinaryInstanceIdAndNotASecondKindOfKey()
        {
            // Reusing InstanceId is the point: no parallel id type, no client-supplied id,
            // and nothing in the network layer that could name one.
            Assert.That(typeof(MonsterRewardLootEntry).GetProperty("Instance").PropertyType,
                Is.EqualTo(typeof(InstanceId)));

            Assert.That(typeof(LootResult).GetProperty("Instance").PropertyType,
                Is.EqualTo(typeof(InstanceId)));

            string[] network = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(Application.dataPath, "_Game/Scripts/Network"),
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in network)
            {
                string code = System.IO.File.ReadAllText(file);

                foreach (string forbidden in new[]
                {
                    "item_instance_id", "ItemInstanceId", "RewardItemId",
                })
                {
                    Assert.That(code, Does.Not.Contain(forbidden),
                        System.IO.Path.GetFileName(file) + " lets a client name '"
                        + forbidden + "'");
                }
            }
        }

        /// <summary>How much of an item this character is actually carrying.</summary>
        private static int Carried(LivingCharacter character, string item)
        {
            var total = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item)
                {
                    total += instance.Quantity;
                }
            }

            return total;
        }

        private static void StandOn(LivingCharacter character, LootObjectState pile)
        {
            character.Combatant.Position = new CombatPosition(pile.Position.X,
                pile.Position.Y, pile.Position.Z);
        }
    }
}
