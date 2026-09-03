using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Client.Combat;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE regression harness for the PHASE 07.4 production combat runtime.
    /// </summary>
    /// <remarks>
    /// Test scaffolding only. It drives the production <see cref="CombatActionDriver"/> on
    /// the approved Male and Female characters so the regression gates have measured
    /// evidence. The production runtime does not reference this type or any other
    /// prototype type; the dependency runs one way only.
    /// </remarks>
    public sealed class ProtoProductionCombatTest : MonoBehaviour
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

        private void OnDestroy()
        {
            Time.captureFramerate = 0;
        }

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

        private IEnumerator WaitAvailable(CombatActionDriver d)
        {
            for (int i = 0; i < 900 && !d.IsAvailable; i++) yield return null;
            yield return null;
        }

        private IEnumerator WaitCooldown(CombatantBehaviour c, string skill)
        {
            var id = new ChibiFantasy.Core.DefinitionId(skill);
            for (int i = 0; i < 900 && !c.Cooldowns.IsReady(id); i++) yield return null;
            yield return null;
        }

        private IEnumerator Run()
        {
            yield return Frames(5);
            yield return Gate("MALE", maleDriver);
            yield return Gate("FEMALE", femaleDriver);
            Log("=== PRODUCTION COMBAT HARNESS COMPLETE ===");
            Done = true;
        }

        private IEnumerator Gate(string label, CombatActionDriver driver)
        {
            Log("=== " + label + " PRODUCTION COMBAT ===");

            if (driver == null || dummy == null) { Log("  MISSING REFERENCES"); yield break; }

            CombatantBehaviour self = driver.Self;
            driver.SetTarget(dummy);
            driver.ResetCombatRuntime();
            CombatRuntimeReset.Restore(dummy);
            driver.transform.position = dummy.transform.position - new Vector3(0f, 0f, 1.2f);
            Physics.SyncTransforms();
            yield return Frames(3);

            Log("  hp=" + self.CurrentHealth + "/" + self.MaxHealth
                + " mp=" + self.CurrentMana + "/" + self.MaxMana
                + " known=" + self.LearnedSkills.Count
                + " dummyHP=" + dummy.CurrentHealth);

            // ---- basic attack ----
            int before = dummy.CurrentHealth;
            CombatActionResult atk = driver.RequestBasicAttack();
            yield return Frames(2);
            Log("  BASIC ATTACK: accepted=" + atk.IsAccepted
                + " executed=" + (atk.Action != null && atk.Action.HasExecuted)
                + " dmg=" + (atk.Action != null ? atk.Action.AttackResult.Damage : -1)
                + " dummyHP " + before + "->" + dummy.CurrentHealth);

            CombatActionResult busy = driver.RequestBasicAttack();
            Log("  second request while busy: accepted=" + busy.IsAccepted + " reason=" + busy.Reason);

            yield return WaitAvailable(driver);

            // ---- PHYSICAL skill vs DEF ----
            dummy.SetStat("stat.def", 20);
            dummy.SetStat("stat.mdef", 0);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            int physBefore = dummy.CurrentHealth;
            CombatActionResult phys = driver.RequestSkill(Cleave);
            Log("  PHYSICAL cast started: accepted=" + phys.IsAccepted
                + " executedImmediately=" + (phys.Action != null && phys.Action.HasExecuted)
                + " (cast time 1.0s)");
            yield return Frames(35);
            bool midCast = phys.Action != null && !phys.Action.HasExecuted;
            yield return Frames(40);
            Log("  PHYSICAL vs DEF 20: notEarlyAt0.58s=" + midCast
                + " executed=" + (phys.Action != null && phys.Action.HasExecuted)
                + " dmg=" + (phys.Action != null && phys.Action.SkillResult.Effects.Count > 0
                    ? phys.Action.SkillResult.Effects[0].Amount : -1)
                + " dummyHP " + physBefore + "->" + dummy.CurrentHealth
                + " mp=" + self.CurrentMana);

            // ---- PHYSICAL vs high DEF ----
            yield return WaitAvailable(driver);
            yield return WaitCooldown(self, Cleave);
            dummy.SetStat("stat.def", 500);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            CombatActionResult physHigh = driver.RequestSkill(Cleave);
            yield return Frames(80);
            Log("  PHYSICAL vs DEF 500: dmg="
                + (physHigh.Action != null && physHigh.Action.SkillResult.Effects.Count > 0
                    ? physHigh.Action.SkillResult.Effects[0].Amount : -1) + " (reduced)");

            // ---- MAGIC skill vs MDEF ----
            yield return WaitAvailable(driver);
            dummy.SetStat("stat.def", 500);
            dummy.SetStat("stat.mdef", 0);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            int magBefore = dummy.CurrentHealth;
            CombatActionResult mag = driver.RequestSkill(Bolt);
            yield return Frames(3);
            Log("  MAGIC vs MDEF 0 (DEF 500): executed=" + (mag.Action != null && mag.Action.HasExecuted)
                + " dmg=" + (mag.Action != null && mag.Action.SkillResult.Effects.Count > 0
                    ? mag.Action.SkillResult.Effects[0].Amount : -1)
                + " dummyHP " + magBefore + "->" + dummy.CurrentHealth
                + "  <- high DEF must NOT reduce magic");

            // ---- MAGIC vs high MDEF ----
            yield return WaitAvailable(driver);
            yield return WaitCooldown(self, Bolt);
            dummy.SetStat("stat.mdef", 500);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            CombatActionResult magHigh = driver.RequestSkill(Bolt);
            yield return Frames(3);
            Log("  MAGIC vs MDEF 500: dmg="
                + (magHigh.Action != null && magHigh.Action.SkillResult.Effects.Count > 0
                    ? magHigh.Action.SkillResult.Effects[0].Amount : -1) + " (reduced)");

            // ---- cooldown rejects the immediate repeat ----
            yield return WaitAvailable(driver);
            CombatActionResult cd = driver.RequestSkill(Bolt);
            Log("  COOLDOWN: repeat accepted=" + cd.IsAccepted + " reason=" + cd.SkillReason);

            // ---- cancellation during cast ----
            yield return WaitCooldown(self, Cleave);
            yield return WaitAvailable(driver);
            dummy.SetStat("stat.def", 0);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            int cancelBefore = dummy.CurrentHealth;
            int manaBefore = self.CurrentMana;
            CombatActionResult toCancel = driver.RequestSkill(Cleave);
            yield return Frames(20);
            bool cancelled = driver.CancelAction();
            yield return Frames(80);
            Log("  CANCEL mid-cast: cancelled=" + cancelled
                + " executed=" + (toCancel.Action != null && toCancel.Action.HasExecuted)
                + " dummyUnchanged=" + (dummy.CurrentHealth == cancelBefore)
                + " manaUnchanged=" + (self.CurrentMana == manaBefore)
                + " cooldownStarted=" + !self.Cooldowns.IsReady(new ChibiFantasy.Core.DefinitionId(Cleave)));

            // ---- target dies mid-cast ----
            yield return WaitAvailable(driver);
            yield return WaitCooldown(self, Cleave);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            CombatActionResult racing = driver.RequestSkill(Cleave);
            yield return Frames(20);
            dummy.ApplyHealthDelta(-999999);
            yield return Frames(80);
            Log("  TARGET DIES mid-cast: executed=" + (racing.Action != null && racing.Action.HasExecuted)
                + " phase=" + (racing.Action != null ? racing.Action.Phase.ToString() : "n/a")
                + " reason=" + (racing.Action != null ? racing.Action.CancelReason.ToString() : "n/a"));

            // ---- dead target refuses new actions, reset restores ----
            CombatActionResult onCorpse = driver.RequestBasicAttack();
            Log("  DEAD TARGET: accepted=" + onCorpse.IsAccepted + " reason=" + onCorpse.AttackReason);
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(2);
            Log("  RESET: dummyAlive=" + dummy.IsAlive() + " hp=" + dummy.CurrentHealth
                + "/" + dummy.MaxHealth);

            // ---- actor death ----
            yield return WaitAvailable(driver);
            self.ApplyHealthDelta(-999999);
            CombatActionResult whenDead = driver.RequestBasicAttack();
            Log("  ACTOR DEAD: alive=" + self.IsAlive() + " accepted=" + whenDead.IsAccepted
                + " reason=" + whenDead.Reason);
            driver.ResetCombatRuntime();
            yield return Frames(2);
            Log("  ACTOR RESET: alive=" + self.IsAlive() + " hp=" + self.CurrentHealth
                + " mp=" + self.CurrentMana + " available=" + driver.IsAvailable);

            // ---- movement still works ----
            var move = driver.GetComponent<ProtoThirdPersonController>();
            Vector3 p0 = driver.transform.position;
            yield return Frames(30);
            Log("  after combat: movement=" + (move != null)
                + " grounded=" + (move != null && move.IsGrounded)
                + " drift=" + (driver.transform.position - p0).magnitude.ToString("F4") + "m");

            driver.ResetCombatRuntime();
            CombatRuntimeReset.Restore(dummy);
            yield return Frames(3);
        }
    }
}
