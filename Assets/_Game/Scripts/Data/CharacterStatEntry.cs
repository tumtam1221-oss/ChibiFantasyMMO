using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One stat and the value a character has in it.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not <see cref="StatValue"/>.</b> The two look alike and are
    /// deliberately different. StatValue pairs a stat with a <c>float</c> and describes
    /// authored content: a monster's base STR, a class's starting VIT. This pairs a stat
    /// with an <c>int</c> and describes a living player's persisted state.
    ///
    /// The integer is the point. A player's base stats are counted, not measured. Once
    /// modifiers from equipment, cards, pets and buffs start being summed, float base
    /// values would let rounding drift accumulate into a stat a player can see, and a
    /// database column would have to store an approximation of a whole number. StatValue
    /// stays float because authored curves legitimately want fractions; this stays exact.
    ///
    /// Reusing StatValue was considered and rejected. Changing it to int would have
    /// rewritten ClassDefinition and MonsterDefinition, which is a refactor of a completed
    /// phase, and storing integers in a float field would state the wrong contract.
    ///
    /// The stat is named by <see cref="DefinitionId"/>, never by an enum ordinal, array
    /// index or field name, so a saved character survives new stats being added and the
    /// stat list being reordered.
    /// </remarks>
    [Serializable]
    public struct CharacterStatEntry
    {
        [SerializeField] private DefinitionId _stat;
        [SerializeField] private int _value;

        public CharacterStatEntry(DefinitionId stat, int value)
        {
            _stat = stat;
            _value = value;
        }

        /// <summary>Reference to a <see cref="StatDefinition"/>.</summary>
        public DefinitionId Stat => _stat;

        public int Value => _value;
    }
}
