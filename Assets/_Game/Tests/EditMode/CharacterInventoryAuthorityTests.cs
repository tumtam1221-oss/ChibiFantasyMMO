using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a client may ask about its own belongings, and what it can never say.
    /// </summary>
    /// <remarks>
    /// <b>The rules are not here.</b> Whether a stack merges, whether a bag has room,
    /// whether a level-five character may wear a level-sixty sword -- all of that belongs to
    /// <c>ItemContainerState</c> and <c>EquipmentService</c>, which already existed and are
    /// unchanged. What is tested here is the layer above: that a request names slots and
    /// never results, that the character comes from the connection, that a refusal changes
    /// nothing, and that the snapshot a client receives is the server's own state with the
    /// server's own item identities.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterInventoryAuthorityTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public readonly List<PersistedCharacter> Saved = new List<PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Saved.Add(c);

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private const string Sword = "item.sword";
        private const string Helm = "item.helm";
        private const string HighSword = "item.sword.high";
        private const int Connection = 5;
        private const int Capacity = 6;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterInventoryAuthority _inventory;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpInventory()
        {
            AddEquipment(Sword, EquipmentSlot.MainHand, level: 1);
            AddEquipment(Helm, EquipmentSlot.Head, level: 1);
            AddEquipment(HighSword, EquipmentSlot.MainHand, level: 60);

            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, Capacity);

            _inventory = new CharacterInventoryAuthority(_players, _ => true, Items);
        }

        [TearDown]
        public void TearDownInventory()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private EquipmentDefinition AddEquipment(string id, EquipmentSlot slot, int level)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + "\"},"
                + "\"_stackable\":false,\"_maxStackSize\":1,\"_slot\":" + (int)slot
                + ",\"_levelRequirement\":" + level + "}", definition);

            _local.Add(definition);
            Items.Register(definition);

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

        private LivingCharacter AddPlayer(string character = "char-a",
            int connection = Connection, int level = 5, PersistedItem[] items = null)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, level, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1, items, Capacity);

            WorldSpawnResult result = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(HomeMap),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        /// <summary>A bag row for a piece of equipment with per-copy state on it.</summary>
        private static PersistedItem Row(string instance, string item, int slot,
            int enhancement = 0, string rarity = null, int equipmentSlot = 0)
        {
            return new PersistedItem(new InstanceId(instance), new DefinitionId(item), 1,
                slot, 0, equipmentSlot, enhancement,
                rarity == null ? default : new DefinitionId(rarity),
                enhancement > 0
                    ? new[] { new PersistedEnchant(new DefinitionId("stone.fire"), 0, 3) }
                    : null,
                null);
        }

        // ---- the snapshot ------------------------------------------------------------------

        [Test]
        public void TheSnapshotCarriesTheServersOwnItemIdentity()
        {
            LivingCharacter character = AddPlayer(items: new[]
            {
                Row("item-1", Sword, 0),
                Row("item-2", Helm, 3),
            });

            InventorySnapshot snapshot = _inventory.SnapshotOf(character);

            Assert.That(snapshot.CharacterId, Is.EqualTo("char-a"));
            Assert.That(snapshot.Capacity, Is.EqualTo(Capacity));
            Assert.That(snapshot.Count, Is.EqualTo(2));

            var ids = new List<string>();

            foreach (InventoryItemSnapshot item in snapshot.Items) ids.Add(item.InstanceId);

            Assert.That(ids, Is.EquivalentTo(new[] { "item-1", "item-2" }),
                "a snapshot must never mint a new identity for an item that already has one");
        }

        [Test]
        public void TheSnapshotKeepsSlotsQuantitiesAndPerCopyState()
        {
            LivingCharacter character = AddPlayer(items: new[]
            {
                Row("item-1", Sword, 2, enhancement: 9, rarity: "rarity.epic"),
            });

            InventoryItemSnapshot item = _inventory.SnapshotOf(character).Items[0];

            Assert.That(item.Slot, Is.EqualTo(2));
            Assert.That(item.Quantity, Is.EqualTo(1));
            Assert.That(item.EnhancementLevel, Is.EqualTo(9));
            Assert.That(item.RarityId, Is.EqualTo("rarity.epic"));
            Assert.That(item.EnchantCount, Is.EqualTo(1));
            Assert.That(item.IsEquipped, Is.False);
        }

        [Test]
        public void AWornPieceIsInTheSnapshotWithItsEquipmentSlotAndNoBagSlot()
        {
            LivingCharacter character = AddPlayer(items: new[]
            {
                Row("item-1", Sword, -1, enhancement: 4, equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            InventorySnapshot snapshot = _inventory.SnapshotOf(character);

            var worn = new List<InventoryItemSnapshot>(snapshot.Worn);

            Assert.That(worn, Has.Count.EqualTo(1));
            Assert.That(worn[0].EquipmentSlot, Is.EqualTo((int)EquipmentSlot.MainHand));
            Assert.That(worn[0].Slot, Is.LessThan(0), "a worn piece is in no bag");
            Assert.That(worn[0].EnhancementLevel, Is.EqualTo(4));
            Assert.That(new List<InventoryItemSnapshot>(snapshot.Bagged), Is.Empty);
        }

        [Test]
        public void EveryChangeAdvancesTheServersRevision()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            int first = _inventory.SnapshotOf(character).Revision;
            int second = _inventory.SnapshotOf(character).Revision;

            Assert.That(second, Is.GreaterThan(first),
                "a client needs to be able to drop a snapshot that arrived late");
        }

        [Test]
        public void TheSnapshotCarriesNothingSecret()
        {
            foreach (System.Reflection.FieldInfo field in
                typeof(InventoryItemSnapshot).GetFields())
            {
                string name = field.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("owner"), field.Name);
                Assert.That(name, Does.Not.Contain("account"), field.Name);
                Assert.That(name, Does.Not.Contain("session"), field.Name);
                Assert.That(name, Does.Not.Contain("token"), field.Name);
            }

            foreach (System.Reflection.FieldInfo field in
                typeof(InventorySnapshot).GetFields())
            {
                Assert.That(field.Name.ToLowerInvariant(), Does.Not.Contain("save"),
                    "the persistence revision is a database concurrency token, not a "
                    + "network one");
            }
        }

        // ---- move, split, merge ---------------------------------------------------------------

        [Test]
        public void AMoveGoesThroughTheExistingContainerAndTheServerDecidesTheResult()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(character.Inventory.GetSlot(4).InstanceId.Value,
                Is.EqualTo("item-1"), "the same item, in the slot the server put it in");
        }

        [Test]
        public void ASplitFollowsTheExistingContainerRules()
        {
            // Coin stacks; the container decides what a split produces.
            LivingCharacter character = AddPlayer();

            character.Inventory.Add(new ItemInstance(new InstanceId("stack-1"),
                new DefinitionId(Coin), character.Owner, 10), Items);

            _inventory.Submit(Connection, InventoryAction.Split, 0, 3, 4, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Inventory.CountOf(new DefinitionId(Coin)),
                Is.EqualTo(10), "a split moves quantity, it does not create any");
            Assert.That(character.Inventory.GetSlot(3).IsOccupied, Is.True);
        }

        [Test]
        public void AnUnsupportedActionIsRefusedAndChangesNothing()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.None, 0, 1, 0, 1);

            Assert.That(_inventory.LastResult.Rejection,
                Is.EqualTo(InventoryRequestRejection.UnsupportedAction));
            Assert.That(character.Inventory.GetSlot(0).InstanceId.Value,
                Is.EqualTo("item-1"));
        }

        [Test]
        public void ARefusedMoveLeavesTheBagExactlyAsItWas()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 99, 0, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.False);
            Assert.That(character.Inventory.GetSlot(0).InstanceId.Value,
                Is.EqualTo("item-1"));
            Assert.That(character.IsDirty, Is.False,
                "a refused request must not even queue a save");
        }

        // ---- equip and unequip ------------------------------------------------------------------

        [Test]
        public void EquippingMovesThePieceOutOfTheBagAndOntoTheCharacter()
        {
            LivingCharacter character = AddPlayer(items: new[]
            {
                Row("item-1", Sword, 0, enhancement: 7, rarity: "rarity.rare"),
            });

            _inventory.Submit(Connection, InventoryAction.Equip, 0, 0, 0, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Inventory.GetSlot(0).IsEmpty, Is.True);

            Assert.That(character.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance worn), Is.True);
            Assert.That(worn.InstanceId.Value, Is.EqualTo("item-1"),
                "the same object, not a copy");
            Assert.That(worn.EnhancementLevel, Is.EqualTo(7),
                "and it kept everything a player paid for");
            Assert.That(worn.Rarity.Value, Is.EqualTo("rarity.rare"));
        }

        [Test]
        public void UnequippingPutsTheSamePieceBackInTheBag()
        {
            LivingCharacter character = AddPlayer(items: new[]
            {
                Row("item-1", Sword, -1, enhancement: 7,
                    equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.True,
                "precondition: it was loaded as worn");

            _inventory.Submit(Connection, InventoryAction.Unequip,
                (int)EquipmentSlot.MainHand, 0, 0, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.False);

            int slot = character.Inventory.IndexOf(new InstanceId("item-1"));

            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "it went back to the bag");
            Assert.That(((EquipmentInstance)character.Inventory.GetSlot(slot).Content)
                .EnhancementLevel, Is.EqualTo(7));
        }

        [Test]
        public void TheExistingLevelGateStillRefusesAPieceTheCharacterCannotWear()
        {
            LivingCharacter character = AddPlayer(level: 5, items: new[]
            {
                Row("item-1", HighSword, 0),
            });

            _inventory.Submit(Connection, InventoryAction.Equip, 0, 0, 0, 1);

            Assert.That(_inventory.LastResult.IsAccepted, Is.False);
            Assert.That(_inventory.LastResult.Equip.Reason,
                Is.EqualTo(EquipRejection.LevelTooLow),
                "the gate is EquipmentService's and is not restated anywhere here");
            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.False);
            Assert.That(character.Inventory.GetSlot(0).IsOccupied, Is.True);
        }

        [Test]
        public void TheLevelGateReadsTheAuthoritativeCharacterAndNotTheRequest()
        {
            // There is nowhere in the request to claim a level, and the context is built
            // from the character the connection resolved to.
            System.Reflection.MethodInfo submit =
                typeof(CharacterInventoryAuthority).GetMethod("Submit");

            foreach (System.Reflection.ParameterInfo parameter in submit.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("level"));
                Assert.That(name, Does.Not.Contain("class"));
                Assert.That(name, Does.Not.Contain("job"));
                Assert.That(name, Does.Not.Contain("enhancement"));
                Assert.That(name, Does.Not.Contain("rarity"));
                Assert.That(name, Does.Not.Contain("character"));
                Assert.That(name, Does.Not.Contain("owner"));
                Assert.That(name, Does.Not.Contain("instance"));
            }
        }

        // ---- authority --------------------------------------------------------------------------

        [Test]
        public void AConnectionWithNoCharacterChangesNothing()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(999, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(_inventory.LastResult.Rejection,
                Is.EqualTo(InventoryRequestRejection.NoCharacter));
            Assert.That(character.Inventory.GetSlot(0).IsOccupied, Is.True);
        }

        [Test]
        public void AStaleConnectionChangesNothing()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            var stale = new CharacterInventoryAuthority(_players, _ => false, Items);

            stale.Submit(Connection, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(stale.LastResult.Rejection,
                Is.EqualTo(InventoryRequestRejection.StaleConnection));
            Assert.That(character.Inventory.GetSlot(0).IsOccupied, Is.True);
        }

        [Test]
        public void OneConnectionCannotTouchAnotherPlayersBag()
        {
            LivingCharacter first = AddPlayer("char-a", Connection,
                items: new[] { Row("item-1", Sword, 0) });

            LivingCharacter second = AddPlayer("char-b", 6,
                items: new[] { Row("item-2", Helm, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(first.Inventory.GetSlot(4).IsOccupied, Is.True);
            Assert.That(second.Inventory.GetSlot(0).InstanceId.Value,
                Is.EqualTo("item-2"), "the other player's bag is untouched");
            Assert.That(second.Inventory.GetSlot(4).IsEmpty, Is.True);
        }

        [Test]
        public void ADuplicateSequenceChangesNothingASecondTime()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(character.Inventory.GetSlot(4).IsOccupied, Is.True);

            _inventory.Submit(Connection, InventoryAction.Move, 4, 2, 0, 1);

            Assert.That(_inventory.LastResult.Rejection,
                Is.EqualTo(InventoryRequestRejection.OutOfOrder));
            Assert.That(character.Inventory.GetSlot(4).IsOccupied, Is.True,
                "the replayed request moved nothing");
        }

        [Test]
        public void ARefusedRequestDoesNotConsumeTheSequence()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 99, 0, 5);

            Assert.That(_inventory.LastResult.IsAccepted, Is.False);

            // The same number again, this time for something legal.
            _inventory.Submit(Connection, InventoryAction.Move, 0, 4, 0, 5);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                "a player whose move was refused must be able to try another");
        }

        [Test]
        public void AnAcceptedRequestQueuesASaveThroughTheExistingLifecycle()
        {
            LivingCharacter character = AddPlayer(items: new[] { Row("item-1", Sword, 0) });

            _inventory.Submit(Connection, InventoryAction.Move, 0, 4, 0, 1);

            Assert.That(character.IsDirty, Is.True);

            Assert.That(_players.Save(character).IsOk, Is.True);
            Assert.That(_store.Saved, Has.Count.EqualTo(1),
                "persistence is the existing registry's, not a second one");
        }

        // ---- structural guards --------------------------------------------------------------------

        [Test]
        public void TheClientRequestNamesSlotsAndNeverAResult()
        {
            System.Reflection.MethodInfo request =
                typeof(CharacterNetworkEntity).GetMethod("RequestInventoryAction");

            Assert.That(request, Is.Not.Null);

            var names = new List<string>();

            foreach (System.Reflection.ParameterInfo parameter in request.GetParameters())
            {
                names.Add(parameter.Name.ToLowerInvariant());
            }

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "action", "from", "to", "quantity", "sequence",
            }));
            Assert.That(request.ReturnType, Is.EqualTo(typeof(void)));
        }

        [Test]
        public void TheRequestIsAServerRpcRequiringOwnership()
        {
            object[] attributes = typeof(CharacterNetworkEntity)
                .GetMethod("RequestInventoryAction")
                .GetCustomAttributes(typeof(FishNet.Object.ServerRpcAttribute), true);

            Assert.That(attributes, Is.Not.Empty);
            Assert.That(((FishNet.Object.ServerRpcAttribute)attributes[0]).RequireOwnership,
                Is.True);
        }

        [Test]
        public void TheSnapshotIsSentToItsOwnerAndNotToObservers()
        {
            System.Reflection.MethodInfo publish = typeof(CharacterNetworkEntity)
                .GetMethod("TargetPublishInventory",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);

            Assert.That(publish, Is.Not.Null, "there is no inventory delivery at all");
            Assert.That(publish.GetCustomAttributes(
                typeof(FishNet.Object.TargetRpcAttribute), true), Is.Not.Empty,
                "an ObserversRpc or a SyncVar would show every player somebody else's bag");
        }

        [Test]
        public void ExactlyOneTypeReceivesClientInventoryRequests()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var sinks = new List<string>();

            foreach (string file in files)
            {
                if (System.IO.File.ReadAllText(file)
                    .Contains(": ICharacterInventoryRequestSink"))
                {
                    sinks.Add(file.Replace('\\', '/'));
                }
            }

            Assert.That(sinks, Has.Count.EqualTo(1), string.Join(", ", sinks));
            Assert.That(sinks[0], Does.EndWith("/Server/CharacterInventoryAuthority.cs"));
        }

        [Test]
        public void NoItemGetsANetworkObjectOfItsOwn()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var entities = new List<string>();

            foreach (string file in files)
            {
                if (System.IO.File.ReadAllText(file).Contains(": NetworkBehaviour"))
                {
                    entities.Add(System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }

            Assert.That(entities, Is.EquivalentTo(new[]
            {
                "CharacterNetworkEntity", "MonsterNetworkEntity",
            }), "inventory is state, not a world full of spawned objects");
        }

        [Test]
        public void NoClientCodeMutatesInventoryOrEquipmentAuthoritatively()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                string named = file.Replace('\\', '/');

                Assert.That(source, Does.Not.Contain("CharacterInventoryAuthority"), named);
                Assert.That(source, Does.Not.Contain("ServerPublishInventory"), named);
            }
        }
    }
}
