using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A character's persisted level and progress toward the next one.
    /// </summary>
    /// <remarks>
    /// <b>A sibling aggregate</b>, like appearance: linked by <see cref="CharacterId"/>,
    /// carrying its own <see cref="Revision"/>, so <see cref="CharacterState"/> stays small
    /// as stats, class, inventory and equipment arrive, and so progression can be persisted
    /// and transmitted on its own.
    ///
    /// <b>Experience is progress within the current level, not a lifetime total.</b> That
    /// choice is deliberate. With a lifetime total, level is derived from the curve, so
    /// retuning the curve in a patch silently re-levels every existing character, and can
    /// demote them. Here the level is stored, so a rebalance changes only how far the next
    /// level is, never what a player already earned. The cost is two fields instead of one,
    /// which is a good trade for never having to explain a lost level.
    ///
    /// Experience is a long. Within-level values are small, but at maximum level
    /// experience keeps accumulating, so the type has to tolerate a long-lived character.
    /// Overflow throws rather than wrapping.
    ///
    /// <b>At maximum level experience is retained, not discarded.</b> Levelling stops, the
    /// number keeps growing, and if a later patch raises the cap that banked experience
    /// converts into levels the moment the next gain is applied. Discarding it would make a
    /// cap raise silently unfair to the players who kept playing.
    ///
    /// The curve is passed in rather than looked up: state does not reach into content.
    /// Nothing here is authoritative. Experience gains come from a server in production;
    /// this type only records what it is told.
    /// </remarks>
    [Serializable]
    public sealed class CharacterProgressionState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private int _level;
        [SerializeField] private long _experience;
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers.</summary>
        public CharacterProgressionState()
        {
        }

        /// <summary>Starts a character at the bottom of the supplied curve.</summary>
        public CharacterProgressionState(CharacterId characterId, CharacterProgressionDefinition progression)
        {
            if (progression == null)
            {
                throw new ArgumentNullException(nameof(progression));
            }

            Initialise(characterId, progression.MinLevel, 0L);
        }

        /// <summary>Restores previously persisted progression.</summary>
        /// <remarks>Checks only what is knowable without a curve: a real character, a
        /// positive level and non-negative experience. Whether the level fits a particular
        /// curve is a validation question, answered where the curve is available.</remarks>
        public CharacterProgressionState(CharacterId characterId, int level, long experience)
        {
            Initialise(characterId, level, experience);
        }

        public CharacterId CharacterId => _characterId;

        public int Level => _level;

        /// <summary>Experience earned toward the next level. Never negative.</summary>
        public long Experience => _experience;

        public Revision Revision => _revision;

        /// <summary>
        /// Applies an experience gain, levelling up as many times as it affords.
        /// </summary>
        /// <remarks>
        /// One call advances the revision once, however many levels it crosses. A gain
        /// large enough to cross several levels does so in a single call, so no caller has
        /// to loop.
        ///
        /// Deterministic and integer-only: no randomness, no floating point, no precision
        /// loss. A rejected gain leaves level, experience and revision exactly as they were.
        /// </remarks>
        /// <param name="amount">Experience to add. Must not be negative.</param>
        /// <param name="progression">The curve to level against.</param>
        public void AddExperience(long amount, CharacterProgressionDefinition progression)
        {
            if (progression == null)
            {
                throw new ArgumentNullException(nameof(progression));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "Experience gain cannot be negative.");
            }

            if (!progression.IsLevelInRange(_level))
            {
                throw new ArgumentException(
                    "Current level " + _level + " is outside the supplied curve.", nameof(progression));
            }

            if (_experience > long.MaxValue - amount)
            {
                throw new OverflowException("Experience would exceed the range of a long.");
            }

            // Work on copies so a fault leaves the object untouched.
            int level = _level;
            long experience = _experience + amount;

            while (level < progression.MaxLevel)
            {
                long required = progression.GetExperienceToNextLevel(level);

                if (experience < required)
                {
                    break;
                }

                experience -= required;
                level++;
            }

            _level = level;
            _experience = experience;
            _revision = _revision.Next();
        }

        /// <summary>True when this character has reached the top of the supplied curve.</summary>
        public bool IsAtMaxLevel(CharacterProgressionDefinition progression)
        {
            if (progression == null)
            {
                throw new ArgumentNullException(nameof(progression));
            }

            return _level >= progression.MaxLevel;
        }

        private void Initialise(CharacterId characterId, int level, long experience)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException(
                    "Progression must belong to a character.", nameof(characterId));
            }

            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Level starts at one.");
            }

            if (experience < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(experience), experience, "Experience cannot be negative.");
            }

            _characterId = characterId;
            _level = level;
            _experience = experience;
            _revision = Revision.Initial;
        }
    }
}
