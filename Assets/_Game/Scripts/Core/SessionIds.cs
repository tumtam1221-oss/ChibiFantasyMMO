using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity of one account.
    /// </summary>
    /// <remarks>
    /// <b>Not a login name.</b> A player may change what they sign in with; the account they
    /// signed into is the same account. Anything that keyed on the name would orphan every
    /// character, item and audit record the moment it changed.
    ///
    /// <b>Its relationship to <see cref="OwnerId"/>.</b> Ownership of items, equipment and
    /// wallets has been expressed as an <see cref="OwnerId"/> since Phase 08, and that type
    /// already documents itself as originating in the account system. This is that account's
    /// identity in the account system's own vocabulary; <c>AccountIdentity.ToOwnerId</c>
    /// projects one onto the other. They are deliberately not merged: an owner may one day be
    /// a guild or a character, and an account never will be. Nothing here duplicates
    /// ownership -- it names the authority ownership comes from.
    ///
    /// Same GUID-in-"N"-form representation as every other identity, so it maps onto a
    /// <c>CHAR(32)</c> column with no translation.
    /// </remarks>
    [Serializable]
    public struct AccountId : IEquatable<AccountId>
    {
        [SerializeField] private string _value;

        public AccountId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static AccountId None => default;

        /// <summary>Mints a new identity. In production only the account system does this.</summary>
        public static AccountId New()
        {
            return new AccountId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(AccountId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is AccountId other && Equals(other);

        public override int GetHashCode() => IdentityHash.Fnv1a(_value);

        public override string ToString() => _value ?? "<none>";

        public static bool operator ==(AccountId left, AccountId right) => left.Equals(right);

        public static bool operator !=(AccountId left, AccountId right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable identity of one signed-in session.
    /// </summary>
    /// <remarks>
    /// <b>Issued by the authority, never by the client.</b> A client that could mint one of
    /// these could mint somebody else's. The identity arrives in a login result and is quoted
    /// back on every later request; the authority looks it up rather than believing it.
    ///
    /// <b>Not a security token.</b> It names a session so the authority can find it. It
    /// carries no claim, proves nothing on its own, and must never be treated as
    /// authorisation -- see <c>SessionToken</c> for the separate, deliberately opaque
    /// transport concern.
    /// </remarks>
    [Serializable]
    public struct SessionId : IEquatable<SessionId>
    {
        [SerializeField] private string _value;

        public SessionId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static SessionId None => default;

        public static SessionId New()
        {
            return new SessionId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(SessionId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is SessionId other && Equals(other);

        public override int GetHashCode() => IdentityHash.Fnv1a(_value);

        public override string ToString() => _value ?? "<none>";

        public static bool operator ==(SessionId left, SessionId right) => left.Equals(right);

        public static bool operator !=(SessionId left, SessionId right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable identity of one game server.
    /// </summary>
    /// <remarks>
    /// Never an index and never a display name. "Server 1" is what a player reads; renaming
    /// it, reordering the list or retiring a server must not change which server a character
    /// lives on.
    /// </remarks>
    [Serializable]
    public struct ServerId : IEquatable<ServerId>
    {
        [SerializeField] private string _value;

        public ServerId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static ServerId None => default;

        public static ServerId New()
        {
            return new ServerId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(ServerId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is ServerId other && Equals(other);

        public override int GetHashCode() => IdentityHash.Fnv1a(_value);

        public override string ToString() => _value ?? "<none>";

        public static bool operator ==(ServerId left, ServerId right) => left.Equals(right);

        public static bool operator !=(ServerId left, ServerId right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable identity of one channel.
    /// </summary>
    /// <remarks>
    /// <b>Globally unambiguous, not a number within a server.</b> Channel 1 exists on every
    /// server, so a bare number is not an identity -- it is a label. This is a full identity,
    /// and the channel additionally records which server it belongs to so the pairing can be
    /// checked rather than assumed. Both together are what stops a client selecting server A
    /// and channel 1 of server B.
    /// </remarks>
    [Serializable]
    public struct ChannelId : IEquatable<ChannelId>
    {
        [SerializeField] private string _value;

        public ChannelId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static ChannelId None => default;

        public static ChannelId New()
        {
            return new ChannelId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(ChannelId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is ChannelId other && Equals(other);

        public override int GetHashCode() => IdentityHash.Fnv1a(_value);

        public override string ToString() => _value ?? "<none>";

        public static bool operator ==(ChannelId left, ChannelId right) => left.Equals(right);

        public static bool operator !=(ChannelId left, ChannelId right) => !left.Equals(right);
    }
}
