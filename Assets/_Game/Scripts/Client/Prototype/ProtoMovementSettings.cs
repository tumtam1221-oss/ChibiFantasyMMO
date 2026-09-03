using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE movement tuning for PHASE 07.1. All values are provisional and
    /// intentionally live in data rather than in code.
    /// </summary>
    [CreateAssetMenu(menuName = "ChibiFantasy/Prototype/Movement Settings",
                     fileName = "ProtoMovementSettings")]
    public sealed class ProtoMovementSettings : ScriptableObject
    {
        [Header("Speed (m/s) - PROTOTYPE")]
        public float walkSpeed = 1.2f;

        [Tooltip("Only used when a validated Run clip exists. None exists yet in PHASE 07.1.")]
        public float runSpeed = 2.4f;

        [Header("Responsiveness - PROTOTYPE")]
        [Tooltip("Degrees per second the character turns toward its movement direction.")]
        public float rotationSpeed = 720f;

        public float acceleration = 12f;
        public float deceleration = 16f;

        [Header("Gravity - PROTOTYPE")]
        public float gravity = -9.81f;

        [Tooltip("Downward velocity held while grounded so the controller stays glued to the floor.")]
        public float groundedStickVelocity = -2f;

        [Header("Grounding - PROTOTYPE")]
        public float groundCheckDistance = 0.15f;
        public float groundCheckRadius = 0.12f;

        [Header("Animation - PROTOTYPE")]
        [Tooltip("Retargeted root-motion speed of the walk clip (m/s). Used only to scale playback so the feet slide less.")]
        public float referenceWalkClipSpeed = 0.55f;

        public float minAnimatorSpeed = 0.5f;
        public float maxAnimatorSpeed = 2.5f;
    }
}
