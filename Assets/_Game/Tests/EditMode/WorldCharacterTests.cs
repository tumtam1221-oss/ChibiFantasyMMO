using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The character the server creates, and everything it refuses to invent.
    /// </summary>
    /// <remarks>
    /// Two properties are worth more than the rest here. The first is that no value comes
    /// from a client -- there is no constructor that would let one. The second is that an
    /// unplaceable character produces nothing at all rather than a character at the origin,
    /// because "spawned successfully, inside a hill" is a failure that looks like a success.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldCharacterTests
    {
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private readonly List<SpawnPointDefinition> _created = new List<SpawnPointDefinition>();

        [SetUp]
        public void SetUp()
        {
            _spawns = new DefinitionRegistry<SpawnPointDefinition>();
            _spawns.Register(Spawn("spawn.town.plaza", "map.town", SpawnType.Player, 12f, 3f, -4f));
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

        /// <summary>Authors a spawn the same way the Phase 11 fixtures do.</summary>
        /// <remarks>Serialized fields are private, so a definition is populated through
        /// JsonUtility rather than by adding a test-only setter to shipping content. The
        /// invariant-culture float matters: a comma locale would produce invalid JSON.</remarks>
        private SpawnPointDefinition Spawn(string id, string map, SpawnType type,
            float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)type
                + ",\"_x\":" + F(x) + ",\"_y\":" + F(y) + ",\"_z\":" + F(z) + "}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static string F(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static WorldAdmission Admission(string map = "map.town", int level = 12)
        {
            return WorldAdmission.Admitted(
                new SessionId("s1"),
                new AccountId("acc-1"),
                new CharacterId("char-1"),
                new ServerId("srv-1"),
                new ChannelId("ch-1"),
                new DefinitionId(map),
                new Revision(3),
                new Revision(9),
                SessionState.EnteringWorld,
                new WorldCharacterProfile(level, 2, new DefinitionId("class.novice"),
                    new DefinitionId("job.none"), new DefinitionId("appearance.default")));
        }

        // ---- identity ---------------------------------------------------------------------

        [Test]
        public void TheCharacterKeepsTheIdentifierTheAccountDatabaseIssued()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character, Is.Not.Null);
            Assert.That(character.Character.Value, Is.EqualTo("char-1"),
                "a second identity would be a second character");
            Assert.That(character.Account.Value, Is.EqualTo("acc-1"));
            Assert.That(character.Session.Value, Is.EqualTo("s1"));
        }

        [Test]
        public void OwnershipIsProjectedFromTheAccountRatherThanStoredSeparately()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character.Owner.Value, Is.EqualTo(character.Account.Value),
                "there is one ownership model in this project, and it is the account's");
        }

        [Test]
        public void TheServerAndChannelAreTheOnesTheAuthorityNamed()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character.Server.Value, Is.EqualTo("srv-1"));
            Assert.That(character.Channel.Value, Is.EqualTo("ch-1"));
        }

        [Test]
        public void TheRevisionIsTheOneAuthorityWasTakenAt()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character.CharacterRevision.Value, Is.EqualTo(9),
                "so a later write can detect that the world moved underneath it");
        }

        [Test]
        public void TheAuthoredProfileSurvivesIntact()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(level: 27), _spawns);

            Assert.That(character.HasProfile, Is.True);
            Assert.That(character.Profile.Level, Is.EqualTo(27));
            Assert.That(character.Profile.Class.Value, Is.EqualTo("class.novice"));
            Assert.That(character.Profile.Job.Value, Is.EqualTo("job.none"));
            Assert.That(character.Profile.Appearance.Value, Is.EqualTo("appearance.default"),
                "the authored appearance is the character's, not something re-rolled here");
            Assert.That(character.Profile.Gender, Is.EqualTo(2));
        }

        [Test]
        public void ThereIsNoWayToBuildOneFromValuesAClientSupplied()
        {
            // The only entry point takes an admission, which only the authority produces.
            // A constructor taking loose identifiers would make client-authoritative stats
            // representable, so there isn't one.
            Assert.That(typeof(WorldCharacter).GetConstructors().Length, Is.Zero,
                "no public constructor: an admission is the only way in");
        }

        // ---- placement ---------------------------------------------------------------------

        [Test]
        public void TheCharacterStandsWhereTheAuthoredSpawnSaysAndNotAtTheOrigin()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character.Location.CurrentMap.Value, Is.EqualTo("map.town"));
            Assert.That(character.Location.CurrentSpawnPoint.Value, Is.EqualTo("spawn.town.plaza"));
            Assert.That(character.Location.Position.X, Is.EqualTo(12f));
            Assert.That(character.Location.Position.Y, Is.EqualTo(3f));
            Assert.That(character.Location.Position.Z, Is.EqualTo(-4f));
        }

        [Test]
        public void TheLocationIsThePhase11StateAndNotASecondModel()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(), _spawns);

            Assert.That(character.Location, Is.TypeOf<ChibiFantasy.Gameplay.CharacterLocationState>());
            Assert.That(character.Location.HasArrived, Is.True);
            Assert.That(character.Location.CharacterId.Value, Is.EqualTo("char-1"));
        }

        [Test]
        public void AMapWithNoPlayerSpawnProducesNoCharacterAtAll()
        {
            WorldCharacter character = WorldCharacter.Create(Admission(map: "map.nowhere"),
                _spawns);

            Assert.That(character, Is.Null,
                "spawned successfully, inside a hill, is a failure that looks like a success");
        }

        [Test]
        public void AMonsterSpawnIsNotAPlaceToPutAPlayer()
        {
            var registry = new DefinitionRegistry<SpawnPointDefinition>();
            registry.Register(Spawn("spawn.cave.mob", "map.cave", SpawnType.Monster, 1f, 1f, 1f));

            Assert.That(WorldCharacter.Create(Admission(map: "map.cave"), registry), Is.Null,
                "a monster spawn is not a place to put a player");
        }

        [Test]
        public void ARefusedAdmissionProducesNoCharacter()
        {
            Assert.That(
                WorldCharacter.Create(WorldAdmission.Refused(SessionRejection.SessionExpired),
                    _spawns),
                Is.Null,
                "a rejected connection must not spawn a character");
        }

        [Test]
        public void AnAdmissionWithNoCharacterProducesNoCharacter()
        {
            WorldAdmission noCharacter = WorldAdmission.Admitted(
                new SessionId("s1"), new AccountId("acc-1"), default,
                new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId("map.town"),
                new Revision(1), new Revision(1), SessionState.EnteringWorld);

            Assert.That(WorldCharacter.Create(noCharacter, _spawns), Is.Null);
        }

        [Test]
        public void NoSpawnRegistryAtAllProducesNoCharacter()
        {
            Assert.That(WorldCharacter.Create(Admission(), null), Is.Null,
                "a server with no content cannot place anybody, and must not pretend to");
        }

        // ---- what it deliberately does not hold ------------------------------------------------

        [Test]
        public void NothingHereFabricatesStatsOrInventory()
        {
            // The Phase 15 API serves none of these. A property here would have to be
            // invented, and an invented stat block is indistinguishable from a real one
            // until the moment it decides a fight.
            System.Type type = typeof(WorldCharacter);

            foreach (string absent in new[]
                     { "Stats", "Experience", "Inventory", "Equipment", "Health", "Mana" })
            {
                Assert.That(type.GetProperty(absent), Is.Null,
                    absent + " is not something this phase can know");
            }
        }
    }
}
