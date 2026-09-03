using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE third-person camera tuning for PHASE 07.1. All values are provisional.
    /// </summary>
    [CreateAssetMenu(menuName = "ChibiFantasy/Prototype/Camera Settings",
                     fileName = "ProtoCameraSettings")]
    public sealed class ProtoCameraSettings : ScriptableObject
    {
        [Header("Target - PROTOTYPE")]
        [Tooltip("Height above the character root that the camera looks at.")]
        public float followHeight = 0.85f;

        [Header("Distance (m) - PROTOTYPE")]
        public float defaultDistance = 3.0f;
        public float minDistance = 1.2f;
        public float maxDistance = 6.0f;

        [Header("Pitch (degrees) - PROTOTYPE")]
        public float pitchMin = -20f;
        public float pitchMax = 60f;
        public float startPitch = 15f;

        [Header("Sensitivity - PROTOTYPE")]
        [Tooltip("Degrees of yaw per unit of mouse X delta.")]
        public float orbitSensitivityX = 0.15f;

        [Tooltip("Degrees of pitch per unit of mouse Y delta.")]
        public float orbitSensitivityY = 0.12f;

        [Tooltip("Metres of zoom per unit of scroll. One wheel notch is typically 120.")]
        public float zoomSensitivity = 0.004f;

        [Header("Smoothing - PROTOTYPE")]
        public float positionSmoothTime = 0.06f;
        public float zoomSmoothTime = 0.10f;

        [Header("Collision - PROTOTYPE")]
        public float collisionRadius = 0.20f;

        [Tooltip("Keeps the camera from dropping through the floor.")]
        public float minHeightAboveGround = 0.25f;

        public LayerMask collisionMask = ~0;
    }
}
