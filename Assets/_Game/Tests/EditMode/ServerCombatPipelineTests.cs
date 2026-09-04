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
    /// A player's attack killing a spawned monster, and everything that follows from it.
    /// </summary>
    /// <remarks>
    /// <b>This is the path that did not exist.</b> Every part of it did: the command
    /// boundary, the damage formula, the health write, the defeat claim, the experience, the
    /// loot, the retirement and the respawn all had their own suites and all worked. Nothing
    /// called them in order, so no monster had ever actually died from a player's swing.
    /// These tests drive the whole line and check the joins.
    ///
    /// <b>The client's only inputs are a target and a sequence.</b> Several tests below
    /// exist to hold that: there is no damage in the command, no death, no reward, and no
    /// way to name somebody else's character.
    /// </remarks>
    [TestFixture]
    internal sealed class ServerCombatPipelineTests : MonsterTestBase
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

        private const string Target = "monster.target";
        private const string Tough = "monster.tough";
        private const string Shy = "monster.shy";
        private const string Guard = "monster.guard";
        private const string Table = "drop.target";

        private const float Reach = 3f;
        private const int Connection = 7;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterLootRegistry _loot;
        private MonsterRewardAuthority _rewards;
        private CombatCommandAuthority _commands;
        private ServerCombatPipeline _pipeline;
        private CharacterProgressionDefinition _curve;
        private DefinitionRegistry<SkillDefinition> _skills;
        private readonly List<Object> _local = new List<Object>();

        private long _sequence;

        [SetUp]
        public void SetUpPipeline()
        {
            AddDropTable(Table, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
            });

            // Twenty health, five defence: a level-five attacker with twenty attack does
            // fifteen a swing, so two swings kill and the arithmetic is checkable by hand.
            AddMonster(Target, level: 3, experience: 40, lootTable: Table,
                stats: Body(20, 5));

            AddMonster(Tough, level: 3, experience: 40, stats: Body(500, 5));

            AddMonster(Shy, level: 3, aggression: MonsterAggressionType.Passive,
                detection: 20f, stats: Body(500, 5));

            AddMonster(Guard, level: 3, aggression: MonsterAggressionType.Defensive,
                detection: 20f, attackRange: 2f, leash: 100f, stats: Body(500, 5));

            _curve = Curve();
            _skills = new DefinitionRegistry<SkillDefinition>();
            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));
            spawns.Register(PlayerSpawn("spawn.other", OtherMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, 20);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);

            _rewards = new MonsterRewardAuthority(_runtime, _players, _curve, _loot, Items,
                DropTables);

            _commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            _pipeline = new ServerCombatPipeline(_commands, _runtime, _rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1,
                    Reach));

            _sequence = 0;
        }

        [TearDown]
        public void TearDownPipeline()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        /// <summary>Sets a private serialized field. MonsterTestBase has one; this is its own
        /// because the skill fixture below authors a different definition type.</summary>
        private static new void SetPrivate(Object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);

            Assert.That(info, Is.Not.Null, "no field '" + field + "'");

            info.SetValue(target, value);
        }

        private static StatValue[] Body(int health, int defense)
        {
            return new[]
            {
                new StatValue(new DefinitionId(MaxHp), health),
                new StatValue(new DefinitionId(Atk), 10f),
                new StatValue(new DefinitionId(Def), defense),
            };
        }

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.combat-test\"},\"_minLevel\":1,"
                + "\"_maxLevel\":20,\"_experienceToNextLevel\":[100,100,100,100,100,100,100,"
                + "100,100,100,100,100,100,100,100,100,100,100,100]}", definition);

            _local.Add(definition);

            return definition;
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

        /// <summary>A player in the world, with stats that make the arithmetic obvious.</summary>
        private LivingCharacter AddPlayer(string character, int connection = Connection,
            string map = HomeMap, float x = 0f, PersistedSkill[] learned = null)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 20),
                    new PersistedStat(new DefinitionId(Def), 5),
                },
                null, learned, 1);

            WorldSpawnResult result = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            result.Character.Location.Position = new CombatPosition(x, 0f, 0f);
            result.Character.Combatant.Position = new CombatPosition(x, 0f, 0f);

            return result.Character;
        }

        /// <summary>A monster standing where a player can reach it.</summary>
        private LivingMonster Spawn(string monster, string map = HomeMap, float x = 1f)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(x, 0f, 0f), 0f, 1, 0f, new DefinitionId(map)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.State.DefinitionId.Value == monster && living.IsAlive)
                {
                    return living;
                }
            }

            Assert.Fail("no living '" + monster + "'");

            return null;
        }

        /// <summary>What a client actually sends: a target and a sequence.</summary>
        private CombatCommand Attack(LivingMonster monster, CharacterId claimed = default)
        {
            return new CombatCommand(claimed, monster.Instance, default, 0, ++_sequence);
        }

        // ---- 1-10: a real attack ---------------------------------------------------------

        [Test]
        public void AValidAttackDamagesTheMonster()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            ServerCombatResult result = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Damage, Is.EqualTo(15), "attack 20 less defence 5");
            Assert.That(result.TargetHealthBefore, Is.EqualTo(20));
            Assert.That(result.TargetHealthAfter, Is.EqualTo(5));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(5),
                "the authoritative monster actually lost health");
        }

        [Test]
        public void TheDamageIsTheServersAndTheCommandCarriesNone()
        {
            // There is no damage, no health, no death and no reward field a client could
            // fill in. The whole command is an attacker claim, a target, a skill, a rank
            // and a sequence.
            foreach (System.Reflection.PropertyInfo property in
                typeof(CombatCommand).GetProperties())
            {
                string name = property.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("damage"), property.Name);
                Assert.That(name, Does.Not.Contain("health"), property.Name);
                Assert.That(name, Does.Not.Contain("hp"), property.Name);
                Assert.That(name, Does.Not.Contain("death"), property.Name);
                Assert.That(name, Does.Not.Contain("defeat"), property.Name);
                Assert.That(name, Does.Not.Contain("experience"), property.Name);
                Assert.That(name, Does.Not.Contain("loot"), property.Name);
                Assert.That(name, Does.Not.Contain("reward"), property.Name);
                Assert.That(name, Does.Not.Contain("level"), property.Name);
            }
        }

        [Test]
        public void ADeadMonsterCannotBeAttackedAgain()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));
            _pipeline.Execute(Connection, Attack(monster));

            Assert.That(monster.IsAlive, Is.False, "precondition: it died");

            int healthAtDeath = monster.State.CurrentHealth;

            ServerCombatResult third = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(third.IsAccepted, Is.False);
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(healthAtDeath),
                "a corpse takes no further damage");
        }

        [Test]
        public void AnUnknownTargetIsRefused()
        {
            AddPlayer("char-a");

            ServerCombatResult result = _pipeline.Execute(Connection,
                new CombatCommand(default, new InstanceId("no-such-monster"), default, 0, 1));

            Assert.That(result.Rejection,
                Is.EqualTo(CombatCommandRejection.UnknownTarget));
        }

        [Test]
        public void AMalformedCommandIsRefused()
        {
            AddPlayer("char-a");

            ServerCombatResult result = _pipeline.Execute(Connection,
                new CombatCommand(default, default, default, 0, 1));

            Assert.That(result.Rejection, Is.EqualTo(CombatCommandRejection.Malformed));
        }

        [Test]
        public void AMonsterOnAnotherMapIsRefused()
        {
            AddPlayer("char-a", map: HomeMap);
            LivingMonster elsewhere = Spawn(Target, OtherMap);

            ServerCombatResult result = _pipeline.Execute(Connection, Attack(elsewhere));

            Assert.That(result.Rejection, Is.EqualTo(CombatCommandRejection.DifferentMap),
                "range alone would let a player hit through a loading screen");
            Assert.That(elsewhere.State.CurrentHealth, Is.EqualTo(20));
        }

        [Test]
        public void AMonsterOutOfReachIsRefused()
        {
            AddPlayer("char-a", x: 0f);
            LivingMonster far = Spawn(Target, HomeMap, x: Reach + 5f);

            ServerCombatResult result = _pipeline.Execute(Connection, Attack(far));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.AttackRejection, Is.EqualTo(AttackRejection.OutOfRange));
            Assert.That(far.State.CurrentHealth, Is.EqualTo(20), "untouched");
        }

        [Test]
        public void RangeIsMeasuredFromTheServersPositionAndNotTheClients()
        {
            LivingCharacter attacker = AddPlayer("char-a", x: 0f);
            LivingMonster far = Spawn(Target, HomeMap, x: Reach + 5f);

            Assert.That(_pipeline.Execute(Connection, Attack(far)).AttackRejection,
                Is.EqualTo(AttackRejection.OutOfRange), "precondition");

            // The only way to be in range is for the server's own position to change.
            attacker.Combatant.Position = new CombatPosition(Reach + 4f, 0f, 0f);

            Assert.That(_pipeline.Execute(Connection, Attack(far)).IsAccepted, Is.True);
        }

        [Test]
        public void AConnectionWithNoCharacterIsRefused()
        {
            LivingMonster monster = Spawn(Target);

            ServerCombatResult result = _pipeline.Execute(999, Attack(monster));

            Assert.That(result.Rejection, Is.EqualTo(CombatCommandRejection.NoCharacter));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(20));
        }

        [Test]
        public void AStaleConnectionIsRefusedBeforeAnythingIsResolved()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            var stale = new CombatCommandAuthority(_players, _ => false, _runtime);
            var pipeline = new ServerCombatPipeline(stale, _runtime, _rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1,
                    Reach));

            Assert.That(pipeline.Execute(Connection, Attack(monster)).Rejection,
                Is.EqualTo(CombatCommandRejection.StaleConnection));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(20));
        }

        [Test]
        public void AReplayedCommandIsRefusedAndDamagesNothing()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Tough);

            var command = new CombatCommand(default, monster.Instance, default, 0, 5);

            Assert.That(_pipeline.Execute(Connection, command).IsAccepted, Is.True);

            int afterFirst = monster.State.CurrentHealth;

            ServerCombatResult replay = _pipeline.Execute(Connection, command);

            Assert.That(replay.Rejection, Is.EqualTo(CombatCommandRejection.OutOfOrder));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(afterFirst),
                "the same swing must not land twice");
        }

        [Test]
        public void ACommandNamingSomebodyElsesCharacterIsRefused()
        {
            AddPlayer("char-a");
            AddPlayer("char-b", connection: 8);

            LivingMonster monster = Spawn(Tough);

            ServerCombatResult result = _pipeline.Execute(Connection,
                Attack(monster, new CharacterId("char-b")));

            Assert.That(result.Rejection,
                Is.EqualTo(CombatCommandRejection.NotYourCharacter));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(500));
        }

        // ---- 11-15: death ------------------------------------------------------------------

        [Test]
        public void ALethalBlowKillsTheMonster()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));

            ServerCombatResult lethal = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(lethal.TargetDefeated, Is.True);
            Assert.That(monster.IsAlive, Is.False);
            Assert.That(lethal.TargetHealthAfter, Is.Zero);
        }

        [Test]
        public void DeathIsClaimedExactlyOnce()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));
            _pipeline.Execute(Connection, Attack(monster));

            Assert.That(monster.State.IsDefeatClaimed, Is.True);
            Assert.That(_rewards.GrantedCount, Is.EqualTo(1));

            // Every further attempt, however it arrives.
            for (int i = 0; i < 5; i++) _pipeline.Execute(Connection, Attack(monster));

            Assert.That(_rewards.GrantedCount, Is.EqualTo(1));
        }

        [Test]
        public void TheExistingLifecycleRetiresTheCorpseAndRespawnsIt()
        {
            AddPlayer("char-a");

            // A nest with a respawn delay, so the existing configuration is what brings it
            // back rather than anything in the pipeline.
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Target),
                new CombatPosition(1f, 0f, 0f), 0f, 1, 5f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();

            LivingMonster monster = _runtime.All()[0];

            _pipeline.Execute(Connection, Attack(monster));
            _pipeline.Execute(Connection, Attack(monster));

            Assert.That(monster.IsAlive, Is.False);
            Assert.That(_runtime.AliveCount, Is.EqualTo(1), "the corpse is still resolvable");

            MonsterTickResult tick = _runtime.Tick(1f);

            Assert.That(tick.Retired, Is.EqualTo(1), "retired by the existing lifecycle");
            Assert.That(_runtime.AliveCount, Is.Zero);

            MonsterTickResult later = _runtime.Tick(6f);

            Assert.That(later.Spawned, Is.EqualTo(1),
                "and the authored respawn delay brought it back");
        }

        [Test]
        public void NothingIsRespawnedBeforeTheDefeatIsClaimed()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));
            _pipeline.Execute(Connection, Attack(monster));

            // The claim happened inside the lethal blow, before any tick could retire it.
            Assert.That(monster.State.IsDefeatClaimed, Is.True);
            Assert.That(_runtime.AliveCount, Is.EqualTo(1),
                "a replacement before the reward is claimed would strand the reward");
        }

        // ---- 16-19: experience -------------------------------------------------------------

        [Test]
        public void ALethalBlowGrantsTheAuthoredExperience()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));

            ServerCombatResult lethal = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(lethal.ExperienceGranted, Is.EqualTo(40));
            Assert.That(attacker.Domain.Progression.Experience, Is.EqualTo(40),
                "through the 17.14 authority, on the real character");
        }

        [Test]
        public void ExperienceIsGrantedOnceHoweverManyCommandsArrive()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            for (int i = 0; i < 8; i++) _pipeline.Execute(Connection, Attack(monster));

            Assert.That(attacker.Domain.Progression.Experience, Is.EqualTo(40));
        }

        [Test]
        public void EnoughKillsLevelTheCharacterUpThroughPhase05()
        {
            LivingCharacter attacker = AddPlayer("char-a");

            // Three kills at forty is a hundred and twenty: one level of a hundred, and
            // twenty into the next.
            for (int kill = 0; kill < 3; kill++)
            {
                LivingMonster monster = Spawn(Target, HomeMap, 1f);

                _pipeline.Execute(Connection, Attack(monster));
                _pipeline.Execute(Connection, Attack(monster));

                _runtime.Tick(0.1f);
            }

            Assert.That(attacker.Domain.Progression.Level, Is.EqualTo(6));
            Assert.That(attacker.Domain.Progression.Experience, Is.EqualTo(20));
        }

        [Test]
        public void ANonLethalBlowPaysNothing()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster monster = Spawn(Tough);

            ServerCombatResult result = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.ExperienceGranted, Is.Zero);
            Assert.That(attacker.Domain.Progression.Experience, Is.Zero);
        }

        // ---- 20-23: loot ---------------------------------------------------------------------

        [Test]
        public void ALethalBlowDropsThroughTheLootAuthority()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));

            ServerCombatResult lethal = _pipeline.Execute(Connection, Attack(monster));

            Assert.That(lethal.LootCount, Is.EqualTo(1));
            Assert.That(_loot.TryGet(lethal.LootPile, out LootObjectState pile), Is.True);
            Assert.That(pile.Contents[0].Item.Value, Is.EqualTo(Coin));
            Assert.That(pile.EligibleCharacter.Value, Is.EqualTo("char-a"),
                "owned by whoever the server says killed it");
        }

        [Test]
        public void LootIsGeneratedOnceHoweverManyCommandsArrive()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            for (int i = 0; i < 8; i++) _pipeline.Execute(Connection, Attack(monster));

            Assert.That(_loot.Count, Is.EqualTo(1));
        }

        [Test]
        public void PickupIsStillGovernedByTheLootAuthorityAndNotByCombat()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));

            ServerCombatResult lethal = _pipeline.Execute(Connection, Attack(monster));

            // Killing it does not put anything in a bag: the pile is in the world.
            Assert.That(attacker.Inventory.OccupiedSlots, Is.Zero);

            Assert.That(_loot.Pickup(lethal.LootPile, 0, new CharacterId("char-a"))
                .IsAccepted, Is.True);
            Assert.That(attacker.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(1));
        }

        // ---- 24-27: the AI decides for itself -------------------------------------------------

        [Test]
        public void ADefensiveMonsterRetaliatesAfterBeingHit()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster guard = Spawn(Guard);

            Assert.That(guard.State.HasTarget, Is.False, "precondition: unprovoked");

            _pipeline.Execute(Connection, Attack(guard));

            Assert.That(guard.State.TargetId, Is.EqualTo(attacker.Combatant.CombatantId),
                "the pipeline told it, and 17.13's AI decided");
        }

        [Test]
        public void APassiveMonsterStillDoesNotFightBack()
        {
            AddPlayer("char-a");
            LivingMonster shy = Spawn(Shy);

            _pipeline.Execute(Connection, Attack(shy));

            Assert.That(shy.State.HasTarget, Is.False,
                "combat must not force aggression the AI refuses");

            _runtime.Tick(0.5f);

            Assert.That(shy.Ai.WantsToAttack, Is.False);
        }

        [Test]
        public void AnAggressiveMonsterIsUnaffectedByTheNotification()
        {
            LivingCharacter attacker = AddPlayer("char-a");
            LivingMonster aggressive = Spawn(Grunt);

            _runtime.Tick(0.5f);

            Assert.That(aggressive.State.HasTarget, Is.True, "it acquired on sight");

            _pipeline.Execute(Connection, Attack(aggressive));

            Assert.That(aggressive.State.TargetId,
                Is.EqualTo(attacker.Combatant.CombatantId));
        }

        [Test]
        public void ADeadMonsterIsNotToldItWasAttacked()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            _pipeline.Execute(Connection, Attack(monster));
            _pipeline.Execute(Connection, Attack(monster));

            Assert.That(monster.State.HasTarget, Is.False,
                "a corpse has nothing to retaliate against");
        }

        // ---- timing --------------------------------------------------------------------------

        [Test]
        public void AttackTimingIsServerSideSoAskingFasterDoesNotSwingFaster()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Tough);

            var pipeline = new ServerCombatPipeline(_commands, _runtime, _rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1,
                    Reach),
                new AttackTiming(0.5f, 0.5f));

            Assert.That(pipeline.Execute(Connection, Attack(monster)).IsAccepted, Is.True);

            int afterFirst = monster.State.CurrentHealth;

            ServerCombatResult tooSoon = pipeline.Execute(Connection, Attack(monster));

            Assert.That(tooSoon.AttackRejection, Is.EqualTo(AttackRejection.NotReady));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(afterFirst));

            // The server's own clock is what releases it.
            pipeline.Tick(1.5f);

            Assert.That(pipeline.Execute(Connection, Attack(monster)).IsAccepted, Is.True);
        }

        [Test]
        public void ARefusedSwingDoesNotConsumeTheSequence()
        {
            AddPlayer("char-a", x: 0f);
            LivingMonster far = Spawn(Tough, HomeMap, x: Reach + 5f);

            var command = new CombatCommand(default, far.Instance, default, 0, 3);

            Assert.That(_pipeline.Execute(Connection, command).AttackRejection,
                Is.EqualTo(AttackRejection.OutOfRange));

            // Stepping closer and pressing the same button again must work.
            _players.TryGet(Connection, out LivingCharacter attacker);
            attacker.Combatant.Position = new CombatPosition(Reach + 4f, 0f, 0f);

            Assert.That(_pipeline.Execute(Connection, command).IsAccepted, Is.True);
        }

        [Test]
        public void ForgettingACharacterClearsItsTiming()
        {
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Tough);

            _pipeline.Execute(Connection, Attack(monster));

            Assert.That(_pipeline.TrackedCombatants, Is.EqualTo(1));
            Assert.That(_pipeline.Forget(new CharacterId("char-a")), Is.True);
            Assert.That(_pipeline.TrackedCombatants, Is.Zero);
        }

        // ---- 28-31: one supported skill ------------------------------------------------------

        private const string Strike = "skill.strike";
        private const string Blast = "skill.blast";

        /// <summary>An authored single-target damage skill, at one rank.</summary>
        private SkillDefinition AddSkill(string id, SkillTargetType targetType, int damage)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_maxLevel\":1,"
                + "\"_baseResourceCost\":0,\"_cooldownSeconds\":0,\"_castTimeSeconds\":0,"
                + "\"_range\":0,\"_category\":" + (int)SkillCategory.Active
                + ",\"_targetType\":" + (int)targetType
                + ",\"_resourceType\":" + (int)SkillResourceType.Mana + "}", definition);

            SetPrivate(definition, "_levels", new[]
            {
                new SkillLevelEntry(1, 1, 0f, 0f, new[]
                {
                    SkillEffect.Damage(damage, ElementType.Neutral),
                }),
            });

            _local.Add(definition);
            _skills.Register(definition);

            return definition;
        }

        /// <summary>A pipeline that can execute skills.</summary>
        private ServerCombatPipeline WithSkills()
        {
            return new ServerCombatPipeline(_commands, _runtime, _rewards,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1,
                    Reach),
                default, _skills,
                new SkillExecutionRules(new DefinitionId(Def), 1));
        }

        private CombatCommand Cast(string skill, LivingMonster monster)
        {
            return new CombatCommand(default, monster.Instance, new DefinitionId(skill), 1,
                ++_sequence);
        }

        [Test]
        public void ASupportedSingleTargetDamageSkillLands()
        {
            AddSkill(Strike, SkillTargetType.SingleEnemy, damage: 50);

            AddPlayer("char-a", learned: new[]
            {
                new PersistedSkill(new DefinitionId(Strike), 1),
            });

            LivingMonster monster = Spawn(Tough);

            ServerCombatResult result = WithSkills().Execute(Connection,
                Cast(Strike, monster));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Damage, Is.EqualTo(45), "fifty less the monster's defence");
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(455));
        }

        [Test]
        public void ALethalSkillPaysThroughTheSameAuthorityAsASwing()
        {
            AddSkill(Strike, SkillTargetType.SingleEnemy, damage: 500);

            LivingCharacter attacker = AddPlayer("char-a", learned: new[]
            {
                new PersistedSkill(new DefinitionId(Strike), 1),
            });

            LivingMonster monster = Spawn(Target);

            ServerCombatResult result = WithSkills().Execute(Connection,
                Cast(Strike, monster));

            Assert.That(result.TargetDefeated, Is.True);
            Assert.That(result.ExperienceGranted, Is.EqualTo(40));
            Assert.That(result.LootCount, Is.EqualTo(1));
            Assert.That(attacker.Domain.Progression.Experience, Is.EqualTo(40));
            Assert.That(_rewards.GrantedCount, Is.EqualTo(1),
                "one death, one claim, however it was killed");
        }

        [Test]
        public void ASkillTheCharacterHasNotLearnedIsRefused()
        {
            AddSkill(Strike, SkillTargetType.SingleEnemy, damage: 50);

            AddPlayer("char-a");

            LivingMonster monster = Spawn(Tough);

            ServerCombatResult result = WithSkills().Execute(Connection,
                Cast(Strike, monster));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.SkillRejection, Is.Not.EqualTo(SkillUseRejection.None));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(500), "untouched");
        }

        [Test]
        public void AnUnsupportedTargetTypeIsStillRefused()
        {
            // Area and party targeting have no runtime here, and the existing mapping
            // refuses them rather than guessing. 18.1 does not change that.
            AddSkill(Blast, SkillTargetType.AreaAroundSelf, damage: 50);

            AddPlayer("char-a", learned: new[]
            {
                new PersistedSkill(new DefinitionId(Blast), 1),
            });

            LivingMonster monster = Spawn(Tough);

            ServerCombatResult result = WithSkills().Execute(Connection,
                Cast(Blast, monster));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(500));
        }

        [Test]
        public void AServerWithNoSkillContentExecutesNoSkill()
        {
            AddPlayer("char-a");

            LivingMonster monster = Spawn(Tough);

            // The basic-attack pipeline has no skills registry at all.
            ServerCombatResult result = _pipeline.Execute(Connection,
                Cast(Strike, monster));

            Assert.That(result.Rejection, Is.EqualTo(CombatCommandRejection.Malformed));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(500));
        }

        [Test]
        public void ASkillCannotBeRepeatedWithTheSameSequence()
        {
            AddSkill(Strike, SkillTargetType.SingleEnemy, damage: 50);

            AddPlayer("char-a", learned: new[]
            {
                new PersistedSkill(new DefinitionId(Strike), 1),
            });

            LivingMonster monster = Spawn(Tough);

            ServerCombatPipeline pipeline = WithSkills();

            var command = new CombatCommand(default, monster.Instance,
                new DefinitionId(Strike), 1, 4);

            Assert.That(pipeline.Execute(Connection, command).IsAccepted, Is.True);

            int afterFirst = monster.State.CurrentHealth;

            Assert.That(pipeline.Execute(Connection, command).Rejection,
                Is.EqualTo(CombatCommandRejection.OutOfOrder));
            Assert.That(monster.State.CurrentHealth, Is.EqualTo(afterFirst));
        }

        [Test]
        public void AWorldWithNoRewardAuthorityLeavesTheCorpseUnclaimed()
        {
            // Worth stating plainly, because it is a composition hazard rather than a bug:
            // the reward authority owns the defeat claim, and Phase 10 refuses to retire a
            // corpse nobody claimed -- so a server wired without one kills monsters that
            // then never retire and never respawn. A world server must compose one even if
            // it pays nothing.
            AddPlayer("char-a");
            LivingMonster monster = Spawn(Target);

            var pipeline = new ServerCombatPipeline(_commands, _runtime, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1,
                    Reach));

            pipeline.Execute(Connection, Attack(monster));
            pipeline.Execute(Connection, Attack(monster));

            Assert.That(monster.IsAlive, Is.False, "it still died");
            Assert.That(monster.State.IsDefeatClaimed, Is.False);

            Assert.That(_runtime.Tick(1f).Retired, Is.Zero,
                "and it will lie there forever, which is why the authority is composed");
        }

        // ---- 32-36: authority --------------------------------------------------------------

        [Test]
        public void TheResultIsValuesAndNotServerObjects()
        {
            // A client may be shown what happened. It must not be handed anything it could
            // write through.
            foreach (System.Reflection.PropertyInfo property in
                typeof(ServerCombatResult).GetProperties())
            {
                System.Type type = property.PropertyType;

                Assert.That(type.IsValueType || type == typeof(string), Is.True,
                    property.Name + " exposes a reference a client could hold");
                Assert.That(property.CanWrite, Is.False, property.Name + " is settable");
            }
        }

        [Test]
        public void NoPipelineMethodAcceptsADamageOrRewardValue()
        {
            foreach (System.Reflection.MethodInfo method in
                typeof(ServerCombatPipeline).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    string name = parameter.Name.ToLowerInvariant();

                    Assert.That(name, Does.Not.Contain("damage"), method.Name);
                    Assert.That(name, Does.Not.Contain("experience"), method.Name);
                    Assert.That(name, Does.Not.Contain("loot"), method.Name);
                    Assert.That(name, Does.Not.Contain("health"), method.Name);
                }
            }
        }

        [Test]
        public void TheOnlyHealthWriterIsStillTheExistingExecutor()
        {
            // If a second place starts applying damage, the two will disagree about
            // minimum damage, defence or death the first time either is tuned.
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Server",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                Assert.That(System.IO.File.ReadAllText(file),
                    Does.Not.Contain("ApplyHealthDelta"),
                    file.Replace('\\', '/') + " writes health outside the combat executor");
            }
        }

        [Test]
        public void TheServerIsTheOnlyCallerOfTheRewardAuthority()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var callers = new List<string>();

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');

                if (normalized.Contains("/Server/MonsterRewardAuthority.cs")) continue;

                if (System.IO.File.ReadAllText(file).Contains(".Grant("))
                {
                    callers.Add(normalized);
                }
            }

            Assert.That(callers, Has.Count.EqualTo(1), string.Join(", ", callers));
            Assert.That(callers[0], Does.EndWith("/Server/ServerCombatPipeline.cs"),
                "exactly one production path may pay a kill out");
        }

        [Test]
        public void NoClientAssemblyCanReachTheCombatPipeline()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"),
                    file.Replace('\\', '/') + " reaches the server combat pipeline");
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"),
                    file.Replace('\\', '/') + " reaches the reward authority");
            }
        }
    }
}
