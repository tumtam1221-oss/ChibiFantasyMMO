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

            return PartyPersistenceResult.Loaded(new PersistedParty(
                new PartyId(partyId),
                new CharacterId(json.String("leader_character_id")),
                PolicyOf(json.Int("loot_policy")),
                members,
                json.Int("revision")));
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
        /// The policy a stored number names.
        /// </summary>
        /// <remarks>A value outside the authored enum comes back as
        /// <see cref="PartyLootPolicy.Personal"/> and is the strictest of the three, so a
        /// row nobody understands cannot hand a party's loot to everyone. The server refuses
        /// to write such a value in the first place; this is the second half of that.</remarks>
        private static PartyLootPolicy PolicyOf(int stored)
        {
            switch (stored)
            {
                case (int)PartyLootPolicy.RoundRobin: return PartyLootPolicy.RoundRobin;
                case (int)PartyLootPolicy.NeedGreed: return PartyLootPolicy.NeedGreed;
                default: return PartyLootPolicy.Personal;
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
