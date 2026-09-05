// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world recovers.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A boss killed in one world, and paid for by the next one.
    /// </summary>
    /// <remarks>
    /// <b>The restart is a real one.</b> The scene is unloaded, the bootstrap destroyed and a
    /// fresh production world composed from the committed scene, with only the backend
    /// surviving -- which is exactly what happens when the dedicated server process is
    /// restarted. Nothing is put back by hand: everything the second world knows, it read.
    ///
    /// <b>The roll is the thing being protected.</b> A defeat spends a one in ten million
    /// chance, and no restart may buy another one. Every test here is ultimately about that
    /// number staying at one.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldRewardOutboxTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string DarknessItem = "item.devil_fruit.darkness";

        private const float Fraction = 0.0000001f;

        /// <summary>Storage that outlives the world, as a database does.</summary>
        private sealed class Outbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            public bool Broken { get; set; }

            /// <summary>Refuse only the delivery stamp, as a crash between the two writes
            /// would. Recording still works, so the decision stays durable.</summary>
            public bool RefuseProgress { get; set; }

            public int Records { get; private set; }

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

                _byDefeat[reward.Defeat.Value] = Copy(reward, reward.RewardId, 1,
                    reward.IsCursorCommitted, reward.IsLootPublished, false,
                    reward.Experience, reward.Entries);

                return MonsterRewardOutboxResult.Recorded(reward.RewardId, 1, false);
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
                if (Broken || RefuseProgress)
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
                                    entry.Quantity, entry.Rarity, true, taken.ClaimedBy);
                            }
                        }

                        entries.Add(updated);
                    }

                    _byDefeat[key] = Copy(stored, rewardId, revision + 1,
                        cursorCommitted ?? stored.IsCursorCommitted,
                        lootPublished ?? stored.IsLootPublished,
                        complete || stored.IsComplete, grants, entries);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward Copy(PersistedMonsterReward from,
                string rewardId, int revision, bool cursor, bool published, bool complete,
                IReadOnlyList<MonsterRewardGrant> grants,
                IReadOnlyList<MonsterRewardLootEntry> entries)
            {
                return new PersistedMonsterReward(rewardId, from.Defeat, from.Monster,
                    from.Map, from.Killer, from.Loot, from.LootPolicy, from.Claimant,
                    from.X, from.Y, from.Z, from.Party, from.Cursor, from.HasCursor,
                    new List<MonsterRewardGrant>(grants),
                    new List<MonsterRewardLootEntry>(entries),
                    cursor, published, complete, revision);
            }
        }

        private sealed class CharacterStore : ICharacterStateStore
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

            public int RareRolls => ChancesAsked.Count(c => Mathf.Abs(c - Fraction) < 1e-12f);

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

        // ---- A: nothing about an ordinary reward changes ---------------------------------

        [UnityTest]
        public IEnumerator AnOrdinaryBossRewardIsPaidAndPublishedAsItAlwaysWas()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            long before = hero.Domain.Progression.Experience;

            Kill(hero, Spawn());

            Assert.That(hero.Domain.Progression.Experience - before, Is.EqualTo(900),
                "the ordinary reward stopped being paid");

            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1));
            Assert.That(_rolls.RareRolls, Is.EqualTo(1));

            // And it was written down before any of that happened.
            Assert.That(_outbox.Records, Is.EqualTo(1),
                "the decision was never recorded");
        }

        // ---- B: nothing is handed over before the decision is durable --------------------

        [UnityTest]
        public IEnumerator NothingIsPaidOrPublishedWhileTheDecisionCannotBeWrittenDown()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            long before = hero.Domain.Progression.Experience;

            _outbox.Broken = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("holding the reward"));

            Kill(hero, Spawn());

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(before),
                "experience was paid for a decision that was never written down");

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "a pile reached the world before its decision was durable");

            Assert.That(_bootstrap.Rewards.HeldCount, Is.EqualTo(1),
                "the decision was thrown away instead of held");

            // The roll happened once and is being kept, not repeated.
            Assert.That(_rolls.RareRolls, Is.EqualTo(1));
        }

        // ---- C, D: a restart resumes the decision rather than making a new one -----------

        [UnityTest]
        public IEnumerator AFreshWorldFinishesTheDefeatTheLastOneDecidedAndNeverPaid()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            // Decided and written down, then the delivery is cut off.
            Kill(hero, Spawn());

            InstanceId defeat = _outbox.All()[0].Defeat;
            InstanceId decidedPile = _outbox.All()[0].Loot;

            Assert.That(decidedPile.IsValid, Is.True);

            int rollsBefore = _rolls.RareRolls;

            Assert.That(rollsBefore, Is.EqualTo(1));

            // The world stops with the pile still on the ground and unclaimed.
            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "a fresh world started with loot it had not been asked for");

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            // Recovered from storage, not decided again.
            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1),
                "the unfinished drop did not come back");

            Assert.That(_bootstrap.Loot.All()[0].LootId, Is.EqualTo(decidedPile),
                "recovery minted a new pile instead of restoring the decided one");

            Assert.That(_rolls.RareRolls, Is.Zero,
                "the restarted world rolled the rare chance again");

            Assert.That(_outbox.All().Count, Is.EqualTo(1),
                "the restart produced a second reward for one defeat");

            Assert.That(_outbox.Of(defeat).Exists, Is.True);

            // And the fruit is still the one that was decided.
            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(returned, pile);

            Assert.That(Pickup(returned, pile).IsAccepted, Is.True);
            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1),
                "the recovered drop was not the item that was rolled");
        }

        // ---- E: a roll that failed stays failed ------------------------------------------

        [UnityTest]
        public IEnumerator ARollThatFailedIsNotOfferedASecondChanceByARestart()
        {
            // roll: 1f means every chance is refused, including the rare one.
            yield return LoadWorld(roll: 1f);

            LivingCharacter hero = Admit("char-ann", 1);

            long before = hero.Domain.Progression.Experience;

            Kill(hero, Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1),
                "the boss did not roll its rare chance");

            Assert.That(Held(hero, DarknessItem), Is.Zero);

            // The decision -- including the fact that nothing dropped -- is written down.
            Assert.That(_outbox.All().Count, Is.EqualTo(1),
                "a defeat that dropped nothing was not recorded");

            Assert.That(_outbox.All()[0].Entries.Count, Is.Zero);

            Assert.That(hero.Domain.Progression.Experience - before, Is.EqualTo(900));

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            // The new world would succeed at any roll it made. It must make none.
            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_rolls.RareRolls, Is.Zero,
                "the restarted world bought a second chance at the fruit");

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "a defeat that dropped nothing produced loot after a restart");

            Assert.That(Held(returned, DarknessItem), Is.Zero);
        }

        // ---- F: experience crosses the crash window exactly once -------------------------

        [UnityTest]
        public IEnumerator ExperienceAlreadyPaidIsNotPaidAgainByTheWorldThatFollows()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            long earned = hero.Domain.Progression.Experience;
            int level = hero.Domain.Progression.Level;

            Assert.That(_outbox.All()[0].Experience[0].IsDelivered, Is.True,
                "the payment was made but never written down");

            // The world stops before the reward was ever marked finished: its pile is
            // still on the ground, so it comes back as unfinished work.
            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(level),
                "recovery paid the experience a second time");

            Assert.That(returned.Domain.Progression.Experience, Is.EqualTo(earned),
                "recovery paid the experience a second time");
        }

        // ---- G: an item already carried is not put back on the floor ---------------------

        [UnityTest]
        public IEnumerator AnItemAlreadyInABagIsNotRespawnedByARestart()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(hero, pile);

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True);
            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1));

            // Taken, and storage was told -- which is the whole point of the observer.
            Assert.That(_outbox.All()[0].Entries[0].IsClaimed, Is.True,
                "the pickup was never written down");

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "an item already in somebody's bag was put back on the floor");

            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1),
                "the fruit was duplicated or lost across the restart");

            Assert.That(_rolls.RareRolls, Is.Zero);
        }

        // ---- H: the round-robin turn is not spent twice ----------------------------------

        [UnityTest]
        public IEnumerator ARecoveredRewardDoesNotSpendTheRoundRobinTurnASecondTime()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            Kill(ann, Spawn());

            Assert.That(_bootstrap.Parties.RotationOf(party.Id), Is.EqualTo(1),
                "the turn was not spent");

            PartyId partyId = party.Id;

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            // The claimant was frozen at the defeat, so recovery has no turn left to spend.
            Assert.That(_bootstrap.Parties.RotationOf(partyId), Is.EqualTo(1),
                "the recovered reward spent the round-robin turn a second time");

            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1));
            Assert.That(_bootstrap.Loot.All()[0].EligibleCharacter,
                Is.EqualTo(ann.Character),
                "recovery re-derived the claimant instead of using the decided one");
        }

        // ---- I: the killer does not have to be there --------------------------------------

        [UnityTest]
        public IEnumerator ARewardOutlivesTheKillerLoggingOutAndIsFinishedByTheWorld()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);

            Kill(ann, Spawn());

            InstanceId decidedPile = _outbox.All()[0].Loot;

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            // Somebody else entirely walks in. The reward is the world's to finish.
            LivingCharacter other = Admit("char-ben", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1),
                "the reward waited for a killer who never came back");

            Assert.That(_bootstrap.Loot.All()[0].LootId, Is.EqualTo(decidedPile));

            // char-ann's share is still owed, and still theirs.
            PersistedMonsterReward stored = _outbox.All()[0];

            Assert.That(stored.Experience[0].Character.Value, Is.EqualTo("char-ann"));
            Assert.That(stored.Experience[0].IsDelivered, Is.True,
                "the first world had already paid this share");

            // And the pile is not char-ben's to take.
            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(other, pile);

            Assert.That(Pickup(other, pile).IsAccepted, Is.False,
                "a stranger took a recovered drop that was decided for somebody else");
        }

        // ---- J: the whole slice ------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheWholeDevilFruitSliceSurvivesAFailureAndARestart()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);

            // The decision is made, and cannot be written down.
            _outbox.Broken = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("holding the reward"));

            Kill(ann, Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1));
            Assert.That(_bootstrap.Loot.All().Count, Is.Zero);
            Assert.That(Held(ann, DarknessItem), Is.Zero);

            // The backend comes back and the world finishes what it decided.
            _outbox.Broken = false;

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1),
                "the held decision was never delivered");

            Assert.That(_rolls.RareRolls, Is.EqualTo(1),
                "recovery rolled the one in ten million chance again");

            InstanceId decidedPile = _bootstrap.Loot.All()[0].LootId;

            // And now the whole world restarts, before anybody picks it up.
            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1));
            Assert.That(_bootstrap.Loot.All()[0].LootId, Is.EqualTo(decidedPile),
                "the restart minted a different pile");

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(returned, pile);

            Assert.That(Pickup(returned, pile).IsAccepted, Is.True);
            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1));

            // Eaten, through the existing inventory action and nothing new.
            Assert.That(Consume(returned, DarknessItem), Is.True,
                "the recovered fruit could not be eaten");

            Assert.That(returned.DevilFruit.HasActiveFruit, Is.True,
                "eating the recovered fruit changed nothing");

            // And once more around: reconnect, and it is still eaten and still one fruit.
            //
            // Released first, because a reconnect is a logout and a login: that is the
            // lifecycle point the character is written back at, and skipping it would be
            // testing a crash rather than a reconnect.
            Assert.That(_bootstrap.Simulation.Release(returned.ConnectionId).IsOk, Is.True);

            yield return Tick();

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter again = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(again.DevilFruit.HasActiveFruit, Is.True,
                "the eaten fruit did not survive the reconnect");

            Assert.That(Held(again, DarknessItem), Is.Zero,
                "a second fruit appeared after the reconnect");

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "an item that was eaten was put back on the floor");

            Assert.That(_rolls.RareRolls, Is.Zero);
        }

        // ---- 18.15A: the window between a durable bag and a durable stamp ----------------

        [UnityTest]
        public IEnumerator ADroppedItemIsGivenItsIdentityBeforeAnybodyPicksItUp()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            PersistedMonsterReward stored = _outbox.All()[0];

            Assert.That(stored.Entries[0].Instance.IsValid, Is.True,
                "the drop was written down with no identity to deliver it by");

            LootObjectState pile = _bootstrap.Loot.All()[0];

            Assert.That(pile.Contents[0].Instance, Is.EqualTo(stored.Entries[0].Instance),
                "the pile and the record disagree about what the item will be");

            StandOn(hero, pile);

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True);

            Assert.That(hero.Inventory.IndexOf(stored.Entries[0].Instance),
                Is.GreaterThanOrEqualTo(0),
                "the item that arrived is not the one the reward decided on");
        }

        [UnityTest]
        public IEnumerator AnItemCarriedButNotStampedIsNotPutBackByTheNextWorld()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            InstanceId item = _outbox.All()[0].Entries[0].Instance;
            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(hero, pile);

            // The stamp fails, exactly as a crash between the two writes would leave it.
            _outbox.RefuseProgress = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not record progress"));

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True,
                "the pickup itself was refused");

            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1));

            // Durable ownership, no delivery stamp: the crash window.
            Assert.That(_outbox.All()[0].Entries[0].IsClaimed, Is.False,
                "the fixture did not actually produce the crash window");

            // The character is written back, then everything else is thrown away.
            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            _outbox.RefuseProgress = false;

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "an item already in the bag was put back on the floor");

            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1),
                "the fruit was duplicated or lost across the restart");

            Assert.That(returned.Inventory.IndexOf(item), Is.GreaterThanOrEqualTo(0),
                "the recovered item is not the one that was decided");

            // And the world worked out for itself that it had been delivered.
            Assert.That(_outbox.All()[0].Entries[0].IsClaimed, Is.True,
                "the delivery was never reconciled");

            Assert.That(_rolls.RareRolls, Is.Zero,
                "the restarted world rolled the rare chance again");
        }

        [UnityTest]
        public IEnumerator APickupRepeatedAfterTheStampFailedDoesNotProduceASecondItem()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(hero, pile);

            _outbox.RefuseProgress = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not record progress"));

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True);

            _outbox.RefuseProgress = false;

            // The same request again, and again with a fresh sequence. Neither may hand
            // over a second fruit.
            Pickup(hero, pile);
            Pickup(hero, pile);

            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1),
                "a replayed pickup produced a second fruit");
        }

        [UnityTest]
        public IEnumerator TheIdentityIsUnchangedByAnyNumberOfRestartsBeforePickup()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            InstanceId decided = _outbox.All()[0].Entries[0].Instance;

            for (var restart = 0; restart < 3; restart++)
            {
                yield return TearDownWorld();
                yield return LoadWorld(roll: 0f);

                Admit("char-ann", 1);

                yield return Tick();

                Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1),
                    "restart " + restart + " lost the pile");

                Assert.That(_bootstrap.Loot.All()[0].Contents[0].Instance,
                    Is.EqualTo(decided),
                    "the item identity changed on restart " + restart);
            }

            Assert.That(_outbox.All().Count, Is.EqualTo(1));
            Assert.That(_rolls.RareRolls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ARoundRobinDropKeepsItsClaimantAndItsIdentityAcrossTheCrashWindow()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            Kill(ann, Spawn());

            PartyId partyId = party.Id;
            InstanceId decided = _outbox.All()[0].Entries[0].Instance;

            Assert.That(_bootstrap.Parties.RotationOf(partyId), Is.EqualTo(1));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(ben, pile);

            Assert.That(Pickup(ben, pile).IsAccepted, Is.False,
                "the member the drop was not decided for took it");

            StandOn(ann, pile);

            _outbox.RefuseProgress = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not record progress"));

            Assert.That(Pickup(ann, pile).IsAccepted, Is.True);

            Assert.That(_bootstrap.Simulation.Release(ann.ConnectionId).IsOk, Is.True);

            _outbox.RefuseProgress = false;

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter back = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Parties.RotationOf(partyId), Is.EqualTo(1),
                "the recovered reward spent the round-robin turn a second time");

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "an item already carried was published again");

            Assert.That(Held(back, DarknessItem), Is.EqualTo(1));
            Assert.That(back.Inventory.IndexOf(decided), Is.GreaterThanOrEqualTo(0));
        }

        [UnityTest]
        public IEnumerator TheWholeDarknessSliceSurvivesTheStampCrashWindow()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1));

            InstanceId decided = _outbox.All()[0].Entries[0].Instance;
            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(hero, pile);

            _outbox.RefuseProgress = true;

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not record progress"));

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True);
            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1));

            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            _outbox.RefuseProgress = false;

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero,
                "the fruit was put back on the floor after being carried");

            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1),
                "there is not exactly one fruit");

            Assert.That(returned.Inventory.IndexOf(decided), Is.GreaterThanOrEqualTo(0));

            // Eaten through the ordinary request, and nothing new.
            Assert.That(Consume(returned, DarknessItem), Is.True,
                "the recovered fruit could not be eaten");

            Assert.That(returned.DevilFruit.HasActiveFruit, Is.True);

            Assert.That(_bootstrap.Simulation.Release(returned.ConnectionId).IsOk, Is.True);

            yield return Tick();
            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter again = Admit("char-ann", 1);

            yield return Tick();

            for (var i = 0; i < 4; i++) _bootstrap.Simulation.Tick(0.1f);

            Assert.That(again.DevilFruit.HasActiveFruit, Is.True,
                "the eaten fruit did not survive the reconnect");

            Assert.That(Held(again, DarknessItem), Is.Zero,
                "a second fruit appeared after the reconnect");

            Assert.That(again.Inventory.IndexOf(decided), Is.LessThan(0),
                "the consumed item came back");

            Assert.That(_bootstrap.Loot.All().Count, Is.Zero);
            Assert.That(_rolls.RareRolls, Is.Zero);
        }

        // ---- harness ----------------------------------------------------------------------

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

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private static SessionId Session(string character)
        {
            return new SessionId("session-" + character);
        }

        private PartyState Party(PartyLootPolicy policy, params LivingCharacter[] members)
        {
            var party = new PartyState(new PartyId("party-" + members[0].Character.Value),
                members[0].Character, policy);

            for (var i = 1; i < members.Length; i++) party.TryAdd(members[i].Character);

            Assert.That(_bootstrap.Parties.Register(party), Is.True);

            return party;
        }

        private LivingCharacter Admit(string character, int connection, int team = 1)
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
                    }, null, null, 1);
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

        private LootPickupOutcome Pickup(LivingCharacter taker, LootObjectState pile)
        {
            return _bootstrap.LootAuthority.Apply(taker.ConnectionId, pile.LootId.Value, 0,
                ++_sequence);
        }

        private static void StandOn(LivingCharacter character, LootObjectState pile)
        {
            character.Combatant.Position = new CombatPosition(pile.Position.X,
                pile.Position.Y, pile.Position.Z);
        }

        private static int Held(LivingCharacter character, string item)
        {
            if (character.Inventory == null) return 0;

            var count = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item)
                {
                    count += instance.Quantity;
                }
            }

            return count;
        }

        private static int Slot(LivingCharacter character, string item)
        {
            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item) return i;
            }

            return -1;
        }

        /// <summary>Eats it through the ordinary inventory request, and nothing new.</summary>
        private bool Consume(LivingCharacter character, string item)
        {
            int slot = Slot(character, item);

            if (slot < 0) return false;

            CharacterInventoryAuthority inventory = _bootstrap.Simulation.Inventory(
                _bootstrap.Replication);

            inventory.Submit(character.ConnectionId, InventoryAction.Use, slot, 0, 1,
                ++_sequence);

            return inventory.LastResult.IsAccepted;
        }
    }
}

#endif