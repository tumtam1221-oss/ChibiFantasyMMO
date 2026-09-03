using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Combatant identity, teams and the single aliveness rule. (STEP 1)</summary>
    public sealed class CombatantContractTests
    {
        [Test]
        public void Combatant_exposes_identity_team_and_health()
        {
            var c = new FakeCombatant("a", 1, 50, 100);

            Assert.That(c.CombatantId.IsValid, Is.True);
            Assert.That(c.Team.IsValid, Is.True);
            Assert.That(c.CurrentHealth, Is.EqualTo(50));
            Assert.That(c.MaxHealth, Is.EqualTo(100));
        }

        [Test]
        public void Aliveness_is_derived_from_health_not_stored()
        {
            var c = new FakeCombatant("a", 1, 1, 100);
            Assert.That(c.IsAlive(), Is.True);

            c.ApplyHealthDelta(-1);
            Assert.That(c.CurrentHealth, Is.EqualTo(0));
            Assert.That(c.IsAlive(), Is.False);

            c.ApplyHealthDelta(+5);
            Assert.That(c.IsAlive(), Is.True, "Aliveness must follow the number in both directions.");
        }

        [Test]
        public void Null_combatant_is_not_alive()
        {
            ICombatant none = null;
            Assert.That(none.IsAlive(), Is.False);
        }

        [Test]
        public void Health_never_goes_below_zero()
        {
            var c = new FakeCombatant("a", 1, 10, 100);
            c.ApplyHealthDelta(-999999);
            Assert.That(c.CurrentHealth, Is.EqualTo(0));
        }

        [Test]
        public void Team_none_is_invalid_and_others_are_valid()
        {
            Assert.That(CombatTeam.None.IsValid, Is.False);
            Assert.That(new CombatTeam(1).IsValid, Is.True);
            Assert.That(new CombatTeam(-1).IsValid, Is.True, "Any non-zero value is a team.");
        }

        [Test]
        public void Relationship_self_beats_friendly_for_the_same_combatant()
        {
            var c = new FakeCombatant("a", 1, 10, 10);
            Assert.That(CombatTeams.Relate(c, c), Is.EqualTo(CombatRelationship.Self));
        }

        [Test]
        public void Relationship_is_friendly_on_the_same_team_and_hostile_across_teams()
        {
            var a = new FakeCombatant("a", 1, 10, 10);
            var ally = new FakeCombatant("b", 1, 10, 10);
            var foe = new FakeCombatant("c", 2, 10, 10);

            Assert.That(CombatTeams.Relate(a, ally), Is.EqualTo(CombatRelationship.Friendly));
            Assert.That(CombatTeams.Relate(a, foe), Is.EqualTo(CombatRelationship.Hostile));
        }

        [Test]
        public void Relationship_is_undetermined_when_a_team_is_missing()
        {
            var a = new FakeCombatant("a", 0, 10, 10);
            var b = new FakeCombatant("b", 2, 10, 10);

            Assert.That(CombatTeams.Relate(a, b), Is.EqualTo(CombatRelationship.None));
            Assert.That(CombatTeams.Relate(b, a), Is.EqualTo(CombatRelationship.None));
            Assert.That(CombatTeams.Relate(null, b), Is.EqualTo(CombatRelationship.None));
            Assert.That(CombatTeams.Relate(a, null), Is.EqualTo(CombatRelationship.None));
        }

        [Test]
        public void Mask_never_permits_an_undetermined_relationship()
        {
            var everything = CombatRelationshipMask.Self | CombatRelationshipMask.Friendly
                             | CombatRelationshipMask.Hostile;
            Assert.That(CombatTeams.Permits(everything, CombatRelationship.None), Is.False);
        }
    }

    /// <summary>Target validity. (STEP 2)</summary>
    public sealed class TargetContractTests
    {
        private static readonly CombatRelationshipMask HostileOnly = CombatRelationshipMask.Hostile;

        [Test]
        public void Hostile_target_is_valid()
        {
            var a = new FakeCombatant("a", 1, 10, 10);
            var b = new FakeCombatant("b", 2, 10, 10);

            var result = TargetEvaluator.Evaluate(a, b, HostileOnly);

            Assert.That(result.IsAllowed, Is.True);
            Assert.That(result.Reason, Is.EqualTo(TargetRejection.None));
            Assert.That(result.Relationship, Is.EqualTo(CombatRelationship.Hostile));
        }

        [Test]
        public void Missing_attacker_or_target_is_rejected()
        {
            var a = new FakeCombatant("a", 1, 10, 10);

            Assert.That(TargetEvaluator.Evaluate(null, a, HostileOnly).Reason,
                Is.EqualTo(TargetRejection.NoAttacker));
            Assert.That(TargetEvaluator.Evaluate(a, null, HostileOnly).Reason,
                Is.EqualTo(TargetRejection.NoTarget));
        }

        [Test]
        public void Dead_target_is_rejected_and_reported_as_dead_not_as_relationship()
        {
            var a = new FakeCombatant("a", 1, 10, 10);
            var b = new FakeCombatant("b", 2, 0, 10);

            var result = TargetEvaluator.Evaluate(a, b, HostileOnly);

            Assert.That(result.IsAllowed, Is.False);
            Assert.That(result.Reason, Is.EqualTo(TargetRejection.TargetDead));
        }

        [Test]
        public void Dead_attacker_is_rejected_before_the_target_is_judged()
        {
            var a = new FakeCombatant("a", 1, 0, 10);
            var b = new FakeCombatant("b", 2, 0, 10);

            Assert.That(TargetEvaluator.Evaluate(a, b, HostileOnly).Reason,
                Is.EqualTo(TargetRejection.AttackerDead),
                "Attacker is checked first, so both being dead reports the attacker.");
        }

        [Test]
        public void Self_is_rejected_by_a_hostile_only_action_but_allowed_when_permitted()
        {
            var a = new FakeCombatant("a", 1, 10, 10);

            var denied = TargetEvaluator.Evaluate(a, a, HostileOnly);
            Assert.That(denied.IsAllowed, Is.False);
            Assert.That(denied.Reason, Is.EqualTo(TargetRejection.RelationshipNotPermitted));
            Assert.That(denied.Relationship, Is.EqualTo(CombatRelationship.Self));

            var allowed = TargetEvaluator.Evaluate(a, a, CombatRelationshipMask.Self);
            Assert.That(allowed.IsAllowed, Is.True);
        }

        [Test]
        public void Friendly_is_rejected_by_a_hostile_only_action_but_allowed_when_permitted()
        {
            var a = new FakeCombatant("a", 1, 10, 10);
            var ally = new FakeCombatant("b", 1, 10, 10);

            Assert.That(TargetEvaluator.Evaluate(a, ally, HostileOnly).Reason,
                Is.EqualTo(TargetRejection.RelationshipNotPermitted));
            Assert.That(TargetEvaluator.Evaluate(a, ally, CombatRelationshipMask.Friendly).IsAllowed,
                Is.True);
        }

        [Test]
        public void Undetermined_relationship_is_its_own_rejection()
        {
            var a = new FakeCombatant("a", 0, 10, 10);
            var b = new FakeCombatant("b", 2, 10, 10);

            Assert.That(TargetEvaluator.Evaluate(a, b, HostileOnly).Reason,
                Is.EqualTo(TargetRejection.UndefinedRelationship));
        }
    }

    /// <summary>Attack intent is inert data. (STEP 3)</summary>
    public sealed class AttackIntentTests
    {
        [Test]
        public void Constructing_an_intent_applies_no_damage()
        {
            var a = new FakeCombatant("a", 1, 10, 10);
            var b = new FakeCombatant("b", 2, 10, 10);

            var intent = new AttackIntent(a, b);

            Assert.That(b.CurrentHealth, Is.EqualTo(10));
            Assert.That(b.ApplyCallCount, Is.EqualTo(0), "Describing an attack must not perform it.");
            Assert.That(intent.Attacker, Is.SameAs(a));
            Assert.That(intent.Target, Is.SameAs(b));
        }

        [Test]
        public void Intent_defaults_to_a_generic_basic_attack()
        {
            var intent = new AttackIntent(new FakeCombatant("a", 1, 10, 10),
                                          new FakeCombatant("b", 2, 10, 10));

            Assert.That(intent.AttackDefinition.IsValid, Is.False,
                "No weapon type is implied by a plain basic attack.");
            Assert.That(intent.Sequence, Is.EqualTo(0));
        }

        [Test]
        public void Intent_can_carry_authored_content_without_combat_interpreting_it()
        {
            var id = new DefinitionId("attack.basic.slash");
            var intent = new AttackIntent(new FakeCombatant("a", 1, 10, 10),
                                          new FakeCombatant("b", 2, 10, 10), id, 7);

            Assert.That(intent.AttackDefinition, Is.EqualTo(id));
            Assert.That(intent.Sequence, Is.EqualTo(7));
        }

        [Test]
        public void Structural_completeness_reports_missing_participants()
        {
            var a = new FakeCombatant("a", 1, 10, 10);

            Assert.That(new AttackIntent(a, null).IsStructurallyComplete, Is.False);
            Assert.That(new AttackIntent(null, a).IsStructurallyComplete, Is.False);
            Assert.That(new AttackIntent(a, a).IsStructurallyComplete, Is.True);
        }
    }

    /// <summary>Deterministic damage arithmetic. (STEP 4)</summary>
    public sealed class BasicDamageFormulaTests
    {
        [TestCase(100, 40, 1, 60)]
        [TestCase(100, 0, 1, 100)]
        [TestCase(0, 0, 1, 1)]
        [TestCase(0, 50, 1, 1)]
        [TestCase(50, 50, 1, 1)]
        [TestCase(50, 999, 1, 1)]
        [TestCase(50, 999, 0, 0)]
        [TestCase(1, 0, 0, 1)]
        public void Subtractive_formula_with_floor(int attack, int defense, int floor, int expected)
        {
            Assert.That(BasicDamageFormula.Calculate(attack, defense, floor), Is.EqualTo(expected));
        }

        [Test]
        public void Equal_attack_and_defense_falls_to_the_floor()
        {
            Assert.That(BasicDamageFormula.Calculate(250, 250, 3), Is.EqualTo(3));
        }

        [Test]
        public void Damage_is_never_negative_even_with_negative_inputs()
        {
            Assert.That(BasicDamageFormula.Calculate(-100, 10, 0), Is.EqualTo(0));
            Assert.That(BasicDamageFormula.Calculate(10, -100, 0), Is.EqualTo(10),
                "A negative defence counts as zero, not as a bonus.");
            Assert.That(BasicDamageFormula.Calculate(-5, -5, -5), Is.EqualTo(0));
        }

        [Test]
        public void Extreme_inputs_do_not_overflow()
        {
            Assert.That(BasicDamageFormula.Calculate(int.MaxValue, 0, 0), Is.EqualTo(int.MaxValue));
            Assert.That(BasicDamageFormula.Calculate(int.MaxValue, int.MaxValue, 0), Is.EqualTo(0));
            Assert.That(BasicDamageFormula.Calculate(int.MinValue, int.MaxValue, 0), Is.EqualTo(0));
            Assert.That(BasicDamageFormula.Calculate(0, int.MinValue, 0), Is.EqualTo(0));
        }

        [Test]
        public void Same_inputs_always_produce_the_same_output()
        {
            int first = BasicDamageFormula.Calculate(137, 61, 1);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(BasicDamageFormula.Calculate(137, 61, 1), Is.EqualTo(first));
            }

            Assert.That(first, Is.EqualTo(76));
        }
    }

    /// <summary>Melee range rules. (STEP 7)</summary>
    public sealed class MeleeRangeTests
    {
        private static AttackResult Attack(float distance, float range)
        {
            var a = new FakeCombatant("a", 1, 100, 100).WithStat(CombatTestIds.AttackPower, 10);
            var b = new FakeCombatant("b", 2, 100, 100).WithStat(CombatTestIds.Defense, 0);
            a.Position = CombatPosition.Zero;
            b.Position = new CombatPosition(distance, 0f, 0f);

            var rules = BasicAttackRules.Melee(
                CombatTestIds.AttackPowerStat, CombatTestIds.DefenseStat, 1, range);

            return BasicAttackExecutor.Execute(new AttackIntent(a, b), rules);
        }

        [Test]
        public void Target_inside_range_is_hit()
        {
            Assert.That(Attack(1.0f, 2.0f).IsHit, Is.True);
        }

        [Test]
        public void Target_exactly_at_range_is_hit()
        {
            var result = Attack(2.0f, 2.0f);
            Assert.That(result.IsHit, Is.True,
                "Exactly at reach is within reach; squared comparison keeps this exact.");
        }

        [Test]
        public void Target_outside_range_is_rejected()
        {
            var result = Attack(2.001f, 2.0f);
            Assert.That(result.IsHit, Is.False);
            Assert.That(result.Reason, Is.EqualTo(AttackRejection.OutOfRange));
        }

        [Test]
        public void Target_far_away_is_rejected()
        {
            Assert.That(Attack(10000f, 2.0f).Reason, Is.EqualTo(AttackRejection.OutOfRange));
        }

        [Test]
        public void Same_position_is_in_range()
        {
            Assert.That(Attack(0f, 2.0f).IsHit, Is.True);
        }

        [Test]
        public void Zero_range_still_permits_an_overlapping_target()
        {
            Assert.That(Attack(0f, 0f).IsHit, Is.True);
            Assert.That(Attack(0.001f, 0f).Reason, Is.EqualTo(AttackRejection.OutOfRange));
        }

        [Test]
        public void Non_finite_position_is_rejected_rather_than_silently_unattackable()
        {
            var a = new FakeCombatant("a", 1, 100, 100);
            var b = new FakeCombatant("b", 2, 100, 100);
            a.Position = CombatPosition.Zero;
            b.Position = new CombatPosition(float.NaN, 0f, 0f);

            var result = BasicAttackExecutor.Execute(
                new AttackIntent(a, b), CombatTestIds.MeleeRules());

            Assert.That(result.Reason, Is.EqualTo(AttackRejection.InvalidPosition));
            Assert.That(b.ApplyCallCount, Is.EqualTo(0));
        }
    }
}
