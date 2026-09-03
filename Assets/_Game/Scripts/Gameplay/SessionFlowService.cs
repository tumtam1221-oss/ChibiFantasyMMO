using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Supplies whatever the authority knows about servers, channels and characters.
    /// </summary>
    /// <remarks>
    /// <b>An interface because the answers are not local.</b> Which servers exist, whether a
    /// channel is in maintenance and who owns a character are the account system's facts, and
    /// a client that decided any of them could let itself in. The domain asks; it never
    /// derives.
    ///
    /// Kept separate from <c>IAccountApi</c> on purpose: that one is the transport seam and
    /// lives in Backend, which this assembly must not reference. This is the domain's own
    /// narrow view of the same authority, so the flow service stays engine-free and
    /// transport-free, and an adapter above wires the two together.
    /// </remarks>
    public interface ISessionAuthority
    {
        /// <summary>Whether the account may hold a session at all.</summary>
        AccountStatus StatusOf(AccountId account);

        /// <summary>The server, as the authority currently sees it.</summary>
        bool TryGetServer(ServerId server, out ServerInfo info);

        /// <summary>The channel, as the authority currently sees it.</summary>
        bool TryGetChannel(ChannelId channel, out ChannelInfo info);

        /// <summary>The character row, as the authority currently sees it.</summary>
        bool TryGetCharacter(CharacterId character, out CharacterSelectEntry entry);

        /// <summary>
        /// Whether an account owns a character, asked at the moment it matters.
        /// </summary>
        /// <remarks>Asked rather than inferred from a list the client was sent earlier. A list
        /// is a snapshot; ownership has to be re-established when it is acted on.</remarks>
        bool OwnsCharacter(AccountId account, CharacterId character);

        /// <summary>Whether the service as a whole is closed to players.</summary>
        bool IsUnderMaintenance();
    }

    /// <summary>
    /// The whole login-to-world flow. One command boundary.
    /// </summary>
    /// <remarks>
    /// <b>Every screen goes through here.</b> A login panel, a server list and a character
    /// grid all call a method on this service and are handed a typed result; none of them
    /// touches an <see cref="AccountSessionState"/>. That is what keeps the flow in one place
    /// instead of spread across four screens that each half-remember it.
    ///
    /// <b>The client is quoted, never believed.</b> Every command restates who the caller
    /// thinks it is; the service looks the session up and compares. Editing an account,
    /// character, server or channel in a request produces a request that disagrees with the
    /// session, which is refused -- it does not produce a different outcome.
    ///
    /// <b>Validate fully, then mutate.</b> A refused command leaves the session
    /// byte-identical, so its revision is unchanged and nothing downstream sees a change that
    /// did not happen.
    ///
    /// <b>Nothing here reads a clock, and nothing here knows a transport.</b> Time arrives as
    /// an argument; the authority arrives as an interface. There is no HTTP, no SQL, no PHP
    /// and no <c>UnityEngine</c> in this file, and no password ever reaches it.
    ///
    /// <b>It authorises entry; it does not connect.</b> <see cref="TryEnterWorld"/> ends at
    /// <see cref="SessionState.EnteringWorld"/> with a result naming everything the world
    /// needs. Establishing the connection is Phase 16's, and pretending to do it here would be
    /// the fake handoff the brief forbids.
    /// </remarks>
    public static class SessionFlowService
    {
        /// <summary>Everything a session command needs.</summary>
        public readonly struct Context
        {
            public Context(ISessionAuthority authority, SessionDirectory directory,
                SessionConfiguration configuration = null, long timestampTicks = 0L)
            {
                Authority = authority;
                Directory = directory;
                Limits = SessionConfiguration.Resolve(configuration);
                TimestampTicks = timestampTicks;
            }

            public ISessionAuthority Authority { get; }

            /// <summary>Where sessions live and where retries are recognised.</summary>
            public SessionDirectory Directory { get; }

            public SessionConfiguration.Limits Limits { get; }

            /// <summary>
            /// When the caller says this is happening.
            /// </summary>
            /// <remarks>Supplied, never read from a clock: this assembly is engine-free and an
            /// ambient clock would make expiry irreproducible in a test.</remarks>
            public long TimestampTicks { get; }

            public bool IsUsable => Authority != null && Directory != null;
        }

        // ---- login ---------------------------------------------------------------------

        /// <summary>
        /// Turns an authenticated account into a session.
        /// </summary>
        /// <param name="account">
        /// What the backend concluded. The credential was verified before this call and is not
        /// passed in -- see <see cref="AuthenticatedAccount"/> for why the type has no room
        /// for one.
        /// </param>
        /// <param name="request">The attempt, carrying the client's reported versions.</param>
        /// <param name="required">What the authority demands of a client to sign in.</param>
        /// <param name="context">Authority, directory, limits and time.</param>
        /// <remarks>
        /// Order matters: maintenance and version are checked before the account, so a closed
        /// service does not spend effort on accounts, and a stale build is told to patch rather
        /// than told its credentials are wrong.
        /// </remarks>
        public static LoginResult TryLogin(AuthenticatedAccount account, LoginRequest request,
            VersionRequirement required, in Context context)
        {
            if (!context.IsUsable || !request.IsValid)
                return LoginResult.Rejected(LoginRejection.MissingContext);

            LoginResult replay;
            if (context.Directory.TryReplayLogin(request.Request, out replay))
            {
                return LoginResult.Accepted(replay.Session, replay.Account, replay.Compatibility,
                    true);
            }

            if (!account.IsValid)
                return LoginResult.Rejected(LoginRejection.InvalidCredentials);

            if (!context.Directory.TryRecordAttempt(account.Account,
                SessionDirectory.AttemptKind.Login, context.TimestampTicks,
                context.Limits.MaxLoginAttempts, context.Limits.LoginAttemptWindowSeconds))
            {
                return LoginResult.Rejected(LoginRejection.RateLimited);
            }

            if (context.Authority.IsUnderMaintenance())
                return LoginResult.Rejected(LoginRejection.Maintenance);

            VersionCompatibilityResult compatibility = VersionPolicy.Evaluate(request.Versions,
                required);

            if (!compatibility.IsPlayable)
            {
                return LoginResult.Rejected(
                    compatibility.Kind == VersionKind.Protocol
                        ? LoginRejection.ProtocolVersionMismatch
                        : LoginRejection.ClientVersionMismatch,
                    compatibility);
            }

            // The authority's word on the account, not the caller's.
            AccountStatus status = context.Authority.StatusOf(account.Account);

            LoginRejection blocked = Translate(status);
            if (blocked != LoginRejection.None) return LoginResult.Rejected(blocked, compatibility);

            if (!context.Limits.AllowConcurrentSessions
                && context.Directory.HasLiveSession(account.Account, context.TimestampTicks))
            {
                return LoginResult.Rejected(LoginRejection.SessionAlreadyActive, compatibility);
            }

            // ---- everything is resolved and nothing below can fail ---------------------

            long expires = context.Limits.SessionsExpire
                ? context.TimestampTicks + context.Limits.SessionLifetimeSeconds
                : 0L;

            var session = new AccountSessionState(SessionId.New(), account.Account,
                request.Versions, context.TimestampTicks, expires);

            context.Directory.Register(session);

            LoginResult result = LoginResult.Accepted(session.SessionId, account.Account,
                compatibility);

            context.Directory.RememberLogin(request.Request, result);

            return result;
        }

        // ---- selection -----------------------------------------------------------------

        /// <summary>Chooses a server.</summary>
        /// <remarks>The server's status, capacity and version floor are read from the
        /// authority every time, so a server that closed between the list arriving and the
        /// player clicking is refused.</remarks>
        public static SessionResult TrySelectServer(SessionCommand command, ServerId server,
            in Context context)
        {
            SessionResult replay;
            if (TryReplaySession(command, context, out replay)) return replay;

            AccountSessionState session;
            SessionRejection refusal = Resolve(command, context, out session);
            if (refusal != SessionRejection.None) return Reject(refusal, command);

            if (!AccountSessionState.CanTransitionTo(session.State, SessionState.ServerSelected))
                return Reject(SessionRejection.InvalidTransition, command, session);

            ServerInfo info;
            if (!server.IsValid || !context.Authority.TryGetServer(server, out info)
                || !info.IsValid)
            {
                return Reject(SessionRejection.UnknownServer, command, session);
            }

            SessionRejection availability = CheckServer(info, session, context);
            if (availability != SessionRejection.None)
                return Reject(availability, command, session);

            // ---- everything is resolved and nothing below can fail ---------------------

            session.TrySelectServer(server);

            return Accept(command, session, context);
        }

        /// <summary>Chooses a channel of the selected server.</summary>
        /// <remarks>The channel is required to name the selected server. That check is why a
        /// bare channel number is never an identity: without it, selecting server A and
        /// channel 1 of server B would be indistinguishable from a legitimate choice.</remarks>
        public static SessionResult TrySelectChannel(SessionCommand command, ChannelId channel,
            in Context context)
        {
            SessionResult replay;
            if (TryReplaySession(command, context, out replay)) return replay;

            AccountSessionState session;
            SessionRejection refusal = Resolve(command, context, out session);
            if (refusal != SessionRejection.None) return Reject(refusal, command);

            if (!AccountSessionState.CanTransitionTo(session.State, SessionState.ChannelSelected))
                return Reject(SessionRejection.InvalidTransition, command, session);

            ChannelInfo info;
            if (!channel.IsValid || !context.Authority.TryGetChannel(channel, out info)
                || !info.IsValid)
            {
                return Reject(SessionRejection.UnknownChannel, command, session);
            }

            if (info.Server != session.Server)
                return Reject(SessionRejection.ChannelServerMismatch, command, session);

            SessionRejection availability = CheckChannel(info);
            if (availability != SessionRejection.None)
                return Reject(availability, command, session);

            // ---- everything is resolved and nothing below can fail ---------------------

            session.TrySelectChannel(channel);

            return Accept(command, session, context);
        }

        /// <summary>
        /// Chooses a character.
        /// </summary>
        /// <remarks>
        /// Ownership is asked of the authority, not read from a list the client was given. A
        /// character belonging to another account is refused with
        /// <see cref="SessionRejection.CharacterNotOwned"/> -- the same answer as one that does
        /// not exist would give if it were also unowned, so the refusal does not confirm that
        /// somebody else's character is real.
        /// </remarks>
        public static SessionResult TrySelectCharacter(SessionCommand command,
            CharacterId character, in Context context)
        {
            SessionResult replay;
            if (TryReplaySession(command, context, out replay)) return replay;

            AccountSessionState session;
            SessionRejection refusal = Resolve(command, context, out session);
            if (refusal != SessionRejection.None) return Reject(refusal, command);

            if (!AccountSessionState.CanTransitionTo(session.State,
                SessionState.CharacterSelected))
            {
                return Reject(SessionRejection.InvalidTransition, command, session);
            }

            if (!character.IsValid)
                return Reject(SessionRejection.UnknownCharacter, command, session);

            // Ownership before existence: a caller must not learn that another account's
            // character exists by getting a different refusal for it.
            if (!context.Authority.OwnsCharacter(session.Account, character))
                return Reject(SessionRejection.CharacterNotOwned, command, session);

            CharacterSelectEntry entry;
            if (!context.Authority.TryGetCharacter(character, out entry) || !entry.IsValid)
                return Reject(SessionRejection.UnknownCharacter, command, session);

            if (!entry.IsPlayable)
                return Reject(SessionRejection.CharacterUnavailable, command, session);

            // ---- everything is resolved and nothing below can fail ---------------------

            session.TrySelectCharacter(character);

            return Accept(command, session, context);
        }

        // ---- enter world ---------------------------------------------------------------

        /// <summary>
        /// Authorises the handoff to the game world.
        /// </summary>
        /// <remarks>
        /// <b>Everything is re-checked, and everything the client claimed is compared.</b> The
        /// account, character, server and channel in the request must each match what the
        /// session records; a mismatch on any of them is a refusal naming that field. A client
        /// that edits one has not changed where it is going -- it has produced a request that
        /// no longer describes its session.
        ///
        /// Nothing connects. On success the session reaches
        /// <see cref="SessionState.EnteringWorld"/> and the result names the map from the
        /// character's own Phase 11 location; whoever actually connects moves it to
        /// <see cref="SessionState.Active"/>.
        /// </remarks>
        public static EnterWorldResult TryEnterWorld(EnterWorldRequest request,
            VersionRequirement required, in Context context)
        {
            if (!context.IsUsable || !request.IsValid)
                return EnterWorldResult.Rejected(SessionRejection.MissingContext, request.Session);

            EnterWorldResult replay;
            if (context.Directory.TryReplayEntry(request.Request, out replay))
            {
                return EnterWorldResult.Accepted(replay.Session, replay.Character, replay.Server,
                    replay.Channel, replay.Map, replay.CharacterRevision, replay.SessionRevision,
                    true);
            }

            AccountSessionState session;
            if (!context.Directory.TryGet(request.Session, out session) || session == null)
                return EnterWorldResult.Rejected(SessionRejection.SessionInvalid, request.Session);

            if (session.Account != request.Account)
                return EnterWorldResult.Rejected(SessionRejection.SessionInvalid, request.Session);

            if (session.State == SessionState.Revoked)
                return EnterWorldResult.Rejected(SessionRejection.SessionRevoked, request.Session);

            if (session.State == SessionState.Expired || session.HasExpired(context.TimestampTicks))
                return EnterWorldResult.Rejected(SessionRejection.SessionExpired, request.Session);

            if (session.IsInWorld)
                return EnterWorldResult.Rejected(SessionRejection.AlreadyInWorld, request.Session);

            if (!context.Directory.TryRecordAttempt(session.Account,
                SessionDirectory.AttemptKind.EnterWorld, context.TimestampTicks,
                context.Limits.MaxEnterWorldAttempts, context.Limits.LoginAttemptWindowSeconds))
            {
                return EnterWorldResult.Rejected(SessionRejection.RateLimited, request.Session);
            }

            if (!AccountSessionState.CanTransitionTo(session.State, SessionState.EnteringWorld))
                return EnterWorldResult.Rejected(SessionRejection.InvalidTransition,
                    request.Session);

            // Every claim in the request, against what the session actually holds.
            if (session.Server != request.Server)
                return EnterWorldResult.Rejected(SessionRejection.UnknownServer, request.Session);

            if (session.Channel != request.Channel)
                return EnterWorldResult.Rejected(SessionRejection.ChannelServerMismatch,
                    request.Session);

            if (session.Character != request.Character)
                return EnterWorldResult.Rejected(SessionRejection.CharacterNotOwned,
                    request.Session);

            if (context.Authority.IsUnderMaintenance())
                return EnterWorldResult.Rejected(SessionRejection.ServerMaintenance,
                    request.Session);

            if (!VersionPolicy.IsPlayable(request.Versions, required))
                return EnterWorldResult.Rejected(SessionRejection.VersionMismatch,
                    request.Session);

            // Both places are re-read: either may have closed while the player was choosing.
            ServerInfo server;
            if (!context.Authority.TryGetServer(session.Server, out server) || !server.IsValid)
                return EnterWorldResult.Rejected(SessionRejection.UnknownServer, request.Session);

            SessionRejection serverState = CheckServer(server, session, context);
            if (serverState != SessionRejection.None)
                return EnterWorldResult.Rejected(serverState, request.Session);

            ChannelInfo channel;
            if (!context.Authority.TryGetChannel(session.Channel, out channel) || !channel.IsValid)
                return EnterWorldResult.Rejected(SessionRejection.UnknownChannel, request.Session);

            if (channel.Server != session.Server)
                return EnterWorldResult.Rejected(SessionRejection.ChannelServerMismatch,
                    request.Session);

            SessionRejection channelState = CheckChannel(channel);
            if (channelState != SessionRejection.None)
                return EnterWorldResult.Rejected(channelState, request.Session);

            // Ownership again, at the last moment it can be checked.
            if (!context.Authority.OwnsCharacter(session.Account, session.Character))
                return EnterWorldResult.Rejected(SessionRejection.CharacterNotOwned,
                    request.Session);

            CharacterSelectEntry entry;
            if (!context.Authority.TryGetCharacter(session.Character, out entry) || !entry.IsValid)
                return EnterWorldResult.Rejected(SessionRejection.UnknownCharacter,
                    request.Session);

            if (!entry.IsPlayable)
                return EnterWorldResult.Rejected(SessionRejection.CharacterUnavailable,
                    request.Session);

            // ---- everything is proven and nothing below can fail -----------------------

            session.TryBeginWorldEntry();

            EnterWorldResult result = EnterWorldResult.Accepted(session.SessionId,
                session.Character, session.Server, session.Channel, entry.Map, entry.Revision,
                session.Revision);

            context.Directory.RememberEntry(request.Request, result);

            return result;
        }

        // ---- queries -------------------------------------------------------------------

        /// <summary>
        /// Filters a server list down to what this session could actually select.
        /// </summary>
        /// <remarks>Read-only: it changes no session and advances no revision. A hint for a
        /// list screen, and the selection service asks the same questions again.</remarks>
        public static void SelectableServers(IReadOnlyList<ServerInfo> servers,
            AccountSessionState session, in Context context, List<ServerInfo> into)
        {
            if (into == null) return;

            into.Clear();
            if (servers == null) return;

            for (int i = 0; i < servers.Count; i++)
            {
                ServerInfo info = servers[i];
                if (!info.IsValid) continue;

                // Hidden servers are absent from a list rather than shown and refused.
                if (info.Status == ServerStatus.Hidden) continue;

                into.Add(info);
            }
        }

        /// <summary>Whether a session could select a server right now, without changing it.</summary>
        public static SessionRejection CanSelectServer(AccountSessionState session,
            in ServerInfo info, in Context context)
        {
            if (session == null || !context.IsUsable) return SessionRejection.MissingContext;
            if (!session.IsUsable(context.TimestampTicks)) return SessionRejection.SessionInvalid;

            return CheckServer(info, session, context);
        }

        /// <summary>Whether a session could select a channel right now, without changing it.</summary>
        public static SessionRejection CanSelectChannel(AccountSessionState session,
            in ChannelInfo info, in Context context)
        {
            if (session == null || !context.IsUsable) return SessionRejection.MissingContext;
            if (!session.IsUsable(context.TimestampTicks)) return SessionRejection.SessionInvalid;
            if (info.Server != session.Server) return SessionRejection.ChannelServerMismatch;

            return CheckChannel(info);
        }

        // ---- shared checks -------------------------------------------------------------

        private static SessionRejection CheckServer(in ServerInfo info,
            AccountSessionState session, in Context context)
        {
            if (!info.Enabled) return SessionRejection.ServerUnavailable;

            switch (info.Status)
            {
                case ServerStatus.Maintenance: return SessionRejection.ServerMaintenance;
                case ServerStatus.Offline:
                case ServerStatus.Hidden:
                case ServerStatus.Unknown: return SessionRejection.ServerUnavailable;
            }

            // Capacity is only a barrier when the authority actually reported a figure.
            if (info.Population.IsFull) return SessionRejection.ServerFull;

            if (!VersionPolicy.IsPlayable(session.Versions, info.Versions))
                return SessionRejection.VersionMismatch;

            return SessionRejection.None;
        }

        private static SessionRejection CheckChannel(in ChannelInfo info)
        {
            if (!info.Enabled) return SessionRejection.ChannelUnavailable;

            switch (info.Status)
            {
                case ChannelStatus.Maintenance: return SessionRejection.ChannelMaintenance;
                case ChannelStatus.Offline:
                case ChannelStatus.Unknown: return SessionRejection.ChannelUnavailable;
            }

            if (info.Population.IsFull) return SessionRejection.ChannelFull;

            return SessionRejection.None;
        }

        /// <summary>
        /// Finds the session a command names and confirms it belongs to the caller.
        /// </summary>
        /// <remarks>Where a spoofed account or session dies. The identity is looked up rather
        /// than trusted, and the account on the command must match the one on the session.</remarks>
        private static SessionRejection Resolve(SessionCommand command, in Context context,
            out AccountSessionState session)
        {
            session = null;

            if (!context.IsUsable || !command.IsValid) return SessionRejection.MissingContext;

            if (!context.Directory.TryGet(command.Session, out session) || session == null)
                return SessionRejection.SessionInvalid;

            if (session.Account != command.Account) return SessionRejection.SessionInvalid;

            if (session.State == SessionState.Revoked) return SessionRejection.SessionRevoked;

            if (session.State == SessionState.Expired
                || session.HasExpired(context.TimestampTicks))
            {
                return SessionRejection.SessionExpired;
            }

            if (command.ExpectedRevision.HasValue
                && session.Revision != command.ExpectedRevision.Value)
            {
                return SessionRejection.StaleRevision;
            }

            AccountStatus status = context.Authority.StatusOf(session.Account);
            if (status != AccountStatus.Active) return SessionRejection.AccountUnavailable;

            return SessionRejection.None;
        }

        private static bool TryReplaySession(SessionCommand command, in Context context,
            out SessionResult result)
        {
            result = default;
            if (!context.IsUsable) return false;

            SessionResult previous;
            if (!context.Directory.TryReplaySession(command.Request, out previous)) return false;

            result = SessionResult.Accepted(previous.Session, previous.State, previous.Revision,
                previous.Server, previous.Channel, previous.Character, true);

            return true;
        }

        private static SessionResult Accept(SessionCommand command, AccountSessionState session,
            in Context context)
        {
            SessionResult result = SessionResult.Accepted(session.SessionId, session.State,
                session.Revision, session.Server, session.Channel, session.Character);

            context.Directory.RememberSession(command.Request, result);

            return result;
        }

        private static SessionResult Reject(SessionRejection reason, SessionCommand command,
            AccountSessionState session = null)
        {
            return SessionResult.Rejected(reason, command.Session,
                session == null ? SessionState.Unauthenticated : session.State);
        }

        /// <summary>Reports an account status in the vocabulary a login speaks.</summary>
        private static LoginRejection Translate(AccountStatus status)
        {
            switch (status)
            {
                case AccountStatus.Active: return LoginRejection.None;
                case AccountStatus.Disabled: return LoginRejection.AccountDisabled;
                case AccountStatus.Banned: return LoginRejection.AccountBanned;
                case AccountStatus.Suspended: return LoginRejection.AccountSuspended;
                default: return LoginRejection.InvalidCredentials;
            }
        }
    }
}
