using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Turns an <see cref="AssetRef"/> into a <see cref="Sprite"/>.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole reason <see cref="AssetRef"/> is a string.</b> Gameplay and Data
    /// name an icon by address and never touch a Unity <c>Sprite</c>; a
    /// <c>[SerializeField] Sprite</c> on <c>ItemDefinition</c> would drag a rendering type
    /// into content that a server has to be able to load headless. The conversion happens
    /// here, in the presentation assembly, and nowhere else.
    ///
    /// <b>The loader is injected.</b> The project has no Addressables and no content
    /// delivery system, so the default is <c>Resources.Load</c> -- the smallest thing that
    /// works. Passing a different loader is how a test resolves icons with no assets on
    /// disk, and how this becomes Addressables later without a view changing.
    ///
    /// <b>Cached, including the failures.</b> An address is loaded at most once. Caching
    /// misses matters as much as caching hits: without it, every refresh of a bag full of
    /// unauthored items would retry every failed load, which is exactly the uncontrolled
    /// loading this is meant to prevent. <see cref="LoadAttempts"/> exists so that is
    /// provable rather than asserted.
    /// </remarks>
    public sealed class IconResolver
    {
        /// <summary>How an address becomes a sprite. Returns null when there is nothing there.</summary>
        public delegate Sprite Loader(string address);

        private readonly Dictionary<string, Sprite> _cache =
            new Dictionary<string, Sprite>(System.StringComparer.Ordinal);

        private readonly Loader _loader;

        public IconResolver(Loader loader = null)
        {
            _loader = loader ?? LoadFromResources;
        }

        /// <summary>Drawn when an address is unauthored or resolves to nothing.</summary>
        /// <remarks>Optional. A null placeholder means a view falls back to its own
        /// treatment, which is what <see cref="ItemSlotView"/> already does with a
        /// colour.</remarks>
        public Sprite Placeholder { get; set; }

        /// <summary>
        /// How many times the loader has actually been called.
        /// </summary>
        /// <remarks>Not a statistic for its own sake: it is the observable that proves a
        /// repeated refresh does not repeat the loading.</remarks>
        public int LoadAttempts { get; private set; }

        /// <summary>Distinct addresses resolved so far, hits and misses together.</summary>
        public int CachedCount => _cache.Count;

        /// <summary>
        /// The sprite for an address, or the placeholder.
        /// </summary>
        /// <remarks>Never throws and never logs. Missing art is an ordinary state during
        /// development, not a fault, and a slot that logged an error every refresh would
        /// bury the console.</remarks>
        public Sprite Resolve(AssetRef reference)
        {
            Sprite sprite;
            return TryResolve(reference, out sprite) ? sprite : Placeholder;
        }

        /// <summary>
        /// Whether a real sprite exists for an address.
        /// </summary>
        /// <remarks>False for an invalid <see cref="AssetRef"/> and for a valid one that
        /// resolves to nothing, so a caller can tell "no art authored" from "art authored
        /// and found".</remarks>
        public bool TryResolve(AssetRef reference, out Sprite sprite)
        {
            if (!reference.IsValid)
            {
                sprite = null;
                return false;
            }

            string address = reference.Address;

            if (_cache.TryGetValue(address, out sprite))
            {
                return sprite != null;
            }

            LoadAttempts++;
            sprite = _loader(address);

            // The miss is cached too, so a bag of unauthored icons costs one attempt each
            // for the life of the resolver rather than one per refresh.
            _cache[address] = sprite;
            return sprite != null;
        }

        /// <summary>
        /// Forgets everything resolved so far.
        /// </summary>
        /// <remarks>
        /// Nothing is unloaded. <c>Resources</c> assets are owned by Unity and shared with
        /// anything else referencing them, so unloading one here could pull a sprite out
        /// from under another screen. Dropping the references is all this layer may safely
        /// do; a real content system will own eviction.
        /// </remarks>
        public void Clear()
        {
            _cache.Clear();
        }

        private static Sprite LoadFromResources(string address)
        {
            return Resources.Load<Sprite>(address);
        }
    }
}
