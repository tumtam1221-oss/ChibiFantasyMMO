using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What happens to the stone and the piece when an enchant attempt fails.</summary>
    /// <remarks>
    /// Closed technical category, and deliberately narrower than
    /// <see cref="EnhancementFailureBehavior"/>: enchanting a socket is not enhancing a
    /// weapon, and reusing that enum would offer <c>ResetToZero</c>, which means nothing
    /// here.
    ///
    /// No value destroys the equipment. Losing a stone is a cost a player accepts;
    /// destroying the sword they socketed it into is not something a stone should be able
    /// to decide, and if a game wants that it is an enhancement rule.
    /// </remarks>
    public enum EnchantFailureBehavior
    {
        /// <summary>The stone survives. Nothing is spent but the attempt.</summary>
        KeepStone = 0,

        /// <summary>The stone is consumed and the socket stays empty.</summary>
        LoseStone = 1,

        /// <summary>The stone is consumed and everything already socketed is cleared.</summary>
        ClearSockets = 2
    }

    /// <summary>
    /// The rules for socketing one status stone.
    /// </summary>
    /// <remarks>
    /// <b>A block on <see cref="ItemDefinition"/>, not a new definition type.</b> A status
    /// stone is an item: it sits in the bag, it stacks, it has a name and an icon, and
    /// <see cref="ItemCategory.StatusStone"/> already exists to mark it. Giving it its own
    /// <c>GameDefinition</c> would mean a second thing to register, look up, validate and
    /// keep in sync with the item it shadows -- for one extra field group. This follows the
    /// same shape as the Phase 08.3 use configuration for the same reason.
    ///
    /// <b>One model for every stone.</b> There is no STR stone type and no fire stone
    /// type. A stone's effect is <see cref="StatModifiers"/>, and its restrictions are
    /// authored references. Adding a new stone is content.
    ///
    /// Flat and DB-friendly: primitives, enums and <see cref="DefinitionId"/> arrays, so
    /// one row plus a couple of join tables reconstructs it exactly.
    /// </remarks>
    [Serializable]
    public struct StatusStoneConfig
    {
        [Tooltip("What the stone grants the piece it is socketed into.")]
        [SerializeField] private StatModifier[] _statModifiers;

        [Tooltip("Chance in 0..1 that socketing succeeds. Zero or less is treated as certain.")]
        [SerializeField] private float _successChance;

        [SerializeField] private EnchantFailureBehavior _failureBehavior;

        [Tooltip("Equipment categories this stone fits. None/empty means unrestricted.")]
        [SerializeField] private EquipmentCategory _allowedCategory;

        [Tooltip("Equipment subtypes this stone fits. Empty means unrestricted.")]
        [SerializeField] private EquipmentSubtype[] _allowedSubtypes;

        [Tooltip("Slots this stone fits. Empty means unrestricted.")]
        [SerializeField] private EquipmentSlot[] _allowedSlots;

        [Tooltip("Rarity tiers this stone may be socketed into. Empty means unrestricted.")]
        [SerializeField] private DefinitionId[] _allowedRarities;

        [Tooltip("Minimum item level requirement the piece must have. Zero means none.")]
        [SerializeField] private int _minimumItemLevel;

        [Tooltip("How many of this stone one piece may hold. Zero or less means one.")]
        [SerializeField] private int _maxPerEquipment;

        [Tooltip("Whether this stone may be used as fusion input.")]
        [SerializeField] private bool _fusable;

        /// <summary>What the stone grants. Read at resolve time, never copied onto a piece.</summary>
        public StatModifier[] StatModifiers => _statModifiers ?? NoModifiers;

        /// <summary>
        /// Odds of socketing succeeding.
        /// </summary>
        /// <remarks>Zero or less means certain, which is what an unauthored stone reads as.
        /// Treating a blank as "never succeeds" would make every stone authored before this
        /// field existed permanently useless.</remarks>
        public float SuccessChance => _successChance;

        public EnchantFailureBehavior FailureBehavior => _failureBehavior;

        /// <summary><see cref="EquipmentCategory.None"/> means unrestricted.</summary>
        public EquipmentCategory AllowedCategory => _allowedCategory;

        public EquipmentSubtype[] AllowedSubtypes => _allowedSubtypes ?? NoSubtypes;

        public EquipmentSlot[] AllowedSlots => _allowedSlots ?? NoSlots;

        /// <summary>References to <see cref="RarityDefinition"/>. Empty means unrestricted.</summary>
        public DefinitionId[] AllowedRarities => _allowedRarities ?? NoIds;

        public int MinimumItemLevel => _minimumItemLevel;

        /// <summary>
        /// How many copies of this stone one piece may carry.
        /// </summary>
        /// <remarks>One when unauthored, which is the restrictive reading. A blank meaning
        /// "unlimited" would let a bad import stack the same stone into every socket.</remarks>
        public int MaxPerEquipment => _maxPerEquipment < 1 ? 1 : _maxPerEquipment;

        public bool Fusable => _fusable;

        private static readonly StatModifier[] NoModifiers = new StatModifier[0];
        private static readonly EquipmentSubtype[] NoSubtypes = new EquipmentSubtype[0];
        private static readonly EquipmentSlot[] NoSlots = new EquipmentSlot[0];
        private static readonly DefinitionId[] NoIds = new DefinitionId[0];
    }
}
