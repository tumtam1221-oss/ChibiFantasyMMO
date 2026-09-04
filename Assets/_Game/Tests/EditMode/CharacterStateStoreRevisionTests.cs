using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The save revision a character is written against.
    /// </summary>
    /// <remarks>
    /// <b>Both cases below were real defects, found by trying to save a character twice.</b>
    /// Nothing before 17.14 ever did: world entry loaded, and shutdown saved once. The
    /// moment a monster's experience had to be written mid-session, the adapter refused
    /// every save after the first as stale -- and refused the very first one too, forever,
    /// for a character that had never been saved.
    ///
    /// They are pinned here rather than only in the live suite because a machine with no PHP
    /// still deserves to catch them, and because a scripted transport can show the exact
    /// bytes that go out.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterStateStoreRevisionTests
    {
        private sealed class Token : HttpCharacterStateStore.ITokenSource
        {
            public bool TryGetToken(SessionId session, out string token)
            {
                token = "tok-test";

                return true;
            }
        }

        private ScriptedHttpTransport _transport;
        private HttpCharacterStateStore _store;

        [SetUp]
        public void SetUp()
        {
            _transport = new ScriptedHttpTransport();
            _store = new HttpCharacterStateStore(_transport, new Token());
        }

        private static PersistedCharacter Row(int saveRevision, int level = 5,
            long experience = 0)
        {
            return new PersistedCharacter(
                new CharacterId("char-a"), new AccountId("acc-a"), new ServerId("srv-1"),
                "Ayla", 2, level, experience, 100, 50, new DefinitionId("class.novice"),
                default, new DefinitionId("map.home"), default, null, null, null,
                saveRevision);
        }

        /// <summary>A load body in the API's real shape: revisions are nested.</summary>
        private static string LoadBody(int saveRevision, int level = 5, long experience = 0)
        {
            return "{\"character_id\":\"char-a\",\"account_id\":\"acc-a\","
                + "\"server_id\":\"srv-1\",\"name\":\"Ayla\",\"gender\":2,"
                + "\"level\":" + level + ",\"experience\":" + experience + ","
                + "\"current_health\":100,\"current_mana\":50,"
                + "\"class_id\":\"class.novice\",\"job_id\":\"job.none\","
                + "\"map_id\":\"map.home\",\"spawn_id\":\"spawn.home\","
                + "\"stats\":[],\"appearance\":[],\"skills\":[],"
                + "\"revisions\":{\"identity\":0,\"class\":0,\"appearance\":0,"
                + "\"progression\":3,\"stats\":0,\"skills\":0,\"save\":"
                + saveRevision + "}}";
        }

        [Test]
        public void ALoadReadsTheSaveRevisionOutOfTheNestedRevisions()
        {
            _transport.EnqueueOk("GET", "/api/character/state", LoadBody(saveRevision: 7));

            CharacterPersistenceResult loaded = _store.Load(new SessionId("sess-1"));

            Assert.That(loaded.IsOk, Is.True, loaded.Detail);
            Assert.That(loaded.Character.SaveRevision, Is.EqualTo(7),
                "reading only the top level reported zero however many times the character "
                + "had been written, and every later save was then refused as stale");
        }

        [Test]
        public void ACharacterThatHasNeverBeenSavedReadsAsRevisionZero()
        {
            _transport.EnqueueOk("GET", "/api/character/state", LoadBody(saveRevision: 0));

            CharacterPersistenceResult loaded = _store.Load(new SessionId("sess-1"));

            Assert.That(loaded.Character.SaveRevision, Is.Zero);
        }

        [Test]
        public void AFirstSaveOmitsTheRevisionEntirely()
        {
            _transport.EnqueueOk("POST", "/api/character/state", "{\"save_revision\":1}");

            _store.Save(new SessionId("sess-1"), Row(saveRevision: 0), 0);

            Assert.That(_transport.LastBody, Does.Not.Contain("save_revision"),
                "an absent field is the API's contract for 'never saved'; sending zero "
                + "claims a revision that matches nothing and is refused forever");
        }

        [Test]
        public void ALaterSavePresentsTheRevisionItLoaded()
        {
            _transport.EnqueueOk("POST", "/api/character/state", "{\"save_revision\":8}");

            CharacterPersistenceResult saved =
                _store.Save(new SessionId("sess-1"), Row(saveRevision: 7), 7);

            Assert.That(_transport.LastBody, Does.Contain("\"save_revision\":7"));
            Assert.That(saved.IsOk, Is.True);
            Assert.That(saved.SaveRevision, Is.EqualTo(8),
                "and the answer carries the new one, so the next save is not stale");
        }

        [Test]
        public void ALoadThenASaveCarriesTheRevisionStraightThrough()
        {
            // The whole round trip, which is what actually broke: load, change, save.
            _transport.EnqueueOk("GET", "/api/character/state", LoadBody(saveRevision: 4));
            _transport.EnqueueOk("POST", "/api/character/state", "{\"save_revision\":5}");

            CharacterPersistenceResult loaded = _store.Load(new SessionId("sess-1"));

            _store.Save(new SessionId("sess-1"), loaded.Character,
                loaded.Character.SaveRevision);

            Assert.That(_transport.LastBody, Does.Contain("\"save_revision\":4"));
        }

        [Test]
        public void AStaleRevisionFromTheServerIsATypedFailure()
        {
            _transport.Enqueue("POST", "/api/character/state",
                HttpExchange.Responded(409, "{\"code\":\"stale_revision\"}"));

            CharacterPersistenceResult saved =
                _store.Save(new SessionId("sess-1"), Row(saveRevision: 2), 2);

            Assert.That(saved.IsOk, Is.False);
            Assert.That(saved.Failure,
                Is.EqualTo(CharacterPersistenceFailure.StaleRevision));
        }

        // ---- the reader that made it possible ------------------------------------------------

        [Test]
        public void ANestedObjectIsOnlyReachableByDescendingIntoIt()
        {
            var json = JsonReader.Parse(
                "{\"save\":1,\"revisions\":{\"save\":9},\"other\":{\"save\":99}}");

            Assert.That(json.Int("save"), Is.EqualTo(1), "the outer one is this object's");
            Assert.That(json.Nested("revisions").Int("save"), Is.EqualTo(9));
            Assert.That(json.Nested("other").Int("save"), Is.EqualTo(99),
                "two objects may hold the same key without either being ambiguous");
        }

        [Test]
        public void AMissingOrNonObjectNestedValueReadsAsEmpty()
        {
            var json = JsonReader.Parse("{\"a\":1,\"b\":\"text\",\"c\":[{\"x\":1}]}");

            Assert.That(json.Nested("missing").IsEmpty, Is.True);
            Assert.That(json.Nested("a").IsEmpty, Is.True);
            Assert.That(json.Nested("b").IsEmpty, Is.True);
            Assert.That(json.Nested("c").IsEmpty, Is.True, "an array is not an object");
            Assert.That(json.Nested("missing").Int("anything"), Is.Zero,
                "and every accessor still answers its own missing value");
        }

        [Test]
        public void ABraceInsideAStringDoesNotEndTheNestedObject()
        {
            var json = JsonReader.Parse(
                "{\"revisions\":{\"name\":\"a } brace\",\"save\":6}}");

            Assert.That(json.Nested("revisions").Int("save"), Is.EqualTo(6));
        }
    }
}
