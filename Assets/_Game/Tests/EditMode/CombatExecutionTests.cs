using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Hit result and basic attack execution. (STEPS 5 and 6)</summary>
    public sealed class BasicAttackExecutionTests
    {
        private FakeCombatant _attacker;
        private FakeCombatant _target;

        [SetUp]
        public void SetUp()
        {
            _attacker = new FakeCombatant("attacker", 1, 100, 100)
                .WithStat(CombatTestIds.AttackPower, 30);
            _target = new FakeCombatant("target", 2, 100, 100)
                .WithStat(CombatTestIds.Defense, 10);
        }

        private AttackResult Attack(bool ready = true)
        {
            return BasicAttackExecutor.Execute(
                new AttackIntent(_attacker, _target), CombatTestIds.MeleeRules(), ready);
        }

        [Test]
        public void Valid_attack_produces_a_hit_with_full_bookkeeping()
        {
            var result = Attack();

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Reason, Is.EqualTo(AttackRejection.None));
            Assert.That(result.Damage, Is.EqualTo(20), "30 attack - 10 defence.");
            Assert.That(result.TargetHealthBefore, Is.EqualTo(100));
            Assert.That(result.TargetHealthAfter, Is.EqualTo(80));
            Assert.That(result.HealthLost, Is.EqualTo(20));
            Assert.That(result.TargetDied, Is.False);
            Assert.That(result.AttackerId, Is.EqualTo(_attacker.CombatantId));
            Assert.That(result.TargetId, Is.EqualTo(_target.CombatantId));
        }

        [Test]
        public void Target_health_decreases_and_attacker_health_is_untouched()
        {
            Attack();

            Assert.That(_target.CurrentHealth, Is.EqualTo(80));
            Assert.That(_attacker.CurrentHealth, Is.EqualTo(100));
            Assert.That(_attacker.ApplyCallCount, Is.EqualTo(0));
        }

        [Test]
        public void Repeated_attacks_accumulate()
        {
            Attack();
            Attack();
            Attack();

            Assert.That(_target.CurrentHealth, Is.EqualTo(40));
        }

        [Test]
        public void Killing_blow_sets_died_once_and_health_stops_at_zero()
        {
            _target.ApplyHealthDelta(-85);      // 15 left, one 20-damage blow kills
            Assert.That(_target.CurrentHealth, Is.EqualTo(15));

            var result = Attack();

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Damage, Is.EqualTo(20), "The computed blow is still 20...");
            Assert.That(result.HealthLost, Is.EqualTo(15), "...but only 15 health was actually lost.");
            Assert.That(result.TargetHealthAfter, Is.EqualTo(0));
            Assert.That(result.TargetDied, Is.True);
            Assert.That(_target.CurrentHealth, Is.EqualTo(0), "Health never goes below zero.");
        }

        [Test]
        public void Dead_target_cannot_be_attacked_and_died_is_not_reasserted()
        {
            _target.ApplyHealthDelta(-100);
            int callsBefore = _target.ApplyCallCount;

            var result = Attack();

            Assert.That(result.IsHit, Is.False);
            Assert.That(result.Reason, Is.EqualTo(AttackRejection.TargetDead));
            Assert.That(result.TargetDied, Is.False, "A corpse does not die again.");
            Assert.That(_target.ApplyCallCount, Is.EqualTo(callsBefore),
                "A rejected attack must not write health at all.");
        }

        [Test]
        public void Dead_attacker_cannot_attack()
        {
            _attacker.ApplyHealthDelta(-100);

            var result = Attack();

            Assert.That(result.Reason, Is.EqualTo(AttackRejection.AttackerDead));
            Assert.That(_target.CurrentHealth, Is.EqualTo(100));
            Assert.That(_target.ApplyCallCount, Is.EqualTo(0));
        }

        [Test]
        public void Missing_participants_are_rejected_without_throwing()
        {
            var noTarget = BasicAttackExecutor.Execute(
                new AttackIntent(_attacker, null), CombatTestIds.MeleeRules());
            var noAttacker = BasicAttackExecutor.Execute(
                new AttackIntent(null, _target), CombatTestIds.MeleeRules());

            Assert.That(noTarget.Reason, Is.EqualTo(AttackRejection.NoTarget));
            Assert.That(noAttacker.Reason, Is.EqualTo(AttackRejection.NoAttacker));
            Assert.That(_target.ApplyCallCount, Is.EqualTo(0));
        }

        [Test]
        public void Friendly_and_self_targets_are_rejected_by_a_basic_attack()
        {
            var ally = new FakeCombatant("ally", 1, 100, 100);

            var onAlly = BasicAttackExecutor.Execute(
                new AttackIntent(_attacker, ally), CombatTestIds.MeleeRules());
            var onSelf = BasicAttackExecutor.Execute(
                new AttackIntent(_attacker, _attacker), CombatTestIds.MeleeRules());

            Assert.That(onAlly.Reason, Is.EqualTo(AttackRejection.RelationshipNotPermitted));
            Assert.That(onSelf.Reason, Is.EqualTo(AttackRejection.RelationshipNotPermitted));
            Assert.That(ally.CurrentHealth, Is.EqualTo(100));
            Assert.That(_attacker.CurrentHealth, Is.EqualTo(100));
        }

        [Test]
        public void Unready_attacker_is_rejected_without_mutating_anything()
        {
            var result = Attack(ready: false);

            Assert.That(result.Reason, Is.EqualTo(AttackRejection.NotReady));
            Assert.That(_target.CurrentHealth, Is.EqualTo(100));
            Assert.That(_target.ApplyCallCount, Is.EqualTo(0));
        }

        [Test]
        public void Repeated_invalid_attacks_do_not_corrupt_state()
        {
            for (int i = 0; i < 50; i++)
            {
                BasicAttackExecutor.Execute(new AttackIntent(_attacker, null), CombatTestIds.MeleeRules());
                BasicAttackExecutor.Execute(new AttackIntent(null, _target), CombatTestIds.MeleeRules());
                Attack(ready: false);
            }

            Assert.That(_target.CurrentHealth, Is.EqualTo(100));
            Assert.That(_attacker.CurrentHealth, Is.EqualTo(100));
            Assert.That(_target.ApplyCallCount, Is.EqualTo(0));

            Assert.That(Attack().IsHit, Is.True, "State is still usable after many refusals.");
        }

        [Test]
        public void A_hit_for_zero_damage_is_still_a_hit()
        {
            var tank = new FakeCombatant("tank", 2, 100, 100)
                .WithStat(CombatTestIds.Defense, 9999);

            var rules = BasicAttackRules.Melee(
                CombatTestIds.AttackPowerStat, CombatTestIds.DefenseStat, 0, 100f);

            var result = BasicAttackExecutor.Execute(new AttackIntent(_attacker, tank), rules);

            Assert.That(result.IsHit, Is.True, "Armour holding is not the same as an illegal attack.");
            Assert.That(result.Damage, Is.EqualTo(0));
            Assert.That(tank.CurrentHealth, Is.EqualTo(100));
        }

        [Test]
        public void Missing_stats_read_as_zero_so_a_fight_still_resolves()
        {
            var plain = new FakeCombatant("plain", 1, 100, 100);   // no stats at all
            var victim = new FakeCombatant("victim", 2, 100, 100);

            var result = BasicAttackExecutor.Execute(
                new AttackIntent(plain, victim), CombatTestIds.MeleeRules(minimumDamage: 1));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Damage, Is.EqualTo(1), "0 attack - 0 defence, floored at 1.");
            Assert.That(plain.TryGetCombatStat(CombatTestIds.AttackPowerStat, out _), Is.False,
                "Absence is still reported honestly by the combatant itself.");
        }

        [Test]
        public void Execution_is_deterministic()
        {
            for (int i = 0; i < 200; i++)
            {
                var a = new FakeCombatant("a", 1, 100, 100).WithStat(CombatTestIds.AttackPower, 30);
                var b = new FakeCombatant("b", 2, 100, 100).WithStat(CombatTestIds.Defense, 10);

                var result = BasicAttackExecutor.Execute(
                    new AttackIntent(a, b), CombatTestIds.MeleeRules());

                Assert.That(result.Damage, Is.EqualTo(20));
                Assert.That(result.TargetHealthAfter, Is.EqualTo(80));
            }
        }
    }

    /// <summary>Attack pacing. (STEP 8)</summary>
    public sealed class AttackStateMachineTests
    {
        private static AttackStateMachine Machine(float attack = 0.4f, float recovery = 0.6f)
        {
            return new AttackStateMachine(new AttackTiming(attack, recovery));
        }

        [Test]
        public void Starts_idle_and_ready()
        {
            var m = Machine();
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(m.CanAttack, Is.True);
        }

        [Test]
        public void Cannot_attack_twice_in_the_same_instant()
        {
            var m = Machine();

            Assert.That(m.TryBeginAttack(), Is.True);
            Assert.That(m.TryBeginAttack(), Is.False);
            Assert.That(m.TryBeginAttack(), Is.False);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Attacking));
        }

        [Test]
        public void Unlimited_spam_in_one_instant_yields_exactly_one_attack()
        {
            var m = Machine();
            int accepted = 0;

            for (int i = 0; i < 1000; i++)
            {
                if (m.TryBeginAttack()) accepted++;
            }

            Assert.That(accepted, Is.EqualTo(1));
        }

        [Test]
        public void Progresses_idle_attacking_recovery_idle()
        {
            var m = Machine(0.4f, 0.6f);

            m.TryBeginAttack();
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Attacking));

            m.Advance(0.2f);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Attacking));

            m.Advance(0.2f);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Recovery));

            m.Advance(0.5f);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Recovery));

            m.Advance(0.1f);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(m.CanAttack, Is.True);
        }

        [Test]
        public void A_delta_longer_than_everything_lands_on_idle_in_one_call()
        {
            var m = Machine(0.4f, 0.6f);
            m.TryBeginAttack();

            m.Advance(999f);

            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(m.Remaining, Is.EqualTo(0f), "No backlog is carried.");
        }

        [Test]
        public void Instant_timing_returns_to_idle_immediately()
        {
            var m = new AttackStateMachine(AttackTiming.Instant);

            Assert.That(m.TryBeginAttack(), Is.True);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle),
                "A zero-length swing must not need a frame to finish.");
            Assert.That(m.TryBeginAttack(), Is.True);
        }

        [Test]
        public void Corrupt_deltas_cannot_stick_the_machine()
        {
            var m = Machine(0.4f, 0.6f);
            m.TryBeginAttack();

            m.Advance(float.NaN);
            m.Advance(float.PositiveInfinity);
            m.Advance(float.NegativeInfinity);
            m.Advance(-5f);
            m.Advance(0f);

            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Attacking));
            Assert.That(float.IsNaN(m.Remaining), Is.False);

            m.Advance(1f);
            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle), "It still recovers normally afterwards.");
        }

        [Test]
        public void Many_small_deltas_and_one_large_delta_reach_the_same_phase()
        {
            var small = Machine(0.4f, 0.6f);
            var large = Machine(0.4f, 0.6f);

            small.TryBeginAttack();
            large.TryBeginAttack();

            for (int i = 0; i < 100; i++) small.Advance(0.01f);   // exactly 1.0s
            large.Advance(1.0f);

            Assert.That(small.Phase, Is.EqualTo(large.Phase));
            Assert.That(small.Phase, Is.EqualTo(AttackPhase.Idle));
        }

        [Test]
        public void Reset_always_returns_to_idle()
        {
            var m = Machine();
            m.TryBeginAttack();
            m.Reset();

            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(m.TryBeginAttack(), Is.True);
        }

        [Test]
        public void Negative_timing_is_folded_to_zero_rather_than_throwing()
        {
            var t = new AttackTiming(-1f, -2f);
            Assert.That(t.AttackDuration, Is.EqualTo(0f));
            Assert.That(t.RecoveryDuration, Is.EqualTo(0f));
        }

        [Test]
        public void Advancing_while_idle_does_nothing()
        {
            var m = Machine();
            m.Advance(10f);

            Assert.That(m.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(m.Remaining, Is.EqualTo(0f));
        }
    }
}
