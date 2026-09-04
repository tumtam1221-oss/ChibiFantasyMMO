using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the login, server, channel and character panels to the session flow.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for the whole entry flow.</b> Four screens, one class. Every
    /// change any of them can cause goes through a submit method here, and each calls
    /// <see cref="SessionFlowService"/>. No panel holds a session, a directory or an authority,
    /// so there is nowhere else a sign-in could happen. The same shape the inventory, quest,
    /// world, collectible and social controllers already keep.
    ///
    /// <b>It decides nothing.</b> Not one rule about server availability, character ownership
    /// or version acceptability appears below. Each submit forwards to the service that owns
    /// the rule and reports what came back, which is why a panel cannot let anybody in by
    /// being wrong about state.
    ///
    /// <b>It is where the two seams meet.</b> <see cref="IAccountApi"/> is the transport, in
    /// Backend; <see cref="ISessionAuthority"/> is the domain's view of the same authority, in
    /// Gameplay. Client is the one assembly that references both, so joining them here is what
    /// keeps HTTP out of the domain and the domain out of the transport.
    ///
    /// <b>No credential is held.</b> A password is collected by a panel's input field and
    /// handed to the API implementation; it is never stored on this controller, never put in
    /// view data and never passed to the domain.
    ///
    /// <b>Nothing is polled.</b> Panels rebuild when the session's revision moves.
    /// </remarks>
    public sealed class SessionUiController : MonoBehaviour
    {
        private readonly List<ServerRowViewData> _servers = new List<ServerRowViewData>();
        private readonly List<ChannelRowViewData> _channels = new List<ChannelRowViewData>();
        private readonly List<CharacterRowViewData> _characters = new List<CharacterRowViewData>();

        private readonly List<ServerInfo> _serverInfo = new List<ServerInfo>();
        private readonly List<ChannelInfo> _channelInfo = new List<ChannelInfo>();
        private readonly List<CharacterSelectEntry> _characterInfo =
            new List<CharacterSelectEntry>();

        private IAccountApi _api;
        private ISessionAuthority _authority;
        private ISessionCatalogueSink _catalogue;
        private SessionDirectory _directory;
        private SessionConfiguration _configuration;

        private AccountSessionState _session;
        private AuthenticatedAccount _account;
        private VersionSet _versions;
        private VersionRequirement _required;

        private bool _bound;
        private Revision _lastSessionRevision;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Caller-supplied time. Nothing here reads a clock.</summary>
        public long TimestampTicks { get; set; }

        /// <summary>What the last login attempt produced.</summary>
        public LoginResult LastLoginResult { get; private set; }

        /// <summary>What the last selection produced.</summary>
        public SessionResult LastSessionResult { get; private set; }

        /// <summary>What the last enter-world attempt produced.</summary>
        public EnterWorldResult LastEnterWorldResult { get; private set; }

        /// <summary>The last transport failure, when a call did not reach the authority.</summary>
        public ApiError LastApiError { get; private set; }

        public LoginViewData Login { get; private set; }

        public SessionFlowViewData Flow { get; private set; }

        public IReadOnlyList<ServerRowViewData> Servers => _servers;

        public IReadOnlyList<ChannelRowViewData> Channels => _channels;

        public IReadOnlyList<CharacterRowViewData> Characters => _characters;

        /// <summary>
        /// Raised when the authority has authorised entry to the world.
        /// </summary>
        /// <remarks>Carries identifiers only. Whoever listens resolves the map to a scene
        /// through the Phase 11 loader and, from Phase 16, establishes the connection. Nothing
        /// here loads or connects anything.</remarks>
        public event System.Action<EnterWorldResult> WorldEntryAuthorised;

        /// <summary>Points the controller at the transport and the domain.</summary>
        public void Bind(IAccountApi api, ISessionAuthority authority, SessionDirectory directory,
            VersionSet versions, VersionRequirement required,
            SessionConfiguration configuration = null)
        {
            _api = api;
            _authority = authority;

            // An authority with its own source of truth -- a server-side one, a fixture --
            // does not implement this and is simply never told anything.
            _catalogue = authority as ISessionCatalogueSink;

            _directory = directory;
            _versions = versions;
            _required = required;
            _configuration = configuration;

            _bound = true;
            Refresh();
        }

        private SessionFlowService.Context FlowContext =>
            new SessionFlowService.Context(_authority, _directory, _configuration,
                TimestampTicks);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            Login = SessionAdapter.BuildLogin(StatusOf(LastLoginResult.IsAccepted,
                LastLoginResult.Reason != LoginRejection.None), LastLoginResult,
                _account.DisplayName);

            Flow = SessionAdapter.BuildFlow(_session, StatusOf(LastSessionResult.IsAccepted,
                LastSessionResult.Reason != SessionRejection.None), LastSessionResult.Reason,
                _characterInfo.Count, _configuration);

            SessionAdapter.BuildServers(_serverInfo, _session, FlowContext, _servers);
            SessionAdapter.BuildChannels(_channelInfo, _session, FlowContext, _channels);
            SessionAdapter.BuildCharacters(_characterInfo, _session, _characters);

            if (_session != null) _lastSessionRevision = _session.Revision;
        }

        /// <summary>
        /// Redraws only if the session actually changed.
        /// </summary>
        /// <remarks>A revision comparison rather than a per-frame rebuild, matching every other
        /// controller in this assembly.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound || _session == null) return false;
            if (_session.Revision == _lastSessionRevision) return false;

            Refresh();
            return true;
        }

        private static PanelStatus StatusOf(bool accepted, bool attempted)
        {
            if (accepted) return PanelStatus.Success;
            return attempted ? PanelStatus.Error : PanelStatus.Idle;
        }

        // ---- commands ------------------------------------------------------------------

        /// <summary>
        /// Signs in.
        /// </summary>
        /// <param name="request">
        /// The attempt. It carries no credential: the API implementation collected and verified
        /// one on its own side before this call, which is why <see cref="LoginRequest"/> has
        /// nowhere to put a password.
        /// </param>
        public LoginResult SubmitLogin(LoginRequest request)
        {
            if (!_bound) return LoginResult.Rejected(LoginRejection.MissingContext);

            LastApiError = ApiError.None;

            ApiResult<AuthenticatedAccount> authenticated = _api.Authenticate(request);

            if (!authenticated.IsOk)
            {
                // A transport failure is not a domain answer: the account is not banned, the
                // authority simply did not reply.
                LastApiError = authenticated.Error;
                LastLoginResult = LoginResult.Rejected(LoginRejection.ServerUnavailable);
                Refresh();
                return LastLoginResult;
            }

            _account = authenticated.Value;

            // Before the domain is asked anything: the status it is about to check is the
            // one the authority just reported, not one this controller decided.
            _catalogue?.ObserveAccount(_account);

            LastLoginResult = SessionFlowService.TryLogin(_account, request, _required,
                FlowContext);

            if (LastLoginResult.IsAccepted)
            {
                AccountSessionState issued;
                if (_directory.TryGet(LastLoginResult.Session, out issued)) _session = issued;

                FetchServers();
            }

            Refresh();
            return LastLoginResult;
        }

        /// <summary>Fetches the server list from the authority.</summary>
        /// <remarks>Read-only: it advances no session revision, so refreshing a list is not a
        /// mutation.</remarks>
        public bool FetchServers()
        {
            if (!_bound || _session == null) return false;

            ApiResult<IReadOnlyList<ServerInfo>> servers = _api.GetServers(_session.Account);

            _serverInfo.Clear();

            if (!servers.IsOk)
            {
                LastApiError = servers.Error;
                Refresh();
                return false;
            }

            if (servers.Value != null) _serverInfo.AddRange(servers.Value);

            _catalogue?.ObserveServers(_serverInfo);

            Refresh();
            return true;
        }

        /// <summary>Chooses a server, then fetches its channels.</summary>
        public SessionResult SubmitSelectServer(ServerId server, RequestId request)
        {
            if (!_bound || _session == null)
                return SessionResult.Rejected(SessionRejection.MissingContext);

            var command = new SessionCommand(request, _session.SessionId, _session.Account);

            LastSessionResult = SessionFlowService.TrySelectServer(command, server, FlowContext);

            if (LastSessionResult.IsAccepted) FetchChannels();

            Refresh();
            return LastSessionResult;
        }

        /// <summary>Fetches the channel list for the chosen server.</summary>
        public bool FetchChannels()
        {
            if (!_bound || _session == null || !_session.Server.IsValid) return false;

            ApiResult<IReadOnlyList<ChannelInfo>> channels = _api.GetChannels(_session.Account,
                _session.Server);

            _channelInfo.Clear();

            if (!channels.IsOk)
            {
                LastApiError = channels.Error;
                Refresh();
                return false;
            }

            if (channels.Value != null) _channelInfo.AddRange(channels.Value);

            _catalogue?.ObserveChannels(_channelInfo);

            Refresh();
            return true;
        }

        /// <summary>Chooses a channel, then fetches this account's characters.</summary>
        public SessionResult SubmitSelectChannel(ChannelId channel, RequestId request)
        {
            if (!_bound || _session == null)
                return SessionResult.Rejected(SessionRejection.MissingContext);

            var command = new SessionCommand(request, _session.SessionId, _session.Account);

            LastSessionResult = SessionFlowService.TrySelectChannel(command, channel, FlowContext);

            if (LastSessionResult.IsAccepted) FetchCharacters();

            Refresh();
            return LastSessionResult;
        }

        /// <summary>
        /// Fetches this account's characters on the chosen server.
        /// </summary>
        /// <remarks>Scoped by account at the authority, so another account's characters are
        /// never returned rather than returned and filtered here.</remarks>
        public bool FetchCharacters()
        {
            if (!_bound || _session == null || !_session.Server.IsValid) return false;

            ApiResult<IReadOnlyList<CharacterSelectEntry>> characters =
                _api.GetCharacters(_session.Account, _session.Server);

            _characterInfo.Clear();

            if (!characters.IsOk)
            {
                LastApiError = characters.Error;
                Refresh();
                return false;
            }

            if (characters.Value != null) _characterInfo.AddRange(characters.Value);

            _catalogue?.ObserveCharacters(_characterInfo);

            Refresh();
            return true;
        }

        /// <summary>Chooses a character.</summary>
        public SessionResult SubmitSelectCharacter(CharacterId character, RequestId request)
        {
            if (!_bound || _session == null)
                return SessionResult.Rejected(SessionRejection.MissingContext);

            var command = new SessionCommand(request, _session.SessionId, _session.Account);

            LastSessionResult = SessionFlowService.TrySelectCharacter(command, character,
                FlowContext);

            Refresh();
            return LastSessionResult;
        }

        /// <summary>
        /// Asks the authority to hand the session to the world.
        /// </summary>
        /// <remarks>
        /// The request restates every identity from the <em>session</em>, not from anything a
        /// panel is holding, so a screen cannot enter the world as somebody else by having
        /// stale state. The authority compares each field against the session anyway.
        ///
        /// On success the authority is told, and the result is raised for whoever loads the
        /// world. Nothing here connects: that is Phase 16's.
        /// </remarks>
        public EnterWorldResult SubmitEnterWorld(RequestId request)
        {
            if (!_bound || _session == null)
                return EnterWorldResult.Rejected(SessionRejection.MissingContext);

            var enter = new EnterWorldRequest(request, _session.SessionId, _session.Account,
                _session.Character, _session.Server, _session.Channel, _versions);

            LastEnterWorldResult = SessionFlowService.TryEnterWorld(enter, _required, FlowContext);

            if (!LastEnterWorldResult.IsAccepted)
            {
                Refresh();
                return LastEnterWorldResult;
            }

            ApiResult<bool> noted = _api.NotifyWorldEntry(_session.Account, _session.SessionId,
                _session.Character, _session.Server, _session.Channel);

            if (!noted.IsOk) LastApiError = noted.Error;

            Refresh();

            var handler = WorldEntryAuthorised;
            if (handler != null) handler(LastEnterWorldResult);

            return LastEnterWorldResult;
        }

        // ---- resolvers for Phase 13 panels ---------------------------------------------

        /// <summary>
        /// The name resolver Phase 13's party and guild adapters take.
        /// </summary>
        /// <remarks>Answers only for characters this account listed. It is not a directory of
        /// everybody's names, and building one on a client would leak other accounts' data.</remarks>
        public string ResolveName(CharacterId character)
        {
            return SessionAdapter.NameOf(_characterInfo, character);
        }

        /// <summary>Whether a character is known to be in the world. Usually unknown.</summary>
        public PopulationReading ResolvePresence(CharacterId character)
        {
            return SessionAdapter.PresenceOf(_characterInfo, character);
        }
    }
}
