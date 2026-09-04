using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Keeps the world screens pointed at the character this client owns.
    /// </summary>
    /// <remarks>
    /// <b>Ownership is the whole rule.</b> Every character in view has a network object and
    /// this binds exactly one of them: the one FishNet says this connection owns. A remote
    /// player's object is skipped, which is also why a remote player's bag can never appear
    /// -- the server never sends it, and nothing here would have anywhere to put it.
    ///
    /// <b>Rebinding is the normal case, not an error path.</b> A character despawns on
    /// disconnect and a new object arrives on reconnect; the screens follow. Watching for
    /// that is why this polls rather than binding once: FishNet raises no event this
    /// assembly can subscribe to for "an object you own appeared", and a poll of a small
    /// dictionary once a frame is cheaper than the machinery to avoid it.
    ///
    /// <b>Unbinding is deliberate.</b> A screen still holding a destroyed object is where
    /// null-reference noise after a disconnect comes from, so the screens are told the
    /// moment the object goes.
    /// </remarks>
    public sealed class WorldPresentationBinder : MonoBehaviour
    {
        [Tooltip("The client NetworkManager whose owned character this follows.")]
        [SerializeField] private NetworkManager _networkManager;

        private WorldHudScreen _hud;
        private InventoryScreen _inventory;
        private IDefinitionRegistry<ItemDefinition> _items;

        private CharacterNetworkEntity _bound;

        /// <summary>The character currently bound, or null.</summary>
        public CharacterNetworkEntity Bound => _bound;

        /// <summary>How many times a character has been bound. Rebinding increments it.</summary>
        public int BindCount { get; private set; }

        /// <summary>
        /// Supplies the pieces this drives.
        /// </summary>
        /// <remarks>Content arrives as a registry because a client resolves icons and names
        /// locally -- the snapshot carries ids precisely so definitions do not cross the
        /// wire.</remarks>
        public void Compose(NetworkManager networkManager, WorldHudScreen hud,
            InventoryScreen inventory, IDefinitionRegistry<ItemDefinition> items)
        {
            _networkManager = networkManager;
            _hud = hud;
            _inventory = inventory;
            _items = items;

            if (_hud != null) _hud.InventoryRequested += OnInventoryRequested;
        }

        /// <summary>Looks for the owned character and binds or unbinds accordingly.</summary>
        /// <remarks>Public so a test can step it deterministically rather than waiting on a
        /// frame.</remarks>
        public void Poll()
        {
            CharacterNetworkEntity owned = FindOwned();

            if (ReferenceEquals(owned, _bound))
            {
                // Still the same object -- including still null, which is the common case
                // before entering the world.
                return;
            }

            _bound = owned;

            if (owned == null)
            {
                _hud?.Unbind();
                _inventory?.Unbind();
                _inventory?.SetOpen(false);

                return;
            }

            BindCount++;

            _hud?.Bind(owned);
            _inventory?.Bind(owned, _items);
        }

        private void Update()
        {
            Poll();
        }

        private void OnDestroy()
        {
            if (_hud != null) _hud.InventoryRequested -= OnInventoryRequested;
        }

        private void OnInventoryRequested()
        {
            _inventory?.Toggle();
        }

        /// <summary>
        /// The one character object this connection owns.
        /// </summary>
        /// <remarks>A destroyed object still sits in the dictionary for a frame after a
        /// despawn, so the null check is against the Unity object rather than the
        /// reference.</remarks>
        private CharacterNetworkEntity FindOwned()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return null;

            foreach (KeyValuePair<int, NetworkObject> pair in
                _networkManager.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                var entity = pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null && entity.IsOwner) return entity;
            }

            return null;
        }
    }
}
