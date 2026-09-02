using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Monotonic change counter for a piece of mutable state.
    /// </summary>
    /// <remarks>
    /// An integer counter rather than a timestamp. Timestamps depend on clocks that differ
    /// between a client, a game server and a database, and comparing them across those
    /// boundaries invites subtle ordering bugs; an integer is exact, cheap to persist as an
    /// INT column, and cheap to compare.
    ///
    /// It exists so later systems can answer "has this changed since I last saw it" without
    /// diffing whole objects. It does not perform that comparison, and it neither
    /// synchronises nor persists anything.
    ///
    /// Advancing is explicit via <see cref="Next"/>, which returns a new value rather than
    /// mutating, so a Revision can be treated as the value it is.
    /// </remarks>
    [Serializable]
    public struct Revision : IEquatable<Revision>, IComparable<Revision>
    {
        [SerializeField] private int _value;

        public Revision(int value)
        {
            _value = value;
        }

        public int Value => _value;

        /// <summary>The revision of freshly created state that has not yet been modified.</summary>
        public static Revision Initial => default;

        /// <summary>Returns the next revision. Does not modify this one.</summary>
        /// <remarks>Wraps at int.MaxValue, which at one increment per state change is not
        /// reachable in practice for a single instance.</remarks>
        public Revision Next()
        {
            unchecked
            {
                return new Revision(_value + 1);
            }
        }

        /// <summary>True when this revision is newer than <paramref name="other"/>.</summary>
        public bool IsNewerThan(Revision other) => _value > other._value;

        public bool Equals(Revision other) => _value == other._value;

        public override bool Equals(object obj) => obj is Revision other && Equals(other);

        public override int GetHashCode() => _value;

        public int CompareTo(Revision other) => _value.CompareTo(other._value);

        public override string ToString() => _value.ToString();

        public static bool operator ==(Revision left, Revision right) => left.Equals(right);

        public static bool operator !=(Revision left, Revision right) => !left.Equals(right);
    }
}
