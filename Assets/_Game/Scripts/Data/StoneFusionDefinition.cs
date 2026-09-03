using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>One item a fusion consumes.</summary>
    /// <remarks>Flat on purpose: one row of a future <c>stone_fusion_material</c> table is
    /// a recipe id, an item id and a quantity.</remarks>
    [Serializable]
    public struct FusionIngredient
    {
        [SerializeField] private DefinitionId _item;
        [SerializeField] private int _quantity;

        public FusionIngredient(DefinitionId item, int quantity)
        {
            _item = item;
            _quantity = quantity;
        }

        /// <summary>Reference to the consumed <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Item => _item;

        public int Quantity => _quantity;

        public bool IsValid => _item.IsValid && _quantity > 0;

        public override string ToString()
        {
            return _item + " x" + _quantity;
        }
    }

    /// <summary>
    /// A recipe that turns stones into a better stone.
    /// </summary>
    /// <remarks>
    /// <b>Nothing about grades is in code.</b> "Three of these make one of those" is
    /// entirely <see cref="Inputs"/> and <see cref="Result"/>. There is no notion of a
    /// stone tier here and no arithmetic on ranks: a recipe that fuses three different
    /// stones into something unrelated is just as authorable as three identical ones, and
    /// the service cannot tell the difference.
    ///
    /// <b>Not limited to stones by type.</b> The name says what it is for; the schema is
    /// item ids, so the same machinery would fuse anything content points it at. Whether a
    /// given item may be an input is the stone's own
    /// <see cref="StatusStoneConfig.Fusable"/> flag, checked by the service -- authored,
    /// not inferred.
    ///
    /// Executing a fusion, rolling against <see cref="SuccessChance"/> and consuming the
    /// inputs are server-authoritative Gameplay concerns. Nothing is computed here.
    /// </remarks>
    public sealed class StoneFusionDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;

        [Tooltip("Everything the fusion consumes. All of it, or the attempt is refused.")]
        [SerializeField] private FusionIngredient[] _inputs = new FusionIngredient[0];

        [Tooltip("What the fusion produces on success.")]
        [SerializeField] private DefinitionId _result;

        [Tooltip("How many of the result. Zero or less is treated as one.")]
        [SerializeField] private int _resultQuantity = 1;

        [Tooltip("Chance in 0..1. Zero or less is treated as certain.")]
        [SerializeField] private float _successChance;

        [Tooltip("Produced instead on failure. Invalid means failure yields nothing.")]
        [SerializeField] private DefinitionId _failureResult;

        [Tooltip("How many of the failure result. Zero or less is treated as one.")]
        [SerializeField] private int _failureResultQuantity = 1;

        [Tooltip("Whether a failed fusion still consumes the inputs.")]
        [SerializeField] private bool _consumeInputsOnFailure = true;

        [Tooltip("Currency the attempt costs. Zero means free.")]
        [SerializeField] private int _currencyCost;

        [Tooltip("Currency item the cost is paid in. Invalid means no currency is charged.")]
        [SerializeField] private DefinitionId _currencyItem;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>Everything consumed. Never null.</summary>
        public FusionIngredient[] Inputs => _inputs ?? NoIngredients;

        /// <summary>Reference to the produced <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Result => _result;

        public int ResultQuantity => _resultQuantity < 1 ? 1 : _resultQuantity;

        /// <summary>Zero or less means certain, so an unauthored chance is not "never".</summary>
        public float SuccessChance => _successChance;

        /// <summary>
        /// Consolation output.
        /// </summary>
        /// <remarks>Invalid means a failed fusion produces nothing, which is the common
        /// design. Authoring a lesser stone here is how a game softens failure, and that is
        /// a content decision.</remarks>
        public DefinitionId FailureResult => _failureResult;

        public int FailureResultQuantity => _failureResultQuantity < 1 ? 1 : _failureResultQuantity;

        /// <summary>
        /// Whether failure still eats the inputs.
        /// </summary>
        /// <remarks>Defaults true because that is what makes a success chance meaningful; a
        /// fusion that costs nothing on failure is a free reroll. Content may still choose
        /// otherwise.</remarks>
        public bool ConsumeInputsOnFailure => _consumeInputsOnFailure;

        public int CurrencyCost => _currencyCost;

        /// <summary>
        /// The item the currency cost is paid in.
        /// </summary>
        /// <remarks>Currency is an inventory item like anything else, so a cost is just
        /// another ingredient with its own field. No wallet system exists and none is
        /// invented here.</remarks>
        public DefinitionId CurrencyItem => _currencyItem;

        private static readonly FusionIngredient[] NoIngredients = new FusionIngredient[0];
    }
}
