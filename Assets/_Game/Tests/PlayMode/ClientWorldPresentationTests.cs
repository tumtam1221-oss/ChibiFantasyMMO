// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using ChibiFantasy.UI;
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
    /// The real world screens, driven by a real server over a real socket.
    /// </summary>
    /// <remarks>
    /// <b>The screens are the thing under test, not a stand-in for them.</b> These are the
    /// production <see cref="WorldHudScreen"/> and <see cref="InventoryScreen"/> components
    /// with their canvases built, bound by the production
    /// <see cref="WorldPresentationBinder"/>, and driven through the methods their buttons
    /// call. Nothing here reaches past the presentation layer to make something happen: an
    /// equip is a click, and whether it worked is the next snapshot's answer.
    ///
    /// <b>Privacy is checked from the wrong side on purpose.</b> It is not enough that the
    /// server declines to send B's bag to A. The test that matters is that A's panel, given
    /// every object A can see, still draws only A's items -- because that is the failure a
    /// player would actually experience.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite. What is real here: the sockets, the ownership, the RPCs, the replication, the
    /// existing container and equipment services, and the screens themselves.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientWorldPresentationTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string HomeMap = "map.home";
        private const string Sword = "item.sword";
        private const string Helm = "item.helm";
        private const string Coin = "item.coin";
        private const int Capacity = 8;

        /// <summary>A store that keeps what it was given, so a reconnect reads it back.</summary>
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public int Saves;

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Saves++;

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
        private DefinitionRegistry<ItemDefinition> _items;
        private CharacterInventoryAuthority _inventory;
        private CharacterReplicationService _replication;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(46100, 49000);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("UiServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("UiClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("UiClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            _items = new DefinitionRegistry<ItemDefinition>();
            _items.Register(Equipment(Sword, EquipmentSlot.MainHand));
            _items.Register(Equipment(Helm, EquipmentSlot.Head));
            _items.Register(Stackable(Coin));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, _items, Capacity);

            _replication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath));

            _inventory = new CharacterInventoryAuthority(_players, _ => true, _items,
                _replication);

            _replication.UseInventory(_inventory);
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

        // ---- E: entering the world binds the HUD -------------------------------------------------

        [UnityTest]
        public IEnumerator EnteringTheWorldBindsTheHudToTheCharacterThisClientOwns()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            Assert.That(a.Hud.IsBound, Is.False, "precondition: nothing to show yet");
            Assert.That(a.Hud.Current.IsBound, Is.False,
                "and the bars are hidden rather than reading zero");

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            PublishAll();

            yield return Until(() => a.Hud.IsBound);

            Assert.That(a.Binder.Bound, Is.Not.Null);
            Assert.That(a.Binder.Bound.IsOwner, Is.True,
                "the binder chose the object this connection owns, not the first one it saw");
            Assert.That(a.Binder.BindCount, Is.EqualTo(1));

            yield return Until(() => a.Hud.Current.MaxHealth > 0);

            Assert.That(a.Hud.Current.Character.Value, Is.EqualTo("char-a"));
            Assert.That(a.Hud.Current.MaxHealth,
                Is.EqualTo(character.Combatant.MaxHealth));
            Assert.That(a.Hud.Current.Health, Is.EqualTo(character.Combatant.CurrentHealth));
            Assert.That(a.Hud.Current.Level,
                Is.EqualTo(character.Domain.Progression.Level));

            // The space status effects will occupy exists and is empty, because nothing
            // replicates them yet. Reported as a limitation rather than drawn as a guess.
            Assert.That(a.Hud.StatusEffectAnchor, Is.Not.Null);
            Assert.That(a.Hud.StatusEffectAnchor.childCount, Is.Zero);
        }

        // ---- F: the HUD follows the server ----------------------------------------------------------

        [UnityTest]
        public IEnumerator TheHudFollowsTheServersHealthLevelAndExperience()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            PublishAll();

            yield return Until(() => a.Hud.Current.MaxHealth > 0);

            int startingHealth = a.Hud.Current.Health;

            Assert.That(a.Hud.Current.HealthFraction, Is.EqualTo(1f).Within(0.001f));

            // The server hurts them. Nothing on the client is asked to agree.
            character.Combatant.ApplyHealthDelta(-40);
            _replication.Synchronise();

            yield return Until(() => a.Hud.Current.Health != startingHealth);

            Assert.That(a.Hud.Current.Health,
                Is.EqualTo(character.Combatant.CurrentHealth));
            Assert.That(a.Hud.Current.HealthLabel,
                Is.EqualTo(character.Combatant.CurrentHealth + " / "
                    + character.Combatant.MaxHealth));
            Assert.That(a.Hud.Current.HealthFraction, Is.LessThan(1f));
            Assert.That(a.Hud.Current.IsAlive, Is.True);

            // And the server pays them, through the existing progression state.
            int wasLevel = character.Domain.Progression.Level;

            character.Domain.Progression.AddExperience(250, Curve());
            _replication.Synchronise();

            yield return Until(() => a.Hud.Current.Level != wasLevel);

            Assert.That(character.Domain.Progression.Level, Is.GreaterThan(wasLevel),
                "precondition: the grant actually levelled them");
            Assert.That(a.Hud.Current.Level,
                Is.EqualTo(character.Domain.Progression.Level));
            Assert.That(a.Hud.Current.Experience,
                Is.EqualTo(character.Domain.Progression.Experience));
            Assert.That(a.Hud.Current.LevelLabel,
                Is.EqualTo("Lv " + character.Domain.Progression.Level));
        }

        // ---- G: the panel draws the snapshot, and nothing before it -----------------------------

        [UnityTest]
        public IEnumerator TheInventoryPanelDrawsTheServersSnapshotAndNothingBeforeIt()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            a.Inventory.SetOpen(true);

            Assert.That(a.Inventory.StatusMessage, Is.EqualTo("Waiting for server state"),
                "an empty grid would be a claim about a bag the client has not been sent");

            EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0, enhancement: 5, rarity: "rarity.rare"),
                Row("item-2", Coin, 3, quantity: 40),
                Row("item-3", Helm, -1, equipmentSlot: (int)EquipmentSlot.Head),
            });

            PublishAll();

            yield return Until(() => a.Inventory.Presenter != null
                && a.Inventory.Presenter.HasSnapshot);

            Assert.That(a.Inventory.StatusMessage, Is.Empty);
            Assert.That(a.Inventory.Presenter.Character.Value, Is.EqualTo("char-a"));
            Assert.That(a.Inventory.Presenter.Bag.Count, Is.EqualTo(Capacity),
                "the grid is the capacity the server reported, not a guess");

            IReadOnlyList<ItemSlotViewData> bag = a.Inventory.Presenter.Bag;

            Assert.That(bag[0].IsOccupied, Is.True);
            Assert.That(bag[0].DefinitionId.Value, Is.EqualTo(Sword));
            Assert.That(bag[0].InstanceId.Value, Is.EqualTo("item-1"),
                "the same object the server named, not a copy the client invented");

            Assert.That(bag[3].IsOccupied, Is.True);
            Assert.That(bag[3].Quantity, Is.EqualTo(40));
            Assert.That(bag[3].ShowQuantity, Is.True);

            Assert.That(bag[1].IsEmpty, Is.True, "and the rest is empty, not absent");

            // The worn piece is on the paperdoll and out of the bag.
            Assert.That(Worn(a, EquipmentSlot.Head).IsOccupied, Is.True);
            Assert.That(Worn(a, EquipmentSlot.Head).InstanceId.Value, Is.EqualTo("item-3"));
            Assert.That(Worn(a, EquipmentSlot.MainHand).IsOccupied, Is.False);

            for (int i = 0; i < bag.Count; i++)
            {
                Assert.That(bag[i].InstanceId.Value, Is.Not.EqualTo("item-3"),
                    "a worn piece is not also in the bag");
            }

            // Selecting is presentation and reaches no server.
            a.Inventory.SelectBagSlot(0);

            Assert.That(a.Inventory.SelectedBagSlot, Is.Zero);
            Assert.That(_inventory.Handled, Is.Zero, "looking at an item asked for nothing");
        }

        // ---- H: an equip is a click, and the answer is a snapshot -------------------------------

        [UnityTest]
        public IEnumerator EquippingThroughTheRealPanelGoesThroughTheServerAndComesBack()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0, enhancement: 3, rarity: "rarity.rare"),
            });

            PublishAll();

            yield return Until(() => a.Inventory.Presenter != null
                && a.Inventory.Presenter.HasSnapshot);

            Assert.That(a.Inventory.Presenter.Bag[0].IsOccupied, Is.True, "precondition");
            Assert.That(Worn(a, EquipmentSlot.MainHand).IsOccupied, Is.False);

            // Nothing selected: the button does nothing rather than guessing a slot.
            Assert.That(a.Inventory.Equip(), Is.False);
            Assert.That(_inventory.Handled, Is.Zero);

            a.Inventory.SelectBagSlot(0);

            Assert.That(a.Inventory.Equip(), Is.True, "the request went out");

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());

            // The server moved it. That is the only thing that moved it.
            Assert.That(character.Inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(character.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance worn), Is.True);
            Assert.That(worn.InstanceId.Value, Is.EqualTo("item-1"));

            yield return Until(() => Worn(a, EquipmentSlot.MainHand).IsOccupied);

            Assert.That(Worn(a, EquipmentSlot.MainHand).InstanceId.Value,
                Is.EqualTo("item-1"), "the same object, on the paperdoll");
            Assert.That(a.Inventory.Presenter.Bag[0].IsEmpty, Is.True,
                "and gone from the square it was in");
        }

        // ---- I: and back off again -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator UnequippingThroughTheRealPanelPutsThePieceBackInTheBag()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, -1, enhancement: 2,
                    equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            PublishAll();

            yield return Until(() => a.Inventory.Presenter != null
                && Worn(a, EquipmentSlot.MainHand).IsOccupied);

            Assert.That(a.Inventory.Unequip(), Is.False, "nothing selected, nothing sent");

            a.Inventory.SelectEquipmentSlot(EquipmentSlot.MainHand);

            Assert.That(a.Inventory.SelectedBagSlot, Is.LessThan(0),
                "picking a worn slot drops the bag selection, so one click means one thing");
            Assert.That(a.Inventory.Unequip(), Is.True);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.False);

            yield return Until(() => !Worn(a, EquipmentSlot.MainHand).IsOccupied);

            var found = false;

            for (int i = 0; i < a.Inventory.Presenter.Bag.Count; i++)
            {
                if (a.Inventory.Presenter.Bag[i].InstanceId.Value != "item-1") continue;

                found = true;

                Assert.That(a.Inventory.Presenter.Bag[i].DefinitionId.Value,
                    Is.EqualTo(Sword), "the same piece, not a fresh one of the same kind");
                Assert.That(a.Inventory.Presenter.Bag[i].IsEquipment, Is.True);
            }

            Assert.That(found, Is.True, "the piece is back in a square the server chose");

            // The snapshot still carries the +2, but ItemSlotViewData has no field for it,
            // so the square cannot draw it. A pre-existing limit of the view data rather
            // than of the replication -- reported, not papered over.
            Assert.That(a.Binder.Bound.Inventory.Items[0].EnhancementLevel, Is.EqualTo(2),
                "the client was told, even though the square has nowhere to show it");
        }

        // ---- J: one player's panel never draws another player's bag --------------------------------

        [UnityTest]
        public IEnumerator OneClientsPanelNeverDrawsAnotherPlayersBag()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            Screens a = BuildScreens(_clientA);
            Screens b = BuildScreens(_clientB);

            List<int> connections = Connections();

            EnterWorld("char-a", connections[0], new[] { Row("item-a", Sword, 0) });
            EnterWorld("char-b", connections[1], new[] { Row("item-b", Helm, 0) });

            PublishAll();

            yield return Until(() => a.Inventory.Presenter != null
                && a.Inventory.Presenter.HasSnapshot
                && b.Inventory.Presenter != null
                && b.Inventory.Presenter.HasSnapshot);

            // Both clients can see both characters -- this is not a visibility trick.
            yield return Until(() => _clientA.ClientManager.Objects.Spawned.Count >= 2
                && _clientB.ClientManager.Objects.Spawned.Count >= 2);

            Assert.That(a.Binder.Bound.Character.Value, Is.EqualTo("char-a"));
            Assert.That(b.Binder.Bound.Character.Value, Is.EqualTo("char-b"));

            Assert.That(a.Inventory.Presenter.Character.Value, Is.EqualTo("char-a"));
            Assert.That(b.Inventory.Presenter.Character.Value, Is.EqualTo("char-b"));

            AssertBagHolds(a, "item-a", "item-b");
            AssertBagHolds(b, "item-b", "item-a");

            // The HUD is just as private about it.
            Assert.That(a.Hud.Current.Character.Value, Is.EqualTo("char-a"));
            Assert.That(b.Hud.Current.Character.Value, Is.EqualTo("char-b"));

            // And the other player's object, offered directly, is refused rather than drawn.
            CharacterNetworkEntity foreign = SeenBy(_clientA, "char-b");

            Assert.That(foreign, Is.Not.Null, "precondition: A can see B's character");
            Assert.That(foreign.IsOwner, Is.False);
            Assert.That(foreign.Inventory.Count, Is.Zero,
                "the server never sent B's bag to A, so there is nothing to draw");

            Assert.That(a.Hud.Bind(foreign), Is.False,
                "and a screen asked to draw it says no on its own account");
            Assert.That(a.Inventory.Bind(foreign, _items), Is.False);
            Assert.That(a.Inventory.Presenter.HasSnapshot, Is.False);
        }

        // ---- K: leaving and coming back ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReconnectRebindsTheScreensToTheNewCharacterObject()
        {
            yield return StartServerAndOneClient();

            Screens a = BuildScreens(_clientA);

            int connection = Connections()[0];

            EnterWorld("char-a", connection, new[]
            {
                Row("item-1", Sword, 0, enhancement: 6, rarity: "rarity.epic"),
            });

            PublishAll();

            yield return Until(() => a.Hud.IsBound
                && a.Inventory.Presenter != null && a.Inventory.Presenter.HasSnapshot);

            Assert.That(a.Binder.BindCount, Is.EqualTo(1));

            a.Inventory.SetOpen(true);

            // They leave, through the existing lifecycle.
            Assert.That(_players.Despawn(connection).IsOk, Is.True);

            _replication.Synchronise();

            yield return Until(() => a.Binder.Bound == null);

            Assert.That(a.Hud.IsBound, Is.False,
                "a HUD still holding a destroyed object is where the null references come from");
            Assert.That(a.Hud.Current.IsBound, Is.False);
            Assert.That(a.Inventory.Presenter, Is.Null);
            Assert.That(a.Inventory.IsOpen, Is.False,
                "and the bag closes rather than sitting there showing a character who left");

            // And they come back.
            LivingCharacter returned = EnterWorld("char-a", connection);

            PublishAll();

            yield return Until(() => a.Hud.IsBound
                && a.Inventory.Presenter != null && a.Inventory.Presenter.HasSnapshot);

            Assert.That(a.Binder.BindCount, Is.EqualTo(2),
                "rebinding is the normal case, not an error path");
            Assert.That(a.Binder.Bound.IsOwner, Is.True);
            Assert.That(a.Hud.Current.Character.Value, Is.EqualTo("char-a"));

            yield return Until(() => a.Hud.Current.MaxHealth > 0);

            Assert.That(a.Hud.Current.Health,
                Is.EqualTo(returned.Combatant.CurrentHealth));

            // What the database holds is what the panel draws.
            Assert.That(returned.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance _), Is.False, "precondition: it was never worn");
            Assert.That(FindBag(a, "item-1").HasValue, Is.True,
                "the same object came back, with its identity");
            Assert.That(FindBag(a, "item-1").Value.DefinitionId.Value, Is.EqualTo(Sword));
        }

        // ---- the screens, composed the way production composes them -----------------------------

        /// <summary>The three production components a world client actually runs.</summary>
        private sealed class Screens
        {
            public WorldHudScreen Hud;
            public InventoryScreen Inventory;
            public WorldPresentationBinder Binder;
        }

        private Screens BuildScreens(NetworkManager client)
        {
            var host = new GameObject(client.name + " UI");
            _created.Add(host);

            var screens = new Screens
            {
                Hud = new GameObject(client.name + " HUD").AddComponent<WorldHudScreen>(),
                Inventory = new GameObject(client.name + " Bag")
                    .AddComponent<InventoryScreen>(),
            };

            _created.Add(screens.Hud.gameObject);
            _created.Add(screens.Inventory.gameObject);

            screens.Binder = host.AddComponent<WorldPresentationBinder>();

            screens.Binder.Compose(client, screens.Hud, screens.Inventory, _items);

            return screens;
        }

        private static EquipmentSlotViewData Worn(Screens screens, EquipmentSlot slot)
        {
            IReadOnlyList<EquipmentSlotViewData> worn = screens.Inventory.Presenter.Worn;

            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].Slot == slot) return worn[i];
            }

            Assert.Fail("the paperdoll has no " + slot + " slot");

            return default;
        }

        private static ItemSlotViewData? FindBag(Screens screens, string instance)
        {
            IReadOnlyList<ItemSlotViewData> bag = screens.Inventory.Presenter.Bag;

            for (int i = 0; i < bag.Count; i++)
            {
                if (bag[i].InstanceId.Value == instance) return bag[i];
            }

            return null;
        }

        private static void AssertBagHolds(Screens screens, string mine, string theirs)
        {
            Assert.That(FindBag(screens, mine).HasValue, Is.True,
                "the player's own item is missing from their own bag");
            Assert.That(FindBag(screens, theirs).HasValue, Is.False,
                "another player's item appeared in this bag");
        }

        // ---- harness --------------------------------------------------------------------------------

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
        }

        private IEnumerator StartSecondClient()
        {
            Assert.That(_clientB.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientB.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 2);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(2));
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
            PersistedItem[] items = null)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 2, 20, 0, 100, 50,
                    new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                    default, null, null, null, 1, items, Capacity);
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

        /// <summary>Spawns the objects and pushes each owner their first snapshot.</summary>
        private void PublishAll()
        {
            _replication.Synchronise();

            IReadOnlyList<LivingCharacter> all = _players.All();

            for (int i = 0; i < all.Count; i++) _inventory.Publish(all[i]);
        }

        private static CharacterNetworkEntity SeenBy(NetworkManager client, string character)
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

        private static PersistedItem Row(string instance, string item, int slot,
            int quantity = 1, int enhancement = 0, string rarity = null,
            int equipmentSlot = 0)
        {
            return new PersistedItem(new InstanceId(instance), new DefinitionId(item),
                quantity, slot, 0, equipmentSlot, enhancement,
                rarity == null ? default : new DefinitionId(rarity),
                enhancement > 0
                    ? new[] { new PersistedEnchant(new DefinitionId("stone.fire"), 0, 2) }
                    : null,
                null);
        }

        // ---- fixtures ---------------------------------------------------------------------------------

        /// <summary>A flat curve that reaches past the fixture character's level.</summary>
        /// <remarks>The persisted row starts them at 20, so a curve capped at 20 would grant
        /// experience that could never become a level and the test would prove nothing.</remarks>
        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.ui\"},\"_minLevel\":1,\"_maxLevel\":30,"
                + "\"_experienceToNextLevel\":[100,100,100,100,100,100,100,100,100,100,"
                + "100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,"
                + "100,100]}", definition);

            _created.Add(definition);

            return definition;
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

        private ItemDefinition Stackable(string id)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":true,"
                + "\"_maxStackSize\":999}", definition);

            _created.Add(definition);

            return definition;
        }

        private EquipmentDefinition Equipment(string id, EquipmentSlot slot)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":false,"
                + "\"_maxStackSize\":1,\"_slot\":" + (int)slot
                + ",\"_levelRequirement\":1,\"_statusStoneSlots\":2,\"_cardSlots\":2}",
                definition);

            _created.Add(definition);

            return definition;
        }
    }
}

#endif
