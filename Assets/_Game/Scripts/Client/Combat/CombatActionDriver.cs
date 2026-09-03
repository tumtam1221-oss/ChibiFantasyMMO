using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Combat
{
    /// <summary>
    /// Drives one combatant's production combat runtime.
    /// </summary>
    /// <remarks>
    /// <b>A bridge, not a rule.</b> It owns a <see cref="CombatActionRunner"/>, feeds it the
    /// frame delta and forwards requests. Validation, damage, healing, cost, cooldown and
    /// timing all live in the Gameplay assembly; nothing here computes a number or writes a
    /// resource. Input calls the methods on this type and never touches health, mana,
    /// learned skills or cooldowns.
    ///
    /// <b>Every combat number is authored.</b> The stat ids and the damage floor come from
    /// serialized fields; cast time, cooldown, cost, range and effects come from the skill
    /// definition. Changing balance is editing data, not this file. There is deliberately
    /// no place here to write "if skill == fireball".
    ///
    /// <b>Presentation is downstream.</b> An event is raised after the fact; nothing waits
    /// for a listener and combat resolves identically with none attached.
    /// </remarks>
    [RequireComponent(typeof(CombatantBehaviour))]
    public sealed class CombatActionDriver : MonoBehaviour
    {
        [Header("Combat stat ids (content, not code)")]
        [SerializeField] private string attackPowerStatId = "stat.atk";
        [SerializeField] private string defenseStatId = "stat.def";
        [SerializeField] private string magicDefenseStatId = "stat.mdef";

        [Header("Basic attack")]
        [SerializeField] private int basicAttackMinimumDamage = 1;
        [SerializeField] private float basicAttackRange = 1.6f;
        [SerializeField] private float basicAttackWindUp;
        [SerializeField] private float basicAttackRecovery = 0.35f;

        [Header("Skills")]
        [SerializeField] private int skillMinimumDamage;
        [SerializeField] private float skillRecovery = 0.25f;

        [Header("Target")]
        [SerializeField] private CombatantBehaviour target;

        private CombatantBehaviour _self;
        private CombatActionRunner _runner;
        private DefinitionRegistry<SkillDefinition> _registry;

        /// <summary>Raised after an action's effect resolved. Presentation only.</summary>
        public event System.Action<CombatAction> ActionExecuted;

        /// <summary>Raised when an action ends, however it ended. Presentation only.</summary>
        public event System.Action<CombatAction> ActionFinished;

        public CombatantBehaviour Self => _self;

        public CombatActionRunner Runner => _runner;

        public CombatActionPhase Phase => _runner == null ? CombatActionPhase.Idle : _runner.Phase;

        public bool IsAvailable => _runner != null && _runner.IsAvailable;

        public void SetTarget(CombatantBehaviour value) { target = value; }

        public CombatantBehaviour Target => target;

        private void Awake()
        {
            _self = GetComponent<CombatantBehaviour>();
            _runner = new CombatActionRunner(_self);

            _registry = new DefinitionRegistry<SkillDefinition>();

            var known = _self.KnownSkills;
            for (int i = 0; i < known.Count; i++)
            {
                if (known[i] != null) _registry.Register(known[i]);
            }
        }

        /// <summary>Rules for a basic attack, built from authored ids and numbers.</summary>
        public BasicAttackRules BuildAttackRules()
        {
            return BasicAttackRules.Melee(
                new DefinitionId(attackPowerStatId),
                new DefinitionId(defenseStatId),
                basicAttackMinimumDamage,
                basicAttackRange);
        }

        /// <summary>
        /// Rules for skill damage: which stat resists physical, which resists magic.
        /// </summary>
        /// <remarks>Both are content ids. The formula is the same one basic attacks use;
        /// only the defending stat differs by the effect's authored damage type.</remarks>
        public SkillExecutionRules BuildSkillRules()
        {
            return new SkillExecutionRules(
                new DefinitionId(defenseStatId),
                new DefinitionId(magicDefenseStatId),
                skillMinimumDamage);
        }

        public SkillUseContext BuildContext()
        {
            return new SkillUseContext(
                _registry, _self.LearnedSkills, _self.CharacterLevel, _self.Cooldowns);
        }

        private void Update()
        {
            if (_runner == null) return;

            float dt = Time.deltaTime;

            // Runtime timing, supplied by the caller. The rules never read a clock.
            _self.Cooldowns.Advance(dt);

            CombatActionPhase before = _runner.Phase;
            bool hadExecuted = _runner.Current != null && _runner.Current.HasExecuted;

            _runner.Advance(dt);

            CombatAction action = _runner.Current;
            if (action == null) return;

            if (!hadExecuted && action.HasExecuted) RaiseExecuted(action);
            if (before != CombatActionPhase.Idle && action.IsFinished) RaiseFinished(action);
        }

        /// <summary>Requests a basic attack against the current target.</summary>
        public CombatActionResult RequestBasicAttack()
        {
            if (_runner == null) return CombatActionResult.Rejected(CombatActionRejection.NoActor);

            CombatActionResult result = _runner.RequestBasicAttack(
                target, BuildAttackRules(), basicAttackWindUp, basicAttackRecovery);

            if (result.IsAccepted && result.Action.HasExecuted) RaiseExecuted(result.Action);
            return result;
        }

        /// <summary>Requests a skill by content id against the current target.</summary>
        public CombatActionResult RequestSkill(string skillId, int rank = 1)
        {
            if (_runner == null) return CombatActionResult.Rejected(CombatActionRejection.NoActor);

            var request = new SkillUseRequest(_self, new DefinitionId(skillId), target, rank);
            CombatActionResult result = _runner.RequestSkill(
                request, BuildContext(), BuildSkillRules(), skillRecovery);

            if (result.IsAccepted && result.Action.HasExecuted) RaiseExecuted(result.Action);
            return result;
        }

        /// <summary>Stops the current action. See the runner for the cancellation policy.</summary>
        public bool CancelAction()
        {
            return _runner != null && _runner.Cancel();
        }

        /// <summary>Restores combat runtime to a known state. Touches no persistent value.</summary>
        public void ResetCombatRuntime()
        {
            if (_runner == null) return;
            CombatRuntimeReset.Restore(_self, _runner, _self.Cooldowns);
        }

        private void RaiseExecuted(CombatAction action)
        {
            var handler = ActionExecuted;
            if (handler != null) handler(action);
        }

        private void RaiseFinished(CombatAction action)
        {
            var handler = ActionFinished;
            if (handler != null) handler(action);
        }
    }
}
