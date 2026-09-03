using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One card socketed into a piece of equipment.
    /// </summary>
    /// <remarks>
    /// <b>Three primitives, like <see cref="EquipmentEnchant"/> and for the same reasons.</b>
    /// A card reference, which socket it sits in, and the identity of the copy that went in.
    /// What the card <em>does</em> stays on <see cref="CardDefinition"/>, read at resolve
    /// time, so re-authoring a card updates every piece already carrying it.
    ///
    /// <b>Why the instance id is kept and the stone's is not.</b> A status stone is spent: it
    /// leaves the bag and nothing survives that a player could get back. A card is
    /// <em>moved</em> -- Phase 12 removal returns it -- so the copy has to keep its identity
    /// across the trip, or the card that comes out would be a different object from the one
    /// that went in and its ownership history would be gone. That identity is also what a
    /// future trade needs to say "this exact card".
    ///
    /// <b>Deliberately not an <see cref="EquipmentEnchant"/>.</b> Cards and stones occupy
    /// different sockets, follow different compatibility rules and behave differently on
    /// removal. Sharing the record would have meant sharing
    /// <see cref="EquipmentInstance.Enchants"/>, and a card would then count against a
    /// stone's capacity.
    ///
    /// Flat because it has to persist: one row of a future <c>equipment_card_socket</c>
    /// table is an equipment instance id, a socket index, a card id and a card instance id.
    /// </remarks>
    [Serializable]
    public struct EquipmentCardSocket
    {
        [SerializeField] private DefinitionId _card;
        [SerializeField] private int _socketIndex;
        [SerializeField] private InstanceId _cardInstance;

        public EquipmentCardSocket(DefinitionId card, int socketIndex, InstanceId cardInstance)
        {
            _card = card;
            _socketIndex = socketIndex;
            _cardInstance = cardInstance;
        }

        /// <summary>Reference to the card's <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Card => _card;

        /// <summary>Which socket this occupies. Zero-based, stable across removals.</summary>
        public int SocketIndex => _socketIndex;

        /// <summary>
        /// The exact owned copy that was inserted.
        /// </summary>
        /// <remarks>Preserved so removal returns the same object rather than a fresh one
        /// carrying the same definition. Invalid only for content that predates the field.</remarks>
        public InstanceId CardInstance => _cardInstance;

        public bool IsValid => _card.IsValid && _socketIndex >= 0;

        public override string ToString()
        {
            return "[" + _socketIndex + "] " + _card;
        }
    }
}
