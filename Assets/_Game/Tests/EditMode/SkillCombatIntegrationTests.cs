using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Skill use request is inert data. (STEP 1)</summary>
    public sealed class SkillUseRequestTests
    {
        [Test]
        public void Constructing_a_request_mutates_nothing()
        {
            var caster = new FakePooledCombatant("a", 1, 100, 100, 50, 50);
            var target = new FakePooledCombatant("b", 2, 100, 100);

            var request = new SkillUseRequest(caster, new DefinitionId("skill.x"), target, 2);

            Assert.That(caster.WriteCount, Is.EqualTo(0));
            Assert.That(target.WriteCount, Is.EqualTo(0));
            Assert.That(target.CurrentHealth, Is.EqualTo(100));
            Assert.That(caster.CurrentMana, Is.EqualTo(50));
            Assert.That(request.Rank, Is.EqualTo(2));
        }

        [Test]
        public void Request_names_the_skill_by_id_not_by_object()
        {
            var request = new SkillUseRequest(
                new FakeCombatant("a", 1, 10, 10), new DefinitionId("skill.x"), null);

            Assert.That(request.Skill, Is.EqualTo(new DefinitionId("skill.x")));
            Assert.That(request.Skill.IsValid, Is.True);
        }

        [Test]
        public void Rank_defaults_to_one()
        {
            var request = new SkillUseRequest(
                new FakeCombatant("a", 1, 10, 10), new DefinitionId("skill.x"), null);

            Assert.That(request.Rank, Is.EqualTo(1));
        }

        [Test]
        public void Structural_completeness_reports_missing_caster_or_skill()
        {
            var a = new FakeCombatant("a", 1, 10, 10);

            Assert.That(new SkillUseRequest(null, new DefinitionId("s"), a).IsStructurallyComplete, Is.False);
            Assert.That(new SkillUseRequest(a, DefinitionId.None, a).IsStructurallyComplete, Is.False);
            Assert.That(new SkillUseRequest(a, new DefinitionId("s"), a).IsStructurallyComplete, Is.True);
        }
    }

    /// <summary>SkillTargetType to CombatRelationshipMask. (STEP 3)</summary>
    public sealed class SkillTargetMappingTests
    {
        [TestCase(SkillTargetType.Self, CombatRelationshipMask.Self)]
        [TestCase(SkillTargetType.SingleAlly, CombatRelationshipMask.Friendly)]
        [TestCase(SkillTargetType.SingleEnemy, CombatRelationshipMask.Hostile)]
        public void Supported_target_types_map_deterministically(
            SkillTargetType type, CombatRelationshipMask expected)
        {
            Assert.That(SkillTargetMapping.TryGetPermittedRelationships(type, out var mask), Is.True);
            Assert.That(mask, Is.EqualTo(expected));

            // Same answer every time, not merely once.
            for (int i = 0; i < 100; i++)
            {
                SkillTargetMapping.TryGetPermittedRelationships(type, out var again);
                Assert.That(again, Is.EqualTo(expected));
            }
        }

        [TestCase(SkillTargetType.AreaAroundSelf)]
        [TestCase(SkillTargetType.AreaAtPoint)]
        [TestCase(SkillTargetType.Party)]
        [TestCase(SkillTargetType.None)]
        public void Unsupported_target_types_are_reported_not_guessed(SkillTargetType type)
        {
            Assert.That(SkillTargetMapping.TryGetPermittedRelationships(type, out var mask), Is.False);
            Assert.That(mask, Is.EqualTo(CombatRelationshipMask.None),
                "An unimplemented target type must never silently become hostile.");
        }

        [Test]
        public void Single_ally_excludes_the_caster()
        {
            SkillTargetMapping.TryGetPermittedRelationships(SkillTargetType.SingleAlly, out var mask);

            Assert.That(CombatTeams.Permits(mask, CombatRelationship.Self), Is.False,
                "An ally is somebody else; a single-ally heal must not target the caster.");
            Assert.That(CombatTeams.Permits(mask, CombatRelationship.Friendly), Is.True);
        }

        [Test]
        public void Self_skill_resolves_to_the_caster_even_when_another_target_is_supplied()
        {
            var caster = new FakeCombatant("a", 1, 10, 10);
            var other = new FakeCombatant("b", 1, 10, 10);

            Assert.That(SkillTargetMapping.ResolveTarget(SkillTargetType.Self, caster, other),
                Is.SameAs(caster), "A self-only skill cannot be redirected.");
            Assert.That(SkillTargetMapping.ResolveTarget(SkillTargetType.SingleEnemy, caster, other),
                Is.SameAs(other));
        }

        [Test]
        public void Only_single_target_types_require_an_explicit_target()
        {
            Assert.That(SkillTargetMapping.RequiresExplicitTarget(SkillTargetType.Self), Is.False);
            Assert.That(SkillTargetMapping.RequiresExplicitTarget(SkillTargetType.SingleAlly), Is.True);
            Assert.That(SkillTargetMapping.RequiresExplicitTarget(SkillTargetType.SingleEnemy), Is.True);
        }
    }

    /// <summary>Runtime skill-use validation against real Phase 06 state. (STEP 2)</summary>
    internal sealed class SkillUseValidationTests : SkillTestBase
    {
        private const string Blast = "skill.blast";

        private CharacterSkillsState _learned;
        private FakePooledCombatant _caster;
        private FakePooledCombatant _enemy;
        private FakePooledCombatant _ally;

        private SkillUseContext Context(int level = 10, SkillCooldownState cooldowns = null)
        {
            return new SkillUseContext(Skills, _learned, level, cooldowns);
        }

        private void Prepare(SkillTargetType targetType = SkillTargetType.SingleEnemy,
            float cost = 0f, float range = 0f, float cooldown = 0f,
            int requiredLevel = 1, int maxLevel = 3)
        {
            var skill = AddSkill(Blast, maxLevel: maxLevel, range: range, levels: new[]
            {
                Level(1, requiredLevel, cost, cooldown, new[] { SkillEffect.Damage(10, ElementType.Neutral) }),
                Level(2, requiredLevel, cost, cooldown, new[] { SkillEffect.Damage(20, ElementType.Neutral) }),
                Level(3, requiredLevel + 20, cost, cooldown, new[] { SkillEffect.Damage(30, ElementType.Neutral) })
            });
            SetPrivate(skill, "_targetType", targetType);

            _learned = new CharacterSkillsState(new CharacterId("c1"));
            _caster = new FakePooledCombatant("caster", 1, 100, 100, 50, 50);
            _enemy = new FakePooledCombatant("enemy", 2, 100, 100);
            _ally = new FakePooledCombatant("ally", 1, 100, 100);
        }

        private SkillUseEligibility Evaluate(int rank = 1, ICombatant target = null,
            int level = 10, SkillCooldownState cooldowns = null)
        {
            return SkillUseValidator.Evaluate(
                new SkillUseRequest(_caster, new DefinitionId(Blast), target ?? _enemy, rank),
                Context(level, cooldowns));
        }

        [Test]
        public void Unlearned_skill_is_rejected()
        {
            Prepare();
            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.NotLearned));
        }

        [Test]
        public void Learned_skill_is_accepted()
        {
            Prepare();
            _learned.Learn(new DefinitionId(Blast));

            var result = Evaluate();

            Assert.That(result.IsAllowed, Is.True);
            Assert.That(result.Rank, Is.EqualTo(1));
            Assert.That(result.Relationship, Is.EqualTo(CombatRelationship.Hostile));
            Assert.That(result.ResolvedTarget, Is.SameAs(_enemy));
        }

        [Test]
        public void Rank_above_the_learned_rank_is_rejected()
        {
            Prepare();
            _learned.Learn(new DefinitionId(Blast));   // rank 1

            Assert.That(Evaluate(rank: 2).Reason, Is.EqualTo(SkillUseRejection.RankNotAvailable));
            Assert.That(Evaluate(rank: 0).Reason, Is.EqualTo(SkillUseRejection.RankNotAvailable));
            Assert.That(Evaluate(rank: -1).Reason, Is.EqualTo(SkillUseRejection.RankNotAvailable));
        }

        [Test]
        public void Required_character_level_is_enforced_per_rank()
        {
            Prepare(requiredLevel: 5);
            _learned.Learn(new DefinitionId(Blast));

            Assert.That(Evaluate(level: 4).Reason, Is.EqualTo(SkillUseRejection.LevelTooLow));
            Assert.That(Evaluate(level: 5).IsAllowed, Is.True);
        }

        [Test]
        public void Missing_definition_is_rejected()
        {
            Prepare();
            _learned.Learn(new DefinitionId("skill.nonexistent"));

            var result = SkillUseValidator.Evaluate(
                new SkillUseRequest(_caster, new DefinitionId("skill.nonexistent"), _enemy),
                Context());

            Assert.That(result.Reason, Is.EqualTo(SkillUseRejection.UnknownSkill));
        }

        [Test]
        public void Missing_caster_or_skill_id_is_rejected()
        {
            Prepare();

            Assert.That(SkillUseValidator.Evaluate(
                new SkillUseRequest(null, new DefinitionId(Blast), _enemy), Context()).Reason,
                Is.EqualTo(SkillUseRejection.NoCaster));

            Assert.That(SkillUseValidator.Evaluate(
                new SkillUseRequest(_caster, DefinitionId.None, _enemy), Context()).Reason,
                Is.EqualTo(SkillUseRejection.NoSkill));
        }

        [Test]
        public void Missing_target_is_rejected_for_a_targeted_skill()
        {
            Prepare();
            _learned.Learn(new DefinitionId(Blast));

            var result = SkillUseValidator.Evaluate(
                new SkillUseRequest(_caster, new DefinitionId(Blast), null), Context());

            Assert.That(result.Reason, Is.EqualTo(SkillUseRejection.NoTarget));
        }

        [Test]
        public void Self_skill_needs_no_target_and_resolves_to_the_caster()
        {
            Prepare(SkillTargetType.Self);
            _learned.Learn(new DefinitionId(Blast));

            var result = SkillUseValidator.Evaluate(
                new SkillUseRequest(_caster, new DefinitionId(Blast), null), Context());

            Assert.That(result.IsAllowed, Is.True);
            Assert.That(result.ResolvedTarget, Is.SameAs(_caster));
            Assert.That(result.Relationship, Is.EqualTo(CombatRelationship.Self));
        }

        [Test]
        public void Friendly_target_is_rejected_by_an_enemy_skill_and_accepted_by_an_ally_skill()
        {
            Prepare(SkillTargetType.SingleEnemy);
            _learned.Learn(new DefinitionId(Blast));
            Assert.That(Evaluate(target: _ally).Reason,
                Is.EqualTo(SkillUseRejection.RelationshipNotPermitted));

            TearDown();
            SetUp();
            Prepare(SkillTargetType.SingleAlly);
            _learned.Learn(new DefinitionId(Blast));
            Assert.That(Evaluate(target: _ally).IsAllowed, Is.True);
            Assert.That(Evaluate(target: _enemy).Reason,
                Is.EqualTo(SkillUseRejection.RelationshipNotPermitted));
        }

        [Test]
        public void Dead_target_and_dead_caster_are_rejected()
        {
            Prepare();
            _learned.Learn(new DefinitionId(Blast));

            _enemy.ApplyHealthDelta(-100);
            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.TargetDead));

            _caster.ApplyHealthDelta(-100);
            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.CasterDead),
                "The caster is judged before the target.");
        }

        [Test]
        public void Unsupported_target_type_is_rejected_by_name()
        {
            Prepare(SkillTargetType.AreaAtPoint);
            _learned.Learn(new DefinitionId(Blast));

            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.TargetTypeUnsupported));
        }

        [Test]
        public void Range_is_enforced_when_authored_and_ignored_when_zero()
        {
            Prepare(range: 2f);
            _learned.Learn(new DefinitionId(Blast));

            _caster.Position = CombatPosition.Zero;
            _enemy.Position = new CombatPosition(2f, 0f, 0f);
            Assert.That(Evaluate().IsAllowed, Is.True, "Exactly at range is in range.");

            _enemy.Position = new CombatPosition(2.5f, 0f, 0f);
            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.OutOfRange));

            TearDown();
            SetUp();
            Prepare(range: 0f);
            _learned.Learn(new DefinitionId(Blast));
            _enemy.Position = new CombatPosition(9999f, 0f, 0f);
            Assert.That(Evaluate().IsAllowed, Is.True,
                "A zero authored range means unspecified, not touching-only.");
        }

        [Test]
        public void Insufficient_resource_is_rejected_and_a_poolless_caster_is_named()
        {
            Prepare(cost: 60f);
            _learned.Learn(new DefinitionId(Blast));

            Assert.That(Evaluate().Reason, Is.EqualTo(SkillUseRejection.InsufficientResource),
                "50 mana cannot pay a cost of 60.");

            // A combatant with no pools at all is a different, named failure.
            var poolless = new FakeCombatant("nopool", 1, 100, 100);
            var result = SkillUseValidator.Evaluate(
                new SkillUseRequest(poolless, new DefinitionId(Blast), _enemy), Context());

            Assert.That(result.Reason, Is.EqualTo(SkillUseRejection.ResourcePoolUnavailable));
        }

        [Test]
        public void Fractional_cost_rounds_up_so_a_skill_never_costs_less_than_authored()
        {
            Assert.That(SkillUseValidator.ToWholeCost(2.5f), Is.EqualTo(3));
            Assert.That(SkillUseValidator.ToWholeCost(2.0f), Is.EqualTo(2));
            Assert.That(SkillUseValidator.ToWholeCost(0.1f), Is.EqualTo(1));
            Assert.That(SkillUseValidator.ToWholeCost(0f), Is.EqualTo(0));
            Assert.That(SkillUseValidator.ToWholeCost(-5f), Is.EqualTo(0));
            Assert.That(SkillUseValidator.ToWholeCost(float.NaN), Is.EqualTo(0));
        }

        [Test]
        public void Cooldown_blocks_and_then_releases()
        {
            Prepare(cooldown: 5f);
            _learned.Learn(new DefinitionId(Blast));

            var cooldowns = new SkillCooldownState();
            Assert.That(Evaluate(cooldowns: cooldowns).IsAllowed, Is.True);

            cooldowns.Begin(new DefinitionId(Blast), 5f);
            Assert.That(Evaluate(cooldowns: cooldowns).Reason, Is.EqualTo(SkillUseRejection.OnCooldown));

            cooldowns.Advance(5f);
            Assert.That(Evaluate(cooldowns: cooldowns).IsAllowed, Is.True);
        }

        [Test]
        public void A_null_cooldown_state_means_cooldowns_are_not_tracked()
        {
            Prepare(cooldown: 99f);
            _learned.Learn(new DefinitionId(Blast));

            Assert.That(Evaluate(cooldowns: null).IsAllowed, Is.True);
        }

        [Test]
        public void Validation_never_mutates_anything()
        {
            Prepare(cost: 10f);
            _learned.Learn(new DefinitionId(Blast));

            Revision skillsBefore = _learned.Revision;
            int manaBefore = _caster.CurrentMana;

            for (int i = 0; i < 50; i++)
            {
                Evaluate();
                Evaluate(rank: 99);
                Evaluate(target: _ally);
            }

            Assert.That(_caster.CurrentMana, Is.EqualTo(manaBefore));
            Assert.That(_caster.WriteCount, Is.EqualTo(0));
            Assert.That(_enemy.WriteCount, Is.EqualTo(0));
            Assert.That(_learned.Revision, Is.EqualTo(skillsBefore));
        }
    }
}
