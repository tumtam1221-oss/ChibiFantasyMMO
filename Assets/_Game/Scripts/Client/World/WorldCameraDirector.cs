using ChibiFantasy.Client.Prototype;
using ChibiFantasy.Network;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// The one camera in the world, and who it is looking at.
    /// </summary>
    /// <remarks>
    /// <b>One camera, owned here.</b> Not one per character: a camera on every network object
    /// would mean every remote player rendering the world from their own head, which is both
    /// the wrong picture and a per-player cost for nothing. The rig exists for the whole
    /// session and changes its target; it is never created and destroyed as characters come
    /// and go.
    ///
    /// <b>It binds the owner and refuses everybody else.</b> The target is only ever the
    /// character FishNet says this connection owns. There is no code path that points it at a
    /// remote player, which is what makes "A's camera cannot follow B" a property rather than
    /// a convention.
    ///
    /// <b>The orbit, the zoom and the collision are Phase 07.1's.</b>
    /// <see cref="ProtoThirdPersonCamera"/> already clamps pitch, clamps distance,
    /// sphere-casts towards the pivot and keeps itself off the floor, and it already has
    /// tests. A second camera framework here would have been a second set of those rules to
    /// get subtly wrong. Its known limitation -- a single spherecast, so a thin obstacle
    /// between two casts is not seen -- is inherited deliberately and reported, not papered
    /// over with a redesign this gate did not ask for.
    ///
    /// <b>It renders before anybody spawns.</b> A world scene with no camera is a black
    /// screen and an editor warning, so the rig is up from the moment the scene loads and
    /// simply has no target yet. Entering the world gives it one; a despawn takes it away
    /// again without taking the camera away.
    ///
    /// <b>Nothing here reaches the server.</b> Where a player's camera points is not a fact
    /// about the world, and this class holds no sink, no RPC and no authority.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldCameraDirector : MonoBehaviour
    {
        [Tooltip("The Phase 07.1 rig. Built at runtime if absent.")]
        [SerializeField] private ProtoThirdPersonCamera _rig;

        [Tooltip("Pitch limits, zoom range and collision radius. Phase 07.1's own asset.")]
        [SerializeField] private ProtoCameraSettings _settings;

        [Tooltip("Where orbit and zoom come from. Absent means a camera that only follows.")]
        [SerializeField] private ProtoPlayerInput _input;

        private CharacterNetworkEntity _bound;
        private Camera _camera;

        /// <summary>The camera this director owns.</summary>
        public Camera Camera => _camera;

        /// <summary>The Phase 07.1 rig driving it.</summary>
        public ProtoThirdPersonCamera Rig => _rig;

        /// <summary>The character being followed, or null.</summary>
        public CharacterNetworkEntity Bound => _bound;

        public bool IsBound => _bound != null;

        /// <summary>How many times a character has been followed. A reconnect adds one.</summary>
        public int BindCount { get; private set; }

        /// <summary>Supplies the pieces, for composition that is not the scene.</summary>
        public void Compose(ProtoCameraSettings settings, ProtoPlayerInput input = null)
        {
            _settings = settings;
            _input = input;

            EnsureRig();
        }

        /// <summary>
        /// Follows the character this client owns.
        /// </summary>
        /// <remarks>Returns whether it took. A remote character is refused here rather than
        /// filtered by the caller, so there is one place the rule lives and no caller can
        /// forget it.</remarks>
        public bool Bind(CharacterNetworkEntity entity)
        {
            if (entity == null || !entity.IsOwner)
            {
                return false;
            }

            EnsureRig();

            _bound = entity;

            BindCount++;

            // Snapped rather than eased: the character has just appeared somewhere the
            // camera has never been, and gliding there would fly the view across the map.
            if (_rig != null) _rig.SetTarget(entity.transform, true);

            return true;
        }

        /// <summary>
        /// Stops following, without stopping rendering.
        /// </summary>
        /// <remarks>The rig keeps its last pose. A camera destroyed on despawn is a black
        /// screen during exactly the moment a player most needs to see something.</remarks>
        public void Unbind()
        {
            _bound = null;

            if (_rig != null) _rig.SetTarget(null, false);
        }

        private void Awake()
        {
            EnsureRig();
        }

        /// <summary>
        /// Brings up the rig and its camera, once.
        /// </summary>
        /// <remarks>
        /// Built in code for the same reason the screens are: a scene reference that can be
        /// unset is a scene reference that eventually is, and the failure shows up as a black
        /// screen rather than as an error.
        ///
        /// <b>The main-camera tag is claimed only if it is free.</b> Two clients in one
        /// process -- which is what a two-client test is -- would otherwise both claim it and
        /// <c>Camera.main</c> would answer arbitrarily. A shipped client has one.
        /// </remarks>
        private void EnsureRig()
        {
            if (_rig == null) _rig = GetComponentInChildren<ProtoThirdPersonCamera>(true);

            if (_rig == null)
            {
                var host = new GameObject("World Camera");
                host.transform.SetParent(transform, false);

                _camera = host.AddComponent<Camera>();

                if (Camera.main == null) host.tag = "MainCamera";

                _rig = host.AddComponent<ProtoThirdPersonCamera>();
            }

            if (_camera == null) _camera = _rig.GetComponent<Camera>();

            if (_settings != null) _rig.SetSettings(_settings);

            if (_input != null) _rig.SetInput(_input);
        }
    }
}
