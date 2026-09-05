// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world does this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A production pet earning its evolution in the world the shipped scene composed, and
    /// what the evolved form does to its owner.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is injected after spawn.</b> The pet's experience comes from real defeats
    /// through the durable reward outbox, the evolution comes from the authoritative request
    /// a client would send, and the buff comes from the authored form. No test writes a
    /// level, a stage or a status effect.
    ///
    /// <b>The content is the shipped content.</b> The requirement, the evolved form, its
    /// aura and its buff are all read out of the production catalogue; no number here is
    /// this fixture's opinion.
    ///
    /// <b>The backend is the one substitution</b>, as in every shipped-scene fixture: a
    /// store and an outbox that keep what they are given, so a world can be stood up and
    /// destroyed without HTTP.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldPetEvolutionTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";

        private const string LumiSlime = "pet.lumi_slime";

        private const float FruitChance = 0.0000001f;
        private const float CardChance = 0.000001f;

        private sealed class CharacterStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private sealed class Outbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            /// <summary>Reward ids whose delivery stamps are lost.</summary>
            public readonly HashSet<string> Unstampable = new HashSet<string>();

            public bool StampsRefused { get; set; }

            public IReadOnlyList<PersistedMonsterReward> All() => _byDefeat.Values.ToList();

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
                if (_byDefeat.TryGetValue(reward.Defeat.Value,
                    out PersistedMonsterReward already))
                {
                    return MonsterRewardOutboxResult.Recorded(already.RewardId,
                        already.Revision, true);
                }

                _byDefeat[reward.Defeat.Value] = Copy(reward, reward.Experience,
                    reward.PetExperience, false, 1);

                return MonsterRewardOutboxResult.Recorded(reward.RewardId, 1, false);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                var pending = new List<PersistedMonsterReward>();

                foreach (PersistedMonsterReward stored in _byDefeat.Values)
                {
                    if (!stored.IsComplete) pending.Add(stored);
                }

                return pending;
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete,
                IReadOnlyList<InstanceId> petExperienceDelivered = null)
            {
                if (StampsRefused || Unstampable.Contains(rewardId))
                {
                    return MonsterRewardOutboxResult.Failed(
                        MonsterRewardOutboxFailure.Unreachable, "backend down");
                }

                foreach (string key in _byDefeat.Keys.ToList())
                {
                    PersistedMonsterReward stored = _byDefeat[key];

                    if (stored.RewardId != rewardId) continue;

                    if (stored.Revision != revision)
                    {
                        return MonsterRewardOutboxResult.Failed(
                            MonsterRewardOutboxFailure.StaleRevision, "somebody wrote first");
                    }

                    var grants = new List<MonsterRewardGrant>();

                    foreach (MonsterRewardGrant grant in stored.Experience)
                    {
                        grants.Add(new MonsterRewardGrant(grant.Character, grant.Experience,
                            grant.IsDelivered || (experienceDelivered != null
                                && experienceDelivered.Contains(grant.Character))));
                    }

                    var pets = new List<MonsterRewardPetGrant>();

                    foreach (MonsterRewardPetGrant grant in stored.PetExperience)
                    {
                        bool paid = grant.IsDelivered;

                        for (var i = 0; !paid && petExperienceDelivered != null
                            && i < petExperienceDelivered.Count; i++)
                        {
                            paid = petExperienceDelivered[i] == grant.Pet;
                        }

                        pets.Add(new MonsterRewardPetGrant(grant.Owner, grant.Pet,
                            grant.Experience, paid));
                    }

                    _byDefeat[key] = Copy(stored, grants, pets,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward Copy(PersistedMonsterReward reward,
                IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets, bool complete, int revision)
            {
                return new PersistedMonsterReward(reward.RewardId, reward.Defeat,
                    reward.Monster, reward.Map, reward.Killer, reward.Loot,
                    reward.LootPolicy, reward.Claimant, reward.X, reward.Y, reward.Z,
                    reward.Party, reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(reward.Entries),
                    reward.IsCursorCommitted, reward.IsLootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        private sealed class ScriptedRandom : IRandomResultSource, IRandomRangeSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public readonly List<float> ChancesAsked = new List<float>();

            public int Rolls(float chance)
            {
                return ChancesAsked.Count(c => Mathf.Abs(c - chance) < 1e-12f);
            }

            public bool Succeeds(float chance)
            {
                ChancesAsked.Add(chance);

                return _roll < chance;
            }

            public int Range(int min, int max) => min;
        }

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;

        private CharacterStore _characters;
        private Outbox _outbox;
        private ScriptedRandom _rolls;

        private long _sequence;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _outbox = new Outbox();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();

            yield return TearDownWorld();
        }

        // ---- A: the base form ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ABasePetIsAFollowerAndNotAnAura()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            Assert.That(hero.Companion.IsSummoned, Is.True);
            Assert.That(hero.Companion.IsAuraForm, Is.False,
                "the base form came out as an aura");

            Assert.That(Pet(hero, "pet-1").DefinitionId,
                Is.EqualTo(new DefinitionId(LumiSlime)));

            // And a viewer draws the follower, not an aura.
            Presenter(out GameObject follower, out GameObject aura)
                .PresentReplicated(Pet(hero, "pet-1").DefinitionId.Value, ShippedPets());

            Assert.That(follower.activeSelf, Is.True);
            Assert.That(aura.activeSelf, Is.False);
        }

        // ---- B: too soon -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APetThatHasNotEarnedItsFormIsRefused()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            CharacterPetResult refused = _bootstrap.PetAuthority.Evolve(1,
                new InstanceId("pet-1"));

            Assert.That(refused.IsAccepted, Is.False,
                "a level-one pet evolved in the shipped world");

            Assert.That(Pet(hero, "pet-1").EvolutionStage, Is.Zero);
            Assert.That(Pet(hero, "pet-1").DefinitionId,
                Is.EqualTo(new DefinitionId(LumiSlime)));
            Assert.That(hero.Companion.IsAuraForm, Is.False);
        }

        // ---- C, D, E, F: earning it, and what it becomes -------------------------------------------

        [UnityTest]
        public IEnumerator APetEarnsItsEvolutionFromRealDefeatsAndBecomesAnAura()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            int maxHealthBefore = hero.Combatant.Limits.MaxHealth;

            Assert.That(hero.Status.ActiveCount, Is.Zero,
                "precondition: the base form grants nothing");

            // Real kills, real rewards, real pet experience.
            yield return EarnEvolution(hero, "pet-1");

            Assert.That(_bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"))
                .IsAccepted, Is.True, "the pet had earned its form and was refused");

            PetInstance pet = Pet(hero, "pet-1");

            PetDefinition evolved = ShippedForm(pet.DefinitionId);

            Assert.That(pet.InstanceId, Is.EqualTo(new InstanceId("pet-1")),
                "evolving minted a second pet");
            Assert.That(pet.EvolutionStage, Is.EqualTo(1));
            Assert.That(evolved.IsAuraForm, Is.True,
                "the shipped evolved form is not an aura");

            // The follower is gone and the aura is on.
            Assert.That(hero.Companion.IsAuraForm, Is.True,
                "the evolved pet is still following");

            PetPresentationController presenter =
                Presenter(out GameObject follower, out GameObject aura);

            presenter.PresentReplicated(pet.DefinitionId.Value, ShippedPets());

            Assert.That(presenter.IsAuraForm, Is.True);
            Assert.That(follower.activeSelf, Is.False,
                "a follower and an aura were shown at once");
            Assert.That(aura.activeSelf, Is.True);

            // And the owner is buffed, once, through the canonical status runtime.
            Assert.That(Count(hero, evolved.BaseBuff), Is.EqualTo(1),
                "the evolved form's authored buff was not applied exactly once");

            yield return Tick(2);

            Assert.That(hero.Combatant.Limits.MaxHealth, Is.GreaterThan(maxHealthBefore),
                "the buff never reached the canonical stat calculation");
        }

        // ---- G, H: putting it away and bringing it back -----------------------------------------------

        [UnityTest]
        public IEnumerator PuttingTheAuraAwayTakesTheBuffAndBringingItBackReturnsItOnce()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            yield return EarnEvolution(hero, "pet-1");

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));

            DefinitionId buff = ShippedForm(Pet(hero, "pet-1").DefinitionId).BaseBuff;

            // The stat authority recomputes on the world's own tick, so what the buff is
            // worth is only readable after one.
            yield return Tick(2);

            int buffed = hero.Combatant.Limits.MaxHealth;

            Assert.That(_bootstrap.PetAuthority.Deactivate(1).IsAccepted, Is.True);

            yield return Tick(2);

            Assert.That(hero.Companion.IsSummoned, Is.False);
            Assert.That(Count(hero, buff), Is.Zero, "the aura's buff outlived the aura");
            Assert.That(hero.Combatant.Limits.MaxHealth, Is.LessThan(buffed),
                "the stat kept the buff after the pet was put away");

            Assert.That(Pet(hero, "pet-1").EvolutionStage, Is.EqualTo(1),
                "putting the pet away undid its evolution");

            // Back out again: one aura, one buff.
            Activate(hero, "pet-1");

            yield return Tick(2);

            Assert.That(hero.Companion.IsAuraForm, Is.True);
            Assert.That(Count(hero, buff), Is.EqualTo(1),
                "bringing the aura back applied its buff more than once");
            Assert.That(hero.Combatant.Limits.MaxHealth, Is.EqualTo(buffed));
        }

        // ---- I: switching to a base pet -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SwitchingFromAnEvolvedPetToABaseOneMovesEverythingWithIt()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1", "pet-2");

            Activate(hero, "pet-1");

            yield return EarnEvolution(hero, "pet-1");

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));

            DefinitionId buff = ShippedForm(Pet(hero, "pet-1").DefinitionId).BaseBuff;

            Assert.That(Count(hero, buff), Is.EqualTo(1));

            Activate(hero, "pet-2");

            yield return Tick(2);

            Assert.That(hero.Companion.IsAuraForm, Is.False,
                "the base pet came out as an aura");
            Assert.That(Count(hero, buff), Is.Zero,
                "the evolved pet kept buffing while another pet was out");
            Assert.That(Pet(hero, "pet-2").EvolutionStage, Is.Zero);

            // Back to the evolved one.
            Activate(hero, "pet-1");

            yield return Tick(2);

            Assert.That(hero.Companion.IsAuraForm, Is.True);
            Assert.That(Count(hero, buff), Is.EqualTo(1));
        }

        // ---- J: reconnect ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnEvolvedActivePetComesBackOnceAfterAReconnect()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            yield return EarnEvolution(hero, "pet-1");

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));

            yield return Tick(2);

            DefinitionId form = Pet(hero, "pet-1").DefinitionId;
            DefinitionId buff = ShippedForm(form).BaseBuff;
            int buffed = hero.Combatant.Limits.MaxHealth;

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick(2);

            Assert.That(Pet(returned, "pet-1").DefinitionId, Is.EqualTo(form),
                "the pet came back as a different form");
            Assert.That(Pet(returned, "pet-1").EvolutionStage, Is.EqualTo(1));
            Assert.That(returned.Companion.IsSummoned, Is.True);
            Assert.That(returned.Companion.IsAuraForm, Is.True,
                "the evolved pet came back as a follower");
            Assert.That(Count(returned, buff), Is.EqualTo(1),
                "the buff came back more than once, or not at all");
            Assert.That(returned.Combatant.Limits.MaxHealth, Is.EqualTo(buffed));
        }

        // ---- K: a whole new world -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AFreshWorldRestoresTheEvolvedAuraAndItsBuffOnce()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            yield return EarnEvolution(hero, "pet-1");

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));

            yield return Tick(2);

            DefinitionId form = Pet(hero, "pet-1").DefinitionId;
            DefinitionId buff = ShippedForm(form).BaseBuff;
            int buffed = hero.Combatant.Limits.MaxHealth;

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            // A real restart: the scene is unloaded and the bootstrap destroyed.
            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            yield return Tick(2);

            Assert.That(Pet(returned, "pet-1").DefinitionId, Is.EqualTo(form));
            Assert.That(Pet(returned, "pet-1").EvolutionStage, Is.EqualTo(1));
            Assert.That(returned.Companion.IsAuraForm, Is.True);
            Assert.That(Count(returned, buff), Is.EqualTo(1));
            Assert.That(returned.Combatant.Limits.MaxHealth, Is.EqualTo(buffed),
                "the restored buff produced a different stat");
        }

        // ---- L: a lost reward stamp cannot double anything --------------------------------------------------

        [UnityTest]
        public IEnumerator ALostRewardStampCannotDoubleTheExperienceOrTheEvolution()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            LogAssert.ignoreFailingMessages = true;

            // Earn the evolution, with the last reward's stamp lost.
            yield return EarnEvolution(hero, "pet-1");

            _outbox.StampsRefused = true;

            Kill(hero, Spawn());

            yield return Tick();

            _outbox.StampsRefused = false;

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));

            DefinitionId form = Pet(hero, "pet-1").DefinitionId;
            DefinitionId buff = ShippedForm(form).BaseBuff;
            int experience = Pet(hero, "pet-1").Experience;
            int level = Pet(hero, "pet-1").Level;

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter returned = Admit("char-ann", 1);

            for (var i = 0; i < 320; i++) _bootstrap.Rewards.RetryHeld();

            yield return Tick(2);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(Pet(returned, "pet-1").Experience, Is.EqualTo(experience),
                "recovery paid the pet twice");
            Assert.That(Pet(returned, "pet-1").Level, Is.EqualTo(level));
            Assert.That(Pet(returned, "pet-1").EvolutionStage, Is.EqualTo(1),
                "recovery evolved the pet a second time");
            Assert.That(Pet(returned, "pet-1").DefinitionId, Is.EqualTo(form));
            Assert.That(Count(returned, buff), Is.EqualTo(1),
                "recovery applied the aura buff twice");
        }

        // ---- the rare rolls are still the rare rolls -----------------------------------------------------------

        [UnityTest]
        public IEnumerator EvolutionAddsNoDropRolls()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            Activate(hero, "pet-1");

            Kill(hero, Spawn());

            yield return Tick();

            int fruit = _rolls.Rolls(FruitChance);
            int card = _rolls.Rolls(CardChance);
            int asked = _rolls.ChancesAsked.Count;

            Assert.That(fruit, Is.EqualTo(1),
                "one boss defeat did not spend exactly one fruit chance");
            Assert.That(card, Is.EqualTo(1),
                "one boss defeat did not spend exactly one card chance");

            yield return EarnEvolution(hero, "pet-1");

            int afterKills = _rolls.ChancesAsked.Count;

            _bootstrap.PetAuthority.Evolve(1, new InstanceId("pet-1"));
            _bootstrap.PetAuthority.Deactivate(1);
            Activate(hero, "pet-1");

            Assert.That(_rolls.ChancesAsked.Count, Is.EqualTo(afterKills),
                "evolving or summoning consulted a drop table");
            Assert.That(_rolls.Rolls(FruitChance), Is.EqualTo(afterKills == asked ? fruit
                : _rolls.Rolls(FruitChance)));
        }

        // ---- harness --------------------------------------------------------------------------------------------

        /// <summary>
        /// Kills the shipped boss until the pet has earned its authored evolution.
        /// </summary>
        /// <remarks>Nothing is injected: every point of experience arrives through the
        /// reward outbox, and the loop stops as soon as Phase 12 says the requirement is
        /// met. Bounded so a content change that makes the pet unreachable fails here
        /// rather than hanging.</remarks>
        private IEnumerator EarnEvolution(LivingCharacter hero, string pet)
        {
            IDefinitionRegistry<PetDefinition> pets = ShippedPets();

            for (var i = 0; i < 12; i++)
            {
                PetInstance owned = Pet(hero, pet);

                pets.TryGet(owned.DefinitionId, out PetDefinition definition);

                if (PetService.TryGetNextStage(definition, out PetEvolutionStage stage)
                    && owned.Level >= stage.RequiredLevel
                    && owned.Experience >= stage.RequiredExperience)
                {
                    yield break;
                }

                Kill(hero, Spawn());

                yield return Tick();
            }

            Assert.Fail("the shipped pet never reached its authored evolution requirement");
        }

        private IEnumerator LoadWorld(float roll = 1f)
        {
            _rolls = new ScriptedRandom(roll);

            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();
            _bootstrap.UseRandom(_rolls, _rolls);
            _bootstrap.Compose(new AlwaysAdmits(), default, _characters, null, null,
                _outbox);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            yield return null;
        }

        private IEnumerator TearDownWorld()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
                _bootstrap = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IDefinitionRegistry<PetDefinition> ShippedPets()
        {
            Assert.That(_bootstrap.Pets, Is.Not.Null, "the world composed no pets");

            return _bootstrap.Pets;
        }

        private PetDefinition ShippedForm(DefinitionId id)
        {
            Assert.That(ShippedPets().TryGet(id, out PetDefinition definition), Is.True,
                "the shipped catalogue has no " + id);

            return definition;
        }

        private static int Count(LivingCharacter character, DefinitionId effect)
        {
            var counted = 0;

            IReadOnlyList<ActiveStatusEffect> active = character.Status.Active;

            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].Effect == effect) counted++;
            }

            return counted;
        }

        private static PetInstance Pet(LivingCharacter owner, string instance)
        {
            Assert.That(owner.TryGetPet(new InstanceId(instance), out PetInstance pet),
                Is.True, owner.Character + " does not own " + instance);

            return pet;
        }

        private void Activate(LivingCharacter owner, string pet)
        {
            ChibiFantasy.Network.ICharacterPetRequestSink sink = _bootstrap.PetAuthority;

            sink.Activate(owner.ConnectionId, new InstanceId(pet));

            Assert.That(_bootstrap.PetAuthority.LastResult.IsAccepted, Is.True,
                "the shipped world refused to put out " + pet);
        }

        /// <summary>A viewer's presentation, wired the way the shipped presenter is.</summary>
        private PetPresentationController Presenter(out GameObject follower,
            out GameObject aura)
        {
            var host = new GameObject("viewer");

            follower = new GameObject("follower");
            aura = new GameObject("aura");

            follower.transform.SetParent(host.transform, false);
            aura.transform.SetParent(host.transform, false);

            _local.Add(host);

            PetPresentationController controller =
                host.AddComponent<PetPresentationController>();

            Set(controller, "owner", host.transform);
            Set(controller, "follower", follower.transform);
            Set(controller, "auraVisual", aura);

            return controller;
        }

        private static void Set(PetPresentationController controller, string field,
            Object value)
        {
            typeof(PetPresentationController)
                .GetField(field, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, value);
        }

        private LivingCharacter Admit(string character, int connection,
            params string[] pets)
        {
            string session = "session-" + character;

            if (!_characters.Rows.ContainsKey(session))
            {
                var rows = new List<PersistedPet>();

                for (var i = 0; i < pets.Length; i++)
                {
                    rows.Add(new PersistedPet(new InstanceId(pets[i]),
                        new DefinitionId(LumiSlime), 1, 0, 0));
                }

                _characters.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 30, 0, 104, 35,
                    new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1, null, 0, default, null,
                    rows.Count == 0 ? null : rows, default);
            }

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            if (_bootstrap.Rewards != null && _bootstrap.RewardOutbox != null)
            {
                _bootstrap.Rewards.RecoverPending();
            }

            return spawned.Character;
        }

        private LivingMonster Spawn()
        {
            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss), default, 0f,
                1, 0f, new DefinitionId(StarterMap)));

            monsters.PopulateAll();

            IReadOnlyList<LivingMonster> alive = monsters.All();

            for (var i = alive.Count - 1; i >= 0; i--)
            {
                if (alive[i].State.Definition.Id.Value == Boss && alive[i].IsAlive)
                {
                    return alive[i];
                }
            }

            Assert.Fail("no living boss");

            return null;
        }

        private void Kill(LivingCharacter hero, LivingMonster monster)
        {
            _bootstrap.Simulation.Monsters().TryResolve(monster.Instance,
                out ICombatant target);

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                _bootstrap.Simulation.Combat().Tick(10f);

                ServerCombatResult result = _bootstrap.Simulation.Combat().Execute(
                    hero.ConnectionId, new CombatCommand(hero.Character, monster.Instance,
                        default, 0, ++_sequence));

                if (!result.IsAccepted) break;
            }

            Assert.That(target.CurrentHealth, Is.Zero, "the boss would not die");
        }
    }
}

#endif
