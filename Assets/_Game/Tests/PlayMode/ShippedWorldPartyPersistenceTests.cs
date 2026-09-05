// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world restores it.
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
    /// A party surviving a disconnect, and a whole world being rebuilt around it.
    /// </summary>
    /// <remarks>
    /// <b>The restart is a real one.</b> These tests do not reset a field and call it a
    /// restart: the scene is unloaded, the bootstrap destroyed and a fresh production world
    /// composed from the committed scene, with only the backend surviving — which is
    /// exactly what happens when the dedicated server process is restarted.
    ///
    /// <b>Nothing is put back by hand.</b> Every party that exists after a restart got
    /// there by a member being admitted and the world reading storage.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldPartyPersistenceTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string DarknessItem = "item.devil_fruit.darkness";

        private const float Fraction = 0.0000001f;

        /// <summary>Storage that outlives the world, as a database does.</summary>
        private sealed class PartyStore : IPartyStateStore
        {
            private readonly Dictionary<string, PersistedParty> _byCharacter =
                new Dictionary<string, PersistedParty>();

            public int Loads { get; private set; }

            public int Saves { get; private set; }

            public PartyPersistenceResult Load(SessionId session)
            {
                Loads++;

                // Sessions here are "session-<character>", which is what the fixture admits.
                string character = session.Value.StartsWith("session-")
                    ? session.Value.Substring("session-".Length)
                    : session.Value;

                return _byCharacter.TryGetValue(character, out PersistedParty party)
                    ? PartyPersistenceResult.Loaded(party)
                    : PartyPersistenceResult.None();
            }

            public PartyPersistenceResult Save(SessionId session, PersistedParty party)
            {
                Saves++;

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

        // These two outlive every world this fixture builds, exactly as a backend does.
        private CharacterStore _characters;
        private PartyStore _parties;
        private ScriptedRandom _rolls;

        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _parties = new PartyStore();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return TearDownWorld();
        }

        // ---- A: a party is written down ------------------------------------------------------

        [UnityTest]
        public IEnumerator FormingAPartyStoresItAndTheWorldKnowsAboutIt()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);

            Assert.That(_bootstrap.Parties.Persist(Session("char-ann"), party,
                _parties).IsOk, Is.True, "the party was not written down");

            Assert.That(_bootstrap.Parties.TryGetPartyOf(ann.Character, out PartyState _),
                Is.True);

            // Stored under both members, so either of them restores it.
            Assert.That(_parties.Load(Session("char-ben")).Party.Exists, Is.True);
        }

        // ---- B: one member reconnects ----------------------------------------------------------

        [UnityTest]
        public IEnumerator AMemberWhoReconnectsIsPutBackInTheSameParty()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            PartyId original = party.Id;

            // Ben drops out and comes back.
            Assert.That(_bootstrap.Simulation.Release(2).IsOk, Is.True);

            yield return Tick();

            Admit("char-ben", 2);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ben"),
                out PartyState after), Is.True, "a reconnecting member lost their party");

            Assert.That(after.Id, Is.EqualTo(original));
            Assert.That(after.Leader, Is.EqualTo(ann.Character));
            Assert.That(after.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin));
            Assert.That(after.Members.Count, Is.EqualTo(2));
        }

        // ---- C and D: everybody leaves, and the world is rebuilt --------------------------------------

        [UnityTest]
        public IEnumerator APartyOutlivesEveryMemberGoingOfflineAndTheWholeWorldRestarting()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);
            LivingCharacter cal = Admit("char-cal", 3);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben, cal);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            PartyId original = party.Id;

            // Everybody logs out.
            for (var connection = 1; connection <= 3; connection++)
            {
                _bootstrap.Simulation.Release(connection);
            }

            yield return Tick();

            // And the whole world is torn down and built again from the shipped scene.
            yield return TearDownWorld();
            yield return LoadWorld();

            Assert.That(_bootstrap.Parties.Count, Is.Zero,
                "a fresh world started with parties it had not been asked for");

            LivingCharacter returned = Admit("char-cal", 1);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(returned.Character,
                out PartyState restored), Is.True,
                "the party did not survive a world restart");

            Assert.That(restored.Id, Is.EqualTo(original));
            Assert.That(restored.Leader, Is.EqualTo(ann.Character));
            Assert.That(restored.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin));

            Assert.That(restored.Members.Select(m => m.Value),
                Is.EqualTo(new[] { "char-ann", "char-ben", "char-cal" }),
                "the member order changed, which changes whose loot turn it is");
        }

        // ---- E: everybody comes back at once ------------------------------------------------------------

        [UnityTest]
        public IEnumerator SixMembersReconnectingIntoAFreshWorldShareOneParty()
        {
            yield return LoadWorld();

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            PartyState party = Party(PartyLootPolicy.RoundRobin, members);
            _bootstrap.Parties.Persist(Session("char-0"), party, _parties);

            yield return TearDownWorld();
            yield return LoadWorld();

            for (var i = 0; i < 6; i++) Admit("char-" + i, i + 1);

            yield return Tick();

            Assert.That(_bootstrap.Parties.Count, Is.EqualTo(1),
                "six reconnects built " + _bootstrap.Parties.Count + " parties");

            var seen = new List<PartyState>();

            for (var i = 0; i < 6; i++)
            {
                Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-" + i),
                    out PartyState found), Is.True, "char-" + i + " lost their party");

                seen.Add(found);
            }

            Assert.That(seen.Distinct().Count(), Is.EqualTo(1),
                "members attached to different party objects");

            Assert.That(seen[0].MemberCount, Is.EqualTo(6),
                "restoring repeatedly grew the party");
        }

        // ---- F: the rotation is unchanged ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheLootRotationPointsAtTheSameMemberAfterARestart()
        {
            yield return LoadWorld();

            LivingCharacter[] members = new[]
            {
                Admit("char-ann", 1), Admit("char-ben", 2), Admit("char-cal", 3),
            };

            PartyState party = Party(PartyLootPolicy.RoundRobin, members);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            CharacterId turnOne = PartyLootPolicyService.MemberOnTurn(party, 1);
            CharacterId turnTwo = PartyLootPolicyService.MemberOnTurn(party, 2);

            yield return TearDownWorld();
            yield return LoadWorld();

            Admit("char-ann", 1);

            yield return Tick();

            _bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ann"),
                out PartyState restored);

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored, 1),
                Is.EqualTo(turnOne), "turn one belongs to somebody else after a restart");

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored, 2),
                Is.EqualTo(turnTwo));
        }

        // ---- G: the reason this gate exists ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ARestoredPartyStillSplitsBossRewardsAndClaimsTheRareDrop()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.Personal, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            // A whole new world, and then the same party walks back into it.
            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter annAgain = Admit("char-ann", 1);
            LivingCharacter benAgain = Admit("char-ben", 2);
            LivingCharacter stranger = Admit("char-x", 3, team: 2);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(annAgain.Character,
                out PartyState _), Is.True, "the party was not restored");

            long annBefore = annAgain.Domain.Progression.Experience;
            long benBefore = benAgain.Domain.Progression.Experience;

            Kill(annAgain, Spawn());

            // 900 split between the two restored members.
            Assert.That(annAgain.Domain.Progression.Experience - annBefore,
                Is.EqualTo(450), "a restored party did not share the reward");
            Assert.That(benAgain.Domain.Progression.Experience - benBefore,
                Is.EqualTo(450));

            // Still one roll at the authored rate.
            Assert.That(_rolls.RareRolls, Is.EqualTo(1),
                "restoring a party changed how often the fruit is rolled");

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(stranger, pile);
            StandOn(benAgain, pile);
            StandOn(annAgain, pile);

            Assert.That(Pickup(stranger, pile).IsAccepted, Is.False,
                "a stranger took a restored party's drop");
            Assert.That(Pickup(benAgain, pile).IsAccepted, Is.False,
                "the wrong member took the drop under Personal");
            Assert.That(Pickup(annAgain, pile).IsAccepted, Is.True,
                "the killer could not claim their own drop after a restart");

            Assert.That(Held(annAgain, DarknessItem), Is.EqualTo(1));
        }

        // ---- H: a party that ended stays ended ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADisbandedPartyIsNotRestored()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.Personal, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            // Disband: an explicit empty membership, which is the shape the API defines.
            Assert.That(_parties.Save(Session("char-ann"), new PersistedParty(party.Id,
                ann.Character, party.LootPolicy, new CharacterId[0], 0)).IsOk, Is.True);

            yield return TearDownWorld();
            yield return LoadWorld();

            Admit("char-ann", 1);
            Admit("char-ben", 2);

            yield return Tick();

            Assert.That(_bootstrap.Parties.Count, Is.Zero,
                "a disbanded party came back after a restart");

            Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ann"),
                out PartyState _), Is.False);
        }

        // ---- I: the turn a monster moved -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ATurnSpentOnABossIsStillSpentAfterTheWorldRestarts()
        {
            // The defect this gate closes. Nothing calls Persist after the kill: the
            // rotation moves inside the reward path, and if that move is not written
            // down, the restarted world hands the next drop back to the first member.
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            PartyId original = party.Id;
            int writesBefore = _parties.Saves;

            Kill(ann, Spawn());

            Assert.That(_bootstrap.Parties.RotationOf(original), Is.EqualTo(1),
                "the boss dropped a pile without moving the party's turn");

            Assert.That(_parties.Saves, Is.GreaterThan(writesBefore),
                "the turn moved and nothing wrote it down");

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            Admit("char-ann", 1);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ann"),
                out PartyState restored), Is.True);

            Assert.That(_bootstrap.Parties.RotationOf(restored.Id), Is.EqualTo(1),
                "the restarted world rewound the rotation to the first member");

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored,
                    _bootstrap.Parties.RotationOf(restored.Id)),
                Is.EqualTo(new CharacterId("char-ben")),
                "the turn came back pointing at the wrong member");
        }

        // ---- J: A, B, C, A, across a restart each time -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheRotationWalksTheWholePartyEvenIfTheWorldRestartsBetweenEveryDrop()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);
            LivingCharacter cal = Admit("char-cal", 3);

            PartyState formed = Party(PartyLootPolicy.RoundRobin, ann, ben, cal);
            _bootstrap.Parties.Persist(Session("char-ann"), formed, _parties);

            var order = new List<string>();

            for (var restart = 0; restart < 4; restart++)
            {
                yield return TearDownWorld();
                yield return LoadWorld();

                Admit("char-ann", 1);

                yield return Tick();

                Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ann"),
                    out PartyState party), Is.True,
                    "restart " + restart + " lost the party");

                order.Add(PartyLootPolicyService.MemberOnTurn(party,
                    _bootstrap.Parties.RotationOf(party.Id)).Value);

                // One pile handed to the party, as a defeat would.
                _bootstrap.Parties.AdvanceRotation(party.Id);
            }

            Assert.That(order, Is.EqualTo(new[]
            {
                "char-ann", "char-ben", "char-cal", "char-ann",
            }), "the rotation skipped or repeated a member across restarts");
        }

        // ---- K: the party gets smaller ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMemberLeavingLeavesATurnThatStillAddressesSomebody()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);
            LivingCharacter cal = Admit("char-cal", 3);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben, cal);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            // Two drops: the turn now points at char-cal, the third member.
            _bootstrap.Parties.AdvanceRotation(party.Id);
            _bootstrap.Parties.AdvanceRotation(party.Id);

            Assert.That(_bootstrap.Parties.RotationOf(party.Id), Is.EqualTo(2));

            // char-cal leaves. Position two no longer exists.
            Assert.That(party.TryRemove(cal.Character), Is.True);

            Assert.That(_bootstrap.Parties.Persist(Session("char-ann"), party,
                _parties).IsOk, Is.True, "a shrunk party could not be written");

            yield return TearDownWorld();
            yield return LoadWorld();

            Admit("char-ann", 1);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-ann"),
                out PartyState restored), Is.True,
                "a shrunk party wrote a turn its own loader refuses");

            Assert.That(restored.Members.Count, Is.EqualTo(2));

            // Position two, reduced against a two-member party, is position zero. Not an
            // arbitrary choice: it is the member the running world was already going to
            // pick, because MemberOnTurn takes the same modulo. The restart changes
            // nothing, which is the whole point.
            Assert.That(_bootstrap.Parties.RotationOf(restored.Id), Is.Zero);

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored,
                _bootstrap.Parties.RotationOf(restored.Id)),
                Is.EqualTo(ann.Character),
                "the turn survived pointing at a member who left");
        }

        // ---- L: the restored turn decides who may take the boss drop -------------------------------------------

        [UnityTest]
        public IEnumerator ARestoredTurnDecidesWhoClaimsTheRareDrop()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter ann = Admit("char-ann", 1);
            LivingCharacter ben = Admit("char-ben", 2);

            PartyState party = Party(PartyLootPolicy.RoundRobin, ann, ben);
            _bootstrap.Parties.Persist(Session("char-ann"), party, _parties);

            // One turn spent, and written down by the world rather than by this test.
            _bootstrap.Parties.AdvanceRotation(party.Id);

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            LivingCharacter annAgain = Admit("char-ann", 1);
            LivingCharacter benAgain = Admit("char-ben", 2);

            yield return Tick();

            Assert.That(_bootstrap.Parties.TryGetPartyOf(annAgain.Character,
                out PartyState restored), Is.True);

            Assert.That(_bootstrap.Parties.RotationOf(restored.Id), Is.EqualTo(1),
                "the restored world forgot whose turn it was");

            // char-ann kills it, but it is char-ben's turn -- and the rotation that says
            // so came out of storage, not out of this world's memory.
            Kill(annAgain, Spawn());

            Assert.That(_rolls.RareRolls, Is.EqualTo(1),
                "restoring a turn changed how often the fruit is rolled");

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(annAgain, pile);
            StandOn(benAgain, pile);

            Assert.That(Pickup(annAgain, pile).IsAccepted, Is.False,
                "the killer took a drop that a restored rotation had promised elsewhere");

            Assert.That(Pickup(benAgain, pile).IsAccepted, Is.True,
                "the member whose turn it was could not claim the drop");

            Assert.That(Held(benAgain, DarknessItem), Is.EqualTo(1));
        }

        // ---- M: everybody comes back at the same moment -------------------------------------------------------

        [UnityTest]
        public IEnumerator SixMembersReconnectingAtOnceRestoreOneTurnBetweenThem()
        {
            yield return LoadWorld();

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            PartyState party = Party(PartyLootPolicy.RoundRobin, members);
            _bootstrap.Parties.Persist(Session("char-0"), party, _parties);

            _bootstrap.Parties.AdvanceRotation(party.Id);
            _bootstrap.Parties.AdvanceRotation(party.Id);
            _bootstrap.Parties.AdvanceRotation(party.Id);

            yield return TearDownWorld();
            yield return LoadWorld();

            for (var i = 0; i < 6; i++) Admit("char-" + i, i + 1);

            yield return Tick();

            Assert.That(_bootstrap.Parties.Count, Is.EqualTo(1));

            Assert.That(_bootstrap.Parties.TryGetPartyOf(new CharacterId("char-0"),
                out PartyState restored), Is.True);

            // Six reads of the same row must leave one turn, not six advances of it.
            Assert.That(_bootstrap.Parties.RotationOf(restored.Id), Is.EqualTo(3),
                "reconnecting members moved the turn between them");

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored,
                _bootstrap.Parties.RotationOf(restored.Id)),
                Is.EqualTo(new CharacterId("char-3")));
        }

        // ---- harness --------------------------------------------------------------------------------------------

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
            _bootstrap.Compose(new AlwaysAdmits(), default, _characters, null, _parties);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);
            Assert.That(_bootstrap.PartyStore, Is.Not.Null,
                "the world composed no party store");

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
            // handler, so the restore it performs is done here in the same order.
            if (_bootstrap.Parties != null && _bootstrap.PartyStore != null)
            {
                _bootstrap.Parties.Restore(new SessionId(session),
                    new CharacterId(character), _bootstrap.PartyStore);
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
    }
}

#endif