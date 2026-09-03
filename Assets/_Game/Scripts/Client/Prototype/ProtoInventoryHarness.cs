using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE scene wiring for PHASE 08.2.
    /// </summary>
    /// <remarks>
    /// Owns the containers the prototype scene shows and hands them to the UI once. It is
    /// standing in for whatever will own a character's inventory later, which is why it is
    /// a prototype type in a prototype folder and nothing in Gameplay or UI knows it exists.
    ///
    /// <b>Content is authored, not coded.</b> The items come from
    /// <see cref="ItemDefinition"/> assets dropped into <see cref="content"/>. No item id,
    /// stack size or stat appears below -- the seeding list is authored on the component.
    ///
    /// <b>Input comes from the one asset.</b> Window toggles are read from
    /// <see cref="ProtoPlayerInput"/> like every other key in the project, so no second
    /// input path exists.
    /// </remarks>
    public sealed class ProtoInventoryHarness : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private InventoryUiController controller;
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private GameObject windowRoot;

        [Header("Content (authored definitions)")]
        [SerializeField] private ItemDefinition[] content;

        [Header("Containers - PROTOTYPE")]
        [SerializeField] private int inventoryCapacity = 20;
        [SerializeField] private int storageCapacity = 20;
        [SerializeField] private int characterLevel = 10;

        [Header("Starting items - PROTOTYPE")]
        [Tooltip("One entry per starting stack. Index into Content, and how many.")]
        [SerializeField] private Seed[] startingInventory;

        [SerializeField] private Seed[] startingStorage;

        /// <summary>One authored starting stack.</summary>
        [System.Serializable]
        public struct Seed
        {
            [Tooltip("Index into Content.")]
            public int contentIndex;

            [Tooltip("How many. One for equipment.")]
            public int quantity;
        }

        private readonly DefinitionRegistry<ItemDefinition> _items =
            new DefinitionRegistry<ItemDefinition>();

        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;

        public ItemContainerState Inventory => _inventory;

        public ItemContainerState Storage => _storage;

        public CharacterEquipmentState Equipment => _equipment;

        public IDefinitionRegistry<ItemDefinition> Items => _items;

        public bool IsWindowOpen { get; private set; }

        private void Start()
        {
            Register();
            BuildContainers();

            if (controller == null)
            {
                Debug.LogError("ProtoInventoryHarness: no InventoryUiController assigned.", this);
                return;
            }

            controller.Bind(_inventory, _storage, _equipment, _items, characterLevel);
            SetWindowOpen(true);
            controller.SetStorageOpen(false);
        }

        private void Register()
        {
            if (content == null) return;

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] != null) _items.Register(content[i]);
            }
        }

        private void BuildContainers()
        {
            var owner = new OwnerId("proto:inventory");

            _inventory = new ItemContainerState(owner, inventoryCapacity);
            _storage = new ItemContainerState(owner, storageCapacity);
            _equipment = new CharacterEquipmentState(new CharacterId("proto:character"));

            SeedContainer(_inventory, startingInventory, owner);
            SeedContainer(_storage, startingStorage, owner);
        }

        private void SeedContainer(ItemContainerState container, Seed[] seeds, OwnerId owner)
        {
            if (seeds == null || content == null) return;

            for (int i = 0; i < seeds.Length; i++)
            {
                int index = seeds[i].contentIndex;
                if (index < 0 || index >= content.Length || content[index] == null) continue;

                ItemDefinition definition = content[index];
                int quantity = seeds[i].quantity < 1 ? 1 : seeds[i].quantity;

                // Equipment is an instance with an enhancement level; everything else is a
                // stack. Which one an id is comes from the authored asset, not from here.
                GameInstance instance = definition is EquipmentDefinition
                    ? (GameInstance)new EquipmentInstance(InstanceId.New(), definition.Id, owner)
                    : new ItemInstance(InstanceId.New(), definition.Id, owner, quantity);

                container.Add(instance, _items);
            }
        }

        /// <summary>Shows or hides the whole window.</summary>
        public void SetWindowOpen(bool open)
        {
            IsWindowOpen = open;
            if (windowRoot != null) windowRoot.SetActive(open);
        }

        private void Update()
        {
            if (input != null)
            {
                if (input.ToggleInventoryPressed) SetWindowOpen(!IsWindowOpen);

                if (input.ToggleStoragePressed && controller != null)
                {
                    controller.SetStorageOpen(!controller.IsStorageOpen);
                }
            }

            // Only while the window is on screen, and only a revision comparison unless a
            // container actually moved.
            if (IsWindowOpen && controller != null) controller.RefreshIfChanged();
        }
    }
}
