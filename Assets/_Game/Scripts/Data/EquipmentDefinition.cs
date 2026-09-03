using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Character slot an equipment piece occupies.</summary>
    /// <remarks>Closed technical category: slots correspond to fixed attachment points
    /// and fixed paperdoll UI positions.</remarks>
    public enum EquipmentSlot
    {
        None = 0,
        Head = 1,
        Body = 2,
        Legs = 3,
        Feet = 4,
        Hands = 5,
        MainHand = 6,
        OffHand = 7,
        Accessory = 8,
        Cape = 9
    }

    /// <summary>Top-level equipment family.</summary>
    public enum EquipmentCategory
    {
        None = 0,
        Weapon = 1,
        Armor = 2,
        Accessory = 3,
        Shield = 4
    }

    /// <summary>
    /// Weapon or armour subtype, driving animation sets and job restrictions.
    /// </summary>
    public enum EquipmentSubtype
    {
        None = 0,
        OneHandSword = 1,
        TwoHandSword = 2,
        Dagger = 3,
        Spear = 4,
        Axe = 5,
        Mace = 6,
        Bow = 7,
        Crossbow = 8,
        Staff = 9,
        Wand = 10,
        Shield = 11,
        LightArmor = 20,
        MediumArmor = 21,
        HeavyArmor = 22,
        Robe = 23,
        Accessory = 30
    }

    /// <summary>
    /// What a piece of equipment <em>is</em>.
    /// </summary>
    /// <remarks>
    /// Extends <see cref="ItemDefinition"/> because equipment is an item: it occupies
    /// inventory, has rarity and price, and can be traded or dropped. Inheriting keeps
    /// those shared fields in one place and makes equipment usable anywhere an item is
    /// expected, without a duplicate parallel hierarchy.
    ///
    /// The owned, enhanced, socketed copy is runtime state and belongs to a future
    /// EquipmentInstance. Nothing here computes enhancement results.
    /// </remarks>
    public sealed class EquipmentDefinition : ItemDefinition
    {
        [SerializeField] private EquipmentSlot _slot = EquipmentSlot.None;
        [SerializeField] private EquipmentCategory _equipmentCategory = EquipmentCategory.None;
        [SerializeField] private EquipmentSubtype _subtype = EquipmentSubtype.None;

        [SerializeField] private int _levelRequirement;
        [SerializeField] private DefinitionId[] _allowedClasses = new DefinitionId[0];
        [SerializeField] private DefinitionId[] _allowedJobs = new DefinitionId[0];

        [SerializeField] private StatModifier[] _baseStatModifiers = new StatModifier[0];

        [SerializeField] private bool _enhanceable;
        [SerializeField] private int _maxEnhancementLevel;
        [SerializeField] private DefinitionId _enhancementRule;


        [Tooltip("Number of card sockets. Separate from status-stone sockets on purpose.")]
        [SerializeField] private int _cardSlots;
        [SerializeField] private int _statusStoneSlots;

        [SerializeField] private AssetRef _model;

        public EquipmentSlot Slot => _slot;

        public EquipmentCategory EquipmentCategory => _equipmentCategory;

        public EquipmentSubtype Subtype => _subtype;

        public int LevelRequirement => _levelRequirement;

        /// <summary>References to <see cref="ClassDefinition"/>. Empty means unrestricted.</summary>
        public DefinitionId[] AllowedClasses => _allowedClasses;

        /// <summary>References to <see cref="JobDefinition"/>. Empty means unrestricted.</summary>
        public DefinitionId[] AllowedJobs => _allowedJobs;

        /// <summary>Modifiers granted at enhancement level zero.</summary>
        public StatModifier[] BaseStatModifiers => _baseStatModifiers;

        public bool Enhanceable => _enhanceable;

        public int MaxEnhancementLevel => _maxEnhancementLevel;

        /// <summary>Reference to an <see cref="EnhancementDefinition"/> supplying the rules.</summary>
        public DefinitionId EnhancementRule => _enhancementRule;

        /// <summary>Number of status-stone / enchant sockets this piece is authored with.</summary>
        public int StatusStoneSlots => _statusStoneSlots;

        /// <summary>
        /// Number of card sockets this piece is authored with.
        /// </summary>
        /// <remarks>
        /// Counted separately from <see cref="StatusStoneSlots"/>. A piece with two stone
        /// sockets and one card socket is a normal thing to author, and sharing one number
        /// would mean socketing a card silently cost a player an enchant slot -- a Phase 09
        /// behaviour change nobody asked for.
        /// </remarks>
        public int CardSlots => _cardSlots;

        public AssetRef Model => _model;
    }
}
