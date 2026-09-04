using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Gives the monsters the server already owns a body clients can see.
    /// </summary>
    /// <remarks>
    /// <b>It replicates; it decides nothing.</b> Which monsters exist is
    /// <see cref="MonsterWorldRuntime"/>'s, their behaviour is
    /// <c>MonsterAiController</c>'s, their position is <c>MonsterMovement</c>'s and their
    /// health is combat's. This walks the runtime once a tick and makes the network objects
    /// agree with it. Nothing here computes a value that gameplay did not already decide,
    /// and a monster with no network object is still a fully working monster -- which is the
    /// test that this layer is genuinely presentation.
    ///
    /// <b>One object per monster, keyed by the monster's own instance id.</b> No second
    /// identity is minted: the network object is looked up by the id the runtime already
    /// uses, so a monster and its shadow can never drift apart or be confused for two
    /// things.
    ///
    /// <b>Spawning and despawning are the server's alone.</b> FishNet's
    /// <c>ServerManager.Spawn</c> is the only way an object appears, and there is no message
    /// a client could send that reaches this class. A client observes what the server
    /// spawned and cannot ask for more.
    ///
    /// <b>Despawn is driven by absence, not by an event.</b> Each tick, any object whose
    /// monster the runtime no longer holds is despawned. That means a monster retired for
    /// any reason -- defeated and claimed, cleared on shutdown, an area reset -- cleans up
    /// through one path rather than needing every caller to remember.
    /// </remarks>
    public sealed class MonsterReplicationService
    {
        private readonly NetworkManager _networkManager;
        private readonly MonsterWorldRuntime _runtime;
        private readonly NetworkObject _prefab;

        private readonly Dictionary<string, NetworkObject> _spawned =
            new Dictionary<string, NetworkObject>();

        /// <summary>Reused each tick so a steady-state server allocates nothing here.</summary>
        private readonly List<string> _stale = new List<string>();

        /// <param name="networkManager">The one production NetworkManager.</param>
        /// <param name="runtime">The authoritative monster runtime. Read, never written.</param>
        /// <param name="prefab">
        /// The registered monster network object. Supplied rather than loaded by path, so
        /// this class names no asset and a test can hand it the real registered prefab.
        /// </param>
        public MonsterReplicationService(NetworkManager networkManager,
            MonsterWorldRuntime runtime, NetworkObject prefab)
        {
            _networkManager = networkManager;
            _runtime = runtime;
            _prefab = prefab;
        }

        /// <summary>How many monsters currently have a network object.</summary>
        public int SpawnedCount => _spawned.Count;

        public bool TryGet(InstanceId monster, out NetworkObject networkObject)
        {
            networkObject = null;

            return monster.IsValid && !string.IsNullOrEmpty(monster.Value)
                && _spawned.TryGetValue(monster.Value, out networkObject);
        }

        /// <summary>
        /// Makes the replicated world match the authoritative one.
        /// </summary>
        /// <remarks>
        /// Called after <see cref="MonsterWorldRuntime.Tick"/>, never instead of it. Spawns
        /// what is new, updates what exists and despawns what has gone -- in that order, so
        /// a monster retired and replaced in the same tick does not briefly share an object
        /// with its successor.
        /// </remarks>
        public int Synchronise()
        {
            if (!CanReplicate()) return 0;

            int changed = 0;

            foreach (LivingMonster monster in _runtime.All())
            {
                string id = monster.Instance.Value;

                if (string.IsNullOrEmpty(id)) continue;

                if (!_spawned.TryGetValue(id, out NetworkObject existing))
                {
                    if (SpawnFor(monster)) changed++;

                    continue;
                }

                Publish(existing, monster);
            }

            changed += DespawnDeparted();

            return changed;
        }

        private bool CanReplicate()
        {
            return _networkManager != null
                && _runtime != null
                && _prefab != null
                && _networkManager.ServerManager.Started;
        }

        /// <summary>
        /// Spawns one monster's network object and publishes what it is.
        /// </summary>
        /// <remarks>
        /// No owner is passed to <c>Spawn</c>, deliberately: an owned object lets its owner
        /// write to it, and a monster belongs to nobody. Server-owned is the only correct
        /// answer for a thing no player controls.
        /// </remarks>
        private bool SpawnFor(LivingMonster monster)
        {
            NetworkObject instance = Object.Instantiate(_prefab);

            var entity = instance.GetComponent<MonsterNetworkEntity>();

            if (entity == null)
            {
                Object.Destroy(instance.gameObject);

                return false;
            }

            _networkManager.ServerManager.Spawn(instance);

            entity.ServerPublishIdentity(monster.Instance, monster.State.DefinitionId,
                monster.Map, monster.State.MaxHealth);

            Publish(instance, monster);

            _spawned[monster.Instance.Value] = instance;

            return true;
        }

        /// <summary>Copies the authoritative position and health onto the shadow.</summary>
        private static void Publish(NetworkObject networkObject, LivingMonster monster)
        {
            var entity = networkObject == null
                ? null
                : networkObject.GetComponent<MonsterNetworkEntity>();

            if (entity == null) return;

            entity.ServerPublishState(
                monster.State.Position.X,
                monster.State.Position.Y,
                monster.State.Position.Z,
                monster.State.CurrentHealth);
        }

        /// <summary>
        /// Removes objects whose monster the runtime no longer holds.
        /// </summary>
        /// <remarks>Absence is the signal, so every way a monster can leave -- defeated and
        /// claimed, cleared, reset -- cleans up through one path instead of each caller
        /// having to remember.</remarks>
        private int DespawnDeparted()
        {
            _stale.Clear();

            foreach (KeyValuePair<string, NetworkObject> pair in _spawned)
            {
                if (_runtime.TryGetMonster(new InstanceId(pair.Key), out _)) continue;

                _stale.Add(pair.Key);
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                Despawn(_stale[i]);
            }

            return _stale.Count;
        }

        private void Despawn(string monsterId)
        {
            if (!_spawned.TryGetValue(monsterId, out NetworkObject networkObject)) return;

            _spawned.Remove(monsterId);

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

        /// <summary>
        /// Despawns everything, for a server that is stopping.
        /// </summary>
        /// <remarks>Without it a shutdown leaves network objects behind that no monster
        /// corresponds to, and a restart would find the scene already populated.</remarks>
        public int DespawnAll()
        {
            var all = new List<string>(_spawned.Keys);

            for (int i = 0; i < all.Count; i++) Despawn(all[i]);

            return all.Count;
        }
    }
}
