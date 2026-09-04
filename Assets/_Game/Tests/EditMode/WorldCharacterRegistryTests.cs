using System.Collections.Generic;
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
    /// Bringing a character into the world, keeping it, and writing it back.
    /// </summary>
    /// <remarks>
    /// Two rules carry the weight. <b>One character, one presence</b> — a second spawn of the
    /// same character is refused rather than producing a second authoritative copy. And
    /// <b>saves are lifecycle events, not a heartbeat</b> — an unchanged character is skipped
    /// entirely, so an idle player costs the database nothing.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldCharacterRegistryTests
    {
        /// <summary>A store that answers whatever a test needs, and counts what it was asked.</summary>
        private sealed class FakeStore : ICharacterStateStore
        {
            private readonly Dictionary<string, PersistedCharacter> _rows =
                new Dictionary<string, PersistedCharacter>();

            public int Loads;
            public int Saves;
            public bool RefuseLoad;
            public bool RefuseSave;
            public PersistedCharacter LastSaved;

            public FakeStore Holds(string session, PersistedCharacter row)
            {
                _rows[session] = row;

                return this;
            }

            public CharacterPersistenceResult Load(SessionId session)
            {
                Loads++;

                if (RefuseLoad)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable, "refused by test");
                }

                return _rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId session, PersistedCharacter character,
                int expectedSaveRevision)
            {
                Saves++;
                LastSaved = character;

                if (RefuseSave)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.StaleRevision, "refused by test");
                }

                return CharacterPersistenceResult.Saved(expectedSaveRevision + 1);
            }
        }

        private static readonly ResourceLimits Limits = new ResourceLimits(200, 100);

        private FakeStore _store;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private WorldCharacterRegistry _registry;
        private readonly List<SpawnPointDefinition> _created = new List<SpawnPointDefinition>();

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();

            _spawns = new DefinitionRegistry<SpawnPointDefinition>();
            _spawns.Register(Spawn("spawn.town.plaza", "map.town", SpawnType.Player, 4f, 0f, 2f));
            _spawns.Register(Spawn("spawn.town.gate", "map.town", SpawnType.Player, 9f, 0f, 9f));
            _spawns.Register(Spawn("spawn.cave.mob", "map.cave", SpawnType.Monster, 1f, 0f, 1f));

            _registry = new WorldCharacterRegistry(_store, _spawns);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (SpawnPointDefinition definition in _created)
            {
                if (definition != null) Object.DestroyImmediate(definition);
            }

            _created.Clear();
        }

        private SpawnPointDefinition Spawn(string id, string map, SpawnType type,
            float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)type + ",\"_x\":" + F(x) + ",\"_y\":" + F(y)
                + ",\"_z\":" + F(z) + "}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static string F(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static WorldAdmission Admission(string session = "s1", string character = "char-1")
        {
            return WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-1"),
                new CharacterId(character), new ServerId("srv-1"), new ChannelId("ch-1"),
                new DefinitionId("map.town"), new Revision(1), new Revision(1),
                SessionState.EnteringWorld);
        }

        private static PersistedCharacter Row(string character = "char-1",
            string map = "map.town", string spawn = "spawn.town.plaza", int level = 12,
            int saveRevision = 3, IReadOnlyList<PersistedStat> stats = null)
        {
            return new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-1"), new ServerId("srv-1"),
                "Ayla", 2, level, 4500, 87, 33, new DefinitionId("class.novice"),
                default, new DefinitionId(map), new DefinitionId(spawn),
                stats ?? new[] { new PersistedStat(new DefinitionId("stat.strength"), 14) },
                null,
                new[] { new PersistedSkill(new DefinitionId("skill.slash"), 3) },
                saveRevision);
        }

        private LivingCharacter SpawnOne(string session = "s1", string character = "char-1")
        {
            _store.Holds(session, Row(character));

            return _registry.Spawn(1, Admission(session, character), Limits).Character;
        }

        // ---- spawning ---------------------------------------------------------------------

        [Test]
        public void AnAdmittedCharacterIsLoadedPlacedAndHeld()
        {
            _store.Holds("s1", Row());

            WorldSpawnResult result = _registry.Spawn(1, Admission(), Limits);

            Assert.That(result.IsSpawned, Is.True, result.Detail);
            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(result.Character.Character.Value, Is.EqualTo("char-1"));
            Assert.That(result.Character.Domain.Progression.Level, Is.EqualTo(12));
            Assert.That(result.Character.Skills.Knows(new DefinitionId("skill.slash")), Is.True);
        }

        [Test]
        public void TheCharacterStandsWhereItLastStood()
        {
            _store.Holds("s1", Row(spawn: "spawn.town.gate"));

            LivingCharacter living = _registry.Spawn(1, Admission(), Limits).Character;

            Assert.That(living.Location.CurrentSpawnPoint.Value, Is.EqualTo("spawn.town.gate"));
            Assert.That(living.Location.Position.X, Is.EqualTo(9f));
        }

        [Test]
        public void ASavedSpawnThatContentRemovedFallsBackToTheMapsPlayerSpawn()
        {
            // A player must not be stranded because a level designer deleted a spawn point.
            _store.Holds("s1", Row(spawn: "spawn.that.no.longer.exists"));

            LivingCharacter living = _registry.Spawn(1, Admission(), Limits).Character;

            Assert.That(living.Location.HasArrived, Is.True);
            Assert.That(living.Location.CurrentMap.Value, Is.EqualTo("map.town"));
        }

        [Test]
        public void ASavedSpawnOnAnotherMapIsNotUsed()
        {
            // Honouring it would silently move the player to a different map.
            _store.Holds("s1", Row(map: "map.town", spawn: "spawn.cave.mob"));

            LivingCharacter living = _registry.Spawn(1, Admission(), Limits).Character;

            Assert.That(living.Location.CurrentMap.Value, Is.EqualTo("map.town"));
        }

        [Test]
        public void OwnershipIsProjectedFromTheAccount()
        {
            LivingCharacter living = SpawnOne();

            Assert.That(living.Owner.Value, Is.EqualTo("acc-1"));
        }

        [Test]
        public void ANewlySpawnedCharacterIsNotDirty()
        {
            Assert.That(SpawnOne().IsDirty, Is.False,
                "loading is not a change; saving it back immediately would be pointless work");
        }

        // ---- one character, one presence -----------------------------------------------------

        [Test]
        public void TheSameCharacterCannotBeSpawnedTwice()
        {
            SpawnOne();

            _store.Holds("s2", Row());

            WorldSpawnResult second = _registry.Spawn(2, Admission("s2"), Limits);

            Assert.That(second.IsSpawned, Is.False);
            Assert.That(second.Reason, Is.EqualTo(WorldSpawnRejection.AlreadySpawned));
            Assert.That(_registry.Count, Is.EqualTo(1),
                "a second authoritative copy is the corruption this prevents");
        }

        [Test]
        public void ADuplicateSpawnCostsNoRoundTrip()
        {
            SpawnOne();

            int loadsBefore = _store.Loads;

            _registry.Spawn(2, Admission("s2"), Limits);

            Assert.That(_store.Loads, Is.EqualTo(loadsBefore),
                "the cheapest check runs first");
        }

        [Test]
        public void TwoDifferentCharactersMayBothBeInTheWorld()
        {
            SpawnOne("s1", "char-1");

            _store.Holds("s2", Row("char-2"));

            Assert.That(_registry.Spawn(2, Admission("s2", "char-2"), Limits).IsSpawned, Is.True);
            Assert.That(_registry.Count, Is.EqualTo(2));
        }

        // ---- refusals leave nothing behind ------------------------------------------------------

        [Test]
        public void ARefusedAdmissionSpawnsNothing()
        {
            WorldSpawnResult result = _registry.Spawn(1,
                WorldAdmission.Refused(SessionRejection.SessionExpired), Limits);

            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.NotAdmitted));
            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_store.Loads, Is.Zero, "an unadmitted connection is not worth a query");
        }

        [Test]
        public void AFailedLoadLeavesNothingBehind()
        {
            _store.RefuseLoad = true;

            WorldSpawnResult result = _registry.Spawn(1, Admission(), Limits);

            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.PersistenceFailed));
            Assert.That(result.Detail, Does.Contain("Unreachable"));
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void ACorruptRowIsNamedRatherThanSpawned()
        {
            _store.Holds("s1", Row(stats: new[]
            {
                new PersistedStat(new DefinitionId("stat.strength"), -5),
            }));

            WorldSpawnResult result = _registry.Spawn(1, Admission(), Limits);

            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
            Assert.That(result.Detail, Does.Contain("stat.strength"));
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void AMapWithNoPlayerSpawnRefusesRatherThanUsingTheOrigin()
        {
            _store.Holds("s1", Row(map: "map.cave", spawn: ""));

            WorldSpawnResult result = _registry.Spawn(1, Admission(), Limits);

            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.NoSpawnPoint),
                "the cave has only a monster spawn, which is not a place to put a player");
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void NoStoreConfiguredRefusesRatherThanSpawningAnEmptyCharacter()
        {
            var blind = new WorldCharacterRegistry(null, _spawns);

            Assert.That(blind.Spawn(1, Admission(), Limits).Reason,
                Is.EqualTo(WorldSpawnRejection.PersistenceFailed));
        }

        // ---- saving is a lifecycle event ----------------------------------------------------------

        [Test]
        public void AnUnchangedCharacterIsNotWritten()
        {
            LivingCharacter living = SpawnOne();

            CharacterPersistenceResult result = _registry.Save(living);

            Assert.That(result.IsOk, Is.True, "nothing to do is not a failure");
            Assert.That(_store.Saves, Is.Zero, "an idle player costs the database nothing");
        }

        [Test]
        public void AChangedCharacterIsWritten()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();

            Assert.That(_registry.Save(living).IsOk, Is.True);
            Assert.That(_store.Saves, Is.EqualTo(1));
        }

        [Test]
        public void ForcingASaveWritesEvenAnUnchangedCharacter()
        {
            _registry.Save(SpawnOne(), force: true);

            Assert.That(_store.Saves, Is.EqualTo(1));
        }

        [Test]
        public void AnAcceptedSaveClearsTheDirtyFlagAndAdvancesTheRevision()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();

            _registry.Save(living);

            Assert.That(living.IsDirty, Is.False);
            Assert.That(living.SaveRevision, Is.EqualTo(4), "3 was loaded, 4 was written");
        }

        [Test]
        public void ARefusedSaveLeavesTheCharacterDirtySoTheNextAttemptRetries()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();
            _store.RefuseSave = true;

            Assert.That(_registry.Save(living).IsOk, Is.False);
            Assert.That(living.IsDirty, Is.True,
                "losing the change quietly would be worse than failing loudly");
            Assert.That(living.SaveRevision, Is.EqualTo(3), "the revision did not advance");
        }

        [Test]
        public void TheSavedRowPresentsTheRevisionItWasLoadedAt()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();

            _registry.Save(living);

            Assert.That(_store.LastSaved.SaveRevision, Is.EqualTo(3),
                "a writer presents what it loaded, or it can overwrite a newer save");
        }

        [Test]
        public void TheSavedLocationComesFromTheLocationStateNotTheLoadedRow()
        {
            LivingCharacter living = SpawnOne();

            // The player walked to the gate.
            living.Location.ArriveAt(_spawns.All[1]);
            living.MarkDirty();

            _registry.Save(living);

            Assert.That(_store.LastSaved.Spawn.Value, Is.EqualTo("spawn.town.gate"));
        }

        // ---- leaving -------------------------------------------------------------------------------

        [Test]
        public void DespawningSavesAndRemoves()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();

            Assert.That(_registry.Despawn(1).IsOk, Is.True);
            Assert.That(_registry.Count, Is.Zero);
            Assert.That(_store.Saves, Is.EqualTo(1));
        }

        [Test]
        public void DespawningRemovesTheCharacterEvenIfTheSaveFailed()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();
            _store.RefuseSave = true;

            CharacterPersistenceResult result = _registry.Despawn(1);

            Assert.That(result.IsOk, Is.False);
            Assert.That(_registry.Count, Is.Zero,
                "keeping it would block the player's own reconnection and turn a transient "
                + "database problem into a lockout");
        }

        [Test]
        public void DespawningAConnectionThatHasNoCharacterIsHarmless()
        {
            Assert.That(_registry.Despawn(99).IsOk, Is.False);
            Assert.That(_store.Saves, Is.Zero);
        }

        [Test]
        public void ACharacterCanBeSpawnedAgainAfterDespawning()
        {
            SpawnOne();
            _registry.Despawn(1);

            _store.Holds("s2", Row());

            Assert.That(_registry.Spawn(2, Admission("s2"), Limits).IsSpawned, Is.True,
                "reconnection must work");
        }

        // ---- shutdown ---------------------------------------------------------------------------------

        [Test]
        public void ShutdownSavesEveryChangedCharacter()
        {
            LivingCharacter first = SpawnOne("s1", "char-1");

            _store.Holds("s2", Row("char-2"));
            LivingCharacter second = _registry.Spawn(2, Admission("s2", "char-2"), Limits)
                .Character;

            first.MarkDirty();
            second.MarkDirty();

            Assert.That(_registry.SaveAllAndClear(), Is.EqualTo(2));
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void ShutdownReportsHowManySavesTheAuthorityAccepted()
        {
            LivingCharacter living = SpawnOne();
            living.MarkDirty();
            _store.RefuseSave = true;

            Assert.That(_registry.SaveAllAndClear(), Is.Zero,
                "an operator has to be able to tell a clean stop from one that lost writes");
            Assert.That(_registry.Count, Is.Zero);
        }

        [Test]
        public void ShutdownOnAnEmptyServerIsHarmless()
        {
            Assert.That(_registry.SaveAllAndClear(), Is.Zero);
        }

        // ---- lookups -----------------------------------------------------------------------------------

        [Test]
        public void ACharacterCanBeFoundByConnectionOrByIdentity()
        {
            SpawnOne();

            Assert.That(_registry.TryGet(1, out LivingCharacter byConnection), Is.True);
            Assert.That(_registry.TryGetByCharacter(new CharacterId("char-1"),
                out LivingCharacter byCharacter), Is.True);
            Assert.That(byConnection, Is.SameAs(byCharacter));
            Assert.That(_registry.IsSpawned(new CharacterId("char-1")), Is.True);
        }

        [Test]
        public void AnUnknownCharacterIsNotSpawned()
        {
            Assert.That(_registry.IsSpawned(new CharacterId("nobody")), Is.False);
            Assert.That(_registry.IsSpawned(default), Is.False);
        }
    }
}
