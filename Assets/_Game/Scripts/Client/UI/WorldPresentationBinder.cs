using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using FishNet.Connection;
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
    ///
    /// <b>The camera binds here too, and only here.</b> One place decides which character is
    /// "mine", so the HUD, the bag and the view can never end up pointed at different
    /// characters -- and a reconnect cannot leave a camera following a corpse while the HUD
    /// has already moved on.
    /// </remarks>
    public sealed class WorldPresentationBinder : MonoBehaviour
    {
        [Tooltip("The client NetworkManager whose owned character this follows.")]
        [SerializeField] private NetworkManager _networkManager;

        private WorldHudScreen _hud;
        private InventoryScreen _inventory;
        private WorldCameraDirector _camera;
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
            InventoryScreen inventory, IDefinitionRegistry<ItemDefinition> items,
            WorldCameraDirector camera = null)
        {
            _networkManager = networkManager;
            _hud = hud;
            _inventory = inventory;
            _camera = camera;
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
                _camera?.Unbind();

                return;
            }

            BindCount++;

            _hud?.Bind(owned);
            _inventory?.Bind(owned, _items);

            // The camera goes through the same door as the HUD and the bag, so a reconnect
            // rebinds all three or none -- a camera bound on its own path would be the one
            // that kept following a destroyed object.
            _camera?.Bind(owned);
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
        /// <remarks>
        /// <b>Asked of the connection, not of the world.</b> A connection already knows
        /// which objects it owns, so the answer is one short walk of that set rather than a
        /// walk of everything the client can see. It used to be the latter, and the cost was
        /// measured rather than suspected: with a hundred and fifty monsters spawned and no
        /// character yet owned, this method allocated about 102 kB per frame and cost
        /// 0.31 ms -- roughly six megabytes of garbage a second, produced by looking for
        /// something that was never in the collection being searched.
        ///
        /// <b>Same answer, and a stricter one.</b> Ownership was previously inferred by
        /// testing every spawned object for <c>IsOwner</c>; the set below <i>is</i> the
        /// objects this connection owns, so the question is answered rather than searched.
        /// A destroyed object can still sit in the set for a frame after a despawn, so the
        /// null check is against the Unity object rather than the reference, exactly as
        /// before.
        ///
        /// <b><c>TryGetComponent</c>, not <c>GetComponent</c>.</b> A failed
        /// <c>GetComponent</c> allocates in the Editor, which is where this is profiled and
        /// where a per-frame search made that allocation a hundred and fifty times over.
        /// </remarks>
        private CharacterNetworkEntity FindOwned()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started) return null;

            NetworkConnection connection = _networkManager.ClientManager.Connection;

            if (connection == null || !connection.IsValid) return null;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(out CharacterNetworkEntity entity)) return entity;
            }

            return null;
        }
    }
}
