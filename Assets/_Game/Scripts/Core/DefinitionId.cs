using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable, serializable identity for a piece of authored game content.
    /// </summary>
    /// <remarks>
    /// Lives in Core rather than Data on purpose: Contracts references Core but must
    /// never reference Data, so wire contracts can carry content identity without
    /// depending on the content layer.
    ///
    /// Deliberately a mutable struct rather than a readonly struct: Unity's serializer
    /// writes directly into fields and cannot deserialize into readonly fields. The
    /// backing field is private with no setter, so instances are immutable in practice.
    /// </remarks>
    [Serializable]
    public struct DefinitionId : IEquatable<DefinitionId>
    {
        [SerializeField] private string _value;

        public DefinitionId(string value)
        {
            _value = value;
        }

        /// <summary>The authored, stable identifier. May be null for <see cref="None"/>.</summary>
        public string Value => _value;

        /// <summary>False for null, empty or whitespace-only identifiers.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        /// <summary>The absent identifier.</summary>
        public static DefinitionId None => default;

        public bool Equals(DefinitionId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DefinitionId other && Equals(other);
        }

        /// <summary>
        /// Deterministic FNV-1a 32-bit hash.
        /// </summary>
        /// <remarks>
        /// Implemented explicitly rather than delegating to string.GetHashCode, which is
        /// randomised per process on modern .NET and therefore differs between runs. This
        /// implementation is stable across runs, processes and platforms.
        /// </remarks>
        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? string.Empty;
        }

        public static bool operator ==(DefinitionId left, DefinitionId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DefinitionId left, DefinitionId right)
        {
            return !left.Equals(right);
        }
    }
}
