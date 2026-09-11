// Development only. A production client compiles none of this file, so no build a player
// receives can draw one of these.
#if DEVELOPMENT_BUILD || UNITY_EDITOR

using System.Collections.Generic;
using ChibiFantasy.Network;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Shows where an authoritative monster is, until monsters have art.
    /// </summary>
    /// <remarks>
    /// <b>This is not monster art and must never be described as such.</b> This project has
    /// no monster model -- <c>Art/Monsters</c> is empty -- and the shipped network prefab is
    /// a network identity with no renderer. That is the honest state of the project, and
    /// 18.18B deliberately refused to invent art to fill it.
    ///
    /// What it makes possible is a person playing the game: a capsule and a collider are
    /// enough to see that something authoritative is standing there, click it, and watch its
    /// health fall. The capsule is a Unity primitive, built at runtime, on the client, from
    /// nothing this project authored.
    ///
    /// <b>Compiled out of a production build.</b> The guard above is the mechanism, and a
    /// test asserts it. A player's build draws monsters when there are monsters to draw.
    ///
    /// <b>It presents; it decides nothing.</b> Position and health come from
    /// <see cref="MonsterNetworkEntity"/>, which the server writes. Nothing here moves a
    /// monster, damages one, or decides that one has died.
    /// </remarks>
    public sealed class DevelopmentMonsterVisualizer : MonoBehaviour
    {
        /// <summary>How tall the placeholder stands, in metres.</summary>
        private const float Height = 1.8f;

        private readonly Dictionary<int, GameObject> _shown = new Dictionary<int, GameObject>();

        private NetworkManager _networkManager;
        private Material _material;

        /// <summary>
        /// The presenter that draws monsters which do have art.
        /// </summary>
        /// <remarks>Optional. When set, this placeholder skips anything that presenter is
        /// already drawing -- otherwise a capsule stands inside the slime, and the two
        /// together look like a rendering bug rather than like progress.</remarks>
        public WorldMonsterPresenter Presenter { get; set; }

        /// <summary>How many placeholders are currently standing. For tests.</summary>
        public int Count => _shown.Count;

        /// <summary>Points this at the client whose monsters it should show.</summary>
        public void Compose(NetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        private void Update()
        {
            // Objects too: an editor domain reload tears FishNet's tables down while
            // Update is still running, and the sweep below would throw on them.
            if (_networkManager == null
                || _networkManager.ClientManager == null
                || !_networkManager.ClientManager.Started
                || _networkManager.ClientManager.Objects == null
                || _networkManager.ClientManager.Objects.Spawned == null)
            {
                return;
            }

            Sweep();
        }

        /// <summary>Adds what has arrived, moves what is here, removes what has gone.</summary>
        private void Sweep()
        {
            foreach (KeyValuePair<int, NetworkObject> pair in
                _networkManager.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                if (!pair.Value.TryGetComponent(out MonsterNetworkEntity monster)) continue;

                // Somebody else is drawing this one properly.
                if (Presenter != null && Presenter.Draws(monster.Definition)) continue;

                if (!_shown.TryGetValue(pair.Key, out GameObject shown) || shown == null)
                {
                    shown = Build(pair.Value.transform);

                    _shown[pair.Key] = shown;
                }

                // The server's position, followed rather than interpreted.
                shown.transform.position = new Vector3(monster.X, monster.Y + Height * 0.5f,
                    monster.Z);

                shown.SetActive(monster.IsAlive);
            }

            Forget();
        }

        /// <summary>Drops placeholders whose monster the server has taken away.</summary>
        private void Forget()
        {
            var gone = new List<int>();

            foreach (KeyValuePair<int, GameObject> pair in _shown)
            {
                if (_networkManager.ClientManager.Objects.Spawned.ContainsKey(pair.Key)
                    && pair.Value != null)
                {
                    continue;
                }

                gone.Add(pair.Key);
            }

            for (var i = 0; i < gone.Count; i++)
            {
                if (_shown[gone[i]] != null) Destroy(_shown[gone[i]]);

                _shown.Remove(gone[i]);
            }
        }

        /// <summary>
        /// One capsule, one collider, and a way back to the monster it stands for.
        /// </summary>
        /// <remarks>The collider is the point as much as the capsule is: without one there
        /// is nothing for a click to hit, and targeting would have to guess.</remarks>
        private GameObject Build(Transform monster)
        {
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            capsule.name = "Monster Placeholder (development)";
            capsule.transform.SetParent(transform, worldPositionStays: true);
            capsule.transform.localScale = new Vector3(0.9f, Height * 0.5f, 0.9f);

            var marker = capsule.AddComponent<DevelopmentMonsterMarker>();

            marker.Monster = monster == null
                ? null
                : monster.GetComponent<MonsterNetworkEntity>();

            if (_material == null)
            {
                // The pipeline's own default, so this draws correctly under URP without
                // shipping a material of its own.
                _material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.75f, 0.2f, 0.25f),
                };
            }

            capsule.GetComponent<Renderer>().sharedMaterial = _material;

            return capsule;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, GameObject> pair in _shown)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }

            _shown.Clear();

            if (_material != null) Destroy(_material);
        }
    }
}

#endif
