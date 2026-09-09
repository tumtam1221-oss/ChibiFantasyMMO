using System.Collections.Generic;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// How a person picks a monster and asks the server to hit it.
    /// </summary>
    /// <remarks>
    /// <b>Intent only.</b> A click chooses a target and a key asks for an attack. What
    /// leaves this machine is the target's id and a sequence number, through the character
    /// object this client owns -- the same door 18.18B proved a real socket client can knock
    /// on. No damage, no health, no death and no reach check is decided here; the server
    /// refuses an attack that is out of range and this has no opinion about it.
    ///
    /// <b>The click resolves against what the server sent.</b> A ray hits a placeholder, the
    /// placeholder names its <see cref="MonsterNetworkEntity"/>, and that entity's instance
    /// id is what travels. A client that pointed at nothing sends nothing.
    ///
    /// <b>Deliberately small.</b> One target, one basic attack, no queue, no auto-combat, no
    /// tab-cycling and no skill bar. This gate is about a person being able to play the game
    /// that already exists, not about designing its combat interface.
    /// </remarks>
    public sealed class WorldCombatInput : MonoBehaviour
    {
        [Tooltip("How far a click may reach to select a target, in metres.")]
        [SerializeField] private float _selectRange = 200f;

        [Tooltip("Seconds between attack requests while the key is held.")]
        [SerializeField] private float _attackInterval = 0.6f;

        [Tooltip("Development only: also attack on Space. Off in normal play, where the "
            + "mouse is the only control.")]
        [SerializeField] private bool _developmentKeyboard;

        private NetworkManager _networkManager;
        private Camera _camera;
        private long _sequence;
        private float _nextAttack;

        /// <summary>The monster this player has selected, or null.</summary>
        public MonsterNetworkEntity Target { get; private set; }

        /// <summary>How many attack requests this client has sent. For tests.</summary>
        public int AttacksRequested { get; private set; }

        /// <summary>How many times a click has changed the target. For tests.</summary>
        public int TargetsSelected { get; private set; }

        /// <summary>Points this at the client it sends through.</summary>
        public void Compose(NetworkManager networkManager, Camera view = null)
        {
            _networkManager = networkManager;
            _camera = view;
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return;

            // Selecting and attacking are the pointer's, not this component's. A click is
            // one gesture that can mean four things, and only one place can decide which --
            // see WorldPointerInput. What stays here is the pair of verbs it calls.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_developmentKeyboard)
            {
                Keyboard keyboard = Keyboard.current;

                bool wants = keyboard != null && keyboard.spaceKey.isPressed;

                if (wants && Time.time >= _nextAttack)
                {
                    _nextAttack = Time.time + _attackInterval;

                    RequestAttack();
                }
            }
#endif

            // A target the server has taken away stops being a target.
            if (Target != null && !Target.IsAlive) Target = null;
        }

        /// <summary>Selects whatever the cursor is pointing at, if it is a monster.</summary>
        public void SelectUnderCursor()
        {
            Camera view = _camera != null ? _camera : Camera.main;

            if (view == null) return;

            Mouse mouse = Mouse.current;

            if (mouse == null) return;

            Ray ray = view.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out RaycastHit hit, _selectRange)) return;

            MonsterNetworkEntity monster = MonsterOf(hit.collider);

            if (monster == null) return;

            Target = monster;

            TargetsSelected++;
        }

        /// <summary>Selects a monster directly, for a test or another input path.</summary>
        public void Select(MonsterNetworkEntity monster)
        {
            if (monster == null) return;

            Target = monster;

            TargetsSelected++;
        }

        /// <summary>
        /// Asks the server to attack the selected monster.
        /// </summary>
        /// <returns>False when there is nothing to ask with, or nothing to ask about.</returns>
        public bool RequestAttack()
        {
            if (Target == null || !Target.IsAlive) return false;

            CharacterNetworkEntity owned = Owned();

            if (owned == null) return false;

            owned.RequestAttack(Target.Instance.Value, string.Empty, 0, ++_sequence);

            AttacksRequested++;

            return true;
        }

        /// <summary>The character object this connection owns.</summary>
        /// <remarks>Asked of the connection, for the same reason the presentation binder
        /// asks: it already knows, and walking the world to find out is what 18.18B
        /// measured at a hundred kilobytes a frame.</remarks>
        private CharacterNetworkEntity Owned()
        {
            FishNet.Connection.NetworkConnection connection =
                _networkManager.ClientManager.Connection;

            if (connection == null || !connection.IsValid) return null;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(out CharacterNetworkEntity entity)) return entity;
            }

            return null;
        }

        /// <summary>Which monster a collider stands for, if any.</summary>
        /// <remarks>Two ways, because a monster may one day carry its own collider: the
        /// object itself, or the development placeholder standing in for it.</remarks>
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
