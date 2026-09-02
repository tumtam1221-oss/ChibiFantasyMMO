using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Key into the localization table for a displayable string.
    /// </summary>
    /// <remarks>
    /// Definitions store keys, never localized text, so content is authored once and
    /// displayed per-locale. Wrapped in a value type rather than a raw string so that
    /// "this is a localization key" is visible in the schema and cannot be confused with
    /// a display string or an identifier.
    /// </remarks>
    [Serializable]
    public struct LocalizationKey : IEquatable<LocalizationKey>
    {
        [SerializeField] private string _key;

        public LocalizationKey(string key)
        {
            _key = key;
        }

        public string Key => _key;

        public bool IsValid => !string.IsNullOrWhiteSpace(_key);

        public static LocalizationKey None => default;

        public bool Equals(LocalizationKey other) => string.Equals(_key, other._key, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LocalizationKey other && Equals(other);

        public override int GetHashCode() => _key == null ? 0 : _key.GetHashCode();

        public override string ToString() => _key ?? string.Empty;

        public static bool operator ==(LocalizationKey left, LocalizationKey right) => left.Equals(right);

        public static bool operator !=(LocalizationKey left, LocalizationKey right) => !left.Equals(right);
    }
}
