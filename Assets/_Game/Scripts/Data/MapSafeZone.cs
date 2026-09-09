using System;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A circle on a map where no monster may stand, spawn, walk into or take aim.
    /// </summary>
    /// <remarks>
    /// <b>Why a zone and not a flag.</b> <see cref="MapDefinition.IsSafeZone"/> closes a
    /// whole map. A town whose walls the player can walk out of to meet monsters needs the
    /// safety to stop at the wall, so the map stays open and the safe part is authored as
    /// geometry. A circle is enough for a walled town and a mountain path; a map that needs
    /// more authors more circles.
    ///
    /// <b>Authored data, enforced by the server.</b> The client only reads this to draw
    /// things; every refusal happens in <c>MonsterSpawnPlacement</c>, <c>MonsterMovement</c>
    /// and <c>MonsterWorldRuntime</c>, which is what makes the safety real.
    /// </remarks>
    [Serializable]
    public struct MapSafeZone
    {
        [SerializeField] private Vector2 _center;
        [SerializeField] private float _radius;

        public MapSafeZone(Vector2 center, float radius)
        {
            _center = center;
            _radius = radius < 0f ? 0f : radius;
        }

        /// <summary>World X/Z of the circle's centre.</summary>
        public Vector2 Center => _center;

        /// <summary>Radius in metres. Zero is an empty zone that contains nothing.</summary>
        public float Radius => _radius < 0f ? 0f : _radius;

        /// <summary>Whether a horizontal position is inside the circle.</summary>
        public bool Contains(float x, float z)
        {
            if (Radius <= 0f) return false;

            float dx = x - _center.x;
            float dz = z - _center.y;

            return dx * dx + dz * dz <= Radius * Radius;
        }
    }
}
