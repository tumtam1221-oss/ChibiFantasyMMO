using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Data-driven physical and magic damage. (STEP 17)
    /// </summary>
    /// <remarks>
    /// Every case here changes only <em>authored data</em> -- a base amount, a scaling
    /// term, a stat value, a damage type -- and asserts the runtime answer moves with it.
    /// No test changes gameplay code, which is the property the phase exists to establish.
    /// </remarks>
    internal sealed class CombatDamageMatrixTests : SkillTestBase
    {
        private const string Sk = "skill.matrix";
        private const string PhysPower = "stat.p_power";
        private const string MagicPower = "stat.m_power";
        private const string Def = "stat.def";
        private const string Mdef = "stat.mdef";

        private CharacterSkillsState _learned;
        private FakePooledCombatant _caster;
        private FakePooledCombatant _target;

        private static SkillExecutionRules Rules(int floor = 0)
        {
            return new SkillExecutionRules(
                new DefinitionId(Def), new DefinitionId(Mdef), floor);
        }

        /// <summary>Authors one damage skill from the supplied parameters and runs it.</summary>
        private int Damage(int baseAmount, DamageType type, string scalingStat = null,
            int numerator = 1, int denominator = 1,
            int casterPhys = 0, int casterMagic = 0, int targetDef = 0, int targetMdef = 0,
            int floor = 0)
        {
            TearDown();
            SetUp();

            StatTerm[] scaling = scalingStat == null
                ? null
                : new[] { new StatTerm(new DefinitionId(scalingStat), numerator, denominator) };

            var effect = SkillEffect.Damage(baseAmount, ElementType.Neutral, scaling, type);
            var skill = AddSkill(Sk, maxLevel: 1,
                levels: new[] { Level(1, 1, 0f, 0f, new[] { effect }) });
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(skill, "_resourceType", SkillResourceType.None);

            _learned = new CharacterSkillsState(new CharacterId("c"));
            _learned.Learn(new DefinitionId(Sk));

            _caster = new FakePooledCombatant("caster", 1, 100, 100)
                .WithStat(PhysPower, casterPhys)
                .WithStat(MagicPower, casterMagic);
            _target = new FakePooledCombatant("target", 2, 100000, 100000)
                .WithStat(Def, targetDef)
                .WithStat(Mdef, targetMdef);

            SkillExecutionResult result = SkillExecutor.Execute(
                new SkillUseRequest(_caster, new DefinitionId(Sk), _target),
                new SkillUseContext(Skills, _learned, 10),
                Rules(floor));

            Assert.That(result.IsExecuted, Is.True, "Setup should always produce a legal use.");
            return result.Effects[0].Amount;
        }

        // ---------------- PHYSICAL ----------------

        [Test]
        public void Physical_base_damage_is_data_driven()
        {
            Assert.That(Damage(10, DamageType.Physical), Is.EqualTo(10));
            Assert.That(Damage(50, DamageType.Physical), Is.EqualTo(50));
            Assert.That(Damage(250, DamageType.Physical), Is.EqualTo(250));
        }

        [Test]
        public void Physical_power_ratio_is_data_driven()
        {
            // Same stat value, different authored ratio.
            Assert.That(Damage(0, DamageType.Physical, PhysPower, 1, 1, casterPhys: 100), Is.EqualTo(100));
            Assert.That(Damage(0, DamageType.Physical, PhysPower, 1, 2, casterPhys: 100), Is.EqualTo(50));
            Assert.That(Damage(0, DamageType.Physical, PhysPower, 3, 2, casterPhys: 100), Is.EqualTo(150));
        }

        [Test]
        public void Attacker_physical_power_changes_physical_damage()
        {
            Assert.That(Damage(10, DamageType.Physical, PhysPower, 1, 1, casterPhys: 0), Is.EqualTo(10));
            Assert.That(Damage(10, DamageType.Physical, PhysPower, 1, 1, casterPhys: 40), Is.EqualTo(50));
            Assert.That(Damage(10, DamageType.Physical, PhysPower, 1, 1, casterPhys: 90), Is.EqualTo(100));
        }

        [Test]
        public void Target_DEF_reduces_physical_damage()
        {
            Assert.That(Damage(100, DamageType.Physical, targetDef: 0), Is.EqualTo(100));
            Assert.That(Damage(100, DamageType.Physical, targetDef: 30), Is.EqualTo(70));
            Assert.That(Damage(100, DamageType.Physical, targetDef: 99), Is.EqualTo(1));
        }

        [Test]
        public void Target_MDEF_does_not_reduce_physical_damage()
        {
            Assert.That(Damage(100, DamageType.Physical, targetDef: 0, targetMdef: 9999),
                Is.EqualTo(100), "Magic defence must never answer a physical blow.");
        }

        // ---------------- MAGIC ----------------

        [Test]
        public void Magic_base_damage_is_data_driven()
        {
            Assert.That(Damage(10, DamageType.Magic), Is.EqualTo(10));
            Assert.That(Damage(75, DamageType.Magic), Is.EqualTo(75));
            Assert.That(Damage(300, DamageType.Magic), Is.EqualTo(300));
        }

        [Test]
        public void Magic_power_ratio_is_data_driven()
        {
            Assert.That(Damage(0, DamageType.Magic, MagicPower, 1, 1, casterMagic: 80), Is.EqualTo(80));
            Assert.That(Damage(0, DamageType.Magic, MagicPower, 1, 4, casterMagic: 80), Is.EqualTo(20));
            Assert.That(Damage(0, DamageType.Magic, MagicPower, 5, 2, casterMagic: 80), Is.EqualTo(200));
        }

        [Test]
        public void Attacker_magic_power_changes_magic_damage()
        {
            Assert.That(Damage(20, DamageType.Magic, MagicPower, 1, 1, casterMagic: 0), Is.EqualTo(20));
            Assert.That(Damage(20, DamageType.Magic, MagicPower, 1, 1, casterMagic: 30), Is.EqualTo(50));
            Assert.That(Damage(20, DamageType.Magic, MagicPower, 1, 1, casterMagic: 130), Is.EqualTo(150));
        }

        [Test]
        public void Target_MDEF_reduces_magic_damage()
        {
            Assert.That(Damage(100, DamageType.Magic, targetMdef: 0), Is.EqualTo(100));
            Assert.That(Damage(100, DamageType.Magic, targetMdef: 40), Is.EqualTo(60));
            Assert.That(Damage(100, DamageType.Magic, targetMdef: 95), Is.EqualTo(5));
        }

        [Test]
        public void Target_DEF_does_not_reduce_magic_damage()
        {
            Assert.That(Damage(100, DamageType.Magic, targetDef: 9999, targetMdef: 0),
                Is.EqualTo(100), "Armour must never answer a spell.");
        }

        // ---------------- CROSS-CHECK ----------------

        [Test]
        public void High_DEF_blunts_physical_while_the_same_target_takes_full_magic()
        {
            int phys = Damage(100, DamageType.Physical, targetDef: 90, targetMdef: 0);
            int magic = Damage(100, DamageType.Magic, targetDef: 90, targetMdef: 0);

            Assert.That(phys, Is.EqualTo(10));
            Assert.That(magic, Is.EqualTo(100));
        }

        [Test]
        public void High_MDEF_blunts_magic_while_the_same_target_takes_full_physical()
        {
            int magic = Damage(100, DamageType.Magic, targetDef: 0, targetMdef: 90);
            int phys = Damage(100, DamageType.Physical, targetDef: 0, targetMdef: 90);

            Assert.That(magic, Is.EqualTo(10));
            Assert.That(phys, Is.EqualTo(100));
        }

        [Test]
        public void Unclassified_damage_resolves_as_physical()
        {
            Assert.That(Damage(100, DamageType.None, targetDef: 30, targetMdef: 0), Is.EqualTo(70));
            Assert.That(Damage(100, DamageType.None, targetDef: 0, targetMdef: 30), Is.EqualTo(100));
        }

        [Test]
        public void The_defence_stat_choice_is_made_in_one_place()
        {
            var rules = new SkillExecutionRules(
                new DefinitionId(Def), new DefinitionId(Mdef), 0);

            Assert.That(rules.DefenseStatFor(DamageType.Physical), Is.EqualTo(new DefinitionId(Def)));
            Assert.That(rules.DefenseStatFor(DamageType.Magic), Is.EqualTo(new DefinitionId(Mdef)));
            Assert.That(rules.DefenseStatFor(DamageType.None), Is.EqualTo(new DefinitionId(Def)));
        }

        // ---------------- BOUNDARIES ----------------

        [Test]
        public void Damage_is_never_negative_however_the_data_is_authored()
        {
            Assert.That(Damage(10, DamageType.Physical, targetDef: 9999), Is.EqualTo(0));
            Assert.That(Damage(10, DamageType.Magic, targetMdef: 9999), Is.EqualTo(0));
            Assert.That(Damage(-500, DamageType.Physical), Is.EqualTo(0),
                "A negative authored amount cannot become a heal.");
        }

        [Test]
        public void A_negative_defence_stat_counts_as_zero_rather_than_a_bonus()
        {
            Assert.That(Damage(100, DamageType.Physical, targetDef: -100), Is.EqualTo(100));
            Assert.That(Damage(100, DamageType.Magic, targetMdef: -100), Is.EqualTo(100));
        }

        [Test]
        public void The_authored_floor_applies_to_both_damage_types()
        {
            Assert.That(Damage(1, DamageType.Physical, targetDef: 9999, floor: 5), Is.EqualTo(5));
            Assert.That(Damage(1, DamageType.Magic, targetMdef: 9999, floor: 5), Is.EqualTo(5));
        }

        [Test]
        public void Extreme_values_do_not_overflow_or_produce_nonsense()
        {
            Assert.That(Damage(int.MaxValue, DamageType.Physical), Is.EqualTo(int.MaxValue));
            Assert.That(Damage(int.MaxValue, DamageType.Magic, targetMdef: int.MaxValue), Is.EqualTo(0));
            Assert.That(Damage(0, DamageType.Magic, MagicPower, 1000, 1, casterMagic: int.MaxValue),
                Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void The_same_data_always_gives_the_same_number()
        {
            for (int i = 0; i < 50; i++)
            {
                Assert.That(Damage(77, DamageType.Physical, PhysPower, 3, 4,
                    casterPhys: 61, targetDef: 19), Is.EqualTo(103));
            }
        }

        // ---------------- PER-SKILL AND PER-LEVEL ----------------

        [Test]
        public void Two_skills_may_carry_completely_different_values()
        {
            var a = AddSkill("skill.a", maxLevel: 1, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(15, ElementType.Neutral, null, DamageType.Physical)
                })
            });
            SetPrivate(a, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(a, "_resourceType", SkillResourceType.None);

            var b = AddSkill("skill.b", maxLevel: 1, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(200, ElementType.Neutral, null, DamageType.Magic)
                })
            });
            SetPrivate(b, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(b, "_resourceType", SkillResourceType.None);

            var learned = new CharacterSkillsState(new CharacterId("c"));
            learned.Learn(new DefinitionId("skill.a"));
            learned.Learn(new DefinitionId("skill.b"));

            var caster = new FakePooledCombatant("c", 1, 100, 100);
            var target = new FakePooledCombatant("t", 2, 100000, 100000)
                .WithStat(Def, 10).WithStat(Mdef, 50);
            var context = new SkillUseContext(Skills, learned, 10);

            int aDamage = SkillExecutor.Execute(
                new SkillUseRequest(caster, new DefinitionId("skill.a"), target),
                context, Rules()).Effects[0].Amount;

            int bDamage = SkillExecutor.Execute(
                new SkillUseRequest(caster, new DefinitionId("skill.b"), target),
                context, Rules()).Effects[0].Amount;

            Assert.That(aDamage, Is.EqualTo(5), "15 physical - 10 DEF.");
            Assert.That(bDamage, Is.EqualTo(150), "200 magic - 50 MDEF.");
        }

        [Test]
        public void Two_levels_of_one_skill_may_carry_completely_different_values()
        {
            var skill = AddSkill(Sk, maxLevel: 2, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(10, ElementType.Neutral, null, DamageType.Physical)
                }),
                Level(2, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(400, ElementType.Neutral, null, DamageType.Magic)
                })
            });
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(skill, "_resourceType", SkillResourceType.None);

            var learned = new CharacterSkillsState(new CharacterId("c"));
            learned.Learn(new DefinitionId(Sk));
            learned.SetRank(new DefinitionId(Sk), 2);

            var caster = new FakePooledCombatant("c", 1, 100, 100);
            var target = new FakePooledCombatant("t", 2, 100000, 100000)
                .WithStat(Def, 4).WithStat(Mdef, 100);
            var context = new SkillUseContext(Skills, learned, 10);

            int r1 = SkillExecutor.Execute(
                new SkillUseRequest(caster, new DefinitionId(Sk), target, 1),
                context, Rules()).Effects[0].Amount;

            int r2 = SkillExecutor.Execute(
                new SkillUseRequest(caster, new DefinitionId(Sk), target, 2),
                context, Rules()).Effects[0].Amount;

            Assert.That(r1, Is.EqualTo(6), "Rank 1: 10 physical - 4 DEF.");
            Assert.That(r2, Is.EqualTo(300), "Rank 2: 400 magic - 100 MDEF. Different type and value.");
        }
    }
}
