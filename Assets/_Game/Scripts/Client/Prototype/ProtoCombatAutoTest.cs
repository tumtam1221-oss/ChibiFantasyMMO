using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE automated combat gate harness for PHASE 07.2.
    /// </summary>
    /// <remarks>
    /// Exercises the real components in a running scene so the male, female and shared
    /// gates have measured evidence rather than assertions. Test-only; it is not part of
    /// the combat architecture and is disabled by default in the scene.
    /// </remarks>
    public sealed class ProtoCombatAutoTest : MonoBehaviour
    {
        [SerializeField] private ProtoPlayerInput input;
        [SerializeField] private ProtoCombatant dummy;
        [SerializeField] private ProtoCombatController maleCombat;
        [SerializeField] private ProtoCombatController femaleCombat;

        public static readonly List<string> Results = new List<string>();
        public static bool Done;

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
            InputSettings.BackgroundBehavior bg = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.backgroundBehavior = bg;
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

        /// <summary>Waits until the attacker can swing again, with a guard against sticking.</summary>
        private IEnumerator WaitReady(ProtoCombatController c)
        {
            for (int i = 0; i < 600 && !c.CanAttack; i++) yield return null;
            yield return null;
        }

        private IEnumerator Run()
        {
            yield return Frames(5);

            yield return CharacterGate("MALE", maleCombat);
            yield return CharacterGate("FEMALE", femaleCombat);
            yield return SharedGate();

            Log("=== COMBAT HARNESS COMPLETE ===");
            Done = true;
        }

        private IEnumerator CharacterGate(string label, ProtoCombatController combat)
        {
            Log("=== " + label + " COMBAT ===");

            if (combat == null || dummy == null) { Log("  MISSING REFERENCES"); yield break; }

            ProtoCombatant self = combat.Self;
            Transform attacker = combat.transform;
            Transform victim = dummy.transform;

            dummy.ResetToFull();
            self.ResetToFull();
            combat.ResetCombat();
            yield return Frames(5);

            // ---- in range: attack must damage the target, not the attacker ----
            attacker.position = victim.position - new Vector3(0f, 0f, 1.0f);
            Physics.SyncTransforms();
            yield return Frames(3);

            int dummyBefore = dummy.CurrentHealth;
            int selfBefore = self.CurrentHealth;
            AttackResult r1 = combat.TryAttack();
            yield return Frames(2);

            Log("  attack in range: hit=" + r1.IsHit + " reason=" + r1.Reason
                + " damage=" + r1.Damage
                + " targetHP " + r1.TargetHealthBefore + "->" + r1.TargetHealthAfter
                + " attackerHP " + selfBefore + "->" + self.CurrentHealth
                + " died=" + r1.TargetDied);

            // ---- spam is refused while attacking / recovering ----
            int accepted = 0;
            for (int i = 0; i < 200; i++)
            {
                if (combat.TryAttack().IsHit) accepted++;
            }
            Log("  200 immediate retries accepted=" + accepted + " phase=" + combat.Phase
                + " (state machine paces the swing)");

            // ---- recovery returns to idle ----
            yield return WaitReady(combat);
            Log("  after recovery: phase=" + combat.Phase + " canAttack=" + combat.CanAttack);

            // ---- second attack lands ----
            int beforeSecond = dummy.CurrentHealth;
            AttackResult r2 = combat.TryAttack();
            yield return Frames(2);
            Log("  repeat attack: hit=" + r2.IsHit + " damage=" + r2.Damage
                + " dummyHP " + beforeSecond + "->" + dummy.CurrentHealth);

            // ---- out of range is refused ----
            yield return WaitReady(combat);
            attacker.position = victim.position - new Vector3(0f, 0f, 25f);
            Physics.SyncTransforms();
            yield return Frames(3);
            int beforeFar = dummy.CurrentHealth;
            AttackResult r3 = combat.TryAttack();
            yield return Frames(2);
            Log("  attack out of range: hit=" + r3.IsHit + " reason=" + r3.Reason
                + " dummyHP unchanged=" + (dummy.CurrentHealth == beforeFar));

            // ---- input-driven attack proves the input path, not just the API ----
            yield return WaitReady(combat);
            attacker.position = victim.position - new Vector3(0f, 0f, 1.0f);
            Physics.SyncTransforms();
            yield return Frames(3);
            int beforeInput = dummy.CurrentHealth;
            InputSystem.QueueStateEvent(_kb, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(3);
            Log("  attack via Input System (Space): dummyHP " + beforeInput + "->" + dummy.CurrentHealth
                + " damaged=" + (dummy.CurrentHealth < beforeInput));

            // ---- kill the target, then confirm a corpse is not attackable ----
            int guard = 0;
            while (dummy.CurrentHealth > 0 && guard++ < 400)
            {
                yield return WaitReady(combat);
                combat.TryAttack();
                yield return null;
            }
            Log("  target killed: dummyHP=" + dummy.CurrentHealth + " swings=" + guard);

            yield return WaitReady(combat);
            AttackResult onCorpse = combat.TryAttack();
            Log("  attack on dead target: hit=" + onCorpse.IsHit + " reason=" + onCorpse.Reason);

            // ---- animation is presentation only ----
            var presenter = combat.GetComponent<ProtoCombatPresenter>();
            var anim = combat.GetComponentInChildren<Animator>();
            Log("  presenter: hasAnimator=" + (presenter != null && presenter.HasAnimator)
                + " signals=" + (presenter == null ? -1 : presenter.AttackSignalCount)
                + " lastPhase=" + (presenter == null ? AttackPhase.Idle : presenter.LastReportedPhase)
                + " animatorAttackPhaseParam="
                + (anim != null && anim.runtimeAnimatorController != null ? anim.GetInteger("AttackPhase").ToString() : "n/a"));

            // ---- combat still works with no animator at all ----
            dummy.ResetToFull();
            yield return WaitReady(combat);
            bool animWasEnabled = anim != null && anim.enabled;
            if (anim != null) anim.enabled = false;
            yield return Frames(3);
            AttackResult noAnim = combat.TryAttack();
            yield return Frames(2);
            Log("  attack with Animator DISABLED: hit=" + noAnim.IsHit + " damage=" + noAnim.Damage
                + " (combat does not depend on animation)");
            if (anim != null) anim.enabled = animWasEnabled;

            // ---- movement still works after combat ----
            var move = combat.GetComponent<ProtoThirdPersonController>();
            Vector3 p0 = attacker.position;
            yield return Frames(30);
            Log("  controller intact after combat: movementComponent=" + (move != null)
                + " grounded=" + (move != null && move.IsGrounded)
                + " idleDrift=" + (attacker.position - p0).magnitude.ToString("F5") + "m");

            dummy.ResetToFull();
            combat.ResetCombat();
            yield return Frames(3);
        }

        private IEnumerator SharedGate()
        {
            Log("=== SHARED COMBAT ARCHITECTURE ===");

            var controllers = Object.FindObjectsByType<ProtoCombatController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var combatants = Object.FindObjectsByType<ProtoCombatant>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Log("  ProtoCombatController instances=" + controllers.Length
                + " ProtoCombatant instances=" + combatants.Length
                + " (one shared controller type; no Male/Female combat class)");

            // Identical stats must produce identical damage through the same path.
            dummy.ResetToFull();
            maleCombat.Self.SetStat("stat.str", 20);
            femaleCombat.Self.SetStat("stat.str", 20);
            maleCombat.ResetCombat();
            femaleCombat.ResetCombat();

            maleCombat.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.0f);
            femaleCombat.transform.position = dummy.transform.position - new Vector3(1.0f, 0f, 0f);
            Physics.SyncTransforms();
            yield return Frames(3);

            yield return WaitReady(maleCombat);
            AttackResult m = maleCombat.TryAttack();
            yield return Frames(2);
            dummy.ResetToFull();
            yield return WaitReady(femaleCombat);
            AttackResult f = femaleCombat.TryAttack();
            yield return Frames(2);

            Log("  equal stats -> equal damage: male=" + m.Damage + " female=" + f.Damage
                + " identical=" + (m.Damage == f.Damage));
            Log("  both hit=" + (m.IsHit && f.IsHit));
            dummy.ResetToFull();
        }
    }
}
