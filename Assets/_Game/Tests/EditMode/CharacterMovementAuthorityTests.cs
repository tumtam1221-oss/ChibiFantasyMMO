using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A player walking, and every way a client might try to walk faster than it should.
    /// </summary>
    /// <remarks>
    /// <b>The model is intent, not destination.</b> A client sends which way it is pressing;
    /// the server multiplies that by its own speed and its own clock. Several tests below
    /// exist to hold that shape, because the moment a position or a delta appears in the
    /// message the whole security argument changes.
    ///
    /// <b>The rules themselves are not new.</b> Sequence, map, finiteness, distance, bounds
    /// and aliveness all belong to the existing <c>MovementValidator</c>, which is unchanged;
    /// what 18.3 adds is where the destination comes from. The tests check both: that the
    /// step is computed correctly, and that the old rules still refuse what they always did.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterMovementAuthorityTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private const float Speed = 4f;
        private const int Connection = 3;
        private const int OtherConnection = 4;

        /// <summary>250ms of walking at 4 m/s, the most one accepted step may cover.</summary>
        private const float MaxStep = 1f;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterMovementAuthority _movement;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpMovement()
        {
            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));
            spawns.Register(PlayerSpawn("spawn.other", OtherMap));

            _players = new WorldCharacterRegistry(_store, spawns);

            _movement = new CharacterMovementAuthority(_players, _ => true, Maps, Speed);
        }

        [TearDown]
        public void TearDownMovement()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            _local.Add(spawn);

            return spawn;
        }

        private LivingCharacter AddPlayer(string character = "char-a",
            int connection = Connection, string map = HomeMap)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        /// <summary>Advances the server clock, which is the only clock there is.</summary>
        private void Tick(float seconds = 0.25f)
        {
            _movement.Tick(seconds);
        }

        // ---- the step itself ----------------------------------------------------------------

        [Test]
        public void AForwardInputWalksTheCharacterForwardAtTheServersSpeed()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(_movement.LastResult.IsAccepted, Is.True,
                _movement.LastResult.ToString());
            Assert.That(character.Location.Position.Z, Is.EqualTo(MaxStep).Within(0.001f),
                "quarter of a second at four metres a second");
            Assert.That(character.Location.Position.X, Is.Zero);
        }

        [Test]
        public void TheCombatantMovesWithTheCharacterSoRangeIsMeasuredWhereItStands()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(Connection, 1f, 0f, 1);

            Assert.That(character.Combatant.Position.X,
                Is.EqualTo(character.Location.Position.X).Within(0.0001f),
                "a monster chasing where a player used to be is the bug this prevents");
        }

        [Test]
        public void SeveralStepsAccumulate()
        {
            LivingCharacter character = AddPlayer();

            for (var i = 1; i <= 3; i++)
            {
                Tick();

                _movement.Submit(Connection, 0f, 1f, i);
            }

            Assert.That(character.Location.Position.Z,
                Is.EqualTo(MaxStep * 3f).Within(0.001f));
        }

        [Test]
        public void StandingStillMovesNothingButIsStillAccepted()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(Connection, 0f, 0f, 1);

            Assert.That(_movement.LastResult.IsAccepted, Is.True);
            Assert.That(character.Location.Position.Z, Is.Zero);
        }

        // ---- what a client cannot do -----------------------------------------------------------

        [Test]
        public void ADiagonalIsNotFasterThanAStraightLine()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            // A normalised diagonal: the same distance, split between two axes.
            _movement.Submit(Connection, 0.70710678f, 0.70710678f, 1);

            CombatPosition position = character.Location.Position;

            float travelled = Mathf.Sqrt((position.X * position.X)
                + (position.Z * position.Z));

            Assert.That(travelled, Is.EqualTo(MaxStep).Within(0.01f));
        }

        [Test]
        public void AnOversizedInputIsRefusedRatherThanClamped()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            // The classic version of this cheat: an un-normalised diagonal worth 1.41.
            _movement.Submit(Connection, 1f, 1f, 1);

            Assert.That(_movement.LastResult.IsAccepted, Is.False);
            Assert.That(_movement.LastResult.Reason, Is.EqualTo(MovementRejection.TooFar));
            Assert.That(character.Location.Position.Z, Is.Zero,
                "clamping would silently reward the attempt with full speed");
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.NaN)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(0f, float.NegativeInfinity)]
        public void ANonFiniteInputIsRefusedBeforeItReachesTheArithmetic(float x, float z)
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(Connection, x, z, 1);

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.NotFinite));
            Assert.That(character.Location.Position.IsFinite, Is.True,
                "a NaN that reached the position would poison every later comparison");
            Assert.That(character.Location.Position.Z, Is.Zero);
        }

        [Test]
        public void AFloodOfRequestsInsideOneTickMovesTheCharacterOnce()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            // Increasing sequences, so the replay guard does not catch them -- but no time
            // has passed, and time is what buys distance.
            for (var i = 1; i <= 50; i++) _movement.Submit(Connection, 0f, 1f, i);

            Assert.That(character.Location.Position.Z, Is.EqualTo(MaxStep).Within(0.001f),
                "fifty requests in one tick is one step, not fifty");
        }

        [Test]
        public void WaitingALongTimeDoesNotBuyALongStep()
        {
            LivingCharacter character = AddPlayer();

            // A minute of silence, then one input.
            Tick(60f);

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(character.Location.Position.Z, Is.EqualTo(MaxStep).Within(0.001f),
                "the elapsed time is clamped before it is multiplied by speed");
        }

        [Test]
        public void ADuplicateSequenceMovesNothing()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            float afterFirst = character.Location.Position.Z;

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.OutOfOrder));
            Assert.That(character.Location.Position.Z, Is.EqualTo(afterFirst));
        }

        [Test]
        public void AStaleSequenceMovesNothingAndANewerOneStillWorks()
        {
            LivingCharacter character = AddPlayer();

            Tick();
            _movement.Submit(Connection, 0f, 1f, 5);

            float afterFive = character.Location.Position.Z;

            Tick();
            _movement.Submit(Connection, 0f, 1f, 2);

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.OutOfOrder));
            Assert.That(character.Location.Position.Z, Is.EqualTo(afterFive));

            Tick();
            _movement.Submit(Connection, 0f, 1f, 6);

            Assert.That(_movement.LastResult.IsAccepted, Is.True);
            Assert.That(character.Location.Position.Z, Is.GreaterThan(afterFive));
        }

        [Test]
        public void AConnectionWithNoCharacterMovesNobody()
        {
            LivingCharacter character = AddPlayer();

            Tick();

            _movement.Submit(999, 0f, 1f, 1);

            Assert.That(_movement.LastResult.IsAccepted, Is.False);
            Assert.That(character.Location.Position.Z, Is.Zero);
        }

        [Test]
        public void AStaleConnectionMovesNobody()
        {
            LivingCharacter character = AddPlayer();

            var stale = new CharacterMovementAuthority(_players, _ => false, Maps, Speed);

            stale.Tick(0.25f);
            stale.Submit(Connection, 0f, 1f, 1);

            Assert.That(stale.LastResult.IsAccepted, Is.False);
            Assert.That(character.Location.Position.Z, Is.Zero);
        }

        [Test]
        public void OneConnectionCannotMoveAnotherPlayersCharacter()
        {
            LivingCharacter first = AddPlayer("char-a", Connection);
            LivingCharacter second = AddPlayer("char-b", OtherConnection);

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(first.Location.Position.Z, Is.GreaterThan(0f));
            Assert.That(second.Location.Position.Z, Is.Zero,
                "the connection names the character; there is no id to redirect");
        }

        [Test]
        public void ADeadCharacterDoesNotWalk()
        {
            LivingCharacter character = AddPlayer();

            character.Combatant.ApplyHealthDelta(-10000);

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(_movement.LastResult.Reason, Is.EqualTo(MovementRejection.NotAlive));
            Assert.That(character.Location.Position.Z, Is.Zero);
        }

        [Test]
        public void MovementCannotChangeTheMap()
        {
            LivingCharacter character = AddPlayer(map: HomeMap);

            Tick();

            _movement.Submit(Connection, 0f, 1f, 1);

            Assert.That(character.Location.CurrentMap.Value, Is.EqualTo(HomeMap),
                "there is no map in the request, and travel is a different authority");
        }

        [Test]
        public void AMapsAuthoredRadiusStillBoundsMovement()
        {
            MapDefinition map = null;
            Maps.TryGet(new DefinitionId(HomeMap), out map);

            SetPrivate(map, "_movementRadius", 2f);

            LivingCharacter character = AddPlayer();

            // Three steps of one metre, against a two-metre radius.
            for (var i = 1; i <= 3; i++)
            {
                Tick();

                _movement.Submit(Connection, 0f, 1f, i);
            }

            Assert.That(_movement.LastResult.Reason,
                Is.EqualTo(MovementRejection.OutOfBounds));
            Assert.That(character.Location.Position.Z, Is.LessThanOrEqualTo(2f));
        }

        // ---- the shape of the contract ---------------------------------------------------------

        [Test]
        public void TheMovementRequestCarriesIntentAndNothingElse()
        {
            System.Reflection.MethodInfo request =
                typeof(CharacterNetworkEntity).GetMethod("RequestMove");

            Assert.That(request, Is.Not.Null, "there is no client movement request at all");

            var names = new List<string>();

            foreach (System.Reflection.ParameterInfo parameter in request.GetParameters())
            {
                names.Add(parameter.Name.ToLowerInvariant());
            }

            Assert.That(names, Is.EquivalentTo(new[] { "inputx", "inputz", "sequence" }),
                "two axes and an ordering number -- no position, speed, delta, map or "
                + "character");
            Assert.That(request.ReturnType, Is.EqualTo(typeof(void)));
        }

        [Test]
        public void TheMovementRequestIsAServerRpcRequiringOwnership()
        {
            System.Reflection.MethodInfo request =
                typeof(CharacterNetworkEntity).GetMethod("RequestMove");

            object[] attributes = request.GetCustomAttributes(
                typeof(FishNet.Object.ServerRpcAttribute), true);

            Assert.That(attributes, Is.Not.Empty);

            var rpc = (FishNet.Object.ServerRpcAttribute)attributes[0];

            Assert.That(rpc.RequireOwnership, Is.True,
                "ownership is what stops one connection walking another's character");
        }

        [Test]
        public void NothingOnTheAuthorityAcceptsAPositionSpeedOrDelta()
        {
            System.Reflection.MethodInfo submit =
                typeof(CharacterMovementAuthority).GetMethod("Submit");

            foreach (System.Reflection.ParameterInfo parameter in submit.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("position"));
                Assert.That(name, Does.Not.Contain("destination"));
                Assert.That(name, Does.Not.Contain("speed"));
                Assert.That(name, Does.Not.Contain("delta"));
                Assert.That(name, Does.Not.Contain("time"));
                Assert.That(name, Does.Not.Contain("map"));
                Assert.That(name, Does.Not.Contain("character"));
            }
        }

        [Test]
        public void TheServerClockIsTheOnlyClockAndACallerCannotSetIt()
        {
            Assert.That(typeof(CharacterMovementAuthority).GetProperty("NowMilliseconds")
                .CanWrite, Is.False);

            var authority = new CharacterMovementAuthority(_players, _ => true, Maps, Speed);

            Assert.That(authority.NowMilliseconds, Is.Zero);

            authority.Tick(1f);

            Assert.That(authority.NowMilliseconds, Is.EqualTo(1000L));

            authority.Tick(-5f);

            Assert.That(authority.NowMilliseconds, Is.EqualTo(1000L),
                "a negative tick must not wind the clock back into an earlier step");
        }

        [Test]
        public void TheSimulatorReusesTheExistingValidatorRatherThanRestatingIt()
        {
            // If this file ever grows its own distance, bounds or sequence check, the two
            // will disagree the first time either is tuned.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/CharacterMovementSimulator.cs");

            Assert.That(source, Does.Contain("MovementValidator.Validate"),
                "the destination must be submitted to the rules that already exist");
        }

        // ---- guards on what 18.2 established ------------------------------------------------------

        [Test]
        public void ThereIsStillExactlyOneCharacterNetworkObject()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var entities = new List<string>();

            foreach (string file in files)
            {
                if (System.IO.File.ReadAllText(file).Contains(": NetworkBehaviour"))
                {
                    entities.Add(System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }

            Assert.That(entities, Is.EquivalentTo(new[]
            {
                "CharacterNetworkEntity", "MonsterNetworkEntity",
            }), "a second character network object would be a second authority");
        }

        [Test]
        public void ExactlyOneTypeReceivesClientMovementInput()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var sinks = new List<string>();

            foreach (string file in files)
            {
                if (System.IO.File.ReadAllText(file)
                    .Contains(": ICharacterMovementRequestSink"))
                {
                    sinks.Add(file.Replace('\\', '/'));
                }
            }

            Assert.That(sinks, Has.Count.EqualTo(1), string.Join(", ", sinks));
            Assert.That(sinks[0], Does.EndWith("/Server/CharacterMovementAuthority.cs"));
        }

        [Test]
        public void NoClientCodeWritesAuthoritativeState()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                string named = file.Replace('\\', '/');

                Assert.That(source, Does.Not.Contain("ServerPublishState"), named);
                Assert.That(source, Does.Not.Contain("CharacterMovementAuthority"), named);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), named);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), named);
            }
        }

        [Test]
        public void TheCombatAndDamageSystemsWereNotTouched()
        {
            // 18.3 supplies positions to combat. It does not change how combat uses them.
            foreach (string file in new[]
                     {
                         "Assets/_Game/Scripts/Server/ServerCombatPipeline.cs",
                         "Assets/_Game/Scripts/Gameplay/BasicDamageFormula.cs",
                         "Assets/_Game/Scripts/Server/MonsterRewardAuthority.cs",
                     })
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("CharacterMovementAuthority"),
                    file + " now knows about movement");
                Assert.That(source, Does.Not.Contain("RequestMove"), file);
            }
        }

        [Test]
        public void InventoryIsReplicatedOnlyToItsOwner()
        {
            // 18.3 asserted this file mentioned no inventory at all, because at that point
            // replicating one was a later gate. 18.4 did it, so the guard is restated rather
            // than deleted: what still must hold is that a bag never becomes a synchronised
            // value, because a SyncVar goes to every observer and a bag is private.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Network/CharacterNetworkEntity.cs");

            Assert.That(source, Does.Not.Contain("SyncVar<InventorySnapshot>"),
                "a synchronised bag is a bag everybody can read");
            Assert.That(source, Does.Not.Contain("[ObserversRpc]"),
                "an observers message would tell every player what is in somebody's bag");
            Assert.That(source, Does.Contain("[TargetRpc]"),
                "the owner is addressed directly, which is what makes it private");

            // And no live domain object crosses the wire: the snapshot is ids and numbers.
            Assert.That(source, Does.Not.Contain("ItemInstance "),
                "a client must never be handed an authoritative item object");
            Assert.That(source, Does.Not.Contain("ItemContainerState"));
        }
    }
}
