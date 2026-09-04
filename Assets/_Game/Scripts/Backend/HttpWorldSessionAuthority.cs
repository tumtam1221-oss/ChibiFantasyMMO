using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// The world authority, answered by the PHP API.
    /// </summary>
    /// <remarks>
    /// <b>Why this is in Backend and not in Server.</b> A dedicated server must not depend
    /// on HTTP, PHP or SQL -- it asks <see cref="IWorldSessionAuthority"/> and is answered.
    /// This is the answering half, and it lives beside the only transport in the project.
    /// The server assembly names none of it; it is handed an interface at start-up.
    ///
    /// <b>It resolves, it does not believe.</b> <see cref="Admit"/> sends the client's token
    /// and reads back what the authority recorded. The claims on the request are compared
    /// against that answer and a disagreement is refused -- but no claim is ever used as a
    /// value. Delete every claim from the request and the admission is identical.
    ///
    /// <b>One transport per connection.</b> A world server handles many clients, each with
    /// its own token, so this holds no session of its own and presents whichever token it
    /// was given. That is the opposite of <see cref="HttpAccountApi"/>, which is one
    /// player's client and holds exactly one.
    /// </remarks>
    public sealed class HttpWorldSessionAuthority : IWorldSessionAuthority,
        HttpCharacterStateStore.ITokenSource
    {
        private readonly IHttpTransport _transport;

        public HttpWorldSessionAuthority(IHttpTransport transport)
        {
            _transport = transport;
        }

        /// <summary>
        /// The token last resolved by <see cref="Admit"/>, kept so the follow-up calls can
        /// present it.
        /// </summary>
        /// <remarks>
        /// A world server calls <see cref="ConfirmArrival"/> and <see cref="Release"/> with
        /// a session id, not a token, because a session id is what the rest of the server
        /// carries around. The token that proves the right to act on it is remembered here,
        /// keyed by session, and never leaves this object -- it is not in the admission, not
        /// in a spawn message and not in anything the world holds.
        /// </remarks>
        private readonly System.Collections.Generic.Dictionary<string, string> _tokens =
            new System.Collections.Generic.Dictionary<string, string>();

        public WorldAdmission Admit(WorldJoinClaim claim)
        {
            if (!claim.HasToken)
            {
                return WorldAdmission.Refused(SessionRejection.MissingContext);
            }

            HttpExchange exchange = _transport.Send("GET", "/api/session", null, claim.Token.Value);

            if (!exchange.Reached)
            {
                // The authority could not be asked. Refusing is the only safe answer: a
                // world server that admitted on an unanswered question would let anybody
                // in whenever the account service was down.
                return WorldAdmission.Refused(SessionRejection.MissingContext);
            }

            if (!exchange.IsSuccess)
            {
                return WorldAdmission.Refused(RejectionFor(exchange));
            }

            var json = JsonReader.Parse(exchange.Body);

            var session = new SessionId(json.String("session_id"));
            var account = new AccountId(json.String("account_id"));
            var character = new CharacterId(json.String("character_id"));
            var server = new ServerId(json.String("server_id"));
            var channel = new ChannelId(json.String("channel_id"));
            var state = (SessionState)json.Int("state");

            if (!session.IsValid || !account.IsValid)
            {
                return WorldAdmission.Refused(SessionRejection.SessionInvalid);
            }

            // Every claim, against what the authority actually said. A client that thinks
            // it is somebody else is told so rather than quietly becoming whoever its
            // token says -- which would be correct but bewildering.
            if (claim.ClaimedAccount.IsValid && claim.ClaimedAccount != account)
            {
                return WorldAdmission.Refused(SessionRejection.SessionInvalid);
            }

            if (claim.ClaimedCharacter.IsValid && claim.ClaimedCharacter != character)
            {
                return WorldAdmission.Refused(SessionRejection.CharacterNotOwned);
            }

            if (claim.ClaimedServer.IsValid && claim.ClaimedServer != server)
            {
                return WorldAdmission.Refused(SessionRejection.UnknownServer);
            }

            if (claim.ClaimedChannel.IsValid && claim.ClaimedChannel != channel)
            {
                return WorldAdmission.Refused(SessionRejection.ChannelServerMismatch);
            }

            if (!character.IsValid)
            {
                return WorldAdmission.Refused(SessionRejection.UnknownCharacter);
            }

            // Only a session the authority has already authorised may reach the world.
            // Anything earlier means the client skipped the selection flow.
            if (state != SessionState.EnteringWorld && state != SessionState.Active)
            {
                return WorldAdmission.Refused(SessionRejection.InvalidTransition);
            }

            _tokens[session.Value] = claim.Token.Value;

            return WorldAdmission.Admitted(
                session,
                account,
                character,
                server,
                channel,
                new DefinitionId(json.String("map_id")),
                new Revision(json.Int("revision")),
                new Revision(json.Int("character_revision")),
                state,

                // Read from the character row the API returned, so a world server places
                // what the database says rather than what a client hoped.
                new WorldCharacterProfile(
                    json.Int("level"),
                    json.Int("gender"),
                    new DefinitionId(json.String("class_id")),
                    new DefinitionId(json.String("job_id")),
                    new DefinitionId(json.String("appearance_id"))));
        }

        /// <summary>
        /// Lends the bearer for a session this server admitted.
        /// </summary>
        /// <remarks>
        /// The character state store needs a token and must not keep its own copy — a second
        /// place a secret lives is a second place it leaks. It asks here instead, and a
        /// session this server never admitted has no token to lend, so it cannot be written
        /// to.
        ///
        /// Deliberately not a public property returning the whole table. One session, one
        /// token, on request.
        /// </remarks>
        public bool TryGetToken(SessionId session, out string token)
        {
            token = null;

            return session.IsValid
                && !string.IsNullOrEmpty(session.Value)
                && _tokens.TryGetValue(session.Value, out token);
        }

        public bool ConfirmArrival(SessionId session)
        {
            return Post(session, "/api/session/world-ready");
        }

        public bool Release(SessionId session)
        {
            // An invalid session has no value to key on, and a dictionary lookup with a
            // null key throws. A disconnect path that throws is a server that leaks every
            // subsequent connection, so this is checked rather than assumed -- the callers
            // of a release are the ones least able to guarantee a well-formed argument.
            if (!session.IsValid) return false;

            bool released = Post(session, "/api/session/release");

            // The token is forgotten whatever happened. Holding one for a session this
            // server has finished with is only a way to leak it.
            _tokens.Remove(session.Value);

            return released;
        }

        private bool Post(SessionId session, string path)
        {
            if (!session.IsValid || string.IsNullOrEmpty(session.Value)) return false;

            if (!_tokens.TryGetValue(session.Value, out string token)) return false;

            var body = new JsonWriter().Add("request_id", RequestId.New().Value).ToJson();

            HttpExchange exchange = _transport.Send("POST", path, body, token);

            return exchange.IsSuccess;
        }

        /// <summary>
        /// Turns the authority's refusal into the vocabulary the rest of the project uses.
        /// </summary>
        /// <remarks>The body's <c>code</c> is preferred over the status, because the status
        /// says only which category the refusal falls into and the code says which refusal
        /// it was. The status is the fallback for a reply whose body could not be read.</remarks>
        private static SessionRejection RejectionFor(HttpExchange exchange)
        {
            string code = JsonReader.Parse(exchange.Body).String("code");

            switch (code)
            {
                case "session_expired": return SessionRejection.SessionExpired;
                case "session_revoked": return SessionRejection.SessionRevoked;
                case "session_invalid": return SessionRejection.SessionInvalid;
                case "missing_token": return SessionRejection.MissingContext;
                case "account_disabled":
                case "account_banned":
                case "account_suspended": return SessionRejection.AccountUnavailable;
                case "rate_limited": return SessionRejection.RateLimited;
            }

            if (exchange.Status == 401 || exchange.Status == 403)
            {
                return SessionRejection.SessionInvalid;
            }

            return SessionRejection.MissingContext;
        }
    }
}
