using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a journey was refused.</summary>
    /// <remarks>A reason rather than a bare false, matching the rest of the project's
    /// vocabulary. Every one is checked before any state moves.</remarks>
    public enum TravelRejection
    {
        None = 0,

        /// <summary>No location state or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such portal could be resolved.</summary>
        UnknownPortal = 2,

        /// <summary>Content closed the portal.</summary>
        PortalDisabled = 3,

        /// <summary>The traveller is not on the map the portal stands on.</summary>
        WrongMap = 4,

        /// <summary>The traveller is too far from the portal to use it.</summary>
        TooFar = 5,

        /// <summary>The portal's destination map could not be resolved.</summary>
        UnknownDestinationMap = 6,

        /// <summary>The portal names no destination spawn, or it could not be resolved.</summary>
        UnknownDestinationSpawn = 7,

        /// <summary>The destination spawn belongs to a different map than the portal claims.</summary>
        SpawnMapMismatch = 8,

        /// <summary>The destination spawn is not somewhere a player may arrive.</summary>
        NotAPlayerSpawn = 9,

        /// <summary>The traveller has not reached the required level.</summary>
        LevelTooLow = 10,

        /// <summary>The traveller does not hold the required item.</summary>
        MissingRequiredItem = 11,

        /// <summary>The destination is not somewhere this kind of travel may reach.</summary>
        DestinationNotAllowed = 12
    }

    /// <summary>
    /// A resolved, validated journey.
    /// </summary>
    /// <remarks>
    /// <b>Ids, never a scene path.</b> Gameplay decided <em>where</em>; resolving that to a
    /// Unity scene is presentation's job and happens behind
    /// <c>MapDefinition.Scene</c>. A result carrying a scene name would make gameplay
    /// depend on a filename, which is exactly the coupling rule 11.25 forbids.
    ///
    /// <b>The destination is a spawn, not coordinates.</b> The client places the player at
    /// the authored point this names; it does not invent one, and there is nothing here for
    /// it to invent from.
    /// </remarks>
    public readonly struct TravelResult
    {
        private TravelResult(bool accepted, TravelRejection reason, DefinitionId portal,
            DefinitionId sourceMap, DefinitionId destinationMap, DefinitionId destinationSpawn)
        {
            IsAccepted = accepted;
            Reason = reason;
            Portal = portal;
            SourceMap = sourceMap;
            DestinationMap = destinationMap;
            DestinationSpawn = destinationSpawn;
        }

        public bool IsAccepted { get; }

        public TravelRejection Reason { get; }

        /// <summary>The portal used. Invalid for a warp, which uses none.</summary>
        public DefinitionId Portal { get; }

        public DefinitionId SourceMap { get; }

        public DefinitionId DestinationMap { get; }

        /// <summary>The authored place the traveller arrives at.</summary>
        public DefinitionId DestinationSpawn { get; }

        public static TravelResult Accepted(DefinitionId portal, DefinitionId sourceMap,
            DefinitionId destinationMap, DefinitionId destinationSpawn)
        {
            return new TravelResult(true, TravelRejection.None, portal, sourceMap,
                destinationMap, destinationSpawn);
        }

        public static TravelResult Rejected(TravelRejection reason, DefinitionId portal = default,
            DefinitionId sourceMap = default)
        {
            return new TravelResult(false, reason, portal, sourceMap, default, default);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;
            return SourceMap + " -> " + DestinationMap + " @" + DestinationSpawn;
        }
    }

    /// <summary>
    /// Moving a character between maps.
    /// </summary>
    /// <remarks>
    /// <b>Through a portal, or through an authored warp destination. Nothing else.</b> There
    /// is deliberately no <c>TryTravelTo(mapId)</c>: every entry point below starts from
    /// something content authored, and <c>CharacterLocationState</c> has no method that
    /// takes a map without a spawn. A caller holding only a map id cannot express the move
    /// at all, which is what makes portal validation impossible to walk around.
    ///
    /// <b>It loads nothing.</b> No scene, no prefab, no <c>UnityEngine</c> anything. It
    /// answers where a traveller may go and records that they went; the client resolves
    /// that to a scene and places them. Splitting it there is what lets a server run this
    /// headless later and keeps the client from being the authority on its own position.
    ///
    /// <b>Validate fully, then move.</b> The same contract every other service here keeps:
    /// a refused journey leaves the location state untouched.
    /// </remarks>
    public static class TravelService
    {
        /// <summary>Everything a journey needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<MapDefinition> maps,
                IDefinitionRegistry<SpawnPointDefinition> spawnPoints,
                IDefinitionRegistry<PortalDefinition> portals = null,
                ItemContainerState inventory = null,
                IDefinitionRegistry<ItemDefinition> items = null,
                int characterLevel = 1)
            {
                Maps = maps;
                SpawnPoints = spawnPoints;
                Portals = portals;
                Inventory = inventory;
                Items = items;
                CharacterLevel = characterLevel;
            }

            public IDefinitionRegistry<MapDefinition> Maps { get; }

            public IDefinitionRegistry<SpawnPointDefinition> SpawnPoints { get; }

            /// <summary>Needed only to traverse a portal by id.</summary>
            public IDefinitionRegistry<PortalDefinition> Portals { get; }

            /// <summary>Checked for a portal's required item. Never modified.</summary>
            public ItemContainerState Inventory { get; }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public int CharacterLevel { get; }

            public bool IsUsable => Maps != null && SpawnPoints != null;
        }

        /// <summary>
        /// Walks a character through a portal.
        /// </summary>
        /// <param name="location">Where they are. Updated only on success.</param>
        /// <param name="portalId">Which portal they are trying to use.</param>
        /// <param name="context">Registries, inventory and level.</param>
        public static TravelResult TryTraversePortal(CharacterLocationState location,
            DefinitionId portalId, in Context context)
        {
            if (location == null || !context.IsUsable || context.Portals == null)
                return TravelResult.Rejected(TravelRejection.MissingContext, portalId);

            PortalDefinition portal;
            if (!portalId.IsValid || !context.Portals.TryGet(portalId, out portal) || portal == null)
                return TravelResult.Rejected(TravelRejection.UnknownPortal, portalId);

            if (!portal.Enabled)
                return TravelResult.Rejected(TravelRejection.PortalDisabled, portalId,
                    portal.SourceMap);

            // The traveller must actually be standing on the map the portal is on. A client
            // asserting otherwise proves nothing; this is the check a server runs.
            if (!location.IsOn(portal.SourceMap))
                return TravelResult.Rejected(TravelRejection.WrongMap, portalId, portal.SourceMap);

            if (portal.EntryRadius > 0f)
            {
                var entry = new CombatPosition(portal.EntryX, portal.EntryY, portal.EntryZ);

                if (location.Position.SqrDistanceTo(entry) > portal.EntryRadius * portal.EntryRadius)
                {
                    return TravelResult.Rejected(TravelRejection.TooFar, portalId,
                        portal.SourceMap);
                }
            }

            if (portal.LevelRequirement > 0 && context.CharacterLevel < portal.LevelRequirement)
                return TravelResult.Rejected(TravelRejection.LevelTooLow, portalId,
                    portal.SourceMap);

            if (portal.RequiredItem.IsValid)
            {
                // Checked, never consumed. A key that vanished on entry would be a design
                // decision content must make explicitly through item use.
                if (context.Inventory == null || context.Inventory.CountOf(portal.RequiredItem) < 1)
                {
                    return TravelResult.Rejected(TravelRejection.MissingRequiredItem, portalId,
                        portal.SourceMap);
                }
            }

            SpawnPointDefinition spawn;
            TravelRejection destination = ResolveDestination(portal.DestinationMap,
                portal.DestinationSpawn, context, out spawn);

            if (destination != TravelRejection.None)
                return TravelResult.Rejected(destination, portalId, portal.SourceMap);

            // ---- everything is resolved; nothing below can fail ------------------------

            DefinitionId sourceMap = location.CurrentMap;
            location.ArriveAt(spawn);

            return TravelResult.Accepted(portalId, sourceMap, portal.DestinationMap, spawn.Id);
        }

        /// <summary>
        /// Sends a character to an authored destination that is not a portal.
        /// </summary>
        /// <remarks>
        /// What a warp scroll's resolved destination goes through. It is not a general
        /// teleport: the caller must already hold a destination that content authored and a
        /// service validated -- <c>ItemUseService</c> establishes that the item declared
        /// itself a warp and that the map is a town before this is ever reached.
        ///
        /// <paramref name="requireTown"/> is the rule that keeps a scroll out of a field or
        /// a boss area, restated here so the check cannot be skipped by calling this
        /// directly.
        /// </remarks>
        public static TravelResult TryTravelToSpawn(CharacterLocationState location,
            DefinitionId destinationMap, DefinitionId destinationSpawn, in Context context,
            bool requireTown = false)
        {
            if (location == null || !context.IsUsable)
                return TravelResult.Rejected(TravelRejection.MissingContext);

            SpawnPointDefinition spawn;
            TravelRejection destination = ResolveDestination(destinationMap, destinationSpawn,
                context, out spawn);

            if (destination != TravelRejection.None)
                return TravelResult.Rejected(destination, default, location.CurrentMap);

            if (requireTown)
            {
                MapDefinition map;
                context.Maps.TryGet(destinationMap, out map);

                if (!IsTown(map))
                {
                    return TravelResult.Rejected(TravelRejection.DestinationNotAllowed,
                        default, location.CurrentMap);
                }
            }

            DefinitionId sourceMap = location.CurrentMap;
            location.ArriveAt(spawn);

            return TravelResult.Accepted(default, sourceMap, destinationMap, spawn.Id);
        }

        /// <summary>
        /// Finds the first player spawn authored on a map.
        /// </summary>
        /// <remarks>
        /// How a warp destination becomes a place, since a scroll names a map and not a
        /// point. Returns null when the map authors none, which is a content error the
        /// caller must refuse rather than paper over with an origin.
        /// </remarks>
        public static SpawnPointDefinition FindPlayerSpawn(DefinitionId map,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints)
        {
            if (!map.IsValid || spawnPoints == null) return null;

            for (int i = 0; i < spawnPoints.All.Count; i++)
            {
                SpawnPointDefinition spawn = spawnPoints.All[i];

                if (spawn == null || spawn.Map != map) continue;
                if (spawn.SpawnType != SpawnType.Player) continue;

                return spawn;
            }

            return null;
        }

        /// <summary>
        /// Whether a map is somewhere a town warp may reach.
        /// </summary>
        /// <remarks>
        /// The single definition of that question, so the item-use path and the travel path
        /// cannot disagree. Both the category and the flag must agree, and a boss area is
        /// refused whatever else it claims -- a map authored inconsistently is refused
        /// rather than given the benefit of the doubt.
        /// </remarks>
        public static bool IsTown(MapDefinition map)
        {
            if (map == null) return false;
            if (map.IsBossArea) return false;

            return map.IsTown && map.Category == MapCategory.Town;
        }

        /// <summary>
        /// Resolves and checks a destination.
        /// </summary>
        /// <remarks>Shared by both entry points so a portal and a warp cannot end up
        /// enforcing different things about where someone may land.</remarks>
        private static TravelRejection ResolveDestination(DefinitionId destinationMap,
            DefinitionId destinationSpawn, in Context context, out SpawnPointDefinition spawn)
        {
            spawn = null;

            MapDefinition map;
            if (!destinationMap.IsValid
                || !context.Maps.TryGet(destinationMap, out map) || map == null)
            {
                return TravelRejection.UnknownDestinationMap;
            }

            if (!destinationSpawn.IsValid
                || !context.SpawnPoints.TryGet(destinationSpawn, out spawn) || spawn == null)
            {
                spawn = null;
                return TravelRejection.UnknownDestinationSpawn;
            }

            // A spawn on another map would land the traveller somewhere the portal never
            // claimed to lead.
            if (spawn.Map != destinationMap)
            {
                spawn = null;
                return TravelRejection.SpawnMapMismatch;
            }

            if (spawn.SpawnType != SpawnType.Player)
            {
                spawn = null;
                return TravelRejection.NotAPlayerSpawn;
            }

            return TravelRejection.None;
        }
    }
}
