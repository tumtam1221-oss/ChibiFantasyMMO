using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The game's text, in the language the player chose.
    /// </summary>
    /// <remarks>
    /// <b>What this is the concrete half of.</b> <see cref="ILocalizedTextSource"/> has been
    /// the seam since the inventory work, with nothing behind it -- every screen has been
    /// running with a null source and drawing its own English. This is the thing that was
    /// always meant to arrive later, and no view changed shape to receive it.
    ///
    /// <b>The fallback chain is the whole design.</b> A key is looked for in the chosen
    /// language, then in English, and then it is not found -- and "not found" is a real
    /// answer, not an error. Callers already handle it: content falls back to reading the id
    /// as words, chrome falls back to the English literal written in the view. That is why
    /// an unauthored key can never put a raw key or a blank space on screen, and why adding
    /// a language does not require translating everything before the game runs.
    ///
    /// <b>Nothing below the UI can see this.</b> It lives in the UI assembly beside the
    /// interface it implements. Gameplay and the server hold <see cref="LocalizationKey"/>
    /// and identifiers, never a sentence in anybody's language -- so two players reading two
    /// languages are playing the same game, and the world server has no opinion about which.
    ///
    /// <b>Not a singleton.</b> It is handed to the screens that need it, exactly as the
    /// original note asked for. The client bootstrap composes one; a test composes its own.
    /// </remarks>
    public sealed class LocalizationService : ILocalizedTextSource
    {
        private readonly Dictionary<GameLanguage, Dictionary<string, string>> _tables =
            new Dictionary<GameLanguage, Dictionary<string, string>>();

        /// <summary>The language text is being asked for.</summary>
        public GameLanguage Current { get; private set; } = GameLanguage.English;

        /// <summary>Raised after the language changes, so screens can redraw.</summary>
        /// <remarks>
        /// Screens draw into labels once and then stop; without this a player switching
        /// language would keep reading the old one until they closed and reopened every
        /// panel. Raised only on an actual change, so a redundant set costs nothing.
        /// </remarks>
        public event System.Action Changed;

        /// <summary>How many languages have a table loaded.</summary>
        public int LanguageCount => _tables.Count;

        /// <summary>Whether this language has any text at all.</summary>
        public bool Has(GameLanguage language)
        {
            Dictionary<string, string> table;

            return _tables.TryGetValue(language, out table) && table.Count > 0;
        }

        /// <summary>How many entries a language carries. Zero when it has no table.</summary>
        public int CountOf(GameLanguage language)
        {
            Dictionary<string, string> table;

            return _tables.TryGetValue(language, out table) ? table.Count : 0;
        }

        /// <summary>
        /// Puts a language's text in, replacing whatever that language had.
        /// </summary>
        /// <remarks>Replaced rather than merged: a reload that left deleted keys behind
        /// would keep showing text nobody can find in the file any more.</remarks>
        public void Load(GameLanguage language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            var table = new Dictionary<string, string>(System.StringComparer.Ordinal);

            if (entries != null)
            {
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;

                    // An empty translation is an untranslated line, not a translation to
                    // nothing. Dropping it lets the fallback chain do its job instead of
                    // putting a blank label on screen.
                    if (string.IsNullOrEmpty(entry.Value)) continue;

                    table[entry.Key] = entry.Value;
                }
            }

            _tables[language] = table;
        }

        /// <summary>Switches language and tells everybody drawing text.</summary>
        public void Use(GameLanguage language)
        {
            if (Current == language) return;

            Current = language;

            Changed?.Invoke();
        }

        /// <summary>Every key this language has text for. For tests and tooling.</summary>
        public IEnumerable<string> KeysOf(GameLanguage language)
        {
            Dictionary<string, string> table;

            if (!_tables.TryGetValue(language, out table)) yield break;

            foreach (string key in table.Keys) yield return key;
        }

        /// <summary>
        /// The chosen language's text for a key, falling back to English.
        /// </summary>
        /// <remarks>False when neither language has it, which is how a caller knows to use
        /// its own fallback. Never returns the key itself -- putting "ui.quest.accept" in
        /// front of a player is worse than the English word the view already knows.</remarks>
        public bool TryGet(LocalizationKey key, out string text)
        {
            text = null;

            if (!key.IsValid) return false;

            if (Find(Current, key.Key, out text)) return true;

            return Current != GameLanguage.English
                && Find(GameLanguage.English, key.Key, out text);
        }

        private bool Find(GameLanguage language, string key, out string text)
        {
            text = null;

            Dictionary<string, string> table;

            return _tables.TryGetValue(language, out table)
                && table.TryGetValue(key, out text)
                && !string.IsNullOrEmpty(text);
        }
    }
}
