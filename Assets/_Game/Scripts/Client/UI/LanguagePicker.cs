using System.Collections.Generic;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The row of buttons a player picks their language with.
    /// </summary>
    /// <remarks>
    /// <b>Every language is offered by its own name.</b> "ไทย", not "Thai". A picker written
    /// in the language you are trying to leave is the one menu a player cannot use, and
    /// somebody who reads only Thai needs to recognise the word, not translate it.
    ///
    /// <b>On the sign-in screen, because that is the first thing anybody reads.</b> A
    /// language setting buried behind the world would be a setting a player could only reach
    /// after getting through three screens they could not read.
    ///
    /// <b>It asks; it changes nothing itself.</b> Pressing raises
    /// <see cref="Picked"/>, and the client root switches the language, remembers it and
    /// tells every screen to redraw. This holds no service and no preference, so a test can
    /// press a button and assert what was asked for without a client existing.
    /// </remarks>
    public sealed class LanguagePicker : MonoBehaviour
    {
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<GameLanguage> _offered = new List<GameLanguage>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        private TextMeshProUGUI _caption;

        /// <summary>Raised when somebody picks a language.</summary>
        public event System.Action<GameLanguage> Picked;

        /// <summary>Which language is currently shown as chosen.</summary>
        public GameLanguage Current { get; private set; } = GameLanguage.English;

        /// <summary>Where the caption is translated. The language names never are.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>What each button offers, in order. For a test.</summary>
        public IReadOnlyList<GameLanguage> Offered => _offered;

        /// <summary>What each button reads. For a test, and for the font check.</summary>
        public IReadOnlyList<string> Captions
        {
            get
            {
                var read = new List<string>(_labels.Count);

                for (var i = 0; i < _labels.Count; i++)
                {
                    read.Add(_labels[i] == null ? string.Empty : _labels[i].text);
                }

                return read;
            }
        }

        /// <summary>
        /// Builds the row into a corner of a screen.
        /// </summary>
        /// <remarks>Anchored top-right and small, because it is a setting rather than a
        /// step: it must be findable without competing with the thing the screen is
        /// actually for.</remarks>
        public void Compose(RectTransform root, GameLanguage current)
        {
            if (root == null || _buttons.Count > 0) return;

            Current = current;

            RectTransform row = UiFactory.CreateAnchored("Language", root,
                new Vector2(1f, 1f), new Vector2(300f, 40f));

            row.pivot = new Vector2(1f, 1f);
            row.anchoredPosition = new Vector2(-24f, -24f);

            _caption = UiFactory.CreateLabel("Caption", row,
                UiText.Of(Text, UiStrings.CommonLanguage), 18f, TextAlignmentOptions.Right);

            _caption.color = UiFactory.Muted;

            RectTransform captionRect = _caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(0f, 1f);
            captionRect.pivot = new Vector2(0f, 0.5f);
            captionRect.sizeDelta = new Vector2(96f, 0f);
            captionRect.anchoredPosition = Vector2.zero;

            GameLanguage[] all = GameLanguages.All;

            for (var i = 0; i < all.Length; i++)
            {
                GameLanguage language = all[i];

                Button button = UiFactory.CreateButton("Language " + GameLanguages.CodeOf(language),
                    row, GameLanguages.NameOf(language), out TextMeshProUGUI label);

                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(94f, 34f);
                rect.anchoredPosition = new Vector2(104f + (i * 100f), 0f);

                label.fontSize = 18f;

                button.onClick.AddListener(() => Pick(language));

                _buttons.Add(button);
                _offered.Add(language);
                _labels.Add(label);
            }

            ShowCurrent();
        }

        /// <summary>Picks a language, as pressing its button does.</summary>
        public void Pick(GameLanguage language)
        {
            Current = language;

            ShowCurrent();

            Picked?.Invoke(language);
        }

        /// <summary>Redraws the caption after the language changed underneath it.</summary>
        public void Relabel(GameLanguage current)
        {
            Current = current;

            if (_caption != null) _caption.text = UiText.Of(Text, UiStrings.CommonLanguage);

            ShowCurrent();
        }

        /// <summary>
        /// The chosen language's button is the one that cannot be pressed.
        /// </summary>
        /// <remarks>Rather than a tick or a highlight colour: a disabled button reads as
        /// "you are already here" in every visual style the screens might wear, and it costs
        /// nothing to draw.</remarks>
        private void ShowCurrent()
        {
            for (var i = 0; i < _buttons.Count && i < _offered.Count; i++)
            {
                if (_buttons[i] != null) _buttons[i].interactable = _offered[i] != Current;
            }
        }
    }
}
