using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns maps, portals and NPCs into view data. The read half.
    /// </summary>
    /// <remarks>
    /// <b>Reads only.</b> Nothing here travels, interacts or moves a player; every output is
    /// a snapshot. Building a prompt twenty times costs nothing and changes nothing.
    ///
    /// <b>The world map is a projection.</b> <see cref="BuildWorldMapLinks"/> derives every
    /// connection from the authored portals -- there is no hand-written "town leads to
    /// field" anywhere, so a world map cannot disagree with the world.
    ///
    /// <b>Range hints are advisory.</b> They exist so a prompt does not appear for something
    /// the service would refuse. <c>TravelService</c> and
    /// <c>NpcInteractionService</c> re-check and remain the authority.
    /// </remarks>
    public static class WorldMapAdapter
    {
        /// <summary>The registries these views need.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<MapDefinition> maps,
                IDefinitionRegistry<SpawnPointDefinition> spawnPoints = null,
                IDefinitionRegistry<PortalDefinition> portals = null,
                IDefinitionRegistry<NPCDefinition> npcs = null)
            {
                Maps = maps;
                SpawnPoints = spawnPoints;
                Portals = portals;
                Npcs = npcs;
            }

            public IDefinitionRegistry<MapDefinition> Maps { get; }

            public IDefinitionRegistry<SpawnPointDefinition> SpawnPoints { get; }

            public IDefinitionRegistry<PortalDefinition> Portals { get; }

            public IDefinitionRegistry<NPCDefinition> Npcs { get; }

            public bool IsUsable => Maps != null;
        }

        /// <summary>What a banner should say about a map.</summary>
        public static MapViewData BuildMap(DefinitionId mapId, in Context context)
        {
            if (!context.IsUsable || !mapId.IsValid) return MapViewData.None;

            MapDefinition map;
            if (!context.Maps.TryGet(mapId, out map) || map == null) return MapViewData.None;

            return new MapViewData(mapId, map.NameKey, map.Category, map.IsTown,
                map.IsBossArea, map.PkAllowed);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the portals on the player's current map.
        /// </summary>
        /// <remarks>Only the current map's portals: a prompt for a gate on another map would
        /// be noise, and the service would refuse it anyway.</remarks>
        public static void BuildPortals(CharacterLocationState location, in Context context,
            List<PortalViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (location == null || !context.IsUsable || context.Portals == null) return;

            IReadOnlyList<PortalDefinition> all = context.Portals.All;

            for (int i = 0; i < all.Count; i++)
            {
                PortalDefinition portal = all[i];
                if (portal == null || portal.SourceMap != location.CurrentMap) continue;

                into.Add(Describe(portal, location, context));
            }
        }

        /// <summary>What a prompt should say about one portal.</summary>
        public static PortalViewData BuildPortal(DefinitionId portalId,
            CharacterLocationState location, in Context context)
        {
            if (!context.IsUsable || context.Portals == null) return PortalViewData.None;

            PortalDefinition portal;
            if (!portalId.IsValid || !context.Portals.TryGet(portalId, out portal) || portal == null)
                return PortalViewData.None;

            return Describe(portal, location, context);
        }

        private static PortalViewData Describe(PortalDefinition portal,
            CharacterLocationState location, in Context context)
        {
            MapDefinition destination;
            context.Maps.TryGet(portal.DestinationMap, out destination);

            bool inRange = true;

            if (location != null && portal.EntryRadius > 0f)
            {
                var entry = new CombatPosition(portal.EntryX, portal.EntryY, portal.EntryZ);

                inRange = location.IsOn(portal.SourceMap)
                    && location.Position.SqrDistanceTo(entry)
                        <= portal.EntryRadius * portal.EntryRadius;
            }

            return new PortalViewData(portal.Id, portal.NameKey, portal.SourceMap,
                portal.DestinationMap,
                destination == null ? default : destination.NameKey,
                destination == null ? MapCategory.Field : destination.Category,
                portal.Enabled, inRange, portal.LevelRequirement);
        }

        /// <summary>Fills <paramref name="into"/> with the NPCs on the player's current map.</summary>
        public static void BuildNpcs(CharacterLocationState location, in Context context,
            List<NpcViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (location == null || context.Npcs == null) return;

            IReadOnlyList<NPCDefinition> all = context.Npcs.All;

            for (int i = 0; i < all.Count; i++)
            {
                NPCDefinition npc = all[i];
                if (npc == null || npc.Map != location.CurrentMap) continue;

                into.Add(BuildNpc(npc, location, context));
            }
        }

        /// <summary>
        /// What a prompt should say about one NPC.
        /// </summary>
        /// <remarks>The roles are asked of the definition rather than derived here, so the
        /// UI and the service read the same answer from the same place.</remarks>
        public static NpcViewData BuildNpc(NPCDefinition npc, CharacterLocationState location,
            in Context context)
        {
            if (npc == null) return NpcViewData.None;

            var roles = new List<NpcRole>();

            for (int role = 0; role <= (int)NpcRole.Warp; role++)
            {
                if (npc.HasRole((NpcRole)role)) roles.Add((NpcRole)role);
            }

            var interaction = new NpcInteractionService.Context(context.Npcs,
                context.SpawnPoints);

            bool inRange = NpcInteractionService.CanReach(location, npc, interaction);

            return new NpcViewData(npc.Id, npc.NameKey, npc.Category, npc.Map, npc.Enabled,
                inRange, roles.ToArray());
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every connection the authored portals describe.
        /// </summary>
        /// <remarks>
        /// The world map, derived rather than written. Nothing here knows that any
        /// particular map leads anywhere; it reads the portals and reports what they say, so
        /// adding a gate updates the map with no UI change at all.
        /// </remarks>
        public static void BuildWorldMapLinks(in Context context, List<WorldMapLinkViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (!context.IsUsable || context.Portals == null) return;

            IReadOnlyList<PortalDefinition> all = context.Portals.All;

            for (int i = 0; i < all.Count; i++)
            {
                PortalDefinition portal = all[i];
                if (portal == null || !portal.SourceMap.IsValid || !portal.DestinationMap.IsValid)
                {
                    continue;
                }

                MapDefinition destination;
                context.Maps.TryGet(portal.DestinationMap, out destination);

                into.Add(new WorldMapLinkViewData(portal.SourceMap, portal.DestinationMap,
                    destination == null ? default : destination.NameKey,
                    destination == null ? MapCategory.Field : destination.Category,
                    portal.Enabled));
            }
        }
    }
}
