// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Two players' bags, over one real socket, each private to its owner.
    /// </summary>
    /// <remarks>
    /// <b>Inventory is the one system where "it replicated" is not enough.</b> A bag is
    /// private: replicating it correctly and to the wrong person is worse than not
    /// replicating it at all. So these run two real clients and check both halves -- that
    /// each sees its own, and that neither can see or touch the other's.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite. What is real here: the sockets, the ownership, the RPCs, the existing container
    /// and equipment services, the persistence round trip and the replication back.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterInventoryNetworkTests
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

                // What a real store does: the next load returns what the last save wrote.
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
        private MonsterLootRegistry _loot;

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(42100, 46000);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("InvServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("InvClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("InvClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            _items = new DefinitionRegistry<ItemDefinition>();
            _items.Register(Equipment(Sword, EquipmentSlot.MainHand));
            _items.Register(Equipment(Helm, EquipmentSlot.Head));
            _items.Register(Stackable(Coin));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, _items, Capacity);

            _loot = new MonsterLootRegistry(_players, _items);

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

        // ---- harness ------------------------------------------------------------------------

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

        private static InventoryItemSnapshot? Find(InventorySnapshot snapshot,
            string instanceId)
        {
            if (snapshot.Items == null) return null;

            for (int i = 0; i < snapshot.Items.Length; i++)
            {
                if (snapshot.Items[i].InstanceId == instanceId) return snapshot.Items[i];
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

        // ---- A: the client is told what it owns -------------------------------------------------

        [UnityTest]
        public IEnumerator AClientReceivesItsOwnInventoryExactly()
        {
            yield return StartServerAndOneClient();

            EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0, enhancement: 5, rarity: "rarity.rare"),
                Row("item-2", Coin, 3, quantity: 40),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 2);

            InventorySnapshot snapshot = SeenBy(_clientA, "char-a").Inventory;

            Assert.That(snapshot.CharacterId, Is.EqualTo("char-a"));
            Assert.That(snapshot.Capacity, Is.EqualTo(Capacity));

            InventoryItemSnapshot sword = Find(snapshot, "item-1").Value;

            Assert.That(sword.DefinitionId, Is.EqualTo(Sword));
            Assert.That(sword.Slot, Is.EqualTo(0));
            Assert.That(sword.Quantity, Is.EqualTo(1));
            Assert.That(sword.EnhancementLevel, Is.EqualTo(5),
                "per-copy state reaches the client, or the UI cannot draw a +5 sword");
            Assert.That(sword.RarityId, Is.EqualTo("rarity.rare"));
            Assert.That(sword.EnchantCount, Is.EqualTo(1));

            InventoryItemSnapshot coins = Find(snapshot, "item-2").Value;

            Assert.That(coins.Quantity, Is.EqualTo(40));
            Assert.That(coins.Slot, Is.EqualTo(3));
        }

        // ---- B and C: equipment ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator AlreadyWornEquipmentArrivesInItsSlot()
        {
            yield return StartServerAndOneClient();

            EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, -1, enhancement: 9,
                    equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            InventoryItemSnapshot worn = Find(SeenBy(_clientA, "char-a").Inventory,
                "item-1").Value;

            Assert.That(worn.IsEquipped, Is.True);
            Assert.That(worn.EquipmentSlot, Is.EqualTo((int)EquipmentSlot.MainHand));
            Assert.That(worn.Slot, Is.LessThan(0), "a worn piece is in no bag slot");
            Assert.That(worn.EnhancementLevel, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator AClientEquipRequestMovesThePieceAndTheClientSeesBothChanges()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0, enhancement: 3, rarity: "rarity.rare"),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            CharacterNetworkEntity entity = SeenBy(_clientA, "char-a");

            Assert.That(Find(entity.Inventory, "item-1").Value.IsEquipped, Is.False,
                "precondition: in the bag");

            entity.RequestInventoryAction(InventoryAction.Equip, 0, 0, 0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());

            // The authoritative side moved.
            Assert.That(character.Inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(character.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance worn), Is.True);
            Assert.That(worn.InstanceId.Value, Is.EqualTo("item-1"));

            // And the client was told, in one replacement snapshot.
            yield return Until(() =>
            {
                InventoryItemSnapshot? item = Find(SeenBy(_clientA, "char-a").Inventory,
                    "item-1");

                return item.HasValue && item.Value.IsEquipped;
            });

            InventoryItemSnapshot replicated = Find(SeenBy(_clientA, "char-a").Inventory,
                "item-1").Value;

            Assert.That(replicated.EquipmentSlot, Is.EqualTo((int)EquipmentSlot.MainHand));
            Assert.That(replicated.EnhancementLevel, Is.EqualTo(3),
                "the same object, with everything it was carrying");
            Assert.That(replicated.RarityId, Is.EqualTo("rarity.rare"));
        }

        // ---- D: unequip -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientUnequipRequestPutsThePieceBackInTheBag()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, -1, enhancement: 2,
                    equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            SeenBy(_clientA, "char-a").RequestInventoryAction(InventoryAction.Unequip,
                (int)EquipmentSlot.MainHand, 0, 0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.False);

            yield return Until(() =>
            {
                InventoryItemSnapshot? item = Find(SeenBy(_clientA, "char-a").Inventory,
                    "item-1");

                return item.HasValue && !item.Value.IsEquipped;
            });

            InventoryItemSnapshot bagged = Find(SeenBy(_clientA, "char-a").Inventory,
                "item-1").Value;

            Assert.That(bagged.Slot, Is.GreaterThanOrEqualTo(0));
            Assert.That(bagged.EnhancementLevel, Is.EqualTo(2));
        }

        // ---- E and F: move and split -------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientMoveRequestIsResolvedByTheServer()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            SeenBy(_clientA, "char-a").RequestInventoryAction(InventoryAction.Move, 0, 5,
                0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(character.Inventory.GetSlot(5).InstanceId.Value,
                Is.EqualTo("item-1"));

            yield return Until(() =>
            {
                InventoryItemSnapshot? item = Find(SeenBy(_clientA, "char-a").Inventory,
                    "item-1");

                return item.HasValue && item.Value.Slot == 5;
            });

            Assert.That(Find(SeenBy(_clientA, "char-a").Inventory, "item-1").Value.Slot,
                Is.EqualTo(5), "the client sees the slot the server chose");
        }

        [UnityTest]
        public IEnumerator AClientSplitRequestFollowsTheExistingContainerRules()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Coin, 0, quantity: 20),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            SeenBy(_clientA, "char-a").RequestInventoryAction(InventoryAction.Split, 0, 4,
                8, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Inventory.CountOf(new DefinitionId(Coin)),
                Is.EqualTo(20), "a split moves quantity, it never creates any");

            yield return Until(() => SeenBy(_clientA, "char-a").Inventory.Count == 2);

            InventorySnapshot snapshot = SeenBy(_clientA, "char-a").Inventory;

            var total = 0;

            foreach (InventoryItemSnapshot item in snapshot.Items) total += item.Quantity;

            Assert.That(total, Is.EqualTo(20),
                "and the client is told the exact result, not a guess");
        }

        // ---- G: loot pickup ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APickedUpItemAppearsInTheClientsInventoryWithItsOwnIdentity()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null);

            Assert.That(SeenBy(_clientA, "char-a").Inventory.Count, Is.Zero,
                "precondition: an empty bag");

            // 17.15's pile, unchanged, with the killer as its owner.
            var pile = new LootObjectState(InstanceId.New(), InstanceId.New(), default,
                new[] { new LootResult(InstanceId.New(), new DefinitionId(Coin), 7) },
                LootPolicy.OwnerOnly, character.Character);

            Assert.That(_loot.Add(pile, new DefinitionId(HomeMap)), Is.True);

            LootPickupOutcome taken = _loot.Pickup(pile.LootId, 0, character.Character);

            Assert.That(taken.IsAccepted, Is.True, taken.ToString());

            // The pickup mutated the authoritative bag; the snapshot follows it.
            _inventory.Publish(character);

            yield return Until(() => SeenBy(_clientA, "char-a").Inventory.Count == 1);

            InventoryItemSnapshot item = SeenBy(_clientA, "char-a").Inventory.Items[0];

            Assert.That(item.DefinitionId, Is.EqualTo(Coin));
            Assert.That(item.Quantity, Is.EqualTo(7));

            int slot = character.Inventory.IndexOf(new InstanceId(item.InstanceId));

            Assert.That(slot, Is.GreaterThanOrEqualTo(0),
                "the client's item id must be the one the server actually holds");
        }

        // ---- H: refusal --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ARefusedRequestChangesNothingOnEitherSide()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            int revisionBefore = SeenBy(_clientA, "char-a").Inventory.Revision;

            // A slot that does not exist.
            SeenBy(_clientA, "char-a").RequestInventoryAction(InventoryAction.Move, 0, 999,
                0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.False);
            Assert.That(character.Inventory.GetSlot(0).InstanceId.Value,
                Is.EqualTo("item-1"), "the bag is untouched");

            yield return null;
            yield return null;

            Assert.That(SeenBy(_clientA, "char-a").Inventory.Revision,
                Is.EqualTo(revisionBefore),
                "nothing changed, so nothing was sent -- and the client shows the truth");
            Assert.That(Find(SeenBy(_clientA, "char-a").Inventory, "item-1").Value.Slot,
                Is.Zero);
        }

        // ---- I: two clients ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NeitherClientCanSeeOrTouchTheOthersBag()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter a = EnterWorld("char-a", connections[0], new[]
            {
                Row("item-a", Sword, 0),
            });

            LivingCharacter b = EnterWorld("char-b", connections[1], new[]
            {
                Row("item-b", Helm, 0),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientB, "char-b") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1
                && SeenBy(_clientB, "char-b").Inventory.Count == 1);

            // Each sees its own.
            Assert.That(Find(SeenBy(_clientA, "char-a").Inventory, "item-a").HasValue,
                Is.True);
            Assert.That(Find(SeenBy(_clientB, "char-b").Inventory, "item-b").HasValue,
                Is.True);

            // Neither sees the other's, even though both objects are observed.
            CharacterNetworkEntity bOnA = SeenBy(_clientA, "char-b");
            CharacterNetworkEntity aOnB = SeenBy(_clientB, "char-a");

            Assert.That(bOnA, Is.Not.Null, "the character objects are observed by both");
            Assert.That(bOnA.Inventory.Count, Is.Zero,
                "a bag is private: an observer is told nothing about it");
            Assert.That(aOnB.Inventory.Count, Is.Zero);

            // And B cannot act through A's object: FishNet refuses a request from a
            // connection that does not own it.
            int handledBefore = _inventory.Handled;

            LogAssert.ignoreFailingMessages = true;

            try
            {
                aOnB.RequestInventoryAction(InventoryAction.Move, 0, 5, 0, 1);
            }
            catch (System.Exception)
            {
                // Refused is the correct outcome.
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            yield return null;
            yield return null;

            Assert.That(_inventory.Handled, Is.EqualTo(handledBefore),
                "the request never reached the authority");
            Assert.That(a.Inventory.GetSlot(0).InstanceId.Value, Is.EqualTo("item-a"),
                "and A's bag is exactly as it was");
            Assert.That(b.Inventory.GetSlot(0).InstanceId.Value, Is.EqualTo("item-b"));
        }

        // ---- J: replay -----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADuplicateRequestDoesNotActTwice()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 1);

            CharacterNetworkEntity entity = SeenBy(_clientA, "char-a");

            entity.RequestInventoryAction(InventoryAction.Move, 0, 5, 0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(character.Inventory.GetSlot(5).IsOccupied, Is.True);

            // The same sequence again, asking for a different move.
            entity.RequestInventoryAction(InventoryAction.Move, 5, 2, 0, 1);

            yield return Until(() => _inventory.Handled > 1);

            Assert.That(_inventory.LastResult.Rejection,
                Is.EqualTo(InventoryRequestRejection.OutOfOrder));
            Assert.That(character.Inventory.GetSlot(5).IsOccupied, Is.True,
                "the replayed request moved nothing");
            Assert.That(character.Inventory.GetSlot(2).IsEmpty, Is.True);
        }

        // ---- K: reconnect ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EquipmentAndItsUpgradesSurviveALeaveAndReturn()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            LivingCharacter character = EnterWorld("char-a", connection, new[]
            {
                Row("item-1", Sword, 0, enhancement: 6, rarity: "rarity.epic"),
                Row("item-2", Coin, 2, quantity: 15),
            });

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 2);

            // Wear it, over the network.
            SeenBy(_clientA, "char-a").RequestInventoryAction(InventoryAction.Equip, 0, 0,
                0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True);

            // The existing lifecycle writes it.
            Assert.That(_players.Despawn(connection).IsOk, Is.True);
            Assert.That(_store.Saves, Is.GreaterThan(0), "leaving wrote the character");

            _replication.Synchronise();

            yield return Until(() => SeenBy(_clientA, "char-a") == null);

            // Back again. What the database holds is what they get.
            LivingCharacter returned = EnterWorld("char-a", connection);

            Assert.That(returned.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance worn), Is.True, "the sword came back worn");
            Assert.That(worn.InstanceId.Value, Is.EqualTo("item-1"),
                "with the identity it always had");
            Assert.That(worn.EnhancementLevel, Is.EqualTo(6),
                "and the upgrade the player paid for");
            Assert.That(worn.Rarity.Value, Is.EqualTo("rarity.epic"));
            Assert.That(worn.EnchantCount, Is.EqualTo(1), "and the stone in it");

            Assert.That(returned.Inventory.CountOf(new DefinitionId(Coin)),
                Is.EqualTo(15), "and the coins, in their slot");

            PublishAll();

            yield return Until(() => SeenBy(_clientA, "char-a") != null
                && SeenBy(_clientA, "char-a").Inventory.Count == 2);

            InventoryItemSnapshot replicated = Find(SeenBy(_clientA, "char-a").Inventory,
                "item-1").Value;

            Assert.That(replicated.IsEquipped, Is.True);
            Assert.That(replicated.EnhancementLevel, Is.EqualTo(6));
            Assert.That(replicated.RarityId, Is.EqualTo("rarity.epic"));
        }

        // ---- fixtures ---------------------------------------------------------------------------------

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
