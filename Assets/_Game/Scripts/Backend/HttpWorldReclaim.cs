using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Asks the PHP API to hand back what a dead world server left holding.
    /// </summary>
    /// <remarks>
    /// <b>The second write with no player behind it.</b> Like the calendar, this concerns
    /// nobody in particular and is made when the world is empty by definition, so there is
    /// no session token to borrow. It is authorised by the same deployment key, read from
    /// the same place -- the environment of the process that launched this server, and
    /// nowhere a scene, a prefab or a commit could carry it.
    ///
    /// <b>It matters more than the calendar does.</b> A stolen calendar key sets what time
    /// it is; a stolen key here throws a channel's worth of players out of the world. That
    /// is why there is no convenience path: no serialized field, no argument from a
    /// component, and no behaviour at all when the key is absent.
    ///
    /// <b>It is never logged.</b> There is no logging call in this file, which is the only
    /// reliable way to keep a credential out of a log.
    ///
    /// <b>Total.</b> An unreachable API, a refusal or a malformed body all report nothing
    /// found. The cost of that is a world that stays as stranded as it already was; the cost
    /// of throwing instead would be a server that will not start.
    /// </remarks>
    public sealed class HttpWorldReclaim : IWorldReclaim
    {
        private readonly IHttpTransport _transport;
        private readonly string _key;

        public HttpWorldReclaim(IHttpTransport transport, string key = null)
        {
            _transport = transport;

            // Supplied for a test; otherwise the environment, and nowhere else.
            _key = string.IsNullOrEmpty(key)
                ? System.Environment.GetEnvironmentVariable(HttpWorldClockStore.KeyVariable)
                : key;
        }

        /// <summary>Reports only whether a key is present, never any part of it.</summary>
        public bool CanReclaim => !string.IsNullOrEmpty(_key) && _transport != null;

        public WorldReclaimResult ReleaseStranded(ServerId server, ChannelId channel)
        {
            if (!CanReclaim || !server.IsValid || !channel.IsValid)
            {
                return WorldReclaimResult.Nothing;
            }

            string body = "{\"server_id\":\"" + Escaped(server.Value)
                + "\",\"channel_id\":\"" + Escaped(channel.Value) + "\"}";

            HttpExchange exchange = _transport.Send("POST", "/api/world/reclaim", body, _key);

            if (!exchange.Reached || !exchange.IsSuccess) return WorldReclaimResult.Nothing;

            var json = JsonReader.Parse(exchange.Body);

            return new WorldReclaimResult(
                json.Int("sessions_released"),
                json.Int("characters_released"));
        }

        private static string Escaped(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
