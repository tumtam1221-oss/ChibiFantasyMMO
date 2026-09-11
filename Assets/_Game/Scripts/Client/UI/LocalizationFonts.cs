using ChibiFantasy.UI;
using TMPro;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Checks that the letters a language is written in can actually be drawn.
    /// </summary>
    /// <remarks>
    /// <b>The problem, measured.</b> The pre-world screens draw with TextMeshPro, whose
    /// default asset is <c>LiberationSans SDF</c>: a <i>static</i> atlas of 250 characters
    /// containing no Thai at all. Translating those screens without a fallback would have
    /// shipped a Thai build made of empty boxes.
    ///
    /// <b>How that is solved, and why not the way it was.</b> A Thai font asset lives in the
    /// project at <see cref="FallbackAssetPath"/> and is listed in TextMeshPro's settings, so
    /// it is present before the game starts and identical on every machine. This class now
    /// only <i>checks</i> that.
    ///
    /// It used to build that face at runtime and push it into TextMeshPro's global fallback
    /// list, and that went badly in three separate ways worth recording:
    ///
    /// <list type="bullet">
    /// <item>It called <c>TMP_ResourceManager.ClearFontAssetGlyphCache()</c> so that labels
    /// drawn before the fallback existed would pick it up. That empties the glyph cache of
    /// <i>every</i> font asset -- and it emptied the character table of the shared
    /// <c>LiberationSans SDF</c> project asset, on disk. With no characters left, every Latin
    /// letter missed and fell through to the Thai face: a button reading "English" came back
    /// drawn in Thai letters, and the editor ground to a halt trying to rasterise the whole
    /// UI every frame.</item>
    /// <item>The faces were created with <c>HideAndDontSave</c>, so play mode never unloaded
    /// them. Five had accumulated in one session, each with its own atlas, each claiming the
    /// same characters.</item>
    /// <item>None of it was reproducible for anybody else: the fallback existed only in a
    /// running editor, so a teammate opening the project would still have seen boxes.</item>
    /// </list>
    ///
    /// <b>The rule this leaves behind.</b> Runtime code does not modify shared font assets,
    /// TextMeshPro's settings, or its caches. Fonts are content; content is authored.
    /// </remarks>
    public static class LocalizationFonts
    {
        /// <summary>The authored Thai face, listed in TextMeshPro's settings.</summary>
        public const string FallbackAssetPath =
            "Assets/_Game/Art/UI/Fonts/TMP_ThaiFallback.asset";

        /// <summary>
        /// Whether TextMeshPro can currently draw Thai.
        /// </summary>
        /// <remarks>
        /// Asks the default font asset the same question a label asks, searching fallbacks --
        /// so this answers "would a player see letters", not "did the setup code run".
        ///
        /// <b>It does not add anything.</b> The earlier version passed
        /// <c>tryAddCharacter: true</c>, which quietly rasterised glyphs as a side effect of
        /// asking a question, so merely checking changed the atlas.
        /// </remarks>
        public static bool CanDrawThai()
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            if (font == null) return false;

            // Three characters from three parts of the Thai block -- a consonant, a vowel
            // and a tone mark. A face carrying only some of them is still broken.
            return font.HasCharacter('ไ', true, false)
                && font.HasCharacter('ท', true, false)
                && font.HasCharacter('่', true, false);
        }

        /// <summary>Whether the Latin the rest of the UI is drawn in is still intact.</summary>
        /// <remarks>Here because it is exactly what broke: a fallback that arrives by
        /// damaging the main font is worse than no fallback, and this is the cheap check
        /// that says so.</remarks>
        public static bool CanDrawLatin()
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;

            return font != null
                && font.HasCharacter('E', false, false)
                && font.HasCharacter('a', false, false)
                && font.HasCharacter('0', false, false);
        }

        /// <summary>Whether this language needs anything beyond the default font.</summary>
        public static bool NeedsFallback(GameLanguage language)
        {
            return language == GameLanguage.Thai;
        }

        /// <summary>
        /// Reports whether the fonts can draw the game.
        /// </summary>
        /// <remarks>
        /// <b>Reports; it does not repair.</b> Repairing meant mutating shared assets, which
        /// is what caused the damage this class is named after. A missing fallback is a
        /// content problem with a content fix -- add the asset to TextMeshPro's settings --
        /// and saying so plainly is more use than a silent runtime patch that works on one
        /// machine and corrupts the project on another.
        /// </remarks>
        public static bool Verify()
        {
            bool latin = CanDrawLatin();
            bool thai = CanDrawThai();

            if (!latin)
            {
                Debug.LogError("TextMeshPro's default font cannot draw Latin: the shared font "
                    + "asset has lost its character table. Restore it from version control. "
                    + "Nothing at runtime should ever write to it.");
            }

            if (!thai)
            {
                Debug.LogWarning("No Thai-capable font is in TextMeshPro's fallback list, so "
                    + "Thai will draw as empty boxes. Add " + FallbackAssetPath
                    + " under Project Settings > TextMesh Pro > Settings > Fallback Font Assets.");
            }

            return latin && thai;
        }

        /// <summary>The name the client already calls. Checks only, never repairs.</summary>
        public static bool EnsureDefault()
        {
            return Verify();
        }
    }
}
