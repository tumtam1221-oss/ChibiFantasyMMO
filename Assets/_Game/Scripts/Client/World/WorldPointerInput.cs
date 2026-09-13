using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// What a left click means, and how the character gets there.
    /// </summary>
    /// <remarks>
    /// <b>One place decides what a click is for.</b> Ground, monster, loot and NPC all begin
    /// as the same gesture, and splitting them across four components would mean four
    /// raycasts a frame and four opinions about which one won. The ray is cast once, the hit
    /// is classified once, and the result becomes one intent that replaces whatever intent
    /// was there before.
    ///
    /// <b>Intent only -- the authority is untouched.</b> Nothing here moves, damages, kills,
    /// or picks anything up. Walking is <see cref="CharacterMovementInput.Intent"/>, which
    /// this writes and that component sends on its own cadence through
    /// <c>CharacterNetworkEntity.RequestMove</c>. Attacking is
    /// <see cref="WorldCombatInput.RequestAttack"/>. Picking up is
    /// <see cref="WorldLootInput.RequestPickup"/>. Every one of those already existed, and
    /// the server still decides range, damage, death, reward and inventory. A destination is
    /// a client's private opinion about which direction to ask for next.
    ///
    /// <b>Why steering rather than pathfinding.</b> The authoritative movement channel takes
    /// a direction, not a destination, and the server integrates it. Sending a direction that
    /// happens to point at a destination therefore needs no second navigation authority and
    /// no new message: the existing path already supports destination steering. This project
    /// has no runtime NavMesh seam, so introducing one would have meant a second thing that
    /// decides where a character may stand. Obstacle avoidance is consequently not solved
    /// here, and that is reported rather than hidden.
    ///
    /// <b>Arrival is deliberately hysteretic.</b> Stopping exactly at the destination makes a
    /// character oscillate: it overshoots by whatever one server tick is worth, turns around,
    /// overshoots back. Movement stops at <see cref="_arriveMetres"/> and does not resume
    /// unless a new click arrives, so there is nothing to circle.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldPointerInput : MonoBehaviour
    {
        /// <summary>What the last click asked for.</summary>
        public enum Intent
        {
            None = 0,
            Ground = 1,
            Monster = 2,
            Loot = 3,
            Npc = 4,
        }

        [Tooltip("How far a click may reach into the world, in metres.")]
        [SerializeField] private float _clickRange = 200f;

        [Tooltip("How close counts as arrived, in metres. Larger than one tick of movement "
            + "so the character cannot overshoot and turn around.")]
        [SerializeField] private float _arriveMetres = 0.4f;

        [Tooltip("How close to stand before attacking, centre to centre, in metres. Measured "
            + "from the punch: the Mixamo cross punch puts the fist 0.22 m past the character's "
            + "origin at impact and the training slime is drawn 0.135 m in radius, so at 0.45 m "
            + "the fist stops a few centimetres short of its surface and the slime, which closes "
            + "in on its own, meets it. Inside the server's 1 m melee reach, so arriving is "
            + "never immediately out of range again.")]
        [SerializeField] private float _attackMetres = 0.45f;

        [Tooltip("How close to stand before picking up. Inside the server's 4m loot reach.")]
        [SerializeField] private float _pickupMetres = 2.5f;

        [Tooltip("Seconds between pickup requests while in range.")]
        [SerializeField] private float _pickupInterval = 0.5f;

        private NetworkManager _networkManager;
        private Camera _camera;
        private WorldCombatInput _combat;
        private WorldLootInput _loot;

        private CharacterMovementInput _movement;
        private CharacterNetworkEntity _owned;

        private Vector3 _destination;

        // The route around whatever stands between here and the destination, planned on the
        // map's baked ground (see GridPathfinder). Empty means "steer straight", which is
        // exactly what this component did before routes existed.
        private IDefinitionRegistry<MapDefinition> _maps;
        private readonly List<Vector2> _path = new List<Vector2>();
        private int _pathIndex;
        private float _nextRepath;

        [Tooltip("How close to a route corner counts as having turned it, in metres.")]
        [SerializeField] private float _waypointReach = 0.45f;

        [Tooltip("How often the route to a moving monster may be re-planned, in seconds.")]
        [SerializeField] private float _repathInterval = 0.75f;

        [Tooltip("Seconds between destination updates while the button is held. Holding is "
            + "steering, not a stream of clicks, so this need only keep up with a hand.")]
        [SerializeField] private float _dragInterval = 0.05f;

        [Tooltip("How far the pointer must move, in pixels, before a held button counts as "
            + "a drag. Below this a click is a click and the hand is simply not perfectly "
            + "still.")]
        [SerializeField] private float _dragThresholdPixels = 6f;

        // Set when a press chose ground, cleared on release. Only a ground press starts a
        // drag: dragging off a monster must not silently turn an attack into a walk.
        private bool _dragging;
        private Vector2 _pressPoint;
        private float _nextDrag;

        private MonsterNetworkEntity _monster;
        private WorldNpcMarker _npc;
        private string _lootId;
        private int _lootIndex;

        private float _nextPickup;

        /// <summary>What the current click is trying to do.</summary>
        public Intent Current { get; private set; }

        /// <summary>Where the character is currently walking, when it is walking.</summary>
        public Vector3 Destination => _destination;

        /// <summary>How many clicks have been turned into an intent. For tests.</summary>
        public int IntentsIssued { get; private set; }

        /// <summary>How many clicks were swallowed because the pointer was over UI.</summary>
        public int ClicksOverUi { get; private set; }

        /// <summary>How many times a held button moved the destination. For tests.</summary>
        public int DragUpdates { get; private set; }

        /// <summary>Whether the button is currently held and steering. For tests.</summary>
        public bool IsDragging => _dragging;

        /// <summary>Points this at the client it drives.</summary>
        public void Compose(NetworkManager networkManager, Camera view,
            WorldCombatInput combat, WorldLootInput loot)
        {
            _networkManager = networkManager;
            _camera = view;
            _combat = combat;
            _loot = loot;
        }

        /// <summary>
        /// Lets clicks route around obstacles, using each map's baked ground.
        /// </summary>
        /// <remarks>Optional. Without it, or on a map that authors no height field, a click
        /// is followed in a straight line as it always was. The route never reaches anywhere
        /// the server would refuse: it is planned over the same field the server samples.</remarks>
        public void UsePathfinding(IDefinitionRegistry<MapDefinition> maps)
        {
            _maps = maps;
        }

        /// <summary>The corners still ahead on the current route. For tests and debugging.</summary>
        public IReadOnlyList<Vector2> Route => _path;

        private MapHeightField FieldForOwner()
        {
            if (_maps == null || _owned == null) return null;

            return _maps.TryGet(_owned.Map, out MapDefinition map) && map != null
                ? map.HeightField
                : null;
        }

        /// <summary>
        /// Plans the route to the current destination, or leaves it straight when there is
        /// no ground to plan on or no way through.
        /// </summary>
        private void PlanPath()
        {
            _path.Clear();
            _pathIndex = 0;
            _nextRepath = Time.time + _repathInterval;

            if (_owned == null) return;

            MapHeightField field = FieldForOwner();

            if (field == null) return;

            var start = new Vector2(_owned.X, _owned.Z);
            var goal = new Vector2(_destination.x, _destination.z);

            // Nothing in the way is the ordinary case -- an open street, an open field --
            // and answering that costs a few dozen samples. Only a click that is actually
            // blocked pays for a search, so walking never stutters on a route nobody needed.
            if (GridPathfinder.IsStraightWalkable(field, start, goal)) return;

            if (!GridPathfinder.TryFindPath(field, start, goal, _path)) return;

            // The planner may have moved a ground click off a rock onto the nearest
            // standable spot; that spot is where the character will actually stop. For a
            // target (monster, loot, npc) the stop distance around the thing itself decides.
            Vector2 last = _path[_path.Count - 1];

            if (Current == Intent.Ground) _destination = new Vector3(last.x, _destination.y, last.y);

            // The final leg is steered by the destination logic below, so the goal itself
            // is not a corner to turn.
            _path.RemoveAt(_path.Count - 1);
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return;

            Bind();

            if (_owned == null || _movement == null) return;

            Mouse mouse = Mouse.current;

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) OnClick(mouse);
                else if (mouse.leftButton.isPressed) OnHeld(mouse);
                else _dragging = false;
            }

            Steer();
        }

        // ---- the hold ----------------------------------------------------------------------

        /// <summary>
        /// Keeps walking toward the pointer for as long as the button is held.
        /// </summary>
        /// <remarks>
        /// <b>Holding is one continuous instruction, not a stream of clicks.</b> Re-running
        /// the whole classification every frame would let a cursor that happens to sweep over
        /// a monster turn a walk into an attack halfway through the gesture, and a cursor
        /// that sweeps over a loot pile into a pickup. So a drag only ever moves the ground
        /// destination, and only when the press that began it was itself a ground click.
        ///
        /// <b>Rate limited on purpose.</b> Each update costs a raycast and, when something is
        /// in the way, a route. A hand does not move meaningfully faster than
        /// <see cref="_dragInterval"/>, so spending a search every frame at 144 Hz would buy
        /// nothing and is exactly the kind of per-frame cost that shows up as a hitch.
        /// </remarks>
        private void OnHeld(Mouse mouse)
        {
            if (!_dragging) return;

            Vector2 point = mouse.position.ReadValue();

            // A perfectly still hand still jitters a pixel or two; below the threshold this
            // is a click being held, which must keep the destination the click chose.
            if ((point - _pressPoint).sqrMagnitude
                < _dragThresholdPixels * _dragThresholdPixels)
            {
                return;
            }

            if (Time.time < _nextDrag) return;

            _nextDrag = Time.time + _dragInterval;

            if (IsPointerOverUi()) return;

            DragTo(point);
        }

        /// <summary>
        /// Moves the destination to a point on the ground under the screen position.
        /// </summary>
        /// <remarks>
        /// Separated from the mouse for the same reason <see cref="ClickAt"/> is: a test
        /// drives the code the hand drives. Anything that is not ground is ignored rather
        /// than refused -- sweeping the cursor across a tree on the way should carry on
        /// walking, not stop dead.
        /// </remarks>
        /// <returns>Whether the destination moved.</returns>
        public bool DragTo(Vector2 screenPoint)
        {
            // Ground, or nothing at all. Nothing at all is the ordinary case halfway through
            // a hold: the character caught up with the cursor, arrival cleared the intent,
            // and the hand is still asking to go somewhere -- so the walk is taken up again
            // rather than ending under a button that was never released. What it must not do
            // is take over a monster, loot or NPC approach that is still running.
            if (Current != Intent.Ground && Current != Intent.None) return false;

            Camera view = _camera != null ? _camera : Camera.main;

            if (view == null) return false;

            Ray ray = view.ScreenPointToRay(screenPoint);

            if (!Physics.Raycast(ray, out RaycastHit hit, _clickRange)) return false;

            // Only real ground steers. A monster, a loot pile or an NPC under the cursor is
            // scenery for the purposes of a drag.
            if (MonsterOf(hit.collider) != null) return false;

            if (hit.collider != null
                && (hit.collider.GetComponentInParent<WorldLootMarker>() != null
                    || hit.collider.GetComponentInParent<WorldNpcMarker>() != null))
            {
                return false;
            }

            _destination = hit.point;

            Current = Intent.Ground;

            DragUpdates++;

            PlanPath();

            return true;
        }

        // ---- the click ---------------------------------------------------------------------

        /// <summary>
        /// Turns one press into one intent.
        /// </summary>
        /// <remarks>A click that lands on UI is not a click on the world. Asked of the
        /// event system rather than by testing widgets, so a screen built later is covered
        /// without anybody remembering to add it here.</remarks>
        private void OnClick(Mouse mouse)
        {
            if (IsPointerOverUi())
            {
                ClicksOverUi++;

                return;
            }

            Vector2 point = mouse.position.ReadValue();

            _pressPoint = point;
            _nextDrag = 0f;

            // Only a press that chose ground may become a drag.
            _dragging = ClickAt(point) == Intent.Ground;
        }

        /// <summary>
        /// Acts on a click at one point on the screen.
        /// </summary>
        /// <remarks>
        /// The whole of what a press does, separated from where the press came from. A test
        /// -- and a person proving the game by hand from the editor -- drives exactly the
        /// code a mouse drives, rather than a re-implementation of it that could drift.
        ///
        /// It does not ask whether the pointer is over UI: that is a property of a real
        /// pointer, checked by the caller that has one.
        /// </remarks>
        /// <returns>What the click was taken to mean.</returns>
        public Intent ClickAt(Vector2 screenPoint)
        {
            Camera view = _camera != null ? _camera : Camera.main;

            if (view == null) return Intent.None;

            Ray ray = view.ScreenPointToRay(screenPoint);

            if (!Physics.Raycast(ray, out RaycastHit hit, _clickRange)) return Intent.None;

            IntentsIssued++;

            // A monster first: a click that lands on something alive means that thing.
            MonsterNetworkEntity monster = MonsterOf(hit.collider);

            if (monster != null && monster.IsAlive)
            {
                Clear();

                _monster = monster;

                Current = Intent.Monster;

                _combat?.Select(monster);

                return Current;
            }

            WorldLootMarker pile = hit.collider == null
                ? null
                : hit.collider.GetComponentInParent<WorldLootMarker>();

            if (pile != null)
            {
                Clear();

                _lootId = pile.LootId;
                _lootIndex = pile.Index;
                _destination = pile.transform.position;

                Current = Intent.Loot;

                PlanPath();

                return Current;
            }

            var npc = hit.collider == null
                ? null
                : hit.collider.GetComponentInParent<WorldNpcMarker>();

            if (npc != null)
            {
                Clear();

                _npc = npc;
                _destination = npc.transform.position;

                Current = Intent.Npc;

                PlanPath();

                return Current;
            }

            Clear();

            _destination = hit.point;

            Current = Intent.Ground;

            PlanPath();

            return Current;
        }

        /// <summary>Whether a UI element is under the pointer right now.</summary>
        public static bool IsPointerOverUi()
        {
            EventSystem events = EventSystem.current;

            if (events == null) return false;

            Mouse mouse = Mouse.current;

            if (mouse == null) return events.IsPointerOverGameObject();

            // The pointer id a mouse uses, so a touch build asks the same question correctly.
            return events.IsPointerOverGameObject(PointerInputModule.kMouseLeftId)
                || events.IsPointerOverGameObject();
        }

        // ---- getting there -----------------------------------------------------------------

        /// <summary>
        /// Walks toward whatever the last click chose, and acts when close enough.
        /// </summary>
        /// <remarks>
        /// Run every frame, but it writes a direction rather than sending one: the cadence
        /// that reaches the wire belongs to <see cref="CharacterMovementInput"/>, which sends
        /// twenty times a second whatever this last decided. No allocation, no search, no
        /// LINQ; the owned character is cached and only re-resolved when it goes away.
        /// </remarks>
        private void Steer()
        {
            if (Current == Intent.None) { _movement.Intent = Vector2.zero; return; }

            // A target the server has taken away, or killed, ends the approach.
            if (Current == Intent.Monster && (_monster == null || !_monster.IsAlive))
            {
                Stop();

                return;
            }

            if (Current == Intent.Monster)
            {
                _destination = MonsterPosition(_monster);

                // A monster moves, so the way around things between us and it goes stale.
                // Re-planned only when the straight line is actually blocked: chasing across
                // open ground must not pay for a search twice a second.
                if (Time.time >= _nextRepath)
                {
                    _nextRepath = Time.time + _repathInterval;

                    MapHeightField field = FieldForOwner();
                    var here2 = new Vector2(_owned.X, _owned.Z);
                    var there = new Vector2(_destination.x, _destination.z);

                    if (field != null && !GridPathfinder.IsStraightWalkable(field, here2, there))
                    {
                        PlanPath();
                    }
                    else if (_path.Count > 0)
                    {
                        _path.Clear();
                        _pathIndex = 0;
                    }
                }
            }

            Vector3 here = new Vector3(_owned.X, _owned.Y, _owned.Z);

            // Turn the corners of the planned route first; the destination logic below
            // takes over for the final leg, exactly as it did before routes existed.
            while (_pathIndex < _path.Count)
            {
                Vector2 corner = _path[_pathIndex];
                float cdx = corner.x - here.x;
                float cdz = corner.y - here.z;
                float cornerDistance = Mathf.Sqrt(cdx * cdx + cdz * cdz);

                if (cornerDistance <= _waypointReach)
                {
                    _pathIndex++;
                    continue;
                }

                _movement.Intent = new Vector2(cdx / cornerDistance, cdz / cornerDistance);

                return;
            }

            float dx = _destination.x - here.x;
            float dz = _destination.z - here.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float stop = StopDistanceFor(Current);

            if (distance > stop)
            {
                _movement.Intent = new Vector2(dx / distance, dz / distance);

                return;
            }

            // Arrived. Standing still is an intent too, and sending it once is what tells the
            // server to stop rather than coast.
            _movement.Intent = Vector2.zero;

            Act();
        }

        /// <summary>What to do once the character is standing close enough.</summary>
        private void Act()
        {
            switch (Current)
            {
                case Intent.Ground:
                    Current = Intent.None;

                    break;

                case Intent.Monster:
                    // Through the existing combat input, which is the only thing that names
                    // an instance id and a sequence -- and, now, the only thing that knows
                    // how often this character may ask: the cadence follows the replicated
                    // attack speed rather than a number typed here. A refusal is the
                    // server's business.
                    _combat?.RequestAttackWhenReady();

                    break;

                case Intent.Loot:
                    if (Time.time < _nextPickup) return;

                    _nextPickup = Time.time + _pickupInterval;

                    if (_loot != null && _loot.RequestPickup(_lootId, _lootIndex))
                    {
                        // Asked for. Whether it arrives is the server's answer, and the
                        // pile disappearing from the next snapshot is how this client
                        // learns it did.
                        Stop();
                    }

                    break;

                case Intent.Npc:
                    // Arrival is announced through the marker's own seam. Today nothing is
                    // listening -- this project authors NPC content and spawns none -- so
                    // this is where a real interaction will be called from rather than
                    // dialogue invented here to look finished.
                    if (_npc != null) _npc.Arrive();

                    Stop();

                    break;
            }
        }

        private float StopDistanceFor(Intent intent)
        {
            switch (intent)
            {
                case Intent.Monster: return _attackMetres;
                case Intent.Loot: return _pickupMetres;
                default: return _arriveMetres;
            }
        }

        /// <summary>Stops walking and forgets the current intent.</summary>
        public void Stop()
        {
            Clear();

            Current = Intent.None;

            if (_movement != null) _movement.Intent = Vector2.zero;
        }

        private void Clear()
        {
            _monster = null;
            _npc = null;
            _lootId = null;
            _lootIndex = 0;
            _path.Clear();
            _pathIndex = 0;
        }

        private static Vector3 MonsterPosition(MonsterNetworkEntity monster)
        {
            return new Vector3(monster.X, monster.Y, monster.Z);
        }

        // ---- who this is driving -------------------------------------------------------------

        /// <summary>
        /// Finds the character this connection owns, and keeps it.
        /// </summary>
        /// <remarks>Re-resolved only when the cached one has gone -- a despawn, a
        /// reconnect. Walking the connection's objects every frame is exactly the cost
        /// 18.18B measured and removed.</remarks>
        private void Bind()
        {
            if (_owned != null && _movement != null) return;

            NetworkConnection connection = _networkManager.ClientManager.Connection;

            if (connection == null || !connection.IsValid) return;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned == null) continue;

                var entity = owned.GetComponent<CharacterNetworkEntity>();

                if (entity == null) continue;

                _owned = entity;
                _movement = owned.GetComponent<CharacterMovementInput>();

                return;
            }
        }

        private static MonsterNetworkEntity MonsterOf(Collider collider)
        {
            if (collider == null) return null;

            var direct = collider.GetComponentInParent<MonsterNetworkEntity>();

            if (direct != null) return direct;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var marker = collider.GetComponentInParent<DevelopmentMonsterMarker>();

            if (marker != null) return marker.Monster;
#endif

            return null;
        }
    }
}
