using System.Collections.Generic;
using System.Linq;
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
    /// One reward, one application, whatever else is in flight.
    /// </summary>
    /// <remarks>
    /// <b>What these tests exist to prevent.</b> Two rewards can be outstanding for the same
    /// character and the same pet at once: a defeat is settled the moment it happens, while
    /// an older reward whose delivery stamp failed is still waiting to be retried. Anything
    /// that remembers only the <em>last</em> reward a recipient received therefore loses the
    /// evidence for the earlier one, and a restart pays it again.
    ///
    /// <b>So the evidence is per reward.</b> A row naming the reward and the recipient,
    /// written in the same transaction as the progression it describes, and retired only by
    /// the transaction that stamps the delivery. Nothing here is keyed on an amount, a
    /// current total or a level -- two rewards can leave a recipient on the same number, so
    /// those cannot answer the question.
    ///
    /// <b>Applied is not paid.</b> A save that fails leaves the experience in memory and the
    /// reward owed; nothing is stamped, because stamping a payment no database has seen is
    /// how experience gets lost rather than duplicated.
    /// </remarks>
    [TestFixture]
    internal sealed class ExperienceRewardLedgerTests : MonsterTestBase
    {
        /// <summary>
        /// A character store and a reward outbox that agree about transaction boundaries.
        /// </summary>
        /// <remarks>
        /// The two fakes are wired together on purpose: in production the application
        /// evidence is written by the character's save transaction and removed by the
        /// reward's progress transaction, and a double that let them drift apart would
        /// prove nothing about the real thing.
        /// </remarks>
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public bool Broken { get; set; }

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                if (Broken)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable);
                }

                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }

            /// <summary>What storage still holds for this character, as rows.</summary>
            public IReadOnlyList<PersistedRewardApplication> Applications(string character)
            {
                return Rows.TryGetValue("session-" + character, out PersistedCharacter row)
                    ? row.RewardApplications
                    : new List<PersistedRewardApplication>();
            }

            /// <summary>
            /// Retires the evidence for one stamped delivery, as progress() does.
            /// </summary>
            /// <remarks>Called by the outbox, in the same step that stamps the delivery,
            /// because that is the transaction that owns it in the backend.</remarks>
            public void Retire(string rewardId, CharacterId character, InstanceId pet)
            {
                string session = "session-" + character.Value;

                if (!Rows.TryGetValue(session, out PersistedCharacter row)) return;

                var kept = new List<PersistedRewardApplication>();

                foreach (PersistedRewardApplication applied in row.RewardApplications)
                {
                    bool same = applied.RewardId == rewardId && applied.Pet == pet;

                    if (!same) kept.Add(applied);
                }

                Rows[session] = new PersistedCharacter(row.Character, row.Account,
                    row.Server, row.Name, row.Gender, row.Level, row.Experience,
                    row.CurrentHealth, row.CurrentMana, row.Class, row.Job, row.Map,
                    row.Spawn, row.Stats, row.Appearance, row.Skills, row.SaveRevision,
                    row.Items, row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource,
                    row.Pets, row.ActivePet, kept);
            }
        }

        private sealed class FakeOutbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            private readonly FakeStore _store;

            public FakeOutbox(FakeStore store) => _store = store;

            public bool Broken { get; set; }

            /// <summary>Reward ids whose delivery stamps are refused. Empty means none.</summary>
            public readonly HashSet<string> StampsRefused = new HashSet<string>();

            /// <summary>Refuses every stamp, for the window a test wants one lost in.</summary>
            public bool RefuseAllStamps { get; set; }

            public IReadOnlyList<PersistedMonsterReward> All() => _byDefeat.Values.ToList();

            public PersistedMonsterReward Of(InstanceId defeat)
            {
                return _byDefeat.TryGetValue(defeat.Value, out PersistedMonsterReward r)
                    ? r
                    : default;
            }

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
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

                _byDefeat[reward.Defeat.Value] = With(reward, reward.Experience,
                    reward.PetExperience, false, 1);

                return MonsterRewardOutboxResult.Recorded(reward.RewardId, 1, false);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                var pending = new List<PersistedMonsterReward>();

                foreach (PersistedMonsterReward stored in _byDefeat.Values)
                {
                    if (!stored.IsComplete) pending.Add(stored);
                }

                return pending;
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete,
                IReadOnlyList<InstanceId> petExperienceDelivered = null)
            {
                if (Broken || RefuseAllStamps || StampsRefused.Contains(rewardId))
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

                        // Stamped and retired together, as one transaction does both.
                        if (paid && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Character, default);
                        }

                        grants.Add(new MonsterRewardGrant(grant.Character, grant.Experience,
                            paid));
                    }

                    var pets = new List<MonsterRewardPetGrant>();

                    foreach (MonsterRewardPetGrant grant in stored.PetExperience)
                    {
                        bool paid = grant.IsDelivered;

                        for (var i = 0; !paid && petExperienceDelivered != null
                            && i < petExperienceDelivered.Count; i++)
                        {
                            paid = petExperienceDelivered[i] == grant.Pet;
                        }

                        if (paid && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Owner, grant.Pet);
                        }

                        pets.Add(new MonsterRewardPetGrant(grant.Owner, grant.Pet,
                            grant.Experience, paid));
                    }

                    _byDefeat[key] = With(stored, grants, pets,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward With(PersistedMonsterReward reward,
                IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets, bool complete, int revision)
            {
                return new PersistedMonsterReward(reward.RewardId, reward.Defeat,
                    reward.Monster, reward.Map, reward.Killer, reward.Loot,
                    reward.LootPolicy, reward.Claimant, reward.X, reward.Y, reward.Z,
                    reward.Party, reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(reward.Entries),
                    reward.IsCursorCommitted, reward.IsLootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        private sealed class CountingRolls : IRandomResultSource, IRandomRangeSource
        {
            public readonly List<float> Chances = new List<float>();

            public bool Succeeds(float chance)
            {
                Chances.Add(chance);

                return true;
            }

            public int Range(int min, int max) => min;
        }

        private const string Worth100 = "monster.ledger100";
        private const string SlimePet = "pet.ledger-slime";
        private const float Share = 0.25f;

        /// <summary>A quarter of a hundred, floored. The pet amount every test expects.</summary>
        private const int PetAward = 25;

        private FakeStore _store;
        private FakeOutbox _outbox;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterLootRegistry _loot;
        private CharacterProgressionDefinition _curve;
        private DefinitionRegistry<PetDefinition> _pets;
        private CountingRolls _rolls;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpLedger()
        {
            AddMonster(Worth100, level: 5, experience: 100);

            _curve = Curve();
            _store = new FakeStore();
            _outbox = new FakeOutbox(_store);
            _rolls = new CountingRolls();

            _pets = new DefinitionRegistry<PetDefinition>();
            _pets.Register(Pet(SlimePet));

            _players = NewRegistry();

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);
        }

        [TearDown]
        public void TearDownLedger()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- the closure test ------------------------------------------------------------

        [Test]
        public void AnUnstampedRewardIsNotErasedByTheNextOneAndNeitherIsPaidTwice()
        {
            // R1 lands but is never stamped; R2 then lands normally. A marker that
            // remembered only the last reward would now say R2, and R1 -- still owed
            // according to storage -- would be paid a second time after a restart.
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            InstanceId first = Corpse().Instance;

            LogAssert.ignoreFailingMessages = true;

            GrantUnstamped(rewards, first, hero);

            InstanceId second = Corpse().Instance;

            rewards.Grant(second, Of(hero));

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_outbox.Of(first).Experience[0].IsDelivered, Is.False,
                "precondition: R1's stamp never landed");
            Assert.That(_outbox.Of(second).Experience[0].IsDelivered, Is.True,
                "precondition: R2 was delivered normally");

            long characterAfterBoth = hero.Domain.Progression.Experience;
            int characterLevel = hero.Domain.Progression.Level;
            int petAfterBoth = Pet(hero, "pet-1").Experience;

            Assert.That(petAfterBoth, Is.EqualTo(PetAward * 2),
                "precondition: the pet was paid for both defeats");

            // The process dies -- and the backend that lost the stamp is back. A new world
            // reads storage, which holds both applications and one unstamped reward.
            _outbox.StampsRefused.Clear();

            LivingCharacter returned = Restart("char-a");

            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(characterAfterBoth),
                "precondition: the character's experience was durable");
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(petAfterBoth),
                "precondition: the pet's experience was durable");

            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();

            Retry(recovered);

            Assert.That(returned.Domain.Progression.Experience,
                Is.EqualTo(characterAfterBoth),
                "the character was paid R1 a second time");
            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(characterLevel));
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(petAfterBoth),
                "the pet was paid R1 a second time");

            Assert.That(_outbox.Of(first).Experience[0].IsDelivered, Is.True,
                "R1 was not reconciled");
            Assert.That(_outbox.Of(first).PetExperience[0].IsDelivered, Is.True);
            Assert.That(_outbox.Of(first).IsComplete, Is.True);
        }

        [Test]
        public void ThreeRapidRewardsWithFailuresAtDifferentPointsEachApplyOnce()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            // R1: decided, delivered, stamp lost.
            InstanceId r1 = Corpse().Instance;
            GrantUnstamped(rewards, r1, hero);

            // R2: the character store is down, so nothing about it is durable.
            _store.Broken = true;
            InstanceId r2 = Corpse().Instance;
            rewards.Grant(r2, Of(hero));
            _store.Broken = false;

            // R3: ordinary.
            InstanceId r3 = Corpse().Instance;
            rewards.Grant(r3, Of(hero));

            Retry(rewards);

            LogAssert.ignoreFailingMessages = false;

            _outbox.StampsRefused.Clear();

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            // Three defeats worth a hundred each, and a pet share of each.
            Assert.That(Total(returned), Is.EqualTo(300),
                "three defeats did not pay exactly three times");
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(PetAward * 3),
                "the pet was not paid exactly three times");

            foreach (PersistedMonsterReward reward in _outbox.All())
            {
                Assert.That(reward.IsComplete, Is.True,
                    "reward " + reward.RewardId + " never finished");
            }

            // And nothing is left claiming to be in flight.
            Assert.That(_store.Applications("char-a").Count, Is.Zero,
                "application evidence outlived the deliveries it belonged to");
        }

        [Test]
        public void TwoRewardsWorthTheSameAreStillTwoRewards()
        {
            // Identical amounts, so nothing can tell them apart by value. Only the reward
            // identity can, which is the point.
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId first = Corpse().Instance;
            GrantUnstamped(rewards, first, hero);

            InstanceId second = Corpse().Instance;
            rewards.Grant(second, Of(hero));

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_outbox.Of(first).Experience[0].Experience,
                Is.EqualTo(_outbox.Of(second).Experience[0].Experience),
                "precondition: the two rewards are worth the same");

            Assert.That(Total(hero), Is.EqualTo(200), "one of the two was swallowed");
            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(PetAward * 2));

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(Total(returned), Is.EqualTo(200),
                "a reward worth the same as another was paid twice");
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(PetAward * 2));
        }

        [Test]
        public void TwoRewardsForTwoDifferentPetsStayWithTheirOwnPets()
        {
            // R1 freezes P1, the player switches, R2 freezes P2. Both pets are the same
            // authored kind, so only the instance distinguishes them.
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1", "pet-2"),
                active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId first = Corpse().Instance;
            GrantUnstamped(rewards, first, hero);

            Summon(hero, "pet-2");

            InstanceId second = Corpse().Instance;
            rewards.Grant(second, Of(hero));

            LogAssert.ignoreFailingMessages = false;

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(PetAward),
                "the pet that was out for the first defeat was paid the wrong amount");
            Assert.That(Pet(returned, "pet-2").Experience, Is.EqualTo(PetAward),
                "the pet that was out for the second defeat was paid the wrong amount");

            Assert.That(Pet(returned, "pet-1").DefinitionId,
                Is.EqualTo(Pet(returned, "pet-2").DefinitionId),
                "precondition: the two pets are the same authored kind");
        }

        // ---- one reward, one application -------------------------------------------------------

        [Test]
        public void ACharacterRewardSavedButNotStampedIsReconciledRatherThanPaidAgain()
        {
            LivingCharacter hero = AddPlayer("char-a");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse().Instance;

            GrantUnstamped(rewards, defeat, hero);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Total(hero), Is.EqualTo(100), "precondition: paid");
            Assert.That(_outbox.Of(defeat).Experience[0].IsDelivered, Is.False,
                "precondition: unstamped");

            LivingCharacter returned = Restart("char-a");

            Assert.That(returned.HasAppliedReward(_outbox.Of(defeat).RewardId), Is.True,
                "the character carries no evidence of what it was already paid");

            _outbox.StampsRefused.Clear();

            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(Total(returned), Is.EqualTo(100),
                "the character was paid a second time");
            Assert.That(_outbox.Of(defeat).Experience[0].IsDelivered, Is.True);
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True);
        }

        [Test]
        public void APetRewardSavedButNotStampedIsReconciledRatherThanPaidAgain()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse().Instance;

            GrantUnstamped(rewards, defeat, hero);

            LogAssert.ignoreFailingMessages = false;

            LivingCharacter returned = Restart("char-a");

            Assert.That(returned.HasAppliedReward(_outbox.Of(defeat).RewardId,
                new InstanceId("pet-1")), Is.True,
                "the pet's application left no evidence");

            _outbox.StampsRefused.Clear();

            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(PetAward),
                "the pet was paid a second time");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True);
        }

        [Test]
        public void AFailedSaveClaimsNothingAndIsRetriedUntilItLands()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse().Instance;

            rewards.Grant(defeat, Of(hero));

            // Nothing may be stamped: no database has seen any of it.
            Assert.That(_outbox.Of(defeat).Experience[0].IsDelivered, Is.False,
                "a delivery was stamped although the save failed");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False);
            Assert.That(_outbox.Of(defeat).IsComplete, Is.False);

            Retry(rewards);
            Retry(rewards);

            LogAssert.ignoreFailingMessages = false;

            _store.Broken = false;

            Retry(rewards);

            Assert.That(Total(hero), Is.EqualTo(100),
                "repeated attempts while the backend was down multiplied the experience");
            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(PetAward));
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True);
        }

        [Test]
        public void TheSameRewardOfferedAgainAppliesNothingFurther()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            InstanceId defeat = Corpse().Instance;

            rewards.Grant(defeat, Of(hero));

            long character = Total(hero);
            int pet = Pet(hero, "pet-1").Experience;

            rewards.Grant(defeat, Of(hero));
            Retry(rewards);

            Assert.That(Total(hero), Is.EqualTo(character));
            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(pet));
        }

        // ---- corruption is visible ---------------------------------------------------------------

        [Test]
        public void EvidenceForAPetTheCharacterNoLongerOwnsBlocksTheRewardVisibly()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse().Instance;

            GrantUnstamped(rewards, defeat, hero);

            // The character comes back without that pet at all: the reward names a pet
            // nothing can find, and its application evidence names it too.
            _players.Despawn(hero.ConnectionId);

            Seed("char-a", Pets("pet-9"), null);

            _players = NewRegistry();

            LivingCharacter returned = Enter("char-a");

            _outbox.StampsRefused.Clear();

            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-9").Experience, Is.Zero,
                "the experience was redirected to a pet the defeat never named");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "a reward nobody could be paid was stamped anyway");
            Assert.That(_outbox.Of(defeat).IsComplete, Is.False,
                "a reward nobody could be paid was quietly completed");
        }

        // ---- caps still finish ---------------------------------------------------------------------

        [Test]
        public void AMaxLevelPetsRewardStillCompletesAndIsAppliedOnce()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1",
                petExperience: 100000);

            int before = Pet(hero, "pet-1").Level;

            MonsterRewardAuthority rewards = Authority();

            InstanceId defeat = Corpse().Instance;

            rewards.Grant(defeat, Of(hero));

            Assert.That(Pet(hero, "pet-1").Level, Is.EqualTo(before), "the cap was passed");
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True,
                "a max-level pet left the reward pending forever");

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(100000 + PetAward),
                "the capped pet was paid twice");
        }

        [Test]
        public void ACharacterAtTheCurvesCapStillFinishesTheReward()
        {
            // The authored curve tops out at level 20; this character is already there, so
            // Phase 05 banks the experience rather than levelling.
            LivingCharacter hero = AddPlayer("char-a", level: 20);

            MonsterRewardAuthority rewards = Authority();

            InstanceId defeat = Corpse().Instance;

            rewards.Grant(defeat, Of(hero));

            Assert.That(_outbox.Of(defeat).Experience[0].IsDelivered, Is.True);
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True,
                "a capped character left the reward pending forever");

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority recovered = Authority();

            recovered.RecoverPending();
            Retry(recovered);

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(20));
        }

        // ---- shape --------------------------------------------------------------------------------------

        [Test]
        public void NothingKeysIdempotencyOnAnAmountOrATotal()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/MonsterRewardAuthority.cs");

            int applied = source.IndexOf("HasAppliedReward", System.StringComparison.Ordinal);

            Assert.That(applied, Is.GreaterThan(0),
                "the authority no longer asks whether a reward was applied");

            // The pet's own "last reward" marker may be written as a diagnostic, but no
            // decision may be made from reading it back.
            Assert.That(source.Contains("pet.AppliedRewardId"), Is.False,
                "correctness still reads the pet's last-reward marker");
        }

        [Test]
        public void TheApplicationEvidenceIsPerRewardAndPerRecipient()
        {
            string[] fields = typeof(PersistedRewardApplication).GetProperties()
                .Select(p => p.Name.ToLowerInvariant()).ToArray();

            Assert.That(fields, Contains.Item("rewardid"));
            Assert.That(fields, Contains.Item("pet"));

            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1", "pet-2"),
                active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            string r1 = GrantUnstamped(rewards, Corpse().Instance, hero);

            Summon(hero, "pet-2");

            string r2 = GrantUnstamped(rewards, Corpse().Instance, hero);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(r1, Is.Not.EqualTo(r2), "precondition: two rewards");

            // Each application answers for exactly one reward and one recipient.
            Assert.That(hero.HasAppliedReward(r1), Is.True);
            Assert.That(hero.HasAppliedReward(r2), Is.True);

            Assert.That(hero.HasAppliedReward(r1, new InstanceId("pet-1")), Is.True);
            Assert.That(hero.HasAppliedReward(r2, new InstanceId("pet-2")), Is.True);

            // The later reward did not erase what is known about the earlier one, and
            // neither reward answers for the pet it never named.
            Assert.That(hero.HasAppliedReward(r1, new InstanceId("pet-2")), Is.False,
                "one pet's application answered for another pet");
            Assert.That(hero.HasAppliedReward(r2, new InstanceId("pet-1")), Is.False,
                "one reward's application answered for another reward");
            Assert.That(hero.HasAppliedReward("reward-nobody-minted"), Is.False);
        }

        [Test]
        public void ThereIsStillOneRewardOutboxAndOneAuthority()
        {
            var outboxes = new List<string>();

            foreach (System.Type type in typeof(IMonsterRewardOutbox).Assembly.GetTypes())
            {
                if (!type.IsInterface) continue;

                string name = type.Name.ToLowerInvariant();

                if (name.Contains("reward") && name.Contains("outbox"))
                {
                    outboxes.Add(type.FullName);
                }
            }

            Assert.That(outboxes.Count, Is.EqualTo(1),
                "a second reward outbox exists: " + string.Join(", ", outboxes));

            foreach (System.Type type in typeof(MonsterRewardAuthority).Assembly.GetTypes())
            {
                string name = type.Name.ToLowerInvariant();

                Assert.That(name.Contains("experienceledger")
                    || name.Contains("rewardledgerservice"), Is.False,
                    type.FullName + " is a second reward system");
            }
        }

        // ---- harness ---------------------------------------------------------------------------------------

        private WorldCharacterRegistry NewRegistry()
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            return new WorldCharacterRegistry(_store, spawns, Items, 8, null, _pets);
        }

        private MonsterRewardAuthority Authority()
        {
            var authority = new MonsterRewardAuthority(_runtime, _players, _curve, _loot,
                Items, DropTables, _rolls, _rolls, 300f, 0f, null, 0f, _outbox, _pets,
                Share);

            _loot.Observe(authority);

            return authority;
        }

        /// <summary>
        /// Grants a defeat whose delivery stamp never lands, and keeps it that way.
        /// </summary>
        /// <remarks>
        /// The decision is recorded, the experience is applied and saved, and only the
        /// stamp is lost -- which is the crash window every test here stands in. Refusing
        /// by id afterwards keeps that reward unstamped through later retries while other
        /// rewards are stamped normally.
        /// </remarks>
        /// <returns>The reward's own durable id.</returns>
        private string GrantUnstamped(MonsterRewardAuthority rewards, InstanceId defeat,
            LivingCharacter killer)
        {
            _outbox.RefuseAllStamps = true;

            rewards.Grant(defeat, Of(killer));

            _outbox.RefuseAllStamps = false;

            string rewardId = _outbox.Of(defeat).RewardId;

            Assert.That(rewardId, Is.Not.Null.And.Not.Empty,
                "the defeat was never recorded");

            _outbox.StampsRefused.Add(rewardId);

            return rewardId;
        }

        private static void Retry(MonsterRewardAuthority rewards)
        {
            for (var i = 0; i < 320; i++) rewards.RetryHeld();
        }

        private static long Total(LivingCharacter character)
        {
            // Level and remainder together, because the authored curve turns a hundred
            // experience into a level rather than a total.
            return (character.Domain.Progression.Level - 5) * 100
                + character.Domain.Progression.Experience;
        }

        private static PersistedPet[] Pets(params string[] instances)
        {
            var rows = new PersistedPet[instances.Length];

            for (var i = 0; i < instances.Length; i++)
            {
                rows[i] = new PersistedPet(new InstanceId(instances[i]),
                    new DefinitionId(SlimePet), 1, 0, 0);
            }

            return rows;
        }

        private void Seed(string character, PersistedPet[] pets, string active,
            int petExperience = 0, int level = 5)
        {
            var rows = new List<PersistedPet>();

            for (var i = 0; pets != null && i < pets.Length; i++)
            {
                _pets.TryGet(pets[i].Pet, out PetDefinition definition);

                rows.Add(new PersistedPet(pets[i].Instance, pets[i].Pet,
                    PetService.LevelFor(definition, petExperience), petExperience, 0));
            }

            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, level, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1, null, 8, default, null,
                rows.Count == 0 ? null : rows,
                active == null ? default : new InstanceId(active));
        }

        private LivingCharacter Enter(string character)
        {
            WorldSpawnResult result = _players.Spawn(character.GetHashCode(),
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(HomeMap),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            return result.Character;
        }

        private LivingCharacter AddPlayer(string character, PersistedPet[] pets = null,
            string active = null, int petExperience = 0, int level = 5)
        {
            Seed(character, pets, active, petExperience, level);

            return Enter(character);
        }

        /// <summary>A new world over the same storage: the process died, nothing else.</summary>
        private LivingCharacter Restart(string character)
        {
            _players = NewRegistry();

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);

            return Enter(character);
        }

        private static PetInstance Pet(LivingCharacter owner, string instance)
        {
            Assert.That(owner.TryGetPet(new InstanceId(instance), out PetInstance pet),
                Is.True, owner.Character + " does not own " + instance);

            return pet;
        }

        private void Summon(LivingCharacter owner, string instance)
        {
            Assert.That(PetService.TrySummon(owner.Companion, Pet(owner, instance),
                new PetService.Context(_pets, Items, null, owner.Status, owner.Owner))
                .IsAccepted, Is.True);
        }

        private LivingMonster Corpse()
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Worth100),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.State.DefinitionId.Value != Worth100 || !living.IsAlive) continue;

                living.State.ApplyHealthDelta(-10000);

                return living;
            }

            Assert.Fail("no living monster");

            return null;
        }

        private static InstanceId Of(LivingCharacter character)
        {
            return character.Combatant.CombatantId;
        }

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.ledger-test\"},\"_minLevel\":1,"
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

        private PetDefinition Pet(string id)
        {
            var definition = ScriptableObject.CreateInstance<PetDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id
                + ".name\"},\"_followBehavior\":0,\"_verticalOffset\":0,\"_maxLevel\":20,"
                + "\"_auraForm\":false,\"_disabled\":false}", definition);

            var thresholds = new int[19];

            for (var i = 0; i < thresholds.Length; i++) thresholds[i] = (i + 1) * 10;

            typeof(PetDefinition)
                .GetField("_experienceThresholds", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, thresholds);

            _local.Add(definition);

            return definition;
        }
    }
}
