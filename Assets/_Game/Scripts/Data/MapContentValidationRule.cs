using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored maps, spawn points and portals are coherent.
    /// </summary>
    /// <remarks>
    /// The travel service already refuses malformed content at runtime, so this is not a
    /// safety net for the game -- it is one for whoever authors it. A portal whose
    /// destination spawn sits on the wrong map should fail in the content pass pointing at
    /// the row, not turn up as a player who arrives somewhere impossible.
    ///
    /// <b>The category and the flags must agree.</b> Phase 04 authored both
    /// <see cref="MapDefinition.Category"/> and <c>IsTown</c>/<c>IsBossArea</c>. Neither was
    /// replaced, because working content depends on both; instead a disagreement between
    /// them is an error. That is the only safe way to keep two representations of the same
    /// fact -- make it impossible for them to differ.
    ///
    /// Deterministic and read-only: nothing here mutates a definition or any runtime state.
    /// </remarks>
    public sealed class MapContentValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var map = definition as MapDefinition;
            if (map != null)
            {
                ValidateMap(map, lookup, report);
                return;
            }

            var spawn = definition as SpawnPointDefinition;
            if (spawn != null)
            {
                ValidateSpawn(spawn, lookup, report);
                return;
            }

            var portal = definition as PortalDefinition;
            if (portal != null) ValidatePortal(portal, lookup, report);
        }

        // ---- maps ----------------------------------------------------------------------

        private static void ValidateMap(MapDefinition map, IDefinitionLookup lookup,
            ValidationReport report)
        {
            // The two representations of "this is a town" must never disagree, or one system
            // will treat it as a town and another will not.
            if (map.IsTown != (map.Category == MapCategory.Town))
            {
                report.AddError(ValidationCode.InvalidConfiguration, map.Id,
                    "IsTown is " + map.IsTown + " but the category is " + map.Category
                    + "; the two must agree.");
            }

            bool categorySaysBoss = map.Category == MapCategory.BossArena;

            if (map.IsBossArea != categorySaysBoss)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, map.Id,
                    "IsBossArea is " + map.IsBossArea + " but the category is " + map.Category
                    + "; a boss area authored inconsistently is refused by travel.");
            }

            if (map.IsTown && map.IsBossArea)
            {
                report.AddError(ValidationCode.InvalidConfiguration, map.Id,
                    "The map is both a town and a boss area, so no warp rule can hold.");
            }

            if (map.RecommendedLevelMax > 0 && map.RecommendedLevelMax < map.RecommendedLevelMin)
            {
                report.AddError(ValidationCode.ValueOutOfRange, map.Id,
                    "The recommended level range is inverted.");
            }

            if (!map.Scene.IsValid)
            {
                // Not an error: a map may exist as content before its scene does.
                report.AddWarning(ValidationCode.InvalidConfiguration, map.Id,
                    "The map references no scene, so presentation cannot load it.");
            }

            if (map.MonsterSpawnTable.IsValid)
            {
                Require(lookup, map.Id, map.MonsterSpawnTable, "Monster spawn table", report);
            }

            if (map.NpcPlacement.IsValid)
            {
                Require(lookup, map.Id, map.NpcPlacement, "NPC placement", report);
            }

            // The inline portals Phase 04 authored carry an exit POSITION and no identity,
            // so travel cannot use them. Flagged rather than silently ignored.
            MapPortal[] portals = map.Portals;

            if (portals != null && portals.Length > 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, map.Id,
                    "The map authors " + portals.Length + " inline MapPortal entries. Travel "
                    + "uses addressable PortalDefinition assets, which resolve to a validated "
                    + "spawn; these are ignored by traversal.");

                for (int i = 0; i < portals.Length; i++)
                {
                    if (!portals[i].TargetMap.IsValid) continue;
                    Require(lookup, map.Id, portals[i].TargetMap,
                        "Inline portal " + i + " target map", report);
                }
            }
        }

        // ---- spawn points --------------------------------------------------------------

        private static void ValidateSpawn(SpawnPointDefinition spawn, IDefinitionLookup lookup,
            ValidationReport report)
        {
            if (!spawn.Map.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, spawn.Id,
                    "The spawn point belongs to no map.");
                return;
            }

            Require(lookup, spawn.Id, spawn.Map, "Map", report);
        }

        // ---- portals -------------------------------------------------------------------

        private static void ValidatePortal(PortalDefinition portal, IDefinitionLookup lookup,
            ValidationReport report)
        {
            if (!portal.SourceMap.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, portal.Id,
                    "The portal stands on no map.");
            }
            else
            {
                Require(lookup, portal.Id, portal.SourceMap, "Source map", report);
            }

            if (!portal.DestinationMap.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, portal.Id,
                    "The portal leads nowhere.");
            }
            else
            {
                Require(lookup, portal.Id, portal.DestinationMap, "Destination map", report);
            }

            if (!portal.DestinationSpawn.IsValid)
            {
                report.AddError(ValidationCode.InvalidConfiguration, portal.Id,
                    "The portal names no destination spawn, so a traveller would have "
                    + "nowhere to arrive.");
            }
            else
            {
                Require(lookup, portal.Id, portal.DestinationSpawn, "Destination spawn", report);
            }

            if (portal.SourceMap.IsValid && portal.SourceMap == portal.DestinationMap)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, portal.Id,
                    "The portal leads back to its own map.");
            }

            if (portal.EntryRadius < 0f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, portal.Id,
                    "The entry radius is negative.");
            }

            if (portal.LevelRequirement < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, portal.Id,
                    "The level requirement is negative.");
            }

            if (portal.RequiredItem.IsValid)
            {
                Require(lookup, portal.Id, portal.RequiredItem, "Required item", report);
            }
        }

        /// <summary>
        /// Cross-checks a portal against the spawn it names.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Validate"/> because it needs the spawn's own definition,
        /// not just its existence, and <see cref="IDefinitionLookup"/> deliberately answers
        /// only "does this id exist". A caller with both registries runs this; the runtime
        /// check in <c>TravelService</c> is what actually protects a player either way.
        /// </remarks>
        public static void ValidatePortalDestinations(
            IDefinitionRegistry<PortalDefinition> portals,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints, ValidationReport report)
        {
            if (portals == null || spawnPoints == null || report == null) return;

            IReadOnlyList<PortalDefinition> all = portals.All;

            for (int i = 0; i < all.Count; i++)
            {
                PortalDefinition portal = all[i];
                if (portal == null || !portal.DestinationSpawn.IsValid) continue;

                SpawnPointDefinition spawn;
                if (!spawnPoints.TryGet(portal.DestinationSpawn, out spawn) || spawn == null)
                {
                    continue;   // already reported as a missing reference
                }

                if (spawn.Map != portal.DestinationMap)
                {
                    report.AddError(ValidationCode.SlotMismatch, portal.Id,
                        "Destination spawn '" + spawn.Id + "' is on map '" + spawn.Map
                        + "', but the portal leads to '" + portal.DestinationMap + "'.");
                }

                if (spawn.SpawnType != SpawnType.Player)
                {
                    report.AddError(ValidationCode.SlotMismatch, portal.Id,
                        "Destination spawn '" + spawn.Id + "' is a " + spawn.SpawnType
                        + " point, which no player may arrive at.");
                }
            }
        }

        /// <summary>
        /// Checks that every map players can reach has somewhere for them to stand.
        /// </summary>
        /// <remarks>Run over the whole set rather than per definition, because "this map has
        /// a player spawn" is a question about the spawn registry, not about the map.</remarks>
        public static void ValidatePlayerSpawns(IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints, ValidationReport report)
        {
            if (maps == null || spawnPoints == null || report == null) return;

            IReadOnlyList<MapDefinition> all = maps.All;

            for (int i = 0; i < all.Count; i++)
            {
                MapDefinition map = all[i];
                if (map == null) continue;

                bool found = false;

                for (int s = 0; s < spawnPoints.All.Count && !found; s++)
                {
                    SpawnPointDefinition spawn = spawnPoints.All[s];

                    found = spawn != null && spawn.Map == map.Id
                        && spawn.SpawnType == SpawnType.Player;
                }

                if (found) continue;

                // A town without one cannot be warped to at all, so it is the stricter case.
                if (map.IsTown)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, map.Id,
                        "The town authors no player spawn, so nothing can warp or travel to it.");
                    continue;
                }

                report.AddWarning(ValidationCode.InvalidConfiguration, map.Id,
                    "The map authors no player spawn, so no portal can lead to it.");
            }
        }

        private static void Require(IDefinitionLookup lookup, DefinitionId owner,
            DefinitionId reference, string what, ValidationReport report)
        {
            if (lookup == null || !reference.IsValid) return;
            if (lookup.Contains(reference)) return;

            report.AddError(ValidationCode.MissingReference, owner,
                what + " '" + reference + "' does not resolve to any definition.");
        }
    }
}
