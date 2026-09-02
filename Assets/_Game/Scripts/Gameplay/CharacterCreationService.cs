using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Turns a set of player choices into a fully initialised character.
    /// </summary>
    /// <remarks>
    /// <b>One path for every class.</b> The starting class is a DefinitionId, so Swordsman,
    /// Cleric, Mage and Archer travel identical code and a fifth class is an asset. No
    /// switch enumerates classes and no class-specific type exists.
    ///
    /// <b>Atomic.</b> Everything is validated before anything is built. A rejected input
    /// yields a report and nothing else: no half-made character, no minted identity, no
    /// touched registry, no altered definition. Identity is minted only once the input is
    /// known to be sound, so a failed attempt cannot consume one.
    ///
    /// <b>Nothing is recomputed.</b> Starting stats come from the class's authored base
    /// stats, derived stats from the existing calculator, maximum health and mana from the
    /// derived result, current resources from the existing resource rules, and the starting
    /// level from the progression curve. This orchestrates; it owns no formula.
    ///
    /// <b>A starting class is not a job.</b> A new character holds no job, and no
    /// job-change logic runs, so eligibility rules cannot be bypassed through creation.
    ///
    /// Class and job stat modifiers are deliberately not applied. They sit on the
    /// definitions and reach the derived layer later as StatModifier inputs.
    /// </remarks>
    public sealed class CharacterCreationService
    {
        private static readonly StatModifier[] NoModifiers = new StatModifier[0];

        private readonly CharacterCreationValidator _validator = new CharacterCreationValidator();
        private readonly DerivedStatsCalculator _calculator = new DerivedStatsCalculator();

        /// <summary>
        /// Creates a character if the input is valid.
        /// </summary>
        /// <returns>True when a character was produced. On false <paramref name="created"/>
        /// is null and the report says why.</returns>
        public bool TryCreate(CharacterCreationInput input, CharacterCreationContent content,
            out Character created, out ValidationReport report)
        {
            created = null;
            report = _validator.Validate(input, content, out ClassDefinition startingClass);

            if (!report.IsValid)
            {
                return false;
            }

            created = Build(input, content, startingClass);
            return true;
        }

        private Character Build(CharacterCreationInput input, CharacterCreationContent content,
            ClassDefinition startingClass)
        {
            CharacterId characterId = CharacterId.New();

            var identity = new CharacterState(characterId, input.Owner, input.Name, input.Gender);
            var characterClass = new CharacterClassState(characterId, startingClass.Id);
            var progression = new CharacterProgressionState(characterId, content.Progression);
            CharacterAppearanceState appearance = BuildAppearance(characterId, input);
            CharacterStatsState stats = BuildStats(characterId, startingClass);

            DerivedStatsResult derived = _calculator.Calculate(
                stats, content.DerivedFormulas, content.Stats, NoModifiers);

            ResourceLimits limits = ResourceLimits.From(
                derived, content.MaxHealthStat, content.MaxManaStat);

            CharacterResourceState resources = CharacterResourceState.CreateFull(characterId, limits);

            return new Character(identity, characterClass, appearance, progression, stats, resources);
        }

        private static CharacterAppearanceState BuildAppearance(CharacterId characterId,
            CharacterCreationInput input)
        {
            var appearance = new CharacterAppearanceState(characterId);
            IReadOnlyList<AppearanceChoice> choices = input.Appearance;

            for (int i = 0; i < choices.Count; i++)
            {
                appearance.Select(choices[i].Slot, choices[i].Option);
            }

            return appearance;
        }

        /// <summary>
        /// Copies the class's authored base stats onto the character.
        /// </summary>
        /// <remarks>
        /// Authored content is float because curves legitimately want fractions, while a
        /// character's base stats are counted. The conversion truncates toward zero, which
        /// is the convention every other integer division in this project follows, and
        /// clamps at zero because a base stat cannot be negative. No balance is invented
        /// here: the numbers are whatever the class asset says.
        /// </remarks>
        private static CharacterStatsState BuildStats(CharacterId characterId, ClassDefinition startingClass)
        {
            var stats = new CharacterStatsState(characterId);
            StatValue[] authored = startingClass.BaseStats;

            if (authored == null)
            {
                return stats;
            }

            for (int i = 0; i < authored.Length; i++)
            {
                StatValue value = authored[i];

                if (!value.Stat.IsValid)
                {
                    continue;
                }

                int whole = (int)value.Value;
                stats.Set(value.Stat, whole < 0 ? 0 : whole);
            }

            return stats;
        }
    }
}
