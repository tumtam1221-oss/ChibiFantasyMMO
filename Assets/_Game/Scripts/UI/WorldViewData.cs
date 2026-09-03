using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>What a map banner needs.</summary>
    /// <remarks>A snapshot. The panel holding it cannot travel anywhere, because there is
    /// no map here to travel to -- only its name and how it should be presented.</remarks>
    public readonly struct MapViewData
    {
        public MapViewData(DefinitionId map, LocalizationKey nameKey, MapCategory category,
            bool isTown, bool isBossArea, bool pkAllowed)
        {
            Map = map;
            NameKey = nameKey;
            Category = category;
            IsTown = isTown;
            IsBossArea = isBossArea;
            PkAllowed = pkAllowed;
        }

        public DefinitionId Map { get; }

        public LocalizationKey NameKey { get; }

        public MapCategory Category { get; }

        public bool IsTown { get; }

        public bool IsBossArea { get; }

        /// <summary>Drives a warning banner, not a rule. The rule lives on the map.</summary>
        public bool PkAllowed { get; }

        public bool IsValid => Map.IsValid;

        public static MapViewData None => default;

        public override string ToString()
        {
            return IsValid ? Map + " (" + Category + ")" : "nowhere";
        }
    }

    /// <summary>One portal, as a prompt or a world map draws it.</summary>
    public readonly struct PortalViewData
    {
        public PortalViewData(DefinitionId portal, LocalizationKey nameKey,
            DefinitionId sourceMap, DefinitionId destinationMap,
            LocalizationKey destinationNameKey, MapCategory destinationCategory,
            bool enabled, bool inRange, int levelRequirement)
        {
            Portal = portal;
            NameKey = nameKey;
            SourceMap = sourceMap;
            DestinationMap = destinationMap;
            DestinationNameKey = destinationNameKey;
            DestinationCategory = destinationCategory;
            Enabled = enabled;
            IsInRange = inRange;
            LevelRequirement = levelRequirement;
        }

        public DefinitionId Portal { get; }

        public LocalizationKey NameKey { get; }

        public DefinitionId SourceMap { get; }

        public DefinitionId DestinationMap { get; }

        /// <summary>
        /// The destination map's own name key.
        /// </summary>
        /// <remarks>Resolved from the <see cref="MapDefinition"/> by the Client, so a map
        /// renamed once is renamed everywhere and no map name is written into a portal or
        /// into UI code.</remarks>
        public LocalizationKey DestinationNameKey { get; }

        public MapCategory DestinationCategory { get; }

        public bool Enabled { get; }

        /// <summary>
        /// Whether the player is close enough right now.
        /// </summary>
        /// <remarks>Advisory, like every other hint in this project:
        /// <c>TravelService</c> re-checks it and remains the authority.</remarks>
        public bool IsInRange { get; }

        public int LevelRequirement { get; }

        public bool IsValid => Portal.IsValid;

        /// <summary>Whether a prompt is worth showing. Not permission.</summary>
        public bool CanOffer => IsValid && Enabled && IsInRange;

        public static PortalViewData None => default;

        public override string ToString()
        {
            return IsValid ? Portal + " -> " + DestinationMap : "no portal";
        }
    }

    /// <summary>One NPC, as a prompt or a dialogue panel draws it.</summary>
    public readonly struct NpcViewData
    {
        private static readonly NpcRole[] NoRoles = new NpcRole[0];

        private readonly NpcRole[] _roles;

        public NpcViewData(DefinitionId npc, LocalizationKey nameKey, NPCCategory category,
            DefinitionId map, bool enabled, bool inRange, NpcRole[] roles)
        {
            Npc = npc;
            NameKey = nameKey;
            Category = category;
            Map = map;
            Enabled = enabled;
            IsInRange = inRange;
            _roles = roles ?? NoRoles;
        }

        public DefinitionId Npc { get; }

        public LocalizationKey NameKey { get; }

        public NPCCategory Category { get; }

        public DefinitionId Map { get; }

        public bool Enabled { get; }

        /// <summary>Advisory. <c>NpcInteractionService</c> re-checks and decides.</summary>
        public bool IsInRange { get; }

        /// <summary>
        /// What this NPC offers.
        /// </summary>
        /// <remarks>Resolved by the Client from the NPC's authored capabilities, so the
        /// panel draws one button per role without knowing what any role means.</remarks>
        public IReadOnlyList<NpcRole> Roles => _roles;

        public bool IsValid => Npc.IsValid;

        public bool CanOffer => IsValid && Enabled && IsInRange;

        public static NpcViewData None => default;

        public override string ToString()
        {
            return IsValid ? Npc + " (" + _roles.Length + " roles)" : "no npc";
        }
    }

    /// <summary>One edge of the world map: a map and where it leads.</summary>
    /// <remarks>
    /// Derived from the authored portals, never written by hand. A world map that hard-coded
    /// "town leads to field" would be a second description of the world, and it would be
    /// wrong the first time content changed.
    /// </remarks>
    public readonly struct WorldMapLinkViewData
    {
        public WorldMapLinkViewData(DefinitionId fromMap, DefinitionId toMap,
            LocalizationKey toMapNameKey, MapCategory toCategory, bool enabled)
        {
            FromMap = fromMap;
            ToMap = toMap;
            ToMapNameKey = toMapNameKey;
            ToCategory = toCategory;
            Enabled = enabled;
        }

        public DefinitionId FromMap { get; }

        public DefinitionId ToMap { get; }

        public LocalizationKey ToMapNameKey { get; }

        public MapCategory ToCategory { get; }

        public bool Enabled { get; }

        public override string ToString()
        {
            return FromMap + " -> " + ToMap;
        }
    }
}
