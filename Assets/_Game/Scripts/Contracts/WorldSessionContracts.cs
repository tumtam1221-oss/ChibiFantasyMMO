using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>
    /// What a connecting client offers as proof, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>One field carries meaning; the rest are claims.</b> The token is the only thing
    /// the server acts on -- it is resolved against the authority, which answers with the
    /// account, character, server and channel it recorded. The version set is checked. The
    /// remaining fields are what the client *believes*, carried so the server can compare
    /// them and refuse a disagreement.
    ///
    /// <b>Why carry the claims at all, if they are never trusted?</b> Because a client whose
    /// idea of which character it is playing has diverged from the authority's must be told
    /// so, rather than silently dropped into the world as somebody else. A disagreement is a
    /// bug or an attack; either way it is worth naming. Phase 14's
    /// <see cref="EnterWorldRequest"/> restates identities for exactly the same reason.
    ///
    /// Editing any claim produces a request that disagrees with the session and is refused.
    /// It cannot produce a different outcome, because no claim is ever read as an answer.
    /// </remarks>
    public readonly struct WorldJoinClaim
    {
        public WorldJoinClaim(SessionToken token, VersionSet versions, AccountId account = default,
            CharacterId character = default, ServerId server = default, ChannelId channel = default)
        {
            Token = token;
            Versions = versions;
            ClaimedAccount = account;
            ClaimedCharacter = character;
            ClaimedServer = server;
            ClaimedChannel = channel;
        }

        /// <summary>The only thing here that proves anything.</summary>
        public SessionToken Token { get; }

        /// <summary>What the client reports it is running. Decided by the authority.</summary>
        public VersionSet Versions { get; }

        public AccountId ClaimedAccount { get; }

        public CharacterId ClaimedCharacter { get; }

        public ServerId ClaimedServer { get; }

        public ChannelId ClaimedChannel { get; }

        public bool HasToken => Token.IsPresent;

        /// <summary>Never prints the token.</summary>
        public override string ToString()
        {
            return "join claim " + Token;
        }
    }

    /// <summary>
    /// The authored facts about a character, as the authority holds them.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not the whole character.</b> Level, class, job, gender and appearance
    /// are what the account database actually stores and what a world server needs in order
    /// to place somebody correctly. Stats, experience, inventory and equipment are not here
    /// because the Phase 15 API does not serve them -- and inventing plausible values so the
    /// type looked complete would be worse than the gap, because a fabricated stat block is
    /// indistinguishable from a real one until it matters.
    ///
    /// <b>Everything is a definition id or a number.</b> Nothing here is a resolved
    /// definition object: the server names what the character is, and content is looked up
    /// against the registries Phase 05 already owns. Duplicating authored content in the
    /// account database would create a second source of truth that goes stale on the next
    /// content patch, which is the same reason the character table stores ids and not stats.
    /// </remarks>
    public readonly struct WorldCharacterProfile
    {
        public WorldCharacterProfile(int level, int gender, DefinitionId characterClass,
            DefinitionId job, DefinitionId appearance)
        {
            Level = level;
            Gender = gender;
            Class = characterClass;
            Job = job;
            Appearance = appearance;
        }

        /// <summary>Read from the character row. Never from the client.</summary>
        public int Level { get; }

        /// <summary>Mirrors Phase 04 <c>CharacterGender</c> numerically.</summary>
        public int Gender { get; }

        public DefinitionId Class { get; }

        public DefinitionId Job { get; }

        public DefinitionId Appearance { get; }

        public bool IsPresent => Level > 0 || Class.IsValid;

        public override string ToString()
        {
            return "level " + Level + " " + Class;
        }
    }

    /// <summary>
    /// The authority's answer: who this connection actually is, or why it is not admitted.
    /// </summary>
    /// <remarks>
    /// <b>Every identity here came from the authority.</b> Nothing on this type was copied
    /// from the claim. That is the invariant the whole design rests on: a caller handed an
    /// admission is holding facts, not repetitions.
    ///
    /// Refusals reuse <see cref="SessionRejection"/> rather than inventing a network-specific
    /// vocabulary, so a client handles one enum whether it was refused at character select or
    /// at the world door, and a reason cannot mean two things in two places.
    /// </remarks>
    public readonly struct WorldAdmission
    {
        private WorldAdmission(bool admitted, SessionRejection reason, SessionId session,
            AccountId account, CharacterId character, ServerId server, ChannelId channel,
            DefinitionId map, Revision sessionRevision, Revision characterRevision,
            SessionState state, WorldCharacterProfile profile)
        {
            IsAdmitted = admitted;
            Reason = reason;
            Session = session;
            Account = account;
            Character = character;
            Server = server;
            Channel = channel;
            Map = map;
            SessionRevision = sessionRevision;
            CharacterRevision = characterRevision;
            State = state;
            Profile = profile;
        }

        public bool IsAdmitted { get; }

        /// <summary>Why not. <see cref="SessionRejection.None"/> when admitted.</summary>
        public SessionRejection Reason { get; }

        public SessionId Session { get; }

        /// <summary>Resolved from the token. Never from the connecting client.</summary>
        public AccountId Account { get; }

        public CharacterId Character { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        /// <summary>Where the character stands, read from its own row.</summary>
        public DefinitionId Map { get; }

        public Revision SessionRevision { get; }

        public Revision CharacterRevision { get; }

        /// <summary>The session's state at the moment of admission.</summary>
        public SessionState State { get; }

        /// <summary>What the character is, according to the authority.</summary>
        public WorldCharacterProfile Profile { get; }

        /// <summary>
        /// The account, projected onto the ownership Phase 08 already defined.
        /// </summary>
        /// <remarks>
        /// <b>Projected, never supplied.</b> There is no owner on a claim, on a join message
        /// or anywhere a client can reach, so a forged owner is unrepresentable rather than
        /// refused. This is the same projection <c>AuthenticatedAccount.ToOwnerId</c> makes,
        /// and there is deliberately only one ownership model in the project.
        /// </remarks>
        public OwnerId Owner => new OwnerId(Account.Value);

        /// <summary>Whether the account owns a character worth spawning.</summary>
        public bool HasCharacter => Character.IsValid;

        public static WorldAdmission Admitted(SessionId session, AccountId account,
            CharacterId character, ServerId server, ChannelId channel, DefinitionId map,
            Revision sessionRevision, Revision characterRevision, SessionState state,
            WorldCharacterProfile profile = default)
        {
            return new WorldAdmission(true, SessionRejection.None, session, account, character,
                server, channel, map, sessionRevision, characterRevision, state, profile);
        }

        public static WorldAdmission Refused(SessionRejection reason)
        {
            return new WorldAdmission(false, reason, default, default, default, default,
                default, default, default, default, SessionState.Unauthenticated, default);
        }

        public override string ToString()
        {
            return IsAdmitted
                ? Account + " as " + Character + " on " + Server + "/" + Channel
                : "refused: " + Reason;
        }
    }

    /// <summary>
    /// The authority a world server asks about a connection.
    /// </summary>
    /// <remarks>
    /// <b>This is the boundary in rule 16.14.</b> A world server implemented against it
    /// names no HTTP, no PHP, no SQL and no PDO -- it asks three questions and is answered.
    /// The implementation that speaks to the Phase 15 API lives in the Backend assembly,
    /// which is the only place a transport exists.
    ///
    /// <b>Deliberately narrow.</b> Three methods, because a world server needs exactly
    /// three things: to find out who a connection is, to say the character arrived, and to
    /// say it left. A wider interface would invite the server to do account work that is
    /// not its to do.
    ///
    /// <b>Synchronous, like every other authority seam here.</b> A dedicated server is
    /// already off the main thread, and the alternative would push asynchrony through every
    /// caller and every test to no benefit. See <c>IAccountApi</c> for the same reasoning.
    /// </remarks>
    public interface IWorldSessionAuthority
    {
        /// <summary>
        /// Resolves a connection's proof into an identity, or refuses it.
        /// </summary>
        /// <remarks>The one call that decides whether anything is spawned. Everything the
        /// result contains is the authority's, so a caller cannot accidentally act on a
        /// value the client supplied.</remarks>
        WorldAdmission Admit(WorldJoinClaim claim);

        /// <summary>
        /// Reports that the character is in the world, moving the session to Active.
        /// </summary>
        /// <remarks>Separate from <see cref="Admit"/> because admission and arrival are
        /// genuinely different moments: a connection can be admitted and then fail to load,
        /// and a session left in EnteringWorld is exactly the right record of that.</remarks>
        bool ConfirmArrival(SessionId session);

        /// <summary>
        /// Releases the session and the character it was holding.
        /// </summary>
        /// <remarks>Must be safe to call repeatedly: a disconnect can be observed more than
        /// once, and the second observation must not corrupt a session that a third party
        /// has since started.</remarks>
        bool Release(SessionId session);
    }

    /// <summary>Where a character is, as the world server knows it.</summary>
    /// <remarks>
    /// <b>Three values, all derived from something real.</b> Offline is the absence of a
    /// connection, Connecting is an admitted connection that has not finished arriving, and
    /// InWorld is a spawned character. None of them is inferred from a timer or a guess,
    /// which is why there is no Online: "online" would be a claim nothing here can
    /// substantiate.
    ///
    /// This is the first authoritative presence in the project. Phase 13's
    /// <c>CharacterAvailability.InWorld</c> recorded that the authority had handed a
    /// character over; this records whether anybody is actually holding it.
    /// </remarks>
    public enum WorldPresence
    {
        /// <summary>No connection. The default, and what a disconnect returns to.</summary>
        Offline = 0,

        /// <summary>Admitted, not yet arrived.</summary>
        Connecting = 1,

        /// <summary>Spawned and playing.</summary>
        InWorld = 2
    }
}
