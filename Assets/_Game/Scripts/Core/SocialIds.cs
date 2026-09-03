using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity of one party.
    /// </summary>
    /// <remarks>
    /// A party is named by this and never by its members or its leader: leadership changes,
    /// members come and go, and a party that identified itself by either would become a
    /// different party every time somebody left.
    ///
    /// Its own type rather than an <see cref="InstanceId"/>, for the reason
    /// <see cref="OwnerId"/> gives: the compiler then refuses to pass a character's identity
    /// where a party's belongs, and those are exactly the two most easily transposed in a
    /// membership call.
    ///
    /// Same GUID-in-"N"-form representation as every other identity here, so it maps onto a
    /// <c>CHAR(32)</c> column with no translation.
    /// </remarks>
    [Serializable]
    public struct PartyId : IEquatable<PartyId>
    {
        [SerializeField] private string _value;

        public PartyId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static PartyId None => default;

        /// <summary>Mints a new identity. In production only the server does this.</summary>
        public static PartyId New()
        {
            return new PartyId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(PartyId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PartyId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? "<none>";
        }

        public static bool operator ==(PartyId left, PartyId right) => left.Equals(right);

        public static bool operator !=(PartyId left, PartyId right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable identity of one guild.
    /// </summary>
    /// <remarks>
    /// Distinct from the guild's <em>name</em>, which is a display property a leader may
    /// change. Renaming a guild must not orphan its members, its ranks or its audit records,
    /// and that is only true if nothing keys on the name.
    /// </remarks>
    [Serializable]
    public struct GuildId : IEquatable<GuildId>
    {
        [SerializeField] private string _value;

        public GuildId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static GuildId None => default;

        public static GuildId New()
        {
            return new GuildId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(GuildId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GuildId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? "<none>";
        }

        public static bool operator ==(GuildId left, GuildId right) => left.Equals(right);

        public static bool operator !=(GuildId left, GuildId right) => !left.Equals(right);
    }
}
