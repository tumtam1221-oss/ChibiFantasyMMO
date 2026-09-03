using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Equipment reaching effective stats and then combat. (STEPS 15, 16, 26)
    /// </summary>
    /// <remarks>
    /// Inherits the character-creation fixtures so the character under test is built by the
    /// production service from authored content, and the stats flow through the real
    /// <see cref="DerivedStatsCalculator"/>. Nothing here computes a stat or a damage
    /// figure of its own; that is the whole point of the phase.
    /// </remarks>
    internal sealed class EquipmentStatIntegrationTests : CharacterCreationTestBase
    {
        private const string SwordId = "equip.stat.sword";
        private const string RingId = "equip.stat.ring";

        private DefinitionRegistry<ItemDefinition> _items;
        private List<Object> _created;

        private Character MakeCharacter()
        {
            new CharacterCreationService().TryCreate(
                Input(Swordsman), Content(), out Character character, out _);
            return character;
        }

        /// <summary>Effective stats: base stats plus whatever is worn, through the real calculator.</summary>
        private DerivedStatsResult Derive(Character character, CharacterEquipmentState worn)
        {
            List<StatModifier> modifiers = worn == null
                ? new List<StatModifier>()
                : worn.CollectModifiers(_items);

            return new DerivedStatsCalculator().Calculate(
                character.Stats, Formulas, Stats, modifiers);
        }

        private EquipmentDefinition AddGear(string id, EquipmentSlot slot, string stat, float amount)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.Equipment + ",\"_slot\":" + (int)slot
                + ",\"_levelRequirement\":0}", definition);

            definition.GetType()
                .GetField("_baseStatModifiers", System.Reflection.BindingFlags.Instance
                                                | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, new[]
                {
                    new StatModifier(new DefinitionId(stat), StatModifierKind.Flat, amount)
                });

            _created.Add(definition);
            _items.Register(definition);
            return definition;
        }

        private void Prepare()
        {
            _items = new DefinitionRegistry<ItemDefinition>();
            _created = new List<Object>();

            // Equipment modifiers target a DERIVED stat, which is where the existing
            // DerivedStatsCalculator applies them. See the phase report for why.
            AddGear(SwordId, EquipmentSlot.MainHand, MaxHp, 100f);
            AddGear(RingId, EquipmentSlot.Accessory, MaxHp, 50f);
        }

        [TearDown]
        public void TearDownGear()
        {
            if (_created == null) return;
            foreach (Object created in _created) Object.DestroyImmediate(created);
            _created = null;
        }

        // ---------------- base stats are never touched ----------------

        [Test]
        public void Equipping_does_not_change_the_authored_base_stats()
        {
            Prepare();
            Character character = MakeCharacter();
            var bag = new ItemContainerState(new OwnerId("account:1"), 6);
            var worn = new CharacterEquipmentState(character.Identity.CharacterId);

            int baseVit = character.Stats.GetOrDefault(new DefinitionId(Vit), -1);
            Revision statsBefore = character.Stats.Revision;

            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(SwordId),
                new OwnerId("account:1")), _items);
            EquipmentService.Equip(bag, worn, 0,
                new EquipmentService.Context(_items, 50));

            Assert.That(character.Stats.GetOrDefault(new DefinitionId(Vit), -1),
                Is.EqualTo(baseVit), "Equipment must never write into the authored stats.");
            Assert.That(character.Stats.Revision, Is.EqualTo(statsBefore));
        }

        [Test]
        public void Effective_stats_rise_with_equipment_and_fall_again_when_it_comes_off()
        {
            Prepare();
            Character character = MakeCharacter();
            var bag = new ItemContainerState(new OwnerId("account:1"), 6);
            var worn = new CharacterEquipmentState(character.Identity.CharacterId);

            DerivedStatsResult bare = Derive(character, worn);
            bare.TryGet(new DefinitionId(MaxHp), out int hpBare);

            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(SwordId),
                new OwnerId("account:1")), _items);
            EquipmentService.Equip(bag, worn, 0, new EquipmentService.Context(_items, 50));

            DerivedStatsResult armed = Derive(character, worn);
            armed.TryGet(new DefinitionId(MaxHp), out int hpArmed);

            Assert.That(hpArmed, Is.GreaterThan(hpBare),
                "+100 MaxHP flows through the existing calculator untouched.");

            EquipmentService.Unequip(bag, worn, EquipmentSlot.MainHand,
                new EquipmentService.Context(_items, 50));

            DerivedStatsResult unarmed = Derive(character, worn);
            unarmed.TryGet(new DefinitionId(MaxHp), out int hpAfter);

            Assert.That(hpAfter, Is.EqualTo(hpBare), "Taking it off restores exactly the bare value.");
        }

        [Test]
        public void Several_pieces_contribute_together()
        {
            Prepare();
            Character character = MakeCharacter();
            var bag = new ItemContainerState(new OwnerId("account:1"), 6);
            var worn = new CharacterEquipmentState(character.Identity.CharacterId);
            var context = new EquipmentService.Context(_items, 50);

            Derive(character, worn).TryGet(new DefinitionId(MaxHp), out int bare);

            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(SwordId), new OwnerId("a")), _items);
            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(RingId), new OwnerId("a")), _items);
            EquipmentService.Equip(bag, worn, 0, context);
            EquipmentService.Equip(bag, worn, 1, context);

            Derive(character, worn).TryGet(new DefinitionId(MaxHp), out int both);

            // +100 from the sword and +50 from the ring.
            Assert.That(both - bare, Is.EqualTo(150));
            Assert.That(worn.Count, Is.EqualTo(2));
        }

        [Test]
        public void Recalculating_repeatedly_is_deterministic()
        {
            Prepare();
            Character character = MakeCharacter();
            var bag = new ItemContainerState(new OwnerId("account:1"), 6);
            var worn = new CharacterEquipmentState(character.Identity.CharacterId);
            var context = new EquipmentService.Context(_items, 50);

            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(SwordId), new OwnerId("a")), _items);
            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(RingId), new OwnerId("a")), _items);
            EquipmentService.Equip(bag, worn, 0, context);
            EquipmentService.Equip(bag, worn, 1, context);

            Derive(character, worn).TryGet(new DefinitionId(MaxHp), out int first);

            for (int i = 0; i < 50; i++)
            {
                Derive(character, worn).TryGet(new DefinitionId(MaxHp), out int again);
                Assert.That(again, Is.EqualTo(first),
                    "Recalculation must not drift upward with every call.");
            }
        }

        // ---------------- STEP 26: combat consumes effective stats ----------------

        [Test]
        public void A_weapon_raises_damage_through_the_existing_combat_path()
        {
            Prepare();
            Character character = MakeCharacter();
            var bag = new ItemContainerState(new OwnerId("account:1"), 6);
            var worn = new CharacterEquipmentState(character.Identity.CharacterId);
            var context = new EquipmentService.Context(_items, 50);

            // A combatant reads its stats from the derived result it is handed, so
            // "effective stats" reach combat with no combat change at all.
            DerivedStatsResult bare = Derive(character, worn);
            ResourceLimits limits = ResourceLimits.From(bare, new DefinitionId(MaxHp), new DefinitionId(MaxMp));
            var attacker = new CharacterCombatant(character, bare, limits, new CombatTeam(1));

            var target = new FakePooledCombatant("target", 2, 100000, 100000);
            attacker.Position = CombatPosition.Zero;
            target.Position = CombatPosition.Zero;

            var rules = BasicAttackRules.Melee(
                new DefinitionId(MaxHp), new DefinitionId("stat.none"), 0, 100f);

            AttackResult before = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);

            // Equip, recompute through the same calculator, hand the combatant the result.
            bag.Add(new EquipmentInstance(InstanceId.New(), new DefinitionId(SwordId), new OwnerId("a")), _items);
            EquipmentService.Equip(bag, worn, 0, context);

            DerivedStatsResult armed = Derive(character, worn);
            attacker.SetLimits(ResourceLimits.From(armed, new DefinitionId(MaxHp), new DefinitionId(MaxMp)),
                armed);

            AttackResult after = BasicAttackExecutor.Execute(
                new AttackIntent(attacker, target), rules);

            Assert.That(after.Damage, Is.GreaterThan(before.Damage),
                "The sword raised the derived stat the rules name as attack power. "
                + "No damage formula changed.");
            Assert.That(after.Damage - before.Damage, Is.EqualTo(100),
                "Exactly the authored +100, carried by the existing modifier pipeline.");
        }
    }
}
