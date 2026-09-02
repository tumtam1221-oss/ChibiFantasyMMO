using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity of a player character.
    /// </summary>
    /// <remarks>
    /// A distinct type from <see cref="InstanceId"/> and <see cref="OwnerId"/> even though
    /// all three are string-backed, because they answer different questions and confusing
    /// them is silent and costly. An InstanceId names an owned item, pet or card; an
    /// OwnerId names the authority a thing belongs to; a CharacterId names the character
    /// itself. A character is not an owned content instance, so it does not borrow the
    /// instance identity space.
    ///
    /// Backed by a GUID in "N" form using the same proven approach as InstanceId, and
    /// sharing its hashing through <see cref="IdentityHash"/> rather than repeating it.
    ///
    /// Minted once and stored. Never derived from a Unity instance ID, GameObject, scene
    /// object, asset GUID, array index or position in a player list, so a character stays
    /// the same character across logout, reconnect, server restart, database persistence,
    /// client and server patches, and asset replacement.
    ///
    /// A CharacterId arriving from a client proves nothing. The server will have to confirm
    /// that the authenticated owner actually holds the character it names; that check is an
    /// authority concern and does not belong in a value type.
    /// </remarks>
    [Serializable]
    public struct CharacterId : IEquatable<CharacterId>
    {
        [SerializeField] private string _value;

        public CharacterId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        /// <summary>The absent character identity.</summary>
        public static CharacterId None => default;

        /// <summary>
        /// Mints a new, previously unused character identity.
        /// </summary>
        /// <remarks>In production only the server should mint these.</remarks>
        public static CharacterId New()
        {
            return new CharacterId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(CharacterId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is CharacterId other && Equals(other);
        }

        /// <summary>Deterministic FNV-1a 32-bit hash, stable across runs and platforms.</summary>
        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? string.Empty;
        }

        public static bool operator ==(CharacterId left, CharacterId right) => left.Equals(right);

        public static bool operator !=(CharacterId left, CharacterId right) => !left.Equals(right);
    }
}
