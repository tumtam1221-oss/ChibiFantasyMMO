using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// How a person picks up what a monster left behind.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here decides what is in a pile.</b> The server publishes a
    /// <see cref="LootSnapshot"/> to the one player entitled to see it, and this asks to
    /// take one entry of it by the pile's own id and slot. Which item that is, whether it is
    /// still there, whether the player is close enough and whether their bag has room are
    /// all answered on the server, from state this cannot reach. No item identity is minted
    /// on this side.
    ///
    /// <b>The smallest interaction that makes it playable.</b> One key takes the first entry
    /// the server has offered. There is no loot window, no filtering and no auto-pickup,
    /// because this gate exists to let a person finish a kill rather than to design a loot
    /// interface.
    /// </remarks>
    public sealed class WorldLootInput : MonoBehaviour
    {
        [Tooltip("Seconds between pickup requests while the key is held.")]
        [SerializeField] private float _interval = 0.4f;

        private NetworkManager _networkManager;
        private CharacterNetworkEntity _owned;
        private long _sequence;
        private float _next;

        /// <summary>What the server has told this player is on the ground.</summary>
        public LootSnapshot Snapshot { get; private set; }

        /// <summary>How many pickup requests this client has sent. For tests.</summary>
        public int PickupsRequested { get; private set; }

        /// <summary>How many piles the server is currently offering this player.</summary>
        public int Available => Snapshot.Count;

        public void Compose(NetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return;

            Follow();

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard.fKey.isPressed) return;

            if (Time.time < _next) return;

            _next = Time.time + _interval;

            RequestPickup();
        }

        /// <summary>Keeps listening to whichever character object this client owns.</summary>
        private void Follow()
        {
            CharacterNetworkEntity owned = Owned();

            if (ReferenceEquals(owned, _owned)) return;

            if (_owned != null) _owned.LootChanged -= OnLootChanged;

            _owned = owned;

            if (_owned == null)
            {
                Snapshot = default;

                return;
            }

            _owned.LootChanged += OnLootChanged;

            // Whatever was published before this client started listening.
            Snapshot = _owned.Loot;
        }

        private void OnLootChanged(LootSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>Asks the server for the first entry it is offering.</summary>
        /// <returns>False when the server has offered nothing.</returns>
        public bool RequestPickup()
        {
            if (_owned == null || Snapshot.Count == 0) return false;

            LootEntrySnapshot entry = Snapshot.Entries[0];

            _owned.RequestPickup(entry.LootId, entry.Index, ++_sequence);

            PickupsRequested++;

            return true;
        }

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

        private void OnDestroy()
        {
            if (_owned != null) _owned.LootChanged -= OnLootChanged;
        }
    }
}
