using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>Whether a character can be played right now, and why not.</summary>
    /// <remarks>
    /// Decided by the authority. A character being mid-transfer, locked for a rename or
    /// scheduled for deletion is a fact the account system holds; a client that decided for
    /// itself would let a player enter the world as a character the server is busy moving.
    /// </remarks>
    public enum CharacterAvailability
    {
        /// <summary>Not known. Treated as unplayable, because unknown is not permission.</summary>
        Unknown = 0,

        Playable = 1,

        /// <summary>Marked for deletion.</summary>
        PendingDeletion = 2,

        /// <summary>Held by the authority: a transfer, a rename, a support action.</summary>
        Locked = 3,

        /// <summary>Already in the world on some session.</summary>
        InWorld = 4
    }

    /// <summary>
    /// One row of a character-select screen.
    /// </summary>
    /// <remarks>
    /// <b>A read model, deliberately thin.</b> It is what a list needs to draw and to let a
    /// player choose: who the character is, what they look like in summary, where they left
    /// off. It is emphatically not <see cref="CharacterState"/> -- no stats, no inventory, no
    /// equipment, no skills, no quests. Copying the persistent character into a UI row would
    /// mean a screen holding a second, diverging copy of authoritative state, and would send
    /// a player's whole estate over the wire to draw a name.
    ///
    /// The full state is loaded by the game server after enter-world, from the database, once.
    ///
    /// <b>Where it lives.</b> In Data rather than Contracts because it needs
    /// <see cref="CharacterGender"/>, which is authored-content vocabulary; Contracts
    /// deliberately depends on Core alone. It sits beside <see cref="CharacterState"/>, which
    /// is the thing it summarises.
    ///
    /// <b>Location reuses Phase 11.</b> <see cref="Map"/> is a <see cref="DefinitionId"/>
    /// naming a <see cref="MapDefinition"/>, the same reference
    /// <c>CharacterLocationState</c> holds. There is no second location model and no scene
    /// name here.
    ///
    /// Flat because it has to travel and to persist: one row of a future join across
    /// <c>character</c> and <c>character_account</c>.
    /// </remarks>
    public readonly struct CharacterSelectEntry
    {
        public CharacterSelectEntry(CharacterId character, string name, CharacterGender gender,
            int level, DefinitionId characterClass, DefinitionId job, DefinitionId map,
            DefinitionId appearance = default,
            CharacterAvailability availability = CharacterAvailability.Playable,
            long lastPlayedTicks = 0L, Revision revision = default)
        {
            Character = character;
            Name = name;
            Gender = gender;
            Level = level;
            Class = characterClass;
            Job = job;
            Map = map;
            Appearance = appearance;
            Availability = availability;
            LastPlayedTicks = lastPlayedTicks;
            Revision = revision;
        }

        public CharacterId Character { get; }

        /// <summary>Display only. Never an identity and never used to look anything up.</summary>
        public string Name { get; }

        public CharacterGender Gender { get; }

        public int Level { get; }

        /// <summary>Reference to a <see cref="ClassDefinition"/>.</summary>
        public DefinitionId Class { get; }

        /// <summary>Reference to a <see cref="JobDefinition"/>. Invalid when unjobbed.</summary>
        public DefinitionId Job { get; }

        /// <summary>Reference to the <see cref="MapDefinition"/> they logged out on.</summary>
        public DefinitionId Map { get; }

        /// <summary>
        /// A single reference standing for the character's look.
        /// </summary>
        /// <remarks>One id rather than a copy of <see cref="CharacterAppearanceState"/>: a
        /// select screen needs enough to show a preview, and duplicating every appearance slot
        /// into a list row would be the same mistake as duplicating the character state.</remarks>
        public DefinitionId Appearance { get; }

        public CharacterAvailability Availability { get; }

        /// <summary>When they were last played. Zero means never, or not recorded.</summary>
        public long LastPlayedTicks { get; }

        /// <summary>
        /// The character's revision when this row was built.
        /// </summary>
        /// <remarks>Carried so a selection made against a stale list can be refused rather
        /// than acted on -- the same optimistic-concurrency shape Phase 13 uses for items.</remarks>
        public Revision Revision { get; }

        public bool IsValid => Character.IsValid;

        public bool IsPlayable => Availability == CharacterAvailability.Playable;

        public override string ToString()
        {
            return Name + " (" + Character + ", level " + Level + ")";
        }
    }
}
