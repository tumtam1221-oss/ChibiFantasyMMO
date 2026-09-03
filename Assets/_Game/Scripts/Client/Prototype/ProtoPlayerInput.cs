using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE input source for PHASE 07.1. Wraps the single ProtoControls
    /// InputActionAsset so the controller and camera never touch the Input System
    /// directly and no second input path is introduced.
    /// </summary>
    public sealed class ProtoPlayerInput : MonoBehaviour
    {
        [SerializeField] private InputActionAsset controls;

        private InputActionMap _map;
        private InputAction _move;
        private InputAction _look;
        private InputAction _zoom;

        /// <summary>Raw WASD vector. X = strafe, Y = forward/back. Never longer than 1.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>Mouse delta for this frame. X = yaw, Y = pitch.</summary>
        public Vector2 Look { get; private set; }

        /// <summary>Mouse wheel delta for this frame.</summary>
        public float Zoom { get; private set; }

        public bool IsReady => _map != null;

        public void SetControls(InputActionAsset asset)
        {
            controls = asset;
        }

        private void Awake()
        {
            if (controls == null)
            {
                Debug.LogError("ProtoPlayerInput: no InputActionAsset assigned.", this);
                return;
            }

            _map = controls.FindActionMap("Player", true);
            _move = _map.FindAction("Move", true);
            _look = _map.FindAction("Look", true);
            _zoom = _map.FindAction("Zoom", true);
        }

        private void OnEnable()
        {
            if (_map != null) _map.Enable();
        }

        private void OnDisable()
        {
            if (_map != null) _map.Disable();
            Move = Vector2.zero;
            Look = Vector2.zero;
            Zoom = 0f;
        }

        private void Update()
        {
            if (_map == null) return;

            Vector2 move = _move.ReadValue<Vector2>();

            // Diagonal input must never exceed unit length, otherwise diagonal
            // movement would be faster than cardinal movement.
            if (move.sqrMagnitude > 1f) move.Normalize();

            Move = move;
            Look = _look.ReadValue<Vector2>();
            Zoom = _zoom.ReadValue<float>();
        }
    }
}
