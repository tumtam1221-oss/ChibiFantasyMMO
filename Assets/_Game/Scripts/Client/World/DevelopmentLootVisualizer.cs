// Development only. A production client compiles none of this file, so no build a player
// receives can draw one of these.
#if DEVELOPMENT_BUILD || UNITY_EDITOR

using System.Collections.Generic;
using ChibiFantasy.Network;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Shows where a pile of loot is, until loot has art.
    /// </summary>
    /// <remarks>
    /// <b>This is not loot art and must never be described as such.</b> The same honest gap
    /// as monsters: this project has no dropped-item model, so a small cube stands in. What
    /// it makes possible is a person clicking a pile and walking to it, which is the thing
    /// this gate is about.
    ///
    /// <b>It draws what the server offered, and nothing else.</b> Positions and identities
    /// come from <see cref="LootSnapshot"/>, which the server sends to this owner alone. No
    /// pile is invented, none is kept after the server stops offering it, and clicking one
    /// still asks the server for it rather than taking it.
    /// </remarks>
    public sealed class DevelopmentLootVisualizer : MonoBehaviour
    {
        private const float Size = 0.35f;

        private readonly Dictionary<string, GameObject> _shown =
            new Dictionary<string, GameObject>();

        private readonly List<string> _gone = new List<string>();

        private WorldLootInput _loot;
        private Material _material;

        /// <summary>How many placeholders are standing. For tests.</summary>
        public int Count => _shown.Count;

        /// <summary>The production presenter, when one draws piles; this then draws none.</summary>
        public WorldLootPresenter Presenter { get; set; }

        /// <summary>Points this at the loot this client has been offered.</summary>
        public void Compose(WorldLootInput loot)
        {
            _loot = loot;
        }

        private void Update()
        {
            if (_loot == null) return;

            if (Presenter != null && Presenter.IsComposed)
            {
                // Production draws every pile now; a cube on top of it would only steal
                // the click. Anything this drew before the presenter arrived is dropped.
                Forget(default);

                return;
            }

            Sweep();
        }

        private void Sweep()
        {
            LootSnapshot snapshot = _loot.Snapshot;

            if (snapshot.Entries != null)
            {
                for (var i = 0; i < snapshot.Entries.Length; i++)
                {
                    LootEntrySnapshot entry = snapshot.Entries[i];

                    string key = entry.LootId + "#" + entry.Index;

                    if (!_shown.TryGetValue(key, out GameObject shown) || shown == null)
                    {
                        shown = Build(entry);

                        _shown[key] = shown;
                    }

                    shown.transform.position = new Vector3(entry.X, entry.Y + Size, entry.Z);
                }
            }

            Forget(snapshot);
        }

        /// <summary>Drops placeholders for piles the server no longer offers.</summary>
        private void Forget(LootSnapshot snapshot)
        {
            _gone.Clear();

            foreach (KeyValuePair<string, GameObject> pair in _shown)
            {
                var offered = false;

                if (snapshot.Entries != null)
                {
                    for (var i = 0; i < snapshot.Entries.Length && !offered; i++)
                    {
                        offered = pair.Key == snapshot.Entries[i].LootId + "#"
                            + snapshot.Entries[i].Index;
                    }
                }

                if (!offered || pair.Value == null) _gone.Add(pair.Key);
            }

            for (var i = 0; i < _gone.Count; i++)
            {
                if (_shown[_gone[i]] != null) Destroy(_shown[_gone[i]]);

                _shown.Remove(_gone[i]);
            }
        }

        /// <summary>One cube, one collider, and which pile it stands for.</summary>
        private GameObject Build(LootEntrySnapshot entry)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

            cube.name = "Loot Placeholder (development)";
            cube.transform.SetParent(transform, worldPositionStays: true);
            cube.transform.localScale = new Vector3(Size, Size, Size);

            var marker = cube.AddComponent<WorldLootMarker>();

            marker.LootId = entry.LootId;
            marker.Index = entry.Index;

            if (_material == null)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.95f, 0.78f, 0.25f),
                };
            }

            cube.GetComponent<Renderer>().sharedMaterial = _material;

            return cube;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<string, GameObject> pair in _shown)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }

            _shown.Clear();

            if (_material != null) Destroy(_material);
        }
    }
}

#endif
