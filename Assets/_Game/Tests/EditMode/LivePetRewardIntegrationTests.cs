using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A pet's experience, from an authoritative defeat to rows in MySQL and back.
    /// </summary>
    /// <remarks>
    /// <b>Nothing between Unity and MySQL is a mock.</b> The decision is written by the
    /// production outbox over HTTP, stored by the production PHP repository, and read back
    /// by a world that shares nothing with the one that decided but the database. Only the
    /// character store is wrapped, and only so a backend outage can be simulated -- every
    /// call it does make is the real one.
    ///
    /// <b>The scenario is the one that cannot be tested any other way.</b> A defeat freezes
    /// a pet, the world dies before the experience is durable, the player swaps pets, and a
    /// fresh world finishes the job. Which pet is paid is decided by a row in MySQL, not by
    /// anything left in memory.
    ///
    /// <b>How to run it.</b> Seed the fixture and serve the API:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public
    /// </code>
    /// Without them every test here skips with a reason rather than failing.
    /// </remarks>
    [TestFixture]
    internal sealed class LivePetRewardIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private const string PetA = "pet.itest-reward-pet";
        private const string PetAEvolved = "pet.itest-reward-pet.aura";
        private const string AuraBuff = "status.itest-reward-aura";

        /// <summary>What the fixture chain asks of a pet before it may evolve.</summary>
        private const int EvolveLevel = 3;
        private const int EvolveExperience = 40;
        private const string Monster = "monster.itest-pet-reward";
        private const string MaxHp = "stat.maxhp";
        private const int MonsterExperience = 200;
        private const float Share = 0.25f;

        /// <summary>
        /// The real store, with a switch for pretending the backend went away.
        /// </summary>
        /// <remarks>Every call it forwards is the production one against PHP and MySQL. The
        /// switch exists because a crash between "the reward is durable" and "the pet's
        /// experience is durable" is the window this suite is about, and there is no other
        /// way to stand in it deliberately.</remarks>
        private sealed class InterruptibleStore : ICharacterStateStore
        {
            private readonly ICharacterStateStore _real;

            public InterruptibleStore(ICharacterStateStore real) => _real = real;

            public bool Broken { get; set; }

            public CharacterPersistenceResult Load(SessionId session)
            {
                return _real.Load(session);
            }

            public CharacterPersistenceResult Save(SessionId session, PersistedCharacter c,
                int revision)
            {
                return Broken
                    ? CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable)
                    : _real.Save(session, c, revision);
            }
        }

        private sealed class ApiToken : HttpCharacterStateStore.ITokenSource
        {
            private readonly HttpAccountApi _api;

            public ApiToken(HttpAccountApi api) => _api = api;

            public bool TryGetToken(SessionId session, out string token)
            {
                token = _api.SessionToken;

                return session == _api.Session && !string.IsNullOrEmpty(token);
            }
        }

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;
        private HttpCharacterStateStore _real;
        private InterruptibleStore _store;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _fixture = IntegrationFixture.Load();

            if (!_fixture.IsAvailable)
            {
                Assert.Ignore("no live backend fixture: " + _fixture.Reason);
            }

            _transport = new UnityWebRequestTransport(BaseAddress, 15);
            _api = new HttpAccountApi(_transport);

            HttpExchange health = _transport.Send("GET", "/api/health", null, null);

            if (!health.IsSuccess)
            {
                Assert.Ignore("no PHP server on " + BaseAddress + " ("
                    + health.FailureKind + ") -- start it with: "
                    + "php -S 127.0.0.1:8099 -t backend/public");
            }

            EnterWorld();

            _real = new HttpCharacterStateStore(_transport, new ApiToken(_api));
            _store = new InterruptibleStore(_real);

            Release();
        }

        [TearDown]
        public void TearDown()
        {
            if (_store != null)
            {
                _store.Broken = false;

                Release();
            }

            if (_api != null && !string.IsNullOrEmpty(_api.SessionToken))
            {
                _api.ReleaseSession(RequestId.New());
            }

            _transport?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the whole journey, through PHP and MySQL --------------------------------------------

        [Test]
        public void ThePetFrozenAtTheDefeatIsPaidOnceEvenAfterTheOwnerSwapsPets()
        {
            // 1. A character with two pets of the same kind, the first one out.
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter hero = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);
            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);

            InstanceId first = hero.Pets[0].InstanceId;
            InstanceId second = hero.Pets[1].InstanceId;

            Assert.That(pets.Activate(1, first).IsAccepted, Is.True);
            Assert.That(world.Save(hero, force: true).IsOk, Is.True);

            // 2. An authoritative defeat, with the backend gone for character writes. The
            //    decision reaches MySQL; nothing it decided does.
            MonsterRewardAuthority rewards = NewAuthority(world, out MonsterWorldRuntime runtime);

            _store.Broken = true;

            LivingMonster corpse = Corpse(runtime);

            rewards.Grant(corpse.Instance, hero.Combatant.CombatantId);

            _store.Broken = false;

            // 3. What MySQL was told, read back through the production outbox.
            PersistedMonsterReward stored = Stored(corpse.Instance);

            Assert.That(stored.Exists, Is.True, "the decision never reached storage");
            Assert.That(stored.PetExperience.Count, Is.EqualTo(1));
            Assert.That(stored.PetExperience[0].Pet, Is.EqualTo(first),
                "the stored decision names the wrong pet");
            Assert.That(stored.PetExperience[0].Owner, Is.EqualTo(hero.Character));
            Assert.That(stored.PetExperience[0].IsDelivered, Is.False);

            int owed = stored.PetExperience[0].Experience;

            Assert.That(owed, Is.EqualTo(Mathf.FloorToInt(
                stored.Experience[0].Experience * Share)),
                "the stored amount is not the authored share of the owner's award");
            Assert.That(owed, Is.GreaterThan(0));

            // 4. The world dies. A fresh one, reading only what MySQL holds.
            WorldCharacterRegistry restarted = NewRegistry();
            LivingCharacter returned = Enter(restarted, connectionId: 2);

            Assert.That(Pet(returned, first).Experience, Is.Zero,
                "precondition: nothing about the delivery was durable");

            // 5. The player puts their other pet out before recovery runs.
            var petsAgain = new CharacterPetAuthority(restarted, PetRegistry());

            Assert.That(petsAgain.Activate(2, second).IsAccepted, Is.True);

            MonsterRewardAuthority recovered = NewAuthority(restarted, out MonsterWorldRuntime _);

            Assert.That(recovered.RecoverPending(), Is.GreaterThan(0),
                "the fresh world found nothing to finish");

            for (var i = 0; i < 320; i++) recovered.RetryHeld();

            // 6. What actually happened, from a third world that shares nothing with either.
            LivingCharacter finally_ = Enter(NewRegistry(), connectionId: 3);

            Assert.That(Pet(finally_, first).Experience, Is.EqualTo(owed),
                "the pet that earned the experience does not have it in MySQL");
            Assert.That(Pet(finally_, second).Experience, Is.Zero,
                "the experience was redirected to the pet the player has out now");

            Assert.That(Pet(finally_, first).AppliedRewardId, Is.EqualTo(stored.RewardId),
                "the pet does not record which reward its experience includes");

            PersistedMonsterReward after = Stored(corpse.Instance);

            Assert.That(after.Exists, Is.False,
                "a reward that owes nothing is still pending");

            // 7. And once more: nothing left to pay a second time.
            for (var i = 0; i < 320; i++) recovered.RetryHeld();

            Assert.That(Pet(Enter(NewRegistry(), connectionId: 4), first).Experience,
                Is.EqualTo(owed), "a second recovery pass paid the pet again");
        }

        [Test]
        public void APetPaidAndSavedButNeverStampedIsNotPaidAgain()
        {
            // The crash window this gate exists to close, against the real database: the
            // pet's experience is durable in MySQL and the reward still says it is owed.
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter hero = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);

            InstanceId pet = hero.Pets[0].InstanceId;

            Assert.That(pets.Activate(1, pet).IsAccepted, Is.True);
            Assert.That(world.Save(hero, force: true).IsOk, Is.True);

            MonsterRewardAuthority rewards = NewAuthority(world, out MonsterWorldRuntime runtime,
                stampsRefused: true);

            LivingMonster corpse = Corpse(runtime);

            rewards.Grant(corpse.Instance, hero.Combatant.CombatantId);

            PersistedMonsterReward stored = Stored(corpse.Instance);

            int owed = stored.PetExperience[0].Experience;

            Assert.That(stored.PetExperience[0].IsDelivered, Is.False,
                "precondition: the stamp never landed");

            // The world dies here. A fresh one reads durable experience and an unstamped
            // reward, and must recognise the difference.
            LivingCharacter returned = Enter(NewRegistry(), connectionId: 2);

            Assert.That(Pet(returned, pet).Experience, Is.EqualTo(owed),
                "precondition: the experience was durable");

            WorldCharacterRegistry third = NewRegistry();
            LivingCharacter again = Enter(third, connectionId: 3);

            MonsterRewardAuthority recovered = NewAuthority(third, out MonsterWorldRuntime _);

            recovered.RecoverPending();

            for (var i = 0; i < 320; i++) recovered.RetryHeld();

            Assert.That(Pet(Enter(NewRegistry(), connectionId: 4), pet).Experience,
                Is.EqualTo(owed), "recovery paid the pet a second time");

            Assert.That(Stored(corpse.Instance).Exists, Is.False,
                "the reconciled reward is still pending");
        }

        [Test]
        public void OverlappingRewardsAreEachAppliedOnceThroughTheRealDatabase()
        {
            // The closure scenario, against MySQL: one reward loses its delivery stamp, the
            // next is delivered normally to a different pet, the world dies, and a fresh one
            // finishes the job. A recipient that remembered only its last reward would pay
            // the first one again here.
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter hero = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);
            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);

            InstanceId first = hero.Pets[0].InstanceId;
            InstanceId second = hero.Pets[1].InstanceId;

            Assert.That(pets.Activate(1, first).IsAccepted, Is.True);
            Assert.That(world.Save(hero, force: true).IsOk, Is.True);

            long experienceBefore = Banked(hero);

            MonsterRewardAuthority rewards = NewAuthority(world, out MonsterWorldRuntime runtime,
                out UnstampedOutbox stamps);

            // R1: everything lands except the stamp.
            stamps.LoseEverything = true;

            LivingMonster firstCorpse = Corpse(runtime);

            rewards.Grant(firstCorpse.Instance, hero.Combatant.CombatantId);

            stamps.LoseEverything = false;

            PersistedMonsterReward r1 = Stored(firstCorpse.Instance);

            Assert.That(r1.Exists, Is.True, "R1 never reached storage");
            Assert.That(r1.Experience[0].IsDelivered, Is.False,
                "precondition: R1's stamp was lost");

            stamps.Unstampable.Add(r1.RewardId);

            // The player swaps pets, and R2 is delivered normally.
            Assert.That(pets.Activate(1, second).IsAccepted, Is.True);

            LivingMonster secondCorpse = Corpse(runtime);

            rewards.Grant(secondCorpse.Instance, hero.Combatant.CombatantId);

            PersistedMonsterReward r2 = Stored(secondCorpse.Instance);

            Assert.That(r2.Exists, Is.False, "R2 did not finish");

            int owed = r1.PetExperience[0].Experience;

            Assert.That(r1.PetExperience[0].Pet, Is.EqualTo(first));

            // What the database holds now, read by a world that shares nothing with this one.
            LivingCharacter between = Enter(NewRegistry(), connectionId: 2);

            long experienceAfterBoth = Banked(between);
            int firstPet = Pet(between, first).Experience;
            int secondPet = Pet(between, second).Experience;

            Assert.That(firstPet, Is.EqualTo(owed));
            Assert.That(secondPet, Is.EqualTo(owed),
                "the second pet was not paid for the second defeat");

            // The world dies, and the backend that lost R1's stamp is back.
            WorldCharacterRegistry restarted = NewRegistry();
            LivingCharacter returned = Enter(restarted, connectionId: 3);

            MonsterRewardAuthority recovered = NewAuthority(restarted,
                out MonsterWorldRuntime _);

            Assert.That(recovered.RecoverPending(), Is.GreaterThan(0),
                "the fresh world found nothing to finish");

            for (var i = 0; i < 320; i++) recovered.RetryHeld();

            // And the last word, from MySQL, through a fourth world.
            LivingCharacter finally_ = Enter(NewRegistry(), connectionId: 4);

            Assert.That(Banked(finally_), Is.EqualTo(experienceAfterBoth),
                "the character was paid one of the two rewards twice");
            Assert.That(Pet(finally_, first).Experience, Is.EqualTo(firstPet),
                "the first pet was paid its reward twice");
            Assert.That(Pet(finally_, second).Experience, Is.EqualTo(secondPet),
                "the second pet was paid its reward twice");

            Assert.That(Banked(finally_) - experienceBefore,
                Is.EqualTo(MonsterExperience * 2),
                "two defeats did not pay exactly twice");

            Assert.That(Stored(firstCorpse.Instance).Exists, Is.False,
                "R1 never reconciled");
        }

        [Test]
        public void APetEarnsItsEvolutionFromRealRewardsAndTheEvolvedFormSurvivesInMySql()
        {
            // 1. A character with one base pet, out.
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter hero = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry(), ItemRegistry(),
                EffectRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);

            InstanceId instance = hero.Pets[0].InstanceId;

            Assert.That(pets.Activate(1, instance).IsAccepted, Is.True);
            Assert.That(hero.Companion.IsAuraForm, Is.False, "the base form is not an aura");
            Assert.That(world.Save(hero, force: true).IsOk, Is.True);

            // 2. Real defeats, real rewards, real pet experience -- nothing is injected.
            MonsterRewardAuthority rewards = NewAuthority(world,
                out MonsterWorldRuntime runtime);

            for (var i = 0; i < 12 && !IsEligible(hero, instance); i++)
            {
                LivingMonster corpse = Corpse(runtime);

                rewards.Grant(corpse.Instance, hero.Combatant.CombatantId);
            }

            Assert.That(IsEligible(hero, instance), Is.True,
                "the pet never reached its authored requirement through real rewards");

            int experience = Pet(hero, instance).Experience;
            int level = Pet(hero, instance).Level;

            // 3. The evolution itself, through the authoritative request, saved to MySQL.
            Assert.That(pets.Evolve(1, instance).IsAccepted, Is.True);

            Assert.That(Pet(hero, instance).DefinitionId,
                Is.EqualTo(new DefinitionId(PetAEvolved)));
            Assert.That(hero.Companion.IsAuraForm, Is.True,
                "the evolved pet is still a follower");
            Assert.That(hero.Status.Has(new DefinitionId(AuraBuff)), Is.True,
                "the evolved form's buff was not applied");

            // 4. What the database holds, read by a world that shares nothing with this one.
            WorldCharacterRegistry restarted = NewRegistry();
            LivingCharacter returned = Enter(restarted, connectionId: 2);

            PetInstance restored = Pet(returned, instance);

            Assert.That(returned.Pets.Count, Is.EqualTo(1),
                "evolving produced a second pet row in MySQL");
            Assert.That(restored.InstanceId, Is.EqualTo(instance),
                "the evolved pet is not the pet that evolved");
            Assert.That(restored.DefinitionId, Is.EqualTo(new DefinitionId(PetAEvolved)),
                "the evolved form did not survive the round trip");
            Assert.That(restored.EvolutionStage, Is.EqualTo(1));
            Assert.That(restored.Experience, Is.EqualTo(experience),
                "the pet lost what it had earned");
            Assert.That(restored.Level, Is.EqualTo(level));

            // 5. And it comes back out as an aura, buffed once.
            Assert.That(returned.Companion.IsSummoned, Is.True,
                "the active selection did not survive");
            Assert.That(returned.Companion.IsAuraForm, Is.True,
                "the evolved pet came back as a follower");
            Assert.That(Count(returned, AuraBuff), Is.EqualTo(1),
                "the aura's buff was restored more than once, or not at all");
        }

        /// <summary>Whether Phase 12 says this pet has earned its next form.</summary>
        private bool IsEligible(LivingCharacter owner, InstanceId instance)
        {
            PetInstance pet = Pet(owner, instance);

            DefinitionRegistry<PetDefinition> pets = PetRegistry();

            pets.TryGet(pet.DefinitionId, out PetDefinition definition);

            return PetService.TryGetNextStage(definition, out PetEvolutionStage stage)
                && pet.Level >= stage.RequiredLevel
                && pet.Experience >= stage.RequiredExperience;
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

        // ---- the world this test builds around the real backend --------------------------------------

        private string PetCharacter => _fixture.RewardCharacterId;

        /// <summary>Writes the character out owning no pets, so each test starts clean.</summary>
        private void Release()
        {
            CharacterPersistenceResult loaded = _real.Load(_api.Session);

            if (!loaded.IsOk || loaded.Character == null) return;

            PersistedCharacter row = loaded.Character;

            if (row.Pets.Count == 0 && !row.ActivePet.IsValid) return;

            _real.Save(_api.Session, new PersistedCharacter(row.Character, row.Account,
                row.Server, row.Name, row.Gender, row.Level, row.Experience,
                row.CurrentHealth, row.CurrentMana, row.Class, row.Job, row.Map, row.Spawn,
                row.Stats, row.Appearance, row.Skills, row.SaveRevision, row.Items,
                row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource),
                row.SaveRevision);
        }

        private WorldAdmission Admission()
        {
            return WorldAdmission.Admitted(_api.Session,
                new AccountId(_fixture.RewardAccountId), new CharacterId(PetCharacter),
                new ServerId(_fixture.ServerId), new ChannelId(_fixture.ChannelId),
                new DefinitionId(_fixture.MapId), new Revision(1), new Revision(1),
                SessionState.EnteringWorld);
        }

        private WorldCharacterRegistry NewRegistry()
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(Spawn());

            return new WorldCharacterRegistry(_store, spawns, ItemRegistry(), 12, null,
                PetRegistry(), EffectRegistry());
        }

        private LivingCharacter Enter(WorldCharacterRegistry registry, int connectionId = 1)
        {
            WorldSpawnResult result = registry.Spawn(connectionId, Admission(),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(result.IsSpawned, Is.True,
                "the real character did not load: " + result.Detail);

            return result.Character;
        }

        private MonsterRewardAuthority NewAuthority(WorldCharacterRegistry registry,
            out MonsterWorldRuntime runtime, bool stampsRefused = false)
        {
            return NewAuthority(registry, out runtime, out UnstampedOutbox _,
                stampsRefused);
        }

        private MonsterRewardAuthority NewAuthority(WorldCharacterRegistry registry,
            out MonsterWorldRuntime runtime, out UnstampedOutbox stamps,
            bool stampsRefused = false)
        {
            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(MonsterDefinition());

            runtime = new MonsterWorldRuntime(registry, monsters, new DefinitionId(MaxHp),
                new CombatTeam(2));

            stamps = new UnstampedOutbox(
                new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)))
            {
                LoseEverything = stampsRefused,
            };

            return new MonsterRewardAuthority(runtime, registry, Curve(), null, null, null,
                null, null, 0f, 0f, null, 0f, stamps, PetRegistry(), Share);
        }

        /// <summary>
        /// The real outbox, with its progress calls dropped.
        /// </summary>
        /// <remarks>Records and reads exactly as production does; only the stamp is lost,
        /// which is the crash window under test. Nothing about the decision is faked.
        /// </remarks>
        private sealed class UnstampedOutbox : IMonsterRewardOutbox
        {
            private readonly IMonsterRewardOutbox _real;

            public UnstampedOutbox(IMonsterRewardOutbox real) => _real = real;

            /// <summary>Reward ids whose stamps are lost. Empty means every stamp lands.</summary>
            /// <remarks>Per reward, because the scenario that matters is one reward losing
            /// its stamp while the next one is delivered normally.</remarks>
            public readonly HashSet<string> Unstampable = new HashSet<string>();

            /// <summary>Loses every stamp, for the window one reward is decided in.</summary>
            public bool LoseEverything { get; set; }

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
                return _real.Record(session, reward);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                return _real.Pending(session);
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete,
                IReadOnlyList<InstanceId> petExperienceDelivered = null)
            {
                if (LoseEverything || Unstampable.Contains(rewardId))
                {
                    return MonsterRewardOutboxResult.Failed(
                        MonsterRewardOutboxFailure.Unreachable, "the world stopped here");
                }

                return _real.Progress(session, rewardId, revision, experienceDelivered,
                    lootClaimed, cursorCommitted, lootPublished, complete,
                    petExperienceDelivered);
            }
        }

        /// <summary>What MySQL holds for this defeat, or nothing when it owes nothing.</summary>
        private PersistedMonsterReward Stored(InstanceId defeat)
        {
            var reader = new HttpMonsterRewardOutbox(_transport, new ApiToken(_api));

            foreach (PersistedMonsterReward reward in reader.Pending(_api.Session))
            {
                if (reward.Defeat == defeat) return reward;
            }

            return default;
        }

        private LivingMonster Corpse(MonsterWorldRuntime runtime)
        {
            runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f,
                new DefinitionId(_fixture.MapId)));

            runtime.PopulateAll();

            // The first one still standing. Taking the first of all of them would hand
            // back a corpse from an earlier defeat once a test kills more than one.
            foreach (LivingMonster living in runtime.All())
            {
                if (!living.IsAlive) continue;

                living.State.ApplyHealthDelta(-10000);

                return living;
            }

            Assert.Fail("no living monster to kill");

            return null;
        }

        /// <summary>
        /// Total experience, level and remainder together.
        /// </summary>
        /// <remarks>The authored curve turns experience into levels, so the remainder alone
        /// says nothing about how much was paid.</remarks>
        private static long Banked(LivingCharacter character)
        {
            return (long)character.Domain.Progression.Level * 1000
                + character.Domain.Progression.Experience;
        }

        private static PetInstance Pet(LivingCharacter owner, InstanceId instance)
        {
            Assert.That(owner.TryGetPet(instance, out PetInstance pet), Is.True,
                owner.Character + " does not own " + instance);

            return pet;
        }

        /// <summary>Signs in and walks the real state machine to in-world.</summary>
        private void EnterWorld()
        {
            _api.PendingLoginIdentifier = _fixture.RewardLoginIdentifier;
            _api.PendingPassword = _fixture.RewardPassword;

            ApiResult<AuthenticatedAccount> login = _api.Authenticate(new LoginRequest(
                RequestId.New(), new VersionSet(new VersionNumber(1, 0, 0),
                    new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0))));

            Assert.That(login.IsOk, Is.True, "login failed: " + login.Error);

            Assert.That(_api.SelectServer(RequestId.New(),
                new ServerId(_fixture.ServerId)).IsOk, Is.True);
            Assert.That(_api.SelectChannel(RequestId.New(),
                new ChannelId(_fixture.ChannelId)).IsOk, Is.True);
            Assert.That(_api.SelectCharacter(RequestId.New(),
                new CharacterId(PetCharacter)).IsOk, Is.True);

            ApiResult<bool> entered = _api.NotifyWorldEntry(login.Value.Account, _api.Session,
                new CharacterId(PetCharacter), new ServerId(_fixture.ServerId),
                new ChannelId(_fixture.ChannelId));

            Assert.That(entered.IsOk, Is.True, "enter-world failed: " + entered.Error);
        }

        // ---- fixture content ---------------------------------------------------------------------------

        private DefinitionRegistry<PetDefinition> PetRegistry()
        {
            var pets = new DefinitionRegistry<PetDefinition>();

            // The base form, and the aura it evolves into. A chain, so the live slice
            // exercises the authored model rather than a single definition.
            PetDefinition baseForm = Pet(PetA, aura: false, buff: null);

            typeof(PetDefinition)
                .GetField("_evolutionStages", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(baseForm, new[]
                {
                    new PetEvolutionStage(new DefinitionId(PetAEvolved), EvolveLevel,
                        EvolveExperience),
                });

            pets.Register(baseForm);
            pets.Register(Pet(PetAEvolved, aura: true, buff: AuraBuff));

            return pets;
        }

        /// <summary>Ten experience a level, to twenty. Fixture content, like every number here.</summary>
        private PetDefinition Pet(string id, bool aura, string buff)
        {
            var definition = ScriptableObject.CreateInstance<PetDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id
                + ".name\"},\"_followBehavior\":0,\"_verticalOffset\":0,\"_maxLevel\":20,"
                + "\"_baseBuff\":{\"_value\":\"" + (buff ?? string.Empty) + "\"},"
                + "\"_auraForm\":" + (aura ? "true" : "false")
                + ",\"_disabled\":false}", definition);

            var thresholds = new int[19];

            for (var i = 0; i < thresholds.Length; i++) thresholds[i] = (i + 1) * 10;

            typeof(PetDefinition)
                .GetField("_experienceThresholds", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, thresholds);

            _created.Add(definition);

            return definition;
        }

        /// <summary>The aura's buff: one stat modifier, authored like any other effect.</summary>
        private DefinitionRegistry<StatusEffectDefinition> EffectRegistry()
        {
            var effects = new DefinitionRegistry<StatusEffectDefinition>();

            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + AuraBuff + "\"},\"_nameKey\":{\"_key\":\""
                + AuraBuff + ".name\"},\"_category\":1,\"_controlEffect\":0,"
                + "\"_durationSeconds\":0,\"_stackBehavior\":2,\"_maxStacks\":1,"
                + "\"_statModifiers\":[{\"_stat\":{\"_value\":\"stat.max_hp\"},"
                + "\"_kind\":1,\"_value\":0.03}]}", definition);

            _created.Add(definition);

            effects.Register(definition);

            return effects;
        }

        private DefinitionRegistry<ItemDefinition> ItemRegistry()
        {
            return new DefinitionRegistry<ItemDefinition>();
        }

        private MonsterDefinition MonsterDefinition()
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Monster + "\"},\"_level\":5,"
                + "\"_aggressionType\":2,\"_experienceReward\":" + MonsterExperience
                + ",\"_attackRange\":2,"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}",
                definition);

            // Health is authored as a stat, and a monster without one never spawns.
            typeof(MonsterDefinition)
                .GetField("_baseStats", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition,
                    new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            _created.Add(definition);

            return definition;
        }

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            var costs = new System.Text.StringBuilder();

            for (var level = 1; level < 60; level++)
            {
                if (level > 1) costs.Append(',');

                costs.Append(1000);
            }

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.itest-pet-reward\"},\"_minLevel\":1,"
                + "\"_maxLevel\":60,\"_experienceToNextLevel\":[" + costs + "]}", definition);

            _created.Add(definition);

            return definition;
        }

        private SpawnPointDefinition Spawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.itest\"},\"_map\":{\"_value\":\""
                + _fixture.MapId + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }
    }
}
