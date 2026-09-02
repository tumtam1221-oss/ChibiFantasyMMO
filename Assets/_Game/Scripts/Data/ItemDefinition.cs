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

        public bool Usable => _usable;
    }
}
