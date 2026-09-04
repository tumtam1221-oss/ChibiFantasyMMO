using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The world UI, the scene seam, and the architecture rules behind them.
    /// </summary>
    /// <remarks>
    /// Deciding and presenting are two steps, and the split is the point: the travel service
    /// cannot load a scene and the loader cannot refuse a journey. Several tests below check
    /// that neither has quietly grown the other's job.
    ///
    /// The world map is a projection of authored portals, never a hand-written diagram --
    /// which is what stops it disagreeing with the world the first time content changes.
    /// </remarks>
    internal sealed class WorldUiIntegrationTests : WorldTestBase
    {
        private const string Elder = "npc.elder";
        private const string ElderSpawn = "spawn.npc.elder";
        private const string GeneralStore = "shop.general";
        private const string FetchQuest = "quest.fetch";

        private GameObject _host;
        private WorldUiController _controller;
        private MapSceneLoader _loader;

        [SetUp]
        public void CreateWorldUi()
        {
            _host = new GameObject("WorldUiHost");
            _controller = _host.AddComponent<WorldUiController>();
            _loader = _host.AddComponent<MapSceneLoader>();

            AddSpawn(ElderSpawn, TownA, SpawnType.Npc, 12f, 0f, 10f);
            AddQuestDefinition(FetchQuest);
            AddShopDefinition(GeneralStore);
            AddNpcDefinition(Elder, TownA, ElderSpawn, questGiver: true, shop: GeneralStore,
                quests: new[] { new DefinitionId(FetchQuest) });
        }

        [TearDown]
        public void DestroyWorldUi()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private WorldMapAdapter.Context ViewContext()
        {
            return new WorldMapAdapter.Context(Maps, SpawnPoints, Portals, Npcs);
        }

        private void Bind(CharacterLocationState location, int level = 1)
        {
            _controller.Bind(location, Container(8), Maps, SpawnPoints, Portals, Npcs,
                Shops, Quests, Items, level);
        }

        // ---- view data reads only ------------------------------------------------------

        [Test]
        public void Building_world_views_changes_nothing()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Revision before = location.Revision;
            CombatPosition where = location.Position;

            var portals = new List<PortalViewData>();
            var npcs = new List<NpcViewData>();

            for (int i = 0; i < 25; i++)
            {
                WorldMapAdapter.BuildMap(location.CurrentMap, ViewContext());
                WorldMapAdapter.BuildPortals(location, ViewContext(), portals);
                WorldMapAdapter.BuildNpcs(location, ViewContext(), npcs);
            }

            Assert.That(location.Revision, Is.EqualTo(before));
            Assert.That(location.Position, Is.EqualTo(where),
                "twenty-five reads must cost exactly nothing");
        }

        [Test]
        public void A_map_view_carries_the_name_and_the_classification()
        {
            MapViewData town = WorldMapAdapter.BuildMap(new DefinitionId(TownA), ViewContext());

            Assert.That(town.IsValid, Is.True);
            Assert.That(town.IsTown, Is.True);
            Assert.That(town.Category, Is.EqualTo(MapCategory.Town));
            Assert.That(MapNameView.FormatLabel(town, null), Does.Contain("Town"));

            Assert.That(WorldMapAdapter.BuildMap(new DefinitionId("map.nowhere"),
                ViewContext()).IsValid, Is.False);
            Assert.That(MapNameView.FormatLabel(MapViewData.None, null), Is.Empty);
        }

        [Test]
        public void Only_the_portals_on_the_current_map_are_offered()
        {
            var portals = new List<PortalViewData>();

            WorldMapAdapter.BuildPortals(StartAt(TownASpawn), ViewContext(), portals);
            Assert.That(portals.Count, Is.EqualTo(2), "the gate and the closed one");

            WorldMapAdapter.BuildPortals(StartAt(FieldASpawn), ViewContext(), portals);
            Assert.That(portals, Is.Empty, "a gate on another map would be noise");
        }

        [Test]
        public void A_portal_view_reports_range_and_the_destinations_own_name()
        {
            CharacterLocationState near = StartAt(TownASpawn);

            PortalViewData inRange = WorldMapAdapter.BuildPortal(new DefinitionId(GateToField),
                near, ViewContext());

            Assert.That(inRange.IsInRange, Is.True);
            Assert.That(inRange.CanOffer, Is.True);
            Assert.That(inRange.DestinationNameKey.IsValid, Is.True,
                "the name comes off the MapDefinition, never out of the portal");
            Assert.That(PortalInteractionView.FormatLabel(inRange, null),
                Does.Contain("Field"));

            near.Position = new CombatPosition(900f, 0f, 900f);

            PortalViewData far = WorldMapAdapter.BuildPortal(new DefinitionId(GateToField),
                near, ViewContext());

            Assert.That(far.IsInRange, Is.False);
            Assert.That(far.CanOffer, Is.False);
            Assert.That(PortalInteractionView.FormatLabel(far, null), Does.Contain("Too far"));
        }

        [Test]
        public void A_closed_portal_is_shown_rather_than_hidden()
        {
            PortalViewData closed = WorldMapAdapter.BuildPortal(new DefinitionId(ClosedGate),
                StartAt(TownASpawn), ViewContext());

            Assert.That(closed.IsValid, Is.True);
            Assert.That(closed.Enabled, Is.False);
            Assert.That(closed.CanOffer, Is.False);
            Assert.That(PortalInteractionView.FormatLabel(closed, null), Does.Contain("Closed"),
                "a player can see the gate exists");
        }

        [Test]
        public void An_npc_view_lists_the_roles_its_definition_offers()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(12f, 0f, 10f);

            var npcs = new List<NpcViewData>();
            WorldMapAdapter.BuildNpcs(location, ViewContext(), npcs);

            Assert.That(npcs.Count, Is.EqualTo(1));
            Assert.That(npcs[0].IsInRange, Is.True);
            Assert.That(npcs[0].Roles, Contains.Item(NpcRole.Quest));
            Assert.That(npcs[0].Roles, Contains.Item(NpcRole.Shop));
            Assert.That(npcs[0].Roles, Has.No.Member(NpcRole.Storage));
            Assert.That(NpcInteractionView.FormatTitle(npcs[0], null), Is.Not.Empty);
        }

        [Test]
        public void The_world_map_is_derived_from_the_authored_portals()
        {
            var links = new List<WorldMapLinkViewData>();
            WorldMapAdapter.BuildWorldMapLinks(ViewContext(), links);

            Assert.That(links.Count, Is.EqualTo(2));

            bool foundGate = false;
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].FromMap != new DefinitionId(TownA)) continue;
                if (links[i].ToMap != new DefinitionId(FieldA)) continue;
                foundGate = true;
            }

            Assert.That(foundGate, Is.True);

            // Authoring one more gate changes the map with no UI change at all.
            AddPortal("portal.extra", FieldA, DungeonA, TownASpawn);
            WorldMapAdapter.BuildWorldMapLinks(ViewContext(), links);

            Assert.That(links.Count, Is.EqualTo(3),
                "the world map is a projection, never a hand-written diagram");
        }

        // ---- the controller ------------------------------------------------------------

        [Test]
        public void Travelling_through_the_controller_moves_the_player()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Bind(location);

            TravelResult result = _controller.SubmitTravel(new DefinitionId(GateToField));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(FieldA)));
            Assert.That(_controller.LastTravelResult.DestinationSpawn,
                Is.EqualTo(new DefinitionId(FieldASpawn)));
        }

        [Test]
        public void A_refused_journey_keeps_the_services_reason_and_moves_nobody()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Bind(location);

            TravelResult result = _controller.SubmitTravel(new DefinitionId(ClosedGate));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(TravelRejection.PortalDisabled),
                "the UI reports the service's reason rather than inventing one");
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
        }

        [Test]
        public void A_warp_through_the_controller_still_demands_a_town()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Bind(location);

            Assert.That(_controller.SubmitWarp(new DefinitionId(FieldA),
                new DefinitionId(FieldASpawn)).Reason,
                Is.EqualTo(TravelRejection.DestinationNotAllowed),
                "the rule is enforced again, so this cannot be used to reach a field");

            _controller.SubmitTravel(new DefinitionId(GateToField));

            Assert.That(_controller.SubmitWarp(new DefinitionId(TownA),
                new DefinitionId(TownASpawn)).IsAccepted, Is.True);
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
        }

        [Test]
        public void Interacting_through_the_controller_authorises_a_role()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(12f, 0f, 10f);
            Bind(location);

            NpcInteractionResult result = _controller.SubmitInteract(new DefinitionId(Elder),
                NpcRole.Shop);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Content, Is.EqualTo(new DefinitionId(GeneralStore)));
        }

        [Test]
        public void The_controller_redraws_only_when_the_location_changed()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Bind(location);

            Assert.That(_controller.RefreshIfChanged(), Is.False);

            _controller.SubmitTravel(new DefinitionId(GateToField));

            Assert.That(_controller.RefreshIfChanged(), Is.False,
                "the submit already refreshed");
        }

        [Test]
        public void An_unbound_controller_does_nothing_rather_than_throwing()
        {
            Assert.DoesNotThrow(() => _controller.Refresh());
            Assert.That(_controller.RefreshIfChanged(), Is.False);
            Assert.That(_controller.SubmitTravel(new DefinitionId(GateToField)).IsAccepted,
                Is.False);
        }

        // ---- the scene seam ------------------------------------------------------------

        [Test]
        public void The_loader_refuses_a_journey_gameplay_refused()
        {
            _loader.Bind(Maps, SpawnPoints);

            TravelResult rejected = TravelResult.Rejected(TravelRejection.PortalDisabled);

            Assert.That(_loader.Validate(rejected), Is.EqualTo(MapLoadFailure.TravelRejected),
                "presentation can never run for a journey that was not allowed");
        }

        [Test]
        public void The_loader_resolves_a_scene_from_the_map_and_from_nothing_else()
        {
            _loader.Bind(Maps, SpawnPoints);

            Assert.That(_loader.ResolveScene(new DefinitionId(TownA)),
                Is.EqualTo("scenes/" + TownA), "the one place an id becomes a scene name");

            Assert.That(_loader.ResolveScene(new DefinitionId("map.nowhere")), Is.Null);
            Assert.That(_loader.ResolveScene(DefinitionId.None), Is.Null);
        }

        [Test]
        public void A_map_with_no_scene_is_an_explicit_failure()
        {
            AddMapDefinition("map.noscene", MapCategory.Field, scene: string.Empty);
            AddSpawn("spawn.noscene", "map.noscene");

            _loader.Bind(Maps, SpawnPoints);

            TravelResult travel = TravelResult.Accepted(default, new DefinitionId(TownA),
                new DefinitionId("map.noscene"), new DefinitionId("spawn.noscene"));

            Assert.That(_loader.Validate(travel), Is.EqualTo(MapLoadFailure.NoScene),
                "silently loading the wrong place would be worse than stopping");
        }

        [Test]
        public void A_missing_destination_spawn_is_an_explicit_failure()
        {
            _loader.Bind(Maps, SpawnPoints);

            TravelResult travel = TravelResult.Accepted(default, new DefinitionId(TownA),
                new DefinitionId(FieldA), new DefinitionId("spawn.deleted"));

            Assert.That(_loader.Validate(travel), Is.EqualTo(MapLoadFailure.UnknownSpawn));
            Assert.That(_loader.PlacePlayer(new DefinitionId("spawn.deleted")), Is.False,
                "and placement refuses rather than dropping the player at the origin");
        }

        [Test]
        public void The_loader_places_a_player_at_the_authored_point()
        {
            var player = new GameObject("Player").transform;

            try
            {
                _loader.Bind(Maps, SpawnPoints, player);

                Assert.That(_loader.PlacePlayer(new DefinitionId(FieldASpawn)), Is.True);
                Assert.That(player.position, Is.EqualTo(new Vector3(50f, 0f, 0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player.gameObject);
            }
        }

        [Test]
        public void An_unbound_loader_reports_missing_context_rather_than_loading()
        {
            TravelResult travel = TravelResult.Accepted(default, new DefinitionId(TownA),
                new DefinitionId(FieldA), new DefinitionId(FieldASpawn));

            Assert.That(_loader.Validate(travel), Is.EqualTo(MapLoadFailure.MissingContext));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void Gameplay_has_no_non_comment_reference_to_the_engine()
        {
            string[] files = System.IO.Directory.GetFiles(
                "Assets/_Game/Scripts/Gameplay", "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string[] lines = System.IO.File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i].TrimStart();
                    if (code.StartsWith("//")) continue;

                    Assert.That(code, Does.Not.Contain("UnityEngine"),
                        file + ":" + (i + 1));
                }
            }
        }

        [Test]
        public void No_gameplay_service_routes_on_a_scene_name_or_a_hard_coded_id()
        {
            string[] services =
            {
                "Assets/_Game/Scripts/Gameplay/TravelService.cs",
                "Assets/_Game/Scripts/Gameplay/NpcInteractionService.cs",
                "Assets/_Game/Scripts/Gameplay/CharacterLocationState.cs"
            };

            foreach (string file in services)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain(".unity"), file);
                    Assert.That(code, Does.Not.Contain("SceneManager"), file);
                    Assert.That(code, Does.Not.Contain("Prontera"), file);

                    // A content id in code would appear as a string literal. Bare `npc.`
                    // is a member access on a variable, which is not routing.
                    Assert.That(code, Does.Not.Contain("\"npc."), file);
                    Assert.That(code, Does.Not.Contain("\"map."), file);
                    Assert.That(code, Does.Not.Contain("\"portal."), file);
                    Assert.That(code, Does.Not.Contain("\"spawn."), file);
                }
            }
        }

        /// <summary>
        /// A file's lines with the comments removed.
        /// </summary>
        /// <remarks>Prose may name a type or an example; code may not. Asserting over the
        /// raw text checks the documentation instead of the implementation.</remarks>
        private static IEnumerable<string> CodeLines(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;

                yield return code;
            }
        }

        [Test]
        public void There_is_exactly_one_travel_service_and_one_of_each_world_state()
        {
            Type[] types = typeof(TravelService).Assembly.GetTypes()
                .Concat(typeof(MapDefinition).Assembly.GetTypes())
                .Concat(typeof(WorldUiController).Assembly.GetTypes())
                .Concat(typeof(MapViewData).Assembly.GetTypes())
                .ToArray();

            string[] forbidden =
            {
                "TravelService2", "MapRegistry", "MapRegistry2", "NpcRegistry", "NpcSystem",
                "NpcSystem2", "StorageSystem2", "QuestSystem2", "InventorySystem2",
                "WorldState2", "MapSystem", "PortalSystem", "TravelManager", "NpcManager",
                "TradeItem", "ShopItem", "LootItem", "NpcItem"
            };

            foreach (Type type in types)
            {
                Assert.That(forbidden, Does.Not.Contain(type.Name),
                    type.FullName + " duplicates something that already exists");
            }

            int travelServices = types.Count(t => t.Name == "TravelService");
            Assert.That(travelServices, Is.EqualTo(1));
        }

        [Test]
        public void The_world_ui_holds_no_gameplay_state()
        {
            Assembly ui = typeof(MapViewData).Assembly;

            foreach (Type type in ui.GetTypes())
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (FieldInfo field in fields)
                {
                    string held = field.FieldType.FullName ?? string.Empty;

                    Assert.That(held, Does.Not.Contain("ChibiFantasy.Gameplay"),
                        type.Name + "." + field.Name);
                    Assert.That(held, Does.Not.Contain("CharacterLocationState"),
                        type.Name + "." + field.Name);
                }
            }
        }

        /// <summary>
        /// The files allowed to start a journey or an interaction.
        /// </summary>
        /// <remarks>
        /// Phase 11 permitted exactly one: the world UI controller, which was the command
        /// boundary when the client decided its own travel. Phase 17 made the server
        /// authoritative, so there is now a second and it is the one that matters -- a
        /// client's request reaches <c>TravelCommandAuthority</c> and the server calls the
        /// travel rules.
        ///
        /// The property this test protects is unchanged: travel is not scattered. Naming the
        /// new boundary keeps that check meaningful, where deleting the test would not.
        /// </remarks>
        private static readonly string[] TravelCommandBoundaries =
        {
            // The client-side flow, still used by the prototype and offline scenes.
            "/Client/UI/WorldUiController.cs",

            // The authoritative one. A client asks; this decides.
            "/Server/TravelCommandAuthority.cs",
        };

        [Test]
        public void Exactly_two_files_may_start_a_journey()
        {
            // One boundary per side of the wire, and no more. A third entry must be a
            // deliberate decision somebody makes by editing this test.
            Assert.That(TravelCommandBoundaries.Length, Is.EqualTo(2));
        }

        [Test]
        public void Travel_and_interaction_happen_only_in_the_world_controller()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');

                bool isBoundary = false;

                foreach (string boundary in TravelCommandBoundaries)
                {
                    if (normalized.Contains(boundary)) isBoundary = true;
                }

                if (isBoundary) continue;
                if (normalized.Contains("/Gameplay/")) continue;

                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("TravelService.TryTraversePortal"),
                    normalized + " travels outside the command boundary");
                Assert.That(source, Does.Not.Contain("NpcInteractionService.TryInteract"),
                    normalized + " interacts outside the command boundary");
            }
        }

        [Test]
        public void The_scene_loader_cannot_decide_whether_a_journey_is_allowed()
        {
            // Structural: the loader takes a TravelResult and never a portal, a location or
            // a registry it could re-derive permission from.
            MethodInfo[] methods = typeof(MapSceneLoader).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (MethodInfo method in methods)
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(PortalDefinition)),
                        method.Name + " takes a portal, which would let it re-judge a journey");
                    Assert.That(parameter.ParameterType,
                        Is.Not.EqualTo(typeof(CharacterLocationState)),
                        method.Name + " takes a location, which would let it re-judge a journey");
                }
            }

            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Client/World/MapSceneLoader.cs"))
            {
                Assert.That(code, Does.Not.Contain("TravelService."),
                    "the loader obeys a decision; it does not make one");
            }
        }

        // ---- integration ---------------------------------------------------------------

        [Test]
        public void Map_to_portal_to_travel_to_scene_runs_end_to_end()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            Bind(location);
            _loader.Bind(Maps, SpawnPoints);

            // 1. the player sees where they are and what is nearby
            Assert.That(_controller.Portals.Count, Is.EqualTo(2));
            Assert.That(_controller.Portals[0].CanOffer, Is.True);

            // 2. gameplay decides
            TravelResult travel = _controller.SubmitTravel(new DefinitionId(GateToField));
            Assert.That(travel.IsAccepted, Is.True, travel.ToString());

            // 3. presentation obeys
            Assert.That(_loader.Validate(travel), Is.EqualTo(MapLoadFailure.None));
            Assert.That(_loader.ResolveScene(travel.DestinationMap), Is.EqualTo("scenes/" + FieldA));

            // 4. and the world the player now sees follows
            Assert.That(_controller.Portals, Is.Empty, "no gates authored on the field");
            Assert.That(WorldMapAdapter.BuildMap(location.CurrentMap, ViewContext()).Category,
                Is.EqualTo(MapCategory.Field));
        }

        [Test]
        public void Map_to_npc_to_quest_resolves_the_npcs_own_name()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(12f, 0f, 10f);
            Bind(location);

            NpcInteractionResult authorised = _controller.SubmitInteract(new DefinitionId(Elder),
                NpcRole.Quest);
            Assert.That(authorised.IsAccepted, Is.True, authorised.ToString());

            // The Phase 10 gap: quest state stores an id, and the UI now resolves the name.
            var questState = new CharacterQuestState(Character);

            QuestDefinition quest = AddTalkQuest("quest.talk", Elder);
            QuestService.TryAccept(questState, quest.Id,
                new QuestService.Context(Quests, Items, 10, Owner));

            var context = new WorldViewAdapter.Context(Items, null, Quests, Npcs, Maps);
            QuestViewData view = WorldViewAdapter.BuildQuest(questState, quest.Id, context);

            Assert.That(view.Objectives.Count, Is.EqualTo(1));
            Assert.That(view.Objectives[0].TargetNameKey.IsValid, Is.True,
                "the NPC's name is resolved through the registry, not copied into quest state");

            var table = new LocalizationTable();
            table.Set(view.Objectives[0].TargetNameKey, "Village Elder");

            Assert.That(QuestTrackerView.FormatRow(view, table), Does.Contain("Village Elder"));
        }

        [Test]
        public void Quest_state_still_stores_ids_and_never_a_name()
        {
            FieldInfo[] fields = typeof(QuestProgress).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(string)),
                    field.Name + " holds text; quest state stores ids so names stay content");
                Assert.That(field.FieldType.FullName, Does.Not.Contain("LocalizationKey"),
                    field.Name);
            }
        }

        [Test]
        public void A_town_warp_resolves_a_spawn_and_reaches_the_map_through_travel()
        {
            // The full Phase 08.3 -> Phase 11 path: an item resolves a destination, and the
            // world controller turns that into an arrival at an authored point.
            CharacterLocationState location = StartAt(FieldASpawn);
            Bind(location);

            TravelResult warp = _controller.SubmitWarp(new DefinitionId(TownA),
                new DefinitionId(TownASpawn));

            Assert.That(warp.IsAccepted, Is.True, warp.ToString());
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
            Assert.That(location.Position.X, Is.EqualTo(10f),
                "placed at the authored point, never at the origin");
        }

        // ---- authoring helpers ---------------------------------------------------------

        private void AddNpcDefinition(string id, string map, string spawnPoint,
            bool questGiver = false, string shop = null, DefinitionId[] quests = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<NPCDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_shop\":{\"_value\":\"" + (shop ?? string.Empty) + "\"}"
                + ",\"_isQuestGiver\":" + (questGiver ? "true" : "false")
                + ",\"_map\":{\"_value\":\"" + (map ?? string.Empty) + "\"}"
                + ",\"_spawnPoint\":{\"_value\":\"" + (spawnPoint ?? string.Empty) + "\"}"
                + ",\"_enabled\":true,\"_interactionRadius\":3}", definition);

            if (quests != null) SetPrivate(definition, "_quests", quests);

            Npcs.Register(definition);
        }

        private void AddShopDefinition(string id)
        {
            var definition = Track(ScriptableObject.CreateInstance<ShopDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"}}", definition);

            SetPrivate(definition, "_entries", new[]
            {
                new ShopEntry(new DefinitionId(Potion), 50)
            });

            Shops.Register(definition);
        }

        private QuestDefinition AddQuestDefinition(string id)
        {
            var definition = Track(ScriptableObject.CreateInstance<QuestDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"}}",
                definition);

            Quests.Register(definition);
            return definition;
        }

        private QuestDefinition AddTalkQuest(string id, string npc)
        {
            QuestDefinition definition = AddQuestDefinition(id);

            SetPrivate(definition, "_objectives", new[]
            {
                new QuestObjective(QuestObjectiveType.TalkToNpc, new DefinitionId(npc), 1)
            });

            return definition;
        }
    }
}
