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

        /// <summary>
        /// Facts about combat, for presentation.
        /// </summary>
        /// <remarks>
        /// One typed stream rather than the two loose callbacks PHASE 07.4 left here,
        /// because two overlapping event surfaces is exactly the duplicate presenter
        /// wiring this phase is meant to avoid. Nothing consumed the old pair.
        ///
        /// Raised after the fact and never awaited. A listener that throws, or the absence
        /// of any listener at all, cannot change a gameplay outcome.
        /// </remarks>
        public event System.Action<CombatPresentationEvent> Presentation;

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

            _runner.Advance(dt);
            PublishTransitions();
        }

        /// <summary>
        /// Emits whatever changed since the last look.
        /// </summary>
        /// <remarks>
        /// Idempotency comes from the action's own identity and its
        /// <see cref="CombatAction.HasExecuted"/> flag rather than from a second combat
        /// state machine: an execution is announced on the tick it first becomes true, and
        /// an ending is announced once per action object. Repeated Update calls on a
        /// finished action therefore emit nothing.
        /// </remarks>
        private void PublishTransitions()
        {
            CombatAction action = _runner.Current;
            if (action == null) return;

            if (!ReferenceEquals(action, _watched))
            {
                _watched = action;
                _announcedExecution = false;
                _announcedEnding = false;
            }

            if (!_announcedExecution && action.HasExecuted)
            {
                _announcedExecution = true;
                PublishExecution(action);
            }

            if (_announcedEnding || !action.IsFinished) return;

            _announcedEnding = true;

            Raise(action.Phase == CombatActionPhase.Cancelled
                ? CombatPresentationEvent.Cancelled(action)
                : CombatPresentationEvent.Completed(action));
        }

        /// <summary>Announces one execution as executed, then hit, then death where they apply.</summary>
        private void PublishExecution(CombatAction action)
        {
            CombatPresentationEvent executed = CombatPresentationEvent.Executed(action);
            Raise(executed);

            // A hit only if health actually moved: a skill whose every effect was
            // unsupported resolved without striking anybody.
            if (executed.HealthChange != 0)
            {
                Raise(CombatPresentationEvent.Hit(executed, executed.DamageType));
            }

            // Death is announced because gameplay says the target died, never because an
            // animation reached a frame.
            if (executed.TargetDied) Raise(CombatPresentationEvent.Death(executed));
        }

        /// <summary>Requests a basic attack against the current target.</summary>
        public CombatActionResult RequestBasicAttack()
        {
            if (_runner == null) return CombatActionResult.Rejected(CombatActionRejection.NoActor);

            CombatActionResult result = _runner.RequestBasicAttack(
                target, BuildAttackRules(), basicAttackWindUp, basicAttackRecovery);

            AnnounceRequest(CombatActionType.BasicAttack, DefinitionId.None, result);
            return result;
        }

        /// <summary>Requests a skill by content id against the current target.</summary>
        public CombatActionResult RequestSkill(string skillId, int rank = 1)
        {
            if (_runner == null) return CombatActionResult.Rejected(CombatActionRejection.NoActor);

            var skill = new DefinitionId(skillId);
            var request = new SkillUseRequest(_self, skill, target, rank);
            CombatActionResult result = _runner.RequestSkill(
                request, BuildContext(), BuildSkillRules(), skillRecovery);

            AnnounceRequest(CombatActionType.Skill, skill, result);
            return result;
        }

        /// <summary>
        /// Announces the outcome of a request.
        /// </summary>
        /// <remarks>A zero-cast action has already executed by the time the request
        /// returns, so its execution is published here rather than waiting for the next
        /// tick; <see cref="PublishTransitions"/> would otherwise announce it a frame late,
        /// and the guard flags stop it announcing twice.</remarks>
        private void AnnounceRequest(CombatActionType type, DefinitionId skill,
            in CombatActionResult result)
        {
            if (!result.IsAccepted)
            {
                Raise(CombatPresentationEvent.Rejected(type, _self.CombatantId,
                    target == null ? default : target.CombatantId, skill, result));
                return;
            }

            _watched = result.Action;
            _announcedExecution = false;
            _announcedEnding = false;

            Raise(CombatPresentationEvent.Started(result.Action));
            PublishTransitions();
        }

        /// <summary>Stops the current action. See the runner for the cancellation policy.</summary>
        public bool CancelAction()
        {
            if (_runner == null || !_runner.Cancel()) return false;

            PublishTransitions();
            return true;
        }

        /// <summary>Restores combat runtime to a known state. Touches no persistent value.</summary>
        public void ResetCombatRuntime()
        {
            if (_runner == null) return;

            CombatAction cancelled = _runner.Current;
            bool wasBusy = cancelled != null && cancelled.IsBusy;

            CombatRuntimeReset.Restore(_self, _runner, _self.Cooldowns);

            // The reset clears Current, so the cancellation is announced from the action
            // captured beforehand rather than being lost.
            if (wasBusy && !_announcedEnding)
            {
                _announcedEnding = true;
                Raise(CombatPresentationEvent.Cancelled(cancelled));
            }

            _watched = null;
        }

        private void Raise(CombatPresentationEvent e)
        {
            var handler = Presentation;
            if (handler != null) handler(e);
        }

        private CombatAction _watched;
        private bool _announcedExecution;
        private bool _announcedEnding;
    }
}
