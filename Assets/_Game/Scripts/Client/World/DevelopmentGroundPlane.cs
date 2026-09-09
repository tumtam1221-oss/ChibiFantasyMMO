// Development only. A production client compiles none of this file.
#if DEVELOPMENT_BUILD || UNITY_EDITOR

using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Something to click on, until this world has ground.
    /// </summary>
    /// <remarks>
    /// <b>This is not level art and must never be described as such.</b> The honest state of
    /// the project: <c>map.harbor_town</c> authors an empty scene address, no map scene
    /// exists on disk, and the world contains no geometry whatsoever -- the "floor" visible
    /// in a running client is the sky. There is consequently nothing for a click to land on,
    /// and click-to-move cannot be exercised at all without something under the character's
    /// feet.
    ///
    /// <b>What it is.</b> One flat collider at the height the server spawns characters,
    /// large enough to click anywhere the camera can see, with a plain material so a person
    /// can tell where they are pointing. It is the same admission the monster capsules and
    /// the loot cubes are: a placeholder that makes the real systems playable while the
    /// content they present does not exist yet.
    ///
    /// <b>It decides nothing.</b> No walkability, no navigation, no collision rules and no
    /// authority. A click on it produces a world point; the server still decides whether the
    /// character may end up there, and would refuse a position it did not like whatever this
    /// plane says. When a real map scene arrives, this stops being composed and nothing else
    /// changes.
    /// </remarks>
    public sealed class DevelopmentGroundPlane : MonoBehaviour
    {
        /// <summary>How wide the clickable area is, in metres.</summary>
        /// <remarks>Two hundred: comfortably past the far clip of a third-person camera at
        /// maximum zoom, so a click never falls off the edge of the world during a test.</remarks>
        private const float Extent = 200f;

        private GameObject _plane;
        private Material _material;

        /// <summary>Whether a plane is currently standing in for the ground.</summary>
        public bool IsPresent => _plane != null;

        /// <summary>
        /// Builds the plane, unless this world already has ground of its own.
        /// </summary>
        /// <remarks>The check matters: the day a map scene exists, composing this as well
        /// would put an invisible floor over it and clicks would land on the wrong one.</remarks>
        public void Compose(float height = 0f)
        {
            if (_plane != null) return;

            if (HasRealGround()) return;

            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);

            _plane.name = "Ground Placeholder (development)";
            _plane.transform.SetParent(transform, worldPositionStays: true);

            // A Unity plane is ten metres across at scale one.
            _plane.transform.localScale = new Vector3(Extent / 10f, 1f, Extent / 10f);
            _plane.transform.position = new Vector3(0f, height, 0f);

            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.30f, 0.32f, 0.36f),
            };

            _plane.GetComponent<Renderer>().sharedMaterial = _material;
        }

        /// <summary>
        /// Whether anything in the loaded scenes is already a floor.
        /// </summary>
        /// <remarks>A collider that is not a placeholder, not a trigger, and not attached to
        /// something networked. Deliberately conservative: a false positive here means no
        /// placeholder and a world nobody can click, so it looks for real static geometry
        /// rather than guessing.</remarks>
        private static bool HasRealGround()
        {
            foreach (Collider collider in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (collider == null || collider.isTrigger) continue;

                if (collider.GetComponentInParent<FishNet.Object.NetworkObject>() != null)
                {
                    continue;
                }

                if (collider.GetComponentInParent<DevelopmentMonsterMarker>() != null) continue;
                if (collider.GetComponentInParent<WorldLootMarker>() != null) continue;
                if (collider.GetComponentInParent<DevelopmentGroundPlane>() != null) continue;

                return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            if (_plane != null) Destroy(_plane);

            if (_material != null) Destroy(_material);
        }
    }
}

#endif
