using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>How far along a session is.</summary>
    /// <remarks>
    /// <b>A sequence, not a set of flags.</b> Each value is reached from exactly one
    /// predecessor, so "selected a channel without selecting a server" is not representable
    /// rather than merely refused. <c>AccountSessionState</c> owns the legal moves and every
    /// other move is rejected -- there is no path that skips a stage.
    ///
    /// <see cref="Expired"/> and <see cref="Revoked"/> are terminal and distinct: one is the
    /// clock running out and the other is somebody taking the session away, and a player is
    /// owed different words for them.
    /// </remarks>
    public enum SessionState
    {
        /// <summary>Nobody has signed in. The starting point and not a session anybody holds.</summary>
        Unauthenticated = 0,

        /// <summary>Signed in. No server chosen yet.</summary>
        Authenticated = 1,

        ServerSelected = 2,

        ChannelSelected = 3,

        CharacterSelected = 4,

        /// <summary>The handoff has begun. Offers are locked and the world is loading.</summary>
        EnteringWorld = 5,

        /// <summary>In the world.</summary>
        Active = 6,

        /// <summary>The clock ran out. Terminal.</summary>
        Expired = 7,

        /// <summary>Taken away by the authority or by a newer sign-in. Terminal.</summary>
        Revoked = 8
    }

    /// <summary>Why a session command was refused.</summary>
    /// <remarks>
    /// One vocabulary shared by every selection step, so a client handles one enum and a
    /// second step cannot invent a reason nobody displays.
    /// </remarks>
    public enum SessionRejection
    {
        None = 0,

        /// <summary>No session, no authority or no request was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such session, or it is not this account's.</summary>
        SessionInvalid = 2,

        /// <summary>The clock ran out.</summary>
        SessionExpired = 3,

        /// <summary>The authority took it away.</summary>
        SessionRevoked = 4,

        /// <summary>The session is not at a stage where this command makes sense.</summary>
        InvalidTransition = 5,

        /// <summary>The session's recorded revision is not the one the caller expected.</summary>
        StaleRevision = 6,

        /// <summary>No such server.</summary>
        UnknownServer = 7,

        /// <summary>The server is off, hidden, offline or unknown.</summary>
        ServerUnavailable = 8,

        /// <summary>The server is closed for maintenance.</summary>
        ServerMaintenance = 9,

        /// <summary>The server is at capacity.</summary>
        ServerFull = 10,

        /// <summary>No such channel.</summary>
        UnknownChannel = 11,

        /// <summary>The channel belongs to a different server than the one selected.</summary>
        ChannelServerMismatch = 12,

        /// <summary>The channel is off or offline.</summary>
        ChannelUnavailable = 13,

        ChannelMaintenance = 14,

        ChannelFull = 15,

        /// <summary>No such character.</summary>
        UnknownCharacter = 16,

        /// <summary>The character belongs to another account.</summary>
        CharacterNotOwned = 17,

        /// <summary>The character exists and is this account's, but cannot be played now.</summary>
        CharacterUnavailable = 18,

        /// <summary>The client's versions no longer satisfy the target.</summary>
        VersionMismatch = 19,

        /// <summary>This session is already in the world.</summary>
        AlreadyInWorld = 20,

        /// <summary>The account is not in a state that permits play.</summary>
        AccountUnavailable = 21,

        /// <summary>Too many attempts, too quickly.</summary>
        RateLimited = 22
    }

    /// <summary>
    /// A command against a session.
    /// </summary>
    /// <remarks>
    /// <b>Everything the authority needs to refuse a lie.</b> A client states which session
    /// and which account it believes it is; the authority looks the session up and compares.
    /// Editing the account in this request does not change whose session it is -- it changes
    /// the request into one that does not match, which is refused.
    ///
    /// <see cref="ExpectedRevision"/> is optional optimistic concurrency: supply it to refuse
    /// a command built against a stale view of the session.
    /// </remarks>
    public readonly struct SessionCommand
    {
        public SessionCommand(RequestId request, SessionId session, AccountId account,
            Revision? expectedRevision = null)
        {
            Request = request;
            Session = session;
            Account = account;
            ExpectedRevision = expectedRevision;
        }

        public RequestId Request { get; }

        public SessionId Session { get; }

        /// <summary>Who the client says it is. Compared against the session, never believed.</summary>
        public AccountId Account { get; }

        /// <summary>The session revision the caller last saw, or null to skip the check.</summary>
        public Revision? ExpectedRevision { get; }

        public bool IsValid => Request.IsValid && Session.IsValid;

        public override string ToString()
        {
            return Request + " on " + Session;
        }
    }

    /// <summary>What a session command did.</summary>
    /// <remarks>
    /// Ids and a revision, never the session object. A result that handed back live state
    /// would let a caller change a session by writing to what it was told.
    /// </remarks>
    public readonly struct SessionResult
    {
        private SessionResult(bool accepted, SessionRejection reason, SessionId session,
            SessionState state, Revision revision, ServerId server, ChannelId channel,
            CharacterId character, bool replay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Session = session;
            State = state;
            Revision = revision;
            Server = server;
            Channel = channel;
            Character = character;
            IsReplay = replay;
        }

        public bool IsAccepted { get; }

        public SessionRejection Reason { get; }

        public SessionId Session { get; }

        /// <summary>What the session is now.</summary>
        public SessionState State { get; }

        public Revision Revision { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        public CharacterId Character { get; }

        /// <summary>Whether this answer came from a previous identical request.</summary>
        public bool IsReplay { get; }

        /// <summary>Whether the client still has to fetch a channel list.</summary>
        /// <remarks>Reported by a server selection so a client knows the next screen without
        /// deciding the flow for itself.</remarks>
        public bool ChannelDiscoveryRequired => State == SessionState.ServerSelected;

        public static SessionResult Accepted(SessionId session, SessionState state,
            Revision revision, ServerId server = default, ChannelId channel = default,
            CharacterId character = default, bool replay = false)
        {
            return new SessionResult(true, SessionRejection.None, session, state, revision,
                server, channel, character, replay);
        }

        public static SessionResult Rejected(SessionRejection reason,
            SessionId session = default, SessionState state = SessionState.Unauthenticated)
        {
            return new SessionResult(false, reason, session, state, default, default, default,
                default, false);
        }

        public override string ToString()
        {
            return IsAccepted ? Session + " -> " + State : "rejected: " + Reason;
        }
    }

    /// <summary>How far into the world a session has got.</summary>
    /// <remarks>
    /// Three values because the handoff is genuinely three steps and Phase 14 only owns the
    /// first. <see cref="Authorised"/> is what this phase produces: the authority has agreed
    /// and named everything the world needs. Loading and readiness belong to the phase that
    /// actually connects, and are declared here so that phase changes no contract.
    /// </remarks>
    public enum WorldEntryState
    {
        None = 0,

        /// <summary>The authority agreed. Nothing has connected yet.</summary>
        Authorised = 1,

        /// <summary>A connection is being established. Not implemented in this phase.</summary>
        Connecting = 2,

        /// <summary>The character is in the world. Not implemented in this phase.</summary>
        Ready = 3
    }

    /// <summary>
    /// A request to hand a session over to the game world.
    /// </summary>
    /// <remarks>
    /// <b>Every identity is stated and every one is checked.</b> The client repeats what it
    /// believes -- account, character, server, channel, versions -- and the authority compares
    /// each against the session it holds. Editing any field produces a request that disagrees
    /// with the session and is refused; it does not produce a different outcome. That is the
    /// whole point of restating them rather than trusting the client to send only a session.
    /// </remarks>
    public readonly struct EnterWorldRequest
    {
        public EnterWorldRequest(RequestId request, SessionId session, AccountId account,
            CharacterId character, ServerId server, ChannelId channel, VersionSet versions)
        {
            Request = request;
            Session = session;
            Account = account;
            Character = character;
            Server = server;
            Channel = channel;
            Versions = versions;
        }

        public RequestId Request { get; }

        public SessionId Session { get; }

        public AccountId Account { get; }

        public CharacterId Character { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        public VersionSet Versions { get; }

        public bool IsValid => Request.IsValid && Session.IsValid;

        public override string ToString()
        {
            return "enter " + Character + " on " + Server + "/" + Channel;
        }
    }

    /// <summary>What an enter-world attempt produced.</summary>
    /// <remarks>
    /// <b>Identifiers, not a scene.</b> The domain says who is entering the world where; the
    /// client resolves that to a scene through the loader Phase 11 already built. Nothing here
    /// names a scene, and nothing here connects anything.
    /// </remarks>
    public readonly struct EnterWorldResult
    {
        private EnterWorldResult(bool accepted, SessionRejection reason, SessionId session,
            CharacterId character, ServerId server, ChannelId channel, DefinitionId map,
            WorldEntryState entryState, Revision characterRevision, Revision sessionRevision,
            bool replay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Session = session;
            Character = character;
            Server = server;
            Channel = channel;
            Map = map;
            EntryState = entryState;
            CharacterRevision = characterRevision;
            SessionRevision = sessionRevision;
            IsReplay = replay;
        }

        public bool IsAccepted { get; }

        public SessionRejection Reason { get; }

        public SessionId Session { get; }

        public CharacterId Character { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        /// <summary>
        /// Where the character stands.
        /// </summary>
        /// <remarks>A <see cref="DefinitionId"/> naming a map, read from the character's own
        /// Phase 11 location state. No second location model exists, and no scene name appears
        /// -- resolving one is the Phase 11 loader's job.</remarks>
        public DefinitionId Map { get; }

        public WorldEntryState EntryState { get; }

        /// <summary>The character's revision at the moment authority was granted.</summary>
        public Revision CharacterRevision { get; }

        public Revision SessionRevision { get; }

        public bool IsReplay { get; }

        public static EnterWorldResult Accepted(SessionId session, CharacterId character,
            ServerId server, ChannelId channel, DefinitionId map, Revision characterRevision,
            Revision sessionRevision, bool replay = false)
        {
            return new EnterWorldResult(true, SessionRejection.None, session, character, server,
                channel, map, WorldEntryState.Authorised, characterRevision, sessionRevision,
                replay);
        }

        public static EnterWorldResult Rejected(SessionRejection reason,
            SessionId session = default)
        {
            return new EnterWorldResult(false, reason, session, default, default, default,
                default, WorldEntryState.None, default, default, false);
        }

        public override string ToString()
        {
            return IsAccepted
                ? Character + " authorised for " + Server + "/" + Channel
                : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// An opaque credential a transport may carry.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately empty of meaning.</b> It is a string this project neither creates nor
    /// interprets. Nothing encodes an account into it, nothing decodes one out of it, nothing
    /// signs it, and no secret exists anywhere in this repository to sign it with.
    ///
    /// It exists so that when a real transport arrives it has a typed place to put whatever it
    /// issues, without any code above having to change. Treating this as proof of anything
    /// would be exactly the mistake the type is shaped to prevent -- which is why the session
    /// services never take one, and authorisation is decided by looking the session up.
    /// </remarks>
    public readonly struct SessionToken
    {
        public SessionToken(string value)
        {
            Value = value;
        }

        /// <summary>Whatever the transport issued. Never parsed here.</summary>
        public string Value { get; }

        public bool IsPresent => !string.IsNullOrEmpty(Value);

        public static SessionToken None => default;

        /// <summary>Never prints the value: a token does not belong in a log.</summary>
        public override string ToString()
        {
            return IsPresent ? "<token>" : "<none>";
        }
    }
}
