using System;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// An immutable reading of a character at one moment.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately scalar.</b> It copies the facts that describe a character and not
    /// the collections behind them: no stat list, no appearance list, no definition
    /// objects. Copying those would be a second model of the same data, which is the thing
    /// worth avoiding; counts are enough to tell an empty character from a populated one,
    /// and anything deeper should be read from the aggregate itself.
    ///
    /// Every field is a stable identifier or a plain value, so a snapshot survives content
    /// patches and exposes no Unity object, no mutable collection and nothing a holder can
    /// write back through.
    ///
    /// Taking one changes nothing, including no revision. It is a read, and reads do not
    /// count as changes anywhere in this project.
    ///
    /// This is a domain read boundary, not a network packet. It carries no serialization
    /// attributes and no networking type; transporting character state is a Contracts and
    /// Network concern that does not exist yet.
    /// </remarks>
    public sealed class CharacterSnapshot
    {
        private CharacterSnapshot()
        {
        }

        public CharacterId CharacterId { get; private set; }

        public OwnerId Owner { get; private set; }

        public string Name { get; private set; }

        public CharacterGender Gender { get; private set; }

        public DefinitionId BaseClass { get; private set; }

        /// <summary><see cref="DefinitionId.None"/> when no job has been taken.</summary>
        public DefinitionId CurrentJob { get; private set; }

        public int Level { get; private set; }

        public long Experience { get; private set; }

        public int CurrentHealth { get; private set; }

        public int CurrentMana { get; private set; }

        /// <summary>How many stats the character holds a value for.</summary>
        public int StatCount { get; private set; }

        /// <summary>How many appearance slots have been chosen.</summary>
        public int AppearanceSelectionCount { get; private set; }

        public Revision IdentityRevision { get; private set; }

        public Revision ClassRevision { get; private set; }

        public Revision AppearanceRevision { get; private set; }

        public Revision ProgressionRevision { get; private set; }

        public Revision StatsRevision { get; private set; }

        public Revision ResourceRevision { get; private set; }

        /// <summary>Reads a character without altering it.</summary>
        public static CharacterSnapshot Capture(Character character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            return new CharacterSnapshot
            {
                CharacterId = character.Identity.CharacterId,
                Owner = character.Identity.Owner,
                Name = character.Identity.Name,
                Gender = character.Identity.Gender,
                BaseClass = character.Class.BaseClass,
                CurrentJob = character.Class.CurrentJob,
                Level = character.Progression.Level,
                Experience = character.Progression.Experience,
                CurrentHealth = character.Resources.CurrentHealth,
                CurrentMana = character.Resources.CurrentMana,
                StatCount = character.Stats.Count,
                AppearanceSelectionCount = CountAppearance(character.Appearance),
                IdentityRevision = character.Identity.Revision,
                ClassRevision = character.Class.Revision,
                AppearanceRevision = character.Appearance.Revision,
                ProgressionRevision = character.Progression.Revision,
                StatsRevision = character.Stats.Revision,
                ResourceRevision = character.Resources.Revision
            };
        }

        private static int CountAppearance(CharacterAppearanceState appearance)
        {
            int count = 0;

            foreach (AppearanceSlot slot in Enum.GetValues(typeof(AppearanceSlot)))
            {
                if (slot != AppearanceSlot.None && appearance.Get(slot).IsValid)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
