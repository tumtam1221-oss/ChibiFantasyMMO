using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What a spawn point is for.</summary>
    /// <remarks>
    /// Closed technical category: each value is validated differently and consumed by a
    /// different system. One enum rather than three separate point types, because a place
    /// in the world is the same idea whoever arrives there -- three unrelated coordinate
    /// systems is exactly what this exists to avoid.
    /// </remarks>
    public enum SpawnType
    {
        /// <summary>Where a player arrives. What a portal and a warp resolve to.</summary>
        Player = 0,

        /// <summary>Where monsters come from.</summary>
        Monster = 1,

        /// <summary>Where an NPC stands.</summary>
        Npc = 2
    }

    /// <summary>
    /// One authored place on a map.
    /// </summary>
    /// <remarks>
    /// <b>Addressable, so nothing has to carry coordinates.</b> A portal names a spawn
    /// point id, not a position; so does a warp scroll's destination and an NPC's
    /// placement. That is what makes "never silently arrive at the world origin" a
    /// property of the schema: there is no coordinate to fall back to, only a reference
    /// that either resolves or does not.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>map_spawn_point</c>
    /// table is an id, a map id, a type, three floats and a facing.
    ///
    /// Position is three floats rather than a <c>Vector3</c> for the reason
    /// <c>CombatPosition</c> gives: Gameplay holds no <c>using UnityEngine</c>, and a
    /// place in the world is data.
    /// </remarks>
    public sealed class SpawnPointDefinition : GameDefinition
    {
        [SerializeField] private DefinitionId _map;
        [SerializeField] private SpawnType _spawnType = SpawnType.Player;

        [SerializeField] private float _x;
        [SerializeField] private float _y;
        [SerializeField] private float _z;

        [Tooltip("Facing in degrees about the vertical axis.")]
        [SerializeField] private float _facingDegrees;

        /// <summary>Reference to the <see cref="MapDefinition"/> this point is on.</summary>
        /// <remarks>Held here rather than as a list on the map, so adding a spawn is one new
        /// asset rather than an edit to a shared one -- and so a spawn can never belong to
        /// two maps at once.</remarks>
        public DefinitionId Map => _map;

        public SpawnType SpawnType => _spawnType;

        public float X => _x;

        public float Y => _y;

        public float Z => _z;

        public float FacingDegrees => _facingDegrees;

        /// <summary>Whether the point is usable at all.</summary>
        public bool IsValid => Id.IsValid && _map.IsValid;

        public override string ToString()
        {
            return Id + " (" + _spawnType + " on " + _map + ")";
        }
    }
}
