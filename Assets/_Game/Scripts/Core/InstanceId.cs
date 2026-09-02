using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity for one runtime, player-owned object.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DefinitionId"/> by design. A DefinitionId names a piece of
    /// authored content shared by everyone; an InstanceId names one actual copy belonging
    /// to one owner. They are separate types so the compiler refuses to confuse them.
    ///
    /// Backed by a GUID in "N" form (32 lowercase hex characters). That representation is
    /// chosen because it is:
    /// <list type="bullet">
    /// <item>Unity-serializable, which System.Guid is not;</item>
    /// <item>generated without coordination, so a server may mint ids without a round trip;</item>
    /// <item>trivially mapped to a MySQL CHAR(32) or BINARY(16) column later.</item>
    /// </list>
    ///
    /// Patch safety is the whole point of this type. The value is minted once and stored.
    /// It is never derived from a Unity instance ID, an object reference, an array index,
    /// an asset GUID or scene object ID, so it survives save and load, server restart,
    /// database persistence, client reconnect, content patches and asset reimport.
    ///
    /// <see cref="GetHashCode"/> is FNV-1a rather than string.GetHashCode, which is
    /// randomised per process on modern .NET and would differ between runs.
    /// </remarks>
    [Serializable]
    public struct InstanceId : IEquatable<InstanceId>
    {
        [SerializeField] private string _value;

        public InstanceId(string value)
        {
            _value = value;
        }

        /// <summary>The raw identifier. May be null for <see cref="None"/>.</summary>
        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        /// <summary>The absent identity, used for not-yet-persisted or cleared references.</summary>
        public static InstanceId None => default;

        /// <summary>
        /// Mints a new, previously unused identity.
        /// </summary>
        /// <remarks>
        /// In production only the server should mint identities for player-owned state; the
        /// client is untrusted and any instance identity it supplies must be validated
        /// server-side. Nothing here enforces that, because enforcement is an authority
        /// concern, not a data one.
        /// </remarks>
        public static InstanceId New()
        {
            return new InstanceId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(InstanceId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InstanceId other && Equals(other);
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

        public static bool operator ==(InstanceId left, InstanceId right) => left.Equals(right);

        public static bool operator !=(InstanceId left, InstanceId right) => !left.Equals(right);
    }
}
