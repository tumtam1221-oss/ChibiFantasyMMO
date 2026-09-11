using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// What every screen before the world has in common.
    /// </summary>
    /// <remarks>
    /// <b>A screen shows and asks; it decides nothing.</b> Whether a server may be picked,
    /// whether a login is valid, whether a character belongs to this account -- all of that
    /// is <see cref="SessionUiController"/>'s and the API's behind it. What lives here is a
    /// title, a list, a status line and a back button, because those four are the whole of
    /// what these screens are.
    ///
    /// <b>Built in code and rebuilt on change.</b> Rows are destroyed and recreated when the
    /// underlying list changes rather than every frame; a menu that rebuilt itself sixty
    /// times a second would allocate for no reason and flicker a selection.
    /// </remarks>
    public abstract class SessionScreenBase : MonoBehaviour
    {
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _titleLabel;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private bool _built;

        /// <summary>The controller this screen shows. Bound by the flow driver.</summary>
        public SessionUiController Session { get; private set; }

        /// <summary>
        /// Where this screen's words are translated. Optional.
        /// </summary>
        /// <remarks>
        /// <b>Taken from the controller rather than injected separately.</b> A screen is
        /// handed its controller by whoever composed the scene, and the language is a
        /// property of that same composition -- a second wire would be a second thing to
        /// forget, and a screen wired for data but not for language is a screen that is half
        /// translated.
        ///
        /// Assigning it relabels immediately, because these screens build their widgets on
        /// first use and that happens before anything has had a chance to say what language
        /// the player reads.
        /// </remarks>
        public ILocalizedTextSource Text
        {
            get => _text;
            set
            {
                _text = value;

                Relabel();
            }
        }

        private ILocalizedTextSource _text;

        /// <summary>Where rows are put.</summary>
        protected RectTransform Content { get; private set; }

        /// <summary>The last message shown, so a test can read it without a renderer.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        /// <summary>Whether a request is in flight, so a button cannot be double-pressed.</summary>
        public bool IsBusy { get; protected set; }

        protected abstract string Title { get; }

        /// <summary>Fills the list from the controller. Called when something changed.</summary>
        protected abstract void BuildRows();

        /// <summary>Fetches whatever this screen lists. Called once on binding.</summary>
        protected abstract void Fetch();

        public void Bind(SessionUiController session)
        {
            EnsureBuilt();

            Session = session;

            if (Session == null) return;

            // The language travels with the controller, so a scene wired for data is wired
            // for language too. Set before anything is drawn, so nothing is drawn twice.
            if (Session.Text != null) Text = Session.Text;

            IsBusy = true;
            SetStatus(UiText.Of(Text, UiStrings.CommonLoading));

            Fetch();

            IsBusy = false;

            Rebuild();
        }

        /// <summary>Rebuilds the list from whatever the controller currently holds.</summary>
        public void Rebuild()
        {
            EnsureBuilt();

            ClearRows();

            if (Session == null) return;

            BuildRows();

            if (HasContent) SetStatus(string.Empty);
            else SetStatus(EmptyMessage);
        }

        /// <summary>What to say when the list is empty. Overridden per screen.</summary>
        protected virtual string EmptyMessage => UiText.Of(Text, UiStrings.CommonNothingToShow);

        /// <summary>
        /// Redraws every word this screen wrote once and would otherwise never revisit.
        /// </summary>
        /// <remarks>
        /// <b>What this exists for.</b> Titles, headers and button captions are written when
        /// the screen is built and then never touched again -- they have no reason to change,
        /// until the player changes language. Without this, switching language would redraw
        /// the rows and the status line and leave the title saying "Choose a server" over a
        /// Thai list, which looks less like a language setting than like a bug.
        ///
        /// Rows are not relabelled here: they are rebuilt from the controller by
        /// <see cref="Rebuild"/>, which is called after this.
        /// </remarks>
        public virtual void Relabel()
        {
            if (_titleLabel != null) _titleLabel.text = Title;

            if (Languages != null)
            {
                Languages.Text = Text;

                // The running language, not the stored preference. They agree on every path
                // a player can take, and disagree the moment anything switches language
                // without remembering it -- which is exactly what a fixture does.
                ClientApplicationBootstrap root = ClientApplicationBootstrap.Current;

                Languages.Relabel(root != null && root.Language != null
                    ? root.Language.Current
                    : LanguagePreference.Current);
            }

            if (Session != null) Rebuild();
        }

        /// <summary>
        /// Whether this screen actually put anything on itself.
        /// </summary>
        /// <remarks>
        /// <b>Not simply the row count.</b> A screen that paints its own table never calls
        /// <c>AddRow</c>, so the plain list stays empty however many rows are on screen --
        /// and the status line underneath announced "no available servers" over a full one.
        /// A screen that draws its own rows says so by overriding this.
        /// </remarks>
        protected virtual bool HasContent => _rows.Count > 0;

        protected void SetStatus(string message)
        {
            StatusMessage = message ?? string.Empty;

            if (_status != null) _status.text = StatusMessage;
        }

        /// <summary>
        /// Turns a refused session step into something a player can read.
        /// </summary>
        /// <remarks>
        /// The reason is the domain's own typed rejection, which is exactly why this can be
        /// a lookup rather than a rule: the screen is naming an answer somebody else gave.
        /// Anything unrecognised falls through to the enum name rather than to silence -- a
        /// player told nothing assumes the game is broken, and an unnamed enum value is at
        /// least something a support ticket can quote.
        /// </remarks>
        protected string Explain(SessionRejection reason)
        {
            return reason == SessionRejection.None
                ? string.Empty
                : UiText.Of(Text, SessionRejectionKey(reason), reason.ToString());
        }

        /// <summary>Which sentence a refused session step is worded as.</summary>
        private static string SessionRejectionKey(SessionRejection reason)
        {
            switch (reason)
            {
                case SessionRejection.SessionExpired: return UiStrings.RejectSessionExpired;
                case SessionRejection.SessionRevoked: return UiStrings.RejectSessionRevoked;
                case SessionRejection.SessionInvalid: return UiStrings.RejectSessionInvalid;
                case SessionRejection.ServerFull: return UiStrings.RejectServerFull;
                case SessionRejection.ServerMaintenance: return UiStrings.RejectServerMaintenance;
                case SessionRejection.ServerUnavailable: return UiStrings.RejectServerUnavailable;
                case SessionRejection.ChannelFull: return UiStrings.RejectChannelFull;
                case SessionRejection.ChannelMaintenance: return UiStrings.RejectChannelMaintenance;
                case SessionRejection.ChannelUnavailable: return UiStrings.RejectChannelUnavailable;
                case SessionRejection.UnknownCharacter:
                case SessionRejection.CharacterNotOwned:
                    return UiStrings.RejectCharacterUnavailable;
                case SessionRejection.CharacterUnavailable:
                    return UiStrings.RejectCharacterNotPlayable;
                case SessionRejection.VersionMismatch: return UiStrings.RejectVersionMismatch;
                case SessionRejection.AlreadyInWorld: return UiStrings.RejectCharacterInWorld;

                // Not a key. An unnamed value falls back to the enum name, which is at least
                // something a support ticket can quote -- see the caller's fallback.
                default: return null;
            }
        }


        /// <summary>
        /// The same, for the login vocabulary.
        /// </summary>
        /// <remarks>
        /// A separate enum and deliberately a separate method. Signing in and choosing a
        /// server fail for different reasons, and collapsing them would mean inventing a
        /// mapping between two vocabularies that the domain keeps apart on purpose.
        ///
        /// <b>Every credential failure says the same thing.</b> "Incorrect login or
        /// password" covers a wrong password and an account that does not exist, because
        /// telling them apart tells an attacker which logins are real.
        /// </remarks>
        protected string Explain(LoginRejection reason)
        {
            return reason == LoginRejection.None
                ? string.Empty
                : UiText.Of(Text, LoginRejectionKey(reason), reason.ToString());
        }

        /// <summary>Which sentence a refused sign-in is worded as.</summary>
        private static string LoginRejectionKey(LoginRejection reason)
        {
            switch (reason)
            {
                case LoginRejection.InvalidCredentials: return UiStrings.RejectBadCredentials;
                case LoginRejection.AccountBanned: return UiStrings.RejectAccountBanned;
                case LoginRejection.AccountSuspended: return UiStrings.RejectAccountSuspended;
                case LoginRejection.AccountDisabled: return UiStrings.RejectAccountDisabled;
                case LoginRejection.Maintenance: return UiStrings.RejectMaintenance;
                case LoginRejection.ClientVersionMismatch:
                case LoginRejection.ProtocolVersionMismatch:
                    return UiStrings.RejectVersionMismatch;
                case LoginRejection.ServerUnavailable: return UiStrings.RejectUnreachable;
                default: return null;
            }
        }


        protected virtual void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Builds this screen's widgets, once.</summary>
        /// <remarks>
        /// <b>Not left to <c>Awake</c> alone.</b> Unity only sends <c>Awake</c> to a
        /// component while the player loop is running, so a screen added and bound in the
        /// same breath -- by composition code, or by a test driving the buttons directly --
        /// would otherwise be a screen with no widgets at all: every field null, every
        /// assignment silently skipped, and a form that reads back empty no matter what was
        /// typed into it. Building on first use makes the two orders identical.
        ///
        /// <b>Once.</b> A second call after <c>Awake</c> has already run would build a second
        /// canvas over the first.
        /// </remarks>
        protected void EnsureBuilt()
        {
            if (_built) return;

            _built = true;

            Build();
        }

        private void Build()
        {
            Canvas canvas = UiFactory.CreateCanvas(GetType().Name + " Canvas", gameObject);

            RectTransform root = UiFactory.CreateStretched("Root", canvas.transform);
            UiFactory.CreatePanel("Backdrop", root, UiFactory.Backdrop).rectTransform
                .SetAsFirstSibling();

            RectTransform backdrop = (RectTransform)root.GetChild(0);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            TextMeshProUGUI title = UiFactory.CreateLabel("Title", root, Title, 44f,
                TextAlignmentOptions.Center);
            _titleLabel = title;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -40f);

            Content = UiFactory.CreateScrollList("List", root, out ScrollRect _);
            RectTransform frame = (RectTransform)Content.parent.parent;

            // The list keeps the flat colour it has always had.
            //
            // The generic panel sprites in the UI pack are unusable: they were cut out of a
            // contact sheet with their own filenames printed on them, so dressing a screen in
            // Panel_Large draws the words "Panel_Large.png" across it. The login screen has
            // its own authored art and uses that instead; the rest wait for panel art that
            // is not a screenshot of a spritesheet.

            frame.anchorMin = new Vector2(0.5f, 0f);
            frame.anchorMax = new Vector2(0.5f, 1f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(720f, -280f);
            frame.anchoredPosition = new Vector2(0f, -20f);

            _status = UiFactory.CreateLabel("Status", root, string.Empty, 22f,
                TextAlignmentOptions.Center);
            _status.color = UiFactory.Muted;
            _status.rectTransform.anchorMin = new Vector2(0f, 0f);
            _status.rectTransform.anchorMax = new Vector2(1f, 0f);
            _status.rectTransform.pivot = new Vector2(0.5f, 0f);
            _status.rectTransform.sizeDelta = new Vector2(0f, 80f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 30f);

            BuildExtra(root);

            BuildLanguagePicker(root);
        }

        /// <summary>A hook for a screen that needs more than a list.</summary>
        protected virtual void BuildExtra(RectTransform root)
        {
        }

        /// <summary>The language picker, on every screen before the world.</summary>
        /// <remarks>
        /// <b>On all of them, not only sign-in.</b> A player who picked the wrong language on
        /// the first screen would otherwise have to guess their way back to it through two
        /// screens they cannot read. It is a small corner control, so the cost of repeating
        /// it is a row of two buttons and the benefit is that being lost is recoverable.
        ///
        /// <b>It does not switch anything.</b> The press is forwarded to the client root,
        /// which owns the one language service; a screen that changed language by itself
        /// would change it for itself alone.
        /// </remarks>
        private void BuildLanguagePicker(RectTransform root)
        {
            if (Languages != null) return;

            Languages = gameObject.AddComponent<LanguagePicker>();

            Languages.Text = Text;

            Languages.Compose(root, LanguagePreference.Current);

            Languages.Picked += OnLanguagePicked;
        }

        /// <summary>The picker this screen is wearing. Null before it is built.</summary>
        public LanguagePicker Languages { get; private set; }

        /// <summary>What a screen does when somebody picks a language.</summary>
        /// <remarks>Virtual so a test, or a screen composed without a client root, can take
        /// the choice somewhere else. The default asks the root, which is the only thing that
        /// can change the language for every screen at once.</remarks>
        protected virtual void OnLanguagePicked(GameLanguage language)
        {
            ClientApplicationBootstrap root = ClientApplicationBootstrap.Current;

            if (root != null) root.UseLanguage(language);
        }

        protected Button AddRow(string title, string detail, bool selectable,
            System.Action onPicked)
        {
            Button button = UiFactory.CreateRow(Content, title, detail,
                out TextMeshProUGUI _, out TextMeshProUGUI _);

            button.interactable = selectable;

            if (selectable && onPicked != null) button.onClick.AddListener(() => onPicked());

            _rows.Add(button.gameObject);

            return button;
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                UiFactory.DestroyWidget(_rows[i]);
            }

            _rows.Clear();
        }
    }
}
