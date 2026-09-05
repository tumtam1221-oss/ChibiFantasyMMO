using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// A party, read from and written to the API the rest of persistence already uses.
    /// </summary>
    /// <remarks>
    /// <b>The session says who is asking.</b> Nothing in a request names a character or an
    /// account; the bearer token does, and the server scopes the party to it. A world
    /// server that could ask about an arbitrary character's party would be able to read a
    /// membership list on behalf of somebody who never asked.
    ///
    /// <b>The whole party goes out every time.</b> One shape for join, leave, kick and
    /// policy change, so there is no chance of two write paths disagreeing about what a
    /// party is. An empty member list is how a party ends.
    ///
    /// <b>No credential lives here.</b> Same rule as every other store in this assembly:
    /// the token arrives from the session, and this file contains no address, no password
    /// and no key.
    /// </remarks>
    public sealed class HttpPartyStateStore : IPartyStateStore
    {
        private readonly IHttpTransport _transport;
        private readonly HttpCharacterStateStore.ITokenSource _tokens;

        public HttpPartyStateStore(IHttpTransport transport,
            HttpCharacterStateStore.ITokenSource tokens)
        {
            _transport = transport;
            _tokens = tokens;
        }

        public PartyPersistenceResult Load(SessionId session)
        {
            if (!TryToken(session, out string token))
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.NotAMember,
                    "no token for this session");
            }

            HttpExchange exchange = _transport.Send("GET", "/api/party", null, token);

            if (!exchange.Reached)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.Unreachable,
                    exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return PartyPersistenceResult.Failed(FailureFor(exchange));
            }

            JsonReader json = JsonReader.Parse(exchange.Body);

            string partyId = json.String("party_id");

            // An empty id is the API's way of saying "this character is in no party",
            // which is an answer rather than a failure.
            if (string.IsNullOrEmpty(partyId)) return PartyPersistenceResult.None();

            var members = new List<CharacterId>();

            foreach (JsonReader row in json.Array("members"))
            {
                string id = row.String("character_id");

                if (!string.IsNullOrEmpty(id)) members.Add(new CharacterId(id));
            }

            if (!TryPolicy(json.Int("loot_policy"), out PartyLootPolicy policy))
            {
                // Not folded into Personal, not into anything else. A party whose policy
                // nobody authored would loot by a rule its members never chose, and
                // picking one here would make that look like a successful restore.
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.Corrupt,
                    "stored loot policy is not an authored value");
            }

            var stored = new PersistedParty(
                new PartyId(partyId),
                new CharacterId(json.String("leader_character_id")),
                policy,
                members,
                json.Int("revision"),
                json.Int("round_robin_cursor"));

            if (!stored.IsCursorValid)
            {
                // Same rule as the policy, for the same reason: a cursor past the end of
                // the party is not a number to take modulo, it is a row an operator needs
                // to look at. Silently wrapping it would give somebody a turn that
                // belonged to a member who may not even be in the party any more.
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.Corrupt,
                    "stored round-robin cursor addresses no member");
            }

            return PartyPersistenceResult.Loaded(stored);
        }

        public PartyPersistenceResult Save(SessionId session, PersistedParty party)
        {
            if (!TryToken(session, out string token))
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.NotAMember,
                    "no token for this session");
            }

            if (!party.Party.IsValid)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.InvalidParty,
                    "no party id");
            }

            var body = new System.Text.StringBuilder();

            body.Append("{\"request_id\":\"").Append(RequestId.New().Value).Append("\",");
            body.Append("\"party_id\":\"").Append(party.Party.Value).Append("\",");
            body.Append("\"leader_character_id\":\"")
                .Append(party.Leader.Value ?? string.Empty).Append("\",");
            body.Append("\"loot_policy\":").Append((int)party.LootPolicy).Append(',');
            body.Append("\"round_robin_cursor\":").Append(party.Cursor).Append(',');
            body.Append("\"revision\":").Append(party.Revision).Append(',');
            body.Append("\"members\":[");

            for (var i = 0; i < party.Members.Count; i++)
            {
                if (i > 0) body.Append(',');

                body.Append('"').Append(party.Members[i].Value).Append('"');
            }

            body.Append("]}");

            HttpExchange exchange = _transport.Send("POST", "/api/party", body.ToString(),
                token);

            if (!exchange.Reached)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.Unreachable,
                    exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return PartyPersistenceResult.Failed(FailureFor(exchange));
            }

            return PartyPersistenceResult.Saved(JsonReader.Parse(exchange.Body).Int("revision"));
        }

        /// <summary>
        /// The policy a stored number names, or false when it names none of them.
        /// </summary>
        /// <remarks>
        /// <b>There is no default case.</b> An earlier version of this substituted
        /// <see cref="PartyLootPolicy.Personal"/> on the grounds that it is the strictest
        /// of the three, which was wrong twice over: strictness is not what a party asked
        /// for, and a substitution that reports success gives a corrupt row no way of ever
        /// being noticed -- the world would loot happily by a rule nobody chose and the
        /// next write would stamp the substituted value over the evidence.
        ///
        /// The server refuses to write an unauthored policy in the first place. This is the
        /// half that refuses to read one back.
        /// </remarks>
        private static bool TryPolicy(int stored, out PartyLootPolicy policy)
        {
            switch (stored)
            {
                case (int)PartyLootPolicy.Personal:
                    policy = PartyLootPolicy.Personal;
                    return true;
                case (int)PartyLootPolicy.RoundRobin:
                    policy = PartyLootPolicy.RoundRobin;
                    return true;
                case (int)PartyLootPolicy.NeedGreed:
                    policy = PartyLootPolicy.NeedGreed;
                    return true;
                default:
                    policy = default;
                    return false;
            }
        }

        /// <summary>What went wrong, in terms the world can act on.</summary>
        /// <remarks>Mapped from the status alone. The server's problem document is not
        /// forwarded, because it is written for an operator and a world has nothing to do
        /// with its wording.</remarks>
        private static PartyPersistenceFailure FailureFor(in HttpExchange exchange)
        {
            switch (exchange.Status)
            {
                case 403: return PartyPersistenceFailure.NotAMember;
                case 409: return PartyPersistenceFailure.StaleRevision;

                // A refusal of the party itself: an unauthored policy, a turn that
                // addresses no member, a leader who is not one. Distinguished from
                // Unreachable because re-sending the same body cannot help, and a world
                // that read it as a network blip would re-send it forever.
                case 400:
                case 422: return PartyPersistenceFailure.InvalidParty;
                default: return PartyPersistenceFailure.Unreachable;
            }
        }

        private bool TryToken(SessionId session, out string token)
        {
            token = null;

            return _tokens != null && _tokens.TryGetToken(session, out token)
                && !string.IsNullOrEmpty(token);
        }
    }
}
