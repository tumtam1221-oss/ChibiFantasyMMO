using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE bridge from input to the combat rules, for PHASE 07.2.
    /// </summary>
    /// <remarks>
    /// <b>It decides nothing.</b> Validation, damage and application all live in the pure
    /// Gameplay types; this reads input, builds an <see cref="AttackIntent"/>, advances the
    /// <see cref="AttackStateMachine"/> with the frame delta and hands the resulting
    /// <see cref="AttackResult"/> to presentation. Moving a formula in here would put
    /// balance inside a MonoBehaviour, where a server can never reuse it.
    ///
    /// <b>Damage lands the moment the swing starts</b>, not when an animation says so.
    /// Presentation is told afterwards and cannot veto or delay it, which is what keeps
    /// combat correct with no Animator, no clip and no model present at all.
    ///
    /// One component serves every character. There is no male or female variant: the
    /// difference between two fighters is their <see cref="ProtoCombatSettings"/> and their
    /// stats, which is data.
    /// </remarks>
    [RequireComponent(typeof(ProtoCombatant))]
    public sealed class ProtoCombatController : MonoBehaviour
    {
        [SerializeField] private ProtoCombatSettings settings;
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private ProtoCombatPresenter presenter;
        [SerializeField] private ProtoCombatant target;

        private ProtoCombatant _self;
        private AttackStateMachine _attackState;

        /// <summary>The most recent outcome, for inspection and tests.</summary>
        public AttackResult LastResult { get; private set; }

        public AttackPhase Phase => _attackState == null ? AttackPhase.Idle : _attackState.Phase;

        public bool CanAttack => _attackState != null && _attackState.CanAttack;

        public ProtoCombatant Self => _self;

        public void SetTarget(ProtoCombatant value) { target = value; }
        public void SetInput(ProtoPlayerInput value) { input = value; }
        public void SetSettings(ProtoCombatSettings value) { settings = value; RebuildTiming(); }
        public void SetPresenter(ProtoCombatPresenter value) { presenter = value; }

        private void Awake()
        {
            _self = GetComponent<ProtoCombatant>();
            RebuildTiming();
        }

        private void RebuildTiming()
        {
            var timing = settings == null
                ? new AttackTiming(0.4f, 0.35f)
                : new AttackTiming(settings.attackDuration, settings.recoveryDuration);

            if (_attackState == null)
            {
                _attackState = new AttackStateMachine(timing);
            }
            else
            {
                _attackState.SetTiming(timing);
            }
        }

        /// <summary>Builds the rules from settings. Nothing numeric is hard-coded here.</summary>
        public BasicAttackRules BuildRules()
        {
            if (settings == null)
            {
                return BasicAttackRules.Melee(DefinitionId.None, DefinitionId.None, 1, 1.4f);
            }

            return BasicAttackRules.Melee(
                new DefinitionId(settings.attackPowerStatId),
                new DefinitionId(settings.defenseStatId),
                settings.minimumDamage,
                settings.attackRange);
        }

        private void Update()
        {
            if (_attackState == null) return;

            _attackState.Advance(Time.deltaTime);

            if (input != null && input.IsReady && input.AttackPressed)
            {
                TryAttack();
            }

            if (presenter != null)
            {
                presenter.Report(_attackState.Phase);
            }
        }

        /// <summary>
        /// Attempts one basic attack against the current target.
        /// </summary>
        /// <remarks>
        /// The state machine is asked first and only advanced when it agrees, so a
        /// rejected attack costs no swing. When it agrees but the rules then refuse -- out
        /// of range, target dead -- the swing has already begun, which is deliberate: a
        /// missed swing should still occupy the attacker.
        /// </remarks>
        public AttackResult TryAttack()
        {
            if (_attackState == null || _self == null)
            {
                LastResult = AttackResult.Rejected(AttackRejection.NoAttacker, default, default);
                return LastResult;
            }

            if (!_attackState.CanAttack)
            {
                LastResult = AttackResult.Rejected(
                    AttackRejection.NotReady, _self.CombatantId,
                    target == null ? default : target.CombatantId);
                return LastResult;
            }

            _attackState.TryBeginAttack();

            LastResult = BasicAttackExecutor.Execute(
                new AttackIntent(_self, target), BuildRules());

            if (presenter != null)
            {
                presenter.OnAttackStarted(LastResult);
            }

            return LastResult;
        }

        /// <summary>Clears attack pacing. Used on death and when swapping characters.</summary>
        public void ResetCombat()
        {
            if (_attackState != null) _attackState.Reset();
            if (presenter != null) presenter.Report(AttackPhase.Idle);
        }
    }
}
