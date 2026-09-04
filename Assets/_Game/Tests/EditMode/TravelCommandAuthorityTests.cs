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
    /// Travel, decided by the server.
    /// </summary>
    /// <remarks>
    /// Phase 11's <c>TravelService</c> already decides whether a journey is legal, with
    /// twelve typed reasons, and has its own tests. What is checked here is the layer above:
    /// that a client can only ask, that it cannot ask for coordinates, and that a warp it
    /// initiates is town-restricted whatever spawn id it names.
    /// </remarks>
    [TestFixture]
    internal sealed class TravelCommandAuthorityTests
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public PersistedCharacter Row;

            public CharacterPersistenceResult Load(SessionId s) =>
                CharacterPersistenceResult.Loaded(Row);

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private readonly List<Object> _created = new List<Object>();

        private WorldCharacterRegistry _characters;
        private TravelCommandAuthority _authority;
        private LivingCharacter _living;
        private DefinitionRegistry<MapDefinition> _maps;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private bool _canAct = true;

        [SetUp]
        public void SetUp()
        {
            _canAct = true;

            _maps = new DefinitionRegistry<MapDefinition>();
            _maps.Register(Map("map.town", isTown: true));
            _maps.Register(Map("map.field", isTown: false));

            _spawns = new DefinitionRegistry<SpawnPointDefinition>();
            _spawns.Register(Spawn("spawn.town", "map.town", 1f, 0f, 1f));
            _spawns.Register(Spawn("spawn.field", "map.field", 5f, 0f, 5f));

            var store = new FakeStore
            {
                Row = new PersistedCharacter(
                    new CharacterId("char-1"), new AccountId("acc-1"), new ServerId("srv-1"),
                    "Ayla", 2, 12, 0, 87, 33, new DefinitionId("class.novice"), default,
                    new DefinitionId("map.field"), new DefinitionId("spawn.field"),
                    null, null, null, 1),
            };

            _characters = new WorldCharacterRegistry(store, _spawns);

            _living = _characters.Spawn(1, WorldAdmission.Admitted(
                new SessionId("s1"), new AccountId("acc-1"), new CharacterId("char-1"),
                new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId("map.field"),
                new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(200, 100)).Character;

            Assert.That(_living, Is.Not.Null, "precondition: spawned on the field");

            _authority = new TravelCommandAuthority(_characters, id => _canAct);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        /// <summary>Authors a map.</summary>
        /// <remarks>Both the flag and the category are written, because
        /// <c>TravelService.IsTown</c> requires them to agree -- a map authored
        /// inconsistently is refused rather than given the benefit of the doubt, and the
        /// first version of this fixture set only the flag and was correctly refused.</remarks>
        private MapDefinition Map(string id, bool isTown)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_isTown\":"
                + (isTown ? "true" : "false")
                + ",\"_category\":" + (int)(isTown ? MapCategory.Town : MapCategory.Field)
                + "}", map);

            _created.Add(map);

            return map;
        }

        private SpawnPointDefinition Spawn(string id, string map, float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":" + F(x) + ",\"_y\":"
                + F(y) + ",\"_z\":" + F(z) + "}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static string F(float v) =>
            v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private TravelService.Context Context(int level = 12)
        {
            return new TravelService.Context(_maps, _spawns, characterLevel: level);
        }

        private static TravelCommand Warp(string map = "map.town", string spawn = "spawn.town",
            long sequence = 1, string claimed = "char-1")
        {
            return new TravelCommand(new CharacterId(claimed), default, new DefinitionId(map),
                new DefinitionId(spawn), sequence);
        }

        private TravelCommandResult Execute(in TravelCommand command, bool alive = true)
        {
            return _authority.Execute(1, command, Context(), alive);
        }

        [Test]
        public void FixtureSanity_TheTownMapIsActuallyATown()
        {
            Assert.That(_maps.TryGet(new DefinitionId("map.town"), out MapDefinition town),
                Is.True, "the town is in the registry");
            Assert.That(town, Is.Not.Null);
            Assert.That(town.IsTown, Is.True, "_isTown");
            Assert.That(town.Category, Is.EqualTo(MapCategory.Town), "_category");
            Assert.That(town.IsBossArea, Is.False, "_isBossArea");
            Assert.That(TravelService.IsTown(town), Is.True, "TravelService.IsTown");
        }

        // ---- a permitted warp -----------------------------------------------------------------

        [Test]
        public void AWarpToATownIsAcceptedAndMovesTheCharacter()
        {
            TravelCommandResult result = Execute(Warp());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(_living.Location.CurrentMap.Value, Is.EqualTo("map.town"));
            Assert.That(_living.Location.CurrentSpawnPoint.Value, Is.EqualTo("spawn.town"));
            Assert.That(result.Map.Value, Is.EqualTo("map.town"));
        }

        [Test]
        public void ArrivingMarksTheCharacterForSaving()
        {
            Assert.That(_living.IsDirty, Is.False, "precondition");

            Execute(Warp());

            Assert.That(_living.IsDirty, Is.True,
                "where somebody is standing is worth persisting");
        }

        [Test]
        public void ArrivingResetsTheMovementStream()
        {
            _living.RecordMovement(500, 90000);

            Execute(Warp());

            // Positions measured against the old map would look like enormous deltas on
            // the new one, and every legitimate move would be refused as a speed hack.
            Assert.That(_living.LastMovementSequence, Is.Zero);
            Assert.That(_living.LastMovementTimestamp, Is.Zero);
        }

        // ---- no client teleport authority ---------------------------------------------------------

        [Test]
        public void AWarpToAFieldIsRefusedWhateverSpawnItNames()
        {
            // The town restriction is not optional on a client-initiated warp. Without it a
            // client could name any authored spawn and arrive in a field or a boss area.
            TravelCommandResult result = Execute(Warp("map.field", "spawn.field"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(_living.Location.CurrentMap.Value, Is.EqualTo("map.field"),
                "and it did not move");
        }

        [Test]
        public void ThereIsNowhereInATravelCommandToPutCoordinates()
        {
            System.Type command = typeof(TravelCommand);

            foreach (string absent in new[] { "X", "Y", "Z", "Position", "Destination" })
            {
                Assert.That(command.GetProperty(absent), Is.Null,
                    absent + " must not be something a client can send");
            }
        }

        [Test]
        public void ARefusedJourneyMovesNobody()
        {
            DefinitionId before = _living.Location.CurrentMap;

            TravelCommandResult result = Execute(Warp("map.nowhere", "spawn.nowhere"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(_living.Location.CurrentMap, Is.EqualTo(before));
            Assert.That(result.Map, Is.EqualTo(before), "a client is told where it really is");
        }

        [Test]
        public void ARefusalByTheRulesCarriesTheRulesOwnReason()
        {
            TravelCommandResult result = Execute(Warp("map.nowhere", "spawn.nowhere"));

            Assert.That(result.Rejection, Is.EqualTo(TravelCommandRejection.None),
                "it reached the rules");
            Assert.That(result.Travel.IsAccepted, Is.False);
            Assert.That(result.Travel.Reason, Is.Not.EqualTo(TravelRejection.None));
        }

        // ---- identity and connection state -------------------------------------------------------------

        [Test]
        public void ClaimingSomebodyElsesCharacterIsRefused()
        {
            TravelCommandResult result = Execute(Warp(claimed: "char-theirs"));

            Assert.That(result.Rejection, Is.EqualTo(TravelCommandRejection.NotYourCharacter));
            Assert.That(_living.Location.CurrentMap.Value, Is.EqualTo("map.field"));
        }

        [Test]
        public void AStaleConnectionCannotTravel()
        {
            _canAct = false;

            Assert.That(Execute(Warp()).Rejection,
                Is.EqualTo(TravelCommandRejection.StaleConnection));
        }

        [Test]
        public void AConnectionWithNoCharacterCannotTravel()
        {
            Assert.That(_authority.Execute(99, Warp(), Context()).Rejection,
                Is.EqualTo(TravelCommandRejection.NoCharacter));
        }

        [Test]
        public void ADeadCharacterCannotTravel()
        {
            Assert.That(Execute(Warp(), alive: false).Rejection,
                Is.EqualTo(TravelCommandRejection.NotAlive));
        }

        [Test]
        public void ACommandNamingNeitherAPortalNorADestinationIsMalformed()
        {
            var empty = new TravelCommand(new CharacterId("char-1"), default, default,
                default, 1);

            Assert.That(Execute(empty).Rejection, Is.EqualTo(TravelCommandRejection.Malformed));
        }

        [Test]
        public void AWarpWithASpawnButNoMapIsMalformed()
        {
            var partial = new TravelCommand(new CharacterId("char-1"), default, default,
                new DefinitionId("spawn.town"), 1);

            Assert.That(Execute(partial).Rejection,
                Is.EqualTo(TravelCommandRejection.Malformed));
        }

        // ---- replay ---------------------------------------------------------------------------------------

        [Test]
        public void AReplayedTravelRequestIsRefused()
        {
            Execute(Warp(sequence: 5));

            Assert.That(Execute(Warp("map.field", "spawn.field", sequence: 5)).Rejection,
                Is.EqualTo(TravelCommandRejection.OutOfOrder));
        }

        [Test]
        public void ARefusedJourneyStillConsumesItsSequence()
        {
            // Unlike combat, where a refusal must be retryable: a travel request the rules
            // refused was answered, and replaying the identical one cannot succeed either.
            Execute(Warp("map.field", "spawn.field", sequence: 3));

            Assert.That(Execute(Warp(sequence: 3)).Rejection,
                Is.EqualTo(TravelCommandRejection.OutOfOrder));
        }

        [Test]
        public void TravelAndCombatSequencesAreIndependent()
        {
            Execute(Warp(sequence: 50));

            Assert.That(_living.LastTravelSequence, Is.EqualTo(50));
            Assert.That(_living.LastCombatSequence, Is.Zero);
        }

        // ---- misconfiguration ---------------------------------------------------------------------------------

        [Test]
        public void AnAuthorityWithNoRegistryRefuses()
        {
            var blind = new TravelCommandAuthority(null, id => true);

            Assert.That(blind.Execute(1, Warp(), Context()).Rejection,
                Is.EqualTo(TravelCommandRejection.NoCharacter));
        }
    }
}
