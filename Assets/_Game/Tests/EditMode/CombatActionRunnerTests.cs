using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The shared production combat action lifecycle. (STEPS 4-12, 18-21)
    /// </summary>
    internal sealed class CombatActionRunnerTests : SkillTestBase
    {
        private const string Def = "stat.def";
        private const string Mdef = "stat.mdef";
        private const string Atk = "stat.atk";

        private CharacterSkillsState _learned;
        private SkillCooldownState _cooldowns;
        private FakePooledCombatant _actor;
        private FakePooledCombatant _target;
        private CombatActionRunner _runner;

        private static SkillExecutionRules SkillRules(int floor = 0)
        {
            return new SkillExecutionRules(new DefinitionId(Def), new DefinitionId(Mdef), floor);
        }

        private static BasicAttackRules AttackRules(float range = 100f)
        {
            return BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, range);
        }

        private SkillUseContext Context(int level = 10)
        {
            return new SkillUseContext(Skills, _learned, level, _cooldowns);
        }

        /// <summary>
        /// Authors a skill with the supplied timing and one damage effect.
        /// </summary>
        /// <param name="learn">Teaches it to the actor, which almost every case needs.
        /// Pass false to author a skill the actor deliberately does not know.</param>
        private SkillDefinition Author(string id, float castTime, float cooldown,
            int damage = 30, DamageType type = DamageType.Physical, float cost = 0f,
            SkillTargetType targetType = SkillTargetType.SingleEnemy, int maxLevel = 1,
            SkillLevelEntry[] levels = null, bool learn = true)
        {
            var skill = AddSkill(id, maxLevel: maxLevel, castTime: castTime, range: 0f,
                levels: levels ?? new[]
                {
                    Level(1, 1, cost, cooldown, new[]
                    {
                        SkillEffect.Damage(damage, ElementType.Neutral, null, type)
                    })
                });
            SetPrivate(skill, "_targetType", targetType);
            SetPrivate(skill, "_resourceType",
                cost > 0f ? SkillResourceType.Mana : SkillResourceType.None);

            var definitionId = new DefinitionId(id);
            if (learn && !_learned.Knows(definitionId)) _learned.Learn(definitionId);

            return skill;
        }

        private void Actors(int targetHealth = 1000, int mana = 100)
        {
            _learned = new CharacterSkillsState(new CharacterId("c"));
            _cooldowns = new SkillCooldownState();
            _actor = new FakePooledCombatant("actor", 1, 100, 100, mana, mana)
                .WithStat(Atk, 40);
            _target = new FakePooledCombatant("target", 2, targetHealth, targetHealth)
                .WithStat(Def, 0).WithStat(Mdef, 0);
            _runner = new CombatActionRunner(_actor);
        }

        private CombatActionResult Cast(string id, int rank = 1, float recovery = 0f)
        {
            return _runner.RequestSkill(
                new SkillUseRequest(_actor, new DefinitionId(id), _target, rank),
                Context(), SkillRules(), recovery);
        }

        // ---------------- STEP 8/19: cast time ----------------

        [Test]
        public void Zero_cast_time_executes_immediately()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 0f);

            var result = Cast("s");

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Action.HasExecuted, Is.True, "A zero cast lands at once.");
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));
            Assert.That(_runner.Phase, Is.EqualTo(CombatActionPhase.Idle));
        }

        [TestCase(0.1f)]
        [TestCase(1f)]
        [TestCase(5f)]
        public void A_cast_does_not_execute_early_and_executes_exactly_once(float castTime)
        {
            Actors();
            Author("s", castTime, cooldown: 0f);

            var action = Cast("s").Action;
            Assert.That(action.HasExecuted, Is.False, "Nothing happens on acceptance.");
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));

            // just short of the cast
            _runner.Advance(castTime * 0.5f);
            Assert.That(action.HasExecuted, Is.False);
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));

            _runner.Advance(castTime * 0.49f);
            Assert.That(action.HasExecuted, Is.False, "Still short.");

            _runner.Advance(castTime);
            Assert.That(action.HasExecuted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));

            // keep ticking: it must never land twice
            for (int i = 0; i < 100; i++) _runner.Advance(1f);
            Assert.That(_target.CurrentHealth, Is.EqualTo(970), "Exactly one execution.");
        }

        [Test]
        public void A_delta_longer_than_the_whole_action_executes_once()
        {
            Actors();
            Author("s", castTime: 2f, cooldown: 0f);

            var action = Cast("s", recovery: 3f).Action;
            _runner.Advance(9999f);

            Assert.That(action.HasExecuted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));
            Assert.That(_runner.Phase, Is.EqualTo(CombatActionPhase.Idle));
        }

        [Test]
        public void Corrupt_deltas_neither_advance_nor_rewind_a_cast()
        {
            Actors();
            Author("s", castTime: 1f, cooldown: 0f);

            var action = Cast("s").Action;
            _runner.Advance(0.5f);

            _runner.Advance(float.NaN);
            _runner.Advance(float.PositiveInfinity);
            _runner.Advance(float.NegativeInfinity);
            _runner.Advance(-10f);
            _runner.Advance(0f);

            Assert.That(action.HasExecuted, Is.False);
            Assert.That(action.Elapsed, Is.EqualTo(0.5f));

            _runner.Advance(0.5f);
            Assert.That(action.HasExecuted, Is.True, "It still completes normally afterwards.");
        }

        // ---------------- STEP 21: determinism ----------------

        [TestCase(1, 1.0f)]
        [TestCase(10, 0.1f)]
        [TestCase(100, 0.01f)]
        [TestCase(1000, 0.001f)]
        public void Equivalent_elapsed_time_gives_the_same_outcome(int steps, float delta)
        {
            Actors();
            Author("s", castTime: 1f, cooldown: 0f);

            var action = Cast("s", recovery: 0f).Action;

            for (int i = 0; i < steps; i++) _runner.Advance(delta);

            Assert.That(action.HasExecuted, Is.True,
                steps + " x " + delta + " must complete a 1.0s cast.");
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));
            Assert.That(_runner.Phase, Is.EqualTo(CombatActionPhase.Idle));
        }

        // ---------------- STEP 18: cooldown ----------------

        [Test]
        public void Cooldown_comes_from_the_level_entry_and_differs_per_skill()
        {
            Actors();
            Author("fast", castTime: 0f, cooldown: 2f);
            Author("slow", castTime: 0f, cooldown: 5f);
            _learned.Learn(new DefinitionId("fast"));
            _learned.Learn(new DefinitionId("slow"));

            Cast("fast");
            Cast("slow");

            Assert.That(_cooldowns.GetRemaining(new DefinitionId("fast")), Is.EqualTo(2f));
            Assert.That(_cooldowns.GetRemaining(new DefinitionId("slow")), Is.EqualTo(5f));

            _cooldowns.Advance(2f);
            Assert.That(_cooldowns.IsReady(new DefinitionId("fast")), Is.True);
            Assert.That(_cooldowns.IsReady(new DefinitionId("slow")), Is.False,
                "Independent timers, both from data.");
        }

        [Test]
        public void Cooldown_differs_per_skill_level()
        {
            Actors();
            var skill = Author("s", castTime: 0f, cooldown: 0f, maxLevel: 2, levels: new[]
            {
                Level(1, 1, 0f, 5f, new[] { SkillEffect.Damage(10, ElementType.Neutral) }),
                Level(2, 1, 0f, 3f, new[] { SkillEffect.Damage(10, ElementType.Neutral) })
            });
            _learned.Learn(new DefinitionId("s"));

            Cast("s", rank: 1);
            Assert.That(_cooldowns.GetRemaining(new DefinitionId("s")), Is.EqualTo(5f));

            _cooldowns.Reset();
            _learned.SetRank(new DefinitionId("s"), 2);
            _runner.Reset();

            Cast("s", rank: 2);
            Assert.That(_cooldowns.GetRemaining(new DefinitionId("s")), Is.EqualTo(3f),
                "Rank 2's own authored cooldown, resolved after rank selection.");
        }

        [Test]
        public void A_rejected_request_starts_no_cooldown_and_mutates_nothing()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 5f, cost: 50f);
            _learned.Learn(new DefinitionId("s"));

            // insufficient resource
            _actor.TryApplyResourceDelta(SkillResourceType.Mana, -100);
            var broke = Cast("s");
            Assert.That(broke.IsAccepted, Is.False);
            Assert.That(broke.SkillReason, Is.EqualTo(SkillUseRejection.InsufficientResource));
            Assert.That(_cooldowns.Count, Is.EqualTo(0));
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));

            // unlearned skill: authored but deliberately not taught
            Actors();
            Author("s2", castTime: 0f, cooldown: 5f, learn: false);
            var unlearned = Cast("s2");
            Assert.That(unlearned.SkillReason, Is.EqualTo(SkillUseRejection.NotLearned));
            Assert.That(_cooldowns.Count, Is.EqualTo(0));

            // invalid target
            Actors();
            Author("s3", castTime: 0f, cooldown: 5f);
            _learned.Learn(new DefinitionId("s3"));
            _target.Team = new CombatTeam(1);          // now an ally
            var ally = Cast("s3");
            Assert.That(ally.SkillReason, Is.EqualTo(SkillUseRejection.RelationshipNotPermitted));
            Assert.That(_cooldowns.Count, Is.EqualTo(0));
            Assert.That(_target.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void A_successful_execution_starts_the_cooldown_exactly_once()
        {
            Actors();
            Author("s", castTime: 1f, cooldown: 4f);
            _learned.Learn(new DefinitionId("s"));

            Cast("s");
            Assert.That(_cooldowns.Count, Is.EqualTo(0), "Not started until it lands.");

            _runner.Advance(1f);
            Assert.That(_cooldowns.GetRemaining(new DefinitionId("s")), Is.EqualTo(4f));

            for (int i = 0; i < 20; i++) _runner.Advance(0.1f);
            Assert.That(_cooldowns.GetRemaining(new DefinitionId("s")), Is.EqualTo(4f),
                "Ticking the runner does not restart it.");
        }

        // ---------------- STEP 10: cancellation ----------------

        [Test]
        public void Cancelling_before_execution_prevents_everything()
        {
            Actors();
            Author("s", castTime: 2f, cooldown: 5f, cost: 10f);
            _learned.Learn(new DefinitionId("s"));

            int manaBefore = _actor.CurrentMana;
            var action = Cast("s").Action;
            _runner.Advance(1f);

            Assert.That(_runner.Cancel(), Is.True);

            Assert.That(action.Phase, Is.EqualTo(CombatActionPhase.Cancelled));
            Assert.That(action.CancelReason, Is.EqualTo(CombatActionCancelReason.Explicit));
            Assert.That(action.HasExecuted, Is.False);
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000), "No damage.");
            Assert.That(_actor.CurrentMana, Is.EqualTo(manaBefore), "No cost.");
            Assert.That(_cooldowns.Count, Is.EqualTo(0), "No cooldown.");

            // and it cannot resume
            _runner.Advance(100f);
            Assert.That(action.HasExecuted, Is.False);
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));
        }

        [Test]
        public void Cancelling_after_execution_does_not_roll_the_effect_back()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            var action = Cast("s", recovery: 5f).Action;
            Assert.That(action.HasExecuted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));

            _runner.Cancel();

            Assert.That(_target.CurrentHealth, Is.EqualTo(970),
                "Applied effects are never unwound; the policy is documented.");
            Assert.That(_runner.IsAvailable, Is.True, "Cancelling recovery frees the actor.");
        }

        [Test]
        public void Cancelling_when_idle_reports_that_nothing_was_cancelled()
        {
            Actors();
            Assert.That(_runner.Cancel(), Is.False);
        }

        // ---------------- STEP 9: revalidation ----------------

        [Test]
        public void A_target_that_dies_mid_cast_prevents_the_effect()
        {
            Actors();
            Author("s", castTime: 2f, cooldown: 5f, cost: 10f);
            _learned.Learn(new DefinitionId("s"));

            int manaBefore = _actor.CurrentMana;
            var action = Cast("s").Action;
            _runner.Advance(1f);

            _target.ApplyHealthDelta(-99999);        // dies mid-cast
            _runner.Advance(2f);

            Assert.That(action.HasExecuted, Is.False);
            Assert.That(action.Phase, Is.EqualTo(CombatActionPhase.Cancelled));
            Assert.That(action.CancelReason, Is.EqualTo(CombatActionCancelReason.TargetInvalidated));
            Assert.That(_actor.CurrentMana, Is.EqualTo(manaBefore), "No cost for an effect that never landed.");
            Assert.That(_cooldowns.Count, Is.EqualTo(0), "No cooldown either.");
        }

        [Test]
        public void A_target_that_changes_side_mid_cast_prevents_the_effect()
        {
            Actors();
            Author("s", castTime: 2f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            var action = Cast("s").Action;
            _runner.Advance(1f);
            _target.Team = new CombatTeam(1);        // becomes an ally
            _runner.Advance(2f);

            Assert.That(action.HasExecuted, Is.False);
            Assert.That(action.CancelReason, Is.EqualTo(CombatActionCancelReason.TargetInvalidated));
            Assert.That(_target.WriteCount, Is.EqualTo(0));
        }

        // ---------------- STEP 6: basic attack through the same lifecycle ----------------

        [Test]
        public void One_basic_attack_deals_damage_exactly_once()
        {
            Actors();

            var result = _runner.RequestBasicAttack(_target, AttackRules(), 0f, 0.5f);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Action.Type, Is.EqualTo(CombatActionType.BasicAttack));
            Assert.That(result.Action.HasExecuted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(960), "40 attack - 0 defence.");

            for (int i = 0; i < 50; i++) _runner.Advance(0.1f);
            Assert.That(_target.CurrentHealth, Is.EqualTo(960), "Never once per frame.");
        }

        [Test]
        public void A_second_request_while_busy_is_rejected_then_allowed_after_recovery()
        {
            Actors();

            _runner.RequestBasicAttack(_target, AttackRules(), 0f, 1f);
            Assert.That(_target.CurrentHealth, Is.EqualTo(960));

            var busy = _runner.RequestBasicAttack(_target, AttackRules(), 0f, 1f);
            Assert.That(busy.IsAccepted, Is.False);
            Assert.That(busy.Reason, Is.EqualTo(CombatActionRejection.AlreadyBusy));
            Assert.That(_target.CurrentHealth, Is.EqualTo(960), "The refused request dealt nothing.");

            _runner.Advance(1f);
            Assert.That(_runner.IsAvailable, Is.True);

            var second = _runner.RequestBasicAttack(_target, AttackRules(), 0f, 1f);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(920));
        }

        [Test]
        public void A_skill_request_while_attacking_is_also_rejected()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            _runner.RequestBasicAttack(_target, AttackRules(), 0f, 2f);
            var skill = Cast("s");

            Assert.That(skill.IsAccepted, Is.False);
            Assert.That(skill.Reason, Is.EqualTo(CombatActionRejection.AlreadyBusy),
                "One active action per combatant, whichever kind.");
        }

        [Test]
        public void An_out_of_range_or_dead_target_is_rejected_with_zero_mutation()
        {
            Actors();
            _actor.Position = CombatPosition.Zero;
            _target.Position = new CombatPosition(50f, 0f, 0f);

            var far = _runner.RequestBasicAttack(_target, AttackRules(range: 2f), 0f, 0f);
            Assert.That(far.AttackReason, Is.EqualTo(AttackRejection.OutOfRange));
            Assert.That(_target.WriteCount, Is.EqualTo(0));

            Actors();
            _target.ApplyHealthDelta(-99999);
            var dead = _runner.RequestBasicAttack(_target, AttackRules(), 0f, 0f);
            Assert.That(dead.AttackReason, Is.EqualTo(AttackRejection.TargetDead));
        }

        // ---------------- STEP 11: death ----------------

        [Test]
        public void A_dead_actor_cannot_start_anything()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            _actor.ApplyHealthDelta(-99999);
            Assert.That(_actor.IsAlive(), Is.False);

            Assert.That(_runner.RequestBasicAttack(_target, AttackRules(), 0f, 0f).Reason,
                Is.EqualTo(CombatActionRejection.ActorDead));
            Assert.That(Cast("s").Reason, Is.EqualTo(CombatActionRejection.ActorDead));
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));
            Assert.That(_runner.IsAvailable, Is.False);
        }

        [Test]
        public void An_actor_that_dies_mid_cast_has_the_action_cancelled_and_no_effect_lands()
        {
            Actors();
            Author("s", castTime: 2f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            var action = Cast("s").Action;
            _runner.Advance(1f);
            _actor.ApplyHealthDelta(-99999);
            _runner.Advance(2f);

            Assert.That(action.HasExecuted, Is.False);
            Assert.That(action.Phase, Is.EqualTo(CombatActionPhase.Cancelled));
            Assert.That(action.CancelReason, Is.EqualTo(CombatActionCancelReason.ActorDied));
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));
        }

        [Test]
        public void Death_is_still_derived_from_health_and_health_never_goes_negative()
        {
            Actors();
            _actor.ApplyHealthDelta(-99999);

            Assert.That(_actor.CurrentHealth, Is.EqualTo(0));
            Assert.That(_actor.IsAlive(), Is.False);

            _actor.ApplyHealthDelta(1);
            Assert.That(_actor.IsAlive(), Is.True, "No separate death flag to fall out of step.");
        }

        // ---------------- STEP 12: reset ----------------

        [Test]
        public void Reset_restores_combat_runtime_and_leaves_identity_and_skills_alone()
        {
            Actors();
            Author("s", castTime: 5f, cooldown: 9f);
            _learned.Learn(new DefinitionId("s"));

            InstanceId idBefore = _actor.CombatantId;
            int knownBefore = _learned.Count;
            Revision skillsRevisionBefore = _learned.Revision;

            _actor.ApplyHealthDelta(-99999);
            _actor.TryApplyResourceDelta(SkillResourceType.Mana, -999);
            _cooldowns.Begin(new DefinitionId("s"), 9f);

            CombatRuntimeReset.Restore(_actor, _runner, _cooldowns);

            Assert.That(_actor.CurrentHealth, Is.EqualTo(_actor.MaxHealth));
            Assert.That(_actor.CurrentMana, Is.EqualTo(_actor.MaxMana));
            Assert.That(_cooldowns.Count, Is.EqualTo(0));
            Assert.That(_runner.IsAvailable, Is.True);
            Assert.That(_actor.IsAlive(), Is.True);

            Assert.That(_actor.CombatantId, Is.EqualTo(idBefore), "Identity is untouched.");
            Assert.That(_learned.Count, Is.EqualTo(knownBefore), "Learned skills are untouched.");
            Assert.That(_learned.Revision, Is.EqualTo(skillsRevisionBefore));
            Assert.That(_actor.TryGetCombatStat(new DefinitionId(Atk), out int atk), Is.True);
            Assert.That(atk, Is.EqualTo(40), "Authored stats are untouched.");
        }

        [Test]
        public void Reset_cancels_an_action_in_flight()
        {
            Actors();
            Author("s", castTime: 5f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            var action = Cast("s").Action;
            CombatRuntimeReset.Restore(_actor, _runner, _cooldowns);

            Assert.That(action.Phase, Is.EqualTo(CombatActionPhase.Cancelled));
            Assert.That(action.CancelReason, Is.EqualTo(CombatActionCancelReason.Reset));
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));
        }

        // ---------------- STEP 20: sequences ----------------

        [Test]
        public void Sequence_attack_skill_attack_leaves_no_corruption()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            _runner.RequestBasicAttack(_target, AttackRules(), 0f, 0.5f);
            Assert.That(_target.CurrentHealth, Is.EqualTo(960));
            _runner.Advance(0.5f);

            Cast("s");
            Assert.That(_target.CurrentHealth, Is.EqualTo(930));

            _runner.RequestBasicAttack(_target, AttackRules(), 0f, 0.5f);
            Assert.That(_target.CurrentHealth, Is.EqualTo(890));

            _runner.Advance(0.5f);
            Assert.That(_runner.IsAvailable, Is.True);
            Assert.That(_actor.CurrentHealth, Is.EqualTo(100), "The actor never damaged itself.");
        }

        [Test]
        public void Sequence_skill_cooldown_rejection_then_success()
        {
            Actors();
            Author("s", castTime: 0f, cooldown: 3f);
            _learned.Learn(new DefinitionId("s"));

            Assert.That(Cast("s").IsAccepted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));

            var blocked = Cast("s");
            Assert.That(blocked.SkillReason, Is.EqualTo(SkillUseRejection.OnCooldown));
            Assert.That(_target.CurrentHealth, Is.EqualTo(970));

            _cooldowns.Advance(3f);
            Assert.That(Cast("s").IsAccepted, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(940));
        }

        [Test]
        public void Sequence_damage_until_death_then_reset_restores_availability()
        {
            Actors(targetHealth: 60);
            Author("s", castTime: 0f, cooldown: 0f);
            _learned.Learn(new DefinitionId("s"));

            Cast("s");
            Assert.That(_target.CurrentHealth, Is.EqualTo(30));

            Cast("s");
            Assert.That(_target.CurrentHealth, Is.EqualTo(0));
            Assert.That(_target.IsAlive(), Is.False);

            var onCorpse = Cast("s");
            Assert.That(onCorpse.SkillReason, Is.EqualTo(SkillUseRejection.TargetDead));

            CombatRuntimeReset.Restore(_target);
            Assert.That(_target.IsAlive(), Is.True);
            Assert.That(Cast("s").IsAccepted, Is.True);
        }
    }
}
