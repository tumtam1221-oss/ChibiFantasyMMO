using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
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

        [Tooltip("Maps a warp item can point at.")]
        [SerializeField] private MapDefinition[] maps;

        [Tooltip("Status effects a buff item can grant.")]
        [SerializeField] private StatusEffectDefinition[] statusEffects;

        [Header("Containers - PROTOTYPE")]
        [SerializeField] private int inventoryCapacity = 20;
        [SerializeField] private int storageCapacity = 20;
        [SerializeField] private int characterLevel = 10;

        [Header("Resources - PROTOTYPE")]
        [Tooltip("Stand-in ceilings. Real ones come from the derived stats.")]
        [SerializeField] private int maxHealth = 1000;

        [SerializeField] private int maxMana = 400;
        [SerializeField] private int startingHealth = 400;
        [SerializeField] private int startingMana = 150;

        [Header("Icons - PROTOTYPE")]
        [Tooltip("Resources folder path prefix prepended to an item's AssetRef.")]
        [SerializeField] private string iconPathPrefix = string.Empty;

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

        private readonly DefinitionRegistry<MapDefinition> _maps =
            new DefinitionRegistry<MapDefinition>();

        private readonly DefinitionRegistry<StatusEffectDefinition> _statusEffects =
            new DefinitionRegistry<StatusEffectDefinition>();

        private ItemContainerState _inventory;
        private ItemContainerState _storage;
        private CharacterEquipmentState _equipment;
        private CharacterResourceState _resources;
        private ResourceLimits _limits;

        public ItemContainerState Inventory => _inventory;

        public ItemContainerState Storage => _storage;

        public CharacterEquipmentState Equipment => _equipment;

        /// <summary>
        /// The pools a consumable fills.
        /// </summary>
        /// <remarks>Owned here for the prototype only. A real character's resources belong
        /// to whatever owns the character, and its ceilings come from the derived
        /// stats.</remarks>
        public CharacterResourceState CharacterResources => _resources;

        public ResourceLimits Limits => _limits;

        public IDefinitionRegistry<ItemDefinition> Items => _items;

        public IDefinitionRegistry<MapDefinition> Maps => _maps;

        public IDefinitionRegistry<StatusEffectDefinition> StatusEffects => _statusEffects;

        /// <summary>
        /// The prototype's icon and text boundaries.
        /// </summary>
        /// <remarks>An empty table and a Resources loader: enough to show the seams working.
        /// Neither is a content pipeline, and both are replaced wholesale when real ones
        /// arrive.</remarks>
        public IconResolver Icons { get; private set; }

        public LocalizationTable Text { get; private set; }

        public bool IsWindowOpen { get; private set; }

        /// <summary>
        /// Refuses to run outside the editor.
        /// </summary>
        /// <remarks>
        /// This harness authors a prototype world from inspector fields, including a
        /// hard-coded owner and character id. Those are correct for a prototype scene and
        /// wrong for anything a player runs: a production character's identity comes from
        /// the account database, never from a literal.
        ///
        /// The component is left in place rather than deleted -- the prototype scene
        /// references it, and rewriting prototype architecture is not what this phase is
        /// for -- but it disables itself in a build, so the literals below cannot reach a
        /// production runtime.
        /// </remarks>
        private void Awake()
        {
            if (Application.isEditor) return;

            Debug.LogWarning("[proto] ProtoInventoryHarness is a prototype-only component "
                + "and does not run outside the editor.");

            enabled = false;
        }

        private void Start()
        {
            // Awake disabled this outside the editor; Start still runs on the frame a
            // component is disabled in, so the guard is repeated rather than assumed.
            if (!Application.isEditor) return;

            Register();
            BuildContainers();
            BuildPresentation();

            if (controller == null)
            {
                Debug.LogError("ProtoInventoryHarness: no InventoryUiController assigned.", this);
                return;
            }

            controller.Icons = Icons;
            controller.Text = Text;
            controller.Bind(_inventory, _storage, _equipment, _items, characterLevel,
                default, default, _resources, _limits, _maps, _statusEffects);

            SetWindowOpen(true);
            controller.SetStorageOpen(false);
        }

        private void Register()
        {
            if (content != null)
            {
                for (int i = 0; i < content.Length; i++)
                {
                    if (content[i] != null) _items.Register(content[i]);
                }
            }

            if (maps != null)
            {
                for (int i = 0; i < maps.Length; i++)
                {
                    if (maps[i] != null) _maps.Register(maps[i]);
                }
            }

            if (statusEffects == null) return;

            for (int i = 0; i < statusEffects.Length; i++)
            {
                if (statusEffects[i] != null) _statusEffects.Register(statusEffects[i]);
            }
        }

        /// <summary>
        /// Builds the icon and text boundaries the panels draw through.
        /// </summary>
        /// <remarks>
        /// The table is seeded from the authored name keys themselves, so the prototype
        /// shows something readable without committing the project to a translation file.
        /// The keys stay authoritative -- this only proves the seam resolves through them.
        /// </remarks>
        private void BuildPresentation()
        {
            Icons = new IconResolver(address =>
                Resources.Load<Sprite>(string.IsNullOrEmpty(iconPathPrefix)
                    ? address
                    : iconPathPrefix + "/" + address));

            Text = new LocalizationTable();

            if (content != null)
            {
                for (int i = 0; i < content.Length; i++)
                {
                    if (content[i] == null) continue;
                    SeedText(content[i].NameKey);
                    SeedText(content[i].DescriptionKey);
                }
            }

            if (maps == null) return;

            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] != null) SeedText(maps[i].NameKey);
            }
        }

        /// <summary>
        /// Gives a key a readable stand-in derived from the key itself.
        /// </summary>
        /// <remarks>
        /// A trailing <c>.name</c> or <c>.desc</c> says what the key is for, not what it
        /// names, so it is dropped before the last segment is taken: an
        /// <c>item.scroll.summit.name</c> reads as "Summit" rather than "Name".
        ///
        /// A prototype convenience so the screen is legible, not a translation strategy.
        /// The behaviour actually being demonstrated is the seam: a missing key falls back
        /// to the raw key, and the authored key stays authoritative.
        /// </remarks>
        private void SeedText(LocalizationKey key)
        {
            if (!key.IsValid) return;

            string raw = key.Key;

            if (raw.EndsWith(".name")) raw = raw.Substring(0, raw.Length - 5);
            else if (raw.EndsWith(".desc")) raw = raw.Substring(0, raw.Length - 5);
            else if (raw.EndsWith(".description")) raw = raw.Substring(0, raw.Length - 12);

            int dot = raw.LastIndexOf('.');
            string tail = dot >= 0 && dot < raw.Length - 1 ? raw.Substring(dot + 1) : raw;

            if (tail.Length == 0) return;

            Text.Set(key, char.ToUpperInvariant(tail[0]) + tail.Substring(1));
        }

        private void BuildContainers()
        {
            var owner = new OwnerId("proto:inventory");

            var character = new CharacterId("proto:character");

            _inventory = new ItemContainerState(owner, inventoryCapacity);
            _storage = new ItemContainerState(owner, storageCapacity);
            _equipment = new CharacterEquipmentState(character);

            _limits = new ResourceLimits(maxHealth < 0 ? 0 : maxHealth, maxMana < 0 ? 0 : maxMana);
            _resources = new CharacterResourceState(character, _limits, startingHealth, startingMana);

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

                if (controller != null)
                {
                    if (input.ToggleStoragePressed) controller.SetStorageOpen(!controller.IsStorageOpen);

                    // Escape backs out one step; only when nothing was in progress does it
                    // close the window, so a player never loses the whole screen by
                    // cancelling a drag.
                    if (input.CancelPressed && !controller.CancelActiveInteraction())
                    {
                        SetWindowOpen(false);
                    }
                }
            }

            // Only while the window is on screen, and only a revision comparison unless a
            // container actually moved.
            if (IsWindowOpen && controller != null) controller.RefreshIfChanged();
        }
    }
}
