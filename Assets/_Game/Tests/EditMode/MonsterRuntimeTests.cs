using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Monsters: runtime state, the combat adapter, spawning and behaviour.
    /// </summary>
    /// <remarks>
    /// The expensive failure here is a monster paying out twice -- two killing blows in one
    /// frame handing out loot, experience and quest credit for each. That guard lives in
    /// one place, <c>TryClaimDefeat</c>, and several tests below exist only to hold it.
    ///
    /// Every range, cooldown and stat is a FIXTURE on a definition. Nothing in the AI or the
    /// spawner knows any monster.
    /// </remarks>
    internal sealed class MonsterRuntimeTests : MonsterTestBase
    {
        // ---- runtime state -------------------------------------------------------------

        [Test]
        public void A_spawned_monster_starts_whole_at_its_spawn_point()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            Assert.That(monster.CurrentHealth, Is.EqualTo(100));
            Assert.That(monster.MaxHealth, Is.EqualTo(100));
            Assert.That(monster.IsAlive, Is.True);
            Assert.That(monster.HasTarget, Is.False);
            Assert.That(monster.IsDefeatClaimed, Is.False);
            Assert.That(monster.Level, Is.EqualTo(5), "the authored level");
            Assert.That(monster.DefinitionId, Is.EqualTo(new DefinitionId(Grunt)));
        }

        [Test]
        public void Damage_clamps_at_zero_and_healing_clamps_at_the_ceiling()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            monster.ApplyHealthDelta(-30);
            Assert.That(monster.CurrentHealth, Is.EqualTo(70));

            monster.ApplyHealthDelta(9999);
            Assert.That(monster.CurrentHealth, Is.EqualTo(100), "never above the ceiling");

            monster.ApplyHealthDelta(-9999);
            Assert.That(monster.CurrentHealth, Is.EqualTo(0), "never below zero");
            Assert.That(monster.IsAlive, Is.False, "aliveness is derived, never stored");
        }

        [Test]
        public void A_change_of_nothing_does_not_look_like_a_state_change()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            Revision before = monster.Revision;
            monster.ApplyHealthDelta(0);
            monster.ApplyHealthDelta(500);   // already full

            Assert.That(monster.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_defeat_can_be_claimed_exactly_once()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            Assert.That(monster.TryClaimDefeat(), Is.False, "it is still alive");

            monster.ApplyHealthDelta(-100);

            Assert.That(monster.TryClaimDefeat(), Is.True);
            Assert.That(monster.IsDefeatClaimed, Is.True);

            for (int i = 0; i < 10; i++)
            {
                Assert.That(monster.TryClaimDefeat(), Is.False,
                    "two killing blows in one frame must not pay out twice");
            }
        }

        [Test]
        public void Respawning_restores_it_and_lets_it_be_defeated_again()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            monster.Position = new CombatPosition(50f, 0f, 50f);
            monster.SetTarget(InstanceId.New());
            monster.ApplyHealthDelta(-100);
            monster.TryClaimDefeat();

            monster.Respawn();

            Assert.That(monster.CurrentHealth, Is.EqualTo(100));
            Assert.That(monster.IsAlive, Is.True);
            Assert.That(monster.IsDefeatClaimed, Is.False, "a new life may be taken again");
            Assert.That(monster.HasTarget, Is.False);
            Assert.That(monster.Position, Is.EqualTo(monster.SpawnPosition));
        }

        [Test]
        public void Identity_and_the_authored_definition_never_change()
        {
            MonsterRuntimeState monster = Spawn(Grunt);

            InstanceId id = monster.InstanceId;
            monster.ApplyHealthDelta(-50);
            monster.Respawn();

            Assert.That(monster.InstanceId, Is.EqualTo(id));
            Assert.That(monster.DefinitionId, Is.EqualTo(new DefinitionId(Grunt)));
        }

        // ---- combat adapter ------------------------------------------------------------

        [Test]
        public void The_adapter_stores_nothing_and_forwards_everything()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var combatant = new MonsterCombatant(monster);

            Assert.That(combatant.CombatantId, Is.EqualTo(monster.InstanceId),
                "a second identity would break every comparison combat makes");
            Assert.That(combatant.CurrentHealth, Is.EqualTo(100));
            Assert.That(combatant.MaxHealth, Is.EqualTo(100));

            combatant.ApplyHealthDelta(-40);

            Assert.That(monster.CurrentHealth, Is.EqualTo(60), "it went into the runtime state");
            Assert.That(combatant.CurrentHealth, Is.EqualTo(60));
            Assert.That(combatant.IsAlive(), Is.True);
        }

        [Test]
        public void Combat_stats_come_from_the_authored_definition()
        {
            var combatant = new MonsterCombatant(Spawn(Grunt));

            int attack;
            Assert.That(combatant.TryGetCombatStat(new DefinitionId(Atk), out attack), Is.True);
            Assert.That(attack, Is.EqualTo(20), "the figure a designer typed");

            int missing;
            Assert.That(combatant.TryGetCombatStat(new DefinitionId("stat.luck"), out missing),
                Is.False, "an unauthored stat is absent, which is not a computed zero");
            Assert.That(missing, Is.EqualTo(0));
        }

        [Test]
        public void A_monster_and_a_character_relate_through_the_existing_team_rule()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var monsterCombatant = new MonsterCombatant(monster);
            var player = Player(0f, 0f, 0f);

            Assert.That(CombatTeams.Relate(monsterCombatant, player),
                Is.EqualTo(CombatRelationship.Hostile));
            Assert.That(CombatTeams.Relate(monsterCombatant, monsterCombatant),
                Is.EqualTo(CombatRelationship.Self));
        }

        // ---- spawning ------------------------------------------------------------------

        [Test]
        public void A_spawner_respects_the_authored_population_limit()
        {
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt), CombatPosition.Zero,
                maxAlive: 2);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Not.Null);
            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Not.Null);
            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Null);
            Assert.That(spawner.LastRejection, Is.EqualTo(SpawnRejection.AtCapacity));
            Assert.That(spawner.AliveCount, Is.EqualTo(2));
        }

        [Test]
        public void A_monster_not_authored_for_the_map_is_refused()
        {
            var allowed = new MonsterSpawnPoint(new DefinitionId(Bound), CombatPosition.Zero,
                map: new DefinitionId(HomeMap));
            var elsewhere = new MonsterSpawnPoint(new DefinitionId(Bound), CombatPosition.Zero,
                map: new DefinitionId(OtherMap));

            var stat = new DefinitionId(MaxHp);

            Assert.That(new MonsterSpawnService(allowed, stat).TrySpawn(Monsters, Enemies),
                Is.Not.Null);

            var refused = new MonsterSpawnService(elsewhere, stat);
            Assert.That(refused.TrySpawn(Monsters, Enemies), Is.Null);
            Assert.That(refused.LastRejection, Is.EqualTo(SpawnRejection.MapNotAllowed));
        }

        [Test]
        public void A_monster_with_no_authored_health_is_refused_rather_than_spawned_dead()
        {
            AddMonster(Hollow, level: 1, stats: new StatValue[0]);

            var point = new MonsterSpawnPoint(new DefinitionId(Hollow), CombatPosition.Zero);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Null);
            Assert.That(spawner.LastRejection, Is.EqualTo(SpawnRejection.InvalidHealth),
                "it would spawn dead and pay out its loot immediately");
        }

        [Test]
        public void An_unresolvable_monster_and_a_missing_registry_are_refused()
        {
            var ghost = new MonsterSpawnPoint(new DefinitionId("monster.deleted"),
                CombatPosition.Zero);
            var spawner = new MonsterSpawnService(ghost, new DefinitionId(MaxHp));

            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Null);
            Assert.That(spawner.LastRejection, Is.EqualTo(SpawnRejection.UnknownMonster));

            var good = new MonsterSpawnService(
                new MonsterSpawnPoint(new DefinitionId(Grunt), CombatPosition.Zero),
                new DefinitionId(MaxHp));

            Assert.That(good.TrySpawn(null, Enemies), Is.Null);
            Assert.That(good.LastRejection, Is.EqualTo(SpawnRejection.MissingContext));
        }

        [Test]
        public void A_corpse_is_only_retired_once_its_defeat_was_paid_out()
        {
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt), CombatPosition.Zero,
                maxAlive: 1, respawnDelaySeconds: 5f);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);
            monster.ApplyHealthDelta(-100);

            Assert.That(spawner.RetireDefeated(), Is.EqualTo(0),
                "sweeping a corpse before its loot was handed out would lose the reward");

            monster.TryClaimDefeat();

            Assert.That(spawner.RetireDefeated(), Is.EqualTo(1));
            Assert.That(spawner.AliveCount, Is.EqualTo(0));
            Assert.That(spawner.PendingRespawnCount, Is.EqualTo(1));
        }

        [Test]
        public void Respawn_waits_out_the_authored_delay()
        {
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt), CombatPosition.Zero,
                maxAlive: 1, respawnDelaySeconds: 5f);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);
            monster.ApplyHealthDelta(-100);
            monster.TryClaimDefeat();
            spawner.RetireDefeated();

            Assert.That(spawner.Tick(2f), Is.EqualTo(0));
            Assert.That(spawner.Tick(2f), Is.EqualTo(0));
            Assert.That(spawner.Tick(2f), Is.EqualTo(1), "five seconds have now passed");
            Assert.That(spawner.PendingRespawnCount, Is.EqualTo(0));

            Assert.That(spawner.TrySpawn(Monsters, Enemies), Is.Not.Null,
                "and the point has room again");
        }

        [Test]
        public void A_paused_game_respawns_nothing()
        {
            var point = new MonsterSpawnPoint(new DefinitionId(Grunt), CombatPosition.Zero,
                respawnDelaySeconds: 1f);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);
            monster.ApplyHealthDelta(-100);
            monster.TryClaimDefeat();
            spawner.RetireDefeated();

            Assert.That(spawner.Tick(0f), Is.EqualTo(0));
            Assert.That(spawner.Tick(-5f), Is.EqualTo(0));
            Assert.That(spawner.PendingRespawnCount, Is.EqualTo(1));
        }

        [Test]
        public void The_points_own_delay_overrides_the_monsters()
        {
            // The monster authors 10; the point says 1.
            var point = new MonsterSpawnPoint(new DefinitionId(Slow), CombatPosition.Zero,
                respawnDelaySeconds: 1f);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);
            monster.ApplyHealthDelta(-monster.MaxHealth);
            monster.TryClaimDefeat();
            spawner.RetireDefeated();

            Assert.That(spawner.Tick(1f), Is.EqualTo(1));
        }

        [Test]
        public void A_point_with_no_delay_falls_back_to_the_monsters_own()
        {
            var point = new MonsterSpawnPoint(new DefinitionId(Slow), CombatPosition.Zero);
            var spawner = new MonsterSpawnService(point, new DefinitionId(MaxHp));

            MonsterRuntimeState monster = spawner.TrySpawn(Monsters, Enemies);
            monster.ApplyHealthDelta(-monster.MaxHealth);
            monster.TryClaimDefeat();
            spawner.RetireDefeated();

            Assert.That(spawner.Tick(5f), Is.EqualTo(0), "the monster authors ten seconds");
            Assert.That(spawner.Tick(5f), Is.EqualTo(1));
        }

        // ---- behaviour -----------------------------------------------------------------

        [Test]
        public void An_aggressive_monster_notices_reacts_chases_and_strikes()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            // Detection 10, attack range 2. Start well inside detection, outside reach.
            var player = Player(6f, 0f, 0f);
            var candidates = new List<ICombatant> { player };

            ai.Tick(0.1f, candidates);
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Detect), "a beat of reaction first");
            Assert.That(monster.TargetId, Is.EqualTo(player.CombatantId));

            ai.Tick(MonsterAiController.DetectDurationSeconds, candidates);
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Chase));

            monster.Position = new CombatPosition(5f, 0f, 0f);   // now within reach
            ai.Tick(0.1f, candidates);

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Attack));
            Assert.That(ai.WantsToAttack, Is.True, "an intention, not an attack");
        }

        [Test]
        public void Wanting_to_attack_respects_the_authored_cooldown()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            monster.Position = new CombatPosition(5f, 0f, 0f);

            var ai = new MonsterAiController(monster);
            var candidates = new List<ICombatant>
            {
                Player(6f, 0f, 0f)
            };

            ai.Tick(0.1f, candidates);
            Assert.That(ai.WantsToAttack, Is.True);

            ai.Tick(0.1f, candidates);
            Assert.That(ai.WantsToAttack, Is.False, "the cooldown is two seconds");

            ai.Tick(2f, candidates);
            Assert.That(ai.WantsToAttack, Is.True);
        }

        [Test]
        public void A_passive_monster_never_picks_a_fight()
        {
            MonsterRuntimeState monster = Spawn(Docile);
            var ai = new MonsterAiController(monster);

            var candidates = new List<ICombatant>
            {
                Player(1f, 0f, 0f)
            };

            for (int i = 0; i < 10; i++) ai.Tick(0.5f, candidates);

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Idle));
            Assert.That(ai.WantsToAttack, Is.False);
            Assert.That(monster.HasTarget, Is.False);
        }

        [Test]
        public void Something_out_of_range_is_never_noticed()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            var candidates = new List<ICombatant>
            {
                Player(100f, 0f, 0f)
            };

            ai.Tick(0.5f, candidates);

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Idle));
            Assert.That(monster.HasTarget, Is.False);
        }

        [Test]
        public void A_friendly_is_not_a_target()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            var candidates = new List<ICombatant>
            {
                Ally(1f, 0f, 0f)
            };

            ai.Tick(0.5f, candidates);

            Assert.That(monster.HasTarget, Is.False,
                "who is an enemy has one definition, and it is not restated here");
        }

        [Test]
        public void A_target_that_dies_is_dropped_and_the_monster_goes_home()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            var player = Player(3f, 0f, 0f);
            var candidates = new List<ICombatant> { player };

            ai.Tick(0.5f, candidates);
            Assert.That(monster.HasTarget, Is.True);

            player.ApplyHealthDelta(-1000);
            ai.Tick(0.5f, candidates);

            Assert.That(monster.HasTarget, Is.False);
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Return));
        }

        [Test]
        public void A_target_that_leaves_the_world_is_dropped()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            var player = Player(3f, 0f, 0f);

            ai.Tick(0.5f, new List<ICombatant> { player });
            Assert.That(monster.HasTarget, Is.True);

            // Logged out: not on the candidate list at all.
            ai.Tick(0.5f, new List<ICombatant>());

            Assert.That(monster.HasTarget, Is.False);
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Return));
        }

        [Test]
        public void A_monster_dragged_past_its_leash_gives_up_and_returns()
        {
            MonsterRuntimeState monster = Spawn(Grunt);   // leash 15
            var ai = new MonsterAiController(monster);

            var player = Player(3f, 0f, 0f);
            var candidates = new List<ICombatant> { player };

            ai.Tick(0.5f, candidates);
            Assert.That(monster.HasTarget, Is.True);

            // Walked far from home while still next to the target.
            monster.Position = new CombatPosition(40f, 0f, 0f);
            player.Position = new CombatPosition(41f, 0f, 0f);

            ai.Tick(0.5f, candidates);

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Return),
                "a player must not be able to walk a monster across a map");
            Assert.That(monster.HasTarget, Is.False);
        }

        [Test]
        public void Arriving_home_with_nothing_to_fight_returns_it_to_idle()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            ai.ForceReturn();
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Return));

            ai.Tick(0.5f, new List<ICombatant>());

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Idle), "it is already at its spawn");
        }

        [Test]
        public void Death_outranks_everything_and_stops_the_monster()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            monster.Position = new CombatPosition(5f, 0f, 0f);

            var ai = new MonsterAiController(monster);
            var candidates = new List<ICombatant>
            {
                Player(6f, 0f, 0f)
            };

            ai.Tick(0.1f, candidates);
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Attack));

            monster.ApplyHealthDelta(-100);
            ai.Tick(0.1f, candidates);

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Dead));
            Assert.That(ai.WantsToAttack, Is.False);
            Assert.That(monster.HasTarget, Is.False);
        }

        [Test]
        public void A_respawned_monster_starts_behaving_again()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            monster.ApplyHealthDelta(-100);
            ai.Tick(0.1f, new List<ICombatant>());
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Dead));

            monster.Respawn();
            ai.Tick(0.1f, new List<ICombatant>());

            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Idle));
        }

        [Test]
        public void It_holds_the_target_it_has_rather_than_re_picking_the_nearest()
        {
            MonsterRuntimeState monster = Spawn(Grunt);
            var ai = new MonsterAiController(monster);

            var first = Player(5f, 0f, 0f);

            ai.Tick(0.5f, new List<ICombatant> { first });
            Assert.That(monster.TargetId, Is.EqualTo(first.CombatantId));

            // Someone closer walks past.
            var closer = Player(1f, 0f, 0f);

            ai.Tick(0.5f, new List<ICombatant> { first, closer });

            Assert.That(monster.TargetId, Is.EqualTo(first.CombatantId),
                "swapping whenever two players cross would read as broken");
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_the_ai_or_the_spawner()
        {
            string[] sources =
            {
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/MonsterAiController.cs"),
                System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/MonsterSpawnService.cs")
            };

            string[] mustNotAppear = { Grunt, Docile, Bound, Slow, MaxHp, Atk, "Goblin", "Orc", "Boss" };

            foreach (string source in sources)
            {
                foreach (string forbidden in mustNotAppear)
                {
                    Assert.That(source, Does.Not.Contain(forbidden),
                        "a monster system names '" + forbidden + "'; behaviour must come from data");
                }
            }
        }
    }
}
