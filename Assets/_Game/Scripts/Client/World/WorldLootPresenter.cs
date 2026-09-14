using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Draws the piles of loot the server has offered this player, in every build.
    /// </summary>
    /// <remarks>
    /// <b>Production, not development.</b> Until now a dropped item was drawn only by
    /// <c>DevelopmentLootVisualizer</c>, which is compiled out of the build a player
    /// receives -- so in a shipped client a slime died, the server offered its gel, and
    /// nothing appeared on the ground to walk to. This is the thing that appears.
    ///
    /// <b>It draws what the server offered, and nothing else.</b> Positions, identities and
    /// quantities come from <see cref="LootSnapshot"/>, which the server sends to this
    /// owner alone. No pile is invented, none is kept after the server stops offering it
    /// (picked up, or expired), and clicking one still asks the server for it through the
    /// existing <see cref="WorldLootMarker"/> the pointer already reads.
    ///
    /// <b>No loot art exists, so the pile is drawn rather than modelled:</b> a small
    /// rounded drop that bobs, with the item's translated name and quantity over it. Honest
    /// about what it is; the day loot has a model, this is where it is hung.
    /// </remarks>
    public sealed class WorldLootPresenter : MonoBehaviour
    {
        /// <summary>How wide the drop is, in metres. Small next to a 0.36 m slime.</summary>
        public const float Size = 0.22f;

        /// <summary>Where the label floats, above the drop.</summary>
        public const float PlateHeight = 0.42f;

        private const float LabelFontSize = 0.7f;
        private const float BobMetres = 0.03f;
        private const float BobHertz = 1.2f;

        private sealed class Shown
        {
            public GameObject Visual;
            public CharacterNameplate Plate;
            public float Bob;
            public LootEntrySnapshot Entry;
        }

        private readonly Dictionary<string, Shown> _shown = new Dictionary<string, Shown>();
        private readonly List<string> _gone = new List<string>();

        private WorldLootInput _loot;
        private IDefinitionRegistry<ItemDefinition> _items;
        private ILocalizedTextSource _text;
        private Material _material;

        /// <summary>How many piles are standing. For tests.</summary>
        public int Count => _shown.Count;

        /// <summary>Whether this has something to draw from.</summary>
        public bool IsComposed => _loot != null;

        /// <summary>Points this at the loot this client has been offered.</summary>
        /// <param name="items">Where an item's name comes from; null shows the id, readable.</param>
        /// <param name="text">The player's language; null shows the id, readable.</param>
        public void Compose(WorldLootInput loot, IDefinitionRegistry<ItemDefinition> items,
            ILocalizedTextSource text)
        {
            _loot = loot;
            _items = items;
            _text = text;
        }

        /// <summary>The pile object standing for one offered entry, if it is drawn.</summary>
        public bool TryGetVisual(string lootId, int index, out GameObject visual)
        {
            visual = null;

            if (!_shown.TryGetValue(lootId + "#" + index, out Shown shown) || shown == null)
            {
                return false;
            }

            visual = shown.Visual;

            return visual != null;
        }

        private void Update()
        {
            if (_loot == null) return;

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

                    if (!_shown.TryGetValue(key, out Shown shown) || shown == null
                        || shown.Visual == null)
                    {
                        shown = Build(entry);

                        _shown[key] = shown;
                    }

                    // A pile's quantity can change under it: taking what fits of a stack
                    // leaves the rest, and the label has to say what is left.
                    if (shown.Entry.Quantity != entry.Quantity
                        || shown.Entry.ItemId != entry.ItemId)
                    {
                        shown.Entry = entry;
                        shown.Plate.Refresh(LabelFor(entry));
                    }

                    shown.Bob += Time.deltaTime;

                    float lift = Size * 0.5f
                        + BobMetres * (0.5f + 0.5f * Mathf.Sin(shown.Bob * BobHertz * 2f * Mathf.PI));

                    shown.Visual.transform.position = new Vector3(entry.X, entry.Y + lift, entry.Z);
                    shown.Plate.FaceCamera();
                }
            }

            Forget(snapshot);
        }

        /// <summary>Drops piles the server no longer offers -- taken, or timed out.</summary>
        private void Forget(LootSnapshot snapshot)
        {
            _gone.Clear();

            foreach (KeyValuePair<string, Shown> pair in _shown)
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

                if (!offered || pair.Value == null || pair.Value.Visual == null)
                {
                    _gone.Add(pair.Key);
                }
            }

            for (var i = 0; i < _gone.Count; i++)
            {
                Shown shown = _shown[_gone[i]];

                if (shown != null && shown.Visual != null) Destroy(shown.Visual);

                _shown.Remove(_gone[i]);
            }
        }

        /// <summary>One drop, one collider, its label, and which pile it stands for.</summary>
        private Shown Build(LootEntrySnapshot entry)
        {
            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            drop.name = "Loot " + entry.ItemId;
            drop.transform.SetParent(transform, worldPositionStays: true);
            drop.transform.localScale = new Vector3(Size, Size * 0.7f, Size);

            // The sphere's own collider is what a click lands on; grown a little so a
            // small drop is not a hard target at the MMO camera's distance.
            var collider = drop.GetComponent<SphereCollider>();

            if (collider != null) collider.radius = 1.4f;

            var marker = drop.AddComponent<WorldLootMarker>();

            marker.LootId = entry.LootId;
            marker.Index = entry.Index;

            if (_material == null)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.98f, 0.82f, 0.30f),
                };

                _material.SetFloat("_Smoothness", 0.7f);
            }

            drop.GetComponent<Renderer>().sharedMaterial = _material;

            // The label is hung on the pile rather than the drop, so it does not squash
            // with the drop's flattened scale.
            var pile = new GameObject("Pile").transform;

            pile.SetParent(transform, worldPositionStays: true);

            drop.transform.SetParent(pile, worldPositionStays: true);

            CharacterNameplate plate = CharacterNameplate.Create(pile, PlateHeight,
                LabelFontSize);

            plate.Refresh(LabelFor(entry));

            return new Shown
            {
                Visual = pile.gameObject,
                Plate = plate,
                Entry = entry,
                Bob = Random.value,
            };
        }

        /// <summary>"Slime Gel x2": the item's translated name, and how many.</summary>
        public string LabelFor(LootEntrySnapshot entry)
        {
            string name = NameOf(new DefinitionId(entry.ItemId ?? string.Empty));

            return entry.Quantity > 1 ? name + " x" + entry.Quantity : name;
        }

        /// <summary>The item's name in the player's language, or its id made readable.</summary>
        public string NameOf(DefinitionId item)
        {
            ItemDefinition authored = null;

            if (_items != null && item.IsValid) _items.TryGet(item, out authored);

            if (authored != null && _text != null && authored.NameKey.IsValid
                && _text.TryGet(authored.NameKey, out string name)
                && !string.IsNullOrEmpty(name))
            {
                return name;
            }

            return UiText.Readable(item);
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<string, Shown> pair in _shown)
            {
                if (pair.Value != null && pair.Value.Visual != null) Destroy(pair.Value.Visual);
            }

            _shown.Clear();

            if (_material != null) Destroy(_material);
        }
    }
}
