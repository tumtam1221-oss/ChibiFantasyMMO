using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One status stone socketed into a piece of equipment.
    /// </summary>
    /// <remarks>
    /// <b>Three primitives, deliberately.</b> A stone reference, which socket it sits in,
    /// and what rank it was applied at. Everything the stone <em>does</em> stays on the
    /// stone's own definition, read at resolve time -- so re-authoring a stone's modifiers
    /// changes every sword already carrying it, and no owned copy holds a stale duplicate
    /// of authored numbers.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>equipment_enchant</c>
    /// table maps onto one of these: instance id, socket index, stone id, rank. A
    /// polymorphic enchant object would read better in C# and would not survive the trip
    /// through a database.
    ///
    /// <see cref="SocketIndex"/> is kept rather than inferred from list position because a
    /// socket is a place a player can see: removing the stone from socket 1 must not
    /// renumber the stone in socket 2.
    /// </remarks>
    [Serializable]
    public struct EquipmentEnchant
    {
        [SerializeField] private DefinitionId _stone;
        [SerializeField] private int _socketIndex;
        [SerializeField] private int _rank;

        public EquipmentEnchant(DefinitionId stone, int socketIndex, int rank = 1)
        {
            _stone = stone;
            _socketIndex = socketIndex;
            _rank = rank < 1 ? 1 : rank;
        }

        /// <summary>Reference to the stone's <see cref="ItemDefinition"/>.</summary>
        public DefinitionId Stone => _stone;

        /// <summary>Which socket this occupies. Zero-based, stable across removals.</summary>
        public int SocketIndex => _socketIndex;

        /// <summary>
        /// Rank the stone was applied at.
        /// </summary>
        /// <remarks>One unless content authors ranked stones. Kept because a ranked stone
        /// is a different strength of the same stone, not a different stone, and encoding
        /// that as separate definitions would multiply content for no reason.</remarks>
        public int Rank => _rank;

        public bool IsValid => _stone.IsValid && _socketIndex >= 0;

        public override string ToString()
        {
            return "[" + _socketIndex + "] " + _stone + (_rank > 1 ? " r" + _rank : string.Empty);
        }
    }
}
