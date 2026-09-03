using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The authored limits the login and session flow obeys.
    /// </summary>
    /// <remarks>
    /// <b>One definition rather than four.</b> Character slots, session lifetime and the
    /// attempt allowances are all the same kind of thing -- a number an operator tunes -- and
    /// splitting them would mean four lookups and four chances to hand a service the wrong
    /// one. The same shape <see cref="SocialConfiguration"/> already takes.
    ///
    /// <b>Every default is the harmless reading of an unauthored field.</b> A service handed
    /// no configuration uses <see cref="Default"/>, which carries the shipped values, rather
    /// than zeroing out and refusing everything. A zero character-slot limit would make the
    /// game unplayable rather than unconfigured.
    ///
    /// Flat and DB-friendly: one row of a future <c>session_configuration</c> table.
    /// </remarks>
    public sealed class SessionConfiguration : GameDefinition
    {
        [Tooltip("Most characters one account may hold per server. Zero or less means no limit.")]
        [SerializeField] private int _maxCharacterSlots = DefaultCharacterSlots;

        [Tooltip("How long a session stays valid, in seconds. Zero or less means it does not expire.")]
        [SerializeField] private int _sessionLifetimeSeconds;

        [Tooltip("Login attempts allowed inside the window. Zero or less means unlimited.")]
        [SerializeField] private int _maxLoginAttempts;

        [Tooltip("How long the attempt window is, in seconds.")]
        [SerializeField] private int _loginAttemptWindowSeconds = 60;

        [Tooltip("Enter-world attempts allowed inside the window. Zero or less means unlimited.")]
        [SerializeField] private int _maxEnterWorldAttempts;

        [Tooltip("Whether one account may hold more than one live session at a time.")]
        [SerializeField] private bool _allowConcurrentSessions;

        /// <summary>The slot count this game ships with.</summary>
        /// <remarks>A named constant rather than a literal scattered through the services, and
        /// still only the <em>default</em>: <see cref="MaxCharacterSlots"/> is what anything
        /// reads, and content overrides it.</remarks>
        public const int DefaultCharacterSlots = 5;

        public int MaxCharacterSlots =>
            _maxCharacterSlots > 0 ? _maxCharacterSlots : int.MaxValue;

        /// <summary>Zero means a session does not expire on its own.</summary>
        public int SessionLifetimeSeconds =>
            _sessionLifetimeSeconds > 0 ? _sessionLifetimeSeconds : 0;

        public int MaxLoginAttempts => _maxLoginAttempts > 0 ? _maxLoginAttempts : int.MaxValue;

        public int LoginAttemptWindowSeconds =>
            _loginAttemptWindowSeconds > 0 ? _loginAttemptWindowSeconds : 60;

        public int MaxEnterWorldAttempts =>
            _maxEnterWorldAttempts > 0 ? _maxEnterWorldAttempts : int.MaxValue;

        /// <summary>
        /// Whether a second sign-in is allowed while one session is live.
        /// </summary>
        /// <remarks>Authored because both answers are legitimate: some games refuse the second
        /// login, others revoke the first. This phase implements the refusal
        /// (<c>SessionAlreadyActive</c>); revoking-on-login is a future policy and is not
        /// pretended.</remarks>
        public bool AllowConcurrentSessions => _allowConcurrentSessions;

        /// <summary>
        /// The values used when no configuration asset was supplied.
        /// </summary>
        /// <remarks>A readonly struct rather than a constructed <see cref="ScriptableObject"/>,
        /// because a definition cannot be created outside the editor and a service must still
        /// work in a headless server with no assets loaded.</remarks>
        public readonly struct Limits
        {
            public Limits(int maxCharacterSlots, int sessionLifetimeSeconds, int maxLoginAttempts,
                int loginAttemptWindowSeconds, int maxEnterWorldAttempts,
                bool allowConcurrentSessions)
            {
                MaxCharacterSlots = maxCharacterSlots;
                SessionLifetimeSeconds = sessionLifetimeSeconds;
                MaxLoginAttempts = maxLoginAttempts;
                LoginAttemptWindowSeconds = loginAttemptWindowSeconds;
                MaxEnterWorldAttempts = maxEnterWorldAttempts;
                AllowConcurrentSessions = allowConcurrentSessions;
            }

            public int MaxCharacterSlots { get; }

            public int SessionLifetimeSeconds { get; }

            public int MaxLoginAttempts { get; }

            public int LoginAttemptWindowSeconds { get; }

            public int MaxEnterWorldAttempts { get; }

            public bool AllowConcurrentSessions { get; }

            /// <summary>Whether sessions expire at all under these limits.</summary>
            public bool SessionsExpire => SessionLifetimeSeconds > 0;
        }

        /// <summary>The shipped defaults.</summary>
        public static Limits Default => new Limits(DefaultCharacterSlots, 0, int.MaxValue, 60,
            int.MaxValue, false);

        public Limits ToLimits()
        {
            return new Limits(MaxCharacterSlots, SessionLifetimeSeconds, MaxLoginAttempts,
                LoginAttemptWindowSeconds, MaxEnterWorldAttempts, AllowConcurrentSessions);
        }

        /// <summary>The limits an optional configuration supplies, or the shipped defaults.</summary>
        public static Limits Resolve(SessionConfiguration configuration)
        {
            return configuration == null ? Default : configuration.ToLimits();
        }
    }
}
