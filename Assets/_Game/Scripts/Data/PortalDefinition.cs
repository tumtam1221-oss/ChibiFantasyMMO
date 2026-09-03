using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One authored way from one map to another.
    /// </summary>
    /// <remarks>
    /// <b>A destination is a spawn point, never coordinates.</b>
    /// <see cref="DestinationSpawn"/> is a reference that either resolves to an authored
    /// place on the destination map or does not. There is no position here for travel to
    /// fall back to, which is what makes "never silently arrive at the world origin"
    /// structural rather than a rule somebody has to remember.
    ///
    /// <b>Addressable, unlike the inline <see cref="MapPortal"/>.</b> Phase 04 authored
    /// portals as an array on the map, with an exit <em>position</em> and no identity. Those
    /// remain valid content and are untouched, but they cannot be named by a travel request
    /// and cannot resolve to a validated spawn, so traversal uses this. The map validation
    /// rule points that out rather than leaving the two silently overlapping.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>map_portal</c> table:
    /// portal id, source map, destination map, destination spawn, entry position, radius,
    /// enabled, and its requirements.
    /// </remarks>
    public sealed class PortalDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;

        [SerializeField] private DefinitionId _sourceMap;
        [SerializeField] private DefinitionId _destinationMap;
        [SerializeField] private DefinitionId _destinationSpawn;

        [Header("Placement on the source map")]
        [SerializeField] private float _entryX;
        [SerializeField] private float _entryY;
        [SerializeField] private float _entryZ;

        [Tooltip("How close a player must stand. Zero or less means proximity is not checked.")]
        [SerializeField] private float _entryRadius = 2f;

        [Header("Requirements")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("Level a traveller must have reached. Zero means none.")]
        [SerializeField] private int _levelRequirement;

        [Tooltip("Item a traveller must hold. Invalid means none. Not consumed by travel.")]
        [SerializeField] private DefinitionId _requiredItem;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>Reference to the <see cref="MapDefinition"/> the portal stands on.</summary>
        public DefinitionId SourceMap => _sourceMap;

        /// <summary>Reference to the <see cref="MapDefinition"/> it leads to.</summary>
        public DefinitionId DestinationMap => _destinationMap;

        /// <summary>
        /// Reference to the <see cref="SpawnPointDefinition"/> a traveller arrives at.
        /// </summary>
        /// <remarks>Validated to belong to <see cref="DestinationMap"/> and to be a
        /// <see cref="SpawnType.Player"/> point. A portal landing someone on the wrong map's
        /// spawn is a content mistake that must fail loudly.</remarks>
        public DefinitionId DestinationSpawn => _destinationSpawn;

        public float EntryX => _entryX;

        public float EntryY => _entryY;

        public float EntryZ => _entryZ;

        /// <summary>Zero or less means a traveller may use it from anywhere on the map.</summary>
        public float EntryRadius => _entryRadius;

        /// <summary>
        /// Whether the portal works.
        /// </summary>
        /// <remarks>Content's switch for a portal that exists but is closed -- a gate opened
        /// by an event, a dungeon under maintenance. Disabled is a refusal, not an absence,
        /// so a UI can still show it greyed.</remarks>
        public bool Enabled => _enabled;

        public int LevelRequirement => _levelRequirement;

        /// <summary>
        /// A key the traveller must hold.
        /// </summary>
        /// <remarks>Checked, never consumed: a dungeon key that vanished on entry would be
        /// a different design decision, and one content would have to author explicitly
        /// through item use rather than get as a side effect of walking.</remarks>
        public DefinitionId RequiredItem => _requiredItem;

        public override string ToString()
        {
            return Id + ": " + _sourceMap + " -> " + _destinationMap + " @" + _destinationSpawn;
        }
    }
}
