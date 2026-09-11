using System.Collections.Generic;
using UnityEngine;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The translated text the game ships, as an authored asset.
    /// </summary>
    /// <remarks>
    /// <b>Text files rather than fields on this asset.</b> A ScriptableObject holding a
    /// thousand string pairs is a YAML file that no two people can edit without a merge
    /// conflict and that no translator can open. A JSON file per language is something a
    /// translator can be handed, a diff can be read on, and a build can validate. This asset
    /// exists to say which files are the game's, which is a decision worth putting in the
    /// project rather than in a path constant.
    ///
    /// <b>English is seeded from <see cref="UiStrings"/>, not from the file.</b> The file
    /// only adds what the code has no opinion about -- the names of items, monsters, NPCs
    /// and quests -- and may override a shipped word when a live text change is needed. So
    /// an English build with no files at all is still complete, which is the property that
    /// makes the whole fallback chain honest.
    ///
    /// <b>A broken file is reported, not swallowed.</b> Bad JSON leaves that language empty
    /// and logs once; the fallback chain then quietly shows English, which is a degraded
    /// game rather than a dead one. Silence here would mean a translator's typo shipping as
    /// a mysteriously English screen with nothing to search for.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Localization Catalogue",
        fileName = "LocalizationCatalogue")]
    public sealed class LocalizationCatalogue : ScriptableObject
    {
        [Header("One file per language. English may be omitted entirely.")]
        [Tooltip("Content names and any override of the shipped English wording.")]
        [SerializeField] private TextAsset _english;

        [Tooltip("Thai. Every key the screens use should appear here.")]
        [SerializeField] private TextAsset _thai;

        /// <summary>The file a language is read from. Null when none was authored.</summary>
        public TextAsset FileFor(GameLanguage language)
        {
            return language == GameLanguage.Thai ? _thai : _english;
        }

        /// <summary>
        /// Builds the service the screens will read from.
        /// </summary>
        /// <remarks>Built rather than held: the asset is authored content and must not carry
        /// a running language, which would leak one play session's choice into the next and
        /// dirty the asset on disk.</remarks>
        public LocalizationService Build()
        {
            var service = new LocalizationService();

            var english = new List<KeyValuePair<string, string>>();

            foreach (KeyValuePair<string, string> shipped in UiStrings.English)
            {
                english.Add(shipped);
            }

            english.AddRange(Read(_english, GameLanguage.English));

            service.Load(GameLanguage.English, english);
            service.Load(GameLanguage.Thai, Read(_thai, GameLanguage.Thai));

            return service;
        }

        private static IEnumerable<KeyValuePair<string, string>> Read(TextAsset file,
            GameLanguage language)
        {
            if (file == null) return System.Array.Empty<KeyValuePair<string, string>>();

            List<KeyValuePair<string, string>> entries;
            string error;

            if (Parse(file.text, out entries, out error)) return entries;

            Debug.LogError("Localization file for " + GameLanguages.CodeOf(language)
                + " could not be read (" + error + "). That language falls back to English.",
                file);

            return System.Array.Empty<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Reads one language file.
        /// </summary>
        /// <remarks>
        /// The shape is <c>{"entries":[{"key":"ui.x","text":"..."}]}</c> -- an array of pairs
        /// rather than a JSON object keyed by string, because Unity's JsonUtility cannot read
        /// a dictionary and the project already carries arrays-of-pairs on the wire for the
        /// same reason.
        ///
        /// Exposed so a test can read the shipped files without a ScriptableObject.
        /// </remarks>
        public static bool Parse(string json, out List<KeyValuePair<string, string>> entries,
            out string error)
        {
            entries = new List<KeyValuePair<string, string>>();
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the file is empty";
                return false;
            }

            Document document;

            try
            {
                document = JsonUtility.FromJson<Document>(json);
            }
            catch (System.Exception caught)
            {
                error = caught.Message;
                return false;
            }

            if (document == null || document.entries == null)
            {
                error = "no \"entries\" array";
                return false;
            }

            for (var i = 0; i < document.entries.Length; i++)
            {
                Entry entry = document.entries[i];

                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;

                entries.Add(new KeyValuePair<string, string>(entry.key, entry.text));
            }

            return true;
        }

        [System.Serializable]
        private sealed class Document
        {
            public Entry[] entries;
        }

        [System.Serializable]
        private sealed class Entry
        {
            public string key;
            public string text;
        }
    }
}
