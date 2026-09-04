using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Moving a monster: the step itself, in isolation.
    /// </summary>
    /// <remarks>
    /// Pure and engine-free, so every rule is exercised by arithmetic rather than by standing
    /// a world up. The property underneath all of them is the same as everywhere else in this
    /// phase: <b>a refused step changes nothing</b>.
    ///
    /// Behaviour is not tested here. Which state a monster is in and what it is targeting are
    /// <c>MonsterAiController</c>'s, decided before this runs and covered by Phase 10's own
    /// tests. This answers one question: given that state, where is the monster a moment
    /// later.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterMovementTests : MonsterTestBase
    {
        private const string Fast = "monster.fast";     // speed 10
        private const string Still = "monster.still";   // speed 0

        private MonsterRuntimeState _monster;

        [SetUp]
        public void SetUpMovement()
        {
            AddMonsterWithSpeed(Fast, 10f);
            AddMonsterWithSpeed(Still, 0f);

            _monster = NewMonster(Fast, spawn: new CombatPosition(0f, 0f, 0f));
        }

        /// <summary>Authors a monster with an explicit move speed.</summary>
        /// <remarks>MonsterTestBase does not expose speed, and widening it would change a
        /// fixture five other suites depend on. Setting the one private field afterwards is
        /// the smaller change.</remarks>
        private MonsterDefinition AddMonsterWithSpeed(string id, float speed)
        {
            MonsterDefinition definition = AddMonster(id, level: 5,
                aggression: MonsterAggressionType.Aggressive, detection: 100f,
                attackRange: 1f, leash: 1000f);

            SetPrivate(definition, "_moveSpeed", speed);

            return definition;
        }

        private MonsterRuntimeState NewMonster(string id, CombatPosition spawn)
        {
            Assert.That(Monsters.TryGet(new DefinitionId(id), out MonsterDefinition definition),
                Is.True, "fixture");

            // Max health is an int the caller resolves from the authored stat, and the team
            // is supplied because factions are content -- neither is inferred here.
            definition.TryGetStat(new DefinitionId(MaxHp), out int maxHealth);

            return new MonsterRuntimeState(InstanceId.New(), definition, spawn, maxHealth,
                Enemies);
        }

        private static CombatPosition At(float x, float z = 0f, float y = 0f)
        {
            return new CombatPosition(x, y, z);
        }

        // ---- 1, 2: a living monster moves toward a target, bounded by speed x delta ----------

        [Test]
        public void AChasingMonsterMovesTowardItsTarget()
        {
            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(100f), 0.5f);

            Assert.That(result.Moved, Is.True, result.Reason.ToString());
            Assert.That(_monster.Position.X, Is.EqualTo(5f).Within(0.001f),
                "speed 10 for half a second");
            Assert.That(result.Distance, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void TheStepIsBoundedBySpeedTimesDelta()
        {
            MonsterMovement.Step(_monster, MonsterAiState.Chase, At(1000f), 1f);

            Assert.That(_monster.Position.X, Is.EqualTo(10f).Within(0.001f),
                "one second at speed 10, whatever the distance to the target");
        }

        [Test]
        public void AMonsterNeverOvershootsItsDestination()
        {
            // A long tick must not throw it past the target and leave it oscillating.
            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(2f), 100f);

            Assert.That(result.Moved, Is.True);
            Assert.That(_monster.Position.X, Is.EqualTo(2f).Within(0.001f));
            Assert.That(result.Distance, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void MovementIsDiagonalWhenTheTargetIs()
        {
            MonsterMovement.Step(_monster, MonsterAiState.Chase, At(3f, 4f), 0.5f);

            // 3-4-5 triangle: five metres of budget lands exactly on the target.
            Assert.That(_monster.Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(_monster.Position.Z, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void ArrivingWithinToleranceCountsAsArrivedRatherThanOscillating()
        {
            _monster.Position = At(100f);

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(100f), 1f);

            Assert.That(result.Moved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.AlreadyThere));
        }

        // ---- 3: zero and negative delta -------------------------------------------------------

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(-1000f)]
        public void NoTimePassingMeansNoMovement(float delta)
        {
            CombatPosition before = _monster.Position;

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(100f), delta);

            Assert.That(result.Moved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.NoElapsedTime));
            Assert.That(_monster.Position, Is.EqualTo(before));
        }

        // ---- 4: a corpse does not walk -----------------------------------------------------------

        [Test]
        public void ADeadMonsterDoesNotMove()
        {
            _monster.ApplyHealthDelta(-10000);

            CombatPosition before = _monster.Position;

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(100f), 1f);

            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.NotAlive));
            Assert.That(_monster.Position, Is.EqualTo(before));
        }

        [Test]
        public void ADeadMonsterDoesNotMoveEvenInAStateThatNormallyWould()
        {
            _monster.Position = At(50f);
            _monster.ApplyHealthDelta(-10000);

            // Death outranks the state, whatever the state says.
            Assert.That(MonsterMovement.Step(_monster, MonsterAiState.Return, null, 1f).Reason,
                Is.EqualTo(MonsterMoveRejection.NotAlive));
            Assert.That(_monster.Position.X, Is.EqualTo(50f));
        }

        // ---- 5: nothing to move --------------------------------------------------------------------

        [Test]
        public void NoMonsterAtAllIsRefusedRatherThanCrashing()
        {
            MonsterMoveResult result = MonsterMovement.Step(null, MonsterAiState.Chase,
                At(1f), 1f);

            Assert.That(result.Moved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.MissingContext));
        }

        // ---- 6, 8: states and targets that do not move ------------------------------------------------

        [TestCase(MonsterAiState.Idle)]
        [TestCase(MonsterAiState.Detect)]
        [TestCase(MonsterAiState.Attack)]
        [TestCase(MonsterAiState.Wander)]
        [TestCase(MonsterAiState.Dead)]
        public void TheseStatesDoNotMove(MonsterAiState state)
        {
            CombatPosition before = _monster.Position;

            MonsterMoveResult result = MonsterMovement.Step(_monster, state, At(100f), 1f);

            Assert.That(result.Moved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.StateDoesNotMove));
            Assert.That(_monster.Position, Is.EqualTo(before));
        }

        [Test]
        public void AMonsterAlreadyInReachDoesNotKeepWalkingIntoItsTarget()
        {
            // Attack is stationary on purpose: a monster in range has arrived.
            Assert.That(MonsterMovement.Step(_monster, MonsterAiState.Attack, At(1f), 1f).Moved,
                Is.False);
        }

        [Test]
        public void WanderIsStationaryBecauseTheControllerNeverEntersIt()
        {
            // The state exists in Phase 10's enum but no transition reaches it. Wander
            // behaviour written here would be dead code that looked implemented.
            Assert.That(MonsterMovement.Step(_monster, MonsterAiState.Wander, At(100f), 1f)
                .Reason, Is.EqualTo(MonsterMoveRejection.StateDoesNotMove));
        }

        [Test]
        public void ChasingNothingMovesNothing()
        {
            CombatPosition before = _monster.Position;

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                null, 1f);

            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.NoDestination));
            Assert.That(_monster.Position, Is.EqualTo(before));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ANonFiniteDestinationIsRefused(float x)
        {
            CombatPosition before = _monster.Position;

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(x), 1f);

            Assert.That(result.Moved, Is.False,
                "a position that is not a number would corrupt every distance check after it");
            Assert.That(_monster.Position, Is.EqualTo(before));
        }

        // ---- returning home -------------------------------------------------------------------------------

        [Test]
        public void AReturningMonsterWalksBackTowardItsSpawn()
        {
            _monster.Position = At(100f);

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Return,
                null, 1f);

            Assert.That(result.Moved, Is.True, "no target is needed to go home");
            Assert.That(_monster.Position.X, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void AReturningMonsterIgnoresAnyTargetItIsHanded()
        {
            _monster.Position = At(100f);

            // Home is the destination in Return, whatever else is passed.
            MonsterMovement.Step(_monster, MonsterAiState.Return, At(1000f), 1f);

            Assert.That(_monster.Position.X, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void AMonsterAlreadyHomeStaysThere()
        {
            Assert.That(MonsterMovement.Step(_monster, MonsterAiState.Return, null, 1f).Reason,
                Is.EqualTo(MonsterMoveRejection.AlreadyThere));
        }

        // ---- authored speed ----------------------------------------------------------------------------------

        [Test]
        public void SpeedComesFromTheAuthoredDefinition()
        {
            MonsterRuntimeState slow = NewMonster(Grunt, At(0f));

            // The grunt's authored speed is the fixture default of 2, not the fast one's 10.
            MonsterMovement.Step(slow, MonsterAiState.Chase, At(100f), 1f);

            Assert.That(slow.Position.X, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void AMonsterAuthoredWithNoSpeedCannotMove()
        {
            MonsterRuntimeState stationary = NewMonster(Still, At(0f));

            MonsterMoveResult result = MonsterMovement.Step(stationary, MonsterAiState.Chase,
                At(100f), 1f);

            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.NoSpeed),
                "a turret or a plant, reported rather than silently doing nothing");
            Assert.That(stationary.Position.X, Is.Zero);
        }

        // ---- 12: the authored map bound -------------------------------------------------------------------------

        [Test]
        public void AStepThatWouldLeaveTheMapIsRefused()
        {
            _monster.Position = At(9f);

            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(100f), 1f, maxRadius: 10f);

            Assert.That(result.Reason, Is.EqualTo(MonsterMoveRejection.OutOfBounds));
            Assert.That(_monster.Position.X, Is.EqualTo(9f), "and it did not budge");
        }

        [Test]
        public void AStepInsideTheBoundIsAllowed()
        {
            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(5f), 0.2f, maxRadius: 10f);

            Assert.That(result.Moved, Is.True);
            Assert.That(_monster.Position.X, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void AnUnauthoredRadiusMeansUnbounded()
        {
            // Existing content has no radius. Refusing every step on it would be worse than
            // allowing them, exactly as for players.
            Assert.That(MonsterMovement.Step(_monster, MonsterAiState.Chase, At(1000f), 50f,
                maxRadius: 0f).Moved, Is.True);
        }

        [Test]
        public void HeightIsNotBounded()
        {
            MonsterMoveResult result = MonsterMovement.Step(_monster, MonsterAiState.Chase,
                At(1f, 0f, y: 500f), 0.1f, maxRadius: 10f);

            Assert.That(result.Moved, Is.True, "a floor and a ceiling are geometry, not a number");
        }

        // ---- 13: determinism ------------------------------------------------------------------------------------

        [Test]
        public void RepeatedTicksAreDeterministic()
        {
            MonsterRuntimeState first = NewMonster(Fast, At(0f));
            MonsterRuntimeState second = NewMonster(Fast, At(0f));

            for (int i = 0; i < 25; i++)
            {
                MonsterMovement.Step(first, MonsterAiState.Chase, At(1000f), 0.1f);
                MonsterMovement.Step(second, MonsterAiState.Chase, At(1000f), 0.1f);
            }

            Assert.That(second.Position.X, Is.EqualTo(first.Position.X),
                "no clock and no randomness: the same inputs give the same output");
            Assert.That(first.Position.X, Is.EqualTo(25f).Within(0.001f));
        }

        [Test]
        public void ManySmallStepsCoverTheSameGroundAsOneLargeOne()
        {
            MonsterRuntimeState stepped = NewMonster(Fast, At(0f));
            MonsterRuntimeState leaped = NewMonster(Fast, At(0f));

            for (int i = 0; i < 10; i++)
            {
                MonsterMovement.Step(stepped, MonsterAiState.Chase, At(1000f), 0.1f);
            }

            MonsterMovement.Step(leaped, MonsterAiState.Chase, At(1000f), 1f);

            Assert.That(stepped.Position.X, Is.EqualTo(leaped.Position.X).Within(0.001f),
                "tick length changes smoothness, not distance");
        }

        // ---- 9, 14: the server owns the position -------------------------------------------------------------------

        [Test]
        public void ThereIsNoWayToSetAMonsterPositionThroughThisApi()
        {
            // Step takes a destination to walk toward, bounded by speed and time. It has no
            // parameter that places a monster anywhere, which is what makes teleport-by-
            // command unrepresentable rather than refused.
            System.Reflection.MethodInfo step = typeof(MonsterMovement).GetMethod("Step");

            Assert.That(step, Is.Not.Null);

            foreach (System.Reflection.ParameterInfo parameter in step.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("teleport"));
                Assert.That(name, Does.Not.Contain("setposition"));
                Assert.That(name, Does.Not.Contain("connection"));
                Assert.That(parameter.ParameterType.Name, Does.Not.Contain("Command"),
                    "a monster takes no client command");
            }
        }

        [Test]
        public void ADestinationIsWalkedTowardRatherThanJumpedTo()
        {
            // Handing a far destination does not place the monster there, however large the
            // number: the step is always speed x delta.
            MonsterMovement.Step(_monster, MonsterAiState.Chase, At(999999f), 0.1f);

            Assert.That(_monster.Position.X, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TheMoverIsEngineFreeAndHoldsNoState()
        {
            System.Type mover = typeof(MonsterMovement);

            Assert.That(mover.IsAbstract && mover.IsSealed, Is.True,
                "static: there is no instance state a client could influence");
            Assert.That(mover.GetFields(System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Instance).Length, Is.Zero);
        }
    }
}
