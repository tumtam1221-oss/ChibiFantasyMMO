namespace ChibiFantasy.UI
{
    /// <summary>
    /// A language the game can be read in.
    /// </summary>
    /// <remarks>
    /// <b>Numbered, and never renumbered.</b> The choice is written to player preferences as
    /// its number, so a value that moved would silently switch somebody's language on the
    /// next patch. New languages are appended.
    ///
    /// <b>English is zero on purpose.</b> It is the fallback every other language falls back
    /// to, and the default a player who has never chosen gets -- so the value that means
    /// "nothing stored yet" and the value that means "English" are usefully the same.
    /// </remarks>
    public enum GameLanguage
    {
        /// <summary>English. The fallback for every other language.</summary>
        English = 0,

        /// <summary>Thai.</summary>
        Thai = 1
    }

    /// <summary>Names for the languages, in the language itself.</summary>
    /// <remarks>
    /// A language picker written in the language you are leaving is the one menu a player
    /// cannot use: somebody who only reads Thai needs to find "ไทย", not "Thai". So these
    /// are endonyms and they are deliberately not translated.
    /// </remarks>
    public static class GameLanguages
    {
        /// <summary>Every language, in the order a picker should offer them.</summary>
        public static readonly GameLanguage[] All =
        {
            GameLanguage.English,
            GameLanguage.Thai
        };

        /// <summary>What this language calls itself.</summary>
        public static string NameOf(GameLanguage language)
        {
            switch (language)
            {
                case GameLanguage.Thai: return "ไทย";
                default: return "English";
            }
        }

        /// <summary>The short code used in file names and preferences. Never translated.</summary>
        public static string CodeOf(GameLanguage language)
        {
            switch (language)
            {
                case GameLanguage.Thai: return "th";
                default: return "en";
            }
        }

        /// <summary>Reads a code back. Anything unknown is English.</summary>
        public static GameLanguage FromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return GameLanguage.English;

            switch (code.Trim().ToLowerInvariant())
            {
                case "th":
                case "th-th":
                case "thai":
                    return GameLanguage.Thai;

                default:
                    return GameLanguage.English;
            }
        }
    }
}
