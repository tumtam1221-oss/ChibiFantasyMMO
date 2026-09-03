using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>What an account is allowed to do.</summary>
    /// <remarks>
    /// Closed technical category, decided by the account system and reported here. Nothing in
    /// this project sets it: there is no ban tool and no admin path, because those belong to
    /// the future backend. A domain that could set its own ban state would be a domain a
    /// client could talk out of one.
    /// </remarks>
    public enum AccountStatus
    {
        /// <summary>Not known. Treated as unusable, because unknown is not permission.</summary>
        Unknown = 0,

        Active = 1,

        /// <summary>Turned off by an operator. Usually reversible.</summary>
        Disabled = 2,

        /// <summary>Banned outright.</summary>
        Banned = 3,

        /// <summary>Banned for a period.</summary>
        Suspended = 4
    }

    /// <summary>
    /// An account the authority has already authenticated.
    /// </summary>
    /// <remarks>
    /// <b>The credential boundary, drawn here.</b> This is what the domain receives: an
    /// identity and a status. It is not what the domain is asked to verify. There is no
    /// password, no hash, no salt, no token secret and no login name on this type, and there
    /// is nowhere in the domain that could accept one -- verifying credentials belongs to the
    /// backend, and the domain is handed the conclusion.
    ///
    /// That is why <c>LoginRequest</c> carries no password either. The shape of these two
    /// types is the enforcement: a service cannot mishandle a secret it is never given.
    ///
    /// Flat because it has to persist and to travel: one row of a future <c>account</c> table
    /// is an id, a display name and a status.
    /// </remarks>
    public readonly struct AuthenticatedAccount
    {
        public AuthenticatedAccount(AccountId account, string displayName, AccountStatus status)
        {
            Account = account;
            DisplayName = displayName;
            Status = status;
        }

        public AccountId Account { get; }

        /// <summary>What a player sees. Never an identity, and never used to look anything up.</summary>
        public string DisplayName { get; }

        public AccountStatus Status { get; }

        public bool IsValid => Account.IsValid;

        /// <summary>Whether this account may hold a session.</summary>
        public bool CanSignIn => Status == AccountStatus.Active;

        /// <summary>
        /// The ownership identity this account acts under.
        /// </summary>
        /// <remarks>
        /// <b>Projection, not duplication.</b> Items, equipment and wallets have been owned by
        /// an <see cref="OwnerId"/> since Phase 08, and that type already documents itself as
        /// originating in the account system. Rather than introduce a second ownership notion,
        /// an account maps onto the one that exists, so every ownership check written in
        /// Phases 08 to 13 keeps working unchanged.
        /// </remarks>
        public OwnerId ToOwnerId()
        {
            return new OwnerId(Account.Value);
        }

        public override string ToString()
        {
            return Account + " (" + Status + ")";
        }
    }

    /// <summary>Why a login did not produce a session.</summary>
    /// <remarks>
    /// A typed reason rather than a bare false: each needs a different message to a player and
    /// a different response from a client. <see cref="InvalidCredentials"/> is deliberately
    /// one value covering "no such account" and "wrong secret" -- distinguishing them tells an
    /// attacker which accounts exist.
    /// </remarks>
    public enum LoginRejection
    {
        None = 0,

        /// <summary>No authority or no request was supplied.</summary>
        MissingContext = 1,

        /// <summary>The credential did not verify, or the account does not exist.</summary>
        InvalidCredentials = 2,

        AccountDisabled = 3,

        AccountBanned = 4,

        AccountSuspended = 5,

        /// <summary>The service is closed to players right now.</summary>
        Maintenance = 6,

        /// <summary>The binary is too old, or newer than the authority knows.</summary>
        ClientVersionMismatch = 7,

        /// <summary>The contract shape differs. A patch does not fix this.</summary>
        ProtocolVersionMismatch = 8,

        /// <summary>The authority could not be reached or refused to answer.</summary>
        ServerUnavailable = 9,

        /// <summary>This account already holds a live session.</summary>
        SessionAlreadyActive = 10,

        /// <summary>Too many attempts, too quickly.</summary>
        RateLimited = 11,

        UnknownError = 12
    }

    /// <summary>
    /// A request to begin a session.
    /// </summary>
    /// <remarks>
    /// <b>No secret appears here.</b> The credential is verified by the backend before the
    /// domain sees anything; what reaches the domain is an
    /// <see cref="AuthenticatedAccount"/>. This request carries the identity of the attempt
    /// and what the client says it is -- nothing that could be stolen from a log.
    ///
    /// <see cref="RequestId"/> is the idempotency key, the same one Phase 13 established: a
    /// retried login returns the session the first attempt produced rather than a second one.
    /// </remarks>
    public readonly struct LoginRequest
    {
        public LoginRequest(RequestId request, VersionSet versions)
        {
            Request = request;
            Versions = versions;
        }

        public RequestId Request { get; }

        /// <summary>What the client reports about itself. Checked, never trusted.</summary>
        public VersionSet Versions { get; }

        public bool IsValid => Request.IsValid;

        public override string ToString()
        {
            return "login " + Request + " (" + Versions + ")";
        }
    }

    /// <summary>What a login attempt produced.</summary>
    public readonly struct LoginResult
    {
        private LoginResult(bool accepted, LoginRejection reason, SessionId session,
            AccountId account, VersionCompatibilityResult compatibility, bool replay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Session = session;
            Account = account;
            Compatibility = compatibility;
            IsReplay = replay;
        }

        public bool IsAccepted { get; }

        public LoginRejection Reason { get; }

        /// <summary>The session the authority issued. Invalid on refusal.</summary>
        public SessionId Session { get; }

        public AccountId Account { get; }

        /// <summary>
        /// What the version check concluded.
        /// </summary>
        /// <remarks>Carried even on success, so a client can show an optional-update prompt
        /// without asking a second time, and carried on a version refusal so a launcher knows
        /// what to fetch.</remarks>
        public VersionCompatibilityResult Compatibility { get; }

        /// <summary>Whether this answer came from a previous attempt with the same request.</summary>
        public bool IsReplay { get; }

        public static LoginResult Accepted(SessionId session, AccountId account,
            VersionCompatibilityResult compatibility, bool replay = false)
        {
            return new LoginResult(true, LoginRejection.None, session, account, compatibility,
                replay);
        }

        public static LoginResult Rejected(LoginRejection reason,
            VersionCompatibilityResult compatibility = default)
        {
            return new LoginResult(false, reason, default, default, compatibility, false);
        }

        public override string ToString()
        {
            return IsAccepted ? "session " + Session : "rejected: " + Reason;
        }
    }
}
