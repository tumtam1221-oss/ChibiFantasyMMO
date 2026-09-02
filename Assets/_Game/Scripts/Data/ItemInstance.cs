using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player's actual copy of an item.
    /// </summary>
    /// <remarks>
    /// The definition says what the item is; this says whose it is and how many they have.
    ///
    /// Holds no inventory behaviour. Stacking, splitting, merging, capacity and slot
    /// placement are container concerns that belong to a later inventory system.
    /// </remarks>
    [Serializable]
    public sealed class ItemInstance : GameInstance
    {
        [SerializeField] private int _quantity;

        /// <summary>Exists for deserializers.</summary>
        public ItemInstance()
        {
        }

        public ItemInstance(InstanceId instanceId, DefinitionId itemDefinitionId, OwnerId owner, int quantity)
            : base(instanceId, itemDefinitionId, owner)
        {
            ValidateQuantity(quantity);
            _quantity = quantity;
        }

        /// <summary>How many the owner holds. Always at least one.</summary>
        public int Quantity => _quantity;

        /// <summary>
        /// Sets the held count and advances the revision.
        /// </summary>
        /// <remarks>
        /// Enforces only the universal floor of one. The per-item stack ceiling lives on
        /// <see cref="ItemDefinition.MaxStackSize"/>, which this type cannot see because it
        /// references its definition by id rather than by object. Checking a quantity
        /// against that ceiling is server-side validation, and deliberately not done here:
        /// a client-side check would be unenforceable anyway.
        /// </remarks>
        public void SetQuantity(int quantity)
        {
            ValidateQuantity(quantity);
            _quantity = quantity;
            AdvanceRevision();
        }

        private static void ValidateQuantity(int quantity)
        {
            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity), quantity, "An item instance must hold at least one.");
            }
        }
    }
}
