using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Which approved model stands in for a monster.
    /// </summary>
    /// <remarks>
    /// <b>Keyed by definition id, never by name.</b> The entry says
    /// <c>monster.training_slime</c> and points at a prefab. Nothing anywhere compares a
    /// GameObject's name to decide what a monster is -- that is the rule the whole content
    /// layer is built on, and it is what lets an artist rename a prefab, a folder or a mesh
    /// without touching a line of code.
    ///
    /// <b>The same shape as <see cref="CharacterVisualCatalogue"/>, deliberately.</b> Players
    /// already resolve their model through an authored asset rather than a path in an
    /// assembly. A second mechanism for monsters would be a second thing to learn and a
    /// second thing to forget; this is the one that already exists, pointed at monsters.
    ///
    /// <b>Presentation only.</b> Nothing here is read by the server or by gameplay. A monster
    /// with no entry has no model, which is honest -- the world still spawns it, it still
    /// fights, and the gap is visible rather than papered over with a stand-in that looks
    /// like finished art.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Presentation/Monster Visual Catalogue",
        fileName = "MonsterVisualCatalogue")]
    public sealed class MonsterVisualCatalogue : ScriptableObject
    {
        /// <summary>One monster's approved presentation.</summary>
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("The stable content id, e.g. monster.training_slime.")]
            public DefinitionId Monster;

            [Tooltip("The prefab instanced under the monster's visual root. Never modified.")]
            public GameObject Prefab;

            [Tooltip("Height above the monster root that its name and health sit at. "
                + "Measured from the model, not guessed: a slime's nameplate belongs just "
                + "above a slime, not where a player's would be.")]
            public float NameplateHeight;

            [Tooltip("How far one cycle of the move animation is supposed to carry this "
                + "monster, in metres. The presentation plays the hop faster or slower so "
                + "that a bounce lands about once per step, instead of the monster sliding.")]
            public float HopMetres;

            [Tooltip("Uniform scale the model is shown at. 1 means the size it was modelled. "
                + "Use this to settle how big a monster reads next to a player rather than "
                + "re-exporting the mesh. Zero is treated as 1.")]
            public float VisualScale;
        }

        [SerializeField] private Entry[] _entries = new Entry[0];

        /// <summary>Every authored entry. For validation and for tests.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// The model for a monster, or null when none is authored.
        /// </summary>
        /// <remarks>Null is a real answer rather than a fault: most of this game's monsters
        /// have no approved art yet, and a catalogue that substituted a capsule would make
        /// "finished" and "not started" look the same in a screenshot.</remarks>
        public GameObject PrefabFor(DefinitionId monster)
        {
            if (!monster.IsValid) return null;

            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Monster == monster) return _entries[i].Prefab;
            }

            return null;
        }

        /// <summary>
        /// How far one cycle of this monster's move animation is meant to carry it.
        /// </summary>
        /// <remarks>
        /// <b>Presentation timing, not movement.</b> The server decides where a monster is;
        /// this only says how fast to play the hop so that the bounce lands roughly when the
        /// monster has travelled a hop's worth of ground. Without it a slime authored to
        /// stroll at half a metre a second plays the same one-second hop it plays while
        /// chasing, and the difference shows up as sliding.
        ///
        /// Authored per monster because a hop is a property of the creature. Falls back to a
        /// sensible default so an unauthored monster still animates at its clip's own rate.
        /// </remarks>
        public float HopMetresFor(DefinitionId monster)
        {
            if (!monster.IsValid) return 0f;

            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Monster == monster) return _entries[i].HopMetres;
            }

            return 0f;
        }

        /// <summary>
        /// How big to draw this monster, as a multiple of the size it was modelled at.
        /// </summary>
        /// <remarks>
        /// <b>Why here and not in the mesh.</b> How large a creature reads is a judgement made
        /// by standing it next to a player, and it gets revisited; the shape it is, is not.
        /// Keeping the two apart means the size can be settled without re-exporting an
        /// approved model, and without every measurement written about that model going stale.
        ///
        /// <b>Presentation only.</b> The server's idea of where this monster is, how far it
        /// reaches and how close it has to be to swing is untouched by it -- those are metres
        /// in the world, not pixels on a screen.
        ///
        /// Zero means unauthored, which is treated as full size rather than as a monster
        /// scaled out of existence.
        /// </remarks>
        public float VisualScaleFor(DefinitionId monster)
        {
            if (!monster.IsValid) return 1f;

            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Monster != monster) continue;

                return _entries[i].VisualScale > 0f ? _entries[i].VisualScale : 1f;
            }

            return 1f;
        }

        /// <summary>Where this monster's nameplate belongs. Zero when unauthored.</summary>
        public float NameplateHeightFor(DefinitionId monster)
        {
            if (!monster.IsValid) return 0f;

            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Monster == monster) return _entries[i].NameplateHeight;
            }

            return 0f;
        }

        /// <summary>Whether this catalogue has anything to show for a monster.</summary>
        public bool Knows(DefinitionId monster)
        {
            return PrefabFor(monster) != null;
        }

        /// <summary>
        /// Faults an authored catalogue must not ship with.
        /// </summary>
        /// <remarks>An entry with no id points at art nothing can ask for; an entry with no
        /// prefab is a promise of a model that does not arrive; two entries for one monster
        /// mean the answer depends on array order, which is the kind of thing that changes
        /// when somebody reorders a list in the inspector.</remarks>
        public bool Validate(List<string> faults)
        {
            if (faults == null) faults = new List<string>();

            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            for (var i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];

                if (!e.Monster.IsValid)
                {
                    faults.Add("entry " + i + " names no monster");
                    continue;
                }

                if (!seen.Add(e.Monster.Value))
                {
                    faults.Add(e.Monster.Value + " is listed more than once");
                }

                if (e.Prefab == null)
                {
                    faults.Add(e.Monster.Value + " has no prefab");
                }
            }

            return faults.Count == 0;
        }
    }
}
