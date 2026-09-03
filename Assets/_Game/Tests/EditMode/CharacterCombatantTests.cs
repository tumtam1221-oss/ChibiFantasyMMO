using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Combat driving a real <see cref="Character"/>.
    /// </summary>
    /// <remarks>
    /// The point of this fixture is not that attacks work -- that is covered against a
    /// standalone combatant elsewhere -- but that they work <em>through the existing
    /// state</em>: the same resource object, the same clamping, the same revision counter.
    /// It inherits the character-creation fixtures so the character under test is built by
    /// the production service from authored content, not hand-assembled for combat.
    /// </remarks>
    internal sealed class CharacterCombatantTests : CharacterCreationTestBase
    {
        private Character MakeCharacter(string name)
        {
            new CharacterCreationService().TryCreate(
                Input(Swordsman, CharacterGender.Male, name), Content(),
                out Character character, out _);
            return character;
        }

        private DerivedStatsResult Derive(Character character)
        {
            return new DerivedStatsCalculator().Calculate(
                character.Stats, Formulas, Stats, new List<StatModifier>());
        }

        private CharacterCombatant Combatant(string name, int team)
        {
            Character character = MakeCharacter(name);
            DerivedStatsResult derived = Derive(character);
            ResourceLimits limits = ResourceLimits.From(
                derived, new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            return new CharacterCombatant(character, derived, limits, new CombatTeam(team));
        }

        [Test]
        public void Combatant_reads_health_from_the_character_resource_state()
        {
            CharacterCombatant c = Combatant("A", 1);

            Assert.That(c.CurrentHealth, Is.EqualTo(c.Character.Resources.CurrentHealth));
            Assert.That(c.MaxHealth, Is.EqualTo(c.Limits.MaxHealth));
            Assert.That(c.CurrentHealth, Is.GreaterThan(0), "Created characters start alive.");
        }

        [Test]
        public void Combatant_identity_is_the_character_identity_not_a_new_one()
        {
            CharacterCombatant c = Combatant("A", 1);

            Assert.That(c.CombatantId.Value,
                Is.EqualTo(c.Character.Identity.CharacterId.Value),
                "Minting a second identity would break self-targeting.");
        }

        [Test]
        public void Damage_flows_into_the_existing_resource_state_and_bumps_its_revision()
        {
            CharacterCombatant attacker = Combatant("A", 1);
            CharacterCombatant target = Combatant("B", 2);

            int before = target.Character.Resources.CurrentHealth;
            Revision revisionBefore = target.Character.Resources.Revision;

            var rules = BasicAttackRules.Melee(
                new DefinitionId(Str), new DefinitionId(Vit), 1, 100f);

            AttackResult result = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);

            Assert.That(result.IsHit, Is.True);
            Assert.That(target.Character.Resources.CurrentHealth, Is.EqualTo(before - result.HealthLost));
            Assert.That(target.Character.Resources.Revision.IsNewerThan(revisionBefore), Is.True,
                "Combat must go through the existing mutation path, revision included.");
        }

        [Test]
        public void A_blow_that_changes_nothing_does_not_advance_the_revision()
        {
            CharacterCombatant attacker = Combatant("A", 1);
            CharacterCombatant target = Combatant("B", 2);

            // Zero damage: no attack stat is read, and the floor is zero.
            var rules = BasicAttackRules.Melee(
                new DefinitionId("stat.does.not.exist"), new DefinitionId(Vit), 0, 100f);

            Revision before = target.Character.Resources.Revision;
            AttackResult result = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Damage, Is.EqualTo(0));
            Assert.That(target.Character.Resources.Revision, Is.EqualTo(before),
                "The existing rule that a change of nothing is not a change is inherited.");
        }

        [Test]
        public void Health_clamps_at_zero_through_the_character_state()
        {
            CharacterCombatant attacker = Combatant("A", 1);
            CharacterCombatant target = Combatant("B", 2);

            var rules = BasicAttackRules.Melee(
                new DefinitionId(Str), new DefinitionId(Vit), 999999, 100f);

            AttackResult result = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);

            Assert.That(result.TargetDied, Is.True);
            Assert.That(target.CurrentHealth, Is.EqualTo(0));
            Assert.That(target.Character.Resources.CurrentHealth, Is.EqualTo(0),
                "No negative health ever reaches the character.");

            AttackResult again = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);
            Assert.That(again.Reason, Is.EqualTo(AttackRejection.TargetDead));
        }

        [Test]
        public void Combat_stats_come_from_the_existing_stat_system()
        {
            CharacterCombatant c = Combatant("A", 1);

            Assert.That(c.TryGetCombatStat(new DefinitionId(Str), out int str), Is.True);
            Assert.That(str, Is.EqualTo(c.Character.Stats.GetOrDefault(new DefinitionId(Str), -1)));

            Assert.That(c.TryGetCombatStat(new DefinitionId(MaxHp), out int maxHp), Is.True,
                "Derived stats are visible to combat too.");
            Assert.That(maxHp, Is.EqualTo(c.MaxHealth));

            Assert.That(c.TryGetCombatStat(new DefinitionId("stat.nonexistent"), out _), Is.False,
                "Absent is reported as absent, not as zero.");
        }

        [Test]
        public void Recomputed_limits_clamp_through_the_existing_rule()
        {
            CharacterCombatant c = Combatant("A", 1);
            int full = c.CurrentHealth;

            c.SetLimits(new ResourceLimits(full / 2, 0));

            Assert.That(c.CurrentHealth, Is.EqualTo(full / 2),
                "A dropped ceiling clamps exactly as CharacterResourceState.ClampTo does.");
            Assert.That(c.MaxHealth, Is.EqualTo(full / 2));
        }

        [Test]
        public void The_same_rules_drive_a_character_and_a_non_character_combatant()
        {
            CharacterCombatant player = Combatant("A", 1);

            // Stand-in for a future monster: a different implementation entirely.
            var monster = new FakeCombatant("monster", 2, 40, 40)
                .WithStat(CombatTestIds.Defense, 0);

            var rules = BasicAttackRules.Melee(
                new DefinitionId(Str), CombatTestIds.DefenseStat, 1, 100f);

            AttackResult playerHitsMonster = BasicAttackExecutor.Execute(
                new AttackIntent(player, monster), rules);

            Assert.That(playerHitsMonster.IsHit, Is.True);
            Assert.That(monster.CurrentHealth, Is.LessThan(40));

            // ...and the same executor in the other direction, with no second code path.
            var reverseRules = BasicAttackRules.Melee(
                CombatTestIds.AttackPowerStat, new DefinitionId(Vit), 1, 100f);
            monster.WithStat(CombatTestIds.AttackPower, 5);

            AttackResult monsterHitsPlayer = BasicAttackExecutor.Execute(
                new AttackIntent(monster, player), reverseRules);

            Assert.That(monsterHitsPlayer.IsHit, Is.True);
            Assert.That(player.CurrentHealth, Is.LessThan(player.MaxHealth));
        }

        [Test]
        public void Two_characters_fight_through_one_shared_architecture()
        {
            CharacterCombatant male = Combatant("Male", 1);
            CharacterCombatant female = Combatant("Female", 2);

            var rules = BasicAttackRules.Melee(
                new DefinitionId(Str), new DefinitionId(Vit), 1, 100f);

            AttackResult maleAttacks = BasicAttackExecutor.Execute(
                new AttackIntent(male, female), rules);
            AttackResult femaleAttacks = BasicAttackExecutor.Execute(
                new AttackIntent(female, male), rules);

            Assert.That(maleAttacks.IsHit, Is.True);
            Assert.That(femaleAttacks.IsHit, Is.True);
            Assert.That(maleAttacks.Damage, Is.EqualTo(femaleAttacks.Damage),
                "Identical stats give identical damage: the architecture is shared, "
                + "and only data would make them differ.");
        }

        [Test]
        public void Position_is_combat_runtime_state_and_does_not_touch_the_aggregate()
        {
            CharacterCombatant c = Combatant("A", 1);
            Revision before = c.Character.Resources.Revision;

            c.Position = new CombatPosition(3f, 0f, 4f);

            Assert.That(c.Position.SqrDistanceTo(CombatPosition.Zero), Is.EqualTo(25f));
            Assert.That(c.Character.Resources.Revision, Is.EqualTo(before),
                "Moving is not a change to character state.");
        }
    }
}
