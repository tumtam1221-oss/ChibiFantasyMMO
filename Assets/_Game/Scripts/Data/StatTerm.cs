using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One contribution to a derived stat: a source stat scaled by a rational factor.
    /// </summary>
    /// <remarks>
    /// The factor is a numerator over a denominator rather than a float on purpose. A
    /// derived stat is a number players compare, argue about and screenshot, so it must
    /// come out the same on every machine. Integer arithmetic gives that; a float
    /// coefficient would leave the last digit at the mercy of the platform.
    ///
    /// A term is authored, not written in code, so "half of agility" is content a designer
    /// can retune without a build.
    /// </remarks>
    [Serializable]
    public struct StatTerm
    {
        [SerializeField] private DefinitionId _source;
        [SerializeField] private int _numerator;
        [SerializeField] private int _denominator;

        public StatTerm(DefinitionId source, int numerator, int denominator)
        {
            _source = source;
            _numerator = numerator;
            _denominator = denominator;
        }

        /// <summary>Reference to the primary <see cref="StatDefinition"/> being scaled.</summary>
        public DefinitionId Source => _source;

        public int Numerator => _numerator;

        /// <summary>Must be greater than zero; a zero denominator is a content fault.</summary>
        public int Denominator => _denominator;
    }
}
