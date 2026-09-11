using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// How the Training Slime behaves, rather than merely that it works.
    /// </summary>
    /// <remarks>
    /// <b>Why a second slime suite.</b> <see cref="TrainingSlimeRuntimeTests"/> holds the
    /// loop: it spawns, it can be fought, it pays out, it comes back. Every one of those
    /// passed while the slime was faster than the player, attacked with no anticipation,
    /// spawned six copies on one spot and wore its name at all times. Those are not bugs in
    /// the loop -- they are the difference between a monster that functions and one that is
    /// pleasant to fight, and they need their own assertions or they regress silently.
    ///
    /// <b>They read authored content, not constants.</b> "Slower than the player" is checked
    /// against the walk speed the catalogue actually ships, so re-balancing either one keeps
    /// the relationship honest instead of quietly inverting it.
    /// </remarks>
    [TestFixture]
    internal sealed class SlimeGameplayFeelTests : MonsterTestBase
    {
        private const string Slime = "monster.training_slime";

        private const string ContentPath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private static WorldContentCatalogue Content()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(ContentPath);

            Assert.That(catalogue, Is.Not.Null, ContentPath + " is missing");

            return catalogue;
        }

        private static MonsterDefinition Authored()
        {
            Assert.That(Content().BuildMonsters().TryGet(new DefinitionId(Slime),
                out MonsterDefinition slime), Is.True, Slime + " is not in the catalogue");

            return slime;
        }

        // ---- speed: it must not outrun the player ----------------------------------------

        [Test]
        public void TheSlimeIsSlowerThanAPlayerWalking()
        {
            // It was authored at 1.5 m/s against a player walk of 1.38, so a level-one
            // monster could run a player down and they could never break away. Compared
            // against the shipped figure rather than a number written here, so re-balancing
            // one of them cannot silently invert the relationship.
            float player = Content().WalkMetresPerSecond;
            MonsterDefinition slime = Authored();

            Assert.That(player, Is.GreaterThan(0f), "fixture");
            Assert.That(slime.MoveSpeed, Is.LessThan(player),
                "a beginner monster chases at " + slime.MoveSpeed + " m/s and the player "
                + "walks at " + player + " m/s, so they can never get away");
        }

        [Test]
        public void AStrollIsSlowerThanAChase()
        {
            MonsterDefinition slime = Authored();

            Assert.That(slime.WanderSpeed, Is.LessThan(slime.MoveSpeed),
                "a camp milling about at chase speed reads as a swarm, not as idling");
        }

        [Test]
        public void AMonsterThatAuthorsNoStrollSpeedStrollsAtItsWalkingSpeed()
        {
            // Every monster in the project except this one authors nothing, and none of
            // them may change behaviour because the field was added.
            MonsterDefinition plain = AddMonster("monster.unauthored");

            SetPrivate(plain, "_moveSpeed", 3.25f);

            Assert.That(plain.WanderSpeed, Is.EqualTo(3.25f));
        }

        [Test]
        public void StrollingUsesTheStrollSpeedAndChasingUsesTheOther()
        {
            // Asserted through the movement step, because a property nothing reads is not a
            // speed.
            MonsterDefinition definition = AddMonster("monster.twospeed",
                aggression: MonsterAggressionType.Aggressive, detection: 50f, leash: 500f);

            SetPrivate(definition, "_moveSpeed", 4f);
            SetPrivate(definition, "_wanderSpeed", 1f);

            Assert.That(definition.TryGetStat(new DefinitionId(MaxHp), out int health), Is.True);

            var strolling = new MonsterRuntimeState(new InstanceId("m:stroll"), definition,
                new CombatPosition(0f, 0f, 0f), health, Enemies);
            var chasing = new MonsterRuntimeState(new InstanceId("m:chase"), definition,
                new CombatPosition(0f, 0f, 0f), health, Enemies);

            strolling.SetWanderDestination(new CombatPosition(100f, 0f, 0f));

            MonsterMoveResult stroll = MonsterMovement.Step(strolling, MonsterAiState.Wander,
                null, 1f);
            MonsterMoveResult chase = MonsterMovement.Step(chasing, MonsterAiState.Chase,
                new CombatPosition(100f, 0f, 0f), 1f);

            Assert.That(stroll.Distance, Is.EqualTo(1f).Within(0.001f));
            Assert.That(chase.Distance, Is.EqualTo(4f).Within(0.001f));
        }

        // ---- attack rhythm ----------------------------------------------------------------

        private sealed class Bout
        {
            public MonsterRuntimeState State;
            public MonsterAiController Ai;
            public FakeCombatant Player;

            public int Swings;

            public void Advance(float deltaSeconds, int ticks)
            {
                for (var i = 0; i < ticks; i++)
                {
                    Ai.Tick(deltaSeconds, new ICombatant[] { Player });

                    if (Ai.WantsToAttack) Swings++;
                }
            }
        }

        private Bout Fight(string monsterId)
        {
            Assert.That(Monsters.TryGet(new DefinitionId(monsterId),
                out MonsterDefinition definition), Is.True, "fixture");
            Assert.That(definition.TryGetStat(new DefinitionId(MaxHp), out int health), Is.True);

            var state = new MonsterRuntimeState(new InstanceId("m:bout"), definition,
                new CombatPosition(0f, 0f, 0f), health, Enemies);

            return new Bout
            {
                State = state,
                Ai = new MonsterAiController(state),
                Player = Player(0.5f, 0f, 0f),
            };
        }

        private MonsterDefinition Brawler(float windup, float recovery, float cooldown)
        {
            MonsterDefinition definition = AddMonster("monster.brawler",
                aggression: MonsterAggressionType.Aggressive, detection: 20f,
                attackRange: 3f, cooldown: cooldown, leash: 100f);

            SetPrivate(definition, "_attackWindupSeconds", windup);
            SetPrivate(definition, "_attackRecoverySeconds", recovery);

            return definition;
        }

        [Test]
        public void AnAttackCannotFireOnEveryTick()
        {
            // The shape of the complaint: a monster standing in reach raised the intent the
            // moment its cooldown allowed and nothing else, which at twenty ticks a second
            // with anything less than a full cooldown reads as spam.
            Brawler(windup: 0.2f, recovery: 0.35f, cooldown: 2f);

            Bout bout = Fight("monster.brawler");

            bout.Advance(0.05f, 200);   // ten seconds

            Assert.That(bout.Swings, Is.LessThanOrEqualTo(5),
                "ten seconds produced " + bout.Swings + " swings");
            Assert.That(bout.Swings, Is.GreaterThan(0), "and it must still fight back");
        }

        [Test]
        public void TheFirstSwingWaitsForTheAnticipation()
        {
            // Damage that lands on the tick a monster arrives has nothing a player could
            // have seen coming. The windup is server-side for exactly that reason: it delays
            // the damage, not only the picture.
            Brawler(windup: 0.5f, recovery: 0f, cooldown: 0f);

            Bout bout = Fight("monster.brawler");

            bout.Advance(0.05f, 8);     // 0.4s -- still winding up

            Assert.That(bout.Swings, Is.Zero, "it struck before it had finished winding up");
            Assert.That(bout.Ai.State, Is.EqualTo(MonsterAiState.Attack));
            Assert.That(bout.Ai.IsCommittedToASwing, Is.True);

            bout.Advance(0.05f, 4);     // past 0.5s

            Assert.That(bout.Swings, Is.EqualTo(1));
        }

        [Test]
        public void ARecoveryHoldsItStillAfterTheSwing()
        {
            // Without one it snaps straight back to whatever it was doing, which is what
            // makes a fight read as a machine rather than as a creature.
            Brawler(windup: 0f, recovery: 0.6f, cooldown: 0f);

            Bout bout = Fight("monster.brawler");

            bout.Advance(0.05f, 1);

            Assert.That(bout.Swings, Is.EqualTo(1), "fixture: it swung");
            Assert.That(bout.Ai.RecoveryRemaining, Is.GreaterThan(0f));

            bout.Advance(0.05f, 10);    // 0.5s of recovery

            Assert.That(bout.Swings, Is.EqualTo(1), "it swung again while still recovering");

            bout.Advance(0.05f, 6);

            Assert.That(bout.Swings, Is.EqualTo(2));
        }

        [Test]
        public void AnInterruptedWindupHasToStartAgain()
        {
            // Otherwise a monster that lost its target mid-anticipation strikes the instant
            // it catches anybody again, which is the machine-gun the windup exists to stop.
            Brawler(windup: 0.4f, recovery: 0f, cooldown: 0f);

            Bout bout = Fight("monster.brawler");

            bout.Advance(0.05f, 5);     // 0.25s of a 0.4s windup

            Assert.That(bout.Swings, Is.Zero, "fixture");

            // The target leaves: no candidates at all.
            bout.Ai.Tick(0.05f, System.Array.Empty<ICombatant>());

            Assert.That(bout.Ai.State, Is.Not.EqualTo(MonsterAiState.Attack));

            bout.Advance(0.05f, 5);     // back in reach, 0.25s again

            Assert.That(bout.Swings, Is.Zero,
                "it finished a windup it had already abandoned");
        }

        [Test]
        public void TheSlimeAuthorsAReadableRhythm()
        {
            MonsterDefinition slime = Authored();

            // The anticipation is long, and deliberately so: it is the clip's own time to
            // contact, because the animation starts when the monster commits and the damage
            // has to land on the frame the player sees it arrive. Bounded at both ends --
            // too short and there is nothing to react to, too long and it stops reading as
            // one action. The exact binding to the animation is held by
            // SlimeJumpAttackTests; this only keeps it inside the range that is playable.
            Assert.That(slime.AttackWindupSeconds, Is.GreaterThan(0.3f).And.LessThan(1.1f),
                "anticipation outside the range a player can read and react to");
            Assert.That(slime.AttackRecoverySeconds, Is.GreaterThan(0.15f).And.LessThan(0.8f));
            Assert.That(slime.AttackCooldownSeconds, Is.GreaterThan(1f),
                "a beginner monster should not be able to swing more than once a second");
        }

        // ---- a nest spreads out -------------------------------------------------------------

        [Test]
        public void MonstersFromOneNestDoNotAllAppearOnTheSameSpot()
        {
            // Radius has been authored, stored and carried over the wire since Phase 10 and
            // nothing read it, so a camp of six stood inside one another -- and a monster
            // respawning after a kill appeared inside its neighbours, which from outside is
            // indistinguishable from a respawn that never happened.
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(10f, 0f, -4f), radius: 6f, maxAlive: 8,
                respawnDelaySeconds: 5f, map: new DefinitionId(HomeMap));

            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            var placed = new System.Collections.Generic.List<CombatPosition>();

            for (var i = 0; i < 8; i++)
            {
                MonsterRuntimeState spawned = spawner.TrySpawn(Monsters, Enemies);

                Assert.That(spawned, Is.Not.Null, spawner.LastRejection.ToString());

                placed.Add(spawned.Position);
            }

            var identical = 0;

            for (var i = 0; i < placed.Count; i++)
            {
                for (var j = i + 1; j < placed.Count; j++)
                {
                    if (placed[i].SqrDistanceTo(placed[j]) < 0.01f) identical++;
                }
            }

            Assert.That(identical, Is.Zero, "monsters spawned on top of each other");

            foreach (CombatPosition where in placed)
            {
                float dx = where.X - 10f;
                float dz = where.Z + 4f;

                Assert.That(Mathf.Sqrt(dx * dx + dz * dz), Is.LessThanOrEqualTo(6.01f),
                    "spawned outside its own nest at " + where);
            }
        }

        [Test]
        public void ANestWithNoRadiusStillPutsThemExactlyOnThePoint()
        {
            // Content that says "they stand here" must keep meaning it -- the boss nest does.
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(3f, 0f, 7f), radius: 0f, maxAlive: 3,
                respawnDelaySeconds: 0f, map: new DefinitionId(HomeMap));

            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            for (var i = 0; i < 3; i++)
            {
                MonsterRuntimeState spawned = spawner.TrySpawn(Monsters, Enemies);

                Assert.That(spawned.Position, Is.EqualTo(new CombatPosition(3f, 0f, 7f)));
            }
        }

        [Test]
        public void TheSameNestLaysOutTheSameWayTwice()
        {
            // Deterministic, so a camp is reproducible and a test can say where a monster is.
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt),
                new CombatPosition(-20f, 0f, 5f), radius: 4f, maxAlive: 5,
                respawnDelaySeconds: 0f, map: new DefinitionId(HomeMap));

            var first = new MonsterSpawnService(point, new DefinitionId(MaxHp));
            var second = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            for (var i = 0; i < 5; i++)
            {
                Assert.That(second.TrySpawn(Monsters, Enemies).Position,
                    Is.EqualTo(first.TrySpawn(Monsters, Enemies).Position));
            }
        }

        // ---- presentation carries no authority ------------------------------------------------

        [Test]
        public void TheNameplateIsPresentationAndDecidesNothing()
        {
            // Which monster wears a name is a fact about one player's client. It must not be
            // replicated, and nothing in the world may be able to read it.
            string presenter = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldMonsterPresenter.cs");

            Assert.That(presenter, Does.Not.Contain("ServerPublish"),
                "the presentation writes to the network");

            string[] server = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Server",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in server)
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("WorldMonsterPresenter"),
                    file.Replace('\\', '/') + " reaches into the client's presentation");
                Assert.That(source, Does.Not.Contain("Nameplate"),
                    file.Replace('\\', '/') + " has an opinion about a nameplate");
            }
        }

        [Test]
        public void TheHopIsAuthoredAlongsideTheModelRatherThanInCode()
        {
            var visuals = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(
                "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset");

            Assert.That(visuals, Is.Not.Null);
            Assert.That(visuals.HopMetresFor(new DefinitionId(Slime)), Is.GreaterThan(0f),
                "with no authored hop the bounce cannot be matched to the walking speed, "
                + "and a strolling slime slides");
        }

        [Test]
        public void APlayersSwingIsDrawnFromTheServersReportRatherThanTheClicking()
        {
            // The whole chain already existed -- the server publishes an accepted attack to
            // every observer -- and nothing subscribed, so nobody's character ever moved.
            // Asserted on the wiring because the alternative, animating on the click, would
            // draw swings the server refused.
            string presenter = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs");

            Assert.That(presenter, Does.Contain("AttackPerformed += OnAttackPerformed"),
                "nothing listens for the attack the server published");
            Assert.That(presenter, Does.Contain("SetTrigger(AttackHash)"),
                "the swing is never drawn");

            string input = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldCombatInput.cs");

            Assert.That(input, Does.Not.Contain("SetTrigger"),
                "the input animates a swing the server has not accepted yet");
        }
    }
}
