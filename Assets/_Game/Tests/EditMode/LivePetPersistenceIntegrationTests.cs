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
    /// A pet, from the world's own state to rows in MySQL and back.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is a mock.</b> The pet is granted through the production authority,
    /// written by the production store over HTTP, stored by the production PHP repository in
    /// MySQL, and read back by a second registry that shares nothing with the first but the
    /// database. Everything else about pets can be proven against a fake store; that the
    /// character who owned one still owns it tomorrow cannot.
    ///
    /// <b>The character is left as it was found.</b> Every pet this suite creates is
    /// released and written out again in teardown, so the shared fixture character is the
    /// same before and after -- other suites assert its level and its bag.
    ///
    /// <b>How to run it.</b> Seed the fixture and serve the API:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public
    /// </code>
    /// Without them every test here skips with a reason rather than failing.
    /// </remarks>
    [TestFixture]
    internal sealed class LivePetPersistenceIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private const string PetA = "pet.itest-a";
        private const string PetB = "pet.itest-b";

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;
        private HttpCharacterStateStore _store;
        private readonly List<Object> _created = new List<Object>();

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

            _store = new HttpCharacterStateStore(_transport, new ApiToken(_api));

            // Whatever a previous run left behind, so each test starts from "owns none".
            Release();
        }

        [TearDown]
        public void TearDown()
        {
            if (_store != null) Release();

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

        // ---- A: a pet becomes a row ---------------------------------------------------------

        [Test]
        public void AGrantedPetIsStillOwnedAfterTheWorldForgetsEverything()
        {
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter character = Enter(world);

            CharacterPetAuthority pets = new CharacterPetAuthority(world, PetRegistry());

            CharacterPetResult granted = pets.Grant(1, new DefinitionId(PetA));

            Assert.That(granted.IsAccepted, Is.True, granted.ToString());

            InstanceId instance = character.Pets[0].InstanceId;

            Assert.That(Save(world, character).IsOk, Is.True);

            // A second world that shares nothing with the first but the database.
            LivingCharacter returned = Enter(NewRegistry(), connectionId: 2);

            Assert.That(returned.Pets.Count, Is.EqualTo(1),
                "the granted pet did not survive the round trip");
            Assert.That(returned.Pets[0].InstanceId, Is.EqualTo(instance),
                "the pet came back as a different one");
            Assert.That(returned.Pets[0].DefinitionId.Value, Is.EqualTo(PetA));
        }

        // ---- B: level one is still a pet ------------------------------------------------------

        [Test]
        public void APetWithNothingEarnedYetIsStoredAnyway()
        {
            // The Phase 18.16A defect in its pet-shaped form: whether a row exists is
            // decided by the character owning a pet, never by its numbers being
            // interesting. A freshly granted pet is the least interesting one there is.
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter character = Enter(world);

            Assert.That(new CharacterPetAuthority(world, PetRegistry())
                .Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);

            Assert.That(character.Pets[0].Level, Is.EqualTo(1));
            Assert.That(character.Pets[0].Experience, Is.Zero);
            Assert.That(character.Pets[0].EvolutionStage, Is.Zero);

            Assert.That(Save(world, character).IsOk, Is.True);

            LivingCharacter returned = Enter(NewRegistry(), connectionId: 2);

            Assert.That(returned.Pets.Count, Is.EqualTo(1),
                "a pet at level one with no experience was not written down");
            Assert.That(returned.Pets[0].Level, Is.EqualTo(1));
            Assert.That(returned.Pets[0].Experience, Is.Zero);
            Assert.That(returned.Pets[0].EvolutionStage, Is.Zero);
        }

        // ---- C: which one is out --------------------------------------------------------------

        [Test]
        public void ThePetThatWasOutIsTheOneThatComesBackOut()
        {
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter character = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);
            Assert.That(pets.Grant(1, new DefinitionId(PetB)).IsAccepted, Is.True);

            InstanceId second = character.Pets[1].InstanceId;

            Assert.That(pets.Activate(1, second).IsAccepted, Is.True);
            Assert.That(Save(world, character).IsOk, Is.True);

            LivingCharacter returned = Enter(NewRegistry(), connectionId: 2);

            Assert.That(returned.Pets.Count, Is.EqualTo(2),
                "one of the two pets was lost");
            Assert.That(returned.Companion.IsSummoned, Is.True,
                "the pet that was out did not come back out");
            Assert.That(returned.Companion.Summoned.InstanceId, Is.EqualTo(second),
                "a different pet came back out");
            Assert.That(returned.Companion.Summoned.DefinitionId.Value, Is.EqualTo(PetB));
        }

        // ---- D: putting it away ----------------------------------------------------------------

        [Test]
        public void APetPutAwayStaysAwayAndStaysOwned()
        {
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter character = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);
            Assert.That(pets.Activate(1, character.Pets[0].InstanceId).IsAccepted, Is.True);
            Assert.That(Save(world, character).IsOk, Is.True);

            // Out, in the database, before anything is put away.
            LivingCharacter middle = Enter(NewRegistry(), connectionId: 2);

            Assert.That(middle.Companion.IsSummoned, Is.True);

            WorldCharacterRegistry third = NewRegistry();
            LivingCharacter again = Enter(third, connectionId: 3);

            Assert.That(new CharacterPetAuthority(third, PetRegistry())
                .Deactivate(3).IsAccepted, Is.True);
            Assert.That(Save(third, again).IsOk, Is.True);

            LivingCharacter returned = Enter(NewRegistry(), connectionId: 4);

            Assert.That(returned.Companion.IsSummoned, Is.False,
                "a pet that was put away came back out");
            Assert.That(returned.Pets.Count, Is.EqualTo(1),
                "putting a pet away released it");
        }

        // ---- E: releasing one leaves nothing behind -----------------------------------------------

        [Test]
        public void AReleasedPetLeavesNoRowAndNoDanglingSelection()
        {
            WorldCharacterRegistry world = NewRegistry();
            LivingCharacter character = Enter(world);

            var pets = new CharacterPetAuthority(world, PetRegistry());

            Assert.That(pets.Grant(1, new DefinitionId(PetA)).IsAccepted, Is.True);
            Assert.That(pets.Grant(1, new DefinitionId(PetB)).IsAccepted, Is.True);
            Assert.That(pets.Activate(1, character.Pets[0].InstanceId).IsAccepted, Is.True);
            Assert.That(Save(world, character).IsOk, Is.True);

            // The world lets both go, which is what a save with no pets means.
            Release();

            LivingCharacter returned = Enter(NewRegistry(), connectionId: 2);

            Assert.That(returned.Pets.Count, Is.Zero, "a released pet is still owned");
            Assert.That(returned.Companion.IsSummoned, Is.False,
                "the selection outlived the pet it pointed at");
        }

        // ---- the world this test builds around the real store ---------------------------------------

        /// <summary>The character this suite is allowed to change.</summary>
        /// <remarks>The same one the progression suite writes to, and left exactly as it was
        /// found: pets are additive state, and teardown removes every one this suite made.
        /// </remarks>
        private string PetCharacter => _fixture.RewardCharacterId;

        /// <summary>
        /// Writes the character out owning no pets at all.
        /// </summary>
        /// <remarks>
        /// At the store rather than through the world, deliberately. Releasing a pet is not
        /// something the game does -- there is no give-away action -- so there is no
        /// authority to ask, and inventing one so a test could tidy up would put a feature
        /// in the game that nobody asked for. This rewrites the stored row with the pets
        /// left out, which is exactly what the persistence boundary says "owns none" is.
        ///
        /// Everything else on the row is carried across unchanged, so the shared fixture
        /// character keeps the level, bag and skills other suites assert.
        /// </remarks>
        private void Release()
        {
            CharacterPersistenceResult loaded = _store.Load(_api.Session);

            if (!loaded.IsOk || loaded.Character == null) return;

            PersistedCharacter row = loaded.Character;

            if (row.Pets.Count == 0 && !row.ActivePet.IsValid) return;

            var without = new PersistedCharacter(row.Character, row.Account, row.Server,
                row.Name, row.Gender, row.Level, row.Experience, row.CurrentHealth,
                row.CurrentMana, row.Class, row.Job, row.Map, row.Spawn, row.Stats,
                row.Appearance, row.Skills, row.SaveRevision, row.Items,
                row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource);

            _store.Save(_api.Session, without, row.SaveRevision);
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

            return new WorldCharacterRegistry(_store, spawns, null, 12, null,
                PetRegistry());
        }

        private LivingCharacter Enter(WorldCharacterRegistry registry, int connectionId = 1)
        {
            WorldSpawnResult result = registry.Spawn(connectionId, Admission(),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(result.IsSpawned, Is.True,
                "the real character did not load: " + result.Detail);

            return result.Character;
        }

        private CharacterPersistenceResult Save(WorldCharacterRegistry world,
            LivingCharacter character)
        {
            CharacterPersistenceResult saved = world.Save(character, force: true);

            Assert.That(saved.IsOk, Is.True, "the backend refused the save: "
                + saved.Failure);

            return saved;
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

        /// <summary>Two authored pets, so "two of one kind" is not the only case tested.</summary>
        private DefinitionRegistry<PetDefinition> PetRegistry()
        {
            var pets = new DefinitionRegistry<PetDefinition>();

            pets.Register(Pet(PetA, 0f));
            pets.Register(Pet(PetB, 0.75f));

            return pets;
        }

        private PetDefinition Pet(string id, float verticalOffset)
        {
            var definition = ScriptableObject.CreateInstance<PetDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id
                + ".name\"},\"_followBehavior\":0,\"_verticalOffset\":" + verticalOffset
                + ",\"_maxLevel\":5,\"_auraForm\":false,\"_disabled\":false}", definition);

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
