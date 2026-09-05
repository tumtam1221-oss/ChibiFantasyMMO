// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world does this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Experience earned from real defeats in the world the shipped scene composed: whose
    /// pet gets it, whose character gets it, and how often.
    /// </summary>
    /// <remarks>
    /// <b>Real kills, not injected rewards.</b> Every defeat below is the production boss
    /// killed through the shipped combat pipeline; nothing calls the reward authority
    /// directly to hand a pet anything.
    ///
    /// <b>The two frozen facts are what every test circles.</b> Which pet was out, and how
    /// much it earned, are settled when the monster dies and read back exactly as stored --
    /// through a swap, a dismissal, a logout, and a whole new world process.
    ///
    /// <b>The backend is the one substitution.</b> Storage is a network service, and the
    /// shipped <c>Compose</c> already takes one, so a world can be stood up without HTTP.
    /// What it round-trips is the real persisted rows, pet progression and all.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldPetRewardTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string LumiSlime = "pet.lumi_slime";

        /// <summary>The two rare chances a boss defeat spends exactly once each.</summary>
        private const float FruitChance = 0.0000001f;
        private const float CardChance = 0.000001f;

        /// <summary>What the shipped bootstrap gives a pet, as a share of its owner's award.</summary>
        private const float Share = 0.25f;

        private sealed class CharacterStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            /// <summary>When set, every save is refused, as a backend outage would.</summary>
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
        }

        private sealed class Outbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            public bool Broken { get; set; }

            /// <summary>When set, deliveries land but nothing about them is stamped.</summary>
            public bool StampsRefused { get; set; }

            /// <summary>Reward ids whose stamps stay refused after the window closes.</summary>
            public readonly HashSet<string> Unstampable = new HashSet<string>();

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

                _byDefeat[reward.Defeat.Value] = Copy(reward, reward.RewardId, 1,
                    reward.Experience, reward.PetExperience, reward.Entries,
                    reward.IsCursorCommitted, reward.IsLootPublished, false);

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
                if (Broken || StampsRefused || Unstampable.Contains(rewardId))
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

                    var entries = new List<MonsterRewardLootEntry>();

                    foreach (MonsterRewardLootEntry entry in stored.Entries)
                    {
                        MonsterRewardLootEntry updated = entry;

                        if (!entry.IsClaimed && lootClaimed != null)
                        {
                            foreach (MonsterRewardLootEntry taken in lootClaimed)
                            {
                                if (taken.Index != entry.Index) continue;

                                updated = new MonsterRewardLootEntry(entry.Index, entry.Item,
                                    entry.Quantity, entry.Rarity, true, taken.ClaimedBy,
                                    entry.Instance);
                            }
                        }

                        entries.Add(updated);
                    }

                    _byDefeat[key] = Copy(stored, rewardId, revision + 1, grants, pets,
                        entries, cursorCommitted ?? stored.IsCursorCommitted,
                        lootPublished ?? stored.IsLootPublished,
                        complete || stored.IsComplete);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward Copy(PersistedMonsterReward from,
                string rewardId, int revision, IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets,
                IReadOnlyList<MonsterRewardLootEntry> entries,
                bool cursorCommitted, bool lootPublished, bool complete)
            {
                return new PersistedMonsterReward(rewardId, from.Defeat, from.Monster,
                    from.Map, from.Killer, from.Loot, from.LootPolicy, from.Claimant,
                    from.X, from.Y, from.Z, from.Party, from.Cursor, from.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(entries),
                    cursorCommitted, lootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        private sealed class PartyStore : IPartyStateStore
        {
            private readonly Dictionary<string, PersistedParty> _byCharacter =
                new Dictionary<string, PersistedParty>();

            public PartyPersistenceResult Load(SessionId session)
            {
                string character = session.Value.StartsWith("session-")
                    ? session.Value.Substring("session-".Length)
                    : session.Value;

                return _byCharacter.TryGetValue(character, out PersistedParty party)
                    ? PartyPersistenceResult.Loaded(party)
                    : PartyPersistenceResult.None();
            }

            public PartyPersistenceResult Save(SessionId session, PersistedParty party)
            {
                foreach (string key in _byCharacter.Keys.ToArray())
                {
                    if (_byCharacter[key].Party == party.Party) _byCharacter.Remove(key);
                }

                if (party.Members.Count == 0) return PartyPersistenceResult.Saved(0);

                var stored = new PersistedParty(party.Party, party.Leader, party.LootPolicy,
                    party.Members, party.Revision + 1, party.Cursor);

                foreach (CharacterId member in party.Members)
                {
                    _byCharacter[member.Value] = stored;
                }

                return PartyPersistenceResult.Saved(stored.Revision);
            }
        }

        private sealed class ScriptedRandom : IRandomResultSource, IRandomRangeSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public readonly List<float> ChancesAsked = new List<float>();

            public int Rolls(float chance)
            {
                return ChancesAsked.Count(c => Mathf.Abs(c - chance) < 1e-12f);
            }

            public bool Succeeds(float chance)
            {
                ChancesAsked.Add(chance);

                return _roll < chance;
            }

            public int Range(int min, int max) => min;
        }

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;

        // These outlive every world this fixture builds, exactly as a backend does.
        private CharacterStore _characters;
        private PartyStore _parties;
        private Outbox _outbox;
        private ScriptedRandom _rolls;

        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _parties = new PartyStore();
            _outbox = new Outbox();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return TearDownWorld();
        }

        // ---- A: a real defeat pays the pet that was out --------------------------------------

        [UnityTest]
        public IEnumerator ThePetThatWasOutEarnsFromARealDefeat()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero);

            Kill(hero, Spawn());

            yield return Tick();

            PersistedMonsterReward reward = _outbox.All()[0];

            Assert.That(reward.PetExperience.Count, Is.EqualTo(1),
                "the shipped world decided nothing for the pet that was out");

            MonsterRewardPetGrant grant = reward.PetExperience[0];

            Assert.That(grant.Pet, Is.EqualTo(new InstanceId("pet-1")));
            Assert.That(grant.Owner, Is.EqualTo(hero.Character));

            // A quarter of what its owner was awarded, floored -- from the shipped policy
            // rather than a number this test knows.
            Assert.That(grant.Experience,
                Is.EqualTo(Mathf.FloorToInt(reward.Experience[0].Experience * Share)));

            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(grant.Experience),
                "the decided experience never reached the pet");
            Assert.That(grant.IsDelivered, Is.True);
            Assert.That(reward.IsComplete, Is.True);
        }

        // ---- B: no pet out, nothing owed ---------------------------------------------------------

        [UnityTest]
        public IEnumerator ACharacterWithNoPetOutIsOwedNothingAndTheRewardStillFinishes()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"));

            Kill(hero, Spawn());

            yield return Tick();

            PersistedMonsterReward reward = _outbox.All()[0];

            Assert.That(reward.PetExperience.Count, Is.Zero,
                "a phantom row was written for a character with no pet out");
            Assert.That(reward.IsComplete, Is.True,
                "the reward could not finish because of a delivery nobody is owed");
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero);
        }

        // ---- C: switching afterwards changes nothing ------------------------------------------------

        [UnityTest]
        public IEnumerator SwitchingPetsAfterTheDefeatCannotRedirectTheExperience()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1", "pet-2"),
                active: "pet-1");

            // Nothing can be delivered: storage is down, so the decision is durable and
            // none of its effects are.
            _characters.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            Kill(hero, Spawn());

            yield return Tick();

            _characters.Broken = false;

            // A new world. The player puts their other pet out before recovery runs.
            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            Assert.That(Pet(returned, "pet-1").Experience, Is.Zero,
                "precondition: nothing was durable");

            Activate(returned, "pet-2");

            _bootstrap.Rewards.RecoverPending();

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            int owed = _outbox.All()[0].PetExperience[0].Experience;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(owed),
                "the pet that earned it was not the pet that got it");
            Assert.That(Pet(returned, "pet-2").Experience, Is.Zero,
                "the experience followed the pet the player has out now");
        }

        // ---- D: the owner logs out before delivery ----------------------------------------------------

        [UnityTest]
        public IEnumerator AnOwnerWhoLogsOutBeforeDeliveryIsPaidExactlyOnceOnReturn()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            _characters.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            Kill(hero, Spawn());

            yield return Tick();

            _characters.Broken = false;

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            int owed = _outbox.All()[0].PetExperience[0].Experience;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(owed));
            Assert.That(_outbox.All()[0].PetExperience[0].IsDelivered, Is.True);

            // And a second pass pays nothing more.
            yield return Retry();

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(owed),
                "a second recovery pass paid the pet twice");
        }

        // ---- E: the whole world restarts before delivery -------------------------------------------------

        [UnityTest]
        public IEnumerator AFreshWorldPaysThePetTheLastOneDecidedForAndNeverPaid()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            _characters.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            Kill(hero, Spawn());

            yield return Tick();

            _characters.Broken = false;

            InstanceId defeat = _outbox.All()[0].Defeat;
            int owed = _outbox.Of(defeat).PetExperience[0].Experience;

            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False);

            // A real restart: the scene is unloaded and the bootstrap destroyed.
            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(owed),
                "a restart lost the experience the pet had earned");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True);
            Assert.That(_outbox.Of(defeat).IsComplete, Is.True);
        }

        // ---- F: the crash window between the save and the stamp -------------------------------------------

        [UnityTest]
        public IEnumerator ARestartAfterThePetIsSavedButBeforeTheStampPaysNothingAgain()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            // Deliveries land; nothing about them is ever stamped.
            _outbox.StampsRefused = true;

            LogAssert.ignoreFailingMessages = true;

            Kill(hero, Spawn());

            yield return Tick();

            InstanceId defeat = _outbox.All()[0].Defeat;
            int owed = _outbox.Of(defeat).PetExperience[0].Experience;

            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(owed),
                "precondition: the pet was paid");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.False,
                "precondition: the stamp never landed");

            _outbox.StampsRefused = false;

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            Assert.That(Pet(returned, "pet-1").AppliedRewardId,
                Is.EqualTo(_outbox.Of(defeat).RewardId),
                "the pet does not record which reward its experience already includes");

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(owed),
                "recovery paid the pet a second time");
            Assert.That(_outbox.Of(defeat).PetExperience[0].IsDelivered, Is.True,
                "recovery did not reconcile the delivery it found");
        }

        // ---- G: six players, each with their own pet state ---------------------------------------------------

        [UnityTest]
        public IEnumerator EveryPartyMemberWithAPetOutIsPaidForTheirOwnPet()
        {
            yield return LoadWorld();

            var members = new List<LivingCharacter>();

            for (var i = 0; i < 6; i++)
            {
                string id = "char-" + (char)('a' + i);

                // Half of them have a pet out; one owns a pet and has it away; one owns
                // none at all.
                bool hasPet = i % 2 == 0;

                members.Add(Admit(id, i + 1,
                    hasPet || i == 1 ? Pets("pet-" + id) : null,
                    active: hasPet ? "pet-" + id : null));
            }

            PartyState party = Party(PartyLootPolicy.RoundRobin, members.ToArray());

            Assert.That(party.Members.Count, Is.EqualTo(6));

            Kill(members[0], Spawn());

            yield return Tick();

            PersistedMonsterReward reward = _outbox.All()[0];

            Assert.That(reward.Experience.Count, Is.EqualTo(6),
                "the character split changed");

            var owed = new Dictionary<string, MonsterRewardPetGrant>();

            foreach (MonsterRewardPetGrant grant in reward.PetExperience)
            {
                owed[grant.Owner.Value] = grant;
            }

            Assert.That(owed.Count, Is.EqualTo(3),
                "a pet reward was decided for somebody with no pet out");

            var byCharacter = new Dictionary<string, int>();

            foreach (MonsterRewardGrant grant in reward.Experience)
            {
                byCharacter[grant.Character.Value] = grant.Experience;
            }

            for (var i = 0; i < 6; i++)
            {
                LivingCharacter member = members[i];
                string id = member.Character.Value;

                if (i % 2 != 0)
                {
                    Assert.That(owed.ContainsKey(id), Is.False,
                        id + " had no pet out and was owed one anyway");

                    continue;
                }

                Assert.That(owed[id].Pet, Is.EqualTo(new InstanceId("pet-" + id)),
                    "a member was paid for somebody else's pet");

                // From their own share of the split, not from the monster's total.
                Assert.That(owed[id].Experience,
                    Is.EqualTo(Mathf.FloorToInt(byCharacter[id] * Share)));

                Assert.That(Pet(member, "pet-" + id).Experience,
                    Is.EqualTo(owed[id].Experience));
            }

            // The one who owns a pet but had it away earned nothing for it.
            Assert.That(Pet(members[1], "pet-" + members[1].Character.Value).Experience,
                Is.Zero, "a pet that was not out was paid");
        }

        // ---- H: two of the same kind -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TwoPetsOfOneKindAreNotInterchangeable()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1", "pet-2"),
                active: "pet-2");

            Assert.That(Pet(hero, "pet-1").DefinitionId,
                Is.EqualTo(Pet(hero, "pet-2").DefinitionId),
                "precondition: both pets are the same authored kind");

            Kill(hero, Spawn());

            yield return Tick();

            int owed = _outbox.All()[0].PetExperience[0].Experience;

            Assert.That(_outbox.All()[0].PetExperience[0].Pet,
                Is.EqualTo(new InstanceId("pet-2")));
            Assert.That(Pet(hero, "pet-2").Experience, Is.EqualTo(owed));
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero,
                "the other copy of the same kind was paid");
        }

        // ---- I: the decision must be durable first --------------------------------------------------------------

        [UnityTest]
        public IEnumerator NoPetIsPaidUntilTheDecisionIsWrittenDown()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            _outbox.Broken = true;

            LogAssert.ignoreFailingMessages = true;

            Kill(hero, Spawn());

            yield return Tick();

            Assert.That(_outbox.All().Count, Is.Zero, "precondition: nothing was recorded");
            Assert.That(Pet(hero, "pet-1").Experience, Is.Zero,
                "a pet was paid before the decision was durable");
            Assert.That(Pet(hero, "pet-1").Level, Is.EqualTo(1));

            _outbox.Broken = false;

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_outbox.All().Count, Is.EqualTo(1));

            MonsterRewardPetGrant grant = _outbox.All()[0].PetExperience[0];

            Assert.That(grant.Pet, Is.EqualTo(new InstanceId("pet-1")),
                "the retry chose a recipient rather than resuming the decision");
            Assert.That(Pet(hero, "pet-1").Experience, Is.EqualTo(grant.Experience));
        }

        // ---- J: the rare rolls are untouched -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ABossDefeatStillSpendsEachRareChanceExactlyOnce()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            Kill(hero, Spawn());

            yield return Tick();

            Assert.That(_rolls.Rolls(FruitChance), Is.EqualTo(1),
                "the one in ten million fruit chance was not rolled exactly once");
            Assert.That(_rolls.Rolls(CardChance), Is.EqualTo(1),
                "the one in a million card chance was not rolled exactly once");

            int asked = _rolls.ChancesAsked.Count;

            yield return Retry();

            Assert.That(_rolls.ChancesAsked.Count, Is.EqualTo(asked),
                "paying pet experience consulted a drop table");
        }

        [UnityTest]
        public IEnumerator SixPlayersDoNotMultiplyTheRareRolls()
        {
            yield return LoadWorld();

            var members = new List<LivingCharacter>();

            for (var i = 0; i < 6; i++)
            {
                string id = "char-" + (char)('a' + i);

                members.Add(Admit(id, i + 1, Pets("pet-" + id), active: "pet-" + id));
            }

            Party(PartyLootPolicy.RoundRobin, members.ToArray());

            Kill(members[0], Spawn());

            yield return Tick();

            Assert.That(_rolls.Rolls(FruitChance), Is.EqualTo(1),
                "a six-player party rolled the fruit chance more than once");
            Assert.That(_rolls.Rolls(CardChance), Is.EqualTo(1),
                "a six-player party rolled the card chance more than once");
        }

        // ---- K: a character's own experience, saved but never stamped ----------------------------

        [UnityTest]
        public IEnumerator ACharactersExperienceIsNotPaidTwiceAfterALostStamp()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1);

            LogAssert.ignoreFailingMessages = true;

            string reward = KillUnstamped(hero);

            long experience = hero.Domain.Progression.Experience;
            int level = hero.Domain.Progression.Level;

            Assert.That(_outbox.All()[0].Experience[0].IsDelivered, Is.False,
                "precondition: the stamp never landed");
            Assert.That(level, Is.GreaterThan(1), "precondition: they were paid");

            // The world dies; the backend that lost the stamp comes back.
            _outbox.Unstampable.Clear();

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            Assert.That(returned.HasAppliedReward(reward), Is.True,
                "the character carries no evidence of what it was already paid");

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level),
                "the character was paid a second time");
            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(experience));
            Assert.That(_outbox.All()[0].Experience[0].IsDelivered, Is.True,
                "the reward was never reconciled");
        }

        // ---- L: one reward unstamped, the next one normal, then a restart ---------------------------

        [UnityTest]
        public IEnumerator TwoOverlappingRewardsAreEachAppliedExactlyOnce()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            LogAssert.ignoreFailingMessages = true;

            // R1 lands but is never stamped. R2 then lands normally -- which is what would
            // overwrite a single "last reward" marker on the pet.
            KillUnstamped(hero);

            Kill(hero, Spawn());

            yield return Tick();

            long experience = hero.Domain.Progression.Experience;
            int level = hero.Domain.Progression.Level;
            int pet = Pet(hero, "pet-1").Experience;

            Assert.That(_outbox.All().Count, Is.EqualTo(2), "precondition: two rewards");

            _outbox.Unstampable.Clear();

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(pet),
                "precondition: both pet payments were durable");

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level),
                "the character was paid one of the two rewards twice");
            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(experience));
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(pet),
                "the pet was paid one of the two rewards twice");

            foreach (PersistedMonsterReward stored in _outbox.All())
            {
                Assert.That(stored.IsComplete, Is.True,
                    "reward " + stored.RewardId + " never finished");
            }
        }

        // ---- M: two rewards, two different pets ---------------------------------------------------------

        [UnityTest]
        public IEnumerator EachRewardStaysWithThePetThatWasOutForIt()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1", "pet-2"),
                active: "pet-1");

            LogAssert.ignoreFailingMessages = true;

            KillUnstamped(hero);

            Activate(hero, "pet-2");

            Kill(hero, Spawn());

            yield return Tick();

            int first = Pet(hero, "pet-1").Experience;
            int second = Pet(hero, "pet-2").Experience;

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.GreaterThan(0));

            _outbox.Unstampable.Clear();

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(first),
                "the first pet was paid again for a reward it already had");
            Assert.That(Pet(returned, "pet-2").Experience, Is.EqualTo(second),
                "the second pet was paid again for a reward it already had");
        }

        // ---- N: three rapid defeats, failures at different points -------------------------------------------

        [UnityTest]
        public IEnumerator ThreeRapidDefeatsEachPayOnceAcrossARestart()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            LogAssert.ignoreFailingMessages = true;

            // One with a lost stamp, one with the character store down, one ordinary.
            KillUnstamped(hero);

            _characters.Broken = true;
            Kill(hero, Spawn());
            yield return Tick();
            _characters.Broken = false;

            Kill(hero, Spawn());

            yield return Retry();

            _outbox.Unstampable.Clear();

            yield return Retry();

            long experience = hero.Domain.Progression.Experience;
            int level = hero.Domain.Progression.Level;
            int pet = Pet(hero, "pet-1").Experience;

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(_outbox.All().Count, Is.EqualTo(3), "three defeats, three rewards");

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level),
                "a restart paid one of the three again");
            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(experience));
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(pet));

            foreach (PersistedMonsterReward stored in _outbox.All())
            {
                Assert.That(stored.IsComplete, Is.True,
                    "reward " + stored.RewardId + " never finished");
            }
        }

        // ---- O: two recoveries racing for the same reward ----------------------------------------------------

        [UnityTest]
        public IEnumerator ASecondRecoveryPassPaysNothingTheFirstAlreadyDid()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, Pets("pet-1"), active: "pet-1");

            LogAssert.ignoreFailingMessages = true;

            KillUnstamped(hero);

            _outbox.Unstampable.Clear();

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Retry();

            long experience = returned.Domain.Progression.Experience;
            int level = returned.Domain.Progression.Level;
            int pet = Pet(returned, "pet-1").Experience;

            // A second worker asking storage for the same work, and a third pass over it.
            _bootstrap.Rewards.RecoverPending();

            yield return Retry();
            yield return Retry();

            LogAssert.ignoreFailingMessages = false;

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level),
                "a second recovery pass paid the character again");
            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(experience));
            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(pet),
                "a second recovery pass paid the pet again");
        }

        // ---- P: a party still splits as it did ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APartySplitAndItsRotationSurviveTheLedger()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1, Pets("pet-ann"), active: "pet-ann");
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);

            Assert.That(_bootstrap.Parties.Persist(new SessionId("session-char-ann"), party,
                _parties).IsOk, Is.True);

            Kill(ann, Spawn());

            yield return Tick();

            PersistedMonsterReward reward = _outbox.All()[0];

            Assert.That(reward.Experience.Count, Is.EqualTo(2), "the split changed");

            long total = 0;

            foreach (MonsterRewardGrant grant in reward.Experience)
            {
                total += grant.Experience;

                Assert.That(grant.IsDelivered, Is.True, grant.Character + " was not paid");
            }

            Assert.That(total, Is.GreaterThan(0));

            // The rotation this defeat spent is recorded, exactly as it was before.
            Assert.That(reward.HasCursor, Is.True, "the party turn was not decided");
            Assert.That(reward.IsCursorCommitted, Is.True,
                "the party turn was never committed");
        }

        // ---- harness ---------------------------------------------------------------------------------------------

        private IEnumerator LoadWorld(float roll = 1f)
        {
            _rolls = new ScriptedRandom(roll);

            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();
            _bootstrap.UseRandom(_rolls, _rolls);

            // The backend survives; the world does not. That is what a restart is.
            _bootstrap.Compose(new AlwaysAdmits(), default, _characters, null, _parties,
                _outbox);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);
            Assert.That(_bootstrap.RewardOutbox, Is.Not.Null,
                "the world composed no reward outbox");

            yield return null;
        }

        private IEnumerator TearDownWorld()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
                _bootstrap = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private static IEnumerator Tick(int frames = 2)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        /// <summary>
        /// Pumps retries past the authority's own backoff.
        /// </summary>
        /// <remarks>The world waits a tick interval between attempts so a backend hiccup
        /// does not become a stalled world; a test that waited it out in real frames would
        /// take minutes.</remarks>
        private IEnumerator Retry()
        {
            for (var i = 0; i < 320; i++) _bootstrap.Rewards.RetryHeld();

            yield return null;
        }

        /// <summary>
        /// Kills the boss and loses the delivery stamp for the reward it produced.
        /// </summary>
        /// <remarks>Everything else lands: the decision is recorded, the experience is
        /// applied, and the character is saved. Only the stamp is lost, which is the crash
        /// window these tests stand in. The reward stays unstampable afterwards so later
        /// retries cannot quietly finish it.</remarks>
        /// <returns>The reward's own durable id.</returns>
        private string KillUnstamped(LivingCharacter hero)
        {
            int before = _outbox.All().Count;

            _outbox.StampsRefused = true;

            Kill(hero, Spawn());

            _outbox.StampsRefused = false;

            IReadOnlyList<PersistedMonsterReward> all = _outbox.All();

            Assert.That(all.Count, Is.EqualTo(before + 1), "the defeat was not recorded");

            string rewardId = all[all.Count - 1].RewardId;

            _outbox.Unstampable.Add(rewardId);

            return rewardId;
        }

        private static PersistedPet[] Pets(params string[] instances)
        {
            if (instances == null) return null;

            var rows = new PersistedPet[instances.Length];

            for (var i = 0; i < instances.Length; i++)
            {
                rows[i] = new PersistedPet(new InstanceId(instances[i]),
                    new DefinitionId(LumiSlime), 1, 0, 0);
            }

            return rows;
        }

        private LivingCharacter Admit(string character, int connection,
            PersistedPet[] pets = null, string active = null, int team = 1)
        {
            string session = "session-" + character;

            if (!_characters.Rows.ContainsKey(session))
            {
                _characters.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 30, 0, 104, 35,
                    new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1, null, 0, default, null, pets,
                    active == null ? default : new InstanceId(active));
            }

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            // Admission through the simulation does not run the bootstrap's own admitted
            // handler, so what it does on arrival is done here in the same order.
            if (_bootstrap.Parties != null && _bootstrap.PartyStore != null)
            {
                _bootstrap.Parties.Restore(new SessionId(session),
                    new CharacterId(character), _bootstrap.PartyStore);
            }

            if (_bootstrap.Rewards != null && _bootstrap.RewardOutbox != null)
            {
                _bootstrap.Rewards.RecoverPending();
            }

            return spawned.Character;
        }

        private PartyState Party(PartyLootPolicy policy, params LivingCharacter[] members)
        {
            var party = new PartyState(new PartyId("party-" + members[0].Character.Value),
                members[0].Character, policy);

            for (var i = 1; i < members.Length; i++) party.TryAdd(members[i].Character);

            Assert.That(_bootstrap.Parties.Register(party), Is.True);

            return party;
        }

        /// <summary>Puts a pet out through the shipped authority, as a client's request does.</summary>
        private void Activate(LivingCharacter owner, string pet)
        {
            ChibiFantasy.Network.ICharacterPetRequestSink sink = _bootstrap.PetAuthority;

            sink.Activate(owner.ConnectionId, new InstanceId(pet));

            Assert.That(_bootstrap.PetAuthority.LastResult.IsAccepted, Is.True,
                "the shipped world refused to put out " + pet);
        }

        private static PetInstance Pet(LivingCharacter owner, string instance)
        {
            Assert.That(owner.TryGetPet(new InstanceId(instance), out PetInstance pet),
                Is.True, owner.Character + " does not own " + instance);

            return pet;
        }

        private LivingMonster Spawn()
        {
            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss), default, 0f,
                1, 0f, new DefinitionId(StarterMap)));

            monsters.PopulateAll();

            IReadOnlyList<LivingMonster> alive = monsters.All();

            for (var i = alive.Count - 1; i >= 0; i--)
            {
                if (alive[i].State.Definition.Id.Value == Boss && alive[i].IsAlive)
                {
                    return alive[i];
                }
            }

            Assert.Fail("no living boss");

            return null;
        }

        private void Kill(LivingCharacter hero, LivingMonster monster)
        {
            _bootstrap.Simulation.Monsters().TryResolve(monster.Instance,
                out ICombatant target);

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                _bootstrap.Simulation.Combat().Tick(10f);

                ServerCombatResult result = _bootstrap.Simulation.Combat().Execute(
                    hero.ConnectionId, new CombatCommand(hero.Character, monster.Instance,
                        default, 0, ++_sequence));

                if (!result.IsAccepted) break;
            }

            Assert.That(target.CurrentHealth, Is.Zero, "the boss would not die");
        }
    }
}

#endif
