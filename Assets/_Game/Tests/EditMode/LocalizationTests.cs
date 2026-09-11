using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The game can be read in Thai, and cannot quietly stop being able to.
    /// </summary>
    /// <remarks>
    /// <b>What these are really guarding.</b> A localisation that is 95% done looks finished:
    /// every screen renders, nothing throws, and the five untranslated strings are only found
    /// by a player. So the tests here are mostly completeness tests -- every key the screens
    /// use has a translation, every placeholder survives it, and every name the content
    /// carries resolves to a word rather than to a key.
    ///
    /// <b>The scope decision is asserted, not assumed.</b> Item, monster and NPC names are
    /// deliberately English in both languages. That is a decision somebody made, so it is
    /// written down as a test: if a later patch quietly translates a monster name, this fails
    /// and somebody has to agree to it rather than discover it.
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationTests
    {
        private const string ThaiFile = "Assets/_Game/Data/Localization/th.json";
        private const string EnglishFile = "Assets/_Game/Data/Localization/en.json";
        private const string CataloguePath =
            "Assets/_Game/Data/Localization/LocalizationCatalogue.asset";
        private const string ContentPath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private static Dictionary<string, string> Read(string path)
        {
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

            Assert.That(file, Is.Not.Null, path + " is missing");

            List<KeyValuePair<string, string>> entries;
            string error;

            Assert.That(LocalizationCatalogue.Parse(file.text, out entries, out error),
                Is.True, path + " did not parse: " + error);

            var table = new Dictionary<string, string>(System.StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in entries) table[entry.Key] = entry.Value;

            return table;
        }

        private static WorldContentCatalogue Content()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(ContentPath);

            Assert.That(content, Is.Not.Null, ContentPath + " is missing");

            return content;
        }

        // ---- the tables are complete -------------------------------------------------------

        [Test]
        public void EveryWordTheScreensSayHasAThaiTranslation()
        {
            Dictionary<string, string> thai = Read(ThaiFile);

            var missing = new List<string>();

            foreach (string key in UiStrings.Keys)
            {
                string text;

                if (!thai.TryGetValue(key, out text) || string.IsNullOrWhiteSpace(text))
                {
                    missing.Add(key);
                }
            }

            Assert.That(missing, Is.Empty,
                "these screens would still be English for a Thai player: "
                + string.Join(", ", missing));
        }

        [Test]
        public void EveryShippedEnglishStringIsActuallyThere()
        {
            // The last link in the fallback chain. A key with no English is a key that can
            // put itself on screen, which is the failure UiText exists to make impossible.
            foreach (KeyValuePair<string, string> shipped in UiStrings.English)
            {
                Assert.That(shipped.Value, Is.Not.Null.And.Not.Empty,
                    shipped.Key + " ships with no English");
            }
        }

        [Test]
        public void TheThaiFileSaysNothingTheScreensDoNotAskFor()
        {
            // A key nobody looks up is either a typo or a screen that was deleted. Either way
            // it is dead weight a translator is still being asked to maintain.
            Dictionary<string, string> thai = Read(ThaiFile);

            var known = new HashSet<string>(UiStrings.Keys, System.StringComparer.Ordinal);

            // Quest names and descriptions are content, not chrome, and legitimately live in
            // the language files rather than in UiStrings.
            var stray = new List<string>();

            foreach (string key in thai.Keys)
            {
                if (known.Contains(key)) continue;
                if (key.StartsWith("quest.", System.StringComparison.Ordinal)) continue;
                if (key.StartsWith("_", System.StringComparison.Ordinal)) continue;

                stray.Add(key);
            }

            Assert.That(stray, Is.Empty,
                "nothing looks these up: " + string.Join(", ", stray));
        }

        [Test]
        public void EveryPlaceholderSurvivesTranslation()
        {
            // "Requires level {0}" translated without its {0} silently drops the number, and
            // string.Format is perfectly happy to produce a sentence with the fact missing.
            Dictionary<string, string> thai = Read(ThaiFile);

            var wrong = new List<string>();

            foreach (KeyValuePair<string, string> shipped in UiStrings.English)
            {
                string translated;

                if (!thai.TryGetValue(shipped.Key, out translated)) continue;

                HashSet<string> expected = Holes(shipped.Value);
                HashSet<string> actual = Holes(translated);

                if (!expected.SetEquals(actual))
                {
                    wrong.Add(shipped.Key + " (expected " + Join(expected)
                        + ", found " + Join(actual) + ")");
                }
            }

            Assert.That(wrong, Is.Empty,
                "these translations lose or invent a value: " + string.Join("; ", wrong));
        }

        private static HashSet<string> Holes(string pattern)
        {
            var found = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(pattern ?? string.Empty, @"\{(\d+)\}"))
            {
                found.Add(match.Groups[1].Value);
            }

            return found;
        }

        private static string Join(HashSet<string> holes)
        {
            var listed = new List<string>(holes);

            listed.Sort(System.StringComparer.Ordinal);

            return listed.Count == 0 ? "none" : string.Join(",", listed);
        }

        // ---- names of things ----------------------------------------------------------------

        [Test]
        public void EveryNameTheWorldCarriesResolvesToAWordRatherThanAKey()
        {
            // The defect this closes: nothing supplied a table, LocalizedText.Resolve fell
            // back to the key, and the monster health bar read "monster.training_slime.name"
            // over a slime's head.
            Dictionary<string, string> english = Read(EnglishFile);

            WorldContentCatalogue content = Content();

            var missing = new List<string>();

            void Check(LocalizationKey key, string what)
            {
                if (!key.IsValid) return;

                string text;

                if (!english.TryGetValue(key.Key, out text) || string.IsNullOrWhiteSpace(text))
                {
                    missing.Add(what + " " + key.Key);
                }
            }

            foreach (ItemDefinition item in content.BuildItems().All) Check(item.NameKey, "item");
            foreach (MapDefinition map in content.BuildMaps().All) Check(map.NameKey, "map");
            foreach (MonsterDefinition m in content.BuildMonsters().All) Check(m.NameKey, "monster");
            foreach (NPCDefinition npc in content.BuildNpcs().All) Check(npc.NameKey, "npc");
            foreach (QuestDefinition q in content.BuildQuests().All) Check(q.NameKey, "quest");

            Assert.That(missing, Is.Empty,
                "these would draw as raw keys: " + string.Join(", ", missing));
        }

        [Test]
        public void ItemMonsterAndNpcNamesAreDeliberatelyNotTranslated()
        {
            // The scope that was asked for, written down. A name is what players type to each
            // other; two vocabularies for one slime is worse than one foreign word. If this
            // fails, somebody translated a name -- which may be right, but is a decision.
            Dictionary<string, string> thai = Read(ThaiFile);

            WorldContentCatalogue content = Content();

            var translated = new List<string>();

            void Check(LocalizationKey key)
            {
                if (key.IsValid && thai.ContainsKey(key.Key)) translated.Add(key.Key);
            }

            foreach (ItemDefinition item in content.BuildItems().All) Check(item.NameKey);
            foreach (MonsterDefinition m in content.BuildMonsters().All) Check(m.NameKey);
            foreach (NPCDefinition npc in content.BuildNpcs().All) Check(npc.NameKey);

            Assert.That(translated, Is.Empty,
                "names are English in both languages by decision: "
                + string.Join(", ", translated));
        }

        [Test]
        public void QuestNamesAndDescriptionsAreTranslated()
        {
            // The other half of the same decision: a quest is read, not named to a friend.
            Dictionary<string, string> thai = Read(ThaiFile);

            WorldContentCatalogue content = Content();

            var missing = new List<string>();

            foreach (QuestDefinition quest in content.BuildQuests().All)
            {
                if (quest.NameKey.IsValid && !thai.ContainsKey(quest.NameKey.Key))
                {
                    missing.Add(quest.NameKey.Key);
                }

                if (quest.DescriptionKey.IsValid && !thai.ContainsKey(quest.DescriptionKey.Key))
                {
                    missing.Add(quest.DescriptionKey.Key);
                }
            }

            Assert.That(missing, Is.Empty,
                "untranslated quest text: " + string.Join(", ", missing));
        }

        [Test]
        public void AnUnnamedThingReadsAsWordsRatherThanAsAnIdentifier()
        {
            Assert.That(UiText.ContentText(null, LocalizationKey.None,
                new DefinitionId("monster.training_slime")), Is.EqualTo("Training Slime"));

            Assert.That(UiText.ContentText(null, new LocalizationKey("item.slime_gel.name"),
                new DefinitionId("item.slime_gel")), Is.EqualTo("Slime Gel"),
                "a key with no table behind it must not reach the screen");
        }

        // ---- the fallback chain ---------------------------------------------------------------

        [Test]
        public void ThaiFallsBackToEnglishRatherThanToNothing()
        {
            var service = new LocalizationService();

            service.Load(GameLanguage.English, new[]
            {
                new KeyValuePair<string, string>("a", "Alpha"),
                new KeyValuePair<string, string>("b", "Bravo")
            });

            service.Load(GameLanguage.Thai, new[]
            {
                new KeyValuePair<string, string>("a", "อัลฟา")
            });

            service.Use(GameLanguage.Thai);

            string text;

            Assert.That(service.TryGet(new LocalizationKey("a"), out text), Is.True);
            Assert.That(text, Is.EqualTo("อัลฟา"));

            Assert.That(service.TryGet(new LocalizationKey("b"), out text), Is.True,
                "an untranslated key must fall back, not vanish");
            Assert.That(text, Is.EqualTo("Bravo"));
        }

        [Test]
        public void AKeyNobodyHasIsNotFoundRatherThanReturnedAsItself()
        {
            // What lets every caller supply its own fallback. Returning the key here is how
            // a screen ends up showing "ui.quest.empty" to a player.
            var service = new LocalizationService();

            service.Load(GameLanguage.English, new KeyValuePair<string, string>[0]);

            string text;

            Assert.That(service.TryGet(new LocalizationKey("ui.nothing"), out text), Is.False);
            Assert.That(text, Is.Null.Or.Empty);
        }

        [Test]
        public void AnEmptyTranslationIsTreatedAsNoTranslation()
        {
            // A blank line in a translation file is an untranslated row, not a decision to
            // draw nothing. Honouring it literally would blank a label.
            var service = new LocalizationService();

            service.Load(GameLanguage.English, new[]
            {
                new KeyValuePair<string, string>("a", "Alpha")
            });

            service.Load(GameLanguage.Thai, new[]
            {
                new KeyValuePair<string, string>("a", string.Empty)
            });

            service.Use(GameLanguage.Thai);

            string text;

            service.TryGet(new LocalizationKey("a"), out text);

            Assert.That(text, Is.EqualTo("Alpha"));
        }

        [Test]
        public void AViewWithNoSourceAtAllStillDrawsEnglish()
        {
            Assert.That(UiText.Of(null, UiStrings.QuestEmpty), Is.EqualTo("Nothing here."));
            Assert.That(UiText.Format(null, UiStrings.QuestRequiresLevel, 5),
                Is.EqualTo("Requires level 5"));
        }

        [Test]
        public void ATranslationWithABrokenPlaceholderFallsBackRatherThanThrowing()
        {
            var service = new LocalizationService();

            service.Load(GameLanguage.Thai, new[]
            {
                new KeyValuePair<string, string>(UiStrings.QuestRequiresLevel, "ต้องเลเวล {9}")
            });

            service.Use(GameLanguage.Thai);

            Assert.That(UiText.Format(service, UiStrings.QuestRequiresLevel, 5),
                Is.EqualTo("Requires level 5"),
                "a bad brace in a content file must not take a screen down");
        }

        // ---- the shipped catalogue ---------------------------------------------------------------

        [Test]
        public void TheShippedCatalogueLoadsBothLanguages()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<LocalizationCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, CataloguePath + " is missing");

            LocalizationService service = catalogue.Build();

            Assert.That(service.Has(GameLanguage.English), Is.True);
            Assert.That(service.Has(GameLanguage.Thai), Is.True);

            Assert.That(service.CountOf(GameLanguage.English),
                Is.GreaterThanOrEqualTo(UiStrings.Count),
                "English must carry everything the code ships plus the content names");
        }

        [Test]
        public void TheShippedCatalogueActuallyChangesWhatAScreenSays()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<LocalizationCatalogue>(CataloguePath);

            LocalizationService service = catalogue.Build();

            string english = UiText.Of(service, UiStrings.QuestTabCompleted);

            service.Use(GameLanguage.Thai);

            string thai = UiText.Of(service, UiStrings.QuestTabCompleted);

            Assert.That(english, Is.EqualTo("Completed"));
            Assert.That(thai, Is.Not.EqualTo(english));
            Assert.That(thai, Does.Not.Contain("ui."), "a key reached the screen");
        }

        [Test]
        public void SwitchingLanguageTellsWhoeverIsDrawing()
        {
            var service = new LocalizationService();

            var raised = 0;

            service.Changed += () => raised++;

            service.Use(GameLanguage.Thai);
            service.Use(GameLanguage.Thai);

            Assert.That(raised, Is.EqualTo(1),
                "a redundant set must not make every screen redraw");
        }

        // ---- the NPC lines --------------------------------------------------------------------

        [Test]
        public void EveryNpcRoleHasSomethingToSayInBothLanguages()
        {
            // NpcDialogueView builds its key from the role's name, so this is the test that
            // the enum and the strings cannot drift apart.
            Dictionary<string, string> thai = Read(ThaiFile);

            var missing = new List<string>();

            foreach (NpcRole role in System.Enum.GetValues(typeof(NpcRole)))
            {
                string key = NpcDialogueView.KeyFor(role).Key;

                if (UiStrings.EnglishFor(key) == null) missing.Add("english " + key);
                if (!thai.ContainsKey(key)) missing.Add("thai " + key);
            }

            Assert.That(missing, Is.Empty,
                "an NPC would open a blank panel: " + string.Join(", ", missing));
        }

        // ---- the font the letters need ----------------------------------------------------------

        [Test]
        public void ThaiCanActuallyBeDrawn()
        {
            // Measured rather than assumed. TextMeshPro's default asset is a static atlas of
            // 250 Latin characters and resolves no Thai at all, so translating the pre-world
            // screens without this ships a build made of empty boxes.
            Assert.That(LocalizationFonts.CanDrawThai(), Is.True,
                "no Thai-capable font is in TextMeshPro's fallback list");

            // The other half, and the one that actually broke: a Thai fallback that arrives
            // by emptying the Latin font's character table is worse than no fallback at all.
            // That happened -- the shared LiberationSans asset was left with zero characters
            // on disk, every Latin letter fell through to the Thai face, and the editor
            // stalled rasterising the whole UI. This fails if it ever happens again.
            Assert.That(LocalizationFonts.CanDrawLatin(), Is.True,
                "the default font has lost its characters -- restore it from version control");

            Assert.That(LocalizationFonts.Verify(), Is.True);
        }

        // ---- the preference -----------------------------------------------------------------------

        [Test]
        public void AChosenLanguageIsRememberedAndAnUnchosenOneIsNot()
        {
            GameLanguage before = LanguagePreference.Current;
            bool had = LanguagePreference.HasChosen;

            try
            {
                LanguagePreference.Forget();

                Assert.That(LanguagePreference.HasChosen, Is.False);

                LanguagePreference.Remember(GameLanguage.Thai);

                Assert.That(LanguagePreference.HasChosen, Is.True);
                Assert.That(LanguagePreference.Current, Is.EqualTo(GameLanguage.Thai));
            }
            finally
            {
                if (had) LanguagePreference.Remember(before);
                else LanguagePreference.Forget();
            }
        }

        [Test]
        public void AnUnknownLanguageCodeIsEnglishRatherThanAnError()
        {
            Assert.That(GameLanguages.FromCode("klingon"), Is.EqualTo(GameLanguage.English));
            Assert.That(GameLanguages.FromCode(null), Is.EqualTo(GameLanguage.English));
            Assert.That(GameLanguages.FromCode("TH"), Is.EqualTo(GameLanguage.Thai));
        }

        [Test]
        public void EachLanguageOffersItselfInItsOwnScript()
        {
            // A picker written in the language you are leaving is the one menu a player
            // cannot use.
            Assert.That(GameLanguages.NameOf(GameLanguage.Thai), Is.EqualTo("ไทย"));
            Assert.That(GameLanguages.NameOf(GameLanguage.English), Is.EqualTo("English"));
        }
    }
}
