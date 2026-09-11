using ChibiFantasy.Core;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Looking up a word a player reads, from inside a view.
    /// </summary>
    /// <remarks>
    /// <b>The fallback chain, and why it cannot fail.</b> A key is asked of the player's
    /// language, then of English, then of <see cref="UiStrings"/> -- the English wording
    /// compiled into the game. That last link is the point: every chrome string in this
    /// project was originally an English literal inside the view that drew it, and those
    /// literals are the product. Moving them into <c>UiStrings</c> rather than deleting them
    /// means a missing translation, a missing file, or a corrupt table can only ever leave a
    /// screen in English. It can never blank a label or show a player a raw key.
    ///
    /// <b>A null source is normal.</b> It is what every view saw before a localization
    /// system existed, and what a unit test constructing a view in isolation still sees.
    /// Everything here renders correctly with nothing behind it.
    ///
    /// <b>Content names are not chrome.</b> Item, monster and NPC names come from the
    /// content tables through <see cref="ContentText"/>, which falls back to reading the
    /// identifier as words. They are deliberately not listed in <c>UiStrings</c>: the game
    /// has no opinion about what a patch decides to call a slime.
    /// </remarks>
    public static class UiText
    {
        /// <summary>The player's word for this key, or the English the game ships with.</summary>
        public static string Of(ILocalizedTextSource source, string key)
        {
            return Of(source, key, UiStrings.EnglishFor(key));
        }

        /// <summary>
        /// The same, with a last resort for keys the game does not ship English for.
        /// </summary>
        /// <remarks>
        /// <b>The supplied fallback comes after <see cref="UiStrings"/>, not instead of it.</b>
        /// Getting that order wrong is a real bug this had: a refused sign-in passed the
        /// rejection's enum name as its fallback, and with no translation loaded a player was
        /// told "AccountBanned" instead of "This account is banned" -- the shipped English was
        /// sitting right there and was never consulted.
        ///
        /// So the argument means what its callers actually need: something to say about a
        /// value nobody wrote a sentence for, which for a typed rejection is the value's own
        /// name and is at least quotable in a support ticket.
        /// </remarks>
        public static string Of(ILocalizedTextSource source, string key, string english)
        {
            if (source != null && !string.IsNullOrEmpty(key))
            {
                string text;

                if (source.TryGet(new LocalizationKey(key), out text)
                    && !string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            string shipped = UiStrings.EnglishFor(key);

            if (!string.IsNullOrEmpty(shipped)) return shipped;

            if (!string.IsNullOrEmpty(english)) return english;

            // Nothing anywhere knows this key. Showing it is better than showing a blank,
            // because a blank is invisible and a key gets reported.
            return key ?? string.Empty;
        }

        /// <summary>
        /// The same, with numbers or names filled into the sentence.
        /// </summary>
        /// <remarks>
        /// <b>Placeholders rather than concatenation, because word order is not universal.</b>
        /// "Requires level 5" happens to put the number last in both English and Thai, but
        /// "Speak with X to accept this quest" does not survive being assembled from
        /// fragments. A translator handed a whole sentence with numbered holes can move the
        /// holes; a translator handed "Speak with " cannot.
        ///
        /// A translation with a malformed placeholder falls back to the English pattern
        /// rather than throwing: a stray brace in a content file must not be able to take a
        /// screen down.
        ///
        /// <b>Exactly one overload, deliberately.</b> There was briefly a second taking an
        /// explicit English pattern before the arguments, and because the first argument
        /// filled into most of these sentences is itself a string, C# silently preferred it:
        /// <c>Format(text, ObjectiveProgress, verb, target, 2, 3)</c> bound <c>verb</c> as the
        /// pattern and printed "Defeat" where the objective should have read
        /// "Defeat Training Slime 2 / 3". Nothing threw and seven screens quietly lost their
        /// numbers. One overload cannot be resolved wrongly.
        /// </remarks>
        public static string Format(ILocalizedTextSource source, string key,
            params object[] args)
        {
            string english = UiStrings.EnglishFor(key);

            string pattern = Of(source, key, english);

            if (args == null || args.Length == 0) return pattern;

            try
            {
                return string.Format(pattern, args);
            }
            catch (System.FormatException)
            {
                try
                {
                    return string.Format(english ?? string.Empty, args);
                }
                catch (System.FormatException)
                {
                    return english ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// What to call a piece of content: an item, a monster, an NPC, a map.
        /// </summary>
        /// <remarks>
        /// <b>Separate from <see cref="Of"/> because the fallback is different.</b> Chrome
        /// falls back to English written in the game; content has no English written in the
        /// game and must never fall back to showing a key. "monster.training_slime.name" in
        /// front of a player is the failure this exists to prevent -- it reads as the game
        /// being broken, where "Training Slime" reads as a monster.
        /// </remarks>
        public static string ContentText(ILocalizedTextSource source, LocalizationKey key,
            DefinitionId id)
        {
            if (source != null && key.IsValid)
            {
                string text;

                if (source.TryGet(key, out text) && !string.IsNullOrEmpty(text)) return text;
            }

            return Readable(id);
        }

        /// <summary>
        /// The same, where the thing named has a key but no definition id.
        /// </summary>
        /// <remarks>
        /// Servers and channels are named this way: they are rows from an account service
        /// rather than content definitions, so the key itself is the only identifier there is
        /// to fall back on -- and reading it as words still beats printing it.
        ///
        /// <b>The trailing role is dropped first.</b> These keys are
        /// <c>server.aurora.<b>name</b></c>, so taking the last segment the way a definition
        /// id is read would name every server in the list "Name". What identifies the thing
        /// is the segment before the role.
        /// </remarks>
        public static string ContentText(ILocalizedTextSource source, LocalizationKey key)
        {
            if (source != null && key.IsValid)
            {
                string text;

                if (source.TryGet(key, out text) && !string.IsNullOrEmpty(text)) return text;
            }

            return Readable(new DefinitionId(WithoutRole(key.Key)));
        }

        /// <summary>Strips the part of a key that says what kind of string it is.</summary>
        private static string WithoutRole(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string[] roles = { ".name", ".title", ".description", ".desc" };

            for (var i = 0; i < roles.Length; i++)
            {
                if (key.EndsWith(roles[i], System.StringComparison.Ordinal))
                {
                    return key.Substring(0, key.Length - roles[i].Length);
                }
            }

            return key;
        }

        /// <summary>
        /// An identifier read as words, for content with no name authored yet.
        /// </summary>
        /// <remarks>"monster.training_slime" reads as "Training Slime". Still the content
        /// deciding what a thing is called -- far worse than a real name and far better than
        /// showing a player a key.</remarks>
        public static string Readable(DefinitionId id)
        {
            if (!id.IsValid) return string.Empty;

            string value = id.Value ?? string.Empty;

            int dot = value.LastIndexOf('.');

            if (dot >= 0 && dot + 1 < value.Length) value = value.Substring(dot + 1);

            string[] words = value.Split('_');
            var built = new System.Text.StringBuilder();

            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;

                if (built.Length > 0) built.Append(' ');

                built.Append(char.ToUpperInvariant(words[i][0]));

                if (words[i].Length > 1) built.Append(words[i].Substring(1));
            }

            return built.ToString();
        }
    }
}
