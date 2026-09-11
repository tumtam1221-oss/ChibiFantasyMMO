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

        /// <summary>
        /// How far one wheel notch moves the camera, in metres.
        /// </summary>
        /// <remarks>
        /// <b>Per notch, not per raw unit.</b> This was metres-per-raw-unit at 0.004, sized
        /// for the legacy Input Manager's 120-per-notch wheel. The Input System reports
        /// roughly 1.0 per notch, which made a notch worth four millimetres against a range
        /// of 4.8 metres -- about twelve hundred notches to cross it. It read as a camera
        /// that did not zoom at all, and it was measured doing exactly that: a live rig sat
        /// at 3.016 metres after several notches against a 3.0 default.
        ///
        /// <see cref="ProtoThirdPersonCamera.NotchesFrom"/> is what turns a raw wheel value
        /// into notches, so this number means the same thing on a backend that reports 120.
        /// </remarks>
        [Tooltip("Metres the camera moves per wheel notch.")]
        public float zoomMetresPerNotch = 0.6f;

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
