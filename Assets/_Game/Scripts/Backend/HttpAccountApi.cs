using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// The real <see cref="IAccountApi"/>, speaking to the PHP API over HTTP.
    /// </summary>
    /// <remarks>
    /// <b>This is the only place the wire format exists.</b> Phase 14 defined
    /// <see cref="IAccountApi"/> so that nothing above it would know about transport; this
    /// class is what finally implements it, and everything above -- the session flow, the
    /// controllers, the panels -- is unchanged by its arrival. That was the point of the
    /// seam.
    ///
    /// <b>It holds the session token, and nothing else does.</b> A token is handed to it by
    /// a successful login and attached to later requests. It is never written to view data,
    /// never logged, and never passed to the domain, which authorises by looking a session
    /// up rather than by inspecting a bearer value.
    ///
    /// <b>No credential is stored.</b> <see cref="Authenticate"/> takes the password as a
    /// parameter, puts it in one request body, and lets it go. There is no field for it.
    ///
    /// <b>JSON by hand, deliberately.</b> Unity's <c>JsonUtility</c> cannot deserialize a
    /// top-level array or a dictionary, and pulling in a third-party serializer for eight
    /// endpoints would add a dependency to the one assembly that talks to the outside
    /// world. The reader below is small, allocation-light and total: malformed input
    /// produces a missing value, never an exception that escapes.
    /// </remarks>
    public sealed class HttpAccountApi : IAccountApi
    {
        private readonly IHttpTransport _transport;

        public HttpAccountApi(IHttpTransport transport)
        {
            _transport = transport;
        }

        /// <summary>The bearer token from the last successful login, or null.</summary>
        /// <remarks>Held here so callers never have to, which is what keeps it out of view
        /// data and out of logs.</remarks>
        public string SessionToken { get; private set; }

        /// <summary>The session the last login issued.</summary>
        public SessionId Session { get; private set; }

        /// <summary>Forgets the token. Called on sign-out or when a session is refused.</summary>
        public void ClearSession()
        {
            SessionToken = null;
            Session = SessionId.None;
        }

        // ---- IAccountApi -----------------------------------------------------------

        /// <summary>
        /// Signs in.
        /// </summary>
        /// <remarks>
        /// The password is not on <see cref="LoginRequest"/> -- Phase 14 gave it nowhere to
        /// put one -- so it is supplied here, at the transport boundary, exactly as the
        /// contract intended. It goes into one request body and is not retained.
        /// </remarks>
        public ApiResult<AuthenticatedAccount> Authenticate(LoginRequest request)
        {
            return Authenticate(request, PendingLoginIdentifier, PendingPassword);
        }

        /// <summary>The identifier a panel collected, set immediately before a login call.</summary>
        /// <remarks>
        /// A property rather than a parameter because <see cref="IAccountApi"/> is a
        /// transport-neutral contract that must not grow a password argument. Setting these
        /// two, calling authenticate, and having them cleared is the handshake; they are
        /// wiped in a <c>finally</c> so a thrown exception cannot leave a password in
        /// memory on this object.
        /// </remarks>
        public string PendingLoginIdentifier { private get; set; }

        public string PendingPassword { private get; set; }

        private ApiResult<AuthenticatedAccount> Authenticate(LoginRequest request,
            string loginIdentifier, string password)
        {
            try
            {
                var body = new JsonWriter()
                    .Add("request_id", request.Request.Value)
                    .Add("login_identifier", loginIdentifier)
                    .Add("password", password)
                    .AddObject("versions", new JsonWriter()
                        .Add("client", request.Versions.Client.ToString())
                        .Add("protocol", request.Versions.Protocol.ToString())
                        .Add("content", request.Versions.Content.ToString()))
                    .ToJson();

                HttpExchange exchange = _transport.Send("POST", "/api/auth/login", body, null);

                if (!exchange.Reached)
                {
                    return ApiResult<AuthenticatedAccount>.Failed(MapFailure(exchange), exchange.Failure);
                }

                var json = JsonReader.Parse(exchange.Body);

                if (!exchange.IsSuccess)
                {
                    return ApiResult<AuthenticatedAccount>.Failed(
                        MapStatus(exchange.Status), json.String("code"));
                }

                SessionToken = json.String("token");
                Session = new SessionId(json.String("session_id"));

                var account = new AuthenticatedAccount(
                    new AccountId(json.String("account_id")),
                    json.String("display_name"),
                    AccountStatus.Active);

                return ApiResult<AuthenticatedAccount>.Ok(account);
            }
            finally
            {
                // The password does not outlive the call, whatever happened above.
                PendingPassword = null;
                PendingLoginIdentifier = null;
            }
        }

        public ApiResult<IReadOnlyList<ServerInfo>> GetServers(AccountId account)
        {
            HttpExchange exchange = _transport.Send("GET", "/api/servers", null, SessionToken);

            if (!exchange.Reached)
            {
                return ApiResult<IReadOnlyList<ServerInfo>>.Failed(MapFailure(exchange), exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return ApiResult<IReadOnlyList<ServerInfo>>.Failed(MapStatus(exchange.Status));
            }

            var servers = new List<ServerInfo>();

            foreach (JsonReader row in JsonReader.Parse(exchange.Body).Array("servers"))
            {
                servers.Add(new ServerInfo(
                    new ServerId(row.String("server_id")),
                    new LocalizationKey(row.String("name_key")),
                    row.String("region"),
                    (ServerStatus)row.Int("status"),
                    // Absent information stays absent: a server that reported no figure
                    // yields Unknown rather than a zero a player would read as empty.
                    row.Bool("population_known")
                        ? PopulationReading.Known(row.Int("population"), row.Int("capacity"))
                        : PopulationReading.Unknown(row.Int("capacity")),
                    default,
                    row.Bool("enabled"),
                    new Revision(row.Int("revision"))));
            }

            return ApiResult<IReadOnlyList<ServerInfo>>.Ok(servers);
        }

        public ApiResult<IReadOnlyList<ChannelInfo>> GetChannels(AccountId account, ServerId server)
        {
            HttpExchange exchange = _transport.Send("GET",
                "/api/channels?server_id=" + Escape(server.Value), null, SessionToken);

            if (!exchange.Reached)
            {
                return ApiResult<IReadOnlyList<ChannelInfo>>.Failed(MapFailure(exchange), exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return ApiResult<IReadOnlyList<ChannelInfo>>.Failed(MapStatus(exchange.Status));
            }

            var channels = new List<ChannelInfo>();

            foreach (JsonReader row in JsonReader.Parse(exchange.Body).Array("channels"))
            {
                channels.Add(new ChannelInfo(
                    new ChannelId(row.String("channel_id")),
                    new ServerId(row.String("server_id")),
                    new LocalizationKey(row.String("name_key")),
                    (ChannelStatus)row.Int("status"),
                    row.Bool("population_known")
                        ? PopulationReading.Known(row.Int("population"), row.Int("capacity"))
                        : PopulationReading.Unknown(row.Int("capacity")),
                    // Read from the server's answer and only displayed. The client cannot
                    // set it, and the server enforces PK from its own row regardless.
                    row.Bool("pk_enabled"),
                    row.Bool("enabled"),
                    new Revision(row.Int("revision"))));
            }

            return ApiResult<IReadOnlyList<ChannelInfo>>.Ok(channels);
        }

        public ApiResult<IReadOnlyList<CharacterSelectEntry>> GetCharacters(AccountId account,
            ServerId server)
        {
            HttpExchange exchange = _transport.Send("GET",
                "/api/characters?server_id=" + Escape(server.Value), null, SessionToken);

            if (!exchange.Reached)
            {
                return ApiResult<IReadOnlyList<CharacterSelectEntry>>.Failed(
                    MapFailure(exchange), exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return ApiResult<IReadOnlyList<CharacterSelectEntry>>.Failed(
                    MapStatus(exchange.Status));
            }

            var characters = new List<CharacterSelectEntry>();

            foreach (JsonReader row in JsonReader.Parse(exchange.Body).Array("characters"))
            {
                characters.Add(new CharacterSelectEntry(
                    new CharacterId(row.String("character_id")),
                    row.String("name"),
                    (CharacterGender)row.Int("gender"),
                    row.Int("level"),
                    new DefinitionId(row.String("class_id")),
                    new DefinitionId(row.String("job_id")),
                    new DefinitionId(row.String("map_id")),
                    new DefinitionId(row.String("appearance_id")),
                    (CharacterAvailability)row.Int("availability"),
                    0L,
                    new Revision(row.Int("revision"))));
            }

            return ApiResult<IReadOnlyList<CharacterSelectEntry>>.Ok(characters);
        }

        /// <summary>
        /// Whether the account still owns a character.
        /// </summary>
        /// <remarks>
        /// There is no dedicated endpoint: the server re-checks ownership inside every
        /// operation that needs it, which is the only place the answer can be trusted. This
        /// re-reads the list, so a client-side hint is at least based on the server's own
        /// filtered answer rather than on something the client remembered.
        /// </remarks>
        public ApiResult<bool> OwnsCharacter(AccountId account, CharacterId character)
        {
            ApiResult<IReadOnlyList<CharacterSelectEntry>> characters =
                GetCharacters(account, default);

            if (!characters.IsOk)
            {
                return ApiResult<bool>.Failed(characters.Error);
            }

            for (int i = 0; i < characters.Value.Count; i++)
            {
                if (characters.Value[i].Character == character) return ApiResult<bool>.Ok(true);
            }

            return ApiResult<bool>.Ok(false);
        }

        /// <summary>Asks the authority to hand the session to the world.</summary>
        public ApiResult<bool> NotifyWorldEntry(AccountId account, SessionId session,
            CharacterId character, ServerId server, ChannelId channel)
        {
            var body = new JsonWriter()
                .Add("request_id", RequestId.New().Value)
                .Add("account_id", account.Value)
                .Add("character_id", character.Value)
                .Add("server_id", server.Value)
                .Add("channel_id", channel.Value)
                .ToJson();

            HttpExchange exchange = _transport.Send("POST", "/api/session/enter-world", body,
                SessionToken);

            if (!exchange.Reached)
            {
                return ApiResult<bool>.Failed(MapFailure(exchange), exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return ApiResult<bool>.Failed(MapStatus(exchange.Status),
                    JsonReader.Parse(exchange.Body).String("code"));
            }

            return ApiResult<bool>.Ok(true);
        }

        // ---- selection ---------------------------------------------------------------

        /// <summary>Records a server choice with the authority.</summary>
        /// <remarks>Not part of <see cref="IAccountApi"/> because Phase 14's flow service
        /// owns selection; this is the transport call a client makes so the server's copy of
        /// the session agrees with the client's.</remarks>
        public ApiResult<bool> SelectServer(RequestId request, ServerId server)
        {
            return Select(request, "/api/session/select-server", "server_id", server.Value);
        }

        public ApiResult<bool> SelectChannel(RequestId request, ChannelId channel)
        {
            return Select(request, "/api/session/select-channel", "channel_id", channel.Value);
        }

        public ApiResult<bool> SelectCharacter(RequestId request, CharacterId character)
        {
            return Select(request, "/api/session/select-character", "character_id", character.Value);
        }

        /// <summary>
        /// Hands the session back to the authority and forgets it locally.
        /// </summary>
        /// <remarks>
        /// <b>Why a client needs this at all.</b> The authority refuses a second live
        /// session, so a player who closes the game without giving one up is locked out of
        /// their own account until it expires. This is how it is given up.
        ///
        /// <b>The local token is cleared whatever the server said.</b> If the call failed
        /// the session may or may not still exist server-side, but this client is done with
        /// it either way, and holding a token it will not use again is only a way to leak
        /// one. The authority is the thing that decides the session is over; this decides
        /// only that it has stopped presenting it.
        /// </remarks>
        public ApiResult<bool> ReleaseSession(RequestId request)
        {
            string token = SessionToken;

            try
            {
                if (string.IsNullOrEmpty(token)) return ApiResult<bool>.Ok(false);

                var body = new JsonWriter().Add("request_id", request.Value).ToJson();

                HttpExchange exchange = _transport.Send("POST", "/api/session/release", body,
                    token);

                if (!exchange.Reached)
                {
                    return ApiResult<bool>.Failed(MapFailure(exchange), exchange.Failure);
                }

                if (!exchange.IsSuccess)
                {
                    return ApiResult<bool>.Failed(MapStatus(exchange.Status),
                        JsonReader.Parse(exchange.Body).String("code"));
                }

                return ApiResult<bool>.Ok(JsonReader.Parse(exchange.Body).Bool("session_ended"));
            }
            finally
            {
                ClearSession();
            }
        }

        private ApiResult<bool> Select(RequestId request, string path, string field, string value)
        {
            var body = new JsonWriter()
                .Add("request_id", request.Value)
                .Add(field, value)
                .ToJson();

            HttpExchange exchange = _transport.Send("POST", path, body, SessionToken);

            if (!exchange.Reached)
            {
                return ApiResult<bool>.Failed(MapFailure(exchange), exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return ApiResult<bool>.Failed(MapStatus(exchange.Status),
                    JsonReader.Parse(exchange.Body).String("code"));
            }

            return ApiResult<bool>.Ok(true);
        }

        /// <summary>
        /// Turns an HTTP status into a transport-neutral failure kind.
        /// </summary>
        /// <remarks>
        /// The mapping is what keeps HTTP's vocabulary out of everything above:
        /// <see cref="ApiErrorKind"/> names what went wrong, not what a protocol called it.
        /// A 5xx is transient and worth retrying; a 4xx is not.
        /// </remarks>
        /// <summary>
        /// Turns a failed wire attempt into a transport-neutral failure kind.
        /// </summary>
        /// <remarks>
        /// The companion to <see cref="MapStatus"/>: that one reads a reply, this one reads
        /// the absence of a reply. Kept separate because "the server said 500" and "there was
        /// no server" are different facts, and a caller decides differently about them --
        /// a timeout is worth retrying immediately, a cancellation is not worth retrying at
        /// all, and telling them apart is why <see cref="TransportFailureKind"/> exists.
        /// </remarks>
        private static ApiErrorKind MapFailure(HttpExchange exchange)
        {
            switch (exchange.FailureKind)
            {
                case TransportFailureKind.Timeout: return ApiErrorKind.Timeout;
                case TransportFailureKind.Cancelled: return ApiErrorKind.Cancelled;
                default: return ApiErrorKind.Unreachable;
            }
        }

        private static ApiErrorKind MapStatus(int status)
        {
            if (status == 401) return ApiErrorKind.Unauthorized;
            if (status == 403) return ApiErrorKind.Unauthorized;
            if (status == 404) return ApiErrorKind.BadRequest;
            if (status == 409) return ApiErrorKind.BadRequest;
            if (status == 429) return ApiErrorKind.RateLimited;
            if (status == 400) return ApiErrorKind.BadRequest;
            if (status >= 500) return ApiErrorKind.ServerError;

            return ApiErrorKind.Unknown;
        }

        /// <summary>Percent-encodes a value for a query string.</summary>
        /// <remarks>Written out rather than using <c>UnityWebRequest.EscapeURL</c> so this
        /// class stays testable without the engine's networking stack loaded.</remarks>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                bool unreserved = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~';

                if (unreserved) builder.Append(c);
                else builder.Append('%').Append(((int)c).ToString("X2"));
            }

            return builder.ToString();
        }
    }
}
