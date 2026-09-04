using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a client is allowed to claim about where it is.
    /// </summary>
    /// <remarks>
    /// This is the file that decides whether a speed hack works. Every rule in it only
    /// matters under attack, which is exactly why the validator is pure: reproducing a
    /// hostile client against a real socket is miserable and unreliable, and reproducing one
    /// against a function is a test.
    ///
    /// The property underneath all of them: <b>a refused move changes nothing.</b> A client
    /// must not be able to move by being ignored, so every rejection is checked to have left
    /// the authoritative position alone.
    /// </remarks>
    [TestFixture]
    internal sealed class MovementValidatorTests
    {
        private CharacterLocationState _location;
        private SpawnPointDefinition _spawn;
        private MapDefinition _map;

        private static readonly CharacterId Character = new CharacterId("char-1");
        private static readonly DefinitionId Map = new DefinitionId("map.town");

        /// <summary>Five metres a second, generous tolerance, unbounded map.</summary>
        private static MovementBudget Budget => new MovementBudget(5f);

        [SetUp]
        public void SetUp()
        {
            _spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();
            // (int)SpawnType.Player rather than a literal: Player is 0 and Monster is 1,
            // and writing 1 here authored a monster spawn that ArriveAt correctly refused.
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.town\"},\"_map\":{\"_value\":\"map.town\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                _spawn);

            _location = new CharacterLocationState(Character);
            _location.ArriveAt(_spawn);
        }

        [TearDown]
        public void TearDown()
        {
            if (_spawn != null) Object.DestroyImmediate(_spawn);
            if (_map != null) Object.DestroyImmediate(_map);
        }

        private MapDefinition MapWithRadius(float radius)
        {
            _map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"map.town\"},\"_movementRadius\":"
                + radius.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                _map);

            return _map;
        }

        private static MovementRequest Move(float x, float z, long sequence = 1,
            long timestamp = 1000, string map = "map.town", string character = "char-1",
            float y = 0f)
        {
            return new MovementRequest(new CharacterId(character), new DefinitionId(map),
                new CombatPosition(x, y, z), sequence, timestamp);
        }

        private MovementResult Validate(in MovementRequest request, MovementBudget? budget = null,
            long lastSequence = 0, long lastTimestamp = 0, bool alive = true)
        {
            return MovementValidator.Validate(request, _location, budget ?? Budget,
                lastSequence, lastTimestamp, alive);
        }

        // ---- the ordinary case -------------------------------------------------------------

        [Test]
        public void AReachableMoveIsAcceptedAndApplied()
        {
            // One second at five metres a second: four metres is comfortably legal.
            MovementResult result = Validate(Move(4f, 0f, 1, 1000));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_location.Position.X, Is.EqualTo(4f));
            Assert.That(result.DistanceTravelled, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void AMoveExactlyAtTheLimitIsAccepted()
        {
            // Five metres in one second, before tolerance. Must not be off-by-one refused.
            Assert.That(Validate(Move(5f, 0f, 1, 1000)).IsAccepted, Is.True);
        }

        [Test]
        public void ToleranceAllowsForJitterWithoutAllowingATeleport()
        {
            // 25% headroom: a legal move that arrived late is not a disconnection.
            Assert.That(Validate(Move(6f, 0f, 1, 1000)).IsAccepted, Is.True,
                "a server that disconnected everyone whose packet arrived late would be "
                + "correct and unplayable");

            SetUp();

            Assert.That(Validate(Move(7f, 0f, 1, 1000)).IsAccepted, Is.False,
                "headroom is not a licence");
        }

        // ---- the whole point: a refusal changes nothing ---------------------------------------

        [Test]
        public void ARefusedMoveLeavesTheCharacterExactlyWhereItWas()
        {
            Validate(Move(4f, 0f, 1, 1000));

            CombatPosition before = _location.Position;

            MovementResult refused = Validate(Move(9999f, 0f, 2, 1100), lastSequence: 1,
                lastTimestamp: 1000);

            Assert.That(refused.IsAccepted, Is.False);
            Assert.That(_location.Position, Is.EqualTo(before),
                "a client must not be able to move by being ignored");
        }

        [Test]
        public void ARefusalReportsTheAuthoritativePositionSoAClientCanCorrectItself()
        {
            Validate(Move(3f, 0f, 1, 1000));

            MovementResult refused = Validate(Move(9999f, 0f, 2, 1100), lastSequence: 1,
                lastTimestamp: 1000);

            Assert.That(refused.Position.X, Is.EqualTo(3f),
                "told the truth rather than left to guess");
        }

        // ---- 17.22: forged position -----------------------------------------------------------

        [Test]
        public void AnImpossibleDistanceIsRefused()
        {
            MovementResult result = Validate(Move(1000f, 0f, 1, 1000));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.TooFar));
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.NaN)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        public void ANonFinitePositionIsRefusedAsItsOwnReason(float x, float z)
        {
            MovementResult result = Validate(Move(x, z, 1, 1000));

            Assert.That(result.IsAccepted, Is.False);

            // Not "too far". NaN compares false against every bound, so a distance check
            // written the obvious way would have accepted this -- which is why finiteness
            // is checked first and named separately.
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.NotFinite));
        }

        [Test]
        public void ANaNHeightIsAlsoRefused()
        {
            Assert.That(Validate(Move(1f, 1f, 1, 1000, y: float.NaN)).Reason,
                Is.EqualTo(MovementRejection.NotFinite));
        }

        [Test]
        public void AClientClaimingAnotherMapIsRefused()
        {
            MovementResult result = Validate(Move(1f, 0f, 1, 1000, map: "map.cave"));

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.WrongMap));
        }

        [Test]
        public void AClientWithNoMapIsRefused()
        {
            Assert.That(Validate(Move(1f, 0f, 1, 1000, map: "")).Reason,
                Is.EqualTo(MovementRejection.WrongMap));
        }

        [Test]
        public void AClientMovingSomebodyElsesCharacterIsRefused()
        {
            MovementResult result = Validate(Move(1f, 0f, 1, 1000, character: "char-theirs"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.MissingContext));
        }

        // ---- claiming time to buy distance -------------------------------------------------------

        [Test]
        public void ClaimingAHugeElapsedTimeDoesNotBuyAnArbitraryDistance()
        {
            // An hour of "elapsed time" in one packet. Clamped to the maximum gap before it
            // is ever multiplied by speed -- the oldest trick in the book.
            MovementResult result = Validate(Move(5000f, 0f, 1, 3_600_000));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MovementRejection.TooFar));
        }

        [Test]
        public void TheClampStillAllowsALegitimateLongGap()
        {
            // Two seconds is the default cap: ten metres of movement, plus tolerance.
            Assert.That(Validate(Move(10f, 0f, 1, 5000)).IsAccepted, Is.True);
        }

        [Test]
        public void TwoPositionsWithTheSameTimestampAreRefused()
        {
            Validate(Move(1f, 0f, 1, 1000));

            MovementResult result = Validate(Move(4f, 0f, 2, 1000), lastSequence: 1,
                lastTimestamp: 1000);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.NoElapsedTime),
                "no time passed, so no distance can have been covered");
        }

        [Test]
        public void ATimestampGoingBackwardsIsRefused()
        {
            Assert.That(Validate(Move(1f, 0f, 2, 500), lastSequence: 1, lastTimestamp: 1000)
                .Reason, Is.EqualTo(MovementRejection.NoElapsedTime));
        }

        // ---- replay and reordering --------------------------------------------------------------

        [Test]
        public void AReplayedPacketIsRefused()
        {
            Validate(Move(4f, 0f, 5, 1000));

            MovementResult replayed = Validate(Move(4f, 0f, 5, 2000), lastSequence: 5,
                lastTimestamp: 1000);

            Assert.That(replayed.Reason, Is.EqualTo(MovementRejection.OutOfOrder));
        }

        [Test]
        public void AnOlderPacketArrivingLateIsRefused()
        {
            MovementResult result = Validate(Move(1f, 0f, 3, 2000), lastSequence: 7,
                lastTimestamp: 1000);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.OutOfOrder));
        }

        [Test]
        public void SequenceIsCheckedBeforeAnythingExpensive()
        {
            // A replayed packet carrying a NaN is refused for being old, not for the NaN:
            // the cheap check runs first so a flood of replays costs nothing.
            MovementResult result = Validate(Move(float.NaN, 0f, 1, 2000), lastSequence: 5,
                lastTimestamp: 1000);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.OutOfOrder));
        }

        // ---- state --------------------------------------------------------------------------------

        [Test]
        public void ADeadCharacterDoesNotMove()
        {
            MovementResult result = Validate(Move(1f, 0f, 1, 1000), alive: false);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.NotAlive));
            Assert.That(_location.Position, Is.EqualTo(CombatPosition.Zero));
        }

        [Test]
        public void ACharacterThatHasNotArrivedAnywhereCannotMove()
        {
            var nowhere = new CharacterLocationState(Character);

            MovementResult result = MovementValidator.Validate(Move(1f, 0f, 1, 1000), nowhere,
                Budget, 0, 0);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.NotInWorld));
        }

        [Test]
        public void NoLocationAtAllIsRefusedRatherThanCrashing()
        {
            MovementResult result = MovementValidator.Validate(Move(1f, 0f), null, Budget, 0, 0);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.MissingContext));
        }

        [Test]
        public void AZeroSpeedBudgetRefusesEverythingRatherThanAllowingIt()
        {
            MovementResult result = Validate(Move(1f, 0f, 1, 1000),
                budget: new MovementBudget(0f));

            Assert.That(result.IsAccepted, Is.False,
                "an unconfigured server must not become a permissive one");
        }

        // ---- authored bounds --------------------------------------------------------------------------

        [Test]
        public void APositionOutsideTheAuthoredRadiusIsRefused()
        {
            var bounded = new MovementBudget(1000f, 1f, maxRadius: 10f);

            MovementResult result = Validate(Move(50f, 0f, 1, 1000), budget: bounded);

            Assert.That(result.Reason, Is.EqualTo(MovementRejection.OutOfBounds));
        }

        [Test]
        public void APositionInsideTheAuthoredRadiusIsAccepted()
        {
            var bounded = new MovementBudget(1000f, 1f, maxRadius: 10f);

            Assert.That(Validate(Move(5f, 5f, 1, 1000), budget: bounded).IsAccepted, Is.True);
        }

        [Test]
        public void AnUnauthoredRadiusMeansUnboundedRatherThanForbidden()
        {
            // Existing content has no radius. Refusing every move on it would be worse
            // than allowing them; authoring a radius is what turns the check on.
            var unbounded = new MovementBudget(1000f, 1f, maxRadius: 0f);

            // 500,500 is 707m, inside the 1000m/s budget -- so if this is refused it is
            // the bounds check doing it, which is the point. 900,900 would be 1273m and
            // would fail for speed instead, proving nothing about bounds.
            Assert.That(Validate(Move(500f, 500f, 1, 1000), budget: unbounded).IsAccepted,
                Is.True);
        }

        [Test]
        public void HeightIsNotBounded()
        {
            // A map's floor and ceiling are geometry, not a number anybody authors. A
            // vertical bound invented here would refuse a staircase.
            var bounded = new MovementBudget(1000f, 1f, maxRadius: 10f);

            Assert.That(Validate(Move(1f, 1f, 1, 1000, y: 500f), budget: bounded).IsAccepted,
                Is.True);
        }

        [Test]
        public void TheRadiusComesFromTheMapDefinitionRatherThanALiteral()
        {
            MovementBudget budget = MovementValidator.BudgetFor(5f, MapWithRadius(42f));

            Assert.That(budget.MaxRadius, Is.EqualTo(42f));
        }

        [Test]
        public void NoMapMeansNoRadius()
        {
            Assert.That(MovementValidator.BudgetFor(5f, null).MaxRadius, Is.EqualTo(0f));
        }

        // ---- the budget itself ----------------------------------------------------------------------

        [Test]
        public void ToleranceBelowOneIsRaisedRatherThanShrinkingTheBudget()
        {
            // A tolerance under one would make the server stricter than the real speed,
            // refusing honest movement. Clamped rather than trusted.
            Assert.That(new MovementBudget(5f, 0.1f).ToleranceFactor, Is.EqualTo(1f));
        }

        [Test]
        public void ANegativeSpeedBecomesZeroAndThereforeRefusesEverything()
        {
            var budget = new MovementBudget(-5f);

            Assert.That(budget.MetresPerSecond, Is.EqualTo(0f));
            Assert.That(budget.IsUsable, Is.False);
        }

        [Test]
        public void AZeroMaxElapsedIsRaisedSoTimeCannotBeDividedByNothing()
        {
            Assert.That(new MovementBudget(5f, 1f, 0f, 0L).MaxElapsedMilliseconds,
                Is.GreaterThan(0L));
        }

        // ---- a run of moves ---------------------------------------------------------------------------

        [Test]
        public void ASequenceOfLegalMovesAccumulatesCorrectly()
        {
            long sequence = 0;
            long timestamp = 0;

            for (int i = 0; i < 20; i++)
            {
                sequence++;
                long previous = timestamp;
                timestamp += 200;

                MovementResult result = MovementValidator.Validate(
                    Move(_location.Position.X + 1f, 0f, sequence, timestamp),
                    _location, Budget, sequence - 1, previous);

                Assert.That(result.IsAccepted, Is.True, "step " + i);
            }

            Assert.That(_location.Position.X, Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void OneRefusalInARunDoesNotDesynchroniseTheRest()
        {
            MovementValidator.Validate(Move(2f, 0f, 1, 1000), _location, Budget, 0, 0);

            // A cheat attempt in the middle.
            MovementValidator.Validate(Move(9999f, 0f, 2, 1200), _location, Budget, 1, 1000);

            // The next honest move still works, measured from where the character really is.
            MovementResult next = MovementValidator.Validate(Move(3f, 0f, 3, 1400), _location,
                Budget, 2, 1200);

            Assert.That(next.IsAccepted, Is.True);
            Assert.That(_location.Position.X, Is.EqualTo(3f));
        }
    }
}
