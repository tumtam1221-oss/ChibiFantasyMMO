// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world does this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
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
    /// Owning a pet, putting one out and getting it back, in the world the shipped scene
    /// composed.
    /// </summary>
    /// <remarks>
    /// <b>The shipped world, not a built one.</b> Every world below is the committed
    /// <c>World_Server</c> scene with the production catalogue behind it, so a pet that is
    /// missing from shipped content fails these rather than passing against a fixture.
    ///
    /// <b>Requests go through the seam a client's RPC lands in.</b> Nothing here calls
    /// <c>PetService</c>: activation arrives at <see cref="ICharacterPetRequestSink"/> with a
    /// connection and a pet id, which is exactly what
    /// <c>CharacterNetworkEntity.RequestActivatePet</c> hands it.
    ///
    /// <b>The store outlives the world.</b> It is built in <c>SetUp</c> and the scene is torn
    /// down and reloaded between the two halves of a restart test, which is what happens when
    /// the dedicated server process is restarted while the backend stays up.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldPetTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";

        /// <summary>The pet the production catalogue ships.</summary>
        private const string LumiSlime = "pet.lumi_slime";

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

        /// <summary>Outlives every world this fixture builds, exactly as a backend does.</summary>
        private FakeStore _store;

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
            yield return TearDownWorld();
        }

        // ---- A: putting one out ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator ActivatingAnOwnedPetInTheShippedWorldPutsItOut()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"));

            Assert.That(character.Pets.Count, Is.EqualTo(1),
                "the shipped world did not restore an owned pet");
            Assert.That(character.Companion.IsSummoned, Is.False);

            CharacterPetResult result = Activate(1, "pet-1");

            Assert.That(result.IsAccepted, Is.True,
                "the shipped world refused its own pet: " + result);
            Assert.That(character.Companion.IsSummoned, Is.True);
            Assert.That(character.Companion.Summoned.DefinitionId.Value,
                Is.EqualTo(LumiSlime));
        }

        // ---- B: whose pet -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APetSomebodyElseOwnsCannotBePutOut()
        {
            yield return LoadWorld();

            LivingCharacter a = Admit("char-a", 1, Pets("pet-a"));
            LivingCharacter b = Admit("char-b", 2, Pets("pet-b"));

            // B names A's pet. The connection is B's, so the pet is simply not among the
            // ones this character owns.
            CharacterPetResult forged = Activate(2, "pet-a");

            Assert.That(forged.IsAccepted, Is.False, "B was handed A's pet");
            Assert.That(b.Companion.IsSummoned, Is.False);
            Assert.That(a.Companion.IsSummoned, Is.False,
                "B's request put A's pet out from under them");

            Assert.That(b.Pets.Count, Is.EqualTo(1), "B's pets changed");
            Assert.That(b.Pets[0].InstanceId.Value, Is.EqualTo("pet-b"));
        }

        // ---- C: where it stands -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFollowerStandsWhereTheServerSaysAndAtItsAuthoredHeight()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"));

            Assert.That(Activate(1, "pet-1").IsAccepted, Is.True);

            Assert.That(_bootstrap.PetAuthority.TryFollowPoint(1,
                out CombatPosition point), Is.True, "the pet that is out has nowhere to be");

            CombatPosition owner = character.Combatant.Position;

            Assert.That(point.X, Is.EqualTo(owner.X).Within(0.0001f));
            Assert.That(point.Z, Is.EqualTo(owner.Z).Within(0.0001f));

            // The height is the shipped definition's, not a number this test knows.
            PetDefinition definition = ShippedPet(LumiSlime);

            Assert.That(point.Y,
                Is.EqualTo(owner.Y + definition.VerticalOffset).Within(0.0001f));

            yield return Tick();
        }

        // ---- D: the tether ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFollowerCannotBeLeftBehindHoweverFarTheOwnerGoes()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"));

            Assert.That(Activate(1, "pet-1").IsAccepted, Is.True);

            // Straight across the map, in one step, which is the worst case a correction
            // step would have to chase. There is nothing to chase: the point is derived.
            character.Combatant.Position = new CombatPosition(950f, 12f, -400f);

            yield return Tick();

            Assert.That(_bootstrap.PetAuthority.TryFollowPoint(1,
                out CombatPosition point), Is.True);

            Assert.That(point.X, Is.EqualTo(950f).Within(0.0001f));
            Assert.That(point.Z, Is.EqualTo(-400f).Within(0.0001f));
            Assert.That(point.Y,
                Is.EqualTo(12f + ShippedPet(LumiSlime).VerticalOffset).Within(0.0001f),
                "the pet was left behind");
        }

        // ---- E: one at a time ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SwitchingPetsLeavesExactlyOneOut()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1", "pet-2"));

            Assert.That(Activate(1, "pet-1").IsAccepted, Is.True);
            Assert.That(Activate(1, "pet-2").IsAccepted, Is.True);

            Assert.That(character.Companion.IsSummoned, Is.True);
            Assert.That(character.Companion.Summoned.InstanceId.Value, Is.EqualTo("pet-2"),
                "the switch did not take");

            Assert.That(character.Pets.Count, Is.EqualTo(2),
                "switching pets changed how many are owned");

            yield return Tick();

            // What a viewer is told about this is asserted in ClientWorldVisualTests,
            // where there are real connections to be told: this fixture admits characters
            // without them, so no shadow object exists here to read.
            Assert.That(character.Companion.Summoned.DefinitionId.Value,
                Is.EqualTo(LumiSlime));
        }

        // ---- F: putting it away -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PuttingThePetAwayLeavesNothingOutAndKeepsIt()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"), active: "pet-1");

            Assert.That(character.Companion.IsSummoned, Is.True,
                "the pet that was out did not come back with them");

            Assert.That(Deactivate(1).IsAccepted, Is.True);

            Assert.That(character.Companion.IsSummoned, Is.False);
            Assert.That(character.Pets.Count, Is.EqualTo(1),
                "putting a pet away released it");

            yield return Tick();

            Assert.That(character.Companion.Summoned, Is.Null,
                "something is still out after it was put away");
        }

        // ---- G: coming back -----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePetAndTheOneThatIsOutSurviveAReconnect()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1", "pet-2"));

            Assert.That(Activate(1, "pet-2").IsAccepted, Is.True);

            yield return Tick();

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True,
                "the character could not be written out");

            yield return Tick();

            // No pets are handed to this admission: whatever comes back was stored.
            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.Pets.Count, Is.EqualTo(2),
                "owned pets did not survive a reconnect");
            Assert.That(returned.Companion.IsSummoned, Is.True,
                "the pet that was out did not come back");
            Assert.That(returned.Companion.Summoned.InstanceId.Value, Is.EqualTo("pet-2"),
                "a different pet came back");
        }

        // ---- H: the whole world goes down -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePetsAndTheActiveOneSurviveAWorldRestart()
        {
            yield return LoadWorld();

            Admit("char-a", 1, Pets("pet-1", "pet-2"));

            Assert.That(Activate(1, "pet-1").IsAccepted, Is.True);

            yield return Tick();

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            // A real restart: the scene is unloaded and the bootstrap destroyed. Only the
            // store survives, exactly as a backend does.
            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.Pets.Count, Is.EqualTo(2),
                "a restart lost the pets this character owns");
            Assert.That(returned.Companion.IsSummoned, Is.True,
                "a restart put the pet away");
            Assert.That(returned.Companion.Summoned.InstanceId.Value, Is.EqualTo("pet-1"));
            Assert.That(returned.Companion.Summoned.DefinitionId.Value,
                Is.EqualTo(LumiSlime), "the restarted world resolved a different pet");
        }

        // ---- I: dying -----------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DyingTakesNeitherThePetNorTheOneThatIsOut()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"));
            LivingCharacter killer = Admit("char-b", 2, team: 2);

            Assert.That(Activate(1, "pet-1").IsAccepted, Is.True);

            yield return Tick();

            for (var i = 0; i < 60 && character.Combatant.IsAlive(); i++)
            {
                Combat.Tick(60f);
                Combat.Execute(killer.ConnectionId,
                    new CombatCommand(killer.Character, character.CombatantId, default, 0,
                        ++_sequence));
            }

            Assert.That(character.Combatant.IsAlive(), Is.False, "the victim would not die");

            yield return Tick();

            // Ownership is not a status effect: death clears temporary state, and a pet is
            // neither temporary nor an effect.
            Assert.That(character.Pets.Count, Is.EqualTo(1), "death took the pet");
            Assert.That(character.Companion.IsSummoned, Is.True,
                "death put the pet away, which is a status-effect policy and not an "
                + "ownership one");
        }

        // ---- J: what cannot be said ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NothingAboutAPetCanBeForgedThroughARequest()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1, Pets("pet-1"));

            // A pet that does not exist, one belonging to nobody, and an empty id.
            Assert.That(Activate(1, "pet-nowhere").IsAccepted, Is.False);
            Assert.That(Activate(1, string.Empty).IsAccepted, Is.False);
            Assert.That(character.Companion.IsSummoned, Is.False);

            // A connection with no character here.
            Assert.That(Activate(9999, "pet-1").IsAccepted, Is.False);
            Assert.That(character.Companion.IsSummoned, Is.False,
                "a stranger's request put this character's pet out");

            // The request cannot carry anything but which pet: no character, no level, no
            // experience, no stage, no buff and no position. Proved structurally, because
            // there is no argument that could hold one.
            System.Reflection.MethodInfo activate =
                typeof(ICharacterPetRequestSink).GetMethod("Activate");

            Assert.That(activate.GetParameters().Length, Is.EqualTo(2));
            Assert.That(activate.GetParameters()[0].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(activate.GetParameters()[1].ParameterType,
                Is.EqualTo(typeof(InstanceId)));

            foreach (System.Reflection.ParameterInfo parameter in
                typeof(CharacterNetworkEntity).GetMethod("RequestActivatePet")
                    .GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.EqualTo(typeof(string)),
                    "a client can say something about a pet other than which one");
            }

            Assert.That(typeof(CharacterNetworkEntity).GetMethod("RequestDeactivatePet")
                .GetParameters().Length, Is.Zero,
                "putting a pet away takes an argument, so it could name somebody else's");
        }

        // ---- harness ---------------------------------------------------------------------------------------------

        private IEnumerator LoadWorld()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();
            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            Assert.That(_bootstrap.PetAuthority, Is.Not.Null,
                "the shipped world composed no pet authority");
            Assert.That(_bootstrap.Pets, Is.Not.Null,
                "the shipped world composed no pets");

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

        /// <summary>Rows for pets of the one kind the production catalogue ships.</summary>
        private static PersistedPet[] Pets(params string[] instances)
        {
            var rows = new PersistedPet[instances.Length];

            for (var i = 0; i < instances.Length; i++)
            {
                rows[i] = new PersistedPet(new InstanceId(instances[i]),
                    new DefinitionId(LumiSlime), 1, 0, 0);
            }

            return rows;
        }

        /// <summary>Writes the row a character loads from, unless one is already stored.</summary>
        private void Seed(string character, PersistedPet[] pets, string active)
        {
            string session = "session-" + character;

            if (_store.Rows.ContainsKey(session)) return;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 1, 1, 0, 104, 35,
                new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                default, StarterAttributes(), null, null, 1, null, 0, default, null,
                pets, active == null ? default : new InstanceId(active));
        }

        private LivingCharacter Admit(string character, int connection,
            PersistedPet[] pets = null, string active = null, int team = 1)
        {
            Seed(character, pets, active);

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static PersistedStat[] StarterAttributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId("stat.str"), 10),
                new PersistedStat(new DefinitionId("stat.vit"), 8),
                new PersistedStat(new DefinitionId("stat.int"), 3),
            };
        }

        /// <summary>The seam a client's own RPC lands in, reached the same way.</summary>
        private CharacterPetResult Activate(int connection, string pet)
        {
            ICharacterPetRequestSink sink = _bootstrap.PetAuthority;

            sink.Activate(connection, new InstanceId(pet));

            return _bootstrap.PetAuthority.LastResult;
        }

        private CharacterPetResult Deactivate(int connection)
        {
            ICharacterPetRequestSink sink = _bootstrap.PetAuthority;

            sink.Deactivate(connection);

            return _bootstrap.PetAuthority.LastResult;
        }


        /// <summary>The production definition, so no number here is invented.</summary>
        private PetDefinition ShippedPet(string id)
        {
            Assert.That(_bootstrap.Pets.TryGet(new DefinitionId(id),
                out PetDefinition definition), Is.True,
                "the shipped catalogue has no " + id);

            return definition;
        }

        private ServerCombatPipeline Combat
        {
            get
            {
                var value = (ServerCombatPipeline)typeof(WorldSimulation)
                    .GetField("_combat", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
                    .GetValue(_bootstrap.Simulation);

                Assert.That(value, Is.Not.Null, "the scene composed no combat pipeline");

                return value;
            }
        }
    }
}

#endif
