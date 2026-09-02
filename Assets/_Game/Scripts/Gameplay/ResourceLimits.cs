using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The ceilings a character's resources are currently bounded by.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="DerivedStatsResult"/> and handed to each resource operation
    /// rather than stored on the resource state. That is the whole point: a stored maximum
    /// goes stale the moment a level, a piece of equipment or a buff changes the derived
    /// stats, and a stale ceiling is how a character ends up with more health than they can
    /// have. Passing it in means the caller must always supply a current figure.
    ///
    /// The stat ids are supplied by the caller because maximum health and maximum mana are
    /// content, not constants. Nothing here knows what they are called.
    ///
    /// A missing derived stat reads as zero rather than as an error, which keeps a
    /// character with no computed maximum in a valid, if empty, state.
    /// </remarks>
    public readonly struct ResourceLimits
    {
        public ResourceLimits(int maxHealth, int maxMana)
        {
            if (maxHealth < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxHealth), maxHealth, "A maximum cannot be negative.");
            }

            if (maxMana < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxMana), maxMana, "A maximum cannot be negative.");
            }

            MaxHealth = maxHealth;
            MaxMana = maxMana;
        }

        public int MaxHealth { get; }

        public int MaxMana { get; }

        /// <summary>Both ceilings at zero. A valid state, not an error.</summary>
        public static ResourceLimits None => default;

        /// <summary>
        /// Reads the ceilings out of a derived-stat result.
        /// </summary>
        /// <remarks>
        /// The calculator remains the only thing that decides what a maximum is; this only
        /// looks the answer up. No formula is duplicated here.
        /// </remarks>
        /// <param name="derived">Computed stats for the character.</param>
        /// <param name="maxHealthStat">Id of the maximum-health stat.</param>
        /// <param name="maxManaStat">Id of the maximum-mana stat.</param>
        public static ResourceLimits From(DerivedStatsResult derived,
            DefinitionId maxHealthStat, DefinitionId maxManaStat)
        {
            if (derived == null)
            {
                throw new ArgumentNullException(nameof(derived));
            }

            int health = derived.TryGet(maxHealthStat, out int hp) ? hp : 0;
            int mana = derived.TryGet(maxManaStat, out int mp) ? mp : 0;

            // A derived stat is already clamped by its definition, but a definition with a
            // negative minimum would let a negative through, and a ceiling must not be.
            return new ResourceLimits(health < 0 ? 0 : health, mana < 0 ? 0 : mana);
        }
    }
}
