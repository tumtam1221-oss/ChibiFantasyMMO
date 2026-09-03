using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE third-person locomotion for PHASE 07.1.
    ///
    /// Input -> movement intent -> camera-relative direction -> CharacterController
    /// motion -> character rotation -> Animator.
    ///
    /// One controller serves every character. Per-character differences belong in
    /// data (ProtoMovementSettings), not in subclasses.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ProtoThirdPersonController : MonoBehaviour
    {
        [SerializeField] private ProtoMovementSettings settings;
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Animator animator;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private CharacterController _controller;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;

        public float CurrentPlanarSpeed { get { return _planarVelocity.magnitude; } }
        public bool IsGrounded { get; private set; }

        public void SetCamera(Transform cam) { cameraTransform = cam; }
        public void SetInput(ProtoPlayerInput src) { input = src; }
        public void SetSettings(ProtoMovementSettings s) { settings = s; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Clears accumulated motion so a swapped-out character leaves no stale state.</summary>
        public void ResetMotion()
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            if (animator != null && animator.isActiveAndEnabled &&
                animator.runtimeAnimatorController != null)
            {
                animator.SetFloat(SpeedHash, 0f);
                animator.speed = 1f;
            }
        }

        private void Update()
        {
            if (settings == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 rawMove = (input != null && input.IsReady) ? input.Move : Vector2.zero;

            // --- camera-relative movement intent -------------------------------
            Vector3 desiredDirection = Vector3.zero;
            if (rawMove.sqrMagnitude > 0.0001f)
            {
                Vector3 forward;
                Vector3 right;
                if (cameraTransform != null)
                {
                    // Project the camera basis onto the ground plane so camera pitch
                    // never bleeds into movement direction or speed.
                    forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f)
                        forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                    forward.Normalize();
                    right = Vector3.Cross(Vector3.up, forward);
                }
                else
                {
                    forward = Vector3.forward;
                    right = Vector3.right;
                }

                desiredDirection = forward * rawMove.y + right * rawMove.x;
                if (desiredDirection.sqrMagnitude > 1f) desiredDirection.Normalize();
            }

            // --- accelerate / decelerate ---------------------------------------
            Vector3 targetVelocity = desiredDirection * settings.walkSpeed;
            float rate = desiredDirection.sqrMagnitude > 0.0001f
                ? settings.acceleration
                : settings.deceleration;

            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, rate * dt);

            // --- gravity / grounding -------------------------------------------
            IsGrounded = ProbeGround();
            if (IsGrounded && _verticalVelocity <= 0f)
                _verticalVelocity = settings.groundedStickVelocity;
            else
                _verticalVelocity += settings.gravity * dt;

            Vector3 motion = (_planarVelocity + Vector3.up * _verticalVelocity) * dt;
            if (IsFinite(motion)) _controller.Move(motion);

            // --- rotation toward movement --------------------------------------
            // Rotate only while there is genuine movement intent, so the character
            // never spins in place once input stops.
            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(desiredDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, settings.rotationSpeed * dt);
            }

            // --- animator -------------------------------------------------------
            if (animator != null && animator.isActiveAndEnabled &&
                animator.runtimeAnimatorController != null)
            {
                float speed = _planarVelocity.magnitude;
                float normalized = settings.walkSpeed > 0.0001f ? speed / settings.walkSpeed : 0f;
                animator.SetFloat(SpeedHash, normalized);

                // Scale playback toward the actual ground speed to reduce foot sliding.
                if (speed > 0.05f && settings.referenceWalkClipSpeed > 0.0001f)
                {
                    animator.speed = Mathf.Clamp(speed / settings.referenceWalkClipSpeed,
                        settings.minAnimatorSpeed, settings.maxAnimatorSpeed);
                }
                else
                {
                    animator.speed = 1f;
                }
            }
        }

        private bool ProbeGround()
        {
            if (_controller.isGrounded) return true;

            // Secondary probe: CharacterController.isGrounded flickers on slopes and
            // step edges, which would otherwise let gravity accumulate incorrectly.
            Vector3 origin = transform.position + Vector3.up * (_controller.radius + 0.02f);
            RaycastHit hit;
            return Physics.SphereCast(origin, settings.groundCheckRadius, Vector3.down,
                out hit, _controller.radius + settings.groundCheckDistance,
                ~0, QueryTriggerInteraction.Ignore);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                  || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
