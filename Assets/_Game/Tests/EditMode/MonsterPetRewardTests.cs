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
    /// The pet that was out when the monster died, and nothing else, gets the experience.
    /// </summary>
    /// <remarks>
    /// <b>Two facts are frozen at the defeat: which pet, and how much.</b> Every test here
    /// exists because delivery happens later -- sometimes much later, sometimes in a
    /// different process -- and by then the player may have swapped pets, dismissed the one
    /// that earned it, logged out, or died. None of that may change who is paid.
    ///
    /// <b>The crash slices are the point.</b> A pet's experience and the reward's delivery
    /// stamp live in different aggregates and cannot be written in one transaction, so the
    /// ordering is chosen to fail safely: the pet is saved first with a marker naming the
    /// reward that paid it, and the stamp second. A crash in between therefore looks like
    /// "durable experience, unstamped reward", and the marker is what lets recovery read
    /// that correctly instead of paying twice.
    ///
    /// <b>No rule is re-decided here.</b> The split is Phase 13's, the progression is Phase
    /// 12's, and the drops are the drop tables'. What is under test is the journey.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterPetRewardTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            /// <summary>When set, every save is refused, as a backend outage would.</summary>
            public bool Broken { get; set; }

            public int Saves { get; private set; }

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Saves++;

                if (Broken)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable);
                }

                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        /// <summary>Storage that outlives a world, keyed by defeat exactly as the table is.</summary>
        private sealed class FakeOutbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            public bool Broken { get; set; }

            /// <summary>When set, deliveries land but nothing about them is ever stamped.</summary>
            public bool StampsRefused { get; set; }

            public int Records { get; private set; }

            public IReadOnlyList<PersistedMonsterReward> All() => _byDefeat.Values.ToList();

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

                _byDefeat[reward.Defeat.Value] = With(reward, reward.Experience,
                    reward.PetExperience, reward.Entries, reward.IsCursorCommitted,
                    reward.IsLootPublished, false, 1);

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
                if (Broken || StampsRefused)
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
                        grants.Add(new MonsterRewardGrant(grant.Character, grant.Experience,
                            grant.IsDelivered || (experienceDelivered != null
                                && experienceDelivered.Contains(grant.Character))));
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

                        pets.Add(new MonsterRewardPetGrant(grant.Owner, grant.Pet,
                            grant.Experience, paid));
                    }

                    _byDefeat[key] = With(stored, grants, pets, stored.Entries,
                        cursorCommitted ?? stored.IsCursorCommitted,
                        lootPublished ?? stored.IsLootPublished,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward With(PersistedMonsterReward reward,
                IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets,
                IReadOnlyList<MonsterRewardLootEntry> entries,
                bool cursorCommitted, bool lootPublished, bool complete, int revision)
            {
                return new PersistedMonsterReward(reward.RewardId, reward.Defeat,
                    reward.Monster, reward.Map, reward.Killer, reward.Loot,
                    reward.LootPolicy, reward.Claimant, reward.X, reward.Y, reward.Z,
                    reward.Party, reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(entries),
                    cursorCommitted, lootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        /// <summary>A roll source that always succeeds and counts how often it was asked.</summary>
        private sealed class CountingRolls : IRandomResultSource, IRandomRangeSource
        {
            public int Requests { get; private set; }

            public readonly List<float> Chances = new List<float>();

            public bool Succeeds(float chance)
            {
                Requests++;
                Chances.Add(chance);

                return true;
            }

            public int Range(int min, int max) => min;
        }

        private const string Worth100 = "monster.worth100";
        private const string Boss = "monster.boss";
        private const string BossTable = "drop.boss";

        /// <summary>Two kinds, so a test can tell instance from definition.</summary>
        private const string SlimePet = "pet.itest-slime";
        private const string BirdPet = "pet.itest-bird";

        /// <summary>A quarter of the owner's award, floored. Configuration, not a pet's rule.</summary>
        private const float Share = 0.25f;

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
        public void SetUpPetRewards()
        {
            AddMonster(Worth100, level: 5, experience: 100);

            AddDropTable(BossTable, new[]
            {
                // The two rare chances the boss regression is about.
                new DropEntry(new DefinitionId(Coin), 1, 1, 0.0000001f),
                new DropEntry(new DefinitionId(Hide), 1, 1, 0.000001f),
            });

            AddMonster(Boss, level: 30, experience: 400, lootTable: BossTable);

            _curve = Curve();
            _store = new FakeStore();
            _outbox = new FakeOutbox();
            _rolls = new CountingRolls();

            _pets = new DefinitionRegistry<PetDefinition>();
            _pets.Register(Pet(SlimePet));
            _pets.Register(Pet(BirdPet));

            _players = NewRegistry();

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);
        }

        [TearDown]
        public void TearDownPetRewards()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- frozen at the defeat -----------------------------------------------------------

        [Test]
        public void TheActivePetAtTheDefeatIsWrittenIntoTheDecision()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");
            MonsterRewardAuthority rewards = Authority();

            LivingMonster corpse = Corpse(Worth100);

            rewards.Grant(corpse.Instance, Of(hero));

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.PetExperience.Count, Is.EqualTo(1));
            Assert.That(stored.PetExperience[0].Pet, Is.EqualTo(new InstanceId("pet-1")));
            Assert.That(stored.PetExperience[0].Owner, Is.EqualTo(hero.Character));

            // A quarter of the hundred this monster is worth, floored.
            Assert.That(stored.PetExperience[0].Experience, Is.EqualTo(25));
        }

        [Test]
        public void ACharacterWithNoPetOutIsOwedNothingForOne()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"));
            MonsterRewardAuthority rewards = Authority();

            LivingMonster corpse = Corpse(Worth100);

            rewards.Grant(corpse.Instance, Of(hero));

            PersistedMonsterReward stored = _outbox.Of(corpse.Instance);

            Assert.That(stored.PetExperience.Count, Is.Zero,
                "a phantom row was written for a character with no pet out");
            Assert.That(stored.IsComplete, Is.True,
                "the reward could not finish because of a delivery nobody is owed");

            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero,
                "a pet that was not out was paid anyway");
        }

        [Test]
        public void TheExactInstanceIsPaidAndNotTheOtherOneOfTheSameKind()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1", "pet-2"),
                active: "pet-2");

            Authority().Grant(Corpse(Worth100).Instance, Of(hero));

            Assert.That(Pet(hero, "pet-2").Experience, Is.EqualTo(25),
                "the pet that was out was not the pet that was paid");
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero,
                "the other copy of the same kind was paid as well");
        }

        [Test]
        public void SwitchingPetsAfterTheDefeatCannotRedirectTheExperience()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1", "pet-2"),
                active: "pet-1");

            // The decision is durable; nothing about it is. The character store is down, so
            // no pet progression reaches storage and the world then dies.
            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            _store.Broken = false;

            LivingCharacter returned = Restart("char-a");

            Assert.That(Pet(returned, "pet-1").Experience, Is.Zero,
                "precondition: nothing was durable");

            // In the new world the player puts their other pet out before the reward is
            // recovered. The reward names an instance, so this changes nothing about it.
            Summon(returned, "pet-2");

            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            Retry(second);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "the pet that earned it was not the pet that got it");
            Assert.That(Pet(returned, "pet-2").Experience, Is.Zero,
                "the experience followed the player's current pet instead of the decision");
        }

        [Test]
        public void DismissingThePetAfterTheDefeatCannotStopItBeingPaid()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            Authority().Grant(Corpse(Worth100).Instance, Of(hero));

            _store.Broken = false;

            LivingCharacter returned = Restart("char-a");

            // Put away. There is no follower and no summoned state anywhere, and the
            // reward does not care.
            returned.Companion.Dismiss();

            Assert.That(returned.Companion.IsSummoned, Is.False);

            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            Retry(second);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "a pet that was put away lost experience it had earned");
        }

        // ---- the amount ------------------------------------------------------------------------

        [Test]
        public void ThePetAmountIsAFlooredFractionOfWhatItsOwnerWasAwarded()
        {
            // 100 experience, a quarter share: 25. And with a share that does not divide
            // evenly the answer is floored, never rounded up.
            Assert.That(AmountFor(100, 0.25f), Is.EqualTo(25));
            Assert.That(AmountFor(10, 0.25f), Is.EqualTo(2));
            Assert.That(AmountFor(3, 0.25f), Is.EqualTo(0));
            Assert.That(AmountFor(1, 1f), Is.EqualTo(1));
        }

        [Test]
        public void AWorldConfiguredToGivePetsNothingWritesNoPetRows()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            Authority(share: 0f).Grant(Corpse(Worth100).Instance, Of(hero));

            Assert.That(_outbox.All()[0].PetExperience.Count, Is.Zero);
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero);
        }

        [Test]
        public void AnAmountThatFloorsToNothingIsNotWrittenDownAtAll()
        {
            AddMonster("monster.worth3", level: 1, experience: 3);

            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            Authority().Grant(Corpse("monster.worth3").Instance, Of(hero));

            Assert.That(_outbox.All()[0].PetExperience.Count, Is.Zero,
                "a reward nobody can be paid was written down");
            Assert.That(_outbox.All()[0].IsComplete, Is.True);
        }

        // ---- progression is Phase 12's -----------------------------------------------------------

        [Test]
        public void ThePetLevelsThroughItsOwnAuthoredCurve()
        {
            // The fixture pet needs 10 experience per level. A hundred-experience monster
            // at a quarter share is 25, which is two levels and a remainder -- decided by
            // PetService and not by anything here.
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            Authority().Grant(Corpse(Worth100).Instance, Of(hero));

            PetInstance pet = Pet(hero, "pet-1");

            Assert.That(pet.Experience, Is.EqualTo(25));
            _pets.TryGet(new DefinitionId(SlimePet), out PetDefinition slime);

            Assert.That(pet.Level, Is.EqualTo(PetService.LevelFor(slime, 25)),
                "the level came from somewhere other than the pet's authored curve");
            Assert.That(pet.Level, Is.EqualTo(3), "one grant crossed only one threshold");
        }

        [Test]
        public void APetAtItsAuthoredCapIsPaidAndTheRewardStillFinishes()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1",
                experience: 100000);

            PetInstance pet = Pet(hero, "pet-1");

            int cappedLevel = pet.Level;

            Authority().Grant(Corpse(Worth100).Instance, Of(hero));

            Assert.That(pet.Level, Is.EqualTo(cappedLevel),
                "the authored cap was passed");
            Assert.That(pet.Experience, Is.EqualTo(100025),
                "experience stopped accumulating at the cap, which is not Phase 12 behaviour");

            PersistedMonsterReward stored = _outbox.All()[0];

            Assert.That(stored.PetExperience[0].IsDelivered, Is.True);
            Assert.That(stored.IsComplete, Is.True,
                "a max-level pet left the reward pending forever");
        }

        // ---- durable before any side effect ---------------------------------------------------------

        [Test]
        public void NoPetIsPaidUntilTheDecisionIsWrittenDown()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            _outbox.Broken = true;

            MonsterRewardAuthority rewards = Authority();

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            MonsterRewardResult refused = rewards.Grant(defeat, Of(hero));

            Assert.That(refused.IsGranted, Is.False);
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero,
                "a pet was paid before the decision was durable");
            Assert.That(Pet(hero, "pet-1").Level, Is.EqualTo(1));

            // The same decision, once storage comes back: same pet, same amount, no second
            // roll and no recomputation from anything that has changed since.
            Summon(hero, null);

            _outbox.Broken = false;

            rewards.RetryHeld();

            LogAssert.ignoreFailingMessages = false;

            PersistedMonsterReward stored = _outbox.Of(defeat);

            Assert.That(stored.PetExperience.Count, Is.EqualTo(1));
            Assert.That(stored.PetExperience[0].Pet, Is.EqualTo(new InstanceId("pet-1")),
                "the retry chose a recipient rather than resuming the decision");
            Assert.That(stored.PetExperience[0].Experience, Is.EqualTo(25));
            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(25));
        }

        // ---- crash slices -------------------------------------------------------------------------------

        [Test]
        public void CrashBeforeDeliveryPaysThePetExactlyOnceAfterwards()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            // Decided and durable; the character store is down, so nothing is applied.
            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False);

            // The process dies. A new world, a new registry, nothing in memory.
            _store.Broken = false;

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            second.RetryHeld();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "the pet was not paid after a restart");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True);

            // And again: a second recovery pass changes nothing.
            second.RetryHeld();

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "a second recovery pass paid the pet twice");
        }

        [Test]
        public void CrashAfterThePetIsSavedButBeforeTheStampDoesNotPayTwice()
        {
            // The mandatory slice. The pet's experience is durable; the reward still says
            // it is owed. Only the marker on the pet can tell the difference.
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            _outbox.StampsRefused = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(25),
                "precondition: the pet was paid");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "precondition: the stamp never landed");

            // The process dies here. Everything in memory is lost; storage holds durable
            // pet experience and a reward that still says it is owed.
            _outbox.StampsRefused = false;

            LivingCharacter returned = Restart("char-a");

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "precondition: the experience was durable");
            Assert.That(Pet(returned, "pet-1").AppliedRewardId,
                Is.EqualTo(_outbox.Of(defeat).RewardId),
                "the pet did not record which reward its experience already includes");

            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            second.RetryHeld();

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "recovery paid the pet a second time");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True,
                "recovery did not reconcile the delivery it found");
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True);
        }

        [Test]
        public void CrashAfterTheStampLeavesNothingToPayAgain()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True);

            LivingCharacter returned = Restart("char-a");
            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            second.RetryHeld();

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25),
                "a completed reward was paid again after a restart");
        }

        [Test]
        public void ASaveThatFailsLeavesTheRewardOwedAndPaysOnceWhenItComesBack()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            rewards.Grant(defeat, Of(hero));

            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "a delivery was stamped although nothing was saved");

            // Several attempts while the backend is down. The experience is applied in
            // memory once and no more, whatever happens to the save.
            Retry(rewards);
            Retry(rewards);

            LogAssert.ignoreFailingMessages = false;

            _store.Broken = false;

            Retry(rewards);

            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(25),
                "repeated attempts while the backend was down multiplied the experience");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True);
        }

        [Test]
        public void AnOwnerWhoIsNotHereKeepsTheRewardOwedUntilTheyAre()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            _store.Broken = false;

            // They log out. Nothing in the world can reach their pet.
            _players.Despawn(hero.ConnectionId);

            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            Retry(second);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "an offline owner's pet was marked paid");
            Assert.That(_outbox.Of(defeat).IsComplete, Is.False,
                "the reward completed while it still owed a pet");

            // And when they come back, exactly once. Recovery is attempted on every
            // admission, which is what a pending reward with nobody in the world waits
            // for: there is no session to read storage with until somebody arrives.
            LivingCharacter returned = Enter("char-a");

            second.RecoverPending();
            Retry(second);

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(25));
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True,
                "the pet was not paid when its owner came back");
        }

        [Test]
        public void APetThatIsNoLongerOwnedBlocksTheRewardRatherThanBeingRedirected()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            InstanceId defeat = Corpse(Worth100).Instance;

            Authority().Grant(defeat, Of(hero));

            _store.Broken = false;

            // They leave, and the character comes back owning a different pet entirely.
            // Seeded after the departure, because leaving writes the character out.
            _players.Despawn(hero.ConnectionId);

            Seed("char-a", Pets("pet-9"), null);

            LivingCharacter returned = Enter("char-a");
            MonsterRewardAuthority second = Authority();

            second.RecoverPending();
            Retry(second);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-9").Experience, Is.Zero,
                "the experience was redirected to whatever pet was there instead");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "a reward nobody could be paid was quietly completed");
            Assert.That(_outbox.Of(defeat).IsComplete, Is.False);
        }

        // ---- what pet rewards must not disturb -------------------------------------------------------

        [Test]
        public void TheCharactersOwnExperienceIsUnchangedByAnyOfThis()
        {
            LivingCharacter withPet = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");
            LivingCharacter without = AddPlayer("char-b");

            MonsterRewardAuthority rewards = Authority();

            MonsterRewardResult first = rewards.Grant(Corpse(Worth100).Instance,
                Of(withPet));

            Assert.That(first.IsGranted, Is.True, "the owner was refused: " + first);
            Assert.That(first.ExperienceGranted, Is.EqualTo(100));

            MonsterRewardResult second = rewards.Grant(Corpse(Worth100).Instance,
                Of(without));

            Assert.That(second.IsGranted, Is.True, "the owner was refused: " + second);
            Assert.That(second.ExperienceGranted, Is.EqualTo(100),
                "having no pet changed what its owner earned");

            // The authored curve is a hundred a level, so a hundred experience is one
            // level with nothing left over -- for both of them, pet or no pet.
            Assert.That(withPet.Domain.Progression.Level,
                Is.EqualTo(without.Domain.Progression.Level));
            Assert.That(withPet.Domain.Progression.Experience,
                Is.EqualTo(without.Domain.Progression.Experience),
                "having a pet changed what its owner earned");
        }

        [Test]
        public void ABossDefeatStillRollsEachRareChanceExactlyOnce()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            Authority().Grant(Corpse(Boss).Instance, Of(hero));

            Assert.That(_rolls.Chances.Count(c => Mathf.Abs(c - 0.0000001f) < 1e-12f),
                Is.EqualTo(1), "the one in ten million fruit chance was rolled more than once");
            Assert.That(_rolls.Chances.Count(c => Mathf.Abs(c - 0.000001f) < 1e-12f),
                Is.EqualTo(1), "the one in a million card chance was rolled more than once");

            int afterFirst = _rolls.Requests;

            // A retry pass consults nothing: the decision is already made.
            Authority().RetryHeld();

            Assert.That(_rolls.Requests, Is.EqualTo(afterFirst),
                "pet experience added a drop roll");
        }

        [Test]
        public void PayingAPetConsultsNoDropTableAtAll()
        {
            LivingCharacter hero = AddPlayer("char-a", Pets("pet-1"), active: "pet-1");

            MonsterRewardAuthority rewards = Authority();

            _store.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            rewards.Grant(Corpse(Worth100).Instance, Of(hero));

            int rolled = _rolls.Requests;

            _store.Broken = false;

            rewards.RetryHeld();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_rolls.Requests, Is.EqualTo(rolled),
                "delivering pet experience rolled a drop table");
        }

        // ---- shape ------------------------------------------------------------------------------------

        [Test]
        public void ThereIsOneRewardOutboxAndOnePetSystem()
        {
            System.Reflection.Assembly server = typeof(MonsterRewardAuthority).Assembly;

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

            foreach (System.Type type in server.GetTypes())
            {
                string name = type.Name.ToLowerInvariant();

                Assert.That(name.Contains("petreward") || name.Contains("petexpqueue"),
                    Is.False, type.FullName + " is a second pet reward system");
            }
        }

        [Test]
        public void NoPetIsNamedInTheRewardAuthority()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/MonsterRewardAuthority.cs");

            // A branch on a pet id would make the eleventh pet a code change. The authored
            // ids all begin this way, so any of them appearing here is that mistake.
            Assert.That(source.Contains("\"pet."), Is.False,
                "the reward authority names a specific pet");
        }

        [Test]
        public void AStoredPetGrantCarriesTheInstanceAndNoRuntimeState()
        {
            string[] fields = typeof(MonsterRewardPetGrant).GetProperties()
                .Select(p => p.Name.ToLowerInvariant()).ToArray();

            Assert.That(fields, Contains.Item("pet"));
            Assert.That(fields, Contains.Item("owner"));

            foreach (string forbidden in new[]
            {
                "definition", "position", "connection", "companion", "follower", "summoned",
                "level", "buff",
            })
            {
                Assert.That(fields, Has.No.Member(forbidden),
                    "a stored pet grant carries " + forbidden);
            }
        }

        // ---- harness -----------------------------------------------------------------------------------

        private WorldCharacterRegistry NewRegistry()
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            return new WorldCharacterRegistry(_store, spawns, Items, 8, null, _pets);
        }

        private MonsterRewardAuthority Authority(float share = Share)
        {
            var authority = new MonsterRewardAuthority(_runtime, _players, _curve, _loot,
                Items, DropTables, _rolls, _rolls, 300f, 0f, null, 0f, _outbox, _pets,
                share);

            _loot.Observe(authority);

            return authority;
        }

        /// <summary>
        /// Retries held rewards until one attempt actually happens.
        /// </summary>
        /// <remarks>The authority waits out a tick interval between attempts so a backend
        /// hiccup does not become a stalled world. A test that called it once would be
        /// testing the backoff rather than the retry, so this pumps past it.</remarks>
        private static void Retry(MonsterRewardAuthority rewards)
        {
            for (var i = 0; i < 320; i++) rewards.RetryHeld();
        }

        private static int AmountFor(int ownerAward, float share)
        {
            return (int)System.Math.Floor(ownerAward * (double)share);
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
            int experience = 0)
        {
            var rows = new List<PersistedPet>();

            for (var i = 0; pets != null && i < pets.Length; i++)
            {
                PersistedPet pet = pets[i];

                _pets.TryGet(pet.Pet, out PetDefinition definition);

                rows.Add(new PersistedPet(pet.Instance, pet.Pet,
                    PetService.LevelFor(definition, experience), experience, 0));
            }

            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
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
            string active = null, int experience = 0)
        {
            Seed(character, pets, active, experience);

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

        /// <summary>Puts a pet out, or puts away whatever is, through Phase 12.</summary>
        private void Summon(LivingCharacter owner, string instance)
        {
            var context = new PetService.Context(_pets, Items, null, owner.Status,
                owner.Owner);

            if (instance == null)
            {
                PetService.Dismiss(owner.Companion, context);

                return;
            }

            Assert.That(PetService.TrySummon(owner.Companion, Pet(owner, instance),
                context).IsAccepted, Is.True);
        }

        private LivingMonster Corpse(string monster)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));

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

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.pet-reward-test\"},\"_minLevel\":1,"
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

        /// <summary>Ten experience a level, to twenty. Fixture content, like every number here.</summary>
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
