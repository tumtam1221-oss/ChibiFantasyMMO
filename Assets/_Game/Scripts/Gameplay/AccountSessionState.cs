using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One signed-in session, authoritatively.
    /// </summary>
    /// <remarks>
    /// <b>The state machine, and the only one.</b> Every legal move between
    /// <see cref="SessionState"/> values is expressed once, in <see cref="CanTransitionTo"/>,
    /// and every mutating method goes through it. There is no second login state, no separate
    /// server-select state and no character-select state to fall out of step -- a screen that
    /// wanted to know where the flow stood asks this.
    ///
    /// <b>Skipping a stage is not representable.</b> Selecting a channel requires
    /// <see cref="SessionState.ServerSelected"/>, and selecting a character requires
    /// <see cref="SessionState.ChannelSelected"/>, so a client cannot reach the world by
    /// sending the last request first. Terminal states accept nothing at all.
    ///
    /// <b>It holds identifiers and nothing else.</b> No <c>GameObject</c>, no scene, no
    /// <c>NetworkObject</c>, no FishNet connection, no transport handle. This assembly is
    /// engine-free, and a session that held a connection could not be persisted, restored, or
    /// reasoned about by a headless server.
    ///
    /// <b>Nothing here reads a clock.</b> Expiry is asked against a caller-supplied time, the
    /// same contract every service in this assembly keeps.
    ///
    /// <b>No credential, ever.</b> There is no password field, no hash, no salt and no secret.
    /// The session is created from an <see cref="AuthenticatedAccount"/> -- a conclusion the
    /// backend reached -- so there is nothing here to leak.
    ///
    /// Flat because it has to persist: one row of a future <c>account_session</c> table is a
    /// session id, an account, a state, two timestamps, three selections and a revision.
    /// </remarks>
    public sealed class AccountSessionState : IPersistentState
    {
        private SessionState _state;
        private ServerId _server;
        private ChannelId _channel;
        private CharacterId _character;
        private Revision _revision;

        public AccountSessionState(SessionId sessionId, AccountId account, VersionSet versions,
            long issuedTicks = 0L, long expiresTicks = 0L)
        {
            SessionId = sessionId;
            Account = account;
            Versions = versions;
            IssuedTicks = issuedTicks;
            ExpiresTicks = expiresTicks;
            _state = SessionState.Authenticated;
            _revision = Revision.Initial;
        }

        public SessionId SessionId { get; }

        public AccountId Account { get; }

        /// <summary>What the client reported when it signed in. Re-checked at enter-world.</summary>
        public VersionSet Versions { get; }

        public long IssuedTicks { get; }

        /// <summary>When it lapses. Zero means it does not expire on its own.</summary>
        public long ExpiresTicks { get; }

        public SessionState State => _state;

        public Revision Revision => _revision;

        /// <summary>The chosen server. Invalid until one is selected.</summary>
        public ServerId Server => _server;

        public ChannelId Channel => _channel;

        public CharacterId Character => _character;

        /// <summary>The ownership identity this session acts under.</summary>
        /// <remarks>The Phase 08 projection, so every existing ownership check keeps working.</remarks>
        public OwnerId Owner => new OwnerId(Account.Value);

        public bool IsTerminal => _state == SessionState.Expired || _state == SessionState.Revoked;

        /// <summary>Whether the session is in the world, or on its way.</summary>
        public bool IsInWorld => _state == SessionState.EnteringWorld
            || _state == SessionState.Active;

        /// <summary>Whether it has lapsed as of a caller-supplied time.</summary>
        public bool HasExpired(long nowTicks)
        {
            return ExpiresTicks > 0L && nowTicks >= ExpiresTicks;
        }

        /// <summary>
        /// Whether the session is usable right now.
        /// </summary>
        /// <remarks>Expiry is evaluated rather than remembered, so a session that lapsed while
        /// nobody was looking is not usable the next time somebody asks. Marking it expired is
        /// a separate, explicit step -- see <see cref="Expire"/> -- because reading should not
        /// mutate.</remarks>
        public bool IsUsable(long nowTicks)
        {
            return !IsTerminal && !HasExpired(nowTicks);
        }

        /// <summary>
        /// Whether one state may follow another.
        /// </summary>
        /// <remarks>
        /// The whole machine, stated once. Written as an explicit table rather than as
        /// ordinal arithmetic because the legal moves are not simply "forwards": a player may
        /// step back from a channel to pick a different server, and expiry or revocation may
        /// arrive from anywhere.
        /// </remarks>
        public static bool CanTransitionTo(SessionState from, SessionState to)
        {
            // Terminal states are terminal. Nothing leaves them, including into each other.
            if (from == SessionState.Expired || from == SessionState.Revoked) return false;

            // The authority may end a session from any live state.
            if (to == SessionState.Expired || to == SessionState.Revoked) return true;

            switch (from)
            {
                case SessionState.Unauthenticated:
                    return to == SessionState.Authenticated;

                case SessionState.Authenticated:
                    return to == SessionState.ServerSelected;

                case SessionState.ServerSelected:
                    // Re-picking a server is allowed; going forward needs a channel.
                    return to == SessionState.ChannelSelected
                        || to == SessionState.ServerSelected;

                case SessionState.ChannelSelected:
                    // Stepping back to re-pick a server or a channel, or forward to a character.
                    return to == SessionState.CharacterSelected
                        || to == SessionState.ChannelSelected
                        || to == SessionState.ServerSelected;

                case SessionState.CharacterSelected:
                    // Forward into the world, or back to change any earlier choice.
                    return to == SessionState.EnteringWorld
                        || to == SessionState.CharacterSelected
                        || to == SessionState.ChannelSelected
                        || to == SessionState.ServerSelected;

                case SessionState.EnteringWorld:
                    // Only the world itself moves this on; a failed entry falls back.
                    return to == SessionState.Active
                        || to == SessionState.CharacterSelected;

                case SessionState.Active:
                    // Leaving the world returns to selection.
                    return to == SessionState.CharacterSelected;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Records a chosen server.
        /// </summary>
        /// <remarks>
        /// Assignment only: whether the server exists, is open and is compatible is
        /// <see cref="SessionFlowService"/>'s decision. Choosing a different server clears the
        /// channel and character beneath it, because a channel of another server and a
        /// character on another server are both nonsense -- leaving them would be exactly the
        /// mismatch the enter-world checks exist to catch.
        /// </remarks>
        public bool TrySelectServer(ServerId server)
        {
            if (!server.IsValid) return false;
            if (!CanTransitionTo(_state, SessionState.ServerSelected)) return false;

            if (_state == SessionState.ServerSelected && _server == server) return false;

            _server = server;
            _channel = ChannelId.None;
            _character = CharacterId.None;
            _state = SessionState.ServerSelected;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Records a chosen channel. Clears any character beneath it.</summary>
        public bool TrySelectChannel(ChannelId channel)
        {
            if (!channel.IsValid) return false;
            if (!CanTransitionTo(_state, SessionState.ChannelSelected)) return false;

            if (_state == SessionState.ChannelSelected && _channel == channel) return false;

            _channel = channel;
            _character = CharacterId.None;
            _state = SessionState.ChannelSelected;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Records a chosen character.</summary>
        public bool TrySelectCharacter(CharacterId character)
        {
            if (!character.IsValid) return false;
            if (!CanTransitionTo(_state, SessionState.CharacterSelected)) return false;

            if (_state == SessionState.CharacterSelected && _character == character) return false;

            _character = character;
            _state = SessionState.CharacterSelected;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Begins the handoff to the world.
        /// </summary>
        /// <remarks>Reachable only from <see cref="SessionState.CharacterSelected"/>, so
        /// everything the world needs has already been chosen and checked. Entering twice is
        /// refused, which is what makes a duplicate handoff impossible rather than merely
        /// unlikely.</remarks>
        public bool TryBeginWorldEntry()
        {
            if (!CanTransitionTo(_state, SessionState.EnteringWorld)) return false;

            _state = SessionState.EnteringWorld;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Records that the world accepted the character.
        /// </summary>
        /// <remarks>Called by whatever actually connects. Phase 14 stops at
        /// <see cref="SessionState.EnteringWorld"/> and does not call this itself, because
        /// nothing has connected -- claiming otherwise would be the fake connection the brief
        /// forbids.</remarks>
        public bool TryCompleteWorldEntry()
        {
            if (!CanTransitionTo(_state, SessionState.Active)) return false;

            _state = SessionState.Active;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Falls back after a failed or abandoned world entry.</summary>
        public bool TryLeaveWorld()
        {
            if (!CanTransitionTo(_state, SessionState.CharacterSelected)) return false;
            if (_state == SessionState.CharacterSelected) return false;

            _state = SessionState.CharacterSelected;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Marks the session lapsed.
        /// </summary>
        /// <remarks>Explicit rather than automatic on read, so a query never mutates and a
        /// caller can decide when to sweep. <see cref="IsUsable"/> already treats a lapsed
        /// session as unusable whether or not this has been called.</remarks>
        public bool Expire()
        {
            if (!CanTransitionTo(_state, SessionState.Expired)) return false;

            _state = SessionState.Expired;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Takes the session away. The authority's to call, never a client's.</summary>
        public bool Revoke()
        {
            if (!CanTransitionTo(_state, SessionState.Revoked)) return false;

            _state = SessionState.Revoked;
            _revision = _revision.Next();
            return true;
        }
    }
}
