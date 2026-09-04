using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Character state, read from and written to the PHP API.
    /// </summary>
    /// <remarks>
    /// <b>It borrows the token the session authority already holds.</b> A world server
    /// carries session ids; the tokens that prove them live in
    /// <see cref="HttpWorldSessionAuthority"/> and nowhere else. Rather than keeping a second
    /// copy — a second place for a secret to leak — this asks for one when it needs it. A
    /// session this server never admitted has no token here, so it cannot be written to.
    ///
    /// <b>Failures are typed, not thrown.</b> A load happens during world entry, where an
    /// exception would leave a half-built character and a connection nobody disconnects.
    /// Every path returns a result.
    /// </remarks>
    public sealed class HttpCharacterStateStore : ICharacterStateStore
    {
        private readonly IHttpTransport _transport;
        private readonly ITokenSource _tokens;

        /// <summary>Supplies the bearer for a session this server admitted.</summary>
        /// <remarks>An interface rather than a direct reference to the authority, so the two
        /// can be tested apart and so nothing here can reach into the authority for anything
        /// except the one value it needs.</remarks>
        public interface ITokenSource
        {
            bool TryGetToken(SessionId session, out string token);
        }

        public HttpCharacterStateStore(IHttpTransport transport, ITokenSource tokens)
        {
            _transport = transport;
            _tokens = tokens;
        }

        public CharacterPersistenceResult Load(SessionId session)
        {
            if (!TryToken(session, out string token))
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned,
                    "no token for this session");
            }

            HttpExchange exchange = _transport.Send("GET", "/api/character/state", null, token);

            if (!exchange.Reached)
            {
                return CharacterPersistenceResult.Failed(
                    CharacterPersistenceFailure.Unreachable, exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return CharacterPersistenceResult.Failed(FailureFor(exchange));
            }

            var json = JsonReader.Parse(exchange.Body);

            var character = new CharacterId(json.String("character_id"));

            if (!character.IsValid)
            {
                // A 200 with no character is a malformed answer, not an empty one.
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.Corrupt,
                    "response named no character");
            }

            var stats = new List<PersistedStat>();

            foreach (JsonReader row in json.Array("stats"))
            {
                stats.Add(new PersistedStat(new DefinitionId(row.String("stat_id")),
                    row.Int("value")));
            }

            var appearance = new List<PersistedAppearance>();

            foreach (JsonReader row in json.Array("appearance"))
            {
                appearance.Add(new PersistedAppearance(row.Int("slot"),
                    new DefinitionId(row.String("option_id"))));
            }

            var skills = new List<PersistedSkill>();

            foreach (JsonReader row in json.Array("skills"))
            {
                skills.Add(new PersistedSkill(new DefinitionId(row.String("skill_id")),
                    row.Int("level")));
            }

            var persisted = new PersistedCharacter(
                character,
                new AccountId(json.String("account_id")),
                new ServerId(json.String("server_id")),
                json.String("name"),
                json.Int("gender"),
                json.Int("level"),
                json.Int("experience"),
                json.Int("current_health"),
                json.Int("current_mana"),
                new DefinitionId(json.String("class_id")),
                new DefinitionId(json.String("job_id")),
                new DefinitionId(json.String("map_id")),
                new DefinitionId(json.String("spawn_id")),
                stats,
                appearance,
                skills,
                SaveRevisionOf(json));

            return CharacterPersistenceResult.Loaded(persisted);
        }

        public CharacterPersistenceResult Save(SessionId session, PersistedCharacter character,
            int expectedSaveRevision)
        {
            if (character == null)
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.Corrupt,
                    "nothing to save");
            }

            if (!TryToken(session, out string token))
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned,
                    "no token for this session");
            }

            HttpExchange exchange = _transport.Send("POST", "/api/character/state",
                BodyFor(character, expectedSaveRevision), token);

            if (!exchange.Reached)
            {
                return CharacterPersistenceResult.Failed(
                    CharacterPersistenceFailure.Unreachable, exchange.Failure);
            }

            if (!exchange.IsSuccess)
            {
                return CharacterPersistenceResult.Failed(FailureFor(exchange));
            }

            return CharacterPersistenceResult.Saved(
                JsonReader.Parse(exchange.Body).Int("save_revision"));
        }

        /// <summary>
        /// Builds the save payload.
        /// </summary>
        /// <remarks>
        /// <b>No account id and no character id.</b> Both are the session's, resolved
        /// server-side. Including them would create fields a compromised client could edit,
        /// and the server would then have to check what it already knows.
        ///
        /// Written by hand for the same reason the rest of this assembly is: eight endpoints
        /// do not justify a serializer dependency in the one assembly that talks to the
        /// outside world.
        /// </remarks>
        private static string BodyFor(PersistedCharacter character, int expectedSaveRevision)
        {
            var state = new JsonWriter()
                .Add("level", character.Level)
                // Experience is a long in the domain and JSON has no integer type, so it
                // goes over as a decimal number like every other; PHP reads it as an int.
                .Add("experience", (int)character.Experience)
                .Add("current_health", character.CurrentHealth)
                .Add("current_mana", character.CurrentMana)
                .Add("class_id", character.Class.Value)
                .Add("job_id", character.Job.Value)
                .Add("map_id", character.Map.Value)
                .Add("spawn_id", character.Spawn.Value);

            var body = new System.Text.StringBuilder();

            body.Append("{\"request_id\":\"").Append(RequestId.New().Value).Append("\",");
            body.Append("\"save_revision\":").Append(expectedSaveRevision).Append(',');
            body.Append("\"state\":").Append(WithCollections(state, character));
            body.Append('}');

            return body.ToString();
        }

        /// <summary>
        /// Splices the three collections into the state object.
        /// </summary>
        /// <remarks><see cref="JsonWriter"/> writes flat objects and one nested object; it
        /// cannot express an array of objects, and teaching it to would be more machinery
        /// than three arrays justify. The arrays are appended textually, with every value
        /// escaped through the writer that produced the rest.</remarks>
        private static string WithCollections(JsonWriter state, PersistedCharacter character)
        {
            string flat = state.ToJson();
            var builder = new System.Text.StringBuilder(flat, flat.Length + 256);

            // Drop the closing brace so the arrays can be appended.
            builder.Length -= 1;

            builder.Append(",\"stats\":[");

            for (int i = 0; i < character.Stats.Count; i++)
            {
                if (i > 0) builder.Append(',');

                builder.Append(new JsonWriter()
                    .Add("stat_id", character.Stats[i].Stat.Value)
                    .Add("value", character.Stats[i].Value)
                    .ToJson());
            }

            builder.Append("],\"appearance\":[");

            for (int i = 0; i < character.Appearance.Count; i++)
            {
                if (i > 0) builder.Append(',');

                builder.Append(new JsonWriter()
                    .Add("slot", character.Appearance[i].Slot)
                    .Add("option_id", character.Appearance[i].Option.Value)
                    .ToJson());
            }

            builder.Append("],\"skills\":[");

            for (int i = 0; i < character.Skills.Count; i++)
            {
                if (i > 0) builder.Append(',');

                builder.Append(new JsonWriter()
                    .Add("skill_id", character.Skills[i].Skill.Value)
                    .Add("level", character.Skills[i].Level)
                    .ToJson());
            }

            builder.Append("]}");

            return builder.ToString();
        }

        private static int SaveRevisionOf(JsonReader json)
        {
            // The API nests these under "revisions"; the reader is a flat scanner, so the
            // save revision is read from the top level where the save response puts it, and
            // falls back to zero for a character that has never been written.
            return json.Int("save_revision");
        }

        private bool TryToken(SessionId session, out string token)
        {
            token = null;

            return session.IsValid && _tokens != null && _tokens.TryGetToken(session, out token)
                && !string.IsNullOrEmpty(token);
        }

        private static CharacterPersistenceFailure FailureFor(HttpExchange exchange)
        {
            switch (JsonReader.Parse(exchange.Body).String("code"))
            {
                case "character_not_owned": return CharacterPersistenceFailure.NotOwned;
                case "stale_revision": return CharacterPersistenceFailure.StaleRevision;
                case "invalid_transition": return CharacterPersistenceFailure.InvalidState;
                case "character_unavailable": return CharacterPersistenceFailure.InvalidState;
            }

            if (exchange.Status == 401 || exchange.Status == 403)
            {
                return CharacterPersistenceFailure.NotOwned;
            }

            return exchange.Status >= 500
                ? CharacterPersistenceFailure.Unreachable
                : CharacterPersistenceFailure.InvalidState;
        }
    }
}
