namespace ChibiFantasy.Core
{
    /// <summary>
    /// Deterministic hashing shared by the string-backed identity types.
    /// </summary>
    /// <remarks>
    /// Extracted rather than copied a fourth time. Every identity in this project needs the
    /// same property: a hash that is identical across runs, processes and platforms, unlike
    /// string.GetHashCode which is randomised per process on modern .NET. Keeping one
    /// implementation means that guarantee is stated once and cannot drift between types.
    ///
    /// Internal: this is an implementation detail of the identity types, not a general
    /// hashing utility.
    /// </remarks>
    internal static class IdentityHash
    {
        /// <summary>FNV-1a 32-bit. Returns zero for null.</summary>
        internal static int Fnv1a(string value)
        {
            if (value == null)
            {
                return 0;
            }

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                uint hash = offsetBasis;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }

                return (int)hash;
            }
        }
    }
}
