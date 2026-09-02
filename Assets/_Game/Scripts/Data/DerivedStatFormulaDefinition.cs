using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// How one derived stat is computed from primary stats.
    /// </summary>
    /// <remarks>
    /// <b>No balance lives in code.</b> Whether maximum health is ten times vitality or
    /// twelve is a design decision that belongs in an asset, so a rebalance is a content
    /// patch rather than a build. Nothing in this project multiplies a named stat by a
    /// named number.
    ///
    /// <b>Derived identity reuses <see cref="StatDefinition"/>.</b> Maximum health,
    /// physical attack and the rest are stat definitions with
    /// <see cref="StatDefinition.IsPrimary"/> false, referenced by
    /// <see cref="DefinitionId"/> like everything else. No parallel derived-stat identity
    /// exists, and their clamps come from the min and max those definitions already carry.
    ///
    /// <b>Sources must be primary stats.</b> That single restriction is the whole of the
    /// cycle protection: a formula can only read the six attributes a character actually
    /// stores, so the dependency graph is one level deep and a derived stat cannot, even
    /// indirectly, depend on itself. Allowing derived stats to feed each other would need
    /// real cycle detection, which is a graph engine this step does not need.
    /// <see cref="DerivedStatFormulaValidationRule"/> enforces it.
    /// </remarks>
    public sealed class DerivedStatFormulaDefinition : GameDefinition
    {
        [SerializeField] private DefinitionId _derivedStat;
        [SerializeField] private int _constant;
        [SerializeField] private StatTerm[] _terms = new StatTerm[0];

        /// <summary>The <see cref="StatDefinition"/> this formula produces.</summary>
        public DefinitionId DerivedStat => _derivedStat;

        /// <summary>Value before any term is added. May be zero.</summary>
        public int Constant => _constant;

        /// <summary>Contributions, applied in authored order so the result is reproducible.</summary>
        public StatTerm[] Terms => _terms;
    }
}
