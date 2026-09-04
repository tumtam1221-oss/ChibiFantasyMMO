using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Reads a map's monster configuration from the PHP API.
    /// </summary>
    /// <remarks>
    /// <b>Read-only, and unauthenticated on purpose.</b> A world server has no player
    /// session and no credential of its own -- Phase 16 kept every secret out of Unity --
    /// so it cannot present a bearer for its own startup read. The alternative was to ship
    /// a server credential inside a build, which puts a credential in every player's hands.
    /// Spawn configuration is level design rather than a secret: a player learns where the
    /// monsters are by walking the map. Nothing here carries account data, character data
    /// or a token, and there is no write path.
    ///
    /// <b>Total.</b> An unreachable API, a malformed body or a missing map all produce
    /// null, and the caller leaves the runtime as it was. A world that failed to reload its
    /// configuration should keep the monsters it has, not empty itself.
    ///
    /// <b>It validates nothing.</b> Whether a row may become a live nest is
    /// <c>SpawnConfigurationValidator</c>'s question, asked against the loaded content
    /// registries that this assembly deliberately cannot see.
    /// </remarks>
    public sealed class HttpMonsterSpawnConfigurationSource : IMonsterSpawnConfigurationSource
    {
        private readonly IHttpTransport _transport;

        public HttpMonsterSpawnConfigurationSource(IHttpTransport transport)
        {
            _transport = transport;
        }

        /// <summary>Rows the API itself refused, for an operator's log.</summary>
        /// <remarks>Reported rather than dropped: a misconfigured nest should appear
        /// somewhere, not silently fail to populate.</remarks>
        public IReadOnlyList<string> LastRejected { get; private set; } =
            System.Array.Empty<string>();

        public MapSpawnConfiguration Load(DefinitionId map)
        {
            LastRejected = System.Array.Empty<string>();

            if (!map.IsValid || _transport == null) return null;

            HttpExchange exchange = _transport.Send("GET",
                "/api/world/spawn-configuration?map_id=" + Escape(map.Value), null, null);

            if (!exchange.Reached || !exchange.IsSuccess) return null;

            var json = JsonReader.Parse(exchange.Body);

            if (!string.Equals(json.String("map_id"), map.Value, System.StringComparison.Ordinal))
            {
                // The answer is about a different map. Refusing beats applying somebody
                // else's nests to this one.
                return null;
            }

            var points = new List<MonsterSpawnConfiguration>();

            foreach (JsonReader row in json.Array("spawn_points"))
            {
                points.Add(new MonsterSpawnConfiguration(
                    row.String("spawn_point_id"),
                    new DefinitionId(row.String("map_definition_id")),
                    new DefinitionId(row.String("monster_definition_id")),
                    Number(row, "position_x"),
                    Number(row, "position_y"),
                    Number(row, "position_z"),
                    Number(row, "spawn_radius"),
                    row.Int("initial_spawn_count"),
                    row.Int("max_alive"),
                    Number(row, "respawn_seconds"),
                    row.String("spawn_group_id")));
            }

            var ai = new List<MonsterAiConfiguration>();

            foreach (JsonReader row in json.Array("ai_configurations"))
            {
                ai.Add(new MonsterAiConfiguration(
                    new DefinitionId(row.String("monster_definition_id")),
                    row.Int("aggression_type"),
                    Number(row, "detection_range", -1f),
                    Number(row, "chase_range", -1f),
                    Number(row, "attack_range", -1f),
                    Number(row, "attack_cooldown", -1f),
                    Number(row, "move_speed", -1f)));
            }

            LastRejected = RejectionsIn(json);

            return new MapSpawnConfiguration(map, points, ai);
        }

        /// <summary>
        /// Reads a decimal number the flat scanner only understands as an integer.
        /// </summary>
        /// <remarks>
        /// <c>JsonReader</c> was built in Phase 15 for a payload of ids and counts and reads
        /// integers only. Positions and delays are decimals, so the raw token is located and
        /// parsed here rather than teaching a deliberately small scanner a new type. Invariant
        /// culture, because a comma locale would read 1.5 as 15.
        /// </remarks>
        private static float Number(in JsonReader row, string key, float fallback = 0f)
        {
            string raw = row.Raw(key);

            if (string.IsNullOrEmpty(raw)) return fallback;

            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static IReadOnlyList<string> RejectionsIn(in JsonReader json)
        {
            var rejected = new List<string>();

            foreach (JsonReader row in json.Array("rejected_spawn_points"))
            {
                rejected.Add(row.String("spawn_point_id") + ": " + row.String("reason"));
            }

            foreach (JsonReader row in json.Array("rejected_ai_configurations"))
            {
                rejected.Add(row.String("monster_definition_id") + ": " + row.String("reason"));
            }

            return rejected;
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
