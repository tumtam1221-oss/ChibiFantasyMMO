using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Portable, indirect reference to a loadable asset (icon, model, scene, VFX, SFX).
    /// </summary>
    /// <remarks>
    /// Deliberately an address string rather than a direct UnityEngine.Object field.
    ///
    /// A direct reference would make every definition asset pull its art into memory
    /// whenever the definition loads. The dedicated server loads the same definitions as
    /// the client but renders nothing, so it would pay for icons, models and VFX it never
    /// uses. Indirection also keeps the schema independent of any particular loading
    /// mechanism: Resources, AssetBundles or Addressables can back this later without
    /// changing a single definition, and without adding a package now.
    ///
    /// Resolution is intentionally not implemented here; that is a loading concern.
    /// </remarks>
    [Serializable]
    public struct AssetRef : IEquatable<AssetRef>
    {
        [SerializeField] private string _address;

        public AssetRef(string address)
        {
            _address = address;
        }

        public string Address => _address;

        public bool IsValid => !string.IsNullOrWhiteSpace(_address);

        public static AssetRef None => default;

        public bool Equals(AssetRef other) => string.Equals(_address, other._address, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is AssetRef other && Equals(other);

        public override int GetHashCode() => _address == null ? 0 : _address.GetHashCode();

        public override string ToString() => _address ?? string.Empty;

        public static bool operator ==(AssetRef left, AssetRef right) => left.Equals(right);

        public static bool operator !=(AssetRef left, AssetRef right) => !left.Equals(right);
    }
}
