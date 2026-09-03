using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What an enhancement panel needs to draw one attempt.
    /// </summary>
    /// <remarks>
    /// <b>A snapshot, like every other view type here.</b> It holds no
    /// <c>EquipmentInstance</c>, so the panel drawing it has nothing to mutate. The
    /// preview totals were computed by <c>EquipmentModifierResolver</c> in the Client and
    /// copied in; nothing in the UI works out what an upgrade is worth.
    ///
    /// <b>Current and preview are separate figures.</b> Both are resolved from the same
    /// pure function at two different levels, so a panel can show "+5 -> +6, STR 15 -> 19"
    /// without a second calculation existing and without the piece being touched.
    /// </remarks>
    public readonly struct EnhancementViewData
    {
        private static readonly StatModifier[] NoModifiers = new StatModifier[0];

        private readonly StatModifier[] _current;
        private readonly StatModifier[] _preview;

        private EnhancementViewData(bool valid, DefinitionId definitionId, InstanceId instanceId,
            LocalizationKey nameKey, int slotIndex, int currentLevel, int maxLevel,
            bool canAttempt, float successChance,
            DefinitionId materialItem, LocalizationKey materialNameKey, int materialAmount,
            int materialHeld,
            DefinitionId currencyItem, LocalizationKey currencyNameKey, int currencyCost,
            int currencyHeld,
            EnhancementFailureBehavior failureBehavior,
            StatModifier[] current, StatModifier[] preview)
        {
            IsValid = valid;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            NameKey = nameKey;
            SlotIndex = slotIndex;
            CurrentLevel = currentLevel;
            MaxLevel = maxLevel;
            CanAttempt = canAttempt;
            SuccessChance = successChance;
            MaterialItem = materialItem;
            MaterialNameKey = materialNameKey;
            MaterialAmount = materialAmount;
            MaterialHeld = materialHeld;
            CurrencyItem = currencyItem;
            CurrencyNameKey = currencyNameKey;
            CurrencyCost = currencyCost;
            CurrencyHeld = currencyHeld;
            FailureBehavior = failureBehavior;
            _current = current ?? NoModifiers;
            _preview = preview ?? NoModifiers;
        }

        /// <summary>False when nothing enhanceable is selected.</summary>
        public bool IsValid { get; }

        public DefinitionId DefinitionId { get; }

        public InstanceId InstanceId { get; }

        public LocalizationKey NameKey { get; }

        /// <summary>Where the piece sits in the container it was read from.</summary>
        public int SlotIndex { get; }

        public int CurrentLevel { get; }

        /// <summary>The strictest authored ceiling. Zero means none applies.</summary>
        public int MaxLevel { get; }

        /// <summary>
        /// Whether an attempt is worth offering.
        /// </summary>
        /// <remarks>Advisory, exactly like <see cref="ItemDropAdvice"/>: it reflects a
        /// resolvable step below the ceiling, not permission.
        /// <c>EnhancementService</c> re-checks everything and remains the authority.</remarks>
        public bool CanAttempt { get; }

        /// <summary>Authored odds of the next step, in zero to one.</summary>
        public float SuccessChance { get; }

        public DefinitionId MaterialItem { get; }

        /// <summary>The material's own name key, read off its definition by the Client.</summary>
        /// <remarks>Carried rather than derived: a name key is authored content, and a UI
        /// that built one from an id would invent a key nobody wrote.</remarks>
        public LocalizationKey MaterialNameKey { get; }

        public int MaterialAmount { get; }

        /// <summary>How many the player actually holds, so a panel can grey out.</summary>
        public int MaterialHeld { get; }

        public DefinitionId CurrencyItem { get; }

        /// <summary>The currency item's own name key.</summary>
        public LocalizationKey CurrencyNameKey { get; }

        public int CurrencyCost { get; }

        public int CurrencyHeld { get; }

        /// <summary>What the authored step does on a failed roll.</summary>
        public EnhancementFailureBehavior FailureBehavior { get; }

        /// <summary>What the piece is worth now.</summary>
        public IReadOnlyList<StatModifier> CurrentModifiers => _current;

        /// <summary>What it would be worth one level up. A read, never a change.</summary>
        public IReadOnlyList<StatModifier> PreviewModifiers => _preview;

        public bool HasEnoughMaterial => MaterialAmount <= 0 || MaterialHeld >= MaterialAmount;

        public bool HasEnoughCurrency => CurrencyCost <= 0 || CurrencyHeld >= CurrencyCost;

        public bool IsAtCeiling => MaxLevel > 0 && CurrentLevel >= MaxLevel;

        /// <summary>Nothing to show.</summary>
        public static EnhancementViewData None => default;

        public static EnhancementViewData From(DefinitionId definitionId, InstanceId instanceId,
            LocalizationKey nameKey, int slotIndex, int currentLevel, int maxLevel,
            bool canAttempt, float successChance,
            DefinitionId materialItem, LocalizationKey materialNameKey, int materialAmount,
            int materialHeld,
            DefinitionId currencyItem, LocalizationKey currencyNameKey, int currencyCost,
            int currencyHeld,
            EnhancementFailureBehavior failureBehavior,
            StatModifier[] current, StatModifier[] preview)
        {
            return new EnhancementViewData(true, definitionId, instanceId, nameKey, slotIndex,
                currentLevel, maxLevel, canAttempt, successChance,
                materialItem, materialNameKey, materialAmount, materialHeld,
                currencyItem, currencyNameKey, currencyCost, currencyHeld, failureBehavior,
                current, preview);
        }

        public override string ToString()
        {
            if (!IsValid) return "no selection";
            return DefinitionId + " +" + CurrentLevel + " -> +" + (CurrentLevel + 1);
        }
    }

    /// <summary>One socket on the enchant panel.</summary>
    /// <remarks>Empty sockets are represented too, so the panel draws a fixed shape and a
    /// player can see how much room is left.</remarks>
    public readonly struct EnchantSlotViewData
    {
        private EnchantSlotViewData(int socketIndex, DefinitionId stone, LocalizationKey nameKey,
            AssetRef icon, int rank)
        {
            SocketIndex = socketIndex;
            Stone = stone;
            NameKey = nameKey;
            Icon = icon;
            Rank = rank;
        }

        public int SocketIndex { get; }

        public DefinitionId Stone { get; }

        public LocalizationKey NameKey { get; }

        public AssetRef Icon { get; }

        public int Rank { get; }

        public bool IsEmpty => !Stone.IsValid;

        public bool IsOccupied => Stone.IsValid;

        public static EnchantSlotViewData Empty(int socketIndex)
        {
            return new EnchantSlotViewData(socketIndex, DefinitionId.None, default,
                AssetRef.None, 0);
        }

        public static EnchantSlotViewData From(int socketIndex, DefinitionId stone,
            int rank, ItemDefinition definition)
        {
            if (!stone.IsValid) return Empty(socketIndex);

            // An unresolvable stone still shows as an occupied socket: content removed by a
            // patch must be visible, not silently emptied.
            return definition == null
                ? new EnchantSlotViewData(socketIndex, stone, default, AssetRef.None, rank)
                : new EnchantSlotViewData(socketIndex, stone, definition.NameKey,
                    definition.Icon, rank);
        }

        public override string ToString()
        {
            return IsEmpty ? "[" + SocketIndex + "] empty" : "[" + SocketIndex + "] " + Stone;
        }
    }

    /// <summary>What a fusion panel needs to draw one recipe.</summary>
    public readonly struct FusionViewData
    {
        private static readonly FusionIngredientViewData[] NoInputs = new FusionIngredientViewData[0];

        private readonly FusionIngredientViewData[] _inputs;

        private FusionViewData(bool valid, DefinitionId recipe, LocalizationKey nameKey,
            DefinitionId result, LocalizationKey resultNameKey, AssetRef resultIcon,
            int resultQuantity, float successChance, DefinitionId currencyItem, int currencyCost,
            int currencyHeld, FusionIngredientViewData[] inputs)
        {
            IsValid = valid;
            Recipe = recipe;
            NameKey = nameKey;
            Result = result;
            ResultNameKey = resultNameKey;
            ResultIcon = resultIcon;
            ResultQuantity = resultQuantity;
            SuccessChance = successChance;
            CurrencyItem = currencyItem;
            CurrencyCost = currencyCost;
            CurrencyHeld = currencyHeld;
            _inputs = inputs ?? NoInputs;
        }

        public bool IsValid { get; }

        public DefinitionId Recipe { get; }

        public LocalizationKey NameKey { get; }

        public DefinitionId Result { get; }

        public LocalizationKey ResultNameKey { get; }

        public AssetRef ResultIcon { get; }

        public int ResultQuantity { get; }

        public float SuccessChance { get; }

        public DefinitionId CurrencyItem { get; }

        public int CurrencyCost { get; }

        public int CurrencyHeld { get; }

        public IReadOnlyList<FusionIngredientViewData> Inputs => _inputs;

        public bool HasEnoughCurrency => CurrencyCost <= 0 || CurrencyHeld >= CurrencyCost;

        /// <summary>
        /// Whether every input is covered.
        /// </summary>
        /// <remarks>Advisory. <c>StoneFusionService</c> re-checks quantities, fusability,
        /// the output and the room for it, and remains the authority.</remarks>
        public bool HasEnoughInputs
        {
            get
            {
                for (int i = 0; i < _inputs.Length; i++)
                {
                    if (!_inputs[i].IsSatisfied) return false;
                }

                return true;
            }
        }

        public bool CanAttempt => IsValid && HasEnoughInputs && HasEnoughCurrency;

        public static FusionViewData None => default;

        public static FusionViewData From(DefinitionId recipe, LocalizationKey nameKey,
            DefinitionId result, LocalizationKey resultNameKey, AssetRef resultIcon,
            int resultQuantity, float successChance, DefinitionId currencyItem, int currencyCost,
            int currencyHeld, FusionIngredientViewData[] inputs)
        {
            return new FusionViewData(true, recipe, nameKey, result, resultNameKey, resultIcon,
                resultQuantity, successChance, currencyItem, currencyCost, currencyHeld, inputs);
        }

        public override string ToString()
        {
            return IsValid ? Recipe + " -> " + Result + " x" + ResultQuantity : "no recipe";
        }
    }

    /// <summary>One line of a fusion recipe's cost.</summary>
    public readonly struct FusionIngredientViewData
    {
        public FusionIngredientViewData(DefinitionId item, LocalizationKey nameKey, AssetRef icon,
            int required, int held)
        {
            Item = item;
            NameKey = nameKey;
            Icon = icon;
            Required = required;
            Held = held;
        }

        public DefinitionId Item { get; }

        public LocalizationKey NameKey { get; }

        public AssetRef Icon { get; }

        public int Required { get; }

        /// <summary>How many the player holds, so a panel can show 2/3 in red.</summary>
        public int Held { get; }

        public bool IsSatisfied => Held >= Required;

        public override string ToString()
        {
            return Item + " " + Held + "/" + Required;
        }
    }
}
