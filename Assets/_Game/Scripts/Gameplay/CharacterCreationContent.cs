using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The authored content creation needs to turn a choice into a character.
    /// </summary>
    /// <remarks>
    /// Bundled and passed in rather than looked up, so creation reaches into no registry,
    /// singleton or scene, and a test can build a whole world from fixtures. Nothing here
    /// is modified by creation.
    ///
    /// The two stat ids are carried because maximum health and maximum mana are content,
    /// not constants; nothing in this layer knows what they are called.
    /// </remarks>
    public sealed class CharacterCreationContent
    {
        private readonly DerivedStatFormulaDefinition[] _formulas;

        public CharacterCreationContent(
            IDefinitionRegistry<ClassDefinition> classes,
            IDefinitionRegistry<StatDefinition> stats,
            IDefinitionRegistry<AppearanceOptionDefinition> appearanceOptions,
            IList<DerivedStatFormulaDefinition> derivedFormulas,
            CharacterProgressionDefinition progression,
            DefinitionId maxHealthStat,
            DefinitionId maxManaStat)
        {
            Classes = classes ?? throw new ArgumentNullException(nameof(classes));
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            AppearanceOptions = appearanceOptions ?? throw new ArgumentNullException(nameof(appearanceOptions));
            Progression = progression ?? throw new ArgumentNullException(nameof(progression));
            MaxHealthStat = maxHealthStat;
            MaxManaStat = maxManaStat;

            if (derivedFormulas == null)
            {
                _formulas = new DerivedStatFormulaDefinition[0];
            }
            else
            {
                _formulas = new DerivedStatFormulaDefinition[derivedFormulas.Count];
                derivedFormulas.CopyTo(_formulas, 0);
            }
        }

        public IDefinitionRegistry<ClassDefinition> Classes { get; }

        public IDefinitionRegistry<StatDefinition> Stats { get; }

        public IDefinitionRegistry<AppearanceOptionDefinition> AppearanceOptions { get; }

        /// <summary>Formulas the derived-stat calculator will evaluate.</summary>
        public IReadOnlyList<DerivedStatFormulaDefinition> DerivedFormulas => Array.AsReadOnly(_formulas);

        /// <summary>The level curve a new character starts on. Supplies the starting level.</summary>
        public CharacterProgressionDefinition Progression { get; }

        public DefinitionId MaxHealthStat { get; }

        public DefinitionId MaxManaStat { get; }
    }
}
