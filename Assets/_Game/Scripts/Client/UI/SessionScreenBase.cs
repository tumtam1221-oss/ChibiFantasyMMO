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
        private readonly List<GameObject> _rows = new List<GameObject>();
        private bool _built;

        /// <summary>The controller this screen shows. Bound by the flow driver.</summary>
        public SessionUiController Session { get; private set; }

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

            IsBusy = true;
            SetStatus("Loading...");

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

            if (_rows.Count == 0) SetStatus(EmptyMessage);
            else SetStatus(string.Empty);
        }

        /// <summary>What to say when the list is empty. Overridden per screen.</summary>
        protected virtual string EmptyMessage => "Nothing to show";

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
        protected static string Explain(SessionRejection reason)
        {
            switch (reason)
            {
                case SessionRejection.None: return string.Empty;
                case SessionRejection.SessionExpired:
                    return "Your session expired -- sign in again";
                case SessionRejection.SessionRevoked: return "Your session was ended";
                case SessionRejection.SessionInvalid: return "Your session is no longer valid";
                case SessionRejection.ServerFull: return "That server is full";
                case SessionRejection.ServerMaintenance:
                    return "That server is under maintenance";
                case SessionRejection.ServerUnavailable: return "That server is unavailable";
                case SessionRejection.ChannelFull: return "That channel is full";
                case SessionRejection.ChannelMaintenance:
                    return "That channel is under maintenance";
                case SessionRejection.ChannelUnavailable: return "That channel is unavailable";
                case SessionRejection.UnknownCharacter:
                case SessionRejection.CharacterNotOwned:
                    return "That character is unavailable";
                case SessionRejection.CharacterUnavailable:
                    return "That character cannot be played right now";
                case SessionRejection.VersionMismatch: return "Your client needs updating";
                case SessionRejection.AlreadyInWorld:
                    return "That character is already in the world";
                default: return reason.ToString();
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
        protected static string Explain(LoginRejection reason)
        {
            switch (reason)
            {
                case LoginRejection.None: return string.Empty;
                case LoginRejection.InvalidCredentials:
                    return "Incorrect login or password";
                case LoginRejection.AccountBanned: return "This account is banned";
                case LoginRejection.AccountSuspended: return "This account is suspended";
                case LoginRejection.AccountDisabled: return "This account is disabled";
                case LoginRejection.Maintenance: return "The service is under maintenance";
                case LoginRejection.ClientVersionMismatch:
                case LoginRejection.ProtocolVersionMismatch:
                    return "Your client needs updating";
                case LoginRejection.ServerUnavailable:
                    return "Could not reach the server -- try again";
                default: return reason.ToString();
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
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -40f);

            Content = UiFactory.CreateScrollList("List", root, out ScrollRect _);
            RectTransform frame = (RectTransform)Content.parent.parent;
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
        }

        /// <summary>A hook for a screen that needs more than a list.</summary>
        protected virtual void BuildExtra(RectTransform root)
        {
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
