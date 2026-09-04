// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry and the approved character art through
// AssetDatabase, because the point is to prove the SHIPPED configuration works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.Prototype;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// The approved characters, seen by real clients, over a real socket.
    /// </summary>
    /// <remarks>
    /// <b>Everything under test here is what a player would look at.</b> The production
    /// prefab, the production catalogue, the approved male and female models, the existing
    /// locomotion controller and the Phase 07.1 camera rig -- composed the way the world
    /// scene composes them. A test that built its own cube instead would prove that a cube
    /// can follow a position.
    ///
    /// <b>Movement is asked for, never assigned.</b> Not one test below writes a transform.
    /// A client sends the Phase 18.3 movement request, the server decides, the position
    /// replicates, and the picture follows -- which is the only order that proves the
    /// picture is downstream of the server rather than beside it.
    ///
    /// <b>Two clients, because one proves nothing about isolation.</b> A camera that follows
    /// its owner is only interesting when there is somebody else it could have followed.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientWorldVisualTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";
        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/CharacterVisualCatalogue.asset";
        private const string CameraSettingsPath =
            "Assets/_Game/Prefabs/Prototype/ProtoCameraSettings.asset";

        private const string HomeMap = "map.home";
        private const float Speed = 4f;

        /// <summary>A store that keeps what it was given, so a reconnect reads it back.</summary>
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

        private GameObject _serverObject;
        private GameObject _clientAObject;
        private GameObject _clientBObject;
        private NetworkManager _server;
        private NetworkManager _clientA;
        private NetworkManager _clientB;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterMovementAuthority _movement;
        private CharacterReplicationService _replication;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(49100, 51900);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("VisServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("VisClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("VisClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(HomeMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns);

            _movement = new CharacterMovementAuthority(_players, _ => true, maps, Speed);

            _replication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), null, _movement);
        }

        [TearDown]
        public void TearDown()
        {
            _replication?.DespawnAll();

            if (_clientB != null) _clientB.ClientManager.StopConnection();
            if (_clientA != null) _clientA.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientBObject != null) Object.DestroyImmediate(_clientBObject);
            if (_clientAObject != null) Object.DestroyImmediate(_clientAObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- A: the approved model appears -------------------------------------------------------

        [UnityTest]
        public IEnumerator EnteringTheWorldPutsTheApprovedModelOnTheOwnedCharacter()
        {
            yield return StartServerAndOneClient();

            EnterWorld("char-a", Connections()[0], CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual);

            CharacterVisualPresenter visual = Visual(_clientA, "char-a");

            Assert.That(visual.Gender, Is.EqualTo(CharacterGender.Male),
                "the model follows the gender the server replicated");
            Assert.That(visual.Model.name, Does.Contain("CHR_Base_Male"),
                "the approved production model, not a stand-in");
            Assert.That(visual.VisualRoot, Is.Not.Null);
            Assert.That(visual.Model.transform.IsChildOf(visual.VisualRoot), Is.True,
                "the model hangs off the visual root, not off the network object");

            Assert.That(visual.Model.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Null, "an approved character with no renderer is an invisible player");

            Assert.That(visual.Animator, Is.Not.Null);
            Assert.That(visual.Animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(visual.Animator.runtimeAnimatorController.name,
                Is.EqualTo("Proto_Locomotion"), "the existing controller, not a new one");
            Assert.That(visual.Animator.applyRootMotion, Is.False,
                "root motion is a clip writing the transform, which is a client writing "
                + "its own position");

            // The visual is presentation: the identity is still the one network object.
            Assert.That(visual.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(visual.Model.GetComponentInChildren<NetworkObject>(true), Is.Null,
                "one identity per character, never one per visual");
        }

        [UnityTest]
        public IEnumerator TheServerItselfNeverRigsAModelItWillNeverDraw()
        {
            yield return StartServerAndOneClient();

            EnterWorld("char-a", Connections()[0], CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual);

            Assert.That(_replication.TryGet(new CharacterId("char-a"),
                out NetworkObject onServer), Is.True);

            var serverSide = onServer.GetComponent<CharacterVisualPresenter>();

            Assert.That(serverSide, Is.Not.Null, "the same production prefab, both sides");
            Assert.That(serverSide.HasVisual, Is.False,
                "a headless server building character meshes is a cost per player, forever");
        }

        // ---- B: walking ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AskingTheServerToWalkMovesTheVisualAndPlaysTheWalk()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0],
                CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual);

            CharacterVisualPresenter visual = Visual(_clientA, "char-a");

            float startZ = visual.transform.position.z;

            Assert.That(visual.Speed01, Is.Zero, "precondition: standing");

            // The Phase 18.3 path, in full: the client asks, the server decides, the new
            // position replicates. Nothing below assigns a transform.
            _movement.Tick(0.25f);

            Entity(_clientA, "char-a").RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(_movement.LastResult.IsAccepted, Is.True,
                _movement.LastResult.ToString());

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a").Z > startZ + 0.1f);

            Assert.That(character.Location.Position.Z, Is.GreaterThan(startZ),
                "the server moved them");

            // The picture catches up, and the legs move while it does.
            yield return Until(() => visual.Speed01 > 0f, 200);

            Assert.That(visual.Speed01, Is.GreaterThan(0f), "the walk never started");
            Assert.That(visual.Animator.GetFloat("Speed"), Is.GreaterThan(0f),
                "the animator was never told");

            yield return Until(() =>
                Mathf.Abs(visual.transform.position.z - Entity(_clientA, "char-a").Z) < 0.05f,
                400);

            Assert.That(visual.transform.position.z,
                Is.EqualTo(Entity(_clientA, "char-a").Z).Within(0.05f),
                "the visual arrives where the server put them, not somewhere near it");

            // And standing still returns to idle, because the position stopped changing.
            yield return Until(() => visual.Speed01 <= 0f, 200);

            Assert.That(visual.Speed01, Is.Zero, "the character walks on the spot forever");
            Assert.That(visual.Animator.GetFloat("Speed"), Is.Zero);
        }

        // ---- C: two clients ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EachClientSeesBothCharactersAndItsCameraFollowsOnlyItsOwn()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            EnterWorld("char-a", connections[0], CharacterGender.Male, "Aldric");
            EnterWorld("char-b", connections[1], CharacterGender.Female, "Brenna");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-b") != null
                && Visual(_clientB, "char-a") != null
                && Visual(_clientB, "char-b") != null);

            // Both players are drawn on both clients, each with their own approved model.
            Assert.That(Visual(_clientA, "char-b").Gender, Is.EqualTo(CharacterGender.Female),
                "a remote player is drawn as the gender the server replicated");
            Assert.That(Visual(_clientB, "char-a").Gender, Is.EqualTo(CharacterGender.Male));

            WorldCameraDirector a = Director(_clientA);
            WorldCameraDirector b = Director(_clientB);

            yield return Until(() => a.IsBound && b.IsBound);

            Assert.That(a.Bound.Character.Value, Is.EqualTo("char-a"));
            Assert.That(b.Bound.Character.Value, Is.EqualTo("char-b"));

            Assert.That(a.Rig.Target, Is.SameAs(Entity(_clientA, "char-a").transform));
            Assert.That(b.Rig.Target, Is.SameAs(Entity(_clientB, "char-b").transform));

            Assert.That(a.BindCount, Is.EqualTo(1), "one camera, bound once");
            Assert.That(b.BindCount, Is.EqualTo(1));

            // And neither can be made to follow the other, asked directly.
            Assert.That(a.Bind(Entity(_clientA, "char-b")), Is.False,
                "a camera pointed at somebody else is a spectator mode nobody asked for");
            Assert.That(a.Bound.Character.Value, Is.EqualTo("char-a"), "and it did not move");
            Assert.That(b.Bind(Entity(_clientB, "char-a")), Is.False);

            // One camera per client, and never one per character.
            Assert.That(CamerasUnder(a), Is.EqualTo(1));
            Assert.That(CamerasUnder(b), Is.EqualTo(1));
            Assert.That(Visual(_clientA, "char-b").GetComponentInChildren<Camera>(true),
                Is.Null, "a remote player never gets a camera");
            Assert.That(Visual(_clientB, "char-a").GetComponentInChildren<Camera>(true),
                Is.Null);
        }

        [UnityTest]
        public IEnumerator ANameplateShowsTheNameAndNeverAnIdentifier()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            EnterWorld("char-a", connections[0], CharacterGender.Male, "Aldric");
            EnterWorld("char-b", connections[1], CharacterGender.Female, "Brenna");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-b") != null
                && Visual(_clientA, "char-b").HasVisual);

            CharacterVisualPresenter remote = Visual(_clientA, "char-b");

            yield return Until(() => remote.NameplateText.Length > 0);

            Assert.That(remote.NameplateText, Is.EqualTo("Brenna"));
            Assert.That(remote.NameplateText, Does.Not.Contain("char-b"),
                "an identifier above a head is an identifier in every screenshot");
            Assert.That(remote.NameplateText, Does.Not.Contain("acc-"));

            // The local player is not labelled to themselves, which is the usual choice.
            Assert.That(Visual(_clientA, "char-a").Nameplate, Is.Null);
        }

        // ---- D: watching somebody else walk -----------------------------------------------------------

        [UnityTest]
        public IEnumerator WhenAWalksBSeesAWalk()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            EnterWorld("char-a", connections[0], CharacterGender.Male, "Aldric");
            EnterWorld("char-b", connections[1], CharacterGender.Female, "Brenna");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientB, "char-a") != null
                && Visual(_clientB, "char-a").HasVisual);

            // B's copy of A. B sends nothing for it and owns none of it.
            CharacterVisualPresenter remote = Visual(_clientB, "char-a");

            Assert.That(remote.GetComponent<CharacterNetworkEntity>().IsOwner, Is.False);

            float startZ = remote.transform.position.z;

            Assert.That(remote.Speed01, Is.Zero, "precondition");

            // A asks to walk, on A's own client.
            _movement.Tick(0.25f);

            Entity(_clientA, "char-a").RequestMove(0f, 1f, 1);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(_movement.LastResult.IsAccepted, Is.True);

            _replication.Synchronise();

            yield return Until(() => remote.transform.position.z > startZ + 0.05f, 300);

            Assert.That(remote.transform.position.z, Is.GreaterThan(startZ),
                "B's picture of A never moved");

            yield return Until(() => remote.Speed01 > 0f, 200);

            Assert.That(remote.Speed01, Is.GreaterThan(0f),
                "a remote character sliding along in an idle pose is the classic MMO bug");

            yield return Until(() => remote.Speed01 <= 0f, 300);

            Assert.That(remote.Speed01, Is.Zero, "and stops walking when A stops");
        }

        // ---- E: death ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheServerKillingSomebodyChangesThePictureAndNothingElse()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0],
                CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual);

            CharacterVisualPresenter visual = Visual(_clientA, "char-a");

            Assert.That(visual.IsPresentedDead, Is.False, "precondition: standing");

            // The authoritative health goes to zero on the server, through the character's
            // own combatant. The client is not consulted.
            character.Combatant.ApplyHealthDelta(-character.Combatant.CurrentHealth);

            Assert.That(character.Combatant.CurrentHealth, Is.Zero);

            _replication.Synchronise();

            yield return Until(() => visual.IsPresentedDead, 300);

            Assert.That(Entity(_clientA, "char-a").IsAlive, Is.False,
                "the client learned it from replicated health, not from a message it sent");
            Assert.That(visual.IsPresentedDead, Is.True);
            Assert.That(visual.Speed01, Is.Zero, "a corpse does not walk");
            Assert.That(visual.Animator.GetBool("Dead"), Is.True);

            // The tilt is on the visual child. The network object's own transform is still
            // the server's to place.
            Assert.That(visual.VisualRoot.localRotation, Is.Not.EqualTo(Quaternion.identity));

            // Nothing the client did brought anybody back, paid anybody, or moved anybody.
            Assert.That(character.Combatant.CurrentHealth, Is.Zero,
                "the client must not have healed them by drawing them");
            Assert.That(character.Domain.Progression.Experience, Is.Zero,
                "a death is not a payout the client can grant");
            Assert.That(_players.Count, Is.EqualTo(1), "and nobody was despawned locally");

            // A dead character's movement request is refused by the existing authority, so
            // the picture cannot be walked around either.
            Vector3 restingPlace = visual.transform.position;

            _movement.Tick(0.25f);

            Entity(_clientA, "char-a").RequestMove(0f, 1f, 5);

            yield return Until(() => _movement.Handled > 0);

            Assert.That(_movement.LastResult.IsAccepted, Is.False,
                "the dead do not walk, and that is the server's rule not the animator's");

            _replication.Synchronise();

            yield return Until(() => false, 20);

            Assert.That(visual.transform.position,
                Is.EqualTo(restingPlace).Using<Vector3>(
                    (x, y) => (x - y).magnitude < 0.05f ? 0 : 1));
        }

        // ---- F and G: leaving, and coming back ------------------------------------------------------

        [UnityTest]
        public IEnumerator ADespawnTakesTheVisualAndTheCameraTargetWithIt()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            EnterWorld("char-a", connection, CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            WorldCameraDirector camera = Director(_clientA);

            yield return Until(() => camera.IsBound
                && Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual);

            Assert.That(camera.BindCount, Is.EqualTo(1));

            Assert.That(_players.Despawn(connection).IsOk, Is.True);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") == null);

            Assert.That(Visual(_clientA, "char-a"), Is.Null, "the visual outlived its object");

            yield return Until(() => !camera.IsBound);

            Assert.That(camera.IsBound, Is.False,
                "a camera still following a destroyed object is where the null references "
                + "after a disconnect come from");
            Assert.That(camera.Camera, Is.Not.Null);
            Assert.That(camera.Camera.gameObject.activeInHierarchy, Is.True,
                "the camera survives the character: a black screen is worse than no target");
        }

        [UnityTest]
        public IEnumerator ComingBackRebuildsTheVisualAndBindsTheCameraExactlyOnceMore()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            EnterWorld("char-a", connection, CharacterGender.Male, "Aldric");

            _replication.Synchronise();

            WorldCameraDirector camera = Director(_clientA);

            yield return Until(() => camera.IsBound && Visual(_clientA, "char-a") != null);

            Assert.That(_players.Despawn(connection).IsOk, Is.True);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") == null && !camera.IsBound);

            // Back again, on the same connection.
            LivingCharacter returned = EnterWorld("char-a", connection, CharacterGender.Male,
                "Aldric");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-a") != null
                && Visual(_clientA, "char-a").HasVisual && camera.IsBound);

            CharacterVisualPresenter visual = Visual(_clientA, "char-a");

            Assert.That(visual.Gender, Is.EqualTo(CharacterGender.Male));
            Assert.That(visual.BuildCount, Is.EqualTo(1),
                "one model per character object, not one per snapshot");

            Assert.That(camera.BindCount, Is.EqualTo(2), "rebinding is the normal case");
            Assert.That(CamerasUnder(camera), Is.EqualTo(1),
                "a reconnect that adds a second camera is a reconnect nobody survives");
            Assert.That(camera.Rig.Target, Is.SameAs(visual.transform));

            // And the picture is where the server put them, not gliding in from the last life.
            yield return Until(() =>
                (visual.transform.position
                    - new Vector3(returned.Location.Position.X, returned.Location.Position.Y,
                        returned.Location.Position.Z)).magnitude < 0.05f, 200);

            Assert.That(visual.transform.position.x,
                Is.EqualTo(returned.Location.Position.X).Within(0.05f));
            Assert.That(visual.transform.position.z,
                Is.EqualTo(returned.Location.Position.Z).Within(0.05f));
        }

        // ---- H: the female character ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AFemaleCharacterGetsTheApprovedFemaleModelWithAValidRig()
        {
            yield return StartServerAndOneClient();

            EnterWorld("char-f", Connections()[0], CharacterGender.Female, "Brenna");

            _replication.Synchronise();

            yield return Until(() => Visual(_clientA, "char-f") != null
                && Visual(_clientA, "char-f").HasVisual);

            CharacterVisualPresenter visual = Visual(_clientA, "char-f");

            Assert.That(visual.Gender, Is.EqualTo(CharacterGender.Female));
            Assert.That(visual.Model.name, Does.Contain("CHR_Base_Female"),
                "the approved female model, not the male one with a different name");

            Animator animator = visual.Animator;

            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman, Is.True,
                "a non-humanoid rig cannot retarget the shared clips");
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);

            // The mapping this rig has previously lost. Checked on the imported asset, which
            // is what the instance above was built from.
            var importer = UnityEditor.AssetImporter.GetAtPath(
                "Assets/_Game/Art/Characters/Production/Female/CHR_Base_Female_LOD0.fbx")
                as UnityEditor.ModelImporter;

            Assert.That(importer, Is.Not.Null);

            var mapped = false;

            foreach (HumanBone bone in importer.humanDescription.human)
            {
                if (bone.humanName != "Chest") continue;

                mapped = true;

                Assert.That(bone.boneName, Is.EqualTo("chest"),
                    "the explicit Chest mapping has been remapped");
            }

            Assert.That(mapped, Is.True, "Chest is not mapped at all any more");
        }

        // ---- composition ---------------------------------------------------------------------------------

        /// <summary>
        /// The presentation a real client runs: the binder, the screens and the camera.
        /// </summary>
        /// <remarks>Composed exactly as <c>GameWorld</c> composes it, so what is under test
        /// is the shipped wiring rather than an arrangement invented here.</remarks>
        private WorldCameraDirector Director(NetworkManager client)
        {
            if (_directors.TryGetValue(client, out WorldCameraDirector existing))
            {
                return existing;
            }

            var host = new GameObject(client.name + " Presentation");
            _created.Add(host);

            var director = host.AddComponent<WorldCameraDirector>();

            director.Compose(UnityEditor.AssetDatabase.LoadAssetAtPath<ProtoCameraSettings>(
                CameraSettingsPath));

            var hud = new GameObject(client.name + " HUD")
                .AddComponent<ChibiFantasy.Client.UI.WorldHudScreen>();
            var bag = new GameObject(client.name + " Bag")
                .AddComponent<ChibiFantasy.Client.UI.InventoryScreen>();

            _created.Add(hud.gameObject);
            _created.Add(bag.gameObject);

            var binder = host.AddComponent<ChibiFantasy.Client.UI.WorldPresentationBinder>();

            binder.Compose(client, hud, bag, new DefinitionRegistry<ItemDefinition>(),
                director);

            _directors[client] = director;

            return director;
        }

        private readonly Dictionary<NetworkManager, WorldCameraDirector> _directors =
            new Dictionary<NetworkManager, WorldCameraDirector>();

        private static int CamerasUnder(WorldCameraDirector director)
        {
            return director.GetComponentsInChildren<Camera>(true).Length;
        }

        // ---- harness ---------------------------------------------------------------------------------------

        private NetworkManager BuildManager(string name, bool listening, out GameObject host)
        {
            host = new GameObject(name);
            host.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            NetworkManager manager = host.AddComponent<NetworkManager>();

            manager.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            var transport = host.AddComponent<Tugboat>();
            transport.SetPort(_port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        private static NetworkObject Prefab(string path)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, "no prefab at " + path);

            return prefab.GetComponent<NetworkObject>();
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndOneClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_clientA.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientA.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);

            Director(_clientA);
        }

        private IEnumerator StartSecondClient()
        {
            Assert.That(_clientB.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientB.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 2);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(2));

            Director(_clientB);
        }

        private List<int> Connections()
        {
            var ids = new List<int>();

            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                ids.Add(pair.Key);
            }

            ids.Sort();

            return ids;
        }

        private LivingCharacter EnterWorld(string character, int connection,
            CharacterGender gender, string name)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), name, (int)gender, 5, 0, 100, 50,
                    new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                    default, null, null, null, 1);
            }

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static CharacterNetworkEntity Entity(NetworkManager client, string character)
        {
            foreach (KeyValuePair<int, NetworkObject> pair in client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null && entity.Character.Value == character) return entity;
            }

            return null;
        }

        private static CharacterVisualPresenter Visual(NetworkManager client, string character)
        {
            CharacterNetworkEntity entity = Entity(client, character);

            return entity == null ? null : entity.GetComponent<CharacterVisualPresenter>();
        }

        // ---- fixtures ---------------------------------------------------------------------------------------

        private MapDefinition Map(string id)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_movementRadius\":500}", map);

            _created.Add(map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }
    }
}

#endif
