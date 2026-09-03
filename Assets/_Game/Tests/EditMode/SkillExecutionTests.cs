using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Effect dispatch, damage, heal, resource, status and multi-effect. (STEPS 4-9)</summary>
    internal sealed class SkillExecutionTests : SkillTestBase
    {
        private const string Sk = "skill.test";

        private CharacterSkillsState _learned;
        private FakePooledCombatant _caster;
        private FakePooledCombatant _enemy;
        private FakePooledCombatant _ally;

        private void Build(SkillEffect[] effects,
            SkillTargetType targetType = SkillTargetType.SingleEnemy,
            float cost = 0f, float cooldown = 0f)
        {
            var skill = AddSkill(Sk, maxLevel: 1, levels: new[] { Level(1, 1, cost, cooldown, effects) });
            SetPrivate(skill, "_targetType", targetType);

            _learned = new CharacterSkillsState(new CharacterId("c1"));
            _learned.Learn(new DefinitionId(Sk));

            _caster = new FakePooledCombatant("caster", 1, 100, 100, 50, 50)
                .WithStat(SkillCombatIds.Power, 20);
            _enemy = new FakePooledCombatant("enemy", 2, 200, 200, 30, 30)
                .WithStat(SkillCombatIds.Defense, 0);
            _ally = new FakePooledCombatant("ally", 1, 50, 200, 10, 30);
        }

        private SkillExecutionResult Run(ICombatant target = null, int minimumDamage = 0,
            SkillCooldownState cooldowns = null)
        {
            return SkillExecutor.Execute(
                new SkillUseRequest(_caster, new DefinitionId(Sk), target ?? _enemy),
                new SkillUseContext(Skills, _learned, 10, cooldowns),
                SkillCombatIds.Rules(minimumDamage));
        }

        // ---------------- STEP 5: damage ----------------

        [Test]
        public void Damage_skill_reuses_the_existing_formula_and_hits_the_target_only()
        {
            Build(new[] { SkillEffect.Damage(50, ElementType.Neutral) });

            var result = Run();

            Assert.That(result.IsExecuted, Is.True);
            Assert.That(result.Effects.Count, Is.EqualTo(1));
            Assert.That(result.Effects[0].Kind, Is.EqualTo(SkillEffectKind.Damage));
            Assert.That(result.Effects[0].Amount, Is.EqualTo(50));
            Assert.That(result.TargetHealthBefore, Is.EqualTo(200));
            Assert.That(result.TargetHealthAfter, Is.EqualTo(150));
            Assert.That(_caster.CurrentHealth, Is.EqualTo(100), "The caster is untouched.");
        }

        [Test]
        public void Damage_scales_off_the_casters_stats_through_the_authored_terms()
        {
            // 10 flat + (power 20 * 1/2) = 20
            Build(new[]
            {
                SkillEffect.Damage(10, ElementType.Neutral,
                    new[] { new StatTerm(SkillCombatIds.PowerStat, 1, 2) })
            });

            Assert.That(Run().Effects[0].Amount, Is.EqualTo(20));
        }

        [Test]
        public void Damage_is_reduced_by_the_targets_defending_stat()
        {
            Build(new[] { SkillEffect.Damage(50, ElementType.Neutral) });
            _enemy.WithStat(SkillCombatIds.Defense, 20);

            var result = Run();

            Assert.That(result.Effects[0].Amount, Is.EqualTo(30), "50 - 20 through BasicDamageFormula.");
            Assert.That(result.TargetHealthAfter, Is.EqualTo(170));
        }

        [Test]
        public void Over_defended_damage_falls_to_the_floor_and_never_heals()
        {
            Build(new[] { SkillEffect.Damage(10, ElementType.Neutral) });
            _enemy.WithStat(SkillCombatIds.Defense, 9999);

            Assert.That(Run(minimumDamage: 0).Effects[0].Amount, Is.EqualTo(0));

            // Same skill, different floor. Rebuilding here would register a duplicate id.
            Assert.That(Run(minimumDamage: 3).Effects[0].Amount, Is.EqualTo(3));
        }

        [Test]
        public void Killing_blow_clamps_at_zero_and_reports_death_once()
        {
            Build(new[] { SkillEffect.Damage(500, ElementType.Neutral) });

            var result = Run();

            Assert.That(result.TargetDied, Is.True);
            Assert.That(result.TargetHealthAfter, Is.EqualTo(0));
            Assert.That(result.Effects[0].Amount, Is.EqualTo(500));
            Assert.That(result.Effects[0].Change, Is.EqualTo(-200), "Only 200 health was actually lost.");
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(0));

            var again = Run();
            Assert.That(again.IsExecuted, Is.False);
            Assert.That(again.Reason, Is.EqualTo(SkillUseRejection.TargetDead));
            Assert.That(again.TargetDied, Is.False, "A corpse does not die again.");
        }

        [Test]
        public void Damage_execution_is_deterministic()
        {
            for (int i = 0; i < 100; i++)
            {
                TearDown();
                SetUp();
                Build(new[] { SkillEffect.Damage(37, ElementType.Neutral) });
                _enemy.WithStat(SkillCombatIds.Defense, 11);

                var result = Run();
                Assert.That(result.Effects[0].Amount, Is.EqualTo(26));
                Assert.That(result.TargetHealthAfter, Is.EqualTo(174));
            }
        }

        [Test]
        public void A_rejected_skill_mutates_nothing()
        {
            Build(new[] { SkillEffect.Damage(50, ElementType.Neutral) }, cost: 10f);

            int manaBefore = _caster.CurrentMana;

            for (int i = 0; i < 20; i++)
            {
                Run(target: _ally);                             // friendly, rejected
                SkillExecutor.Execute(
                    new SkillUseRequest(_caster, new DefinitionId("skill.missing"), _enemy),
                    new SkillUseContext(Skills, _learned, 10), SkillCombatIds.Rules());
            }

            Assert.That(_enemy.CurrentHealth, Is.EqualTo(200));
            Assert.That(_enemy.WriteCount, Is.EqualTo(0));
            Assert.That(_ally.WriteCount, Is.EqualTo(0));
            Assert.That(_caster.CurrentMana, Is.EqualTo(manaBefore), "No cost is paid for a refused use.");
        }

        // ---------------- STEP 6: heal ----------------

        [Test]
        public void Heal_restores_health_through_the_existing_pool()
        {
            Build(new[] { SkillEffect.Heal(30, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);

            var result = Run(target: _ally);

            Assert.That(result.IsExecuted, Is.True);
            Assert.That(result.Effects[0].Kind, Is.EqualTo(SkillEffectKind.Heal));
            Assert.That(result.TargetHealthBefore, Is.EqualTo(50));
            Assert.That(result.TargetHealthAfter, Is.EqualTo(80));
        }

        [Test]
        public void Overheal_clamps_at_maximum()
        {
            Build(new[] { SkillEffect.Heal(9999, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);

            var result = Run(target: _ally);

            Assert.That(result.TargetHealthAfter, Is.EqualTo(200), "MaxHealth, not 9999.");
            Assert.That(result.Effects[0].Change, Is.EqualTo(150));
        }

        [Test]
        public void Healing_a_full_target_is_a_no_op_not_a_failure()
        {
            Build(new[] { SkillEffect.Heal(50, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);
            _ally.ApplyHealthDelta(9999);   // to full

            var result = Run(target: _ally);

            Assert.That(result.IsExecuted, Is.True);
            Assert.That(result.Effects[0].Status, Is.EqualTo(SkillEffectStatus.NoOp));
        }

        [Test]
        public void Zero_heal_is_a_no_op()
        {
            Build(new[] { SkillEffect.Heal(0, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);

            Assert.That(Run(target: _ally).Effects[0].Status, Is.EqualTo(SkillEffectStatus.NoOp));
        }

        [Test]
        public void Self_heal_works_and_hostile_heal_is_rejected()
        {
            Build(new[] { SkillEffect.Heal(25, SkillResourceType.Health) }, SkillTargetType.Self);
            _caster.ApplyHealthDelta(-60);   // 40 of 100

            var self = SkillExecutor.Execute(
                new SkillUseRequest(_caster, new DefinitionId(Sk), null),
                new SkillUseContext(Skills, _learned, 10), SkillCombatIds.Rules());

            Assert.That(self.IsExecuted, Is.True);
            Assert.That(_caster.CurrentHealth, Is.EqualTo(65));

            // The same heal authored as ally-only cannot be aimed at an enemy.
            TearDown();
            SetUp();
            Build(new[] { SkillEffect.Heal(25, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);

            Assert.That(Run(target: _enemy).Reason,
                Is.EqualTo(SkillUseRejection.RelationshipNotPermitted));
            Assert.That(_enemy.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void Dead_target_cannot_be_healed_because_validation_rejects_it()
        {
            Build(new[] { SkillEffect.Heal(50, SkillResourceType.Health) },
                SkillTargetType.SingleAlly);
            _ally.ApplyHealthDelta(-9999);

            var result = Run(target: _ally);

            Assert.That(result.Reason, Is.EqualTo(SkillUseRejection.TargetDead),
                "A heal is not a resurrection; the policy is enforced at validation.");
            Assert.That(_ally.CurrentHealth, Is.EqualTo(0));
        }

        // ---------------- STEP 7: resource ----------------

        [Test]
        public void Resource_effect_restores_and_drains_mana()
        {
            Build(new[] { SkillEffect.ModifyResource(SkillResourceType.Mana, 15) },
                SkillTargetType.SingleAlly);

            var restore = Run(target: _ally);
            Assert.That(restore.Effects[0].Before, Is.EqualTo(10));
            Assert.That(restore.Effects[0].After, Is.EqualTo(25));

            TearDown();
            SetUp();
            Build(new[] { SkillEffect.ModifyResource(SkillResourceType.Mana, -5) },
                SkillTargetType.SingleAlly);

            var drain = Run(target: _ally);
            Assert.That(drain.Effects[0].After, Is.EqualTo(5));
        }

        [Test]
        public void Resource_effect_clamps_at_both_ends()
        {
            Build(new[] { SkillEffect.ModifyResource(SkillResourceType.Mana, 9999) },
                SkillTargetType.SingleAlly);
            Assert.That(Run(target: _ally).Effects[0].After, Is.EqualTo(30), "MaxMana.");

            TearDown();
            SetUp();
            Build(new[] { SkillEffect.ModifyResource(SkillResourceType.Mana, -9999) },
                SkillTargetType.SingleAlly);
            Assert.That(Run(target: _ally).Effects[0].After, Is.EqualTo(0), "Never negative.");
        }

        [Test]
        public void Resource_effect_on_a_target_with_no_such_pool_is_reported_unsupported()
        {
            Build(new[] { SkillEffect.ModifyResource(SkillResourceType.Stamina, 10) },
                SkillTargetType.SingleAlly);

            var outcome = Run(target: _ally).Effects[0];

            Assert.That(outcome.Status, Is.EqualTo(SkillEffectStatus.Unsupported));
            Assert.That(outcome.Detail, Does.Contain("Stamina"));
        }

        [Test]
        public void Skill_cost_is_deducted_once_from_the_casters_pool()
        {
            Build(new[] { SkillEffect.Damage(10, ElementType.Neutral) }, cost: 12f);

            var result = Run();

            Assert.That(result.ResourceSpent, Is.EqualTo(12));
            Assert.That(_caster.CurrentMana, Is.EqualTo(38));
        }

        // ---------------- STEP 8: status contract ----------------

        [Test]
        public void Status_effect_is_recognised_and_honestly_reported_as_unsupported()
        {
            Build(new[] { SkillEffect.ApplyStatusEffect(new DefinitionId("status.stun")) });

            var result = Run();

            Assert.That(result.IsExecuted, Is.True, "The skill itself ran.");
            Assert.That(result.Effects[0].Kind, Is.EqualTo(SkillEffectKind.ApplyStatusEffect));
            Assert.That(result.Effects[0].Status, Is.EqualTo(SkillEffectStatus.Unsupported));
            Assert.That(result.Effects[0].DidMutate, Is.False);
            Assert.That(result.HasUnsupportedEffect, Is.True);
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(200), "Nothing was faked.");
        }

        [Test]
        public void Stat_modifier_effect_is_also_unsupported_rather_than_silent()
        {
            Build(new[] { SkillEffect.ModifyStat(default) });

            var outcome = Run().Effects[0];

            Assert.That(outcome.Status, Is.EqualTo(SkillEffectStatus.Unsupported));
            Assert.That(outcome.Detail, Is.Not.Null);
        }

        [Test]
        public void An_effect_with_no_kind_is_unsupported()
        {
            Build(new[] { default(SkillEffect) });

            Assert.That(Run().Effects[0].Status, Is.EqualTo(SkillEffectStatus.Unsupported));
        }

        [Test]
        public void A_skill_with_no_effects_executes_and_reports_none()
        {
            Build(new SkillEffect[0]);

            var result = Run();

            Assert.That(result.IsExecuted, Is.True);
            Assert.That(result.Effects.Count, Is.EqualTo(0));
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(200));
        }

        // ---------------- STEP 9: multi-effect ----------------

        [Test]
        public void Multiple_effects_run_once_each_in_authored_order()
        {
            Build(new[]
            {
                SkillEffect.Damage(30, ElementType.Neutral),
                SkillEffect.ApplyStatusEffect(new DefinitionId("status.burn")),
                SkillEffect.ModifyResource(SkillResourceType.Mana, -10)
            });

            var result = Run();

            Assert.That(result.Effects.Count, Is.EqualTo(3));
            Assert.That(result.Effects[0].Kind, Is.EqualTo(SkillEffectKind.Damage));
            Assert.That(result.Effects[1].Kind, Is.EqualTo(SkillEffectKind.ApplyStatusEffect));
            Assert.That(result.Effects[2].Kind, Is.EqualTo(SkillEffectKind.ModifyResource));

            Assert.That(result.Effects[0].Status, Is.EqualTo(SkillEffectStatus.Applied));
            Assert.That(result.Effects[1].Status, Is.EqualTo(SkillEffectStatus.Unsupported));
            Assert.That(result.Effects[2].Status, Is.EqualTo(SkillEffectStatus.Applied));

            Assert.That(_enemy.CurrentHealth, Is.EqualTo(170), "Damage applied exactly once.");
            Assert.That(_enemy.CurrentMana, Is.EqualTo(20));
            Assert.That(result.HasUnsupportedEffect, Is.True,
                "One unsupported effect does not hide behind two that worked.");
        }

        [Test]
        public void Two_damage_effects_both_apply_and_do_not_double_count_each_other()
        {
            Build(new[]
            {
                SkillEffect.Damage(30, ElementType.Neutral),
                SkillEffect.Damage(20, ElementType.Neutral)
            });

            var result = Run();

            Assert.That(result.Effects[0].Change, Is.EqualTo(-30));
            Assert.That(result.Effects[1].Change, Is.EqualTo(-20));
            Assert.That(result.TargetHealthChange, Is.EqualTo(-50));
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(150));
        }

        // ---------------- STEP 10: runtime state ----------------

        [Test]
        public void Using_a_skill_starts_its_cooldown_and_blocks_the_next_use()
        {
            Build(new[] { SkillEffect.Damage(10, ElementType.Neutral) }, cooldown: 4f);
            var cooldowns = new SkillCooldownState();

            Assert.That(Run(cooldowns: cooldowns).IsExecuted, Is.True);
            Assert.That(cooldowns.IsReady(new DefinitionId(Sk)), Is.False);
            Assert.That(cooldowns.GetRemaining(new DefinitionId(Sk)), Is.EqualTo(4f));

            var blocked = Run(cooldowns: cooldowns);
            Assert.That(blocked.Reason, Is.EqualTo(SkillUseRejection.OnCooldown));
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(190), "The blocked use dealt no damage.");

            cooldowns.Advance(4f);
            Assert.That(Run(cooldowns: cooldowns).IsExecuted, Is.True);
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(180));
        }

        [Test]
        public void A_zero_cooldown_skill_never_blocks()
        {
            Build(new[] { SkillEffect.Damage(1, ElementType.Neutral) }, cooldown: 0f);
            var cooldowns = new SkillCooldownState();

            for (int i = 0; i < 5; i++)
            {
                Assert.That(Run(cooldowns: cooldowns).IsExecuted, Is.True);
            }

            Assert.That(cooldowns.Count, Is.EqualTo(0));
        }
    }

    /// <summary>The runtime cooldown container itself. (STEP 10)</summary>
    public sealed class SkillCooldownStateTests
    {
        private static readonly DefinitionId A = new DefinitionId("skill.a");
        private static readonly DefinitionId B = new DefinitionId("skill.b");

        [Test]
        public void An_untracked_skill_is_ready()
        {
            var state = new SkillCooldownState();
            Assert.That(state.IsReady(A), Is.True);
            Assert.That(state.GetRemaining(A), Is.EqualTo(0f));
        }

        [Test]
        public void Begin_then_advance_releases_the_skill()
        {
            var state = new SkillCooldownState();
            state.Begin(A, 2f);

            Assert.That(state.IsReady(A), Is.False);
            state.Advance(1f);
            Assert.That(state.IsReady(A), Is.False);
            state.Advance(1f);
            Assert.That(state.IsReady(A), Is.True);
        }

        [Test]
        public void Skills_cool_down_independently()
        {
            var state = new SkillCooldownState();
            state.Begin(A, 1f);
            state.Begin(B, 5f);

            state.Advance(1f);

            Assert.That(state.IsReady(A), Is.True);
            Assert.That(state.IsReady(B), Is.False);
            Assert.That(state.Count, Is.EqualTo(1));
        }

        [Test]
        public void Corrupt_or_non_positive_durations_leave_the_skill_ready()
        {
            var state = new SkillCooldownState();
            state.Begin(A, 0f);
            state.Begin(B, float.NaN);

            Assert.That(state.IsReady(A), Is.True);
            Assert.That(state.IsReady(B), Is.True);
            Assert.That(state.Count, Is.EqualTo(0));
        }

        [Test]
        public void Corrupt_deltas_cannot_stick_or_rewind_a_cooldown()
        {
            var state = new SkillCooldownState();
            state.Begin(A, 3f);

            state.Advance(float.NaN);
            state.Advance(float.PositiveInfinity);
            state.Advance(-10f);
            state.Advance(0f);

            Assert.That(state.IsReady(A), Is.False);
            Assert.That(state.GetRemaining(A), Is.EqualTo(3f));

            state.Advance(3f);
            Assert.That(state.IsReady(A), Is.True);
        }

        [Test]
        public void Many_small_deltas_and_one_large_delta_agree()
        {
            var small = new SkillCooldownState();
            var large = new SkillCooldownState();
            small.Begin(A, 1f);
            large.Begin(A, 1f);

            for (int i = 0; i < 100; i++) small.Advance(0.01f);
            large.Advance(1f);

            Assert.That(small.IsReady(A), Is.EqualTo(large.IsReady(A)));
            Assert.That(small.IsReady(A), Is.True);
        }

        [Test]
        public void Reset_and_Clear_release_everything()
        {
            var state = new SkillCooldownState();
            state.Begin(A, 5f);
            state.Begin(B, 5f);

            state.Clear(A);
            Assert.That(state.IsReady(A), Is.True);
            Assert.That(state.IsReady(B), Is.False);

            state.Reset();
            Assert.That(state.IsReady(B), Is.True);
            Assert.That(state.Count, Is.EqualTo(0));
        }

        [Test]
        public void Only_real_transitions_advance_the_revision()
        {
            var state = new SkillCooldownState();
            Revision start = state.Revision;

            state.Advance(1f);
            Assert.That(state.Revision, Is.EqualTo(start), "Advancing an empty set changes nothing.");

            state.Begin(A, 1f);
            Assert.That(state.Revision.IsNewerThan(start), Is.True);

            Revision afterBegin = state.Revision;
            state.Reset();
            Assert.That(state.Revision.IsNewerThan(afterBegin), Is.True);

            Revision afterReset = state.Revision;
            state.Reset();
            Assert.That(state.Revision, Is.EqualTo(afterReset), "Resetting nothing is not a change.");
        }

        [Test]
        public void It_is_runtime_state_and_not_persistent()
        {
            var state = new SkillCooldownState();

            Assert.That(state, Is.InstanceOf<IRuntimeState>());
            Assert.That(state, Is.Not.InstanceOf<IPersistentState>(),
                "A cooldown must never become saved character progression.");
        }
    }

    /// <summary>Effect amount arithmetic. (STEP 4)</summary>
    public sealed class SkillAmountCalculatorTests
    {
        [Test]
        public void Flat_amount_alone_when_there_is_no_scaling()
        {
            var effect = SkillEffect.Damage(42, ElementType.Neutral);
            Assert.That(SkillAmountCalculator.Calculate(effect, null), Is.EqualTo(42));
        }

        [Test]
        public void Scaling_terms_are_summed_with_integer_arithmetic()
        {
            var caster = new FakeCombatant("a", 1, 10, 10)
                .WithStat("stat.p", 7);

            // 10 + (7 * 1 / 2) = 10 + 3, integer division truncates.
            var effect = SkillEffect.Damage(10, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId("stat.p"), 1, 2) });

            Assert.That(SkillAmountCalculator.Calculate(effect, caster), Is.EqualTo(13));
        }

        [Test]
        public void A_missing_stat_contributes_nothing_rather_than_throwing()
        {
            var caster = new FakeCombatant("a", 1, 10, 10);

            var effect = SkillEffect.Damage(10, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId("stat.absent"), 5, 1) });

            Assert.That(SkillAmountCalculator.Calculate(effect, caster), Is.EqualTo(10));
        }

        [Test]
        public void A_zero_denominator_is_skipped_rather_than_dividing_by_zero()
        {
            var caster = new FakeCombatant("a", 1, 10, 10).WithStat("stat.p", 100);

            var effect = SkillEffect.Damage(5, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId("stat.p"), 1, 0) });

            Assert.That(SkillAmountCalculator.Calculate(effect, caster), Is.EqualTo(5));
        }

        [Test]
        public void Magnitude_never_returns_a_negative()
        {
            var effect = SkillEffect.Damage(-50, ElementType.Neutral);

            Assert.That(SkillAmountCalculator.Calculate(effect, null), Is.EqualTo(-50));
            Assert.That(SkillAmountCalculator.CalculateMagnitude(effect, null), Is.EqualTo(0));
        }

        [Test]
        public void Extreme_scaling_cannot_overflow()
        {
            var caster = new FakeCombatant("a", 1, 10, 10).WithStat("stat.p", int.MaxValue);

            var effect = SkillEffect.Damage(int.MaxValue, ElementType.Neutral,
                new[] { new StatTerm(new DefinitionId("stat.p"), 1000, 1) });

            Assert.That(SkillAmountCalculator.Calculate(effect, caster), Is.EqualTo(int.MaxValue));
        }
    }
}
