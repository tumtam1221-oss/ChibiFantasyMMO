using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Devil Fruits: ownership, activation, passives, effects and immunities.
    /// </summary>
    /// <remarks>
    /// The property under test throughout is that <em>no fruit has code</em>. Darkness and
    /// Light behave completely differently and walk the same path; the difference lives in
    /// two assets. Several tests below assert that directly, by reading the source.
    /// </remarks>
    [TestFixture]
    internal sealed class DevilFruitTests : CollectibleTestBase
    {
        private CharacterDevilFruitState _state;
        private StatusEffectRuntimeState _status;

        [SetUp]
        public void SetUpFruitState()
        {
            _state = new CharacterDevilFruitState(new CharacterId("char:test"), Owner);
            _status = new StatusEffectRuntimeState(new CharacterId("char:test"));
        }

        // ---- content -------------------------------------------------------------------

        [Test]
        public void All_ten_fruits_resolve()
        {
            for (int i = 0; i < AllFruits.Length; i++)
            {
                DevilFruitDefinition fruit;
                Assert.That(Fruits.TryGet(new DefinitionId(AllFruits[i]), out fruit), Is.True,
                    AllFruits[i] + " does not resolve");
                Assert.That(fruit, Is.Not.Null);
            }

            Assert.That(AllFruits.Length, Is.EqualTo(10));
        }

        [Test]
        public void Every_fruit_has_a_unique_id()
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < AllFruits.Length; i++)
            {
                Assert.That(seen.Add(AllFruits[i]), Is.True, AllFruits[i] + " is duplicated");
            }
        }

        [Test]
        public void Every_fruit_grants_something()
        {
            for (int i = 0; i < AllFruits.Length; i++)
            {
                DevilFruitDefinition fruit;
                Fruits.TryGet(new DefinitionId(AllFruits[i]), out fruit);

                bool grants = fruit.PassiveAbility.IsValid || fruit.ActiveAbility.IsValid
                    || fruit.GrantedEffects.Length > 0 || fruit.Immunities.Length > 0
                    || fruit.ImmuneCategories.Length > 0 || fruit.StatModifiers.Length > 0;

                Assert.That(grants, Is.True, AllFruits[i] + " grants nothing");
            }
        }

        [Test]
        public void Every_fruit_reference_resolves()
        {
            for (int i = 0; i < AllFruits.Length; i++)
            {
                DevilFruitDefinition fruit;
                Fruits.TryGet(new DefinitionId(AllFruits[i]), out fruit);

                if (fruit.PassiveAbility.IsValid)
                {
                    SkillDefinition skill;
                    Assert.That(Skills.TryGet(fruit.PassiveAbility, out skill), Is.True);
                }

                if (fruit.ActiveAbility.IsValid)
                {
                    SkillDefinition skill;
                    Assert.That(Skills.TryGet(fruit.ActiveAbility, out skill), Is.True);
                }

                DefinitionId[] effects = fruit.GrantedEffects;

                for (int e = 0; e < effects.Length; e++)
                {
                    StatusEffectDefinition effect;
                    Assert.That(Effects.TryGet(effects[e], out effect), Is.True,
                        AllFruits[i] + " references a missing effect");
                }
            }
        }

        [Test]
        public void A_fruit_is_held_as_a_normal_item_instance()
        {
            ItemContainerState bag = Container();
            ItemInstance fruit = Stack(DarknessItem);

            bag.Add(fruit, Items);

            // The ordinary four identity members, and nothing special.
            Assert.That(fruit.InstanceId.IsValid, Is.True);
            Assert.That(fruit.DefinitionId, Is.EqualTo(new DefinitionId(DarknessItem)));
            Assert.That(fruit.Owner, Is.EqualTo(Owner));
            Assert.That(fruit.Quantity, Is.EqualTo(1));
            Assert.That(fruit.Revision, Is.EqualTo(Revision.Initial));
            Assert.That(bag.CountOf(new DefinitionId(DarknessItem)), Is.EqualTo(1));
        }

        [Test]
        public void An_uneaten_fruit_item_is_tradable()
        {
            ItemDefinition item;
            Items.TryGet(new DefinitionId(DarknessItem), out item);

            Assert.That(item.Tradable, Is.True,
                "Phase 13 must be able to trade a fruit nobody has eaten");
            Assert.That(item.Category, Is.EqualTo(ItemCategory.DevilFruit));
        }

        // ---- activation ----------------------------------------------------------------

        [Test]
        public void Activating_a_fruit_records_it()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Darkness), InstanceId.New(), FruitContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)));
            Assert.That(_state.HasActiveFruit, Is.True);
        }

        [Test]
        public void Activating_advances_the_revision_exactly_once()
        {
            Revision before = _state.Revision;

            DevilFruitService.TryActivate(_state, new DefinitionId(Darkness), InstanceId.New(),
                FruitContext(_status));

            Assert.That(_state.Revision.Value, Is.EqualTo(before.Value + 1));
        }

        [Test]
        public void A_second_fruit_is_refused_and_the_first_is_kept()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Darkness), InstanceId.New(),
                FruitContext(_status));

            Revision after = _state.Revision;

            DevilFruitResult second = DevilFruitService.TryActivate(_state,
                new DefinitionId(Light), InstanceId.New(), FruitContext(_status));

            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Reason, Is.EqualTo(DevilFruitRejection.AlreadyHasFruit));
            Assert.That(_state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)),
                "the fruit already eaten is never silently replaced");
            Assert.That(_state.Revision, Is.EqualTo(after), "a refusal is not a mutation");
        }

        [Test]
        public void A_disabled_fruit_is_refused()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Fruit10), InstanceId.New(), FruitContext(_status));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(DevilFruitRejection.FruitDisabled));
            Assert.That(_state.HasActiveFruit, Is.False);
        }

        [Test]
        public void An_unknown_fruit_is_refused()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId("fruit.missing"), InstanceId.New(), FruitContext(_status));

            Assert.That(result.Reason, Is.EqualTo(DevilFruitRejection.UnknownFruit));
        }

        [Test]
        public void Another_characters_activation_is_refused()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Darkness), InstanceId.New(), FruitContext(_status, Stranger));

            Assert.That(result.Reason, Is.EqualTo(DevilFruitRejection.NotOwned));
            Assert.That(_state.HasActiveFruit, Is.False);
        }

        [Test]
        public void A_fruit_whose_skill_was_deleted_is_refused_before_anything_changes()
        {
            AddFruit("fruit.broken", activeAbility: "skill.deleted");

            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId("fruit.broken"), InstanceId.New(), FruitContext(_status));

            Assert.That(result.Reason, Is.EqualTo(DevilFruitRejection.UnknownAbility));
            Assert.That(_state.HasActiveFruit, Is.False);
            Assert.That(_status.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void A_fruit_whose_effect_was_deleted_is_refused_before_anything_changes()
        {
            AddFruit("fruit.brokeneffect", grantedEffects: new[] { "effect.deleted" });

            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId("fruit.brokeneffect"), InstanceId.New(), FruitContext(_status));

            Assert.That(result.Reason, Is.EqualTo(DevilFruitRejection.UnknownEffect));
            Assert.That(_state.HasActiveFruit, Is.False);
            Assert.That(_status.ActiveCount, Is.EqualTo(0));
        }

        // ---- darkness ------------------------------------------------------------------

        [Test]
        public void Darkness_applies_a_silencing_effect_through_data()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Darkness), InstanceId.New(), FruitContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.EffectsApplied, Is.EqualTo(1));

            // The effect landed, and it is a silencing one -- established by reading the
            // authored control type, never by comparing an id to "darkness".
            Assert.That(_status.Has(new DefinitionId(Silence)), Is.True);
            Assert.That(_status.HasControl(ControlEffectType.Silence, Effects), Is.True);
        }

        [Test]
        public void Darkness_carries_its_own_presentation_references()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Darkness), InstanceId.New(), FruitContext(_status));

            Assert.That(result.VisualEffect.IsValid, Is.True);
            Assert.That(result.SoundEffect.IsValid, Is.True);
            Assert.That(result.VisualEffect.Address, Is.EqualTo("vfx/darkness"));
        }

        [Test]
        public void The_silence_relationship_lives_in_data_not_in_the_service()
        {
            // Re-author Darkness to grant a different effect. Nothing in code changes, and
            // the outcome follows the asset.
            AddFruit("fruit.darkness.variant", grantedEffects: new[] { Might });

            DevilFruitService.TryActivate(_state, new DefinitionId("fruit.darkness.variant"),
                InstanceId.New(), FruitContext(_status));

            Assert.That(_status.Has(new DefinitionId(Might)), Is.True);
            Assert.That(_status.HasControl(ControlEffectType.Silence, Effects), Is.False);
        }

        // ---- light ---------------------------------------------------------------------

        [Test]
        public void Light_refuses_every_debuff_through_data()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Light), InstanceId.New(), FruitContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.ImmunitiesGranted, Is.EqualTo(1));

            StatusApplyResult poison = StatusEffectService.TryApply(_status,
                new DefinitionId(Poison), new DefinitionId("source"), Effects);

            Assert.That(poison.IsAccepted, Is.False);
            Assert.That(poison.Reason, Is.EqualTo(StatusApplyRejection.Immune));
            Assert.That(_status.Has(new DefinitionId(Poison)), Is.False);
        }

        [Test]
        public void Light_covers_a_debuff_authored_after_it()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Light), InstanceId.New(),
                FruitContext(_status));

            // A debuff that did not exist when the fruit was authored.
            AddEffect("effect.newcurse", StatusEffectCategory.Debuff, ControlEffectType.None, 10f);

            StatusApplyResult curse = StatusEffectService.TryApply(_status,
                new DefinitionId("effect.newcurse"), new DefinitionId("source"), Effects);

            Assert.That(curse.Reason, Is.EqualTo(StatusApplyRejection.Immune),
                "a category immunity must cover effects authored later");
        }

        [Test]
        public void Light_does_not_refuse_buffs()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Light), InstanceId.New(),
                FruitContext(_status));

            StatusApplyResult might = StatusEffectService.TryApply(_status,
                new DefinitionId(Might), new DefinitionId("source"), Effects);

            Assert.That(might.IsAccepted, Is.True);
        }

        [Test]
        public void A_named_immunity_covers_only_that_effect()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Fruit05), InstanceId.New(),
                FruitContext(_status));

            Assert.That(StatusEffectService.TryApply(_status, new DefinitionId(Poison),
                new DefinitionId("source"), Effects).Reason,
                Is.EqualTo(StatusApplyRejection.Immune));

            AddEffect("effect.othercurse", StatusEffectCategory.Debuff, ControlEffectType.None, 4f);

            Assert.That(StatusEffectService.TryApply(_status, new DefinitionId("effect.othercurse"),
                new DefinitionId("source"), Effects).IsAccepted, Is.True,
                "a named immunity is not a category immunity");
        }

        // ---- passives and abilities ----------------------------------------------------

        [Test]
        public void A_passive_only_fruit_reports_its_passive()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Fruit03), InstanceId.New(), FruitContext(_status));

            Assert.That(result.PassiveAbility, Is.EqualTo(new DefinitionId(DarkSkill)));
            Assert.That(result.ActiveAbility.IsValid, Is.False);
        }

        [Test]
        public void An_active_ability_is_reported_as_an_ordinary_skill_id()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Darkness), InstanceId.New(),
                FruitContext(_status));

            DefinitionId ability = DevilFruitService.ActiveAbilityOf(_state,
                FruitContext(_status));

            Assert.That(ability, Is.EqualTo(new DefinitionId(DarkSkill)));

            // It resolves in the ordinary skill registry, so the ordinary skill pipeline can
            // take it from here. There is no fruit skill registry.
            SkillDefinition skill;
            Assert.That(Skills.TryGet(ability, out skill), Is.True);
        }

        [Test]
        public void A_fruits_modifiers_are_collected_never_computed()
        {
            DevilFruitService.TryActivate(_state, new DefinitionId(Fruit04), InstanceId.New(),
                FruitContext(_status));

            var modifiers = new List<StatModifier>();
            DevilFruitService.CollectModifiers(_state, FruitContext(_status), modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers[0].Stat, Is.EqualTo(new DefinitionId(Str)));
            Assert.That(modifiers[0].Value, Is.EqualTo(12f).Within(0.001f),
                "the authored value arrives unchanged; arithmetic is the calculator's job");
        }

        [Test]
        public void No_active_fruit_contributes_no_modifiers()
        {
            var modifiers = new List<StatModifier>();
            DevilFruitService.CollectModifiers(_state, FruitContext(_status), modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(0));
        }

        [Test]
        public void A_fruit_granting_two_effects_applies_both()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Fruit09), InstanceId.New(), FruitContext(_status));

            Assert.That(result.EffectsApplied, Is.EqualTo(2));
            Assert.That(_status.ActiveCount, Is.EqualTo(2));
        }

        [Test]
        public void Activation_without_a_status_runtime_still_records_the_fruit()
        {
            DevilFruitResult result = DevilFruitService.TryActivate(_state,
                new DefinitionId(Darkness), InstanceId.New(), FruitContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.EffectsApplied, Is.EqualTo(0),
                "less information, never wrong information");
            Assert.That(_state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)));
        }

        // ---- the state itself ----------------------------------------------------------

        [Test]
        public void The_state_refuses_a_second_activation_even_when_called_directly()
        {
            _state.Activate(new DefinitionId(Darkness), InstanceId.New());

            Assert.That(_state.Activate(new DefinitionId(Light), InstanceId.New()), Is.False,
                "the one-fruit rule must not be bypassable by reaching past the service");
            Assert.That(_state.ActiveFruit, Is.EqualTo(new DefinitionId(Darkness)));
        }

        [Test]
        public void The_state_records_which_copy_was_spent()
        {
            InstanceId spent = InstanceId.New();

            DevilFruitService.TryActivate(_state, new DefinitionId(Darkness), spent,
                FruitContext(_status));

            Assert.That(_state.SourceInstance, Is.EqualTo(spent));
        }

        [Test]
        public void There_is_no_list_of_active_fruits()
        {
            // Structural: the one-fruit rule is enforced by there being nowhere to put a
            // second, not by a check somebody could remove.
            System.Reflection.FieldInfo[] fields = typeof(CharacterDevilFruitState).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            foreach (System.Reflection.FieldInfo field in fields)
            {
                Assert.That(field.FieldType.IsArray, Is.False,
                    field.Name + " is a collection; one fruit means one field");

                Assert.That(field.FieldType.IsGenericType
                    && field.FieldType.GetGenericTypeDefinition() == typeof(List<>), Is.False,
                    field.Name + " is a list; one fruit means one field");
            }
        }

        [Test]
        public void Deactivating_clears_the_fruit_and_advances_the_revision()
        {
            _state.Activate(new DefinitionId(Darkness), InstanceId.New());
            Revision after = _state.Revision;

            Assert.That(_state.Deactivate(), Is.True);
            Assert.That(_state.HasActiveFruit, Is.False);
            Assert.That(_state.Revision.Value, Is.EqualTo(after.Value + 1));
        }

        [Test]
        public void Deactivating_nothing_is_not_a_mutation()
        {
            Revision before = _state.Revision;

            Assert.That(_state.Deactivate(), Is.False);
            Assert.That(_state.Revision, Is.EqualTo(before));
        }

        // ---- no fruit-specific code ----------------------------------------------------

        [Test]
        public void No_fruit_is_named_in_the_service()
        {
            foreach (string code in CodeLines("Assets/_Game/Scripts/Gameplay/DevilFruitService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"fruit."));
                Assert.That(code, Does.Not.Contain("Darkness"));
                Assert.That(code, Does.Not.Contain("Silence"));
                Assert.That(code.Contains("DebuffImmunity"), Is.False);
            }
        }

        [Test]
        public void There_is_no_fruit_specific_type()
        {
            System.Type[] types = typeof(DevilFruitService).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("DarknessFruit"));
                Assert.That(type.Name, Is.Not.EqualTo("LightFruit"));
                Assert.That(type.Name, Is.Not.EqualTo("FireFruit"));
                Assert.That(type.Name, Is.Not.EqualTo("IceFruit"));
                Assert.That(type.Name, Is.Not.EqualTo("FruitSkillExecutor"));
                Assert.That(type.Name, Is.Not.EqualTo("FruitStatusEngine"));
                Assert.That(type.Name, Is.Not.EqualTo("DevilFruitItem"));
                Assert.That(type.Name, Is.Not.EqualTo("FruitInventoryItem"));
                Assert.That(type.Name, Is.Not.EqualTo("SpecialLootItem"));
            }
        }

        /// <summary>A file's lines with the comments removed.</summary>
        /// <remarks>Prose may name Darkness while explaining why code does not; asserting
        /// over raw text would check the documentation instead of the implementation.</remarks>
        internal static IEnumerable<string> CodeLines(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;

                yield return code;
            }
        }
    }
}
