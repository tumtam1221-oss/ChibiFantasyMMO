using System.Globalization;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// The world's calendar, read from and written to the PHP API.
    /// </summary>
    /// <remarks>
    /// <b>This is the one write with no player behind it.</b> Every other write in this
    /// assembly borrows the session token of the player it concerns. The world's calendar
    /// concerns nobody in particular and is most often written when the server is shutting
    /// down with nobody online at all, so there is no session to borrow. It is authorised
    /// instead by a key that belongs to the deployment.
    ///
    /// <b>The key can only come from the environment.</b> Not a serialized field, not a
    /// constant, not an argument to a component -- because all three can be saved into a
    /// scene or a prefab and committed by somebody who was not thinking about it. Read at
    /// runtime from the process that launched this server, it has nowhere to leak to. A build
    /// handed to a player contains no key and this class simply does not write.
    ///
    /// <b>It is never logged.</b> There is no logging call in this file, which is the only
    /// reliable way to keep a credential out of a log file.
    ///
    /// <b>Total.</b> An unreachable API, a refusal or a malformed body all produce null or
    /// false, and the world carries on. Losing a saved calendar costs the last few minutes
    /// on the next restart; refusing to run costs the whole server.
    /// </remarks>
    public sealed class HttpWorldClockStore : IWorldClockStore
    {
        /// <summary>The environment variable a deployment supplies the key in.</summary>
        public const string KeyVariable = "CHIBI_WORLD_SERVER_KEY";

        private readonly IHttpTransport _transport;
        private readonly string _key;

        public HttpWorldClockStore(IHttpTransport transport, string key = null)
        {
            _transport = transport;

            // Supplied for a test; otherwise the environment, and nowhere else.
            _key = string.IsNullOrEmpty(key)
                ? System.Environment.GetEnvironmentVariable(KeyVariable)
                : key;
        }

        /// <summary>
        /// Whether this process can write the calendar at all.
        /// </summary>
        /// <remarks>Exposed so a server can say once at startup whether its world will be
        /// remembered, rather than leaving an operator to discover it after a restart lost a
        /// day. Reports only whether a key is present, never any part of it.</remarks>
        public bool CanSave => !string.IsNullOrEmpty(_key) && _transport != null;

        public WorldClockState? Load(ServerId server, ChannelId channel)
        {
            if (_transport == null || !server.IsValid || !channel.IsValid) return null;

            HttpExchange exchange = _transport.Send("GET",
                "/api/world/clock?server_id=" + Escape(server.Value)
                    + "&channel_id=" + Escape(channel.Value),
                null, null);

            if (!exchange.Reached || !exchange.IsSuccess) return null;

            var json = JsonReader.Parse(exchange.Body);

            if (!json.Bool("saved")) return null;

            var state = new WorldClockState(
                Number(json, "elapsed_seconds"),
                Number(json, "seconds_per_day"));

            // A stored clock that is not a usable measurement is no better than no clock.
            return state.IsUsable ? state : (WorldClockState?)null;
        }

        public bool Save(ServerId server, ChannelId channel, WorldClockState state)
        {
            if (!CanSave || !server.IsValid || !channel.IsValid) return false;

            if (!state.IsUsable) return false;

            string body = "{\"server_id\":\"" + Escaped(server.Value)
                + "\",\"channel_id\":\"" + Escaped(channel.Value)
                + "\",\"elapsed_seconds\":" + Decimal(state.ElapsedSeconds)
                + ",\"seconds_per_day\":" + Decimal(state.SecondsPerDay)
                + "}";

            HttpExchange exchange = _transport.Send("POST", "/api/world/clock", body, _key);

            return exchange.Reached && exchange.IsSuccess;
        }

        /// <summary>
        /// A number the API will read back as the same number.
        /// </summary>
        /// <remarks>Invariant culture, because on a machine set to a comma locale the default
        /// formatting writes 1.5 as "1,5" -- which lands in the JSON body as two values and
        /// either fails to parse or, worse, parses as something else.</remarks>
        private static string Decimal(double value)
            => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads a decimal the deliberately small scanner only understands as an integer.
        /// </summary>
        /// <remarks>Same reason as <c>HttpMonsterSpawnConfigurationSource</c>: JsonReader was
        /// built for ids and counts, and the raw token is parsed here rather than teaching it
        /// a new type.</remarks>
        private static double Number(in JsonReader json, string key)
        {
            string raw = json.Raw(key);

            if (string.IsNullOrEmpty(raw)) return -1.0;

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value)
                ? value
                : -1.0;
        }

        private static string Escaped(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

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
