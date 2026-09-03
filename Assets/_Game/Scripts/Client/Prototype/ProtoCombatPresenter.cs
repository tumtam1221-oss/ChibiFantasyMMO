using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE presentation for combat, for PHASE 07.2.
    /// </summary>
    /// <remarks>
    /// <b>Strictly downstream.</b> It is told what happened and reflects it on an Animator.
    /// It never validates, never calculates and never applies anything, and combat never
    /// reads it back. Delete this component and every fight resolves identically -- which
    /// is the property that keeps damage out of animation events.
    ///
    /// <b>Every Animator access is guarded.</b> A missing Animator, a missing controller or
    /// a missing parameter must not throw or stall a fight, because there is no attack clip
    /// authored yet and this has to work without one. Parameters are checked for existence
    /// once rather than assumed.
    ///
    /// The Animator is driven by combat <em>state</em>, not by a clip's length: an int
    /// parameter follows <see cref="AttackPhase"/> and a trigger fires on the swing. That
    /// way the placeholder proves the path works and a real animation can be dropped in
    /// later without changing any of this.
    /// </remarks>
    public sealed class ProtoCombatPresenter : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        private static readonly int AttackTriggerHash = Animator.StringToHash("Attack");
        private static readonly int AttackPhaseHash = Animator.StringToHash("AttackPhase");

        private bool _hasTrigger;
        private bool _hasPhase;
        private bool _checked;
        private AttackPhase _lastPhase = AttackPhase.Idle;

        /// <summary>Last phase pushed to the Animator. For inspection and tests.</summary>
        public AttackPhase LastReportedPhase => _lastPhase;

        /// <summary>How many times a swing was signalled. For inspection and tests.</summary>
        public int AttackSignalCount { get; private set; }

        public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

        public void SetAnimator(Animator value)
        {
            animator = value;
            _checked = false;
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void EnsureParameterInfo()
        {
            if (_checked) return;
            _checked = true;
            _hasTrigger = false;
            _hasPhase = false;

            if (!HasAnimator) return;

            var parameters = animator.parameters;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == AttackTriggerHash
                    && parameters[i].type == AnimatorControllerParameterType.Trigger)
                {
                    _hasTrigger = true;
                }

                if (parameters[i].nameHash == AttackPhaseHash
                    && parameters[i].type == AnimatorControllerParameterType.Int)
                {
                    _hasPhase = true;
                }
            }
        }

        /// <summary>Called when a swing begins. Presentation only; the blow already landed.</summary>
        public void OnAttackStarted(AttackResult result)
        {
            AttackSignalCount++;

            EnsureParameterInfo();

            if (_hasTrigger && animator.isActiveAndEnabled)
            {
                animator.SetTrigger(AttackTriggerHash);
            }
        }

        /// <summary>
        /// Called after a skill resolved. Presentation only.
        /// </summary>
        /// <remarks>Reuses the same Animator trigger as a basic attack, because no skill
        /// animation is authored and inventing one would be a production animation task.
        /// The skill's own <c>SkillDefinition.Animation</c> address is where a real clip
        /// will come from; nothing here loads it.</remarks>
        public void OnSkillExecuted(SkillExecutionResult result)
        {
            SkillSignalCount++;
            LastSkillResult = result;

            EnsureParameterInfo();

            if (_hasTrigger && animator.isActiveAndEnabled)
            {
                animator.SetTrigger(AttackTriggerHash);
            }
        }

        /// <summary>How many times a skill was signalled. For inspection and tests.</summary>
        public int SkillSignalCount { get; private set; }

        /// <summary>The last skill outcome presentation was told about.</summary>
        public SkillExecutionResult LastSkillResult { get; private set; }

        /// <summary>Mirrors the current combat phase onto the Animator.</summary>
        public void Report(AttackPhase phase)
        {
            _lastPhase = phase;

            EnsureParameterInfo();

            if (_hasPhase && animator.isActiveAndEnabled)
            {
                animator.SetInteger(AttackPhaseHash, (int)phase);
            }
        }
    }
}
