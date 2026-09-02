using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player's actual copy of a piece of equipment.
    /// </summary>
    /// <remarks>
    /// Enhancement level lives here, not on <see cref="EquipmentDefinition"/>. The
    /// definition describes the base item every copy shares; two players holding the same
    /// sword may have enhanced it to different levels, so that number is per-copy state.
    ///
    /// Note the deliberate asymmetry with the definition layer: EquipmentDefinition extends
    /// ItemDefinition, but EquipmentInstance extends <see cref="GameInstance"/> directly
    /// rather than <see cref="ItemInstance"/>. The definitions share authored fields
    /// (rarity, price, tradable) so inheritance pays off there. The instances do not:
    /// ItemInstance exists to carry a stack quantity, and equipment does not stack, so
    /// inheriting would give every sword a meaningless quantity field.
    ///
    /// No enhancement is performed here. Rolling against the odds authored on an
    /// EnhancementDefinition, consuming materials and applying failure behaviour are
    /// server-authoritative gameplay.
    /// </remarks>
    [Serializable]
    public sealed class EquipmentInstance : GameInstance
    {
        [SerializeField] private int _enhancementLevel;

        /// <summary>Exists for deserializers.</summary>
        public EquipmentInstance()
        {
        }

        public EquipmentInstance(InstanceId instanceId, DefinitionId equipmentDefinitionId, OwnerId owner)
            : base(instanceId, equipmentDefinitionId, owner)
        {
            _enhancementLevel = 0;
        }

        public EquipmentInstance(InstanceId instanceId, DefinitionId equipmentDefinitionId, OwnerId owner,
            int enhancementLevel)
            : base(instanceId, equipmentDefinitionId, owner)
        {
            ValidateEnhancementLevel(enhancementLevel);
            _enhancementLevel = enhancementLevel;
        }

        /// <summary>Current enhancement level. Zero means unenhanced.</summary>
        public int EnhancementLevel => _enhancementLevel;

        /// <summary>
        /// Sets the enhancement level and advances the revision.
        /// </summary>
        /// <remarks>
        /// Enforces only the floor of zero. The ceiling is authored on
        /// <see cref="EquipmentDefinition.MaxEnhancementLevel"/> and enforced by the
        /// server, which is the only party that may decide an enhancement succeeded.
        /// </remarks>
        public void SetEnhancementLevel(int enhancementLevel)
        {
            ValidateEnhancementLevel(enhancementLevel);
            _enhancementLevel = enhancementLevel;
            AdvanceRevision();
        }

        private static void ValidateEnhancementLevel(int enhancementLevel)
        {
            if (enhancementLevel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(enhancementLevel), enhancementLevel, "Enhancement level cannot be negative.");
            }
        }
    }
}
