using System;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Where a combatant is, expressed without Unity.
    /// </summary>
    /// <remarks>
    /// Deliberately not UnityEngine.Vector3. The Gameplay assembly holds no
    /// <c>using UnityEngine</c> anywhere, and combat rules are the part most likely to move
    /// to a headless server later; a rule that needs the engine to answer "is this in
    /// range" cannot make that move. The presentation layer converts a Vector3 into this
    /// at the boundary, which costs three float copies.
    ///
    /// <see cref="SqrDistanceTo"/> rather than a distance, because range comparisons do not
    /// need the square root and every avoided one is an avoided rounding difference.
    /// </remarks>
    public readonly struct CombatPosition : IEquatable<CombatPosition>
    {
        public CombatPosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        /// <summary>The origin. A valid position, not an absent one.</summary>
        public static CombatPosition Zero => default;

        /// <summary>Squared distance, so callers can compare against a squared range.</summary>
        public float SqrDistanceTo(CombatPosition other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        /// <summary>Whether every component is a real number.</summary>
        /// <remarks>A position that arrived as NaN would make every range comparison false
        /// and silently make a combatant unattackable, so range checks test this first.</remarks>
        public bool IsFinite
        {
            get
            {
                return !(float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z)
                      || float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z));
            }
        }

        public bool Equals(CombatPosition other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj) => obj is CombatPosition other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }
}
