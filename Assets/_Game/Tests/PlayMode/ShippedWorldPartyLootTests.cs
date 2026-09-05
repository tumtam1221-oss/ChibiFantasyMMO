// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world shares this.
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
using FishNet.Managing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A party killing the world boss in the shipped world.
    /// </summary>
    /// <remarks>
    /// <b>The rate is never touched, and neither is the number of rolls.</b> Six players do
    /// not make a Devil Fruit six times likelier: one defeat is one roll at one in ten
    /// million, and a test that quietly rolled per member would be describing a different
    /// game. That count is asserted directly.
    ///
    /// <b>Everything is decided at the defeat.</b> Parties are joined, left and disbanded
    /// after the boss dies in several tests below, and none of it may change who the drop
    /// already belongs to.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldPartyLootTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string DarknessItem = "item.devil_fruit.darkness";
        private const string Darkness = "devil_fruit.darkness";

        private const float Fraction = 0.0000001f;

        /// <summary>A roll a test chooses, counting how often it was asked.</summary>
        private sealed class ScriptedRandom : IRandomResultSource, IRandomRangeSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public readonly List<float> ChancesAsked = new List<float>();

            public int RareRolls => ChancesAsked.Count(c => Mathf.Abs(c - Fraction) < 1e-12f);

            public bool Succeeds(float successChance)
            {
                ChancesAsked.Add(successChance);

                return _roll < successChance;
            }

            public int Range(int minInclusive, int maxInclusive) => minInclusive;
        }

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

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private FakeStore _store;
        private ScriptedRandom _rolls;
        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        // ---- A: solo is unchanged ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASoloKillStillPaysTheWholeRewardAndReservesTheDropToTheKiller()
        {
            yield return LoadWorld(0f);

            LivingCharacter hero = Admit("char-a", 1);
            LivingCharacter stranger = Admit("char-b", 2, team: 2);

            long before = hero.Domain.Progression.Experience;

            Kill(hero, Spawn());

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(before + 900),
                "a solo kill no longer pays the whole reward");

            LootObjectState pile = Pile();

            StandOn(stranger, pile);

            Assert.That(Pickup(stranger, pile).IsAccepted, Is.False,
                "a passer-by took a solo player's drop");

            StandOn(hero, pile);

            Assert.That(Pickup(hero, pile).IsAccepted, Is.True,
                "the killer could not take their own drop");
        }

        // ---- B: the split ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APartyDividesTheBossRewardAndSkipsWhoeverWasNotThere()
        {
            yield return LoadWorld(1f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter ben = Admit("char-b", 2);
            LivingCharacter cal = Admit("char-c", 3);

            // Cal is on the far side of the map when it dies.
            Move(cal, 500f, 0f, 500f);

            Party(PartyLootPolicy.Personal, ann, ben, cal);

            long annBefore = ann.Domain.Progression.Experience;
            long benBefore = ben.Domain.Progression.Experience;
            long calBefore = cal.Domain.Progression.Experience;

            Kill(ann, Spawn());

            // 900 between the two who were there: 450 each, and nothing for the absentee.
            Assert.That(ann.Domain.Progression.Experience - annBefore, Is.EqualTo(450));
            Assert.That(ben.Domain.Progression.Experience - benBefore, Is.EqualTo(450));
            Assert.That(cal.Domain.Progression.Experience, Is.EqualTo(calBefore),
                "a member across the map was paid for a fight they were not in");

            // And walking over afterwards pays nothing retroactively.
            Move(cal, 0f, 0f, 0f);

            yield return Tick();

            Assert.That(cal.Domain.Progression.Experience, Is.EqualTo(calBefore));
        }

        [UnityTest]
        public IEnumerator ASixPlayerPartyDividesTheRewardWithNothingLost()
        {
            yield return LoadWorld(1f);

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            Party(PartyLootPolicy.Personal, members);

            long[] before = members.Select(m => m.Domain.Progression.Experience).ToArray();

            Kill(members[0], Spawn());

            long paid = members.Select((m, i) => m.Domain.Progression.Experience - before[i])
                .Sum();

            Assert.That(paid, Is.EqualTo(900),
                "the party was paid " + paid + " for a 900 experience boss");

            foreach (LivingCharacter member in members)
            {
                Assert.That(member.Domain.Progression.Experience,
                    Is.GreaterThan(before[System.Array.IndexOf(members, member)]),
                    member.Character + " was in the party and paid nothing");
            }
        }

        // ---- C: one roll -----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SixPlayersGetExactlyOneRareRollNotSix()
        {
            yield return LoadWorld(1f);

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            Party(PartyLootPolicy.Personal, members);

            Kill(members[0], Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1),
                "a six-player party rolled the Devil Fruit " + _rolls.RareRolls
                + " times; party size multiplied the odds");

            // And the chance asked about is still the authored one.
            Assert.That(_rolls.ChancesAsked.Any(c => Mathf.Abs(c - Fraction) < 1e-12f),
                Is.True, "the roll was made against a chance that is not one in ten million");
        }

        [UnityTest]
        public IEnumerator ASoloKillAlsoGetsExactlyOneRareRoll()
        {
            yield return LoadWorld(1f);

            Kill(Admit("char-a", 1), Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1));
        }

        // ---- D and E: who may claim ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RoundRobinGivesTheDropToTheMemberWhoseTurnItIsAndNobodyElse()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter ben = Admit("char-b", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);

            // Turn zero is the leader's.
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 0),
                Is.EqualTo(ann.Character));

            Kill(ann, Spawn());

            LootObjectState first = Pile();

            StandOn(ann, first);
            StandOn(ben, first);

            Assert.That(Pickup(ben, first).IsAccepted, Is.False,
                "a member took a drop on somebody else's turn");
            Assert.That(Pickup(ann, first).IsAccepted, Is.True,
                "the member whose turn it was could not claim");

            // The turn moved on, so the next boss belongs to Ben.
            Assert.That(_bootstrap.Parties.RotationOf(party.Id), Is.EqualTo(1));

            Kill(ann, Spawn());

            LootObjectState second = Pile();

            StandOn(ann, second);
            StandOn(ben, second);

            Assert.That(Pickup(ann, second).IsAccepted, Is.False,
                "the same member claimed twice in a row under round robin");
            Assert.That(Pickup(ben, second).IsAccepted, Is.True,
                "the turn did not advance to the next member");
        }

        [UnityTest]
        public IEnumerator AStrangerStandingOnPartyLootCanNeverTakeIt()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter ben = Admit("char-b", 2);
            LivingCharacter stranger = Admit("char-x", 3, team: 2);

            Party(PartyLootPolicy.Personal, ann, ben);

            Kill(ann, Spawn());

            LootObjectState pile = Pile();

            StandOn(stranger, pile);

            Assert.That(Pickup(stranger, pile).IsAccepted, Is.False,
                "a non-member took party loot");

            Assert.That(Held(stranger, DarknessItem), Is.Zero);

            StandOn(ann, pile);

            Assert.That(Pickup(ann, pile).IsAccepted, Is.True);
        }

        // ---- G: the party changes afterwards ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator JoiningAfterTheBossDiesEarnsNothingAndClaimsNothing()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter latecomer = Admit("char-b", 2);

            PartyState party = Party(PartyLootPolicy.Personal, ann);

            long before = latecomer.Domain.Progression.Experience;

            Kill(ann, Spawn());

            Assert.That(latecomer.Domain.Progression.Experience, Is.EqualTo(before));

            // Now they join. The drop is still not theirs.
            party.TryAdd(latecomer.Character);
            _bootstrap.Parties.Register(party);

            LootObjectState pile = Pile();
            StandOn(latecomer, pile);

            Assert.That(Pickup(latecomer, pile).IsAccepted, Is.False,
                "joining the party after the kill made somebody else's drop theirs");

            Assert.That(latecomer.Domain.Progression.Experience, Is.EqualTo(before),
                "joining after the kill paid experience");
        }

        [UnityTest]
        public IEnumerator LeavingOrDisbandingAfterTheKillDoesNotOrphanTheDrop()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter ben = Admit("char-b", 2);

            PartyState party = Party(PartyLootPolicy.Personal, ann, ben);

            Kill(ann, Spawn());

            LootObjectState pile = Pile();

            // The whole party disbands before anybody picks anything up.
            Assert.That(_bootstrap.Parties.Forget(party.Id), Is.True);

            StandOn(ben, pile);

            Assert.That(Pickup(ben, pile).IsAccepted, Is.False,
                "disbanding the party made the drop anybody's");

            StandOn(ann, pile);

            Assert.That(Pickup(ann, pile).IsAccepted, Is.True,
                "the drop was orphaned when the party disappeared");

            Assert.That(Held(ann, DarknessItem), Is.EqualTo(1));
        }

        // ---- H: two parties ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnotherPartyNearbyGetsNeitherExperienceNorTheDrop()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);
            LivingCharacter ben = Admit("char-b", 2);
            LivingCharacter rivalOne = Admit("char-x", 3);
            LivingCharacter rivalTwo = Admit("char-y", 4);

            Party(PartyLootPolicy.Personal, ann, ben);
            Party(PartyLootPolicy.Personal, rivalOne, rivalTwo);

            long rivalBefore = rivalOne.Domain.Progression.Experience;

            Kill(ann, Spawn());

            Assert.That(rivalOne.Domain.Progression.Experience, Is.EqualTo(rivalBefore),
                "a rival party was paid for somebody else's kill");

            LootObjectState pile = Pile();

            StandOn(rivalOne, pile);

            Assert.That(Pickup(rivalOne, pile).IsAccepted, Is.False,
                "a rival party took the drop");
        }

        // ---- I: the one-fruit rule ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AFruitOwnerMayStillTakeASecondOneButNotEatIt()
        {
            yield return LoadWorld(0f);

            LivingCharacter ann = Admit("char-a", 1);

            Kill(ann, Spawn());

            LootObjectState first = Pile();
            StandOn(ann, first);

            Assert.That(Pickup(ann, first).IsAccepted, Is.True);

            Assert.That(Use(ann, Slot(ann, DarknessItem)).IsAccepted, Is.True);
            Assert.That(ann.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));

            // A second boss. Taking it is ordinary inventory; eating it is not.
            Kill(ann, Spawn());

            LootObjectState second = Pile();
            StandOn(ann, second);

            Assert.That(Pickup(ann, second).IsAccepted, Is.True,
                "the one-fruit rule leaked into loot eligibility");

            Assert.That(Use(ann, Slot(ann, DarknessItem)).IsAccepted, Is.False,
                "a character ate two Devil Fruits");

            Assert.That(Held(ann, DarknessItem), Is.EqualTo(1),
                "the refused fruit was destroyed");
        }

        // ---- F, J, K: replay, reconnect, and the whole slice ---------------------------------------------------------

        [UnityTest]
        public IEnumerator ASixPlayerPartyRunsTheWholeChainFromBossToReconnect()
        {
            yield return LoadWorld(0f);

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            LivingCharacter winner = members[0];

            Party(PartyLootPolicy.RoundRobin, members);

            // boss -> combat -> one rare roll -> one pile
            Kill(winner, Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1), "six members, six rolls");

            LootObjectState pile = Pile();

            foreach (LivingCharacter member in members) StandOn(member, pile);

            // Only the member the policy chose may take it.
            for (var i = 1; i < members.Length; i++)
            {
                Assert.That(Pickup(members[i], pile).IsAccepted, Is.False,
                    members[i].Character + " took a drop that was not theirs");
            }

            long sequence = ++_sequence;

            Assert.That(_bootstrap.LootAuthority.Apply(winner.ConnectionId,
                pile.LootId.Value, 0, sequence).IsAccepted, Is.True);

            // A replayed request takes nothing more.
            Assert.That(_bootstrap.LootAuthority.Apply(winner.ConnectionId,
                pile.LootId.Value, 0, sequence).IsAccepted, Is.False);

            Assert.That(Held(winner, DarknessItem), Is.EqualTo(1),
                "the replay produced a second fruit");

            // inventory -> consume
            Assert.That(Use(winner, Slot(winner, DarknessItem)).IsAccepted, Is.True);
            Assert.That(winner.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));

            yield return Tick();

            int mdef = Stat(winner, "stat.mdef");

            // save -> disconnect -> reconnect
            Assert.That(_bootstrap.Simulation.Release(winner.ConnectionId).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-0", 1);

            Assert.That(returned.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness),
                "the fruit did not survive the reconnect");
            Assert.That(Held(returned, DarknessItem), Is.Zero,
                "the consumed item came back");

            yield return Tick();

            Assert.That(Stat(returned, "stat.mdef"), Is.EqualTo(mdef));
        }

        // ---- harness -------------------------------------------------------------------------------------------------

        private IEnumerator LoadWorld(float roll)
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
            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);
            Assert.That(_bootstrap.Parties, Is.Not.Null, "the world tracks no parties");

            yield return null;
        }

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        /// <summary>Forms a real party through Phase 13's own state, and tells the world.</summary>
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

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
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

        /// <summary>
        /// The pile a defeat just left, and only that one.
        /// </summary>
        /// <remarks>A boss drops a card as well as a fruit, so a pile no longer empties on
        /// one pickup and an earlier one can still be lying about. The newest is the one a
        /// test that just killed something means.</remarks>
        private LootObjectState Pile()
        {
            IReadOnlyList<LootObjectState> piles = _bootstrap.Loot.All();

            Assert.That(piles.Count, Is.GreaterThan(0), "no pile on the ground");

            return piles[piles.Count - 1];
        }

        /// <summary>The one pile currently on the ground, when there must be exactly one.</summary>
        private LootObjectState OnlyPile()
        {
            IReadOnlyList<LootObjectState> piles = _bootstrap.Loot.All();

            Assert.That(piles.Count, Is.EqualTo(1),
                "expected one pile, found " + piles.Count);

            return piles[0];
        }

        private LootPickupOutcome Pickup(LivingCharacter taker, LootObjectState pile)
        {
            return _bootstrap.LootAuthority.Apply(taker.ConnectionId, pile.LootId.Value, 0,
                ++_sequence);
        }

        private CharacterInventoryResult Use(LivingCharacter character, int slot)
        {
            CharacterInventoryAuthority inventory = _bootstrap.Simulation.Inventory(
                _bootstrap.Replication);

            inventory.Submit(character.ConnectionId, InventoryAction.Use, slot, 0, 1,
                ++_sequence);

            return inventory.LastResult;
        }

        private static void StandOn(LivingCharacter character, LootObjectState pile)
        {
            Move(character, pile.Position.X, pile.Position.Y, pile.Position.Z);
        }

        private static void Move(LivingCharacter character, float x, float y, float z)
        {
            character.Combatant.Position = new CombatPosition(x, y, z);
        }

        private static int Held(LivingCharacter character, string item)
        {
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

        private static int Stat(LivingCharacter character, string stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(new DefinitionId(stat),
                out int value), Is.True, stat + " was never computed");

            return value;
        }
    }
}

#endif
