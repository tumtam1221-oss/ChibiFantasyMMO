using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE orbiting third-person camera for PHASE 07.1.
    ///
    /// Orbits a follow target, clamps pitch, zooms on the mouse wheel and pulls in
    /// when solid geometry would otherwise come between the camera and the target.
    /// Deliberately minimal: this is not a production camera framework.
    /// </summary>
    public sealed class ProtoThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private ProtoCameraSettings settings;
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private Transform target;

        private float _yaw;
        private float _pitch;
        private float _desiredDistance;
        private float _currentDistance;
        private float _distanceVelocity;
        private Vector3 _positionVelocity;
        private bool _initialised;

        public Transform Target { get { return target; } }
        public float Yaw { get { return _yaw; } }
        public float Pitch { get { return _pitch; } }
        public float CurrentDistance { get { return _currentDistance; } }
        public float DesiredDistance { get { return _desiredDistance; } }

        public void SetInput(ProtoPlayerInput src) { input = src; }
        public void SetSettings(ProtoCameraSettings s) { settings = s; }

        /// <summary>Retargets the camera and snaps to the new subject without interpolating across the gap.</summary>
        public void SetTarget(Transform newTarget, bool snap)
        {
            target = newTarget;
            if (snap) SnapToTarget();
        }

        private void Awake()
        {
            Initialise();
        }

        private void Initialise()
        {
            if (settings == null || _initialised) return;
            _pitch = Mathf.Clamp(settings.startPitch, settings.pitchMin, settings.pitchMax);
            _desiredDistance = Mathf.Clamp(settings.defaultDistance, settings.minDistance, settings.maxDistance);
            _currentDistance = _desiredDistance;
            if (target != null) _yaw = target.eulerAngles.y;
            _initialised = true;
        }

        /// <summary>Places the camera immediately at its solved pose, skipping smoothing.</summary>
        public void SnapToTarget()
        {
            Initialise();
            if (target == null || settings == null) return;

            _currentDistance = _desiredDistance;
            _positionVelocity = Vector3.zero;
            _distanceVelocity = 0f;

            Vector3 pivot = GetPivot();
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            float resolved = ResolveCollision(pivot, rot, _desiredDistance);
            _currentDistance = resolved;
            transform.position = pivot - rot * Vector3.forward * resolved;
            transform.rotation = rot;
        }

        private Vector3 GetPivot()
        {
            return target.position + Vector3.up * settings.followHeight;
        }

        private void LateUpdate()
        {
            if (settings == null || target == null) return;
            Initialise();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- orbit / pitch from mouse --------------------------------------
            if (input != null && input.IsReady)
            {
                Vector2 look = input.Look;
                _yaw += look.x * settings.orbitSensitivityX;
                _pitch -= look.y * settings.orbitSensitivityY;

                // Zoom: positive scroll pulls the camera in.
                _desiredDistance -= NotchesFrom(input.Zoom) * settings.zoomMetresPerNotch;
            }

            // Clamp pitch so the camera can never flip over or pass under the feet.
            _pitch = Mathf.Clamp(_pitch, settings.pitchMin, settings.pitchMax);
            _yaw = Mathf.Repeat(_yaw, 360f);

            // Clamp zoom so distance can never reach zero, go negative, or run away.
            _desiredDistance = Mathf.Clamp(_desiredDistance, settings.minDistance, settings.maxDistance);

            Vector3 pivot = GetPivot();
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // --- collision -------------------------------------------------------
            float allowed = ResolveCollision(pivot, rotation, _desiredDistance);

            // Snap inward immediately when blocked; ease back out when the obstacle clears.
            if (allowed < _currentDistance)
            {
                _currentDistance = allowed;
                _distanceVelocity = 0f;
            }
            else
            {
                _currentDistance = Mathf.SmoothDamp(_currentDistance, allowed,
                    ref _distanceVelocity, settings.zoomSmoothTime);
            }

            Vector3 desiredPosition = pivot - rotation * Vector3.forward * _currentDistance;

            // Never let the camera sink through the floor.
            RaycastHit groundHit;
            if (Physics.Raycast(desiredPosition + Vector3.up * 2f, Vector3.down, out groundHit,
                    10f, settings.collisionMask, QueryTriggerInteraction.Ignore))
            {
                float minY = groundHit.point.y + settings.minHeightAboveGround;
                if (desiredPosition.y < minY) desiredPosition.y = minY;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition,
                ref _positionVelocity, settings.positionSmoothTime);
            transform.rotation = rotation;
        }

        /// <summary>
        /// Turns a raw wheel value into notches.
        /// </summary>
        /// <remarks>
        /// <b>Because the backends disagree.</b> The Input System reports about 1.0 per
        /// notch; the legacy manager and some drivers report 120, the Windows WHEEL_DELTA.
        /// A sensitivity tuned for one is unusable on the other -- which is the defect this
        /// exists for -- so the raw value is converted here and the setting is expressed in
        /// metres per notch, which means the same thing everywhere.
        ///
        /// <b>A trackpad still works.</b> Small fractional values pass through unscaled, so
        /// a continuous two-finger scroll zooms continuously rather than in steps.
        /// </remarks>
        public static float NotchesFrom(float rawScroll)
        {
            const float WheelDelta = 120f;

            // Nothing reports a notch between 10 and 120, so anything that large is a
            // backend counting in WHEEL_DELTA rather than in notches.
            return Mathf.Abs(rawScroll) >= 10f ? rawScroll / WheelDelta : rawScroll;
        }

        /// <summary>Returns the largest distance from the pivot that stays clear of solid geometry.</summary>
        private float ResolveCollision(Vector3 pivot, Quaternion rotation, float distance)
        {
            Vector3 dir = -(rotation * Vector3.forward);
            RaycastHit hit;
            if (Physics.SphereCast(pivot, settings.collisionRadius, dir, out hit,
                    distance, settings.collisionMask, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(settings.minDistance * 0.5f, hit.distance);
            }
            return distance;
        }
    }
}
