using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Where a character stood survives the whole round trip, over the real API.
    /// </summary>
    /// <remarks>
    /// <b>Why a live test and not another fake one.</b> The position crosses five boundaries:
    /// a C# contract, a JSON body, an HTTP endpoint, a PHP repository and a MySQL column.
    /// Every one of those was covered by its own test and every one of them passed, and the
    /// value still did not arrive. The only thing none of them exercised was all five at
    /// once, which is exactly where it was being lost.
    ///
    /// <b>How to run it.</b> Seed the fixture and serve the API:
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public
    /// Without those this ignores itself rather than failing.
    /// </remarks>
    internal sealed class LiveCharacterPositionIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;
        private HttpCharacterStateStore _store;

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
                Assert.Ignore("no PHP server on " + BaseAddress + " (" + health.FailureKind + ")");
            }

            EnterWorld();

            _store = new HttpCharacterStateStore(_transport, new ApiToken(_api));
        }

        [TearDown]
        public void TearDown()
        {
            if (_api != null && !string.IsNullOrEmpty(_api.SessionToken))
            {
                _api.ReleaseSession(RequestId.New());
            }

            _transport?.Dispose();
        }

        [Test]
        public void APositionSurvivesASaveAndComesBackOnTheNextLoad()
        {
            CharacterPersistenceResult loaded = _store.Load(_api.Session);

            Assert.That(loaded.IsOk, Is.True, "could not read the fixture character: "
                + loaded.Failure + " " + loaded.Detail);

            PersistedCharacter row = loaded.Character;

            const float X = -6.25f;
            const float Y = 8.49f;
            const float Z = 35.75f;

            var moved = new PersistedCharacter(row.Character, row.Account, row.Server,
                row.Name, row.Gender, row.Level, row.Experience, row.CurrentHealth,
                row.CurrentMana, row.Class, row.Job, row.Map, row.Spawn, row.Stats,
                row.Appearance, row.Skills, row.SaveRevision, row.Items,
                row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource,
                row.Pets, row.ActivePet, row.RewardApplications,
                true, X, Y, Z);

            Assert.That(moved.HasPosition, Is.True,
                "the contract dropped the position before it was even sent");

            CharacterPersistenceResult saved = _store.Save(_api.Session, moved,
                row.SaveRevision);

            Assert.That(saved.IsOk, Is.True, "the save was refused: "
                + saved.Failure + " " + saved.Detail);

            CharacterPersistenceResult again = _store.Load(_api.Session);

            Assert.That(again.IsOk, Is.True, again.Detail);
            Assert.That(again.Character.HasPosition, Is.True,
                "the row came back with no position, so a login has nothing to return to");
            Assert.That(again.Character.PositionX, Is.EqualTo(X).Within(0.01f));
            Assert.That(again.Character.PositionY, Is.EqualTo(Y).Within(0.01f));
            Assert.That(again.Character.PositionZ, Is.EqualTo(Z).Within(0.01f));
        }

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
                new CharacterId(_fixture.RewardCharacterId)).IsOk, Is.True);

            ApiResult<bool> entered = _api.NotifyWorldEntry(login.Value.Account, _api.Session,
                new CharacterId(_fixture.RewardCharacterId), new ServerId(_fixture.ServerId),
                new ChannelId(_fixture.ChannelId));

            Assert.That(entered.IsOk, Is.True, "enter-world failed: " + entered.Error);
        }
    }
}
