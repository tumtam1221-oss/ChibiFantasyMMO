using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player's actual owned card.
    /// </summary>
    /// <remarks>
    /// Identity only, and deliberately so. A card today is fully described by which card it
    /// is and who owns it; everything else it contributes comes from
    /// <see cref="CardDefinition"/> and is identical for every copy.
    ///
    /// Extension path, should cards later gain per-copy state such as a level, a duplicate
    /// count or a socket binding: add the field and a validating setter that calls
    /// <see cref="GameInstance.AdvanceRevision"/>, exactly as
    /// <see cref="EquipmentInstance"/> does for enhancement level. No speculative field is
    /// added now, because a field that exists is a field that gets persisted, migrated and
    /// synchronised before anything uses it.
    ///
    /// Socketing a card into equipment and applying its modifiers is gameplay and lives
    /// elsewhere.
    /// </remarks>
    [Serializable]
    public sealed class CardInstance : GameInstance
    {
        /// <summary>Exists for deserializers.</summary>
        public CardInstance()
        {
        }

        public CardInstance(InstanceId instanceId, DefinitionId cardDefinitionId, OwnerId owner)
            : base(instanceId, cardDefinitionId, owner)
        {
        }
    }
}
