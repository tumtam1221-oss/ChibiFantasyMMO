using System.Collections.Generic;
using System.Text;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// A decided monster defeat, written to and read from the API the rest of persistence
    /// already uses.
    /// </summary>
    /// <remarks>
    /// <b>The session says which world is asking.</b> Nothing in a request names a server or
    /// a channel; the bearer token does, and the backend scopes every reward to it. A world
    /// that could name its own scope could hand another channel's pending rewards to itself
    /// and pay them twice.
    ///
    /// <b>No credential lives here.</b> Same rule as every other store in this assembly: the
    /// token arrives from the session, and this file contains no address, no key and no
    /// secret of any kind.
    /// </remarks>
    public sealed class HttpMonsterRewardOutbox : IMonsterRewardOutbox
    {
        private readonly IHttpTransport _transport;
        private readonly HttpCharacterStateStore.ITokenSource _tokens;

        public HttpMonsterRewardOutbox(IHttpTransport transport,
            HttpCharacterStateStore.ITokenSource tokens)
        {
            _transport = transport;
            _tokens = tokens;
        }

        public MonsterRewardOutboxResult Record(SessionId session,
            PersistedMonsterReward reward)
        {
            if (!TryToken(session, out string token))
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no token for this session");
            }

            if (!reward.Defeat.IsValid || string.IsNullOrEmpty(reward.RewardId))
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.InvalidReward, "no defeat to record");
            }

            var body = new StringBuilder();

            body.Append("{\"request_id\":\"").Append(RequestId.New().Value).Append("\",");
            Text(body, "reward_id", reward.RewardId);
            Text(body, "defeat_id", reward.Defeat.Value);
            Text(body, "monster_definition_id", reward.Monster.Value);
            Text(body, "map_definition_id", reward.Map.Value);
            Text(body, "killer_character_id", reward.Killer.Value);
            Text(body, "loot_id", reward.Loot.IsValid ? reward.Loot.Value : string.Empty);
            body.Append("\"loot_policy\":").Append(reward.LootPolicy).Append(',');
            Text(body, "claimant_character_id", reward.Claimant.Value);
            Number(body, "position_x", reward.X);
            Number(body, "position_y", reward.Y);
            Number(body, "position_z", reward.Z);
            Text(body, "party_id", reward.Party.IsValid ? reward.Party.Value : string.Empty);

            // Sent only when this defeat actually owes a rotation something. Absent and
            // zero are different answers, and zero is the first member's turn.
            if (reward.HasCursor)
            {
                body.Append("\"party_cursor\":").Append(reward.Cursor).Append(',');
            }

            body.Append("\"experience\":[");

            for (var i = 0; i < reward.Experience.Count; i++)
            {
                if (i > 0) body.Append(',');

                MonsterRewardGrant grant = reward.Experience[i];

                body.Append("{\"character_id\":\"").Append(Escaped(grant.Character.Value))
                    .Append("\",\"experience\":").Append(grant.Experience).Append('}');
            }

            body.Append("],\"pet_experience\":[");

            for (var i = 0; i < reward.PetExperience.Count; i++)
            {
                if (i > 0) body.Append(',');

                MonsterRewardPetGrant grant = reward.PetExperience[i];

                body.Append("{\"character_id\":\"").Append(Escaped(grant.Owner.Value))
                    .Append("\",\"pet_instance_id\":\"").Append(Escaped(grant.Pet.Value))
                    .Append("\",\"experience\":").Append(grant.Experience).Append('}');
            }

            body.Append("],\"loot\":[");

            for (var i = 0; i < reward.Entries.Count; i++)
            {
                if (i > 0) body.Append(',');

                MonsterRewardLootEntry entry = reward.Entries[i];

                body.Append("{\"item_definition_id\":\"").Append(Escaped(entry.Item.Value))
                    .Append("\",\"quantity\":").Append(entry.Quantity)
                    .Append(",\"rarity_definition_id\":\"")
                    .Append(Escaped(entry.Rarity.IsValid ? entry.Rarity.Value : string.Empty))
                    .Append("\",\"item_instance_id\":\"")
                    .Append(Escaped(entry.Instance.Value ?? string.Empty))
                    .Append("\"}");
            }

            body.Append("]}");

            HttpExchange exchange = _transport.Send("POST", "/api/world/monster-reward",
                body.ToString(), token);

            if (!exchange.Reached)
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.Unreachable, exchange.Failure);
            }

            if (!exchange.IsSuccess) return MonsterRewardOutboxResult.Failed(FailureFor(exchange));

            JsonReader json = JsonReader.Parse(exchange.Body);

            return MonsterRewardOutboxResult.Recorded(json.String("reward_id"),
                json.Int("revision"), json.Bool("existing"));
        }

        public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
        {
            var pending = new List<PersistedMonsterReward>();

            if (!TryToken(session, out string token)) return pending;

            HttpExchange exchange = _transport.Send("GET", "/api/world/monster-rewards",
                null, token);

            if (!exchange.Reached || !exchange.IsSuccess) return pending;

            foreach (JsonReader row in JsonReader.Parse(exchange.Body).Array("rewards"))
            {
                PersistedMonsterReward reward = RewardFrom(row);

                if (reward.Exists) pending.Add(reward);
            }

            return pending;
        }

        public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
            int revision, IReadOnlyList<CharacterId> experienceDelivered,
            IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
            bool? cursorCommitted, bool? lootPublished, bool complete,
            IReadOnlyList<InstanceId> petExperienceDelivered = null)
        {
            if (!TryToken(session, out string token))
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no token for this session");
            }

            if (string.IsNullOrEmpty(rewardId))
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no reward named");
            }

            var body = new StringBuilder();

            body.Append("{\"request_id\":\"").Append(RequestId.New().Value).Append("\",");
            Text(body, "reward_id", rewardId);
            body.Append("\"revision\":").Append(revision).Append(',');
            body.Append("\"complete\":").Append(complete ? 1 : 0).Append(',');

            if (cursorCommitted.HasValue)
            {
                body.Append("\"cursor_committed\":")
                    .Append(cursorCommitted.Value ? 1 : 0).Append(',');
            }

            if (lootPublished.HasValue)
            {
                body.Append("\"loot_published\":")
                    .Append(lootPublished.Value ? 1 : 0).Append(',');
            }

            body.Append("\"experience_delivered\":[");

            for (var i = 0; experienceDelivered != null && i < experienceDelivered.Count; i++)
            {
                if (i > 0) body.Append(',');

                body.Append('"').Append(Escaped(experienceDelivered[i].Value)).Append('"');
            }

            body.Append("],\"pet_experience_delivered\":[");

            for (var i = 0;
                petExperienceDelivered != null && i < petExperienceDelivered.Count; i++)
            {
                if (i > 0) body.Append(',');

                body.Append('"').Append(Escaped(petExperienceDelivered[i].Value)).Append('"');
            }

            body.Append("],\"loot_claimed\":[");

            for (var i = 0; lootClaimed != null && i < lootClaimed.Count; i++)
            {
                if (i > 0) body.Append(',');

                body.Append("{\"entry_index\":").Append(lootClaimed[i].Index)
                    .Append(",\"character_id\":\"")
                    .Append(Escaped(lootClaimed[i].ClaimedBy.Value)).Append("\"}");
            }

            body.Append("]}");

            HttpExchange exchange = _transport.Send("POST",
                "/api/world/monster-reward/progress", body.ToString(), token);

            if (!exchange.Reached)
            {
                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.Unreachable, exchange.Failure);
            }

            if (!exchange.IsSuccess) return MonsterRewardOutboxResult.Failed(FailureFor(exchange));

            JsonReader json = JsonReader.Parse(exchange.Body);

            return MonsterRewardOutboxResult.Recorded(json.String("reward_id"),
                json.Int("revision"), false);
        }

        /// <summary>One stored reward, exactly as the backend described it.</summary>
        private static PersistedMonsterReward RewardFrom(JsonReader row)
        {
            var experience = new List<MonsterRewardGrant>();

            foreach (JsonReader grant in row.Array("experience"))
            {
                experience.Add(new MonsterRewardGrant(
                    new CharacterId(grant.String("character_id")),
                    grant.Int("experience"),
                    grant.Bool("delivered")));
            }

            var pets = new List<MonsterRewardPetGrant>();

            foreach (JsonReader grant in row.Array("pet_experience"))
            {
                pets.Add(new MonsterRewardPetGrant(
                    new CharacterId(grant.String("character_id")),
                    new InstanceId(grant.String("pet_instance_id")),
                    grant.Int("experience"),
                    grant.Bool("delivered")));
            }

            var entries = new List<MonsterRewardLootEntry>();

            foreach (JsonReader entry in row.Array("loot"))
            {
                string rarity = entry.String("rarity_definition_id");

                string identity = entry.String("item_instance_id");

                entries.Add(new MonsterRewardLootEntry(
                    entry.Int("entry_index"),
                    new DefinitionId(entry.String("item_definition_id")),
                    entry.Int("quantity"),
                    string.IsNullOrEmpty(rarity) ? default : new DefinitionId(rarity),
                    entry.Bool("claimed"),
                    new CharacterId(entry.String("claimed_by")),
                    string.IsNullOrEmpty(identity) ? default : new InstanceId(identity)));
            }

            string loot = row.String("loot_id");
            string party = row.String("party_id");

            // Raw rather than a typed reader: an absent key and an explicit null both come
            // back empty, and that is exactly the question being asked -- does this defeat
            // owe a rotation anything at all? A cursor of zero is a real turn.
            bool hasCursor = row.Raw("party_cursor").Length > 0;

            return new PersistedMonsterReward(
                row.String("reward_id"),
                new InstanceId(row.String("defeat_id")),
                new DefinitionId(row.String("monster_definition_id")),
                new DefinitionId(row.String("map_definition_id")),
                new CharacterId(row.String("killer_character_id")),
                string.IsNullOrEmpty(loot) ? default : new InstanceId(loot),
                row.Int("loot_policy"),
                new CharacterId(row.String("claimant_character_id")),
                Decimal(row, "position_x"), Decimal(row, "position_y"),
                Decimal(row, "position_z"),
                string.IsNullOrEmpty(party) ? default : new PartyId(party),
                row.Int("party_cursor"), hasCursor,
                experience, entries,
                row.Bool("cursor_committed"), row.Bool("loot_published"),
                row.Int("state") == 1, row.Int("revision"), pets);
        }

        /// <summary>
        /// A decimal from the payload, parsed in the invariant culture.
        /// </summary>
        /// <remarks>The reader deliberately hands decimals back as raw tokens rather than
        /// holding an opinion about locale, so a position is parsed here -- and always with
        /// the invariant culture, or a server running under a comma-decimal locale would
        /// read a coordinate of 1.5 as 15.</remarks>
        private static float Decimal(JsonReader row, string key)
        {
            string raw = row.Raw(key);

            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : 0f;
        }

        /// <summary>What went wrong, in terms a world can act on.</summary>
        /// <remarks>Read from the status plus the problem document's stable <c>code</c>,
        /// never its prose: a conflict that means "already paid" and one that means "you
        /// lost a race" need different answers, and only the code separates them.</remarks>
        private static MonsterRewardOutboxFailure FailureFor(in HttpExchange exchange)
        {
            string code = string.IsNullOrEmpty(exchange.Body)
                ? string.Empty
                : JsonReader.Parse(exchange.Body).String("code") ?? string.Empty;

            switch (code)
            {
                case "already_complete": return MonsterRewardOutboxFailure.AlreadyComplete;
                case "stale_revision": return MonsterRewardOutboxFailure.StaleRevision;
                case "unknown_reward": return MonsterRewardOutboxFailure.UnknownReward;
                case "not_this_world": return MonsterRewardOutboxFailure.UnknownReward;
            }

            switch (exchange.Status)
            {
                case 400:
                case 422: return MonsterRewardOutboxFailure.InvalidReward;
                case 403:
                case 404: return MonsterRewardOutboxFailure.UnknownReward;
                case 409: return MonsterRewardOutboxFailure.StaleRevision;
                default: return MonsterRewardOutboxFailure.Unreachable;
            }
        }

        private static void Text(StringBuilder body, string field, string value)
        {
            body.Append('"').Append(field).Append("\":\"")
                .Append(Escaped(value ?? string.Empty)).Append("\",");
        }

        private static void Number(StringBuilder body, string field, float value)
        {
            body.Append('"').Append(field).Append("\":")
                .Append(value.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append(',');
        }

        /// <summary>
        /// A string that cannot break out of the value it is written into.
        /// </summary>
        /// <remarks>Ids here are server-side and well formed, but this body is assembled by
        /// hand and a quote arriving in one would change the shape of the request rather
        /// than its content. Escaping is cheaper than trusting.</remarks>
        private static string Escaped(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private bool TryToken(SessionId session, out string token)
        {
            token = null;

            return _tokens != null && _tokens.TryGetToken(session, out token)
                && !string.IsNullOrEmpty(token);
        }
    }
}
