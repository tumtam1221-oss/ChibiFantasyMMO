using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Everything a player chooses when making a character.
    /// </summary>
    /// <remarks>
    /// The class is a <see cref="DefinitionId"/>, which is what keeps creation to one code
    /// path. There is no CreateSwordsman and no switch: every starting class travels the
    /// same route and differs only in the id supplied, so adding a fifth class is content.
    ///
    /// Identity is deliberately absent. A caller does not get to choose a character's id;
    /// creation mints one, so a client cannot claim an identity that is not its to claim.
    /// </remarks>
    public sealed class CharacterCreationInput
    {
        private readonly AppearanceChoice[] _appearance;

        public CharacterCreationInput(OwnerId owner, string name, CharacterGender gender,
            DefinitionId startingClass, IList<AppearanceChoice> appearance = null)
        {
            Owner = owner;
            Name = name;
            Gender = gender;
            StartingClass = startingClass;

            if (appearance == null)
            {
                _appearance = new AppearanceChoice[0];
            }
            else
            {
                _appearance = new AppearanceChoice[appearance.Count];
                appearance.CopyTo(_appearance, 0);
            }
        }

        /// <summary>The account the character will belong to.</summary>
        public OwnerId Owner { get; }

        public string Name { get; }

        public CharacterGender Gender { get; }

        /// <summary>Reference to the chosen <see cref="ClassDefinition"/>.</summary>
        public DefinitionId StartingClass { get; }

        /// <summary>Appearance picks, in the order supplied. May be empty.</summary>
        public IReadOnlyList<AppearanceChoice> Appearance => Array.AsReadOnly(_appearance);
    }
}
