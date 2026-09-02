using System;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The aggregates a freshly created character consists of.
    /// </summary>
    /// <remarks>
    /// A composition, not a god object: it holds the existing aggregates side by side and
    /// adds no state of its own. Each keeps its own revision and can be persisted,
    /// transmitted and validated separately, which is the arrangement every step since
    /// 05.2 has been building toward.
    ///
    /// Resources are the one runtime member; the rest are persistent. That split is the
    /// point, and it is why they are not flattened into a single record.
    /// </remarks>
    public sealed class NewCharacter
    {
        public NewCharacter(CharacterState identity, CharacterClassState characterClass,
            CharacterAppearanceState appearance, CharacterProgressionState progression,
            CharacterStatsState stats, CharacterResourceState resources)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Class = characterClass ?? throw new ArgumentNullException(nameof(characterClass));
            Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
            Progression = progression ?? throw new ArgumentNullException(nameof(progression));
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        /// <summary>Name, gender, owner and the character id every other aggregate shares.</summary>
        public CharacterState Identity { get; }

        public CharacterClassState Class { get; }

        public CharacterAppearanceState Appearance { get; }

        public CharacterProgressionState Progression { get; }

        public CharacterStatsState Stats { get; }

        /// <summary>Runtime, not persistent. Recomputed rather than saved.</summary>
        public CharacterResourceState Resources { get; }
    }
}
