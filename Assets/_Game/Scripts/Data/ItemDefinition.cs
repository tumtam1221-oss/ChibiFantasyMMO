using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad handling category for an item.</summary>
    /// <remarks>Closed technical category: each value implies a different code path
    /// (consume, equip, turn in, apply to equipment), so adding one is a code change
    /// regardless of whether it is an enum.</remarks>
    public enum ItemCategory
    {
        Misc = 0,
        Consumable = 1,
        Equipment = 2,
        Material = 3,
        Quest = 4,
        Card = 5,
        DevilFruit = 6,
        Currency = 7,
        StatusStone = 8
    }

    /// <summary>
    /// What an item <em>is</em>. Static, shared by every copy in the world.
    /// </summary>
    /// <remarks>
    /// A player's actual owned copy (stack count, enhancement level, bound sockets) is
    /// runtime state and belongs to a future ItemInstance, not here.
    ///
    /// Not sealed: <see cref="EquipmentDefinition"/> extends it.
    /// </remarks>
    public class ItemDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;
        [SerializeField] private ItemCategory _category = ItemCategory.Misc;
        [SerializeField] private DefinitionId _rarity;

        [SerializeField] private bool _stackable;
        [SerializeField] private int _maxStackSize = 1;

        [SerializeField] private int _buyPrice;
        [SerializeField] private int _sellPrice;

        [SerializeField] private bool _tradable = true;
        [SerializeField] private bool _droppable = true;
        [SerializeField] private bool _usable;

        [Header("Use")]
        [Tooltip("What using this item is for. None means it cannot be used.")]
        [SerializeField] private ItemUseType _useType = ItemUseType.None;

        [Tooltip("Who it acts on. Only Self is executable today.")]
        [SerializeField] private ItemUseTarget _useTarget = ItemUseTarget.Self;

        [Tooltip("What it does, in authored order. Empty means nothing happens.")]
        [SerializeField] private ItemUseEffect[] _useEffects = new ItemUseEffect[0];

        [Header("Status stone")]
        [Tooltip("Socketing rules. Meaningful only when Category is StatusStone.")]
        [SerializeField] private StatusStoneConfig _stoneConfig;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        public ItemCategory Category => _category;

        /// <summary>Reference to a <see cref="RarityDefinition"/>.</summary>
        public DefinitionId Rarity => _rarity;

        public bool Stackable => _stackable;

        /// <summary>Authored stack ceiling. Meaningful only when <see cref="Stackable"/>.</summary>
        public int MaxStackSize => _maxStackSize;

        public int BuyPrice => _buyPrice;

        public int SellPrice => _sellPrice;

        public bool Tradable => _tradable;

        public bool Droppable => _droppable;

        /// <summary>
        /// Whether a player may use this item at all.
        /// </summary>
        /// <remarks>Authored separately from <see cref="UseType"/> so content can disable a
        /// configured item without deleting its configuration -- an event consumable turned
        /// off, a quest item that becomes inert. The use service demands both.</remarks>
        public bool Usable => _usable;

        /// <summary>
        /// What using this item is for.
        /// </summary>
        /// <remarks>The item's authored classification, checked against
        /// <see cref="UseEffects"/> rather than trusted blindly. See
        /// <see cref="ItemUseType"/>.</remarks>
        public ItemUseType UseType => _useType;

        /// <summary>Who it acts on. See <see cref="ItemUseTarget"/>.</summary>
        public ItemUseTarget UseTarget => _useTarget;

        /// <summary>
        /// What it does, in authored order.
        /// </summary>
        /// <remarks>
        /// Order is the authored order and is preserved on execution, because a food that
        /// restores health and then grants a buff scaled off it must not run backwards.
        ///
        /// Never null: an item authored before this field existed reads as an empty array,
        /// which is refused as unusable rather than crashing a save.
        /// </remarks>
        public ItemUseEffect[] UseEffects => _useEffects ?? NoEffects;

        /// <summary>
        /// Socketing rules, when this item is a status stone.
        /// </summary>
        /// <remarks>Meaningful only alongside <see cref="ItemCategory.StatusStone"/>.
        /// <see cref="IsStatusStone"/> is the question callers should ask; reading this
        /// block off a potion returns an empty configuration rather than throwing.</remarks>
        public StatusStoneConfig StoneConfig => _stoneConfig;

        /// <summary>
        /// Whether this item can be socketed into equipment.
        /// </summary>
        /// <remarks>The authored category is what decides, not the presence of a
        /// configuration: a stone with no modifiers yet is still a stone, and an item
        /// mis-categorised as a potion must not become socketable because someone filled
        /// in the block by accident.</remarks>
        public bool IsStatusStone => _category == ItemCategory.StatusStone;

        private static readonly ItemUseEffect[] NoEffects = new ItemUseEffect[0];
    }
}
