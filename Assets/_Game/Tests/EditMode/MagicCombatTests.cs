using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Magic damage: which stats it reads, and what it must never read.
    /// </summary>
    /// <remarks>
    /// <b>Physical and magic differ by two stat ids and nothing else.</b> Every test below
    /// is really one assertion in different clothes: the same subtraction runs either way,
    /// and the only thing an authored damage type changes is which defence is subtracted.
    /// If that ever stops being true, most of this file fails at once, which is the point.
    ///
    /// <b>The regression that matters is the physical one.</b> Adding a second damage type
    /// is exactly the change that could quietly route ordinary attacks through the wrong
    /// defence, so the physical numbers are pinned here against literals rather than against
    /// anything this file computes.
    /// </remarks>
    [TestFixture]
    internal sealed class MagicCombatTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string MagicBolt = "skill.magic_bolt";

        private static readonly DefinitionId Atk = new DefinitionId("stat.atk");
        private static readonly DefinitionId Def = new DefinitionId("stat.def");
        private static readonly DefinitionId Matk = new DefinitionId("stat.matk");
        private static readonly DefinitionId Mdef = new DefinitionId("stat.mdef");

        /// <summary>A combatant with exactly the stats a test gives it.</summary>
        private sealed class Dummy : ICombatant
        {
            private readonly Dictionary<string, int> _stats = new Dictionary<string, int>();

            public Dummy(int health = 1000, CombatTeam team = default)
            {
                CurrentHealth = health;
                MaxHealth = health;
                Team = team.Value == 0 ? new CombatTeam(2) : team;
                CombatantId = new InstanceId("dummy");
            }

            public InstanceId CombatantId { get; }

            public CombatTeam Team { get; }

            public CombatPosition Position => default;

            public int CurrentHealth { get; private set; }

            public int MaxHealth { get; }

            public Dummy With(DefinitionId stat, int value)
            {
                _stats[stat.Value] = value;

                return this;
            }

            public bool TryGetCombatStat(DefinitionId stat, out int value)
            {
                return _stats.TryGetValue(stat.Value, out value);
            }

            public void ApplyHealthDelta(long delta)
            {
                long next = CurrentHealth + delta;
                CurrentHealth = next < 0 ? 0 : (int)Math.Min(next, MaxHealth);
            }
        }

        // ---- the choice itself -----------------------------------------------------------------

        [Test]
        public void MagicIsAnsweredByMagicDefenceAndPhysicalByArmour()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);

            Assert.That(rules.DefenseStatFor(DamageType.Physical), Is.EqualTo(Def));
            Assert.That(rules.DefenseStatFor(DamageType.Magic), Is.EqualTo(Mdef));
        }

        [Test]
        public void AnUnclassifiedDamageTypeIsResistedByArmourRatherThanNothing()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);

            // Every damage effect authored before the type existed deserializes to None, and
            // every one of those was written as an ordinary blow. Resolving None as magic
            // would silently re-balance content nobody edited.
            Assert.That(rules.DefenseStatFor(DamageType.None), Is.EqualTo(Def));
        }

        [Test]
        public void AWorldThatNamesNoMagicDefenceResolvesMagicAgainstNothingRatherThanArmour()
        {
            // The one-argument constructor is the pre-magic world. It must not quietly fall
            // back to physical armour, which would make spells and swords the same thing.
            var rules = new SkillExecutionRules(Def, 1);

            Assert.That(rules.DefenseStatFor(DamageType.Magic).IsValid, Is.False);
            Assert.That(rules.DefenseStatFor(DamageType.Physical), Is.EqualTo(Def));
        }

        // ---- one formula -----------------------------------------------------------------------

        [Test]
        public void BothDamageTypesRunTheSameArithmetic()
        {
            var physical = Effect(DamageType.Physical, flat: 30);
            var magical = Effect(DamageType.Magic, flat: 30);

            var caster = new Dummy();
            var target = new Dummy(health: 500).With(Def, 8).With(Mdef, 8);

            var rules = new SkillExecutionRules(Def, Mdef, 1);

            // Same power, same defence figure: the numbers must be identical, because the
            // subtraction is the same subtraction.
            Assert.That(Damage(physical, caster, target, rules),
                Is.EqualTo(Damage(magical, caster, new Dummy(500).With(Def, 8).With(Mdef, 8),
                    rules)));
        }

        [Test]
        public void ChangingOnlyMagicDefenceLeavesPhysicalDamageAlone()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);
            var blow = Effect(DamageType.Physical, flat: 30);
            var caster = new Dummy();

            int soft = Damage(blow, caster, new Dummy(500).With(Def, 8).With(Mdef, 0), rules);
            int hard = Damage(blow, caster, new Dummy(500).With(Def, 8).With(Mdef, 90), rules);

            Assert.That(hard, Is.EqualTo(soft), "a sword was answered by magic defence");
            Assert.That(hard, Is.EqualTo(30 - 8));
        }

        [Test]
        public void ChangingOnlyArmourLeavesMagicDamageAlone()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);
            var spell = Effect(DamageType.Magic, flat: 30);
            var caster = new Dummy();

            int soft = Damage(spell, caster, new Dummy(500).With(Def, 0).With(Mdef, 8), rules);
            int hard = Damage(spell, caster, new Dummy(500).With(Def, 90).With(Mdef, 8), rules);

            Assert.That(hard, Is.EqualTo(soft), "a spell was answered by armour");
            Assert.That(hard, Is.EqualTo(30 - 8));
        }

        // ---- the attacking side ------------------------------------------------------------------

        [Test]
        public void MagicPowerScalesFromTheStatTheEffectNames()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);

            SkillEffect spell = Effect(DamageType.Magic, flat: 4,
                scaling: new[] { new StatTerm(Matk, 1, 1) });

            var weak = new Dummy().With(Matk, 11).With(Atk, 999);
            var strong = new Dummy().With(Matk, 31).With(Atk, 0);

            Assert.That(Damage(spell, weak, new Dummy(500).With(Mdef, 3), rules),
                Is.EqualTo(4 + 11 - 3));

            Assert.That(Damage(spell, strong, new Dummy(500).With(Mdef, 3), rules),
                Is.EqualTo(4 + 31 - 3),
                "a caster's physical attack must not reach a spell");
        }

        [Test]
        public void AMissingDefenceStatCountsAsNoneRatherThanRefusingTheBlow()
        {
            var rules = new SkillExecutionRules(Def, Mdef, 1);

            // A target that has no magic defence at all -- a monster from before the stat
            // existed. It takes full damage; it does not become immune, and nothing throws.
            int damage = Damage(Effect(DamageType.Magic, flat: 30), new Dummy(),
                new Dummy(500), rules);

            Assert.That(damage, Is.EqualTo(30));
        }

        // ---- what the shipped world actually says ------------------------------------------------

        [Test]
        public void TheShippedCatalogueNamesBothMagicRolesAndBacksThemWithFormulas()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var faults = new List<string>();

            Assert.That(catalogue.Validate(faults), Is.True, string.Join("; ", faults));

            Assert.That(catalogue.MagicAttackStat, Is.EqualTo(Matk));
            Assert.That(catalogue.MagicDefenceStat, Is.EqualTo(Mdef));

            Assert.That(catalogue.MagicAttackStat, Is.Not.EqualTo(catalogue.AttackStat),
                "magic attack and attack must not be the same stat");
            Assert.That(catalogue.MagicDefenceStat, Is.Not.EqualTo(catalogue.DefenceStat),
                "magic defence and armour must not be the same stat");

            DefinitionRegistry<StatDefinition> stats = catalogue.BuildStats();

            Assert.That(stats.TryGet(Matk, out StatDefinition _), Is.True);
            Assert.That(stats.TryGet(Mdef, out StatDefinition _), Is.True);

            Assert.That(catalogue.Formulas.Any(f => f != null && f.DerivedStat == Matk), Is.True);
            Assert.That(catalogue.Formulas.Any(f => f != null && f.DerivedStat == Mdef), Is.True);
        }

        [Test]
        public void ACatalogueThatNamesAMagicRoleWithNoFormulaIsRefused()
        {
            var empty = ScriptableObject.CreateInstance<WorldContentCatalogue>();

            try
            {
                var faults = new List<string>();

                Assert.That(empty.Validate(faults), Is.False);

                // Named as a fault by role, not as a generic "something is wrong".
                Assert.That(faults.Any(f => f.Contains("magic attack")), Is.True,
                    string.Join("; ", faults));
                Assert.That(faults.Any(f => f.Contains("magic defence")), Is.True,
                    string.Join("; ", faults));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(empty);
            }
        }

        [Test]
        public void TheProductionSpellIsMagicAndScalesFromMagicAttack()
        {
            SkillDefinition bolt = Bolt();

            Assert.That(bolt.Levels, Is.Not.Empty);

            foreach (SkillLevelEntry level in bolt.Levels)
            {
                Assert.That(level.Effects, Is.Not.Empty, "rank " + level.Level + " does nothing");

                foreach (SkillEffect effect in level.Effects)
                {
                    if (effect.Kind != SkillEffectKind.Damage) continue;

                    Assert.That(effect.DamageType, Is.EqualTo(DamageType.Magic),
                        "rank " + level.Level + " is not authored as magic");

                    Assert.That(effect.Scaling.Any(t => t.Source == Matk), Is.True,
                        "rank " + level.Level + " does not scale from magic attack");

                    Assert.That(effect.Scaling.Any(t => t.Source == Atk), Is.False,
                        "a spell scaling from physical attack");
                }
            }
        }

        [Test]
        public void TheProductionSpellCostsManaAndKeepsTheOrdinarySkillRules()
        {
            SkillDefinition bolt = Bolt();

            Assert.That(bolt.ResourceType, Is.EqualTo(SkillResourceType.Mana));
            Assert.That(bolt.BaseResourceCost, Is.GreaterThan(0f), "free magic");
            Assert.That(bolt.CooldownSeconds, Is.GreaterThan(0f), "a spell on no cooldown");
            Assert.That(bolt.Range, Is.GreaterThan(0f), "a spell with unlimited reach");
            Assert.That(bolt.TargetType, Is.EqualTo(SkillTargetType.SingleEnemy));

            foreach (SkillLevelEntry level in bolt.Levels)
            {
                Assert.That(level.ResourceCost, Is.GreaterThan(0f));
                Assert.That(level.CooldownSeconds, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void TheProductionSpellIsNotOwnedByAClassAndSoBreaksNoClassRule()
        {
            SkillDefinition bolt = Bolt();

            // The reason a Swordsman may legally learn it: it belongs to no class and no
            // job. Nothing was granted to Swordsman, and no class rule was relaxed.
            Assert.That(bolt.RequiredClass.IsValid, Is.False);
            Assert.That(bolt.RequiredJob.IsValid, Is.False);
            Assert.That(bolt.Prerequisites, Is.Empty);
        }

        [Test]
        public void TheProductionMonsterCanBeHurtByMagicAndResistsItWithItsOwnStat()
        {
            var monster = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                "Assets/_Game/Data/Production/Monsters/monster_training_slime.asset");

            Assert.That(monster, Is.Not.Null);

            Assert.That(monster.TryGetStat(Mdef, out int mdef), Is.True,
                "the production monster has no magic defence");
            Assert.That(mdef, Is.GreaterThan(0));

            Assert.That(monster.TryGetStat(Def, out int def), Is.True);
            Assert.That(def, Is.Not.EqualTo(mdef),
                "identical armour and magic defence would hide a routing bug");
        }

        // ---- the numbers a shipped Swordsman actually gets ------------------------------------------

        [Test]
        public void AStarterSwordsmanGetsRealMagicStatsFromTheProductionFormulas()
        {
            WorldContentCatalogue catalogue = Catalogue();

            DerivedStatsResult derived = Derive(catalogue);

            Assert.That(derived.TryGet(Matk, out int matk), Is.True);
            Assert.That(derived.TryGet(Mdef, out int mdef), Is.True);

            // MATK = 5 + INT 3 x 2, MDEF = INT 3 x 1, from the authored formulas.
            Assert.That(matk, Is.EqualTo(11));
            Assert.That(mdef, Is.EqualTo(3));

            Assert.That(derived.TryGet(Atk, out int atk), Is.True);
            Assert.That(atk, Is.EqualTo(25), "physical attack moved");
        }

        [Test]
        public void PhysicalDamageBetweenTwoStarterSwordsmenIsExactlyWhatItWas()
        {
            // Pinned against literals, not against anything computed here: this is the
            // number 18.8C shipped, and magic must not have moved it by a single point.
            // ATK 25 - DEF 8 = 17.
            Assert.That(BasicDamageFormula.Calculate(25, 8, 1), Is.EqualTo(17));

            WorldContentCatalogue catalogue = Catalogue();
            DerivedStatsResult derived = Derive(catalogue);

            derived.TryGet(Atk, out int atk);
            derived.TryGet(Def, out int def);

            Assert.That(BasicDamageFormula.Calculate(atk, def, 1), Is.EqualTo(17),
                "the shipped physical exchange changed");
        }

        [Test]
        public void MagicAndPhysicalDamageDifferSoARoutingMistakeWouldBeVisible()
        {
            WorldContentCatalogue catalogue = Catalogue();
            DerivedStatsResult derived = Derive(catalogue);

            derived.TryGet(Atk, out int atk);
            derived.TryGet(Def, out int def);
            derived.TryGet(Matk, out int matk);
            derived.TryGet(Mdef, out int mdef);

            int physical = BasicDamageFormula.Calculate(atk, def, 1);
            int magic = BasicDamageFormula.Calculate(4 + matk, mdef, 1);

            Assert.That(magic, Is.Not.EqualTo(physical),
                "if the two are equal, no test in this file can prove they are separate");
        }

        // ---- architecture ----------------------------------------------------------------------------

        [Test]
        public void ThereIsExactlyOneDamageFormulaAndOneCombatPipeline()
        {
            Assembly gameplay = typeof(BasicDamageFormula).Assembly;
            Assembly server = typeof(ChibiFantasy.Server.ServerCombatPipeline).Assembly;

            string[] formulas = gameplay.GetTypes()
                .Where(t => t.Name.Contains("DamageFormula"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(formulas.Length, Is.EqualTo(1), string.Join(", ", formulas));

            string[] pipelines = server.GetTypes()
                .Concat(gameplay.GetTypes())
                .Where(t => t.Name.EndsWith("CombatPipeline") || t.Name.Contains("MagicCombat")
                    || t.Name.Contains("SpellCombat"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(pipelines.Length, Is.EqualTo(1),
                "a second combat pipeline exists: " + string.Join(", ", pipelines));

            string[] executors = gameplay.GetTypes()
                .Where(t => t.Name.EndsWith("SkillExecutor"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(executors.Length, Is.EqualTo(1), string.Join(", ", executors));
        }

        [Test]
        public void ThereIsStillExactlyOneDerivedStatCalculator()
        {
            string[] calculators = typeof(DerivedStatsCalculator).Assembly.GetTypes()
                .Where(t => t.Name.Contains("DerivedStats") && t.Name.EndsWith("Calculator"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(calculators.Length, Is.EqualTo(1), string.Join(", ", calculators));
        }

        [Test]
        public void MagicAttackAndMagicDefenceAreNeverComputedInSkillOrCombatCode()
        {
            // The stat ids belong to content. A literal in code would mean a world could not
            // rename a stat, and would put balance back inside the executable.
            foreach (string path in new[]
            {
                "Assets/_Game/Scripts/Gameplay/SkillExecutor.cs",
                "Assets/_Game/Scripts/Gameplay/BasicDamageFormula.cs",
                "Assets/_Game/Scripts/Gameplay/SkillAmountCalculator.cs",
                "Assets/_Game/Scripts/Server/ServerCombatPipeline.cs",
                "Assets/_Game/Scripts/Server/CombatCommandAuthority.cs",
            })
            {
                string source = System.IO.File.ReadAllText(path);

                Assert.That(source.Contains("\"stat.mdef\""), Is.False, path);
                Assert.That(source.Contains("\"stat.matk\""), Is.False, path);
                Assert.That(source.Contains("\"stat.def\""), Is.False, path);
                Assert.That(source.Contains("\"stat.atk\""), Is.False, path);
            }
        }

        [Test]
        public void NoMagicArithmeticLivesInTheClientAssembly()
        {
            Assembly client = typeof(ChibiFantasy.Client.Combat.CombatPresenter).Assembly;

            Assert.That(client.GetTypes().Any(t => t.Name.Contains("DamageFormula")), Is.False,
                "the client computes damage");

            // What the shipped client does with a spell: draw it.
            string presenter = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/Combat/CombatPresenter.cs");

            Assert.That(presenter.Contains("BasicDamageFormula"), Is.False);
            Assert.That(presenter.Contains("\"stat.mdef\""), Is.False);
            Assert.That(presenter.Contains("\"stat.matk\""), Is.False);
        }

        [Test]
        public void TheLocalCombatDriverStaysOutOfEveryProductionScene()
        {
            // CombatActionDriver runs a combat loop inside the client. That is legitimate
            // for the offline prototype it was written for, and would be a second combat
            // authority anywhere else -- so what matters is not that it exists but that the
            // shipped world never loads it. Its serialized "stat.mdef" default is a content
            // id, not arithmetic; it computes nothing.
            const string DriverGuid = "fe3095b3e96ffaa428c8a55ba21d024d";

            foreach (string scene in new[]
            {
                "Assets/_Game/Scenes/World/World_Server.unity",
                "Assets/_Game/Scenes/Client/GameWorld.unity",
            })
            {
                Assert.That(System.IO.File.ReadAllText(scene).Contains(DriverGuid), Is.False,
                    scene + " loads the client-side combat driver");
            }

            string[] production = UnityEditor.AssetDatabase
                .FindAssets("t:Scene", new[] { "Assets/_Game/Scenes" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/Prototype/"))
                .ToArray();

            Assert.That(production, Is.Not.Empty);

            foreach (string scene in production)
            {
                Assert.That(System.IO.File.ReadAllText(scene).Contains(DriverGuid), Is.False,
                    scene + " loads the client-side combat driver");
            }
        }

        // ---- the wire ---------------------------------------------------------------------------------

        [Test]
        public void ACombatRequestCarriesNoDamageTypeNoStatsAndNoResult()
        {
            Type command = typeof(ChibiFantasy.Server.CombatCommand);

            string[] members = command.GetProperties()
                .Select(p => p.Name.ToLowerInvariant())
                .ToArray();

            // Named exactly, never by substring: "attack" is a substring of
            // ClaimedAttacker, which is who is attacking and is precisely the field a
            // request is supposed to carry. A guard that cannot tell those apart would
            // either fail forever or be quietly deleted.
            foreach (string forbidden in new[]
            {
                "damagetype", "damage", "attackpower", "defense", "defence", "mdef", "matk",
                "magicattack", "magicdefence", "magicdefense", "crit", "coefficient",
                "power", "resultinghealth", "targethealth", "health", "stats",
            })
            {
                Assert.That(members.Contains(forbidden), Is.False,
                    "a client can send '" + forbidden + "'");
            }

            // What it may carry: who, what, which rank, in what order.
            Assert.That(members, Is.EquivalentTo(new[]
            {
                "claimedattacker", "target", "skill", "rank", "sequence", "isskill",
            }));
        }

        [Test]
        public void TheRequestAttackRpcStillCarriesNothingAboutDamage()
        {
            MethodInfo rpc = typeof(ChibiFantasy.Network.CharacterNetworkEntity)
                .GetMethod("RequestAttack",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(rpc, Is.Not.Null);

            foreach (ParameterInfo parameter in rpc.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name.Contains("damage") || name.Contains("mdef")
                    || name.Contains("matk") || name.Contains("magic")
                    || name.Contains("crit"), Is.False,
                    "RequestAttack takes '" + parameter.Name + "'");
            }
        }

        // ---- helpers ------------------------------------------------------------------------------------

        private static int Damage(in SkillEffect effect, ICombatant caster, ICombatant target,
            in SkillExecutionRules rules)
        {
            int power = SkillAmountCalculator.CalculateMagnitude(effect, caster);

            DefinitionId defenceStat = rules.DefenseStatFor(effect.DamageType);

            int defence = defenceStat.IsValid && target.TryGetCombatStat(defenceStat, out int v)
                ? v
                : 0;

            return BasicDamageFormula.Calculate(power, defence, rules.MinimumDamage);
        }

        private static SkillEffect Effect(DamageType type, int flat, StatTerm[] scaling = null)
        {
            return SkillEffect.Damage(flat, ElementType.Neutral, scaling, type);
        }

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "no catalogue at " + CataloguePath);

            return catalogue;
        }

        private static SkillDefinition Bolt()
        {
            Assert.That(Catalogue().BuildSkills()
                .TryGet(new DefinitionId(MagicBolt), out SkillDefinition bolt), Is.True,
                "the shipped catalogue has no magic skill");

            return bolt;
        }

        /// <summary>A starter Swordsman's stats, through the one canonical calculator.</summary>
        private static DerivedStatsResult Derive(WorldContentCatalogue catalogue)
        {
            Assert.That(catalogue.BuildClasses()
                .TryGet(new DefinitionId("class.swordsman"), out ClassDefinition swordsman),
                Is.True);

            var attributes = new CharacterStatsState(new CharacterId("magic-test"));

            foreach (StatValue stat in swordsman.BaseStats)
            {
                attributes.Set(stat.Stat, (int)stat.Value);
            }

            // The one canonical calculator, with no modifiers: base content only.
            return new DerivedStatsCalculator().Calculate(attributes, catalogue.Formulas,
                catalogue.BuildStats(), System.Array.Empty<StatModifier>());
        }
    }
}
