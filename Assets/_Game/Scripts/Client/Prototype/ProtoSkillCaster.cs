using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE bridge from input to the skill rules, for PHASE 07.3.
    /// </summary>
    /// <remarks>
    /// <b>It decides nothing.</b> Availability, targeting, damage, healing, cost and
    /// cooldown are all decided by <see cref="SkillUseValidator"/> and
    /// <see cref="SkillExecutor"/> in the pure Gameplay assembly. This reads input, builds
    /// a <see cref="SkillUseRequest"/>, advances the runtime cooldowns with the frame
    /// delta and hands the <see cref="SkillExecutionResult"/> to presentation. It never
    /// touches health, mana or learned skills directly.
    ///
    /// The shape deliberately matches <see cref="ProtoCombatController"/>, which is the
    /// same bridge for basic attacks. Two entry points, one architecture.
    ///
    /// No hotbar, no icons, no cast bar, no VFX and no SFX. A key is pressed and a skill
    /// resolves; everything else is a later phase.
    /// </remarks>
    [RequireComponent(typeof(ProtoCombatant))]
    public sealed class ProtoSkillCaster : MonoBehaviour
    {
        [Header("Content - PROTOTYPE")]
        [Tooltip("Skill definitions this caster can use. Registered into a lookup at start.")]
        [SerializeField] private SkillDefinition[] skills;

        [Tooltip("Skill used by the first prototype key.")]
        [SerializeField] private string primarySkillId;

        [Tooltip("Skill used by the second prototype key.")]
        [SerializeField] private string secondarySkillId;

        [Header("Combat - PROTOTYPE")]
        [Tooltip("Id of the stat the target resists skill damage with.")]
        [SerializeField] private string defenseStatId = "stat.vit";

        [SerializeField] private int minimumDamage;

        [Tooltip("Character level used for the skill's per-rank level requirement.")]
        [SerializeField] private int casterLevel = 10;

        [Header("Wiring - PROTOTYPE")]
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private ProtoCombatant target;
        [SerializeField] private ProtoCombatPresenter presenter;

        private ProtoCombatant _self;
        private DefinitionRegistry<SkillDefinition> _registry;

        /// <summary>The most recent outcome, for inspection and tests.</summary>
        public SkillExecutionResult LastResult { get; private set; }

        public ProtoCombatant Self => _self;

        public void SetTarget(ProtoCombatant value) { target = value; }
        public void SetInput(ProtoPlayerInput value) { input = value; }
        public void SetCasterLevel(int value) { casterLevel = value; }

        private void Awake()
        {
            _self = GetComponent<ProtoCombatant>();
            if (presenter == null) presenter = GetComponent<ProtoCombatPresenter>();
            BuildRegistry();
        }

        private void BuildRegistry()
        {
            _registry = new DefinitionRegistry<SkillDefinition>();

            if (skills == null) return;

            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] == null) continue;

                _registry.Register(skills[i]);

                // The prototype teaches everything it is given, through the real Phase 06
                // state. Acquisition rules are 06.6's job and are not restated here.
                _self.LearnSkill(skills[i].Id.Value);
            }
        }

        /// <summary>Everything the validator needs that is not in the request.</summary>
        public SkillUseContext BuildContext()
        {
            return new SkillUseContext(_registry, _self.LearnedSkills, casterLevel, _self.Cooldowns);
        }

        public SkillExecutionRules BuildRules()
        {
            return new SkillExecutionRules(new DefinitionId(defenseStatId), minimumDamage);
        }

        private void Update()
        {
            // Cooldowns are runtime state and advance with the frame, not with a clock the
            // rules can see.
            _self.Cooldowns.Advance(Time.deltaTime);

            if (input == null || !input.IsReady) return;

            if (input.SkillPrimaryPressed) Cast(primarySkillId);
            if (input.SkillSecondaryPressed) Cast(secondarySkillId);
        }

        /// <summary>
        /// Attempts one skill use.
        /// </summary>
        /// <remarks>The target is passed as requested; a self skill is redirected to the
        /// caster by <see cref="SkillTargetMapping"/>, not here, so this bridge cannot
        /// disagree with the rules about who is hit.</remarks>
        public SkillExecutionResult Cast(string skillId)
        {
            if (_self == null || string.IsNullOrWhiteSpace(skillId))
            {
                LastResult = SkillExecutionResult.Rejected(
                    SkillUseRejection.NoSkill, DefinitionId.None, default, default);
                return LastResult;
            }

            var request = new SkillUseRequest(
                _self, new DefinitionId(skillId), target, 1);

            LastResult = SkillExecutor.Execute(request, BuildContext(), BuildRules());

            if (presenter != null && LastResult.IsExecuted)
            {
                // Presentation is told after the fact and can neither veto nor delay it.
                presenter.OnSkillExecuted(LastResult);
            }

            return LastResult;
        }

        /// <summary>Clears runtime cooldowns. For death and character swaps.</summary>
        public void ResetSkills()
        {
            if (_self != null) _self.Cooldowns.Reset();
        }
    }
}
