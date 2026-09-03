using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Turns a <see cref="LocalizationKey"/> into text a player can read.
    /// </summary>
    /// <remarks>
    /// <b>An interface because there is no localization system yet.</b> The project has no
    /// Unity Localization package and no string tables; inventing one in an inventory phase
    /// would be the wrong place to commit the game to a localisation strategy. This is the
    /// seam: views depend on this, and whatever arrives later implements it without any
    /// view changing.
    ///
    /// <b>Presentation only.</b> It lives in the UI assembly and Gameplay cannot see it.
    /// <c>ItemDefinition.NameKey</c> stays the authoritative name and Gameplay stays
    /// language-neutral -- nothing below the UI ever holds a translated string.
    /// </remarks>
    public interface ILocalizedTextSource
    {
        /// <summary>Looks a key up. False when this source has no text for it.</summary>
        bool TryGet(LocalizationKey key, out string text);
    }

    /// <summary>
    /// A plain in-memory table of translations.
    /// </summary>
    /// <remarks>
    /// The smallest thing that satisfies <see cref="ILocalizedTextSource"/>. Enough to
    /// prove the boundary works and to author a prototype scene; not a localisation
    /// pipeline, and deliberately not a global manager, so a screen is handed the source it
    /// should use rather than reaching for an ambient one.
    /// </remarks>
    public sealed class LocalizationTable : ILocalizedTextSource
    {
        private readonly Dictionary<string, string> _entries =
            new Dictionary<string, string>(System.StringComparer.Ordinal);

        public int Count => _entries.Count;

        /// <summary>Adds or replaces one translation. An invalid key is ignored.</summary>
        public void Set(LocalizationKey key, string text)
        {
            if (!key.IsValid) return;
            _entries[key.Key] = text;
        }

        public bool TryGet(LocalizationKey key, out string text)
        {
            if (!key.IsValid)
            {
                text = null;
                return false;
            }

            return _entries.TryGetValue(key.Key, out text);
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }

    /// <summary>Resolving a key with a fallback that never fails.</summary>
    public static class LocalizedText
    {
        /// <summary>
        /// The text for a key, or something safe to draw instead.
        /// </summary>
        /// <remarks>
        /// A missing key falls back to the raw key, which is the useful failure: the screen
        /// stays readable, and the untranslated string is visible to whoever has to add it.
        /// Blank or an error would hide the gap.
        ///
        /// A null source is not a fault. It is what every screen sees until a localisation
        /// system exists, and it must render.
        /// </remarks>
        public static string Resolve(ILocalizedTextSource source, LocalizationKey key)
        {
            if (!key.IsValid) return string.Empty;

            string text;
            if (source != null && source.TryGet(key, out text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            return key.Key;
        }

        /// <summary>
        /// The text for a key, falling back to a second key and then to the raw key.
        /// </summary>
        /// <remarks>Used where a name key may be unauthored and an id is the only thing
        /// left to show, which is what an item whose content was removed by a patch looks
        /// like.</remarks>
        public static string ResolveOr(ILocalizedTextSource source, LocalizationKey key,
            string fallback)
        {
            if (!key.IsValid) return fallback ?? string.Empty;
            return Resolve(source, key);
        }
    }
}
