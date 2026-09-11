using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The door between a click and the rules: who may ask, and what they are told.
    /// </summary>
    /// <remarks>
    /// <b>The rules themselves are somebody else's tests.</b> <c>NpcInteractionTests</c>
    /// proves what an NPC will do for a player and <c>HarborTownNpcTests</c> proves the
    /// shipped town is authored coherently. What is left -- and what this covers -- is the
    /// part a networked game adds: the identity comes from the connection rather than the
    /// message, the answer goes back to exactly one person, and a refusal is an answer
    /// rather than silence.
    ///
    /// <b>Every test here is a way a client could cheat if the door were thin.</b> Asking on
    /// somebody else's behalf, naming an NPC that does not exist, naming a role number
    /// nobody authored, and standing too far away are four separate attempts at the same
    /// thing, and each has to fail for its own reason.
    /// </remarks>
    internal sealed class NpcInteractionRuntimeTests
    {
        private const string Map = "map.town";
        private const string Elsewhere = "map.other";
        private const string Guide = "npc.guide";
        private const string Vendor = "npc.vendor";
        private const string GuideSpawn = "spawn.guide";
        private const string Shop = "shop.general";
        private const string Quest = "quest.first";

        private const int Connection = 3;
        private const int Stranger = 9;

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
                => CharacterPersistenceResult.Saved(r + 1);
        }

        private readonly List<Object> _local = new List<Object>();

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterNpcAuthority _authority;
        private DefinitionRegistry<NPCDefinition> _npcs;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private DefinitionRegistry<ShopDefinition> _shops;
        private DefinitionRegistry<QuestDefinition> _quests;

        [SetUp]
        public void BuildAWorldWithTwoTownspeople()
        {
            _store = new FakeStore();

            _spawns = new DefinitionRegistry<SpawnPointDefinition>();
            _spawns.Register(Spawn("spawn.home", Map, SpawnType.Player, 0f, 0f, 0f));
            _spawns.Register(Spawn("spawn.away", Elsewhere, SpawnType.Player, 0f, 0f, 0f));
            _spawns.Register(Spawn(GuideSpawn, Map, SpawnType.Npc, 10f, 0f, 0f));

            _shops = new DefinitionRegistry<ShopDefinition>();
            _shops.Register(MakeShop(Shop));

            _quests = new DefinitionRegistry<QuestDefinition>();
            _quests.Register(MakeQuest(Quest));

            _npcs = new DefinitionRegistry<NPCDefinition>();
            _npcs.Register(MakeNpc(Guide, questGiver: true, quests: Quest));
            _npcs.Register(MakeNpc(Vendor, shop: Shop));

            _players = new WorldCharacterRegistry(_store, _spawns);

            _authority = new CharacterNpcAuthority(_players,
                new NpcInteractionService.Context(_npcs, _spawns, _shops, _quests));
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- the ordinary answer ----------------------------------------------------------

        [Test]
        public void A_player_standing_beside_an_npc_is_allowed()
        {
            StandAt(AddPlayer(), 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Guide, (int)NpcRole.Quest, 1);

            Assert.That(answer.Accepted, Is.True, "reason " + answer.Reason);
            Assert.That(answer.NpcId, Is.EqualTo(Guide));
            Assert.That(answer.Role, Is.EqualTo((int)NpcRole.Quest));
            Assert.That(answer.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void A_vendor_answers_with_the_shop_to_open()
        {
            // The content the role resolves to travels, so the client opens the authored
            // stock list rather than choosing one.
            StandAt(AddPlayer(), 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Vendor, (int)NpcRole.Shop, 1);

            Assert.That(answer.Accepted, Is.True);
            Assert.That(answer.Content, Is.EqualTo(Shop));
        }

        [Test]
        public void A_role_that_needs_no_content_carries_none()
        {
            StandAt(AddPlayer(), 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Guide, (int)NpcRole.Quest, 1);

            Assert.That(answer.Content, Is.Empty,
                "a quest giver's quests are read from the NPC, not handed to the client");
        }

        // ---- every way it must refuse -----------------------------------------------------

        [Test]
        public void A_player_across_the_map_is_refused()
        {
            StandAt(AddPlayer(), 40f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Guide, (int)NpcRole.Quest, 1);

            Assert.That(answer.Accepted, Is.False);
            Assert.That(answer.Reason,
                Is.EqualTo((int)NpcInteractionRejection.TooFar));
        }

        [Test]
        public void Walking_closer_and_asking_again_works()
        {
            // The refusal must not be sticky. A player told to walk closer does exactly
            // that, and a door that remembered the first answer would never let them in.
            LivingCharacter player = AddPlayer();

            StandAt(player, 40f, 0f, 0f);

            Assert.That(_authority.Apply(Connection, Guide, (int)NpcRole.Quest, 1).Accepted,
                Is.False);

            StandAt(player, 10f, 0f, 0f);

            Assert.That(_authority.Apply(Connection, Guide, (int)NpcRole.Quest, 2).Accepted,
                Is.True, "a player who walked closer was still refused");
        }

        [Test]
        public void A_connection_with_no_character_is_refused()
        {
            AddPlayer();

            NpcInteractionSnapshot answer =
                _authority.Apply(Stranger, Guide, (int)NpcRole.Quest, 1);

            Assert.That(answer.Accepted, Is.False,
                "a connection holding no character interacted with an NPC");
        }

        [Test]
        public void An_npc_that_does_not_exist_is_refused()
        {
            StandAt(AddPlayer(), 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, "npc.invented", (int)NpcRole.Shop, 1);

            Assert.That(answer.Accepted, Is.False);
            Assert.That(answer.Reason,
                Is.EqualTo((int)NpcInteractionRejection.UnknownNpc));
        }

        [Test]
        public void An_empty_npc_id_is_refused_rather_than_matching_something()
        {
            StandAt(AddPlayer(), 10f, 0f, 0f);

            Assert.That(_authority.Apply(Connection, string.Empty, (int)NpcRole.Shop, 1)
                .Accepted, Is.False);

            Assert.That(_authority.Apply(Connection, null, (int)NpcRole.Shop, 2)
                .Accepted, Is.False);
        }

        [Test]
        public void A_role_number_nobody_authored_is_refused_rather_than_cast()
        {
            // The wire carries an int. A client is free to put 99 in it, and the cast must
            // not be the thing that decides what happens next.
            StandAt(AddPlayer(), 10f, 0f, 0f);

            foreach (int nonsense in new[] { -1, 99, int.MaxValue, int.MinValue })
            {
                NpcInteractionSnapshot answer =
                    _authority.Apply(Connection, Guide, nonsense, 1);

                Assert.That(answer.Accepted, Is.False, "role " + nonsense + " was accepted");

                Assert.That(answer.Reason,
                    Is.EqualTo((int)NpcInteractionRejection.RoleNotOffered),
                    "role " + nonsense);
            }
        }

        [Test]
        public void Asking_an_npc_for_a_service_it_does_not_offer_is_refused()
        {
            StandAt(AddPlayer(), 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Guide, (int)NpcRole.Shop, 1);

            Assert.That(answer.Accepted, Is.False);
            Assert.That(answer.Reason,
                Is.EqualTo((int)NpcInteractionRejection.RoleNotOffered));
        }

        [Test]
        public void A_player_on_another_map_is_refused()
        {
            LivingCharacter player = AddPlayer(map: Elsewhere);

            StandAt(player, 10f, 0f, 0f);

            NpcInteractionSnapshot answer =
                _authority.Apply(Connection, Guide, (int)NpcRole.Quest, 1);

            Assert.That(answer.Accepted, Is.False);
            Assert.That(answer.Reason,
                Is.EqualTo((int)NpcInteractionRejection.WrongMap));
        }

        [Test]
        public void Every_request_is_answered_including_the_refused_ones()
        {
            // Silence is indistinguishable from a lost packet, and a player who is told
            // nothing clicks again -- and again.
            StandAt(AddPlayer(), 40f, 0f, 0f);

            _authority.Submit(Connection, Guide, (int)NpcRole.Quest, 7);

            Assert.That(_authority.Handled, Is.EqualTo(1));
            Assert.That(_authority.LastSnapshot.Sequence, Is.EqualTo(7),
                "the refusal did not carry the sequence it answers");
        }

        // ---- what the player is shown -----------------------------------------------------

        [Test]
        public void The_dialogue_opens_on_the_service_the_server_authorised()
        {
            NpcDialogueView panel = Panel();

            panel.Show(new DefinitionId(Vendor), NpcRole.Shop, "Village Vendor");

            Assert.That(panel.IsVisible, Is.True);
            Assert.That(panel.Npc.Value, Is.EqualTo(Vendor));
            Assert.That(panel.Role, Is.EqualTo(NpcRole.Shop));
            Assert.That(panel.Title, Is.EqualTo("Village Vendor"));
            Assert.That(panel.Body, Is.Not.Empty);
        }

        [Test]
        public void Each_service_says_something_different()
        {
            // A panel that said the same thing for every role would prove nothing about
            // which NPC was actually reached.
            var seen = new HashSet<string>();

            foreach (NpcRole role in new[]
            {
                NpcRole.Quest, NpcRole.Shop, NpcRole.Enhancement, NpcRole.Storage,
                NpcRole.JobChange,
            })
            {
                Assert.That(seen.Add(NpcDialogueView.BodyFor(role, null)), Is.True,
                    role + " shares its text with another service");
            }
        }

        [Test]
        public void The_dialogue_closes_cleanly_and_says_so_once()
        {
            NpcDialogueView panel = Panel();

            var closed = 0;

            panel.Closed += () => closed++;

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");
            panel.Close();

            Assert.That(panel.IsVisible, Is.False);
            Assert.That(panel.Npc.IsValid, Is.False, "a closed panel still names an NPC");
            Assert.That(panel.Title, Is.Empty);
            Assert.That(closed, Is.EqualTo(1));

            panel.Close();

            Assert.That(closed, Is.EqualTo(1), "closing a closed panel raised it again");
        }

        [Test]
        public void An_open_conversation_stops_the_world_taking_clicks()
        {
            // The pointer already refuses any click that lands on UI, so a full-screen
            // raycast target is the whole modal. Without it a player walks away mid
            // sentence and the panel follows them around.
            NpcDialogueView panel = PanelOnACanvas();

            Assert.That(panel.BlocksTheWorld, Is.False, "a closed panel blocks nothing");

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");

            Assert.That(panel.BlocksTheWorld, Is.True,
                "clicking the ground would still walk the character away");
        }

        [Test]
        public void Closing_gives_the_world_back()
        {
            NpcDialogueView panel = PanelOnACanvas();

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");
            panel.Close();

            Assert.That(panel.BlocksTheWorld, Is.False,
                "an invisible sheet left over the whole world that nothing explains");
        }

        [Test]
        public void The_blocker_sits_behind_the_panel_and_not_over_it()
        {
            // Over the panel it would eat the Close button, and a conversation nobody can
            // leave is worse than one that does not open.
            NpcDialogueView panel = PanelOnACanvas();

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");

            Transform parent = panel.transform.parent;
            Transform sheet = parent.Find("Dialogue Blocker");

            Assert.That(sheet, Is.Not.Null);
            Assert.That(sheet.GetSiblingIndex(),
                Is.LessThan(panel.transform.GetSiblingIndex()),
                "the blocker is drawn in front of the panel it is meant to sit behind");
        }

        [Test]
        public void The_blocker_covers_the_whole_screen()
        {
            NpcDialogueView panel = PanelOnACanvas();

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");

            var sheet = (RectTransform)panel.transform.parent.Find("Dialogue Blocker");

            Assert.That(sheet.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(sheet.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(sheet.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(sheet.offsetMax, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void The_blocker_is_invisible()
        {
            // It catches clicks; it does not change how the town looks.
            NpcDialogueView panel = PanelOnACanvas();

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");

            var sheet = panel.transform.parent.Find("Dialogue Blocker")
                .GetComponent<UnityEngine.UI.Image>();

            Assert.That(sheet.color.a, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(sheet.raycastTarget, Is.True,
                "an invisible sheet that does not raycast blocks nothing at all");
        }

        [Test]
        public void Opening_twice_makes_one_blocker_not_two()
        {
            NpcDialogueView panel = PanelOnACanvas();

            panel.Show(new DefinitionId(Guide), NpcRole.Quest, "Guide");
            panel.Close();
            panel.Show(new DefinitionId(Vendor), NpcRole.Shop, "Vendor");

            var sheets = 0;

            foreach (Transform child in panel.transform.parent)
            {
                if (child.name == "Dialogue Blocker") sheets++;
            }

            Assert.That(sheets, Is.EqualTo(1),
                "a conversation per click would stack sheets until nothing was clickable");
        }

        [Test]
        public void Nothing_in_the_modal_reaches_into_movement_or_the_pointer()
        {
            // The whole reason this is a UI sheet: movement and the pointer are locked, and
            // a "dialogue is open" flag inside them would be a UI concern the next person
            // has to know about.
            // Comments stripped first: the remark in EnsureBlocker has to be free to say
            // *why* a UI sheet is enough, which means naming the pointer it relies on. What
            // must not exist is a reference in the code.
            string code = CodeWithoutComments("Assets/_Game/Scripts/UI/NpcDialogueView.cs");

            foreach (string forbidden in new[]
            {
                "WorldPointerInput", "CharacterMovementInput", "MovementAuthority",
                "RequestMove", "ChibiFantasy.Client",
            })
            {
                Assert.That(code.Contains(forbidden), Is.False,
                    "the dialogue depends on " + forbidden
                    + "; the modal must stay presentation, because movement is locked");
            }
        }

        // ---- the markers in a scene -------------------------------------------------------

        [Test]
        public void A_marker_gets_something_for_a_click_to_hit()
        {
            // The NPC art is a plain model with no collider. Without one the pointer ray
            // passes through and the townsperson is scenery.
            WorldNpcMarker marker = Marker(Guide);

            Assert.That(marker.GetComponentInChildren<Collider>(), Is.Not.Null);
        }

        [Test]
        public void A_marker_that_already_has_a_collider_is_not_given_a_second()
        {
            var host = new GameObject("npc");

            _local.Add(host);

            host.AddComponent<BoxCollider>();

            WorldNpcMarker marker = host.AddComponent<WorldNpcMarker>();

            marker.EnsureClickable();

            Assert.That(host.GetComponents<Collider>().Length, Is.EqualTo(1));
        }

        [Test]
        public void The_presenter_adopts_a_marker_once()
        {
            WorldNpcPresenter presenter = Presenter();

            WorldNpcMarker marker = Marker(Guide);

            Assert.That(presenter.Adopt(marker), Is.True);
            Assert.That(presenter.Adopt(marker), Is.False,
                "adopting twice would leave two nameplates and two requests per click");

            Assert.That(presenter.Count, Is.EqualTo(1));
        }

        [Test]
        public void A_nameplate_reads_the_name_and_the_role()
        {
            WorldNpcPresenter presenter = Presenter();

            presenter.Adopt(Marker(Vendor));
            presenter.RefreshPlates();

            string label = presenter.LabelFor(Marker(Vendor));

            Assert.That(label, Does.Contain("Vendor"));
            Assert.That(label, Does.Contain("<Shop>"));
        }

        [Test]
        public void An_npc_the_catalogue_does_not_ship_shows_no_name_rather_than_its_id()
        {
            // A player reading "npc.ghost" above somebody's head learns only that the game
            // is broken.
            WorldNpcPresenter presenter = Presenter();

            WorldNpcMarker ghost = Marker("npc.ghost");

            presenter.Adopt(ghost);

            Assert.That(presenter.LabelFor(ghost), Is.Empty);
        }

        [Test]
        public void A_click_with_no_player_bound_asks_nothing()
        {
            // Before the world spawns the owner there is nobody to ask on behalf of.
            WorldNpcPresenter presenter = Presenter();

            WorldNpcMarker marker = Marker(Guide);

            presenter.Adopt(marker);

            Assert.That(presenter.Request(marker), Is.False);
        }

        [Test]
        public void Arriving_at_an_npc_goes_through_the_presenter()
        {
            WorldNpcPresenter presenter = Presenter();

            WorldNpcMarker marker = Marker(Guide);

            presenter.Adopt(marker);

            marker.Arrive();

            Assert.That(marker.Arrivals, Is.EqualTo(1),
                "the pointer's arrival seam is what interaction hangs off");
        }

        [Test]
        public void Clearing_lets_go_of_every_marker()
        {
            WorldNpcPresenter presenter = Presenter();

            WorldNpcMarker marker = Marker(Guide);

            presenter.Adopt(marker);
            presenter.Clear();

            Assert.That(presenter.Count, Is.EqualTo(0));
            Assert.That(marker.Interaction, Is.Null,
                "a marker still calling a presenter that let go of it");
        }

        // ---- helpers ----------------------------------------------------------------------

        private WorldNpcPresenter Presenter()
        {
            var host = new GameObject("presenter");

            _local.Add(host);

            WorldNpcPresenter presenter = host.AddComponent<WorldNpcPresenter>();

            presenter.Bind(_npcs);

            return presenter;
        }

        private readonly Dictionary<string, WorldNpcMarker> _markers =
            new Dictionary<string, WorldNpcMarker>();

        private WorldNpcMarker Marker(string npcId)
        {
            if (_markers.TryGetValue(npcId, out WorldNpcMarker existing) && existing != null)
            {
                return existing;
            }

            var host = new GameObject(npcId);

            _local.Add(host);

            WorldNpcMarker marker = host.AddComponent<WorldNpcMarker>();

            marker.NpcId = npcId;
            marker.EnsureClickable();

            _markers[npcId] = marker;

            return marker;
        }

        /// <summary>A panel under a canvas, which is what the world builds.</summary>
        private NpcDialogueView PanelOnACanvas()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform));

            _local.Add(canvas);

            var host = new GameObject("panel", typeof(RectTransform));

            host.transform.SetParent(canvas.transform, false);

            NpcDialogueView panel = host.AddComponent<NpcDialogueView>();

            panel.EnsureVisuals();

            return panel;
        }

        private NpcDialogueView Panel()
        {
            var host = new GameObject("panel", typeof(RectTransform));

            _local.Add(host);

            NpcDialogueView panel = host.AddComponent<NpcDialogueView>();

            panel.EnsureVisuals();

            return panel;
        }

        /// <summary>A file's code, with its comments removed.</summary>
        private static string CodeWithoutComments(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("/*")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }

        private LivingCharacter AddPlayer(string character = "char-a", string map = Map)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(Connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50));

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        private static void StandAt(LivingCharacter who, float x, float y, float z)
        {
            who.Location.Position = new CombatPosition(x, y, z);
        }

        private SpawnPointDefinition Spawn(string id, string map, SpawnType type,
            float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map
                + "\"},\"_spawnType\":" + (int)type + ",\"_x\":" + x + ",\"_y\":" + y
                + ",\"_z\":" + z + "}", spawn);

            _local.Add(spawn);

            return spawn;
        }

        private NPCDefinition MakeNpc(string id, bool questGiver = false,
            string shop = null, string quests = null)
        {
            var npc = ScriptableObject.CreateInstance<NPCDefinition>();

            var json = new System.Text.StringBuilder();

            json.Append("{\"_id\":{\"_value\":\"").Append(id).Append("\"}");
            json.Append(",\"_map\":{\"_value\":\"").Append(Map).Append("\"}");
            json.Append(",\"_spawnPoint\":{\"_value\":\"").Append(GuideSpawn).Append("\"}");
            json.Append(",\"_enabled\":true,\"_interactionRadius\":3");
            json.Append(",\"_isQuestGiver\":").Append(questGiver ? "true" : "false");
            json.Append(",\"_shop\":{\"_value\":\"").Append(shop ?? string.Empty).Append("\"}");

            if (!string.IsNullOrEmpty(quests))
            {
                json.Append(",\"_quests\":[{\"_value\":\"").Append(quests).Append("\"}]");
            }

            json.Append("}");

            JsonUtility.FromJsonOverwrite(json.ToString(), npc);

            _local.Add(npc);

            return npc;
        }

        private ShopDefinition MakeShop(string id)
        {
            var shop = ScriptableObject.CreateInstance<ShopDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_entries\":[{\"_item\":{\"_value\":"
                + "\"item.potion\"},\"_price\":10,\"_stock\":0,\"_enabled\":true}]}", shop);

            _local.Add(shop);

            return shop;
        }

        private QuestDefinition MakeQuest(string id)
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"}}", quest);

            _local.Add(quest);

            return quest;
        }
    }
}
