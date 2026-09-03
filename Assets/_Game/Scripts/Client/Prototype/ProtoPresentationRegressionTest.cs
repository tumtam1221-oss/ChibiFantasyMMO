using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Client.Combat;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE regression harness for PHASE 07.5 combat presentation.
    /// </summary>
    /// <remarks>
    /// Test scaffolding. It drives the production driver and presenter on the approved
    /// characters and, most importantly, runs the identical fight twice -- once with the
    /// Animator alive and once with it disabled -- then compares the gameplay figures.
    /// Production presentation does not reference this type.
    /// </remarks>
    public sealed class ProtoPresentationRegressionTest : MonoBehaviour
    {
        [SerializeField] private CombatActionDriver maleDriver;
        [SerializeField] private CombatActionDriver femaleDriver;
        [SerializeField] private CombatantBehaviour dummy;

        public static readonly List<string> Results = new List<string>();
        public static bool Done;

        private const string Cleave = "combat.skill.cleave";   // physical, 1s cast, 2s cd
        private const string Bolt = "combat.skill.bolt";       // magic, 0s cast, 4s cd

        private static void Log(string s) { Results.Add(s); }

        private void Awake()
        {
            Application.runInBackground = true;
            Time.captureFramerate = 60;
        }

        private void OnDestroy() { Time.captureFramerate = 0; }

        private void Start()
        {
            Results.Clear();
            Done = false;
            StartCoroutine(Run());
        }

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private IEnumerator WaitReady(CombatActionDriver d, string skill)
        {
            var id = new ChibiFantasy.Core.DefinitionId(skill);
            for (int i = 0; i < 900 && (!d.IsAvailable || !d.Self.Cooldowns.IsReady(id)); i++)
                yield return null;
            yield return null;
        }

        private IEnumerator Run()
        {
            yield return Frames(5);
            yield return PresentationGate("MALE", maleDriver);
            yield return PresentationGate("FEMALE", femaleDriver);
            yield return IndependenceGate("MALE", maleDriver);
            yield return IndependenceGate("FEMALE", femaleDriver);
            Log("=== PRESENTATION HARNESS COMPLETE ===");
            Done = true;
        }

        // ---------------- events reach presentation exactly once ----------------

        private IEnumerator PresentationGate(string label, CombatActionDriver driver)
        {
            Log("=== " + label + " PRESENTATION ===");

            var presenter = driver.GetComponent<CombatPresenter>();
            if (presenter == null) { Log("  NO PRESENTER"); yield break; }

            var animator = driver.GetComponentInChildren<Animator>();
            driver.SetTarget(dummy);
            driver.ResetCombatRuntime();
            CombatRuntimeReset.Restore(dummy);
            driver.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.2f);
            Physics.SyncTransforms();
            yield return Frames(3);

            Log("  animator=" + (animator != null) + " hasController=" + presenter.HasAnimator
                + " config=" + (presenter != null));

            // ---- basic attack: one execution, one presentation ----
            presenter.ResetCounters();
            dummy.SetStat("stat.def", 0);
            CombatActionResult atk = driver.RequestBasicAttack();
            yield return Frames(40);
            Log("  BASIC ATTACK: started=" + presenter.StartedCount + " executed=" + presenter.ExecutedCount
                + " hit=" + presenter.HitCount + " completed=" + presenter.CompletedCount
                + " lastType=" + presenter.LastEvent.ActionType
                + " (one execution must give exactly one executed + one hit)");

            // ---- many idle frames must add nothing ----
            int executedBefore = presenter.ExecutedCount;
            int hitBefore = presenter.HitCount;
            yield return Frames(120);
            Log("  120 idle frames later: executed=" + presenter.ExecutedCount
                + " hit=" + presenter.HitCount
                + " unchanged=" + (presenter.ExecutedCount == executedBefore && presenter.HitCount == hitBefore));

            // ---- physical skill with a cast: started fires, executed waits ----
            yield return WaitReady(driver, Cleave);
            presenter.ResetCounters();
            dummy.SetStat("stat.def", 10);
            CombatRuntimeReset.Restore(dummy);
            driver.RequestSkill(Cleave);
            yield return Frames(3);
            int executedDuringCast = presenter.ExecutedCount;
            yield return Frames(90);
            Log("  PHYSICAL SKILL: startedAtRequest=" + presenter.StartedCount
                + " executedDuringCast=" + executedDuringCast
                + " executedAfterCast=" + presenter.ExecutedCount
                + " hit=" + presenter.HitCount
                + " hitDamageType=" + presenter.LastHit.DamageType + " hitAmount=" + presenter.LastHit.Amount);

            // ---- magic skill reports magic ----
            yield return WaitReady(driver, Bolt);
            presenter.ResetCounters();
            dummy.SetStat("stat.mdef", 0);
            CombatRuntimeReset.Restore(dummy);
            driver.RequestSkill(Bolt);
            yield return Frames(10);
            Log("  MAGIC SKILL: executed=" + presenter.ExecutedCount + " hit=" + presenter.HitCount
                + " hitDamageType=" + presenter.LastHit.DamageType + " hitAmount=" + presenter.LastHit.Amount);

            // ---- rejection reaches presentation without touching gameplay ----
            presenter.ResetCounters();
            int hpBefore = dummy.CurrentHealth;
            CombatActionResult refused = driver.RequestSkill(Bolt);
            yield return Frames(2);
            Log("  REJECTED: accepted=" + refused.IsAccepted + " presenterRejected=" + presenter.RejectedCount
                + " rejection=" + presenter.LastRejected.Rejection + "/" + presenter.LastRejected.SkillRejection
                + " dummyUnchanged=" + (dummy.CurrentHealth == hpBefore));

            // ---- cancellation reaches presentation ----
            yield return WaitReady(driver, Cleave);
            presenter.ResetCounters();
            CombatRuntimeReset.Restore(dummy);
            int cancelHp = dummy.CurrentHealth;
            driver.RequestSkill(Cleave);
            yield return Frames(20);
            driver.CancelAction();
            yield return Frames(5);
            Log("  CANCELLED: cancelled=" + presenter.CancelledCount
                + " executed=" + presenter.ExecutedCount
                + " dummyUnchanged=" + (dummy.CurrentHealth == cancelHp)
                + " reason=" + presenter.LastEvent.CancelReason);

            // ---- death reaches presentation from gameplay ----
            yield return WaitReady(driver, Bolt);
            presenter.ResetCounters();
            CombatRuntimeReset.Restore(dummy);
            dummy.ApplyHealthDelta(-(dummy.MaxHealth - 5));   // 5 hp left
            dummy.SetStat("stat.mdef", 0);
            driver.RequestSkill(Bolt);
            yield return Frames(10);
            Log("  DEATH: executed=" + presenter.ExecutedCount + " hit=" + presenter.HitCount
                + " death=" + presenter.DeathCount
                + " dummyAlive=" + dummy.IsAlive() + " dummyHP=" + dummy.CurrentHealth
                + " (death follows gameplay, never an animation frame)");

            CombatRuntimeReset.Restore(dummy);
            driver.ResetCombatRuntime();
            yield return Frames(3);
        }

        // ---------------- STEP 17: gameplay must not depend on presentation ----------------

        private IEnumerator IndependenceGate(string label, CombatActionDriver driver)
        {
            Log("=== " + label + " GAMEPLAY INDEPENDENCE ===");

            var presenter = driver.GetComponent<CombatPresenter>();
            var animator = driver.GetComponentInChildren<Animator>();

            // Run the identical fight with presentation alive, then with it stripped.
            int[] withPresentation = null;
            int[] withoutPresentation = null;

            for (int pass = 0; pass < 2; pass++)
            {
                bool enablePresentation = pass == 0;

                if (animator != null) animator.enabled = enablePresentation;
                if (presenter != null) presenter.enabled = enablePresentation;

                yield return Frames(3);

                var scenario = Scenario(driver);
                while (scenario.MoveNext()) yield return scenario.Current;

                if (enablePresentation) withPresentation = _lastFigures;
                else withoutPresentation = _lastFigures;
            }

            if (animator != null) animator.enabled = true;
            if (presenter != null) presenter.enabled = true;
            yield return Frames(2);

            bool identical = withPresentation != null && withoutPresentation != null
                && withPresentation.Length == withoutPresentation.Length;

            if (identical)
            {
                for (int i = 0; i < withPresentation.Length; i++)
                {
                    if (withPresentation[i] != withoutPresentation[i]) identical = false;
                }
            }

            Log("  with presentation:    " + Describe(withPresentation));
            Log("  without presentation: " + Describe(withoutPresentation));
            Log("  IDENTICAL=" + identical
                + "  (animator disabled, presenter disabled -> same HP, MP, cooldown, death, phase)");
        }

        private int[] _lastFigures;

        /// <summary>A fixed fight whose gameplay figures are recorded at the end.</summary>
        private IEnumerator Scenario(CombatActionDriver driver)
        {
            CombatantBehaviour self = driver.Self;

            driver.ResetCombatRuntime();
            CombatRuntimeReset.Restore(dummy);
            dummy.SetStat("stat.def", 15);
            dummy.SetStat("stat.mdef", 25);
            driver.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.2f);
            Physics.SyncTransforms();
            yield return Frames(3);

            driver.RequestBasicAttack();
            yield return Frames(40);

            yield return WaitReady(driver, Cleave);
            driver.RequestSkill(Cleave);          // 1s cast, physical
            yield return Frames(90);

            yield return WaitReady(driver, Bolt);
            driver.RequestSkill(Bolt);            // instant, magic
            yield return Frames(10);

            CombatActionResult blocked = driver.RequestSkill(Bolt);   // on cooldown
            yield return Frames(5);

            _lastFigures = new[]
            {
                dummy.CurrentHealth,
                self.CurrentHealth,
                self.CurrentMana,
                self.Cooldowns.IsReady(new ChibiFantasy.Core.DefinitionId(Bolt)) ? 1 : 0,
                self.Cooldowns.IsReady(new ChibiFantasy.Core.DefinitionId(Cleave)) ? 1 : 0,
                dummy.IsAlive() ? 1 : 0,
                blocked.IsAccepted ? 1 : 0,
                (int)driver.Phase
            };
        }

        private static string Describe(int[] f)
        {
            if (f == null) return "<none>";
            return "dummyHP=" + f[0] + " selfHP=" + f[1] + " selfMP=" + f[2]
                + " boltReady=" + f[3] + " cleaveReady=" + f[4]
                + " dummyAlive=" + f[5] + " repeatAccepted=" + f[6] + " phase=" + f[7];
        }
    }
}
