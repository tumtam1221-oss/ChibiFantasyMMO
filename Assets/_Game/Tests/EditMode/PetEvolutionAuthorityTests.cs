using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A pet that has earned its next form, and what that form does to its owner.
    /// </summary>
    /// <remarks>
    /// <b>Phase 12 owns the rules; this owns the seam.</b> Whether a pet may evolve, what it
    /// becomes, what that costs and which buff the new form grants are all
    /// <see cref="PetService"/>'s, authored on definitions and unchanged here. What is tested
    /// below is that a connection can ask, that the server decides, that the answer is
    /// durable, and that the character ends up with exactly one aura and exactly one buff.
    ///
    /// <b>Nothing names a pet.</b> No test asserts a rule about a particular creature: the
    /// requirement, the form and the buff all come out of fixture content, and a production
    /// pet is only used where the point is that the shipped catalogue authors a real one.
    ///
    /// <b>The buff is a status effect, not a pet feature.</b> It arrives through the one
    /// status runtime, is removed by grantor, and is read back through the canonical stat
    /// calculation -- so these tests assert on a character's stats, not on a pet's fields.
    /// </remarks>
    [TestFixture]
    internal sealed class PetEvolutionAuthorityTests : CollectibleTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public bool Broken { get; set; }

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                if (Broken)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable);
                }

                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private const string HomeMap = "map.home";
        private const int Connection = 11;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterPetAuthority _authority;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpEvolution()
        {
            _store = new FakeStore();
            _players = NewRegistry();
            _authority = new CharacterPetAuthority(_players, Pets, Items, Effects);
        }

        [TearDown]
        public void TearDownEvolution()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        // ---- earning it -------------------------------------------------------------------

        [Test]
        public void APetThatHasNotEarnedItsNextFormIsRefused()
        {
            // The fixture chain needs level three. This one is level one.
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC));

            CharacterPetResult refused = _authority.Evolve(Connection,
                new InstanceId("pet-1"));

            Assert.That(refused.IsAccepted, Is.False);
            Assert.That(refused.Pet.Reason,
                Is.EqualTo(PetRejection.LevelRequirementNotMet));

            Assert.That(PetOf(hero, "pet-1").DefinitionId,
                Is.EqualTo(new DefinitionId(PetC)), "a refused evolution changed the pet");
            Assert.That(PetOf(hero, "pet-1").EvolutionStage, Is.Zero);
            Assert.That(hero.Status.ActiveCount, Is.Zero, "a refused evolution gave a buff");
        }

        [Test]
        public void APetThatHasEarnedItBecomesTheAuthoredForm()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            CharacterPetResult result = _authority.Evolve(Connection,
                new InstanceId("pet-1"));

            Assert.That(result.IsAccepted, Is.True, result.ToString());

            PetInstance pet = PetOf(hero, "pet-1");

            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId(PetCEvolved)),
                "the pet did not become the form its own content names");
            Assert.That(pet.EvolutionStage, Is.EqualTo(1));

            // The same creature: an evolution repoints an instance, it does not mint one.
            Assert.That(pet.InstanceId, Is.EqualTo(new InstanceId("pet-1")));
            Assert.That(pet.Experience, Is.EqualTo(400), "the pet lost what it had earned");
            Assert.That(hero.Pets.Count, Is.EqualTo(1), "evolving produced a second pet");
        }

        [Test]
        public void AnEvolutionIsWrittenDownBeforeItIsReportedAsDone()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            Assert.That(_authority.Evolve(Connection, new InstanceId("pet-1")).IsAccepted,
                Is.True);

            PersistedCharacter stored = _store.Rows["session-char-a"];

            Assert.That(stored.Pets.Count, Is.EqualTo(1));
            Assert.That(stored.Pets[0].Pet, Is.EqualTo(new DefinitionId(PetCEvolved)),
                "the evolved form never reached storage");
            Assert.That(stored.Pets[0].EvolutionStage, Is.EqualTo(1));
            Assert.That(stored.Pets[0].Instance, Is.EqualTo(new InstanceId("pet-1")));
        }

        [Test]
        public void AnEvolutionWhoseSaveFailsIsStillHeldByTheWorldForTheRetry()
        {
            // Existing lifecycle: the mutation is authoritative in memory, the character
            // stays dirty, and the save is retried. Nothing here reports a durable outcome
            // that no database has seen.
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _store.Broken = true;

            _authority.Evolve(Connection, new InstanceId("pet-1"));

            Assert.That(_store.Rows["session-char-a"].Pets[0].Pet,
                Is.EqualTo(new DefinitionId(PetC)),
                "precondition: the evolution never reached storage");
            Assert.That(hero.IsDirty, Is.True,
                "the evolved pet was not left queued for the save lifecycle");

            _store.Broken = false;

            Assert.That(_players.Save(hero).IsOk, Is.True);

            Assert.That(_store.Rows["session-char-a"].Pets[0].Pet,
                Is.EqualTo(new DefinitionId(PetCEvolved)));
            Assert.That(_store.Rows["session-char-a"].Pets[0].EvolutionStage, Is.EqualTo(1));
        }

        [Test]
        public void AskingTwiceEvolvesOnce()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            Assert.That(_authority.Evolve(Connection, new InstanceId("pet-1")).IsAccepted,
                Is.True);

            // The evolved form is terminal in the fixture chain, so Phase 12 answers the
            // repeat with "there is nothing to evolve into" rather than evolving again.
            CharacterPetResult again = _authority.Evolve(Connection,
                new InstanceId("pet-1"));

            Assert.That(again.IsAccepted, Is.False);
            Assert.That(again.Pet.Reason, Is.EqualTo(PetRejection.NoEvolutionAvailable));

            Assert.That(PetOf(hero, "pet-1").EvolutionStage, Is.EqualTo(1),
                "the stage was incremented twice");
            Assert.That(hero.Status.ActiveCount, Is.LessThanOrEqualTo(1),
                "the buff was applied twice");
        }

        [Test]
        public void APetSomebodyElseOwnsCannotBeEvolved()
        {
            AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            LivingCharacter other = AddPlayer(Pet("pet-2", PetC, level: 3, experience: 400),
                character: "char-b", connection: 12);

            CharacterPetResult forged = _authority.Evolve(Connection,
                new InstanceId("pet-2"));

            Assert.That(forged.IsAccepted, Is.False);
            Assert.That(PetOf(other, "pet-2").EvolutionStage, Is.Zero,
                "somebody else's pet was evolved");
        }

        // ---- what the client may say ----------------------------------------------------------

        [Test]
        public void AnEvolveRequestCanCarryOnlyWhichPet()
        {
            MethodInfo evolve = typeof(ICharacterPetRequestSink).GetMethod("Evolve");

            Assert.That(evolve.GetParameters().Length, Is.EqualTo(2));
            Assert.That(evolve.GetParameters()[0].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(evolve.GetParameters()[1].ParameterType,
                Is.EqualTo(typeof(InstanceId)));

            // Nothing on the wire may name a form, a stage, a level or a buff.
            foreach (ParameterInfo parameter in typeof(CharacterNetworkEntity)
                .GetMethod("RequestEvolvePet").GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.EqualTo(typeof(string)),
                    "a client can say something about an evolution other than which pet");
            }

            Assert.That(typeof(CharacterNetworkEntity).GetMethod("RequestEvolvePet")
                .GetParameters().Length, Is.EqualTo(1));
        }

        [Test]
        public void NoPetOrFormIsNamedInTheAuthority()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/CharacterPetAuthority.cs");

            Assert.That(source.Contains("\"pet."), Is.False,
                "the pet authority names a specific pet");
            Assert.That(source.Contains("\"status."), Is.False,
                "the pet authority names a specific status effect");
            Assert.That(source.Contains("IsAuraForm ="), Is.False,
                "the pet authority decides an aura rather than reading one");
        }

        // ---- the buff --------------------------------------------------------------------------

        [Test]
        public void AnEvolvedPetThatIsOutBuffsItsOwnerThroughTheStatusRuntime()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Activate(Connection, new InstanceId("pet-1"));

            // Before: the base form's own buff, from the fixture content.
            Assert.That(hero.Status.Has(new DefinitionId(PetVigour)), Is.True,
                "the base form's authored buff was not applied");

            _authority.Evolve(Connection, new InstanceId("pet-1"));

            Assert.That(hero.Status.Has(new DefinitionId(PetVigour)), Is.False,
                "the old form's buff outlived the form that granted it");
            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.True,
                "the evolved form's authored buff was not applied");

            // One of it, whatever else happens.
            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(Count(hero, PetGuard), Is.EqualTo(1),
                "the evolved buff stacked with itself");
        }

        [Test]
        public void AnEvolvedPetThatIsNotOutBuffsNobody()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Evolve(Connection, new InstanceId("pet-1"));

            Assert.That(hero.Companion.IsSummoned, Is.False, "precondition: not out");
            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.False,
                "an evolved pet nobody has out was buffing its owner");
        }

        [Test]
        public void PuttingAnEvolvedPetAwayTakesBackItsBuffAndNothingElse()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));

            // Something the pet did not grant, which must survive.
            StatusEffectService.TryApply(hero.Status, new DefinitionId(Might),
                new DefinitionId("skill.somewhere-else"), Effects);

            Assert.That(_authority.Deactivate(Connection).IsAccepted, Is.True);

            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.False,
                "the pet's buff outlived the pet being put away");
            Assert.That(hero.Status.Has(new DefinitionId(Might)), Is.True,
                "putting a pet away removed a buff the pet never granted");

            Assert.That(PetOf(hero, "pet-1").EvolutionStage, Is.EqualTo(1),
                "putting a pet away undid its evolution");
        }

        [Test]
        public void BringingAnEvolvedPetBackOutAppliesItsBuffExactlyOnce()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));
            _authority.Deactivate(Connection);

            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(Count(hero, PetGuard), Is.EqualTo(1));
            Assert.That(hero.Companion.IsAuraForm, Is.True,
                "the evolved form came back out as a follower");
        }

        [Test]
        public void SwitchingPetsMovesTheBuffWithTheActiveOne()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400),
                Pet("pet-2", PetA));

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));

            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.True);

            // The other pet, a base follower with its own authored buff.
            _authority.Activate(Connection, new InstanceId("pet-2"));

            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.False,
                "the evolved pet kept buffing while another pet was out");
            Assert.That(hero.Companion.IsAuraForm, Is.False,
                "a base pet came out as an aura");

            // And back again.
            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(Count(hero, PetGuard), Is.EqualTo(1));
            Assert.That(hero.Companion.IsAuraForm, Is.True);
        }

        [Test]
        public void TwoEvolvedPetsNeverBuffAtOnce()
        {
            // Two evolved pets with different authored buffs. Only what is out counts.
            LivingCharacter hero = AddPlayer(
                Pet("pet-1", PetC, level: 3, experience: 400),
                Pet("pet-2", PetD, level: 2, experience: 400));

            Give(hero, EvolutionStone, 3);

            _authority.Evolve(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-2"));

            Assert.That(PetOf(hero, "pet-1").DefinitionId,
                Is.EqualTo(new DefinitionId(PetCEvolved)));
            Assert.That(PetOf(hero, "pet-2").DefinitionId,
                Is.EqualTo(new DefinitionId(PetDEvolved)));

            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(Count(hero, PetGuard), Is.EqualTo(1));
            Assert.That(hero.Status.Has(new DefinitionId(PetVigour)), Is.False);

            _authority.Activate(Connection, new InstanceId("pet-2"));

            Assert.That(hero.Status.Has(new DefinitionId(PetGuard)), Is.False,
                "the pet that was put away kept buffing");
            Assert.That(Count(hero, PetVigour), Is.EqualTo(1));
        }

        [Test]
        public void TheBuffReachesTheCharactersStatsThroughTheCanonicalCalculation()
        {
            // The fixture's evolved buff is +3 VIT. What matters is that it arrives as a
            // stat modifier through the one status runtime, not that this test knows the
            // number: it is read off the authored effect.
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            var before = new List<StatModifier>();

            hero.Status.CollectModifiers(Effects, before);

            Assert.That(before.Count, Is.Zero);

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));

            var after = new List<StatModifier>();

            hero.Status.CollectModifiers(Effects, after);

            Effects.TryGet(new DefinitionId(PetGuard), out StatusEffectDefinition authored);

            Assert.That(after.Count, Is.EqualTo(authored.StatModifiers.Length),
                "the evolved buff did not reach the canonical modifier collection");
            Assert.That(after[0].Stat, Is.EqualTo(authored.StatModifiers[0].Stat));
            Assert.That(after[0].Value, Is.EqualTo(authored.StatModifiers[0].Value));
        }

        // ---- coming back -------------------------------------------------------------------------

        [Test]
        public void AnEvolvedActivePetComesBackEvolvedActiveAndBuffedOnce()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));

            Assert.That(_players.Save(hero, force: true).IsOk, Is.True);

            // A new world over the same storage.
            _players = NewRegistry();
            _authority = new CharacterPetAuthority(_players, Pets, Items, Effects);

            LivingCharacter returned = Enter("char-a", Connection);

            PetInstance pet = PetOf(returned, "pet-1");

            Assert.That(pet.DefinitionId, Is.EqualTo(new DefinitionId(PetCEvolved)));
            Assert.That(pet.EvolutionStage, Is.EqualTo(1));

            Assert.That(returned.Companion.IsSummoned, Is.True, "the pet did not come back out");
            Assert.That(returned.Companion.IsAuraForm, Is.True,
                "the evolved pet came back as a follower");

            Assert.That(Count(returned, PetGuard), Is.EqualTo(1),
                "the evolved buff was not restored exactly once");
        }

        [Test]
        public void AnEvolvedPetThatWasPutAwayComesBackWithoutABuff()
        {
            LivingCharacter hero = AddPlayer(Pet("pet-1", PetC, level: 3, experience: 400));

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Evolve(Connection, new InstanceId("pet-1"));
            _authority.Deactivate(Connection);

            Assert.That(_players.Save(hero, force: true).IsOk, Is.True);

            _players = NewRegistry();
            _authority = new CharacterPetAuthority(_players, Pets, Items, Effects);

            LivingCharacter returned = Enter("char-a", Connection);

            Assert.That(PetOf(returned, "pet-1").EvolutionStage, Is.EqualTo(1),
                "the pet forgot it had evolved");
            Assert.That(returned.Companion.IsSummoned, Is.False);
            Assert.That(returned.Status.Has(new DefinitionId(PetGuard)), Is.False,
                "a pet nobody has out came back buffing its owner");
        }

        // ---- corrupt rows -------------------------------------------------------------------------

        [Test]
        public void AStageOnAFormNothingEvolvesIntoRefusesTheSpawn()
        {
            Seed("char-x", new[] { Row("pet-1", PetA, 3, 260, 1) }, null);

            WorldSpawnResult result = Spawn("char-x", 21);

            Assert.That(result.IsSpawned, Is.False);
            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
        }

        [Test]
        public void AnEvolvedFormThisWorldDoesNotHaveRefusesTheSpawn()
        {
            Seed("char-x", new[] { Row("pet-1", "pet.nobody-authored-this", 3, 260, 1) },
                null);

            WorldSpawnResult result = Spawn("char-x", 22);

            Assert.That(result.IsSpawned, Is.False);
            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter),
                "an unknown evolved form quietly became something else");
        }

        [Test]
        public void ANegativeStageRefusesTheSpawn()
        {
            Seed("char-x", new[] { Row("pet-1", PetCEvolved, 3, 260, -1) }, null);

            Assert.That(Spawn("char-x", 23).Reason,
                Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
        }

        // ---- presentation ----------------------------------------------------------------------------

        [Test]
        public void AViewerDrawsAnAuraOrAFollowerAndNeverBoth()
        {
            AddPet("pet.viewer.base", buff: PetVigour, thresholds: new[] { 10 },
                verticalOffset: 0.5f);
            AddPet("pet.viewer.aura", buff: PetGuard, thresholds: new[] { 10 },
                auraForm: true);

            var host = new GameObject("viewer");
            var followerObject = new GameObject("follower");
            var auraObject = new GameObject("aura");

            followerObject.transform.SetParent(host.transform, false);
            auraObject.transform.SetParent(host.transform, false);

            _local.Add(host);

            PetPresentationController presenter =
                host.AddComponent<PetPresentationController>();

            Set(presenter, "owner", host.transform);
            Set(presenter, "follower", followerObject.transform);
            Set(presenter, "auraVisual", auraObject);

            // A base form: a follower, and no aura.
            presenter.PresentReplicated("pet.viewer.base", Pets);

            Assert.That(presenter.IsAuraForm, Is.False);
            Assert.That(followerObject.activeSelf, Is.True);
            Assert.That(auraObject.activeSelf, Is.False);

            // The evolved form: an aura, and no follower.
            presenter.PresentReplicated("pet.viewer.aura", Pets);

            Assert.That(presenter.IsAuraForm, Is.True,
                "the aura form was drawn as a follower");
            Assert.That(followerObject.activeSelf, Is.False,
                "a follower and an aura were shown at once");
            Assert.That(auraObject.activeSelf, Is.True);

            // Nothing out: neither.
            presenter.PresentReplicated(string.Empty, Pets);

            Assert.That(followerObject.activeSelf, Is.False);
            Assert.That(auraObject.activeSelf, Is.False);
        }

        [Test]
        public void AViewerNeverDecidesThatAPetHasEvolved()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/PetPresentationController.cs");

            foreach (string forbidden in new[]
            {
                "Level >=", "Experience >=", "TryEvolve", "CanEvolve", "EvolutionStage",
            })
            {
                Assert.That(source.Contains(forbidden), Is.False,
                    "the presenter decides evolution for itself: " + forbidden);
            }
        }

        // ---- the shipped content ------------------------------------------------------------------------

        [Test]
        public void TheShippedCatalogueAuthorsARealEvolutionWithARealRequirement()
        {
            var catalogue = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(
                "Assets/_Game/Data/Production/WorldContentCatalogue.asset");

            DefinitionRegistry<PetDefinition> pets = catalogue.BuildPets();
            DefinitionRegistry<StatusEffectDefinition> effects =
                catalogue.BuildStatusEffects();

            Assert.That(pets.TryGet(new DefinitionId("pet.lumi_slime"),
                out PetDefinition baseForm), Is.True);

            Assert.That(PetService.TryGetNextStage(baseForm, out PetEvolutionStage stage),
                Is.True, "the shipped pet has no authored evolution");

            // A real threshold, not a free upgrade.
            Assert.That(stage.RequiredLevel, Is.GreaterThan(1));
            Assert.That(stage.RequiredExperience, Is.GreaterThan(0));

            Assert.That(pets.TryGet(stage.EvolvedForm, out PetDefinition evolved), Is.True,
                "the shipped chain names a form the world does not have");

            Assert.That(evolved.IsAuraForm, Is.True, "the shipped evolved form is not an aura");
            Assert.That(evolved.BaseBuff.IsValid, Is.True,
                "the shipped evolved form grants nothing");

            Assert.That(effects.TryGet(evolved.BaseBuff,
                out StatusEffectDefinition buff), Is.True,
                "the shipped evolved buff is not in the catalogue");

            Assert.That(buff.StatModifiers.Length, Is.GreaterThan(0),
                "the shipped evolved buff changes nothing");
            Assert.That(buff.Category, Is.EqualTo(StatusEffectCategory.Buff));
        }

        // ---- harness ------------------------------------------------------------------------------------

        private WorldCharacterRegistry NewRegistry()
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            return new WorldCharacterRegistry(_store, spawns, Items, 12, null, Pets,
                Effects);
        }

        private static int Count(LivingCharacter character, string effect)
        {
            var counted = 0;

            IReadOnlyList<ActiveStatusEffect> active = character.Status.Active;

            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].Effect == new DefinitionId(effect)) counted++;
            }

            return counted;
        }

        private static PetInstance PetOf(LivingCharacter owner, string instance)
        {
            Assert.That(owner.TryGetPet(new InstanceId(instance), out PetInstance pet),
                Is.True, owner.Character + " does not own " + instance);

            return pet;
        }

        private static PersistedPet Row(string instance, string pet, int level = 1,
            int experience = 0, int stage = 0)
        {
            return new PersistedPet(new InstanceId(instance), new DefinitionId(pet),
                level, experience, stage);
        }

        /// <summary>A pet row for the fixture's own content.</summary>
        private static PersistedPet Pet(string instance, string definition, int level = 1,
            int experience = 0, int stage = 0)
        {
            return Row(instance, definition, level, experience, stage);
        }

        private void Seed(string character, PersistedPet[] pets, string active)
        {
            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1, null, 12, default, null, pets,
                active == null ? default : new InstanceId(active));
        }

        private WorldSpawnResult Spawn(string character, int connection)
        {
            return _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(HomeMap),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));
        }

        private LivingCharacter Enter(string character, int connection)
        {
            WorldSpawnResult result = Spawn(character, connection);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        private LivingCharacter AddPlayer(params PersistedPet[] pets)
        {
            return AddPlayer(pets, "char-a", Connection);
        }

        private LivingCharacter AddPlayer(PersistedPet pet, string character = "char-a",
            int connection = Connection)
        {
            return AddPlayer(new[] { pet }, character, connection);
        }

        private LivingCharacter AddPlayer(PersistedPet[] pets, string character,
            int connection)
        {
            Seed(character, pets, null);

            return Enter(character, connection);
        }

        /// <summary>Puts an authored material in the bag, as a drop would.</summary>
        private void Give(LivingCharacter character, string item, int quantity)
        {
            for (var i = 0; i < quantity; i++)
            {
                character.Inventory.Add(new ItemInstance(InstanceId.New(),
                    new DefinitionId(item), character.Owner, 1), Items);
            }
        }

        private static void Set(PetPresentationController controller, string field,
            Object value)
        {
            typeof(PetPresentationController)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, value);
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _local.Add(spawn);

            return spawn;
        }
    }
}
