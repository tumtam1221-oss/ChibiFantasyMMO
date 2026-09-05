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

            var items = new List<PersistedItem>();

            foreach (JsonReader row in json.Array("items"))
            {
                var enchants = new List<PersistedEnchant>();

                foreach (JsonReader stone in row.Array("enchants"))
                {
                    enchants.Add(new PersistedEnchant(
                        new DefinitionId(stone.String("stone_id")),
                        stone.Int("socket"),
                        stone.Int("rank")));
                }

                var cards = new List<PersistedCard>();

                foreach (JsonReader card in row.Array("cards"))
                {
                    cards.Add(new PersistedCard(
                        new DefinitionId(card.String("card_id")),
                        card.Int("socket"),
                        new InstanceId(card.String("card_instance_id"))));
                }

                items.Add(new PersistedItem(
                    new InstanceId(row.String("instance_id")),
                    new DefinitionId(row.String("item_id")),
                    row.Int("quantity"),
                    // A worn piece has no container slot. The API sends -1 for it, and
                    // JsonReader reads a negative number, so the two agree.
                    row.Int("slot"),
                    row.Int("lock_state"),
                    row.Int("equipment_slot"),
                    row.Int("enhancement_level"),
                    new DefinitionId(row.String("rarity_id")),
                    enchants,
                    cards));
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
                SaveRevisionOf(json),
                items,
                json.Int("inventory_capacity"),
                new DefinitionId(json.String("devil_fruit")),
                json.String("devil_fruit_source"));

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
                .Add("spawn_id", character.Spawn.Value)
                // Zero means "this server carries no inventory", which the API reads as
                // "leave the bag alone" rather than as "the bag is now empty". A world
                // composed without an item registry must not delete anybody's belongings.
                .Add("inventory_capacity", character.InventoryCapacity)
                // The stable fruit id, and nothing about what it does. An empty string is
                // "this character owns none", which is what the API deletes the row for.
                .Add("devil_fruit", character.DevilFruit.Value ?? string.Empty)
                .Add("devil_fruit_source", character.DevilFruitSource ?? string.Empty);

            var body = new System.Text.StringBuilder();

            body.Append("{\"request_id\":\"").Append(RequestId.New().Value).Append("\",");

            // A character that has never been saved has no revision, and the API's contract
            // for that is an absent field -- it then requires that no revision row exists,
            // which is what makes a first save safe. Sending zero instead says "I read
            // revision zero", which matches nothing and refuses every first save forever.
            // The race between two first saves is still caught: the revision table's
            // primary key lets exactly one of them insert.
            if (expectedSaveRevision > 0)
            {
                body.Append("\"save_revision\":").Append(expectedSaveRevision).Append(',');
            }

            body.Append("\"state\":").Append(WithCollections(state, character));
            body.Append('}');

            return body.ToString();
        }

        /// <summary>
        /// Splices the four collections into the state object.
        /// </summary>
        /// <remarks><see cref="JsonWriter"/> writes flat objects and one nested object; it
        /// cannot express an array of objects, and teaching it to would be more machinery
        /// than four arrays justify. The arrays are appended textually, with every value
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

            builder.Append("],\"items\":[");

            for (int i = 0; i < character.Items.Count; i++)
            {
                if (i > 0) builder.Append(',');

                PersistedItem item = character.Items[i];

                string flatItem = new JsonWriter()
                    .Add("instance_id", item.Instance.Value)
                    .Add("item_id", item.Item.Value)
                    .Add("quantity", item.Quantity)
                    .Add("slot", item.SlotIndex)
                    .Add("lock_state", item.LockState)
                    .Add("equipment_slot", item.EquipmentSlot)
                    .ToJson();

                if (!IsEquipmentRow(item))
                {
                    // No enhancement key at all for an ordinary item. The API treats the
                    // key's presence as "this is equipment", so sending a zeroed one would
                    // make every potion look like a sword.
                    builder.Append(flatItem);

                    continue;
                }

                var withEquipment = new System.Text.StringBuilder(flatItem);

                withEquipment.Length -= 1;

                withEquipment.Append(",\"enhancement_level\":").Append(item.EnhancementLevel);
                withEquipment.Append(",\"rarity_id\":\"").Append(item.Rarity.Value ?? string.Empty)
                    .Append("\",\"enchants\":[");

                for (int n = 0; n < item.Enchants.Count; n++)
                {
                    if (n > 0) withEquipment.Append(',');

                    withEquipment.Append(new JsonWriter()
                        .Add("stone_id", item.Enchants[n].Stone.Value)
                        .Add("socket", item.Enchants[n].SocketIndex)
                        .Add("rank", item.Enchants[n].Rank)
                        .ToJson());
                }

                withEquipment.Append("],\"cards\":[");

                for (int n = 0; n < item.Cards.Count; n++)
                {
                    if (n > 0) withEquipment.Append(',');

                    withEquipment.Append(new JsonWriter()
                        .Add("card_id", item.Cards[n].Card.Value)
                        .Add("socket", item.Cards[n].SocketIndex)
                        .Add("card_instance_id", item.Cards[n].CardInstance.Value)
                        .ToJson());
                }

                withEquipment.Append("]}");

                builder.Append(withEquipment);
            }

            builder.Append("]}");

            return builder.ToString();
        }

        /// <summary>
        /// Whether a row carries per-copy equipment state.
        /// </summary>
        /// <remarks>
        /// True for anything worn, and for anything in a bag that has an upgrade on it. A
        /// +0 unenchanted piece sitting in a bag is indistinguishable from an ordinary item
        /// here and is sent as one; nothing is lost, because there is nothing to lose --
        /// the definition supplies everything else, and the piece is rebuilt as equipment
        /// on load from that definition.
        /// </remarks>
        private static bool IsEquipmentRow(in PersistedItem item)
        {
            // The row's own answer. Deriving it from the contents meant a piece that had
            // just lost its last card was sent as an ordinary item, so the server never ran
            // the equipment write and never deleted the socket that had been taken out.
            return item.IsEquipment || item.IsEquipped;
        }

        private static int SaveRevisionOf(JsonReader json)
        {
            // Two shapes, one meaning. A save answers with a top-level "save_revision"; a
            // load nests every revision under "revisions" and calls this one "save".
            //
            // Reading only the top level was a real defect: a load reported revision zero
            // however many times the character had been written, so the first save after a
            // load presented a revision that matched nothing and was refused as stale --
            // and stayed refused forever. Nothing noticed until a world server tried to save
            // a character twice.
            int top = json.Int("save_revision");

            if (top > 0) return top;

            return json.Nested("revisions").Int("save");
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
