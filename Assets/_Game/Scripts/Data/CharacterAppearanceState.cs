using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A character's persisted appearance choices.
    /// </summary>
    /// <remarks>
    /// <b>A sibling of the character, not a part of it.</b> Kept as its own aggregate
    /// linked by <see cref="CharacterId"/> rather than embedded in
    /// <see cref="CharacterState"/>, so that state does not grow into a monolith as
    /// progression, stats, class, inventory and equipment arrive. Each aggregate carries
    /// its own <see cref="Revision"/>, so changing a hairstyle does not make a character's
    /// progression look stale, and each can later be persisted and transmitted on its own.
    ///
    /// <b>No owner and no gender stored here.</b> Ownership already has one home on
    /// CharacterState, reached through the character this appearance belongs to. Gender
    /// likewise: copying it would create a second source of truth that can silently
    /// diverge from the character's actual gender, so
    /// <see cref="CharacterAppearanceValidator"/> takes it as an argument instead.
    ///
    /// <b>Selections are ids, never assets.</b> A choice is a <see cref="DefinitionId"/>
    /// such as hair_001. A patch may repoint that option at a new mesh or texture and every
    /// saved character keeps the hairstyle they chose. Storing an asset path, a Unity GUID
    /// or a file name would tie a player's choice to a build.
    ///
    /// Unset selections are allowed, because appearance exists before it is complete during
    /// creation. Whether a selection is required is a validation question, not a
    /// construction one.
    /// </remarks>
    [Serializable]
    public sealed class CharacterAppearanceState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private DefinitionId _face;
        [SerializeField] private DefinitionId _eyes;
        [SerializeField] private DefinitionId _hair;
        [SerializeField] private DefinitionId _hairColor;
        [SerializeField] private DefinitionId _skinTone;
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers.</summary>
        public CharacterAppearanceState()
        {
        }

        public CharacterAppearanceState(CharacterId characterId)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException(
                    "Appearance must belong to a character.", nameof(characterId));
            }

            _characterId = characterId;
            _revision = Revision.Initial;
        }

        /// <summary>The character these choices belong to.</summary>
        public CharacterId CharacterId => _characterId;

        public DefinitionId Face => _face;

        public DefinitionId Eyes => _eyes;

        public DefinitionId Hair => _hair;

        public DefinitionId HairColor => _hairColor;

        public DefinitionId SkinTone => _skinTone;

        public Revision Revision => _revision;

        /// <summary>
        /// Reads the current selection for a slot.
        /// </summary>
        /// <remarks>Lets validation and future presentation iterate slots generically
        /// instead of naming each field, so a new slot does not ripple outward.</remarks>
        public DefinitionId Get(AppearanceSlot slot)
        {
            switch (slot)
            {
                case AppearanceSlot.Face: return _face;
                case AppearanceSlot.Eyes: return _eyes;
                case AppearanceSlot.Hair: return _hair;
                case AppearanceSlot.HairColor: return _hairColor;
                case AppearanceSlot.SkinTone: return _skinTone;
                default: return DefinitionId.None;
            }
        }

        /// <summary>
        /// Records a selection for a slot and advances the revision.
        /// </summary>
        /// <remarks>
        /// Stores what it is told. That the id resolves, belongs to this slot and suits the
        /// character's gender is checked by
        /// <see cref="CharacterAppearanceValidator"/> against the content registry, because
        /// this type deliberately cannot see content. A client's chosen appearance is a
        /// request until a server validates it.
        /// </remarks>
        public void Select(AppearanceSlot slot, DefinitionId option)
        {
            switch (slot)
            {
                case AppearanceSlot.Face: _face = option; break;
                case AppearanceSlot.Eyes: _eyes = option; break;
                case AppearanceSlot.Hair: _hair = option; break;
                case AppearanceSlot.HairColor: _hairColor = option; break;
                case AppearanceSlot.SkinTone: _skinTone = option; break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(slot), slot, "Not a selectable appearance slot.");
            }

            _revision = _revision.Next();
        }
    }
}
