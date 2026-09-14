using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Which visual model, and how it sits in the hand, stands in for an equipped weapon.
    /// </summary>
    /// <remarks>
    /// <b>Keyed by the equipment's content id, never by name.</b> The entry says
    /// <c>item.weapon.sapphire_guardian</c> and points at a presentation prefab, exactly as
    /// <see cref="CharacterVisualCatalogue"/> and <see cref="MonsterVisualCatalogue"/> key
    /// their models. Nothing here compares a GameObject's name to decide anything.
    ///
    /// <b>Presentation only.</b> The server decides what is equipped and what it does; this
    /// only decides what the equipped thing looks like and where the hand holds it. Nothing
    /// here is read by gameplay — an item with no entry simply shows no weapon, which is
    /// honest, and the character still fights with whatever the server grants.
    ///
    /// <b>The grip transform lives with the weapon, not the rig.</b> Each weapon carries its
    /// own local offset/rotation/scale relative to the hand socket, because that is a
    /// property of where the model's grip is, not of the character. One canonical socket on
    /// the rig plus a per-weapon offset keeps a mis-modelled pivot from forcing a rig change.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Presentation/Weapon Visual Catalogue",
        fileName = "WeaponVisualCatalogue")]
    public sealed class WeaponVisualCatalogue : ScriptableObject
    {
        /// <summary>One weapon's approved presentation and grip.</summary>
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("The stable content id of the equipment item, e.g. "
                + "item.weapon.sapphire_guardian.")]
            public DefinitionId Item;

            [Tooltip("The prefab instanced under the hand socket. Presentation only; never "
                + "modified at runtime.")]
            public GameObject Prefab;

            [Tooltip("Local position of the weapon relative to the hand socket, so the hand "
                + "holds the grip rather than the blade or empty space.")]
            public Vector3 GripPosition;

            [Tooltip("Local rotation (euler degrees) relative to the hand socket, so the "
                + "blade points the right way out of the fist.")]
            public Vector3 GripEuler;

            [Tooltip("Uniform scale the model is shown at in the hand. Zero is treated as 1.")]
            public float Scale;
        }

        [SerializeField] private Entry[] _entries = new Entry[0];

        /// <summary>Every authored entry. For validation and for tests.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>The presentation for one equipped item, if the catalogue ships one.</summary>
        public bool TryGet(DefinitionId item, out Entry entry)
        {
            if (item.IsValid && _entries != null)
            {
                for (var i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].Item == item)
                    {
                        entry = _entries[i];

                        return entry.Prefab != null;
                    }
                }
            }

            entry = default;

            return false;
        }
    }
}
