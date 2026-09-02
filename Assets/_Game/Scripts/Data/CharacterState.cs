using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player character as persisted data.
    /// </summary>
    /// <remarks>
    /// <b>A character is data, not a scene object.</b> Not a GameObject, MonoBehaviour,
    /// NetworkBehaviour, NetworkObject or ScriptableObject. The runtime representation that
    /// eventually walks around a map is a separate presentation object built from this, and
    /// it does not exist yet. Keeping the persisted character free of Unity object identity
    /// is what lets a server hold thousands of them with no scene loaded.
    ///
    /// <b>Not a GameInstance.</b> <see cref="GameInstance"/> models an owned copy of
    /// authored content, which is why it carries a DefinitionId: an item instance is
    /// meaningless without the item it is a copy of. A character is not a copy of a
    /// definition, it is the player entity that owns copies. The two share identity, owner
    /// and revision, but sharing three members is not a reason to inherit a concept that
    /// does not apply. It implements <see cref="IPersistentState"/> directly instead.
    ///
    /// <b>Scope.</b> Identity and profile only. No level, experience, stats, class, job,
    /// map, position, inventory, equipment, skills, pets, cards or Devil Fruits. Those are
    /// later steps, and each will reference content by DefinitionId rather than storing
    /// names or types, so a character stays valid when class and job content is patched.
    ///
    /// <b>Authority.</b> Nothing here is authoritative. A character record supplied by a
    /// client proves only what the client claims; the server must confirm that the
    /// authenticated owner holds this character before trusting any of it.
    /// </remarks>
    [Serializable]
    public sealed class CharacterState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private OwnerId _owner;
        [SerializeField] private string _name;
        [SerializeField] private CharacterGender _gender;
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers, which construct before populating.</summary>
        public CharacterState()
        {
        }

        public CharacterState(CharacterId characterId, OwnerId owner, string name, CharacterGender gender)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException("A character requires a valid identity.", nameof(characterId));
            }

            if (!owner.IsValid)
            {
                throw new ArgumentException(
                    "A character must belong to an owner.", nameof(owner));
            }

            ValidateName(name);

            if (gender == CharacterGender.Unspecified)
            {
                throw new ArgumentException(
                    "A character must have a chosen gender.", nameof(gender));
            }

            _characterId = characterId;
            _owner = owner;
            _name = name;
            _gender = gender;
            _revision = Revision.Initial;
        }

        /// <summary>Stable identity. Never changes for the life of the character.</summary>
        public CharacterId CharacterId => _characterId;

        /// <summary>The account or authority this character belongs to.</summary>
        /// <remarks>No transfer method is offered. Moving a character between owners is an
        /// account operation with consequences well beyond this record.</remarks>
        public OwnerId Owner => _owner;

        public string Name => _name;

        /// <summary>Chosen at creation. No change method is offered here.</summary>
        /// <remarks>Whether a gender change is possible at all is a product decision with
        /// appearance and content implications, so the capability is not assumed.</remarks>
        public CharacterGender Gender => _gender;

        public Revision Revision => _revision;

        /// <summary>Renames the character and advances the revision.</summary>
        /// <remarks>
        /// Validates only that a name was actually supplied. Length limits, allowed
        /// characters, uniqueness across a shard and profanity filtering are backend rules
        /// enforced where the account and character tables live, and inventing them here
        /// would produce a client-side check the server neither shares nor trusts.
        /// </remarks>
        public void Rename(string name)
        {
            ValidateName(name);
            _name = name;
            _revision = _revision.Next();
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A character requires a name.", nameof(name));
            }
        }
    }
}
