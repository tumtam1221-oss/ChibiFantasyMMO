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
        private TMP_InputField _account;
        private TMP_InputField _password;
        private Button _submit;
        private TextMeshProUGUI _submitLabel;

        protected override string Title => "Chibi Fantasy";

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
                SetStatus("Enter your login and password");

                return;
            }

            IsBusy = true;
            SetSubmitEnabled(false);
            SetStatus("Connecting...");

            Credentials?.Invoke(account, password);

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

            RectTransform form = UiFactory.CreateAnchored("Form", root,
                new Vector2(0.5f, 0.5f), new Vector2(520f, 280f));

            UiFactory.CreatePanel("Frame", form, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)form.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _account = UiFactory.CreateField("Account", form, "Login");
            Place(_account.GetComponent<RectTransform>(), 200f);

            _password = UiFactory.CreateField("Password", form, "Password", password: true);
            Place(_password.GetComponent<RectTransform>(), 130f);

            _submit = UiFactory.CreateButton("Submit", form, "Sign in", out _submitLabel);
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
                _submitLabel.text = enabled ? "Sign in" : "Signing in...";
            }
        }
    }

    /// <summary>The server list, as the account authority reported it.</summary>
    public sealed class ServerSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a server";

        protected override string EmptyMessage => "No available servers";

        /// <summary>Raised when the flow service accepted a server.</summary>
        public event System.Action Selected;

        protected override void Fetch()
        {
            Session.FetchServers();
        }

        protected override void BuildRows()
        {
            IReadOnlyList<ServerRowViewData> servers = Session.Servers;

            for (int i = 0; i < servers.Count; i++)
            {
                ServerRowViewData row = servers[i];

                AddRow(row.NameKey.Key, Describe(row), row.IsSelectable,
                    () => Pick(row.Server));
            }
        }

        /// <summary>
        /// The detail line under a server's name.
        /// </summary>
        /// <remarks>Only values the view data actually carries. A population it does not
        /// know is left out rather than shown as zero, because zero players and unknown
        /// players are different things and one of them is a lie.</remarks>
        private static string Describe(in ServerRowViewData row)
        {
            string state = row.IsSelectable ? "Online" : row.Status.ToString();

            return row.PopulationKnown
                ? state + "  ~  " + row.Population + " online"
                : state;
        }

        private void Pick(ServerId server)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult result = Session.SubmitSelectServer(server, RequestId.New());

            IsBusy = false;

            if (result.IsAccepted)
            {
                Selected?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }
    }

    /// <summary>The channels of the chosen server.</summary>
    public sealed class ChannelSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a channel";

        protected override string EmptyMessage => "No available channels";

        public event System.Action Selected;

        protected override void Fetch()
        {
            Session.FetchChannels();
        }

        protected override void BuildRows()
        {
            IReadOnlyList<ChannelRowViewData> channels = Session.Channels;

            for (int i = 0; i < channels.Count; i++)
            {
                ChannelRowViewData row = channels[i];

                AddRow(row.NameKey.Key, Describe(row), row.IsSelectable,
                    () => Pick(row.Channel));
            }
        }

        /// <summary>PK is shown because the view data carries it. It is never set here.</summary>
        private static string Describe(in ChannelRowViewData row)
        {
            string state = row.IsSelectable ? "Open" : row.Status.ToString();

            if (row.PkEnabled) state += "  ~  PK";

            return row.PopulationKnown ? state + "  ~  " + row.Population + " online" : state;
        }

        private void Pick(ChannelId channel)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult result = Session.SubmitSelectChannel(channel, RequestId.New());

            IsBusy = false;

            if (result.IsAccepted)
            {
                Selected?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }
    }

    /// <summary>
    /// The characters on this account, and the way into the world.
    /// </summary>
    /// <remarks>
    /// <b>Scoped by the server, not by this screen.</b> The list comes from an endpoint that
    /// filters by the authenticated account in SQL; there is no filtering here to get wrong,
    /// and nothing this screen could ask that would return somebody else's characters.
    ///
    /// <b>No creation.</b> Character creation exists as a domain service but has no
    /// production screen, so the button is absent rather than present and broken. Reported
    /// as a limitation.
    /// </remarks>
    public sealed class CharacterSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a character";

        protected override string EmptyMessage => "No characters on this account";

        /// <summary>Raised when the server authorised world entry.</summary>
        public event System.Action<EnterWorldResult> WorldAuthorised;

        protected override void Fetch()
        {
            Session.FetchCharacters();
        }

        protected override void BuildRows()
        {
            IReadOnlyList<CharacterRowViewData> characters = Session.Characters;

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterRowViewData row = characters[i];

                AddRow(row.Name, Describe(row), row.IsSelectable, () => Pick(row.Character));
            }
        }

        private static string Describe(in CharacterRowViewData row)
        {
            return "Level " + row.Level;
        }

        /// <summary>
        /// Chooses a character and asks to enter the world.
        /// </summary>
        /// <remarks>Two steps because the server treats them as two: selecting is a session
        /// transition that can be refused on its own, and entering revalidates the server,
        /// the channel and the client version. A screen that jumped straight to the world
        /// scene would be skipping the half that admits the player.</remarks>
        private void Pick(CharacterId character)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult selected = Session.SubmitSelectCharacter(character,
                RequestId.New());

            if (!selected.IsAccepted)
            {
                IsBusy = false;
                SetStatus(Explain(selected.Reason));

                return;
            }

            SetStatus("Entering world...");

            EnterWorldResult entry = Session.SubmitEnterWorld(RequestId.New());

            IsBusy = false;

            if (entry.IsAccepted)
            {
                WorldAuthorised?.Invoke(entry);

                return;
            }

            SetStatus(Explain(entry.Reason));
        }
    }
}
