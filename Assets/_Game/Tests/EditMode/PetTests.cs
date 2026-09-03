using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Pets: ownership, experience, evolution, buffs and following.
    /// </summary>
    /// <remarks>
    /// Progression is integer-only and derived from a cumulative total, so the tests below
    /// check the properties that follow from that: the same award twice is the same as the
    /// award once, a big award crosses several levels in one mutation, and a reload from a
    /// stored total reproduces the level exactly.
    ///
    /// Evolution changes what a pet <em>is</em> without creating a second one, which several
    /// tests assert by identity rather than by value.
    /// </remarks>
    [TestFixture]
    internal sealed class PetTests : CollectibleTestBase
    {
        private StatusEffectRuntimeState _status;
        private PetCompanionState _companion;

        [SetUp]
        public void SetUpPetState()
        {
            _status = new StatusEffectRuntimeState(new CharacterId("char:test"));
            _companion = new PetCompanionState(new CharacterId("char:test"));
        }

        // ---- ownership -----------------------------------------------------------------

        [Test]
        public void Acquiring_a_pet_creates_owned_persistent_state()
        {
            PetResult result = PetService.TryAcquire(new DefinitionId(PetA), Owner, PetContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Pet.InstanceId.IsValid, Is.True);
            Assert.That(result.Pet.DefinitionId, Is.EqualTo(new DefinitionId(PetA)));
            Assert.That(result.Pet.Owner, Is.EqualTo(Owner));
            Assert.That(result.Pet.Level, Is.EqualTo(1));
            Assert.That(result.Pet.Experience, Is.EqualTo(0));
            Assert.That(result.Pet.Revision, Is.EqualTo(Revision.Initial));
        }

        [Test]
        public void A_pet_is_persistent_state()
        {
            Assert.That(typeof(IPersistentState).IsAssignableFrom(typeof(PetInstance))
                || typeof(IGameInstance).IsAssignableFrom(typeof(PetInstance)), Is.True,
                "a pet must be storable; it is not a per-session value");
        }

        [Test]
        public void An_unknown_pet_is_refused()
        {
            Assert.That(PetService.TryAcquire(new DefinitionId("pet.missing"), Owner,
                PetContext()).Reason, Is.EqualTo(PetRejection.UnknownPet));
        }

        [Test]
        public void A_disabled_pet_is_refused()
        {
            AddPet("pet.off", buff: PetVigour, thresholds: new[] { 10 }, enabled: false);

            Assert.That(PetService.TryAcquire(new DefinitionId("pet.off"), Owner,
                PetContext()).Reason, Is.EqualTo(PetRejection.PetDisabled));
        }

        // ---- experience ----------------------------------------------------------------

        [Test]
        public void Experience_accumulates()
        {
            PetInstance pet = Pet(PetA);

            PetResult result = PetService.TryGrantExperience(pet, 40, PetContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(pet.Experience, Is.EqualTo(40));
            Assert.That(pet.Level, Is.EqualTo(1));
            Assert.That(result.LevelsGained, Is.EqualTo(0));
        }

        [Test]
        public void Reaching_a_threshold_raises_the_level()
        {
            PetInstance pet = Pet(PetA);   // thresholds 100, 300, 600

            PetResult result = PetService.TryGrantExperience(pet, 100, PetContext());

            Assert.That(pet.Level, Is.EqualTo(2));
            Assert.That(result.LevelsGained, Is.EqualTo(1));
        }

        [Test]
        public void One_award_can_cross_several_levels()
        {
            PetInstance pet = Pet(PetA);

            PetResult result = PetService.TryGrantExperience(pet, 600, PetContext());

            Assert.That(pet.Level, Is.EqualTo(4));
            Assert.That(result.LevelsGained, Is.EqualTo(3));
        }

        [Test]
        public void One_award_advances_the_revision_exactly_once()
        {
            PetInstance pet = Pet(PetA);
            Revision before = pet.Revision;

            PetService.TryGrantExperience(pet, 600, PetContext());

            Assert.That(pet.Revision.Value, Is.EqualTo(before.Value + 1),
                "crossing three levels is one award, not four mutations");
        }

        [Test]
        public void An_award_of_nothing_is_not_a_mutation()
        {
            PetInstance pet = Pet(PetA);
            Revision before = pet.Revision;

            PetResult result = PetService.TryGrantExperience(pet, 0, PetContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(pet.Revision, Is.EqualTo(before));
        }

        [Test]
        public void Negative_experience_is_refused_rather_than_clamped()
        {
            PetInstance pet = Pet(PetA);
            PetService.TryGrantExperience(pet, 150, PetContext());

            Revision after = pet.Revision;

            PetResult result = PetService.TryGrantExperience(pet, -50, PetContext());

            Assert.That(result.Reason, Is.EqualTo(PetRejection.NegativeExperience));
            Assert.That(pet.Experience, Is.EqualTo(150));
            Assert.That(pet.Revision, Is.EqualTo(after));
        }

        [Test]
        public void The_level_caps_but_the_overflow_is_kept()
        {
            PetInstance pet = Pet(PetA);   // three thresholds, so level four is the ceiling

            PetService.TryGrantExperience(pet, 10000, PetContext());

            Assert.That(pet.Level, Is.EqualTo(4));
            Assert.That(pet.Experience, Is.EqualTo(10000),
                "experience past the cap is credited, so raising the ceiling later honours it");
        }

        [Test]
        public void Raising_the_ceiling_later_credits_experience_already_earned()
        {
            PetInstance pet = Pet(PetA);
            PetService.TryGrantExperience(pet, 10000, PetContext());

            PetDefinition definition;
            Pets.TryGet(new DefinitionId(PetA), out definition);
            SetPrivate(definition, "_experienceThresholds", new[] { 100, 300, 600, 1000, 5000 });

            // No new experience; only the curve changed.
            PetResult result = PetService.TryGrantExperience(pet, 0, PetContext());

            Assert.That(PetService.LevelFor(definition, pet.Experience), Is.EqualTo(6));
            Assert.That(result.IsAccepted, Is.True);
        }

        [Test]
        public void The_same_total_always_gives_the_same_level()
        {
            PetInstance once = Pet(PetA);
            PetInstance twice = Pet(PetA);

            PetService.TryGrantExperience(once, 400, PetContext());

            PetService.TryGrantExperience(twice, 150, PetContext());
            PetService.TryGrantExperience(twice, 250, PetContext());

            Assert.That(once.Experience, Is.EqualTo(twice.Experience));
            Assert.That(once.Level, Is.EqualTo(twice.Level),
                "a level derived from a total cannot depend on how it was awarded");
        }

        [Test]
        public void An_explicit_max_level_is_the_stricter_ceiling()
        {
            AddPet("pet.capped", buff: PetVigour, thresholds: new[] { 10, 20, 30 }, maxLevel: 2);

            PetInstance pet = Pet("pet.capped");
            PetService.TryGrantExperience(pet, 1000, PetContext());

            Assert.That(pet.Level, Is.EqualTo(2));
        }

        [Test]
        public void Another_owners_pet_cannot_be_given_experience()
        {
            PetInstance pet = Pet(PetA, Stranger);

            PetResult result = PetService.TryGrantExperience(pet, 100, PetContext(null, Owner));

            Assert.That(result.Reason, Is.EqualTo(PetRejection.NotOwned));
            Assert.That(pet.Experience, Is.EqualTo(0));
        }

        [Test]
        public void The_next_level_requirement_is_reported_and_is_zero_at_the_cap()
        {
            PetDefinition definition;
            Pets.TryGet(new DefinitionId(PetA), out definition);

            Assert.That(PetService.ExperienceForNextLevel(definition, 1), Is.EqualTo(100));
            Assert.That(PetService.ExperienceForNextLevel(definition, 3), Is.EqualTo(600));
            Assert.That(PetService.ExperienceForNextLevel(definition, 4), Is.EqualTo(0),
                "no next level means no requirement to divide by");
        }

        // ---- evolution -----------------------------------------------------------------

        [Test]
        public void A_pet_that_meets_the_requirement_evolves()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 200, PetContext());   // reaches level 3

            PetResult result = PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId(PetCEvolved)));
            Assert.That(pet.EvolutionStage, Is.EqualTo(1));
        }

        [Test]
        public void Evolution_does_not_create_a_second_pet()
        {
            PetInstance pet = Pet(PetC);
            InstanceId identity = pet.InstanceId;
            PetService.TryGrantExperience(pet, 200, PetContext());

            PetResult result = PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(ReferenceEquals(result.Pet, pet), Is.True,
                "the evolved pet is the same object, not a replacement");
            Assert.That(pet.InstanceId, Is.EqualTo(identity));
            Assert.That(pet.Owner, Is.EqualTo(Owner));
        }

        [Test]
        public void Evolution_preserves_accumulated_experience()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 250, PetContext());
            int before = pet.Experience;

            PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(pet.Experience, Is.EqualTo(before));
        }

        [Test]
        public void Evolution_advances_the_revision_exactly_once()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 200, PetContext());
            Revision before = pet.Revision;

            PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(pet.Revision.Value, Is.EqualTo(before.Value + 1));
        }

        [Test]
        public void An_underlevelled_pet_is_refused()
        {
            PetInstance pet = Pet(PetC);   // level 1, needs 3

            PetResult result = PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(result.Reason, Is.EqualTo(PetRejection.LevelRequirementNotMet));
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId(PetC)));
        }

        [Test]
        public void A_pet_with_no_authored_stage_cannot_evolve()
        {
            PetInstance pet = Pet(PetE);
            PetService.TryGrantExperience(pet, 1000, PetContext());

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status)).Reason,
                Is.EqualTo(PetRejection.NoEvolutionAvailable));
        }

        [Test]
        public void An_evolution_target_that_was_deleted_is_refused()
        {
            AddPet("pet.broken", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.gone"), 1) });

            PetInstance pet = Pet("pet.broken");

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status)).Reason,
                Is.EqualTo(PetRejection.UnknownEvolvedForm));
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId("pet.broken")));
        }

        [Test]
        public void A_material_cost_is_taken_only_on_success()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(EvolutionStone, 5), Items);

            PetInstance pet = Pet(PetD);
            PetService.TryGrantExperience(pet, 100, PetContext());   // reaches level 2

            PetResult result = PetService.TryEvolve(pet, bag, PetContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(bag.CountOf(new DefinitionId(EvolutionStone)), Is.EqualTo(3),
                "exactly the authored quantity is spent");
        }

        [Test]
        public void A_missing_material_refuses_the_evolution_and_spends_nothing()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(EvolutionStone, 1), Items);   // needs two

            PetInstance pet = Pet(PetD);
            PetService.TryGrantExperience(pet, 100, PetContext());

            PetResult result = PetService.TryEvolve(pet, bag, PetContext(_status));

            Assert.That(result.Reason, Is.EqualTo(PetRejection.MissingMaterial));
            Assert.That(bag.CountOf(new DefinitionId(EvolutionStone)), Is.EqualTo(1));
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId(PetD)));
        }

        [Test]
        public void An_underlevelled_pet_with_the_material_still_keeps_the_material()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(EvolutionStone, 5), Items);

            PetInstance pet = Pet(PetD);   // level 1, needs 2

            PetService.TryEvolve(pet, bag, PetContext(_status));

            Assert.That(bag.CountOf(new DefinitionId(EvolutionStone)), Is.EqualTo(5),
                "nothing is spent before every check has passed");
        }

        [Test]
        public void Another_owners_pet_cannot_be_evolved()
        {
            PetInstance pet = Pet(PetC, Stranger);
            PetService.TryGrantExperience(pet, 200, PetContext());

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status, Owner)).Reason,
                Is.EqualTo(PetRejection.NotOwned));
        }

        [Test]
        public void A_chain_of_evolutions_is_allowed()
        {
            // A -> B -> C, three definitions each naming the next.
            AddPet("pet.chain.c", buff: PetVigour, thresholds: new[] { 10 });
            AddPet("pet.chain.b", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.chain.c"), 1) });
            AddPet("pet.chain.a", buff: PetVigour, thresholds: new[] { 10 },
                stages: new[] { new PetEvolutionStage(new DefinitionId("pet.chain.b"), 1) });

            PetInstance pet = Pet("pet.chain.a");

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status)).IsAccepted, Is.True);
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId("pet.chain.b")));

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status)).IsAccepted, Is.True);
            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId("pet.chain.c")));
            Assert.That(pet.EvolutionStage, Is.EqualTo(2));

            Assert.That(PetService.TryEvolve(pet, null, PetContext(_status)).Reason,
                Is.EqualTo(PetRejection.NoEvolutionAvailable), "the chain ends");
        }

        // ---- buffs ---------------------------------------------------------------------

        [Test]
        public void Summoning_a_pet_applies_its_authored_buff()
        {
            PetInstance pet = Pet(PetA);

            PetResult result = PetService.TrySummon(_companion, pet, PetContext(_status));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.GrantedBuff, Is.EqualTo(new DefinitionId(PetVigour)));
            Assert.That(_status.Has(new DefinitionId(PetVigour)), Is.True);
            Assert.That(_companion.IsSummoned, Is.True);
        }

        [Test]
        public void Dismissing_takes_back_exactly_what_the_pet_granted()
        {
            PetInstance pet = Pet(PetA);
            StatusEffectService.TryApply(_status, new DefinitionId(Might),
                new DefinitionId("elsewhere"), Effects);

            PetService.TrySummon(_companion, pet, PetContext(_status));
            Assert.That(_status.ActiveCount, Is.EqualTo(2));

            PetService.Dismiss(_companion, PetContext(_status));

            Assert.That(_status.Has(new DefinitionId(PetVigour)), Is.False);
            Assert.That(_status.Has(new DefinitionId(Might)), Is.True,
                "another source's effect is untouched");
            Assert.That(_companion.IsSummoned, Is.False);
        }

        [Test]
        public void Summoning_a_different_pet_replaces_the_previous_buff()
        {
            PetService.TrySummon(_companion, Pet(PetA), PetContext(_status));
            PetService.TrySummon(_companion, Pet(PetB), PetContext(_status));

            Assert.That(_status.Has(new DefinitionId(PetVigour)), Is.False,
                "the previous pet's grant must not outlive it");
            Assert.That(_status.Has(new DefinitionId(PetGuard)), Is.True);
        }

        [Test]
        public void An_evolved_pet_grants_its_new_forms_buff()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 200, PetContext());

            PetResult result = PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(result.GrantedBuff, Is.EqualTo(new DefinitionId(PetGuard)));
            Assert.That(_status.Has(new DefinitionId(PetGuard)), Is.True);
            Assert.That(_status.Has(new DefinitionId(PetVigour)), Is.False,
                "the old form's buff goes with the old form");
        }

        [Test]
        public void Pet_buff_modifiers_reach_the_stat_pipeline()
        {
            PetService.TrySummon(_companion, Pet(PetB), PetContext(_status));

            var modifiers = new System.Collections.Generic.List<StatModifier>();
            _status.CollectModifiers(Effects, modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(1));
            Assert.That(modifiers[0].Stat, Is.EqualTo(new DefinitionId(Vit)));
            Assert.That(modifiers[0].Value, Is.EqualTo(3f).Within(0.001f));
        }

        // ---- follow and aura -----------------------------------------------------------

        [Test]
        public void A_summoned_pet_starts_following()
        {
            PetService.TrySummon(_companion, Pet(PetA), PetContext(_status));

            Assert.That(_companion.Mode, Is.EqualTo(PetFollowMode.Follow));
        }

        [Test]
        public void Follow_idle_and_return_are_all_settable()
        {
            PetService.TrySummon(_companion, Pet(PetA), PetContext(_status));

            Assert.That(_companion.SetMode(PetFollowMode.Idle), Is.True);
            Assert.That(_companion.Mode, Is.EqualTo(PetFollowMode.Idle));

            Assert.That(_companion.SetMode(PetFollowMode.Return), Is.True);
            Assert.That(_companion.Mode, Is.EqualTo(PetFollowMode.Return));

            Assert.That(_companion.SetMode(PetFollowMode.Follow), Is.True);
        }

        [Test]
        public void Dismissed_cannot_be_set_as_a_mode()
        {
            PetService.TrySummon(_companion, Pet(PetA), PetContext(_status));

            Assert.That(_companion.SetMode(PetFollowMode.Dismissed), Is.False,
                "putting a pet away is Dismiss, which also clears the aura");
            Assert.That(_companion.IsSummoned, Is.True);
        }

        [Test]
        public void Setting_the_same_mode_is_not_a_mutation()
        {
            PetService.TrySummon(_companion, Pet(PetA), PetContext(_status));
            Revision before = _companion.Revision;

            Assert.That(_companion.SetMode(PetFollowMode.Follow), Is.False);
            Assert.That(_companion.Revision, Is.EqualTo(before));
        }

        [Test]
        public void An_evolved_aura_form_reports_itself_as_an_aura()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 200, PetContext());

            PetResult result = PetService.TryEvolve(pet, null, PetContext(_status));

            Assert.That(result.IsAuraForm, Is.True);

            PetService.TrySummon(_companion, pet, PetContext(_status));

            Assert.That(_companion.IsAuraForm, Is.True,
                "the evolved pet stops being a follower and becomes an aura");
            Assert.That(_companion.IsSummoned, Is.True,
                "it is still out; only how it appears changed");
        }

        [Test]
        public void An_evolution_that_is_not_an_aura_keeps_following()
        {
            ItemContainerState bag = Container();
            bag.Add(Stack(EvolutionStone, 5), Items);

            PetInstance pet = Pet(PetD);
            PetService.TryGrantExperience(pet, 100, PetContext());

            PetResult result = PetService.TryEvolve(pet, bag, PetContext(_status));

            Assert.That(result.IsAuraForm, Is.False);

            PetService.TrySummon(_companion, pet, PetContext(_status));

            Assert.That(_companion.IsAuraForm, Is.False,
                "whether an evolved pet becomes an aura is authored, not implied");
        }

        [Test]
        public void The_aura_form_is_state_not_a_second_pet()
        {
            PetInstance pet = Pet(PetC);
            PetService.TryGrantExperience(pet, 200, PetContext());
            PetService.TryEvolve(pet, null, PetContext(_status));
            PetService.TrySummon(_companion, pet, PetContext(_status));

            Assert.That(ReferenceEquals(_companion.Summoned, pet), Is.True,
                "no stand-in object represents the aura");
        }

        [Test]
        public void Gameplay_holds_no_position_for_a_pet()
        {
            // Structural: a pet's whereabouts is presentation. Gameplay states an intent.
            System.Reflection.PropertyInfo[] properties =
                typeof(PetCompanionState).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("Vector3"), property.Name);
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("Transform"), property.Name);
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(CombatPosition)),
                    property.Name);
            }
        }

        // ---- no pet-specific code ------------------------------------------------------

        [Test]
        public void No_pet_is_named_in_the_service()
        {
            foreach (string code in DevilFruitTests.CodeLines(
                "Assets/_Game/Scripts/Gameplay/PetService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"pet."));
                Assert.That(code, Does.Not.Contain("\"item."));
                Assert.That(code, Does.Not.Contain("\"effect."));
            }
        }

        [Test]
        public void There_is_no_pet_specific_type()
        {
            System.Type[] types = typeof(PetService).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("FirePetEvolution"));
                Assert.That(type.Name, Is.Not.EqualTo("WolfEvolution"));
                Assert.That(type.Name, Is.Not.EqualTo("PetInventory"));
                Assert.That(type.Name, Is.Not.EqualTo("PetItemInstance"));
            }
        }

        [Test]
        public void No_floating_point_comparison_decides_a_level()
        {
            Assert.That(typeof(PetService).GetMethod("LevelFor").ReturnType, Is.EqualTo(typeof(int)));

            System.Reflection.ParameterInfo[] parameters =
                typeof(PetService).GetMethod("LevelFor").GetParameters();

            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(int)),
                "experience is a count; a float count compares differently on different machines");
        }
    }
}
