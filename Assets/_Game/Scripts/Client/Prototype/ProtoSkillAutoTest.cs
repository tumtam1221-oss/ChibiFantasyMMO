using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE automated skill gate harness for PHASE 07.3.
    /// </summary>
    /// <remarks>
    /// Exercises the real components in a running scene so the male, female and
    /// attack-plus-skill gates have measured evidence. Test-only, disabled by default.
    /// </remarks>
    public sealed class ProtoSkillAutoTest : MonoBehaviour
    {
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private ProtoCombatant dummy;
        [SerializeField] private ProtoSkillCaster maleSkills;
        [SerializeField] private ProtoSkillCaster femaleSkills;
        [SerializeField] private ProtoCombatController maleCombat;
        [SerializeField] private ProtoCombatController femaleCombat;

        public static readonly List<string> Results = new List<string>();
        public static bool Done;

        private const string Strike = "proto.skill.strike";
        private const string Mend = "proto.skill.mend";

        private Keyboard _kb;
        private InputSettings.EditorInputBehaviorInPlayMode _prevEditor;
        private InputSettings.BackgroundBehavior _prevBackground;
        private bool _overridden;

        private static void Log(string s) { Results.Add(s); }

        private void Awake()
        {
            Application.runInBackground = true;
            _prevEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            _prevBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _overridden = true;
            Time.captureFramerate = 60;
        }

        private void OnDestroy()
        {
            if (_kb != null && _kb.added) InputSystem.RemoveDevice(_kb);

            if (_overridden)
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditor;
                InputSystem.settings.backgroundBehavior = _prevBackground;
                _overridden = false;
            }
            Time.captureFramerate = 0;
        }

        private void Start()
        {
            Results.Clear();
            Done = false;
            _kb = InputSystem.AddDevice<Keyboard>();
            StartCoroutine(Run());
        }

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Waits out a skill cooldown, with a guard against sticking.</summary>
        private IEnumerator WaitSkillReady(ProtoSkillCaster caster, string skillId)
        {
            var id = new ChibiFantasy.Core.DefinitionId(skillId);

            for (int i = 0; i < 900 && !caster.Self.Cooldowns.IsReady(id); i++) yield return null;
            yield return null;
        }

        private IEnumerator WaitAttackReady(ProtoCombatController c)
        {
            for (int i = 0; i < 600 && !c.CanAttack; i++) yield return null;
            yield return null;
        }

        private IEnumerator Run()
        {
            yield return Frames(5);
            yield return SkillGate("MALE", maleSkills, maleCombat);
            yield return SkillGate("FEMALE", femaleSkills, femaleCombat);
            yield return CrossRegression();
            Log("=== SKILL HARNESS COMPLETE ===");
            Done = true;
        }

        private IEnumerator SkillGate(string label, ProtoSkillCaster caster, ProtoCombatController combat)
        {
            Log("=== " + label + " SKILLS ===");

            if (caster == null || dummy == null) { Log("  MISSING REFERENCES"); yield break; }

            ProtoCombatant self = caster.Self;
            dummy.ResetToFull();
            self.ResetToFull();
            self.ResetManaToFull();
            caster.ResetSkills();
            caster.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.2f);
            Physics.SyncTransforms();
            yield return Frames(3);

            self.TryGetResource(SkillResourceType.Mana, out int mana0, out int manaMax);
            Log("  learned=" + self.LearnedSkills.Count + " mana=" + mana0 + "/" + manaMax
                + " hp=" + self.CurrentHealth + " dummyHP=" + dummy.CurrentHealth);

            // ---- damage skill ----
            int dummyBefore = dummy.CurrentHealth;
            SkillExecutionResult dmg = caster.Cast(Strike);
            yield return Frames(2);
            self.TryGetResource(SkillResourceType.Mana, out int manaAfter, out _);
            Log("  DAMAGE skill: executed=" + dmg.IsExecuted + " reason=" + dmg.Reason
                + " effects=" + dmg.Effects.Count
                + " amount=" + (dmg.Effects.Count > 0 ? dmg.Effects[0].Amount : -1)
                + " dummyHP " + dummyBefore + "->" + dummy.CurrentHealth
                + " manaSpent=" + dmg.ResourceSpent + " mana " + mana0 + "->" + manaAfter
                + " casterHP=" + self.CurrentHealth);

            // ---- cooldown blocks the immediate repeat ----
            SkillExecutionResult blocked = caster.Cast(Strike);
            Log("  immediate repeat: executed=" + blocked.IsExecuted + " reason=" + blocked.Reason);

            // ---- heal skill (self) ----
            yield return WaitSkillReady(caster, Mend);
            self.ApplyHealthDelta(-60);
            int hpBefore = self.CurrentHealth;
            SkillExecutionResult heal = caster.Cast(Mend);
            yield return Frames(2);
            Log("  HEAL skill: executed=" + heal.IsExecuted + " reason=" + heal.Reason
                + " amount=" + (heal.Effects.Count > 0 ? heal.Effects[0].Amount : -1)
                + " casterHP " + hpBefore + "->" + self.CurrentHealth
                + " target=" + (heal.TargetId.Value == self.CombatantId.Value ? "self" : "OTHER"));

            // ---- overheal clamps ----
            yield return WaitSkillReady(caster, Mend);
            SkillExecutionResult over = caster.Cast(Mend);
            yield return Frames(2);
            Log("  HEAL overheal: casterHP=" + self.CurrentHealth + "/" + self.MaxHealth
                + " clamped=" + (self.CurrentHealth <= self.MaxHealth));

            // ---- invalid skill id ----
            SkillExecutionResult bad = caster.Cast("proto.skill.does_not_exist");
            Log("  INVALID skill: executed=" + bad.IsExecuted + " reason=" + bad.Reason);

            // ---- invalid target: aim the enemy skill at a friendly ----
            ProtoSkillCaster other = caster == maleSkills ? femaleSkills : maleSkills;
            if (other != null)
            {
                yield return WaitSkillReady(caster, Strike);
                int allyHpBefore = other.Self.CurrentHealth;
                caster.SetTarget(other.Self);
                SkillExecutionResult wrong = caster.Cast(Strike);
                Log("  INVALID target (ally): executed=" + wrong.IsExecuted + " reason=" + wrong.Reason
                    + " allyHPUnchanged=" + (other.Self.CurrentHealth == allyHpBefore));
                caster.SetTarget(dummy);
            }

            // ---- out of range ----
            yield return WaitSkillReady(caster, Strike);
            caster.transform.position = dummy.transform.position - new Vector3(0f, 0f, 40f);
            Physics.SyncTransforms();
            yield return Frames(3);
            int farBefore = dummy.CurrentHealth;
            SkillExecutionResult far = caster.Cast(Strike);
            Log("  OUT OF RANGE: executed=" + far.IsExecuted + " reason=" + far.Reason
                + " dummyUnchanged=" + (dummy.CurrentHealth == farBefore));
            caster.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.2f);
            Physics.SyncTransforms();
            yield return Frames(3);

            // ---- insufficient mana ----
            yield return WaitSkillReady(caster, Strike);
            self.TryApplyResourceDelta(SkillResourceType.Mana, -999);
            SkillExecutionResult broke = caster.Cast(Strike);
            Log("  NO MANA: executed=" + broke.IsExecuted + " reason=" + broke.Reason);
            self.ResetManaToFull();

            // ---- input-driven cast proves the input path ----
            yield return WaitSkillReady(caster, Strike);
            int inputBefore = dummy.CurrentHealth;
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Digit1));
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(3);
            Log("  cast via Input System (key 1): dummyHP " + inputBefore + "->" + dummy.CurrentHealth
                + " damaged=" + (dummy.CurrentHealth < inputBefore));

            // ---- movement and attack state survive ----
            var move = caster.GetComponent<ProtoThirdPersonController>();
            Vector3 p0 = caster.transform.position;
            yield return Frames(30);
            Log("  after skills: movement=" + (move != null) + " grounded=" + (move != null && move.IsGrounded)
                + " drift=" + (caster.transform.position - p0).magnitude.ToString("F5") + "m"
                + " attackPhase=" + (combat != null ? combat.Phase.ToString() : "n/a")
                + " canAttack=" + (combat != null && combat.CanAttack));

            dummy.ResetToFull();
            self.ResetToFull();
            self.ResetManaToFull();
            caster.ResetSkills();
            if (combat != null) combat.ResetCombat();
            yield return Frames(3);
        }

        /// <summary>Basic attack and skills in the same session, both orders. (STEP 14)</summary>
        private IEnumerator CrossRegression()
        {
            Log("=== ATTACK + SKILL CROSS REGRESSION ===");

            ProtoCombatant self = maleSkills.Self;
            dummy.ResetToFull();
            self.ResetToFull();
            self.ResetManaToFull();
            maleSkills.ResetSkills();
            maleCombat.ResetCombat();
            maleSkills.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.0f);
            Physics.SyncTransforms();
            yield return Frames(3);

            // attack -> skill
            yield return WaitAttackReady(maleCombat);
            AttackResult a1 = maleCombat.TryAttack();
            yield return Frames(2);
            yield return WaitSkillReady(maleSkills, Strike);
            SkillExecutionResult s1 = maleSkills.Cast(Strike);
            yield return Frames(2);
            Log("  attack -> skill: attackHit=" + a1.IsHit + " dmg=" + a1.Damage
                + " | skillExecuted=" + s1.IsExecuted + " dmg=" + (s1.Effects.Count > 0 ? s1.Effects[0].Amount : -1)
                + " | dummyHP=" + dummy.CurrentHealth);

            // skill -> attack
            yield return WaitSkillReady(maleSkills, Strike);
            SkillExecutionResult s2 = maleSkills.Cast(Strike);
            yield return Frames(2);
            yield return WaitAttackReady(maleCombat);
            AttackResult a2 = maleCombat.TryAttack();
            yield return Frames(2);
            Log("  skill -> attack: skillExecuted=" + s2.IsExecuted
                + " | attackHit=" + a2.IsHit + " dmg=" + a2.Damage
                + " | dummyHP=" + dummy.CurrentHealth);

            var anim = maleSkills.GetComponentInChildren<Animator>();
            var presenter = maleSkills.GetComponent<ProtoCombatPresenter>();
            Log("  state intact: attackPhase=" + maleCombat.Phase
                + " canAttack=" + maleCombat.CanAttack
                + " casterHP=" + self.CurrentHealth + "/" + self.MaxHealth
                + " revision=" + self.Resources.Revision
                + " animatorAlive=" + (anim != null && anim.isActiveAndEnabled)
                + " skillSignals=" + (presenter != null ? presenter.SkillSignalCount : -1));

            // combat still resolves with no animator at all
            if (anim != null) anim.enabled = false;
            yield return WaitSkillReady(maleSkills, Strike);
            SkillExecutionResult noAnim = maleSkills.Cast(Strike);
            yield return Frames(2);
            Log("  skill with Animator DISABLED: executed=" + noAnim.IsExecuted
                + " dmg=" + (noAnim.Effects.Count > 0 ? noAnim.Effects[0].Amount : -1)
                + " (rules do not depend on presentation)");
            if (anim != null) anim.enabled = true;

            dummy.ResetToFull();
            self.ResetToFull();
            self.ResetManaToFull();
            yield return Frames(3);
        }
    }
}
