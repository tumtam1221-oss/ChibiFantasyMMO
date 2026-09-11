using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Monsters with nothing to fight mill about their camp instead of standing still.
    /// </summary>
    /// <remarks>
    /// <b>What was wrong.</b> <c>MonsterAiState.Wander</c> had existed since Phase 10 and
    /// nothing ever entered it. The controller's "no target" branch put every idle monster
    /// back into <c>Idle</c> on every tick, and the movement step listed <c>Wander</c> among
    /// the states that do not move -- so a camp of slimes stood on four exact spots forever,
    /// which reads as a world that has not been switched on.
    ///
    /// <b>What these hold.</b> The properties a player would notice, not the mechanism:
    /// they move, they stay in their own camp, they do not move in step with each other, and
    /// nothing they do can interrupt or delay a fight. The rest -- which class holds the
    /// timer, how the disc is sampled -- is free to change.
    ///
    /// Engine-free and clockless throughout: every one of these drives the same tick the
    /// server drives, with the tick length supplied as an argument.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterWanderTests : MonsterTestBase
    {
        private const string Stroller = "monster.stroller";

        /// <summary>The camp: six metres across, centred away from the origin.</summary>
        private static readonly CombatPosition Nest = new CombatPosition(20f, 0f, -12f);

        private const float NestRadius = 6f;

        /// <summary>A server tick. Twenty a second, as the world server runs.</summary>
        private const float Tick = 0.05f;

        [SetUp]
        public void SetUpWander()
        {
            MonsterDefinition definition = AddMonster(Stroller, level: 3,
                aggression: MonsterAggressionType.Aggressive, detection: 8f,
                attackRange: 1.5f, leash: 40f);

            SetPrivate(definition, "_moveSpeed", 1.5f);
        }

        // ---- fixtures ------------------------------------------------------------------

        private MonsterRuntimeState NewMonster(CombatPosition at, string id = "m:1")
        {
            Assert.That(Monsters.TryGet(new DefinitionId(Stroller),
                out MonsterDefinition definition), Is.True, "fixture");

            Assert.That(definition.TryGetStat(new DefinitionId(MaxHp), out int maxHealth),
                Is.True, "fixture");

            return new MonsterRuntimeState(new InstanceId(id), definition, at, maxHealth,
                Enemies);
        }

        /// <summary>One monster, its controller and its plan, as the world runtime holds them.</summary>
        private sealed class Living
        {
            public MonsterRuntimeState State;
            public MonsterAiController Ai;
            public MonsterWanderPlan Plan;

            /// <summary>One server tick, in the order the world runtime runs them.</summary>
            public void Advance(float deltaSeconds, params ICombatant[] candidates)
            {
                Ai.Tick(deltaSeconds, candidates);
                Plan.Tick(deltaSeconds, Ai);
                MonsterMovement.Step(State, Ai.State, null, deltaSeconds);
            }
        }

        private Living Alone(string id = "m:1", float radius = NestRadius,
            CombatPosition? at = null)
        {
            MonsterRuntimeState state = NewMonster(at ?? Nest, id);

            return new Living
            {
                State = state,
                Ai = new MonsterAiController(state),
                Plan = new MonsterWanderPlan(new InstanceId(id), Nest, radius),
            };
        }

        private static float FlatDistance(in CombatPosition a, in CombatPosition b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;

            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        // ---- what a player would notice -------------------------------------------------

        [Test]
        public void AMonsterLeftAloneDoesNotStandOnOneSpotForever()
        {
            // The whole point. This failed before the plan existed, however long it ran.
            Living slime = Alone();

            CombatPosition start = slime.State.Position;

            for (var i = 0; i < 400; i++) slime.Advance(Tick);

            Assert.That(FlatDistance(start, slime.State.Position),
                Is.GreaterThan(MonsterWanderPlan.MinimumWalk),
                "twenty seconds alone and it never left the spot it spawned on");
        }

        [Test]
        public void ItKeepsMovingRatherThanTakingOneWalkAndStopping()
        {
            // A single stroll would satisfy the test above while still leaving a camp that
            // freezes a few seconds in.
            Living slime = Alone();

            var walks = 0;
            var wandering = false;

            for (var i = 0; i < 2000; i++)
            {
                slime.Advance(Tick);

                bool now = slime.Ai.State == MonsterAiState.Wander;

                if (now && !wandering) walks++;

                wandering = now;
            }

            Assert.That(walks, Is.GreaterThan(4),
                "a hundred seconds produced " + walks + " walks: it strolls once and gives up");
        }

        [Test]
        public void ItStaysInsideTheCampItCameFrom()
        {
            // Otherwise the camps blur into each other and a quest target nobody can find.
            Living slime = Alone();

            var farthest = 0f;

            for (var i = 0; i < 4000; i++)
            {
                slime.Advance(Tick);

                float distance = FlatDistance(Nest, slime.State.Position);

                if (distance > farthest) farthest = distance;
            }

            Assert.That(farthest, Is.LessThanOrEqualTo(NestRadius + 0.01f),
                "strayed " + farthest + " m from a " + NestRadius + " m camp");
        }

        [Test]
        public void TwoMonstersFromOneCampDoNotWalkInStep()
        {
            // A camp that moves as one body reads as a chorus line, not as animals. They
            // differ only by identity, which is the only thing that differs in the world.
            Living first = Alone("m:first");
            Living second = Alone("m:second");

            var apart = 0;

            for (var i = 0; i < 1200; i++)
            {
                first.Advance(Tick);
                second.Advance(Tick);

                if ((first.Ai.State == MonsterAiState.Wander)
                    != (second.Ai.State == MonsterAiState.Wander))
                {
                    apart++;
                }
            }

            Assert.That(apart, Is.GreaterThan(100),
                "two slimes from one nest were doing the same thing on almost every tick");
        }

        [Test]
        public void TheSameMonsterTakesTheSameWalkTwice()
        {
            // Determinism is what makes every other test here meaningful, and it is why the
            // seed is the monster's identity rather than a shared generator.
            Living first = Alone("m:same");
            Living second = Alone("m:same");

            for (var i = 0; i < 600; i++)
            {
                first.Advance(Tick);
                second.Advance(Tick);
            }

            Assert.That(second.State.Position, Is.EqualTo(first.State.Position));
        }

        // ---- and what it must never disturb ---------------------------------------------

        [Test]
        public void AMonsterWithSomethingToFightDoesNotStroll()
        {
            // The rule that makes wandering safe to add to a live combat system: a stroll
            // can never pre-empt, delay, or interrupt a fight.
            Living slime = Alone();

            FakeCombatant player = Player(Nest.X + 3f, 0f, Nest.Z);

            for (var i = 0; i < 600; i++)
            {
                slime.Advance(Tick, player);

                Assert.That(slime.Ai.State, Is.Not.EqualTo(MonsterAiState.Wander),
                    "it wandered off mid-fight on tick " + i);
            }

            Assert.That(slime.State.HasWanderDestination, Is.False);
        }

        [Test]
        public void ABeginWanderIsRefusedOutrightWhileChasing()
        {
            // Asserted directly as well, because the test above would also pass if the plan
            // merely happened never to ask.
            Living slime = Alone();

            FakeCombatant player = Player(Nest.X + 5f, 0f, Nest.Z);

            for (var i = 0; i < 40; i++) slime.Advance(Tick, player);

            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Chase), "fixture");
            Assert.That(slime.Ai.BeginWander(Nest), Is.False);
            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Chase));
        }

        [Test]
        public void ACorpseDoesNotStroll()
        {
            Living slime = Alone();

            slime.State.SetHealth(0);

            CombatPosition where = slime.State.Position;

            for (var i = 0; i < 600; i++) slime.Advance(Tick);

            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Dead));
            Assert.That(slime.State.Position, Is.EqualTo(where));
        }

        [Test]
        public void ANestWithNoRadiusProducesMonstersThatNeverMove()
        {
            // "They stand exactly here" is a thing content can say, and the boss nest says
            // it. A stroll of radius zero would otherwise be a jitter on the spot.
            Living sentry = Alone("m:sentry", radius: 0f);

            CombatPosition where = sentry.State.Position;

            for (var i = 0; i < 1200; i++) sentry.Advance(Tick);

            Assert.That(sentry.Plan.CanWander, Is.False);
            Assert.That(sentry.State.Position, Is.EqualTo(where));
        }

        [Test]
        public void AFightClearsTheStrollItWasOn()
        {
            // A destination left lying about would drag the monster back to it the moment
            // the fight ended, which reads as a monster that will not stay put.
            Living slime = Alone();

            for (var i = 0; i < 400 && slime.Ai.State != MonsterAiState.Wander; i++)
            {
                slime.Advance(Tick);
            }

            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Wander), "fixture");
            Assert.That(slime.State.HasWanderDestination, Is.True, "fixture");

            FakeCombatant player = Player(Nest.X + 2f, 0f, Nest.Z);

            slime.Advance(Tick, player);

            Assert.That(slime.Ai.State, Is.Not.EqualTo(MonsterAiState.Wander));
            Assert.That(slime.State.HasWanderDestination, Is.False);
        }

        [Test]
        public void AStrollItCannotFinishIsGivenUpRatherThanLeaningOnTheObstacle()
        {
            // The movement step refuses a walk into a safe zone, off the map, or onto ground
            // nothing stands on. Without the give-up timer a monster that picked such a spot
            // would push against it until the server restarted.
            Living slime = Alone();

            for (var i = 0; i < 400 && slime.Ai.State != MonsterAiState.Wander; i++)
            {
                slime.Advance(Tick);
            }

            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Wander), "fixture");

            // Never actually moved: exactly what a monster wedged against a wall sees.
            var ticks = 0;

            while (slime.Ai.State == MonsterAiState.Wander && ticks < 1000)
            {
                slime.Ai.Tick(Tick, System.Array.Empty<ICombatant>());
                slime.Plan.Tick(Tick, slime.Ai);

                ticks++;
            }

            Assert.That(slime.Ai.State, Is.EqualTo(MonsterAiState.Idle));
            Assert.That(ticks * Tick, Is.LessThanOrEqualTo(MonsterWanderPlan.GiveUpSeconds + Tick));
        }

        [Test]
        public void AStrollingMonsterIsOnTheGroundAtEveryStep()
        {
            // Wandering walks the same step a chase walks, so it inherits the ground
            // sampling that stops a monster hovering over a hillside. Asserted because
            // wandering is the first thing that moves a monster nobody is fighting.
            Living slime = Alone();

            var ground = new SlopedGround();

            for (var i = 0; i < 1200; i++)
            {
                slime.Ai.Tick(Tick, System.Array.Empty<ICombatant>());
                slime.Plan.Tick(Tick, slime.Ai);
                MonsterMovement.Step(slime.State, slime.Ai.State, null, Tick, 0f, null, ground);

                CombatPosition where = slime.State.Position;

                if (slime.Ai.State != MonsterAiState.Wander) continue;

                Assert.That(ground.TrySample(where.X, where.Z, out float y), Is.True);
                Assert.That(where.Y, Is.EqualTo(y).Within(0.001f),
                    "floating above, or sunk into, the ground at " + where);
            }
        }

        /// <summary>Ground that is nowhere flat, so standing on it is a real requirement.</summary>
        private sealed class SlopedGround : IGroundHeight
        {
            public bool TrySample(float x, float z, out float height)
            {
                height = 0.02f * x - 0.015f * z;

                return true;
            }
        }
    }
}
