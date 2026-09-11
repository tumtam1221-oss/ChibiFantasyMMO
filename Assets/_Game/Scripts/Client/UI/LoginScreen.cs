using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>The login screen: an account, a password and one button.</summary>
    /// <remarks>
    /// <b>The password is masked, never logged and never held longer than the request.</b>
    /// It goes straight into the existing login call and the field is cleared afterwards,
    /// whether the attempt succeeded or not -- a failed attempt leaving the password on
    /// screen is how it ends up in a screenshot.
    ///
    /// A failure keeps the player here and says why in words. It never advances, and it
    /// never reports success it did not get.
    /// </remarks>
    public sealed class LoginScreen : SessionScreenBase
    {
        /// <summary>PlayerPrefs keys. A login identifier only -- never a password.</summary>
        private const string RememberKey = "chibi.login.remember";

        private const string RememberedLoginKey = "chibi.login.identifier";

        private Image _rememberBox;
        private Sprite _rememberOff;
        private Sprite _rememberOn;

        /// <summary>Whether the login identifier is kept for next time.</summary>
        public bool Remember { get; private set; }

        private TMP_InputField _account;
        private TMP_InputField _password;
        private Button _submit;
        private TextMeshProUGUI _submitLabel;

        protected override string Title => UiText.Of(Text, UiStrings.LoginTitle);

        private TextMeshProUGUI _footer;
        private TextMeshProUGUI _rememberLabel;
        private TextMeshProUGUI _tagline;

        /// <summary>Rewrites the words this screen painted once, after a language change.</summary>
        public override void Relabel()
        {
            if (_footer != null) _footer.text = UiText.Of(Text, UiStrings.LoginFooter);
            if (_tagline != null) _tagline.text = UiText.Of(Text, UiStrings.LoginTagline);

            if (_rememberLabel != null)
            {
                _rememberLabel.text = UiText.Of(Text, UiStrings.LoginRemember);
            }

            if (_account != null)
            {
                SetPlaceholder(_account, UiText.Of(Text, UiStrings.LoginFieldAccount));
            }

            if (_password != null)
            {
                SetPlaceholder(_password, UiText.Of(Text, UiStrings.LoginFieldPassword));
            }

            // Not simply "Sign in": a language change while a request is in flight must
            // not tell the player the button is ready when it is not.
            SetSubmitEnabled(_submit == null || _submit.interactable);

            base.Relabel();
        }

        /// <summary>Rewrites a field's ghost text, wherever the factory put it.</summary>
        private static void SetPlaceholder(TMP_InputField field, string text)
        {
            var placeholder = field.placeholder as TextMeshProUGUI;

            if (placeholder != null) placeholder.text = text;
        }

        /// <summary>
        /// Where the typed credentials go.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a reference to the API, because <c>IAccountApi</c>
        /// deliberately carries no credential -- how a secret is collected and transmitted is
        /// the transport's business, and this screen names no transport. The composition
        /// wires this to whatever is actually sending the request.
        /// </remarks>
        public System.Action<string, string> Credentials { get; set; }

        /// <summary>
        /// What this build reports about itself.
        /// </summary>
        /// <remarks>Supplied rather than invented here: version compatibility is checked by
        /// the authority, and a screen that made a version up would be claiming something
        /// about the build it is running in.</remarks>
        public VersionSet Versions { get; set; }

        /// <summary>Raised when the server accepted a sign-in.</summary>
        public event System.Action SignedIn;

        /// <summary>What the player typed, for a test to drive without a keyboard.</summary>
        public void Fill(string account, string password)
        {
            EnsureBuilt();

            if (_account != null) _account.text = account;
            if (_password != null) _password.text = password;
        }

        /// <summary>
        /// Sends the login the player typed.
        /// </summary>
        /// <remarks>Refuses to run twice at once: a second press while a request is in
        /// flight would open a second session and the server would refuse it, which reads to
        /// a player as the game rejecting a password that worked.</remarks>
        public void Submit()
        {
            EnsureBuilt();

            if (Session == null || IsBusy) return;

            string account = _account == null ? string.Empty : _account.text;
            string password = _password == null ? string.Empty : _password.text;

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                SetStatus(UiText.Of(Text, UiStrings.LoginPromptCredentials));

                return;
            }

            IsBusy = true;
            SetSubmitEnabled(false);
            SetStatus(UiText.Of(Text, UiStrings.LoginStatusConnecting));

            Credentials?.Invoke(account, password);

            RememberLogin(account);

            LoginResult result = Session.SubmitLogin(new LoginRequest(RequestId.New(),
                Versions));

            // Cleared whatever happened. It has been sent; keeping it achieves nothing and
            // risks everything.
            if (_password != null) _password.text = string.Empty;

            IsBusy = false;
            SetSubmitEnabled(true);

            if (result.IsAccepted)
            {
                SetStatus(string.Empty);
                SignedIn?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }

        protected override void Fetch()
        {
        }

        protected override void BuildRows()
        {
        }

        protected override string EmptyMessage => string.Empty;

        protected override void BuildExtra(RectTransform root)
        {
            // The list frame is not wanted here; a login screen is a form.
            Content.parent.parent.gameObject.SetActive(false);

            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin == null || !skin.HasLoginArt)
            {
                BuildPlainForm(root);

                return;
            }

            BuildPaintedForm(root, skin);
        }

        /// <summary>
        /// The login screen as its art directs: a painted scene, a framed panel, two fields
        /// with their icons drawn in, a checkbox and one wide button.
        /// </summary>
        /// <remarks>
        /// <b>Every word on it is TMP.</b> The art carries no text at all -- not the labels,
        /// not the placeholders, not the button. A screen with its words baked into a picture
        /// cannot be translated and cannot say what actually went wrong.
        ///
        /// <b>Laid out against the 1920x1080 reference.</b> The canvas scaler is already
        /// ScaleWithScreenSize at that resolution with match 0.5, so these numbers are the
        /// design's own and every other resolution is a uniform scale of them.
        ///
        /// <b>It changes appearance and nothing else.</b> The same two fields, the same
        /// submit path, the same Credentials handoff and the same SignedIn event as the
        /// plain form below.
        /// </remarks>
        private void BuildPaintedForm(RectTransform root, PreWorldUiSkin skin)
        {
            // The base screen draws an opaque backdrop and a plain title for a screen with no
            // art. This one has art; both would sit on top of it.
            Transform backdrop = root.Find("Backdrop");

            if (backdrop != null) backdrop.gameObject.SetActive(false);

            Transform title = root.Find("Title");

            if (title != null) title.gameObject.SetActive(false);

            if (skin.LoginBackground != null)
            {
                RectTransform scene = UiFactory.CreateStretched("Scene", root);
                scene.SetAsFirstSibling();

                var painted = scene.gameObject.AddComponent<Image>();
                painted.sprite = skin.LoginBackground;
                painted.raycastTarget = false;
                painted.preserveAspect = false;
            }

            // ---- the frame ---------------------------------------------------------------
            //
            // Wide and shallow, as the design is. Everything below is placed against the
            // panel's centre, so the whole form moves as one if the frame ever moves.

            RectTransform panel = UiFactory.CreateAnchored("Panel", root,
                new Vector2(0.5f, 0.5f), new Vector2(PanelWidth, PanelHeight));

            panel.anchoredPosition = new Vector2(0f, PanelY);

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = skin.LoginPanel;
            panelImage.type = Image.Type.Sliced;

            // The frame is authored at four times the size it is drawn at, so the border is
            // divided down rather than eating the interior.
            // The border divided down, so a thick decorative frame does not dictate how
            // big the window's form has to be. At 2.4 the frame draws 41 units at the sides
            // and 77/68 top and bottom, leaving an interior of 619 by 355.
            panelImage.pixelsPerUnitMultiplier = 2.4f;

            if (skin.LoginLogo != null)
            {
                RectTransform logo = UiFactory.CreateAnchored("Logo", root,
                    new Vector2(0.5f, 0.5f), new Vector2(340f, 255f));

                logo.anchoredPosition = new Vector2(0f, 312f);

                var mark = logo.gameObject.AddComponent<Image>();
                mark.sprite = skin.LoginLogo;
                mark.preserveAspect = true;
                mark.raycastTarget = false;
            }

            // The line under the mark. Text, not art, so it can be translated.
            TextMeshProUGUI tagline = UiFactory.CreateLabel("Tagline", root,
                UiText.Of(Text, UiStrings.LoginTagline), 18f, TextAlignmentOptions.Center);

            _tagline = tagline;

            tagline.color = new Color(0.90f, 0.95f, 1f, 1f);
            tagline.characterSpacing = 6f;
            tagline.fontStyle = FontStyles.Bold;

            RectTransform taglineRect = tagline.rectTransform;
            taglineRect.anchorMin = new Vector2(0.5f, 0.5f);
            taglineRect.anchorMax = new Vector2(0.5f, 0.5f);
            taglineRect.pivot = new Vector2(0.5f, 0.5f);
            taglineRect.sizeDelta = new Vector2(640f, 28f);
            taglineRect.anchoredPosition = new Vector2(0f, 162f);

            // ---- the form ----------------------------------------------------------------
            //
            // One width, one left edge, one height for both fields and the button, so nothing
            // can drift out of alignment. Only the vertical position differs.

            _account = Field(panel, "Account", UiText.Of(Text, UiStrings.LoginFieldAccount),
                skin.LoginFieldId, IdOpaque, AccountY, false);

            _password = Field(panel, "Password", UiText.Of(Text, UiStrings.LoginFieldPassword),
                skin.LoginFieldPassword, PasswordOpaque, PasswordY, true);

            BuildRemember(panel, skin);

            _submit = UiFactory.CreateButton("Submit", panel,
                UiText.Of(Text, UiStrings.LoginButtonSubmit), out _submitLabel);

            RectTransform submitRect = _submit.GetComponent<RectTransform>();
            submitRect.anchorMin = new Vector2(0.5f, 0.5f);
            submitRect.anchorMax = new Vector2(0.5f, 0.5f);
            submitRect.pivot = new Vector2(0.5f, 0.5f);

            FitOpaque(submitRect, ButtonOpaque, ButtonWidth, ButtonHeight, SubmitY);

            var submitImage = _submit.GetComponent<Image>();
            submitImage.sprite = skin.LoginButtonNormal;
            submitImage.type = Image.Type.Simple;
            submitImage.color = Color.white;

            _submit.transition = Selectable.Transition.SpriteSwap;

            SpriteState state = _submit.spriteState;
            state.highlightedSprite = skin.LoginButtonHover;
            state.pressedSprite = skin.LoginButtonHover;
            state.selectedSprite = skin.LoginButtonHover;
            state.disabledSprite = skin.LoginButtonDisabled;
            _submit.spriteState = state;

            _submitLabel.fontSize = 22f;
            _submitLabel.fontStyle = FontStyles.Bold;

            // The lit bar is the middle of the art; the word sits on it rather than on the
            // transparent margin above and below.
            float buttonPad = (submitRect.rect.height - ButtonHeight) * 0.5f;

            RectTransform labelRect = _submitLabel.rectTransform;
            labelRect.offsetMin = new Vector2(0f, buttonPad);
            labelRect.offsetMax = new Vector2(0f, -buttonPad);

            _submit.onClick.AddListener(Submit);

            TextMeshProUGUI footer = UiFactory.CreateLabel("Footer", panel,
                UiText.Of(Text, UiStrings.LoginFooter), 16f, TextAlignmentOptions.Center);

            _footer = footer;

            footer.color = new Color(0.62f, 0.74f, 0.92f, 1f);

            RectTransform footerRect = footer.rectTransform;
            footerRect.anchorMin = new Vector2(0.5f, 0.5f);
            footerRect.anchorMax = new Vector2(0.5f, 0.5f);
            footerRect.pivot = new Vector2(0.5f, 0.5f);
            footerRect.sizeDelta = new Vector2(RowWidth, 22f);
            footerRect.anchoredPosition = new Vector2(0f, FooterY);
        }

        // ---- the one set of numbers the form is built from ---------------------------------
        //
        // Every row shares a width and a height so the two fields and the button line up on
        // the same left and right edge; only the Y differs. The gaps below are the spacing
        // between one row's centre and the next, and are equal between the two fields.

        /// <summary>
        /// The frame's size, as a share of the 1920x1080 design.
        /// </summary>
        /// <remarks>
        /// <b>Measured against the screen, not the artwork.</b> The panel art's frame is
        /// thick -- 349 of its 941 pixels, better than a third of it -- and a nine-slice
        /// holds a border at a fixed size, so sizing the panel to fit the frame produced a
        /// form half again too big for the window. The frame is drawn smaller instead (see
        /// the multiplier where it is applied) and the panel is sized to the design: about
        /// 36% of the width and 46% of the height, which is what the reference shows.
        /// </remarks>
        private const float PanelWidth = 700f;
        private const float PanelHeight = 500f;
        private const float PanelY = -110f;

        /// <summary>Shared width of both fields and the button.</summary>
        private const float RowWidth = 520f;

        /// <summary>Shared height. The lit bar is about 43% of it; the rest is margin.</summary>
        /// <summary>
        /// The drawn height of a field's bar.
        /// </summary>
        /// <remarks>
        /// <b>Not chosen -- derived.</b> The field artwork is drawn 1911x299, an aspect of
        /// 6.39, so at <see cref="RowWidth"/> across it is 100 tall. Picking any other number
        /// stretches the image on one axis only, which is what flattened the icons and made
        /// the bars look squashed. The two field images differ in aspect by under two per
        /// cent, so one height serves both without a visible difference.
        /// </remarks>
        private const float RowHeight = 81f;

        /// <summary>
        /// How wide the button is drawn.
        /// </summary>
        /// <remarks>Narrower than a field on purpose. A button as wide as the form reads as
        /// a banner rather than something to press, and at this art's 4.62:1 every extra bit
        /// of width costs height as well.</remarks>
        private const float ButtonWidth = 340f;

        /// <summary>
        /// The drawn height of the button.
        /// </summary>
        /// <remarks>Its art is chunkier than a field's -- 1835x397, an aspect of 4.62 -- so
        /// at the same width it stands 138 tall. That the button ends up about 1.4 times a
        /// field's height is the artwork's own proportion, and matches the reference.</remarks>
        private const float ButtonHeight = 74f;



        /// <summary>
        /// Where the artwork actually sits inside each row image, as fractions of it.
        /// </summary>
        /// <remarks>Measured from the imported textures (2048x683) by scanning for pixels
        /// with any alpha. They are not the same, which is the whole reason
        /// <see cref="FitOpaque"/> exists: left, bottom, right, top.</remarks>
        private static readonly Vector4 IdOpaque =
            new Vector4(67f / 2048f, 205f / 683f, 1978f / 2048f, 504f / 683f);

        private static readonly Vector4 PasswordOpaque =
            new Vector4(138f / 2048f, 201f / 683f, 1911f / 2048f, 484f / 683f);

        private static readonly Vector4 ButtonOpaque =
            new Vector4(107f / 2048f, 154f / 683f, 1942f / 2048f, 551f / 683f);

        /// <summary>
        /// Where the tick box is actually drawn inside each checkbox image.
        /// </summary>
        /// <remarks>Both are 1254x1254 canvases, and both are mostly empty -- the box fills
        /// 35% of the unticked one and 51% of the ticked one. Drawn without compensating,
        /// the box is a third of the size it is asked to be and grows by half the moment it
        /// is ticked. Measured: left, bottom, right, top.</remarks>
        private static readonly Vector4 CheckboxOffOpaque =
            new Vector4(407f / 1254f, 412f / 1254f, 844f / 1254f, 844f / 1254f);

        private static readonly Vector4 CheckboxOnOpaque =
            new Vector4(304f / 1254f, 324f / 1254f, 949f / 1254f, 947f / 1254f);

        /// <summary>How large the tick box is drawn, in design units.</summary>
        private const float CheckboxSize = 30f;

        /// <summary>Where its centre sits from the left of the row.</summary>
        private const float CheckboxX = 22f;

        /// <summary>How much of a field's width the icon block takes.</summary>
        private const float IconShare = 0.17f;

        private const float AccountY = 130f;
        private const float PasswordY = 40f;
        private const float RememberY = -30f;
        private const float SubmitY = -95f;
        private const float FooterY = -158f;

        /// <summary>
        /// One field, wearing art that already contains its icon.
        /// </summary>
        /// <remarks>The text is inset past the icon block drawn into the sprite. The sprite
        /// is nine-sliced with that block inside the left border, so the icon keeps its shape
        /// while the middle of the field stretches.</remarks>
        private static TMP_InputField Field(RectTransform panel, string name, string hint,
            Sprite art, Vector4 opaque, float centre, bool password)
        {
            TMP_InputField field = UiFactory.CreateField(name, panel, hint, password);

            var background = field.GetComponent<Image>();
            background.sprite = art;

            // Stretched, not sliced: the rect is sized so that the drawn part lands on the
            // shared row, which is only possible if the whole image maps linearly onto it.
            background.type = Image.Type.Simple;
            background.color = Color.white;

            RectTransform rect = field.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            FitOpaque(rect, opaque, RowWidth, RowHeight, centre);

            // The text starts after the icon block, measured as a share of the drawn bar so
            // it follows the bar rather than the rect around it.
            float left = (rect.rect.width - RowWidth) * 0.5f + RowWidth * IconShare;
            float right = (rect.rect.width - RowWidth) * 0.5f + RowWidth * 0.04f;
            float pad = (rect.rect.height - RowHeight) * 0.5f + RowHeight * 0.18f;

            var viewport = (RectTransform)field.textViewport;
            viewport.offsetMin = new Vector2(left, pad);
            viewport.offsetMax = new Vector2(-right, -pad);

            field.textComponent.fontSize = 22f;
            field.pointSize = 22f;

            var placeholder = field.placeholder as TextMeshProUGUI;

            if (placeholder != null)
            {
                placeholder.fontSize = 22f;
                placeholder.color = new Color(0.58f, 0.70f, 0.88f, 1f);
            }

            return field;
        }

        /// <summary>Fits a row's art onto the shared band. See UiFactory.FitSprite.</summary>
        private static void FitOpaque(RectTransform rect, Vector4 opaque, float width,
            float height, float centre, float centreX = 0f)
        {
            UiFactory.FitSprite(rect, opaque, width, height, new Vector2(centreX, centre));
        }
        /// <summary>
        /// The "remember me" row.
        /// </summary>
        /// <remarks>
        /// <b>It remembers a login, never a password.</b> The identifier is written to
        /// <c>PlayerPrefs</c> so a returning player does not retype it; the password is not
        /// written anywhere, and there is no field here that could hold one. A "remember me"
        /// that stored a password would be a plaintext credential on disk, which this project
        /// forbids outright.
        /// </remarks>
        private void BuildRemember(RectTransform panel, PreWorldUiSkin skin)
        {
            RectTransform row = UiFactory.CreateAnchored("Remember", panel,
                new Vector2(0.5f, 0.5f), new Vector2(RowWidth, 34f));

            row.anchoredPosition = new Vector2(0f, RememberY);

            var box = new GameObject("Checkbox", typeof(RectTransform), typeof(Image),
                typeof(Button)).GetComponent<RectTransform>();

            box.SetParent(row, false);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);

            _rememberBox = box.GetComponent<Image>();
            _rememberOff = skin.CheckboxOff;
            _rememberOn = skin.CheckboxOn;
            _rememberBox.preserveAspect = true;

            box.GetComponent<Button>().onClick.AddListener(ToggleRemember);

            TextMeshProUGUI label = UiFactory.CreateLabel("Label", row,
                UiText.Of(Text, UiStrings.LoginRemember), 18f);

            _rememberLabel = label;
            label.color = new Color(0.78f, 0.86f, 0.97f, 1f);

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(62f, 0f);
            labelRect.offsetMax = Vector2.zero;

            Remember = PlayerPrefs.GetInt(RememberKey, 0) == 1;

            if (Remember && _account != null)
            {
                _account.text = PlayerPrefs.GetString(RememberedLoginKey, string.Empty);
            }

            RefreshRemember();
        }

        /// <summary>Turns "remember me" on or off, and forgets at once when turned off.</summary>
        public void ToggleRemember()
        {
            Remember = !Remember;

            PlayerPrefs.SetInt(RememberKey, Remember ? 1 : 0);

            if (!Remember) PlayerPrefs.DeleteKey(RememberedLoginKey);

            PlayerPrefs.Save();

            RefreshRemember();
        }

        private void RefreshRemember()
        {
            if (_rememberBox == null) return;

            Sprite art = Remember ? _rememberOn : _rememberOff;

            if (art != null) _rememberBox.sprite = art;

            _rememberBox.color = Color.white;

            // The two states are drawn at different sizes inside their images, so the rect is
            // re-fitted on every change. Without this the box grows by half when it is
            // ticked, which reads as the control jumping.
            FitOpaque(_rememberBox.rectTransform,
                Remember ? CheckboxOnOpaque : CheckboxOffOpaque,
                CheckboxSize, CheckboxSize, 0f, CheckboxX);
        }

        /// <summary>Stores the login just typed, when the player asked for that.</summary>
        /// <remarks>Called after a submit. The password is deliberately not a parameter.</remarks>
        private void RememberLogin(string login)
        {
            if (!Remember) return;

            PlayerPrefs.SetString(RememberedLoginKey, login ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>The unstyled form, for a build with no login art and for every test.</summary>
        private void BuildPlainForm(RectTransform root)
        {
            RectTransform form = UiFactory.CreateAnchored("Form", root,
                new Vector2(0.5f, 0.5f), new Vector2(520f, 280f));

            Image frameImage = UiFactory.CreatePanel("Frame", form, UiFactory.Panel);
            frameImage.rectTransform.SetAsFirstSibling();

            var frame = (RectTransform)form.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _account = UiFactory.CreateField("Account", form,
                UiText.Of(Text, UiStrings.LoginFieldAccountShort));
            Place(_account.GetComponent<RectTransform>(), 200f);

            _password = UiFactory.CreateField("Password", form,
                UiText.Of(Text, UiStrings.LoginFieldPassword), password: true);
            Place(_password.GetComponent<RectTransform>(), 130f);

            _submit = UiFactory.CreateButton("Submit", form,
                UiText.Of(Text, UiStrings.LoginButtonSubmit), out _submitLabel);
            Place(_submit.GetComponent<RectTransform>(), 50f);

            _submit.onClick.AddListener(Submit);
        }

        private static void Place(RectTransform rect, float fromBottom)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-48f, 54f);
            rect.anchoredPosition = new Vector2(0f, fromBottom);
        }

        private void SetSubmitEnabled(bool enabled)
        {
            if (_submit != null) _submit.interactable = enabled;

            if (_submitLabel != null)
            {
                _submitLabel.text = UiText.Of(Text, enabled
                    ? UiStrings.LoginButtonSubmit
                    : UiStrings.LoginButtonSubmitting);
            }
        }
    }
}
