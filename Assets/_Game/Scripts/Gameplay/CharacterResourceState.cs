using System;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// How much health and mana a character currently has.
    /// </summary>
    /// <remarks>
    /// <b>Runtime, not persistent.</b> Implements <see cref="IRuntimeState"/> and
    /// deliberately not <see cref="IPersistentState"/>. It is also not marked serializable:
    /// the runtime contract carries no serialization obligation, and adding one would
    /// invite this being written to a database, where it would become a second truth
    /// competing with the derived stats that bound it.
    ///
    /// <b>Maxima are never stored here.</b> Every operation takes a
    /// <see cref="ResourceLimits"/> read from the derived-stat layer. A stored ceiling goes
    /// stale as soon as a level, an item or a buff changes the calculation, and a stale
    /// ceiling is exactly how a character ends up holding more health than they can have.
    /// This type also computes nothing: the calculator owns what a maximum is.
    ///
    /// <b>Clamping is the contract.</b> Values are clamped into range rather than rejected,
    /// so a change of plus or minus a million lands on the ceiling or on zero. One
    /// consequence worth stating: value operations have no failure mode, which makes
    /// atomicity total rather than merely careful. The only way to fail is to construct the
    /// thing wrongly, and that happens before any state exists.
    ///
    /// <b>A change that changes nothing is not a change.</b> Setting health to the value it
    /// already holds leaves the revision alone, so the counter tracks real transitions
    /// rather than call volume.
    ///
    /// Nothing here is damage, healing, death or regeneration. Zero health is a number, not
    /// a state of being; no flag, timer or tick exists. Combat will later call these
    /// primitives, and in production only a server will.
    /// </remarks>
    public sealed class CharacterResourceState : IRuntimeState
    {
        private int _currentHealth;
        private int _currentMana;
        private Revision _revision;

        /// <summary>Creates a character at full health and mana.</summary>
        public static CharacterResourceState CreateFull(CharacterId characterId, ResourceLimits limits)
        {
            return new CharacterResourceState(
                characterId, limits, limits.MaxHealth, limits.MaxMana);
        }

        /// <summary>Creates a character with explicit resources, clamped into range.</summary>
        public CharacterResourceState(CharacterId characterId, ResourceLimits limits,
            int currentHealth, int currentMana)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException(
                    "Resources must belong to a character.", nameof(characterId));
            }

            CharacterId = characterId;
            _currentHealth = Clamp(currentHealth, limits.MaxHealth);
            _currentMana = Clamp(currentMana, limits.MaxMana);
            _revision = Revision.Initial;
        }

        /// <summary>The character these resources belong to. Never changes.</summary>
        public CharacterId CharacterId { get; }

        /// <summary>Current health. Never negative, never above the supplied ceiling.</summary>
        public int CurrentHealth => _currentHealth;

        /// <summary>Current mana. Never negative, never above the supplied ceiling.</summary>
        public int CurrentMana => _currentMana;

        public Revision Revision => _revision;

        /// <summary>Sets health, clamped, advancing the revision only if it actually moved.</summary>
        public void SetHealth(int value, ResourceLimits limits)
        {
            Apply(Clamp(value, limits.MaxHealth), _currentMana);
        }

        /// <summary>Sets mana, clamped, advancing the revision only if it actually moved.</summary>
        public void SetMana(int value, ResourceLimits limits)
        {
            Apply(_currentHealth, Clamp(value, limits.MaxMana));
        }

        /// <summary>
        /// Adjusts health by a delta, clamped.
        /// </summary>
        /// <remarks>The delta is a long so a caller cannot overflow an int before the value
        /// even arrives; the sum is computed in long and clamped back into range.</remarks>
        public void ChangeHealth(long delta, ResourceLimits limits)
        {
            Apply(Clamp(SaturatingAdd(_currentHealth, delta), limits.MaxHealth), _currentMana);
        }

        /// <summary>Adjusts mana by a delta, clamped.</summary>
        public void ChangeMana(long delta, ResourceLimits limits)
        {
            Apply(_currentHealth, Clamp(SaturatingAdd(_currentMana, delta), limits.MaxMana));
        }

        /// <summary>
        /// Brings both resources back inside a new set of ceilings.
        /// </summary>
        /// <remarks>
        /// Called after the derived stats are recalculated. Behaviour is plain clamping: if
        /// a maximum drops below what the character is holding, the surplus is lost. No
        /// percentage is preserved and no ratio is restored, because those are balance
        /// decisions with visible consequences, and inventing one here would quietly commit
        /// the game to it.
        /// </remarks>
        public void ClampTo(ResourceLimits limits)
        {
            Apply(Clamp(_currentHealth, limits.MaxHealth), Clamp(_currentMana, limits.MaxMana));
        }

        /// <summary>Whether health is at the supplied ceiling.</summary>
        public bool IsHealthFull(ResourceLimits limits)
        {
            return _currentHealth >= limits.MaxHealth;
        }

        /// <summary>Whether mana is at the supplied ceiling.</summary>
        public bool IsManaFull(ResourceLimits limits)
        {
            return _currentMana >= limits.MaxMana;
        }

        private void Apply(int health, int mana)
        {
            if (health == _currentHealth && mana == _currentMana)
            {
                return;
            }

            _currentHealth = health;
            _currentMana = mana;
            _revision = _revision.Next();
        }

        /// <summary>
        /// Adds a delta to a current value without ever wrapping.
        /// </summary>
        /// <remarks>
        /// A delta near long.MaxValue would overflow a plain addition and wrap to a large
        /// negative, which would read as a drain rather than a fill. Saturating at the
        /// extremes keeps the subsequent clamp meaningful.
        /// </remarks>
        private static long SaturatingAdd(int current, long delta)
        {
            if (delta > 0 && current > long.MaxValue - delta)
            {
                return long.MaxValue;
            }

            if (delta < 0 && current < long.MinValue - delta)
            {
                return long.MinValue;
            }

            return current + delta;
        }

        private static int Clamp(long value, int max)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= max ? max : (int)value;
        }
    }
}
