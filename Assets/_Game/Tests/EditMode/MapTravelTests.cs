using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Maps, spawn points, portals and travel.
    /// </summary>
    /// <remarks>
    /// Two properties are worth more than the rest, and most of what follows exists for
    /// them. The first is that a traveller never arrives somewhere content did not author:
    /// there is no coordinate anywhere in this path to fall back to, only a spawn reference
    /// that resolves or does not. The second is that there is no way onto a map except
    /// through a validated portal or a validated warp -- the absence of a
    /// <c>TeleportToMap</c> is itself a tested property.
    ///
    /// Every map, portal and radius is a FIXTURE. Nothing in travel knows any of them.
    /// </remarks>
    internal sealed class MapTravelTests : WorldTestBase
    {
        // ---- registry ------------------------------------------------------------------

        [Test]
        public void The_registry_resolves_known_maps_and_refuses_unknown_ones()
        {
            MapDefinition map;

            Assert.That(Maps.TryGet(new DefinitionId(TownA), out map), Is.True);
            Assert.That(map.Category, Is.EqualTo(MapCategory.Town));
            Assert.That(Maps.TryGet(new DefinitionId("map.nowhere"), out map), Is.False);
            Assert.That(map, Is.Null);
        }

        [Test]
        public void The_registry_refuses_a_duplicate_id()
        {
            var duplicate = ScriptableObject.CreateInstance<MapDefinition>();

            try
            {
                JsonUtility.FromJsonOverwrite(
                    "{\"_id\":{\"_value\":\"" + TownA + "\"}}", duplicate);

                Assert.That(Maps.TryRegister(duplicate), Is.False,
                    "two maps claiming one id would make travel ambiguous");
                Assert.That(Maps.Count, Is.EqualTo(4), "and the registry is unchanged");
            }
            finally
            {
                Object.DestroyImmediate(duplicate);
            }
        }

        [Test]
        public void One_map_model_describes_a_town_a_field_and_a_boss_area()
        {
            MapDefinition town;
            MapDefinition field;
            MapDefinition boss;

            Maps.TryGet(new DefinitionId(TownA), out town);
            Maps.TryGet(new DefinitionId(FieldA), out field);
            Maps.TryGet(new DefinitionId(BossA), out boss);

            Assert.That(town.GetType(), Is.EqualTo(field.GetType()));
            Assert.That(field.GetType(), Is.EqualTo(boss.GetType()),
                "no map subclasses: what a map is comes from its category");

            Assert.That(TravelService.IsTown(town), Is.True);
            Assert.That(TravelService.IsTown(field), Is.False);
            Assert.That(TravelService.IsTown(boss), Is.False);
        }

        // ---- spawn points --------------------------------------------------------------

        [Test]
        public void A_spawn_point_belongs_to_exactly_one_map()
        {
            SpawnPointDefinition spawn;
            SpawnPoints.TryGet(new DefinitionId(TownASpawn), out spawn);

            Assert.That(spawn.Map, Is.EqualTo(new DefinitionId(TownA)));
            Assert.That(spawn.SpawnType, Is.EqualTo(SpawnType.Player));
            Assert.That(spawn.IsValid, Is.True);
        }

        [Test]
        public void Finding_a_maps_player_spawn_skips_monster_and_npc_points()
        {
            AddSpawn("spawn.field.monster", FieldA, SpawnType.Monster);
            AddSpawn("spawn.field.npc", FieldA, SpawnType.Npc);

            SpawnPointDefinition found = TravelService.FindPlayerSpawn(
                new DefinitionId(FieldA), SpawnPoints);

            Assert.That(found, Is.Not.Null);
            Assert.That(found.Id, Is.EqualTo(new DefinitionId(FieldASpawn)));
        }

        [Test]
        public void A_map_with_no_player_spawn_resolves_to_nothing_rather_than_the_origin()
        {
            AddMapDefinition("map.bare", MapCategory.Field);

            Assert.That(TravelService.FindPlayerSpawn(new DefinitionId("map.bare"), SpawnPoints),
                Is.Null, "there is no coordinate to fall back to, and that is the point");
        }

        // ---- location ------------------------------------------------------------------

        [Test]
        public void Arriving_requires_a_spawn_and_takes_its_position()
        {
            var location = new CharacterLocationState(Character);

            Assert.That(location.HasArrived, Is.False);

            SpawnPointDefinition spawn;
            SpawnPoints.TryGet(new DefinitionId(TownASpawn), out spawn);

            Assert.That(location.ArriveAt(spawn), Is.True);
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
            Assert.That(location.CurrentSpawnPoint, Is.EqualTo(new DefinitionId(TownASpawn)));
            Assert.That(location.Position.X, Is.EqualTo(spawn.X));
            Assert.That(location.Position.Z, Is.EqualTo(spawn.Z));
        }

        [Test]
        public void Arriving_at_a_monster_point_or_at_nothing_is_refused()
        {
            SpawnPointDefinition monsterPoint = AddSpawn("spawn.mob", FieldA, SpawnType.Monster);
            var location = new CharacterLocationState(Character);

            Assert.That(location.ArriveAt(monsterPoint), Is.False,
                "a player arriving where monsters spawn is a content mistake");
            Assert.That(location.ArriveAt(null), Is.False);
            Assert.That(location.HasArrived, Is.False);
        }

        [Test]
        public void There_is_no_gameplay_call_that_puts_a_character_on_a_map_alone()
        {
            // Rule 11.9 as a structural property: nothing exposes a map-only arrival, so a
            // caller holding a map id cannot express the move and must go through a portal
            // or an authored warp destination.
            System.Reflection.MethodInfo[] methods =
                typeof(CharacterLocationState).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (System.Reflection.MethodInfo method in methods)
            {
                if (!method.Name.StartsWith("Arrive") && !method.Name.StartsWith("Teleport")
                    && !method.Name.StartsWith("SetMap"))
                {
                    continue;
                }

                System.Reflection.ParameterInfo[] parameters = method.GetParameters();

                Assert.That(parameters.Length, Is.EqualTo(1), method.Name);
                Assert.That(parameters[0].ParameterType,
                    Is.EqualTo(typeof(SpawnPointDefinition)),
                    method.Name + " accepts something other than an authored spawn");
            }

            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/TravelService.cs");

            Assert.That(source, Does.Not.Contain("TeleportToMap"),
                "a general teleport would walk around every check above it");
        }

        // ---- portal traversal ----------------------------------------------------------

        [Test]
        public void Walking_through_a_portal_moves_the_traveller_to_the_authored_spawn()
        {
            CharacterLocationState location = StartAt(TownASpawn);

            TravelResult result = TravelService.TryTraversePortal(location,
                new DefinitionId(GateToField), Context());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.SourceMap, Is.EqualTo(new DefinitionId(TownA)));
            Assert.That(result.DestinationMap, Is.EqualTo(new DefinitionId(FieldA)));
            Assert.That(result.DestinationSpawn, Is.EqualTo(new DefinitionId(FieldASpawn)));
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(FieldA)));
            Assert.That(location.Position.X, Is.EqualTo(50f), "placed at the authored point");
        }

        [Test]
        public void A_result_carries_ids_and_never_a_scene_path()
        {
            CharacterLocationState location = StartAt(TownASpawn);

            TravelResult result = TravelService.TryTraversePortal(location,
                new DefinitionId(GateToField), Context());

            string text = result.ToString();

            Assert.That(text, Does.Not.Contain(".unity"));
            Assert.That(text, Does.Not.Contain("scenes/"),
                "resolving a scene is presentation's job, not gameplay's");

            string[] lines = System.IO.File.ReadAllLines(
                "Assets/_Game/Scripts/Gameplay/TravelService.cs");

            foreach (string line in lines)
            {
                string code = line.TrimStart();

                // Prose may discuss the engine; code may not reference it.
                if (code.StartsWith("//") || code.StartsWith("///")) continue;

                Assert.That(code, Does.Not.Contain("UnityEngine"), line);
                Assert.That(code, Does.Not.Contain("SceneManager"), line);
                Assert.That(code, Does.Not.Contain(".unity"), line);
            }
        }

        [Test]
        public void An_unknown_or_disabled_portal_is_refused()
        {
            CharacterLocationState location = StartAt(TownASpawn);

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId("portal.nowhere"), Context()).Reason,
                Is.EqualTo(TravelRejection.UnknownPortal));

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId(ClosedGate), Context()).Reason,
                Is.EqualTo(TravelRejection.PortalDisabled));

            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)),
                "a refused journey leaves the traveller where they were");
        }

        [Test]
        public void A_portal_on_another_map_cannot_be_used()
        {
            CharacterLocationState location = StartAt(FieldASpawn);

            TravelResult result = TravelService.TryTraversePortal(location,
                new DefinitionId(GateToField), Context());

            Assert.That(result.Reason, Is.EqualTo(TravelRejection.WrongMap),
                "a client asserting it stands somewhere else proves nothing");
        }

        [Test]
        public void Standing_too_far_from_a_portal_is_refused()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(500f, 0f, 500f);

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId(GateToField), Context()).Reason,
                Is.EqualTo(TravelRejection.TooFar));
        }

        [Test]
        public void A_portal_with_no_radius_may_be_used_from_anywhere_on_its_map()
        {
            AddPortal("portal.anywhere", TownA, FieldA, FieldASpawn, radius: 0f);

            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(9999f, 0f, 9999f);

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId("portal.anywhere"), Context()).IsAccepted, Is.True);
        }

        [Test]
        public void A_destination_spawn_on_the_wrong_map_is_refused()
        {
            // The portal claims to lead to the field, but names the town's arrival point.
            AddPortal("portal.crossed", TownA, FieldA, TownASpawn);

            CharacterLocationState location = StartAt(TownASpawn);

            TravelResult result = TravelService.TryTraversePortal(location,
                new DefinitionId("portal.crossed"), Context());

            Assert.That(result.Reason, Is.EqualTo(TravelRejection.SpawnMapMismatch));
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
        }

        [Test]
        public void A_destination_spawn_that_is_not_for_players_is_refused()
        {
            AddSpawn("spawn.field.mob", FieldA, SpawnType.Monster);
            AddPortal("portal.tomob", TownA, FieldA, "spawn.field.mob");

            Assert.That(TravelService.TryTraversePortal(StartAt(TownASpawn),
                new DefinitionId("portal.tomob"), Context()).Reason,
                Is.EqualTo(TravelRejection.NotAPlayerSpawn));
        }

        [Test]
        public void A_missing_destination_map_or_spawn_is_refused()
        {
            AddPortal("portal.nomap", TownA, "map.deleted", FieldASpawn);
            AddPortal("portal.nospawn", TownA, FieldA, "spawn.deleted");

            Assert.That(TravelService.TryTraversePortal(StartAt(TownASpawn),
                new DefinitionId("portal.nomap"), Context()).Reason,
                Is.EqualTo(TravelRejection.UnknownDestinationMap));

            Assert.That(TravelService.TryTraversePortal(StartAt(TownASpawn),
                new DefinitionId("portal.nospawn"), Context()).Reason,
                Is.EqualTo(TravelRejection.UnknownDestinationSpawn));
        }

        [Test]
        public void A_level_gate_and_a_key_are_both_enforced()
        {
            AddPortal("portal.gated", TownA, FieldA, FieldASpawn, levelRequirement: 20,
                requiredItem: Key);

            CharacterLocationState location = StartAt(TownASpawn);
            ItemContainerState bag = Container(4);

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId("portal.gated"), Context(bag, level: 5)).Reason,
                Is.EqualTo(TravelRejection.LevelTooLow));

            Assert.That(TravelService.TryTraversePortal(location,
                new DefinitionId("portal.gated"), Context(bag, level: 25)).Reason,
                Is.EqualTo(TravelRejection.MissingRequiredItem));

            bag.Add(Stack(Key, 1), Items);

            TravelResult result = TravelService.TryTraversePortal(location,
                new DefinitionId("portal.gated"), Context(bag, level: 25));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(bag.CountOf(new DefinitionId(Key)), Is.EqualTo(1),
                "a key is checked, never consumed by walking");
        }

        [Test]
        public void Travelling_with_no_registries_is_refused_rather_than_guessed()
        {
            var empty = new TravelService.Context(null, null);

            Assert.That(TravelService.TryTraversePortal(StartAt(TownASpawn),
                new DefinitionId(GateToField), empty).Reason,
                Is.EqualTo(TravelRejection.MissingContext));

            Assert.That(TravelService.TryTraversePortal(null,
                new DefinitionId(GateToField), Context()).Reason,
                Is.EqualTo(TravelRejection.MissingContext));
        }

        // ---- warp destinations ---------------------------------------------------------

        [Test]
        public void An_authored_town_destination_is_reachable_without_a_portal()
        {
            CharacterLocationState location = StartAt(FieldASpawn);

            TravelResult result = TravelService.TryTravelToSpawn(location,
                new DefinitionId(TownA), new DefinitionId(TownASpawn), Context(),
                requireTown: true);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
        }

        [Test]
        public void A_field_and_a_boss_area_are_refused_when_a_town_is_required()
        {
            CharacterLocationState location = StartAt(TownASpawn);

            Assert.That(TravelService.TryTravelToSpawn(location, new DefinitionId(FieldA),
                new DefinitionId(FieldASpawn), Context(), requireTown: true).Reason,
                Is.EqualTo(TravelRejection.DestinationNotAllowed));

            Assert.That(TravelService.TryTravelToSpawn(location, new DefinitionId(BossA),
                new DefinitionId(BossASpawn), Context(), requireTown: true).Reason,
                Is.EqualTo(TravelRejection.DestinationNotAllowed));

            Assert.That(location.CurrentMap, Is.EqualTo(new DefinitionId(TownA)));
        }

        [Test]
        public void A_map_authored_inconsistently_is_refused_rather_than_given_the_benefit()
        {
            // Category says Town, the flag says otherwise. Content is wrong either way.
            AddMapDefinition("map.confused", MapCategory.Town, isTown: false);
            AddSpawn("spawn.confused", "map.confused");

            Assert.That(TravelService.TryTravelToSpawn(StartAt(TownASpawn),
                new DefinitionId("map.confused"), new DefinitionId("spawn.confused"),
                Context(), requireTown: true).Reason,
                Is.EqualTo(TravelRejection.DestinationNotAllowed));
        }

        // ---- content validation --------------------------------------------------------

        [Test]
        public void Well_formed_world_content_passes()
        {
            var validator = new DefinitionValidator(new IDefinitionValidationRule[]
            {
                new MapContentValidationRule()
            });

            MapDefinition town;
            Maps.TryGet(new DefinitionId(TownA), out town);

            PortalDefinition portal;
            Portals.TryGet(new DefinitionId(GateToField), out portal);

            SpawnPointDefinition spawn;
            SpawnPoints.TryGet(new DefinitionId(TownASpawn), out spawn);

            Assert.That(validator.Validate(town, Lookup()).IsValid, Is.True);
            Assert.That(validator.Validate(portal, Lookup()).IsValid, Is.True);
            Assert.That(validator.Validate(spawn, Lookup()).IsValid, Is.True);
        }

        [Test]
        public void A_category_that_disagrees_with_the_flags_is_an_error()
        {
            MapDefinition confused = AddMapDefinition("map.confused", MapCategory.Town,
                isTown: false);

            ValidationReport report = Validate(confused);

            Assert.That(HasError(report, "the two must agree"), Is.True,
                "one system would treat it as a town and another would not");
        }

        [Test]
        public void A_map_that_is_both_a_town_and_a_boss_area_is_an_error()
        {
            MapDefinition impossible = AddMapDefinition("map.both", MapCategory.Town,
                isTown: true, isBossArea: true);

            Assert.That(HasError(Validate(impossible), "no warp rule can hold"), Is.True);
        }

        [Test]
        public void A_portal_with_no_destination_spawn_is_an_error()
        {
            PortalDefinition portal = AddPortal("portal.blank", TownA, FieldA, null);

            Assert.That(HasError(Validate(portal), "nowhere to arrive"), Is.True);
        }

        [Test]
        public void A_portal_naming_deleted_content_is_an_error()
        {
            PortalDefinition portal = AddPortal("portal.ghost", TownA, "map.deleted",
                "spawn.deleted");

            ValidationReport report = Validate(portal);

            Assert.That(HasError(report, "does not resolve"), Is.True);
        }

        [Test]
        public void A_destination_spawn_on_another_map_is_caught_by_the_cross_check()
        {
            AddPortal("portal.crossed", TownA, FieldA, TownASpawn);

            var report = new ValidationReport();
            MapContentValidationRule.ValidatePortalDestinations(Portals, SpawnPoints, report);

            Assert.That(HasError(report, "but the portal leads to"), Is.True);
        }

        [Test]
        public void A_town_with_no_player_spawn_is_an_error_and_a_field_only_warns()
        {
            AddMapDefinition("map.emptytown", MapCategory.Town, isTown: true);
            AddMapDefinition("map.emptyfield", MapCategory.Field);

            var report = new ValidationReport();
            MapContentValidationRule.ValidatePlayerSpawns(Maps, SpawnPoints, report);

            Assert.That(HasError(report, "town authors no player spawn"), Is.True,
                "nothing could warp to it at all");
            Assert.That(report.WarningCount, Is.GreaterThan(0), "and the field is only a warning");
        }

        [Test]
        public void Inline_map_portals_are_flagged_as_not_used_by_traversal()
        {
            MapDefinition map = AddMapDefinition("map.legacy", MapCategory.Field);
            SetPrivate(map, "_portals", new[]
            {
                new MapPortal(new DefinitionId(FieldA), Vector3.zero, Vector3.zero, 0, default)
            });

            ValidationReport report = Validate(map);

            Assert.That(report.WarningCount, Is.GreaterThan(0),
                "Phase 04 inline portals carry a position and no identity, so travel "
                + "cannot use them; silently ignoring them would hide that");
        }
    }
}
