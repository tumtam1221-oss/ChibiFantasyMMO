using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity of whoever owns a runtime instance.
    /// </summary>
    /// <remarks>
    /// Deliberately neutral: it may name an account, a character or a guild. No owner-kind
    /// discriminator is included because nothing in the current architecture needs to
    /// branch on it, and adding one now would guess at a distinction the ownership rules
    /// have not yet made.
    ///
    /// Kept as its own type rather than reusing <see cref="InstanceId"/>, despite the
    /// identical representation, for two reasons:
    /// <list type="bullet">
    /// <item>The compiler then refuses to pass an item's own identity where its owner's
    /// identity belongs. Those are the two ids most easily transposed, and transposing
    /// them silently reassigns ownership.</item>
    /// <item>They come from different authorities. An InstanceId is minted by the game
    /// server; an owner identity originates in the account system behind the PHP API.
    /// Sharing one type would couple the game's identity scheme to the account system's.</item>
    /// </list>
    ///
    /// <see cref="DefinitionId"/> is explicitly not used for ownership: content identity
    /// and owner identity are unrelated concepts that happen to both be strings.
    ///
    /// This type carries no authentication. Establishing who a caller really is, and
    /// enforcing that they own what they claim, is server-side work that does not belong
    /// in a data value type.
    /// </remarks>
    [Serializable]
    public struct OwnerId : IEquatable<OwnerId>
    {
        [SerializeField] private string _value;

        public OwnerId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        /// <summary>Unowned. Valid for instances that exist before assignment, such as a
        /// freshly rolled drop not yet awarded.</summary>
        public static OwnerId None => default;

        public bool Equals(OwnerId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is OwnerId other && Equals(other);
        }

        /// <summary>Deterministic FNV-1a 32-bit hash, stable across runs and platforms.</summary>
        public override int GetHashCode()
        {
            if (_value == null)
            {
                return 0;
            }

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                uint hash = offsetBasis;
                for (int i = 0; i < _value.Length; i++)
                {
                    hash ^= _value[i];
                    hash *= prime;
                }

                return (int)hash;
            }
        }

        public override string ToString() => _value ?? string.Empty;

        public static bool operator ==(OwnerId left, OwnerId right) => left.Equals(right);

        public static bool operator !=(OwnerId left, OwnerId right) => !left.Equals(right);
    }
}
