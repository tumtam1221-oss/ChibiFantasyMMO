using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Every live session, and what each request already produced.
    /// </summary>
    /// <remarks>
    /// <b>The authority's own record.</b> A client quotes a <see cref="SessionId"/>; this is
    /// where it is looked up. Nothing is believed because it was sent -- a session that is not
    /// in here does not exist, whatever a request claims.
    ///
    /// <b>It is the idempotency boundary,</b> and it follows the rule Phase 13 established: a
    /// <see cref="RequestId"/> maps to at most one outcome, a repeat is handed the first
    /// answer, and a <em>rejected</em> request is deliberately not remembered so that
    /// re-sending it after the problem is fixed is re-judged rather than refused forever.
    ///
    /// <b>Rate limiting is a seam, not an implementation.</b> Attempts are counted against
    /// caller-supplied time and compared to authored limits. There is no production limiter
    /// here -- no sliding window, no distributed counter, no ban escalation -- and none is
    /// pretended; this is the shape a real one will replace.
    ///
    /// <b>In-memory here, tables later.</b> Phase 15 replaces this with <c>account_session</c>
    /// plus a unique index on the request key. Every caller is unchanged, because they all go
    /// through these methods.
    /// </remarks>
    public sealed class SessionDirectory : IRuntimeState
    {
        private readonly Dictionary<SessionId, AccountSessionState> _sessions =
            new Dictionary<SessionId, AccountSessionState>();

        private readonly Dictionary<AccountId, SessionId> _byAccount =
            new Dictionary<AccountId, SessionId>();

        private readonly Dictionary<RequestId, SessionResult> _sessionReplies =
            new Dictionary<RequestId, SessionResult>();

        private readonly Dictionary<RequestId, LoginResult> _loginReplies =
            new Dictionary<RequestId, LoginResult>();

        private readonly Dictionary<RequestId, EnterWorldResult> _entryReplies =
            new Dictionary<RequestId, EnterWorldResult>();

        private readonly List<AttemptRecord> _attempts = new List<AttemptRecord>();

        private Revision _revision;

        public Revision Revision => _revision;

        public int SessionCount => _sessions.Count;

        /// <summary>Finds a session by the identity a client quoted.</summary>
        public bool TryGet(SessionId session, out AccountSessionState state)
        {
            state = null;
            if (!session.IsValid) return false;

            return _sessions.TryGetValue(session, out state);
        }

        /// <summary>
        /// The session an account currently holds, if any.
        /// </summary>
        /// <remarks>What the concurrent-session rule is answered from. A terminal session is
        /// not a live one, so it does not count against a fresh sign-in.</remarks>
        public bool TryGetByAccount(AccountId account, out AccountSessionState state)
        {
            state = null;

            SessionId session;
            if (!_byAccount.TryGetValue(account, out session)) return false;

            return _sessions.TryGetValue(session, out state);
        }

        /// <summary>Whether an account already holds a usable session.</summary>
        public bool HasLiveSession(AccountId account, long nowTicks)
        {
            AccountSessionState existing;
            if (!TryGetByAccount(account, out existing)) return false;

            return existing.IsUsable(nowTicks);
        }

        /// <summary>Records a newly issued session.</summary>
        public bool Register(AccountSessionState session)
        {
            if (session == null || !session.SessionId.IsValid) return false;
            if (_sessions.ContainsKey(session.SessionId)) return false;

            _sessions[session.SessionId] = session;
            _byAccount[session.Account] = session.SessionId;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Ends a session and stops it counting against its account.</summary>
        public bool Revoke(SessionId session)
        {
            AccountSessionState state;
            if (!_sessions.TryGetValue(session, out state)) return false;

            if (!state.Revoke()) return false;

            _byAccount.Remove(state.Account);
            _revision = _revision.Next();
            return true;
        }

        // ---- idempotency ---------------------------------------------------------------

        public bool TryReplayLogin(RequestId request, out LoginResult result)
        {
            result = default;
            if (!request.IsValid) return false;

            return _loginReplies.TryGetValue(request, out result);
        }

        public bool TryReplaySession(RequestId request, out SessionResult result)
        {
            result = default;
            if (!request.IsValid) return false;

            return _sessionReplies.TryGetValue(request, out result);
        }

        public bool TryReplayEntry(RequestId request, out EnterWorldResult result)
        {
            result = default;
            if (!request.IsValid) return false;

            return _entryReplies.TryGetValue(request, out result);
        }

        /// <summary>
        /// Remembers what a request produced.
        /// </summary>
        /// <remarks>
        /// Only accepted outcomes are kept. A refusal wrote nothing, so re-sending it must be
        /// re-evaluated: the reason it failed -- a full server, a lapsed session, a stale
        /// build -- may no longer hold, and a player retrying after fixing it should succeed
        /// rather than be told forever what was once true.
        /// </remarks>
        public void RememberLogin(RequestId request, in LoginResult result)
        {
            if (!request.IsValid || !result.IsAccepted) return;

            _loginReplies[request] = result;
        }

        public void RememberSession(RequestId request, in SessionResult result)
        {
            if (!request.IsValid || !result.IsAccepted) return;

            _sessionReplies[request] = result;
        }

        public void RememberEntry(RequestId request, in EnterWorldResult result)
        {
            if (!request.IsValid || !result.IsAccepted) return;

            _entryReplies[request] = result;
        }

        // ---- rate limiting seam --------------------------------------------------------

        /// <summary>What kind of attempt is being counted.</summary>
        public enum AttemptKind
        {
            Login = 0,
            EnterWorld = 1
        }

        private readonly struct AttemptRecord
        {
            public AttemptRecord(AccountId account, AttemptKind kind, long ticks)
            {
                Account = account;
                Kind = kind;
                Ticks = ticks;
            }

            public AccountId Account { get; }

            public AttemptKind Kind { get; }

            public long Ticks { get; }
        }

        /// <summary>
        /// Records an attempt and reports whether it is within the allowance.
        /// </summary>
        /// <remarks>
        /// <b>A seam, and it says so.</b> Counting attempts inside a caller-supplied window is
        /// enough to express the rule and to test it; it is not a production limiter, which
        /// needs to survive a restart, span several servers and resist an attacker who simply
        /// waits. Phase 15 replaces the storage behind this and no caller changes.
        ///
        /// Returns false when the allowance is used up. The attempt is recorded either way, so
        /// hammering the endpoint does not reset the count.
        /// </remarks>
        public bool TryRecordAttempt(AccountId account, AttemptKind kind, long nowTicks,
            int allowance, long windowTicks)
        {
            _attempts.Add(new AttemptRecord(account, kind, nowTicks));

            if (allowance == int.MaxValue || allowance <= 0) return true;

            long since = windowTicks > 0 ? nowTicks - windowTicks : long.MinValue;
            int seen = 0;

            for (int i = 0; i < _attempts.Count; i++)
            {
                AttemptRecord record = _attempts[i];

                if (record.Kind != kind || record.Account != account) continue;
                if (windowTicks > 0 && record.Ticks < since) continue;

                seen++;
            }

            return seen <= allowance;
        }

        /// <summary>How many attempts of a kind an account has made inside a window.</summary>
        public int AttemptsWithin(AccountId account, AttemptKind kind, long nowTicks,
            long windowTicks)
        {
            long since = windowTicks > 0 ? nowTicks - windowTicks : long.MinValue;
            int seen = 0;

            for (int i = 0; i < _attempts.Count; i++)
            {
                AttemptRecord record = _attempts[i];

                if (record.Kind != kind || record.Account != account) continue;
                if (windowTicks > 0 && record.Ticks < since) continue;

                seen++;
            }

            return seen;
        }
    }
}
