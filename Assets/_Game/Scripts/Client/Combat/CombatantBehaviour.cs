using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Combat
{
    /// <summary>
    /// A scene object that can fight.
    /// </summary>
    /// <remarks>
    /// <b>The production combatant.</b> It references no prototype type and nothing in the
    /// prototype references it; the two exist side by side so the older harness keeps
    /// working while this is the path the game grows on.
    ///
    /// <b>It stores no health, no mana and no stat of its own.</b> Resources are a real
    /// <see cref="CharacterResourceState"/> -- the same type the character aggregate uses --
    /// so clamping at zero, the ceiling and the revision behave identically to every other
    /// caller. Stats are answered by id from authored entries, which is a stat
    /// <em>source</em> for an entity that has no <c>CharacterStatsState</c>, not a second
    /// stat system.
    ///
    /// <b>Death is not a field.</b> Aliveness is derived from health by
    /// <see cref="CombatantExtensions.IsAlive"/>, so there is no flag to fall out of step.
    ///
    /// The transform is converted to a <see cref="CombatPosition"/> at the boundary, which
    /// is the only place UnityEngine and the combat rules meet.
    /// </remarks>
    public sealed class CombatantBehaviour : MonoBehaviour, ICombatant, ICombatantResourcePool
    {
        [System.Serializable]
        public struct StatEntry
        {
            [Tooltip("Content id, e.g. stat.atk or stat.mdef. No stat name appears in code.")]
            public string statId;
            public int value;
        }

        [Header("Identity")]
        [SerializeField] private int team = 1;

        [Header("Resources")]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int maxMana = 100;

        [Header("Stats (content ids)")]
        [SerializeField] private StatEntry[] stats;

        [Header("Skills")]
        [Tooltip("Skills this combatant knows. Taught through the real Phase 06 state.")]
        [SerializeField] private SkillDefinition[] knownSkills;

        [Tooltip("Character level, used for each rank's authored level requirement.")]
        [SerializeField] private int characterLevel = 10;

        private CharacterResourceState _resources;
        private ResourceLimits _limits;
        private InstanceId _id;
        private CharacterSkillsState _learnedSkills;
        private readonly SkillCooldownState _cooldowns = new SkillCooldownState();
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

        public int CurrentMana => _resources == null ? 0 : _resources.CurrentMana;

        public int MaxMana => _limits.MaxMana;

        /// <summary>The real resource state, so callers can read its revision.</summary>
        public CharacterResourceState Resources { get { Initialise(); return _resources; } }

        /// <summary>Learned skills, in the real Phase 06 type.</summary>
        public CharacterSkillsState LearnedSkills { get { Initialise(); return _learnedSkills; } }

        /// <summary>Runtime cooldowns. Never persisted.</summary>
        public SkillCooldownState Cooldowns { get { Initialise(); return _cooldowns; } }

        public int CharacterLevel => characterLevel;

        /// <summary>The skills authored on this combatant.</summary>
        public IReadOnlyList<SkillDefinition> KnownSkills =>
            knownSkills ?? System.Array.Empty<SkillDefinition>();

        private void Awake()
        {
            Initialise();
        }

        private void Initialise()
        {
            if (_resources != null) return;

            _id = new InstanceId(name + ":" + GetInstanceID().ToString("X"));
            _limits = new ResourceLimits(Mathf.Max(0, maxHealth), Mathf.Max(0, maxMana));
            _resources = CharacterResourceState.CreateFull(new CharacterId(_id.Value), _limits);
            _learnedSkills = new CharacterSkillsState(new CharacterId(_id.Value));

            _statLookup.Clear();

            if (stats != null)
            {
                for (int i = 0; i < stats.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(stats[i].statId)) continue;
                    _statLookup[new DefinitionId(stats[i].statId)] = stats[i].value;
                }
            }

            if (knownSkills == null) return;

            for (int i = 0; i < knownSkills.Length; i++)
            {
                if (knownSkills[i] == null) continue;

                var id = knownSkills[i].Id;
                if (id.IsValid && !_learnedSkills.Knows(id)) _learnedSkills.Learn(id);
            }
        }

        public bool TryGetCombatStat(DefinitionId stat, out int value)
        {
            Initialise();
            return _statLookup.TryGetValue(stat, out value);
        }

        public void ApplyHealthDelta(long delta)
        {
            Initialise();
            _resources.ChangeHealth(delta, _limits);
        }

        // ---- ICombatantResourcePool: forwards to the same resource state ----

        public bool HasResource(SkillResourceType resource)
        {
            return resource == SkillResourceType.Health || resource == SkillResourceType.Mana;
        }

        public bool TryGetResource(SkillResourceType resource, out int current, out int max)
        {
            Initialise();

            switch (resource)
            {
                case SkillResourceType.Health:
                    current = _resources.CurrentHealth;
                    max = _limits.MaxHealth;
                    return true;

                case SkillResourceType.Mana:
                    current = _resources.CurrentMana;
                    max = _limits.MaxMana;
                    return true;

                default:
                    current = 0;
                    max = 0;
                    return false;
            }
        }

        public bool TryApplyResourceDelta(SkillResourceType resource, long delta)
        {
            Initialise();

            switch (resource)
            {
                case SkillResourceType.Health:
                    _resources.ChangeHealth(delta, _limits);
                    return true;

                case SkillResourceType.Mana:
                    _resources.ChangeMana(delta, _limits);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Sets a stat at runtime, for sandboxes and tests.</summary>
        public void SetStat(string statId, int value)
        {
            Initialise();
            _statLookup[new DefinitionId(statId)] = value;
        }
    }
}
