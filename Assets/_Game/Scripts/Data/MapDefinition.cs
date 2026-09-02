using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad kind of map, driving rules and UI treatment.</summary>
    public enum MapCategory
    {
        Field = 0,
        Town = 1,
        Dungeon = 2,
        Instance = 3,
        Arena = 4,
        BossArena = 5
    }

    /// <summary>
    /// An authored exit from one map to another.
    /// </summary>
    /// <remarks>
    /// Declares where a portal goes and what it demands. Executing travel, streaming the
    /// destination scene and handing the player over between channels are later systems.
    /// </remarks>
    [Serializable]
    public struct MapPortal
    {
        [SerializeField] private DefinitionId _targetMap;
        [SerializeField] private Vector3 _entryPosition;
        [SerializeField] private Vector3 _exitPosition;
        [SerializeField] private int _levelRequirement;
        [SerializeField] private DefinitionId _requiredItem;

        public MapPortal(DefinitionId targetMap, Vector3 entryPosition, Vector3 exitPosition,
            int levelRequirement, DefinitionId requiredItem)
        {
            _targetMap = targetMap;
            _entryPosition = entryPosition;
            _exitPosition = exitPosition;
            _levelRequirement = levelRequirement;
            _requiredItem = requiredItem;
        }

        /// <summary>Reference to the destination <see cref="MapDefinition"/>.</summary>
        public DefinitionId TargetMap => _targetMap;

        /// <summary>Where the portal sits on this map.</summary>
        public Vector3 EntryPosition => _entryPosition;

        /// <summary>Where the traveller arrives on the destination map.</summary>
        public Vector3 ExitPosition => _exitPosition;

        public int LevelRequirement => _levelRequirement;

        /// <summary>Optional item gate, for example a warp scroll or dungeon key.</summary>
        public DefinitionId RequiredItem => _requiredItem;
    }

    /// <summary>
    /// What a map is.
    /// </summary>
    /// <remarks>
    /// No scene loading, portal travel, spawning or placement happens here. Which channel
    /// or world instance a player currently occupies is server-owned runtime state.
    /// </remarks>
    public sealed class MapDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private MapCategory _category = MapCategory.Field;
        [SerializeField] private AssetRef _scene;

        [SerializeField] private int _recommendedLevelMin;
        [SerializeField] private int _recommendedLevelMax;

        [SerializeField] private bool _pkAllowed;
        [SerializeField] private bool _isSafeZone;
        [SerializeField] private bool _isTown;
        [SerializeField] private bool _isBossArea;

        [SerializeField] private MapPortal[] _portals = new MapPortal[0];
        [SerializeField] private DefinitionId _monsterSpawnTable;
        [SerializeField] private DefinitionId _npcPlacement;

        public LocalizationKey NameKey => _nameKey;

        public MapCategory Category => _category;

        /// <summary>Indirect scene reference. Resolution is a loading concern.</summary>
        public AssetRef Scene => _scene;

        public int RecommendedLevelMin => _recommendedLevelMin;

        public int RecommendedLevelMax => _recommendedLevelMax;

        public bool PkAllowed => _pkAllowed;

        public bool IsSafeZone => _isSafeZone;

        public bool IsTown => _isTown;

        public bool IsBossArea => _isBossArea;

        public MapPortal[] Portals => _portals;

        /// <summary>Reference to a spawn table definition.</summary>
        public DefinitionId MonsterSpawnTable => _monsterSpawnTable;

        /// <summary>Reference to an NPC placement definition.</summary>
        public DefinitionId NpcPlacement => _npcPlacement;
    }
}
