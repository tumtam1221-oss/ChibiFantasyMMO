using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Which language this player reads, remembered between sessions.
    /// </summary>
    /// <remarks>
    /// <b>On the machine, not on the account.</b> A language is a property of the person
    /// sitting in front of the screen, not of the character they are playing -- and it has to
    /// be readable before anybody has signed in, because the sign-in screen is the first
    /// thing that needs translating. Putting it on the account would mean a player could not
    /// read the screen where they say who they are.
    ///
    /// <b>Stored as the code, not the number.</b> "th" survives somebody reordering the
    /// enum; a stored 1 does not. The enum is documented as never renumbered, and this is the
    /// belt to that pair of braces -- the cost is a string compare once per launch.
    ///
    /// <b>First launch follows the system.</b> Somebody whose computer is in Thai almost
    /// certainly wants the game in Thai, and a player who has actually chosen is never
    /// second-guessed: only the absence of a stored answer consults the system.
    /// </remarks>
    public static class LanguagePreference
    {
        /// <summary>Where the choice is kept. Namespaced like the other client keys.</summary>
        public const string Key = "chibi.language";

        /// <summary>What this player reads, or what their system suggests.</summary>
        public static GameLanguage Current
        {
            get
            {
                string stored = PlayerPrefs.GetString(Key, string.Empty);

                return string.IsNullOrEmpty(stored) ? FromSystem() : GameLanguages.FromCode(stored);
            }
        }

        /// <summary>Whether this player has ever chosen, as opposed to been guessed at.</summary>
        public static bool HasChosen => !string.IsNullOrEmpty(PlayerPrefs.GetString(Key, string.Empty));

        /// <summary>Remembers a choice.</summary>
        public static void Remember(GameLanguage language)
        {
            PlayerPrefs.SetString(Key, GameLanguages.CodeOf(language));
            PlayerPrefs.Save();
        }

        /// <summary>Forgets it, so the next launch guesses again. For tests.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>What the machine's own language suggests. English when it says nothing.</summary>
        public static GameLanguage FromSystem()
        {
            return Application.systemLanguage == SystemLanguage.Thai
                ? GameLanguage.Thai
                : GameLanguage.English;
        }
    }
}
