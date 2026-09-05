using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Gives the characters the server already owns a body clients can see, and a door
    /// their own client can knock on.
    /// </summary>
    /// <remarks>
    /// <b>It replicates; it decides nothing.</b> Who exists is
    /// <see cref="WorldCharacterRegistry"/>'s, their progression is Phase 05's, their health
    /// is Phase 07's and their position is Phase 11's. This walks the registry once a tick
    /// and makes the network objects agree with it -- the same shape
    /// <c>MonsterReplicationService</c> already has, deliberately, because a second
    /// replication style would be a second set of bugs.
    ///
    /// <b>Ownership is the whole security model.</b> Each object is spawned <i>for</i> the
    /// connection the server admitted, so FishNet will only accept that connection's
    /// requests through it. A client cannot spawn one, cannot claim another player's, and
    /// has nowhere to write a character id -- the id is whichever object the message came
    /// through.
    ///
    /// <b>A character with no network object is still a fully working character.</b> It
    /// fights, earns, loots and persists exactly the same; this layer is presentation and a
    /// world that never composed it loses only the ability to draw.
    /// </remarks>
    public sealed class CharacterReplicationService
    {
        private readonly NetworkManager _networkManager;
        private readonly WorldCharacterRegistry _characters;
        private readonly NetworkObject _prefab;
        private readonly ICharacterCombatRequestSink _combat;

        /// <summary>Where a pickup request goes. Null in a world with no loot.</summary>
        private ICharacterLootRequestSink _lootSink;
        private readonly ICharacterMovementRequestSink _movement;
        private ICharacterInventoryRequestSink _inventory;

        /// <summary>Where a spawned character's pet requests are decided.</summary>
        private ICharacterPetRequestSink _pets;

        /// <summary>Where a spawning character's first status snapshot is built from.</summary>
        private ICharacterStatusSource _status;

        private readonly Dictionary<string, NetworkObject> _spawned =
            new Dictionary<string, NetworkObject>();

        /// <summary>Reused each tick so a steady-state server allocates nothing here.</summary>
        private readonly List<string> _stale = new List<string>();

        /// <param name="networkManager">The one production NetworkManager.</param>
        /// <param name="characters">The authoritative registry. Read, never written.</param>
        /// <param name="prefab">The registered character network object.</param>
        /// <param name="combat">
        /// Where a client's request goes. Optional: a world composed without one still
        /// replicates characters, it simply accepts no requests -- which is the right
        /// behaviour for a server that has not wired combat rather than a crash.
        /// </param>
        /// <param name="movement">
        /// Where a client's movement input goes. Optional, like combat: a world composed
        /// without one replicates characters that simply cannot be asked to walk.
        /// </param>
        public CharacterReplicationService(NetworkManager networkManager,
            WorldCharacterRegistry characters, NetworkObject prefab,
            ICharacterCombatRequestSink combat = null,
            ICharacterMovementRequestSink movement = null,
            ICharacterInventoryRequestSink inventory = null)
        {
            _inventory = inventory;
            _networkManager = networkManager;
            _characters = characters;
            _prefab = prefab;
            _combat = combat;
            _movement = movement;
        }

        public int SpawnedCount => _spawned.Count;

        /// <summary>
        /// Points newly spawned characters at the inventory authority.
        /// </summary>
        /// <remarks>
        /// A setter because the authority needs this service to publish snapshots, and this
        /// service needs the authority to hand to each object -- one of the two has to be
        /// built first. Composing it afterwards is honest about that; a constructor taking
        /// each other would not be buildable at all.
        /// </remarks>
        public void UseStatus(ICharacterStatusSource status)
        {
            _status = status;
        }

        /// <summary>Points every spawned character at where pickup requests land.</summary>
        public void UseLoot(ICharacterLootRequestSink loot)
        {
            _lootSink = loot;
        }

        /// <summary>Points every spawned character's pet requests at the server's authority.</summary>
        public void UsePets(ICharacterPetRequestSink pets)
        {
            _pets = pets;
        }

        public void UseInventory(ICharacterInventoryRequestSink inventory)
        {
            _inventory = inventory;
        }

        public bool TryGet(CharacterId character, out NetworkObject networkObject)
        {
            networkObject = null;

            return character.IsValid && !string.IsNullOrEmpty(character.Value)
                && _spawned.TryGetValue(character.Value, out networkObject);
        }

        /// <summary>
        /// Makes the replicated world match the authoritative one.
        /// </summary>
        /// <remarks>Spawns what is new, updates what exists and despawns what has gone, in
        /// that order -- so a character who left and rejoined in the same tick does not
        /// briefly share an object with themselves.</remarks>
        public int Synchronise()
        {
            if (!CanReplicate()) return 0;

            int changed = 0;

            IReadOnlyList<LivingCharacter> all = _characters.All();

            for (int i = 0; i < all.Count; i++)
            {
                LivingCharacter character = all[i];

                string id = character.Character.Value;

                if (string.IsNullOrEmpty(id)) continue;

                if (!_spawned.TryGetValue(id, out NetworkObject existing))
                {
                    if (SpawnFor(character)) changed++;

                    continue;
                }

                Publish(existing, character);
            }

            changed += DespawnDeparted();

            return changed;
        }

        private bool CanReplicate()
        {
            return _networkManager != null
                && _characters != null
                && _prefab != null
                && _networkManager.ServerManager.Started;
        }

        /// <summary>
        /// Spawns one character's object, owned by their own connection.
        /// </summary>
        /// <remarks>
        /// The owner is what makes the request path safe, so a character whose connection
        /// the server no longer holds gets no object at all rather than an unowned one that
        /// anybody could talk through.
        /// </remarks>
        private bool SpawnFor(LivingCharacter character)
        {
            if (!_networkManager.ServerManager.Clients.TryGetValue(character.ConnectionId,
                out NetworkConnection connection) || connection == null)
            {
                return false;
            }

            NetworkObject instance = Object.Instantiate(_prefab);

            var entity = instance.GetComponent<CharacterNetworkEntity>();

            if (entity == null)
            {
                Object.Destroy(instance.gameObject);

                return false;
            }

            _networkManager.ServerManager.Spawn(instance, connection);

            entity.ServerUseCombatSink(_combat);
            entity.ServerUseMovementSink(_movement);
            entity.ServerUseInventorySink(_inventory);
            entity.ServerUsePetSink(_pets);
            entity.ServerUseStatusSource(_status);
            entity.ServerUseLootSink(_lootSink);

            // The owner's fruit at spawn, read from the live state rather than captured, so
            // a reconnecting player is told what they own before anything else happens.
            entity.ServerUseDevilFruitSource(connectionId =>
                _characters.TryGet(connectionId, out LivingCharacter living)
                    && living.DevilFruit != null
                        ? living.DevilFruit.ActiveFruit.Value ?? string.Empty
                        : string.Empty);

            // Identity, including the two visual facts a client needs to draw somebody:
            // which of the two approved models they are, and what to write above them. Both
            // are already public -- the character list shows them before the world loads.
            entity.ServerPublishIdentity(character.Character,
                character.Location == null ? default : character.Location.CurrentMap,
                character.Combatant == null ? 0 : character.Combatant.MaxHealth,
                character.Domain == null || character.Domain.Identity == null
                    ? 0
                    : (int)character.Domain.Identity.Gender,
                character.Domain == null || character.Domain.Identity == null
                    ? null
                    : character.Domain.Identity.Name);

            Publish(instance, character);

            _spawned[character.Character.Value] = instance;

            return true;
        }

        /// <summary>Copies the authoritative state onto the shadow.</summary>
        private static void Publish(NetworkObject networkObject, LivingCharacter character)
        {
            var entity = networkObject == null
                ? null
                : networkObject.GetComponent<CharacterNetworkEntity>();

            if (entity == null) return;

            CombatPosition position = character.Combatant == null
                ? default
                : character.Combatant.Position;

            // The ceilings come from the combatant's current limits, which the stat
            // authority replaces whenever a modifier changes them. Published every time
            // rather than once, because a maximum is state.
            ResourceLimits limits = character.Combatant == null
                ? ResourceLimits.None
                : character.Combatant.Limits;

            entity.ServerPublishState(position.X, position.Y, position.Z,
                character.Combatant == null ? 0 : character.Combatant.CurrentHealth,
                limits.MaxHealth,
                character.Domain?.Resources == null ? 0 : character.Domain.Resources.CurrentMana,
                limits.MaxMana,
                character.Domain.Progression.Level,
                character.Domain.Progression.Experience);

            // Which pet is out, by authored id. Read off the character's own companion
            // state rather than tracked here: this service publishes what the world says
            // and holds no opinion about pets.
            entity.ServerPublishPet(
                character.Companion == null || character.Companion.Summoned == null
                    ? string.Empty
                    : character.Companion.Summoned.DefinitionId.Value);
        }

        /// <summary>Removes objects whose character the registry no longer holds.</summary>
        /// <remarks>Absence is the signal, so every way a character can leave -- a
        /// disconnect, a despawn, a shutdown -- cleans up through one path.</remarks>
        private int DespawnDeparted()
        {
            _stale.Clear();

            foreach (KeyValuePair<string, NetworkObject> pair in _spawned)
            {
                if (_characters.IsSpawned(new CharacterId(pair.Key))) continue;

                _stale.Add(pair.Key);
            }

            for (int i = 0; i < _stale.Count; i++) Despawn(_stale[i]);

            return _stale.Count;
        }

        private void Despawn(string characterId)
        {
            if (!_spawned.TryGetValue(characterId, out NetworkObject networkObject)) return;

            _spawned.Remove(characterId);

            if (networkObject == null) return;

            if (networkObject.IsSpawned)
            {
                _networkManager.ServerManager.Despawn(networkObject);
            }
            else
            {
                Object.Destroy(networkObject.gameObject);
            }
        }

        /// <summary>Despawns everything, for a server that is stopping.</summary>
        public int DespawnAll()
        {
            var all = new List<string>(_spawned.Keys);

            for (int i = 0; i < all.Count; i++) Despawn(all[i]);

            return all.Count;
        }
    }
}
