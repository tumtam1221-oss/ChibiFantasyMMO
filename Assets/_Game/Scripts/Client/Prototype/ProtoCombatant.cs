using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE scene-side combatant for PHASE 07.2.
    /// </summary>
    /// <remarks>
    /// <b>Health is the real thing.</b> This holds a
    /// <see cref="CharacterResourceState"/> -- the same type the character aggregate uses
    /// -- and never an int of its own, so clamping at zero, the ceiling and the revision
    /// behave identically to every other caller. A prototype with its own hit-point field
    /// would be the second HP system this phase exists to avoid.
    ///
    /// <b>Why not <see cref="CharacterCombatant"/> here?</b> That adapter needs a full
    /// <see cref="Character"/> aggregate, which only exists once a character has been built
    /// by the creation service from authored ScriptableObject content. The prototype scene
    /// has no such content and authoring some would be a content task, not a combat one.
    /// This component is therefore shaped like the future <em>monster</em> combatant: an
    /// entity that has resources and stats but is not a player character.
    /// <see cref="CharacterCombatant"/> is the character path and is covered by tests.
    ///
    /// <b>The stat list is a source, not a system.</b> It answers
    /// <see cref="TryGetCombatStat"/> for an entity that has no
    /// <c>CharacterStatsState</c>. It defines no stats, computes nothing and replaces
    /// nothing; a real character reads through the existing stat and derived-stat layers.
    ///
    /// Position is converted from the transform at the boundary, which is the only place
    /// UnityEngine and the combat rules meet.
    /// </remarks>
    public sealed class ProtoCombatant : MonoBehaviour, ICombatant
    {
        [System.Serializable]
        public struct StatEntry
        {
            public string statId;
            public int value;
        }

        [Header("Identity - PROTOTYPE")]
        [SerializeField] private int team = 1;

        [Header("Resources - PROTOTYPE")]
        [SerializeField] private int maxHealth = 100;

        [Header("Stats (content ids) - PROTOTYPE")]
        [SerializeField] private StatEntry[] stats;

        private CharacterResourceState _resources;
        private ResourceLimits _limits;
        private InstanceId _id;
        private readonly Dictionary<DefinitionId, int> _statLookup = new Dictionary<DefinitionId, int>();

        public InstanceId CombatantId => _id;

        public CombatTeam Team
        {
            get { return new CombatTeam(team); }
            set { team = value.Value; }
        }

        public CombatPosition Position
        {
            get
            {
                Vector3 p = transform.position;
                return new CombatPosition(p.x, p.y, p.z);
            }
        }

        public int CurrentHealth => _resources == null ? 0 : _resources.CurrentHealth;

        public int MaxHealth => _limits.MaxHealth;

        /// <summary>The real resource state. Exposed so tests and UI can read the revision.</summary>
        public CharacterResourceState Resources => _resources;

        private void Awake()
        {
            EnsureInitialised();
        }

        private void EnsureInitialised()
        {
            if (_resources != null)
            {
                return;
            }

            // A stable id per scene object. Reused across enable/disable so self-targeting
            // and result identities stay consistent for the life of the object.
            _id = new InstanceId(name + ":" + GetInstanceID().ToString("X"));

            _limits = new ResourceLimits(Mathf.Max(0, maxHealth), 0);
            _resources = CharacterResourceState.CreateFull(
                new CharacterId(_id.Value), _limits);

            _statLookup.Clear();

            if (stats != null)
            {
                for (int i = 0; i < stats.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(stats[i].statId)) continue;
                    _statLookup[new DefinitionId(stats[i].statId)] = stats[i].value;
                }
            }
        }

        public bool TryGetCombatStat(DefinitionId stat, out int value)
        {
            EnsureInitialised();
            return _statLookup.TryGetValue(stat, out value);
        }

        public void ApplyHealthDelta(long delta)
        {
            EnsureInitialised();
            _resources.ChangeHealth(delta, _limits);
        }

        /// <summary>Restores full health. Prototype convenience for repeat testing.</summary>
        public void ResetToFull()
        {
            EnsureInitialised();
            _resources.SetHealth(_limits.MaxHealth, _limits);
        }

        /// <summary>Sets a stat at runtime. Prototype convenience.</summary>
        public void SetStat(string statId, int value)
        {
            EnsureInitialised();
            _statLookup[new DefinitionId(statId)] = value;
        }
    }
}
