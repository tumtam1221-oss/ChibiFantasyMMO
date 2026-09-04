<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\World\MonsterSpawnRepository;

/**
 * Monster spawn and AI configuration, read from the database.
 *
 * The property under test throughout is that a bad row is *named*, not corrected. An
 * operator who typed 30 into a nest that holds 10 meant something; silently spawning
 * 10 hides the mistake until somebody notices the map is wrong.
 */
final class MonsterSpawnConfigurationTest extends BackendTestCase
{
    private MonsterSpawnRepository $repository;

    protected function setUp(): void
    {
        parent::setUp();

        $this->pdo->exec('DELETE FROM monster_spawn_point');
        $this->pdo->exec('DELETE FROM monster_ai_configuration');

        $this->repository = new MonsterSpawnRepository($this->pdo);
    }

    /**
     * Inserts a spawn row.
     *
     * The one invalid combination the read-side validation must catch --
     * initial_spawn_count above max_alive -- deliberately has no CHECK constraint, so it
     * inserts cleanly and is refused on read with a named reason. A cross-column CHECK
     * would refuse the row outright and give an operator a SQL error instead of a
     * sentence naming the field they got wrong.
     *
     * The genuinely impossible values -- a non-positive max_alive, a negative radius, an
     * unknown aggression -- are refused by the database itself, and the tests below assert
     * that by expecting a PDOException.
     */
    private function makeSpawn(array $overrides = []): string
    {
        $row = array_merge([
            'spawn_point_id'        => 'spawn-' . bin2hex(random_bytes(4)),
            'map_definition_id'     => 'map.field',
            'monster_definition_id' => 'monster.orc',
            'position_x'            => 10.5,
            'position_y'            => 0.0,
            'position_z'            => -4.25,
            'spawn_radius'          => 3.0,
            'initial_spawn_count'   => 2,
            'max_alive'             => 5,
            'respawn_seconds'       => 20.0,
            'enabled'               => 1,
            'spawn_group_id'        => null,
        ], $overrides);

        $statement = $this->pdo->prepare(
            'INSERT INTO monster_spawn_point
                (spawn_point_id, map_definition_id, monster_definition_id,
                 position_x, position_y, position_z, spawn_radius,
                 initial_spawn_count, max_alive, respawn_seconds, enabled,
                 spawn_group_id, created_at, updated_at)
             VALUES (:id, :map, :monster, :x, :y, :z, :radius, :initial, :max, :respawn,
                     :enabled, :grp, NOW(3), NOW(3))'
        );

        $statement->execute([
            ':id'      => $row['spawn_point_id'],
            ':map'     => $row['map_definition_id'],
            ':monster' => $row['monster_definition_id'],
            ':x'       => $row['position_x'],
            ':y'       => $row['position_y'],
            ':z'       => $row['position_z'],
            ':radius'  => $row['spawn_radius'],
            ':initial' => $row['initial_spawn_count'],
            ':max'     => $row['max_alive'],
            ':respawn' => $row['respawn_seconds'],
            ':enabled' => $row['enabled'],
            ':grp'     => $row['spawn_group_id'],
        ]);

        return $row['spawn_point_id'];
    }

    private function makeAi(array $overrides = []): string
    {
        $row = array_merge([
            'monster_definition_id' => 'monster.goblin',
            'aggression_type'       => MonsterSpawnRepository::AGGRESSION_DEFENSIVE,
            'detection_range'       => 12.0,
            'chase_range'           => 25.0,
            'attack_range'          => 2.0,
            'attack_cooldown'       => 1.5,
            'move_speed'            => 3.0,
            'enabled'               => 1,
        ], $overrides);

        $statement = $this->pdo->prepare(
            'INSERT INTO monster_ai_configuration
                (monster_definition_id, aggression_type, detection_range, chase_range,
                 attack_range, attack_cooldown, move_speed, enabled, created_at, updated_at)
             VALUES (:monster, :aggression, :detection, :chase, :attack, :cooldown,
                     :speed, :enabled, NOW(3), NOW(3))'
        );

        $statement->execute([
            ':monster'    => $row['monster_definition_id'],
            ':aggression' => $row['aggression_type'],
            ':detection'  => $row['detection_range'],
            ':chase'      => $row['chase_range'],
            ':attack'     => $row['attack_range'],
            ':cooldown'   => $row['attack_cooldown'],
            ':speed'      => $row['move_speed'],
            ':enabled'    => $row['enabled'],
        ]);

        return $row['monster_definition_id'];
    }

    // ---- 1: a valid row loads ----------------------------------------------------

    public function testAValidSpawnRowLoadsWithEveryFieldIntact(): void
    {
        $id = $this->makeSpawn();

        $loaded = $this->repository->loadSpawnPoints('map.field');

        self::assertCount(1, $loaded['points']);
        self::assertSame([], $loaded['rejected']);

        $point = $loaded['points'][0];

        self::assertSame($id, $point['spawn_point_id']);
        self::assertSame('monster.orc', $point['monster_definition_id']);
        self::assertEqualsWithDelta(10.5, $point['position_x'], 0.001);
        self::assertEqualsWithDelta(-4.25, $point['position_z'], 0.001);
        self::assertEqualsWithDelta(3.0, $point['spawn_radius'], 0.001);
        self::assertSame(2, $point['initial_spawn_count']);
        self::assertSame(5, $point['max_alive']);
        self::assertEqualsWithDelta(20.0, $point['respawn_seconds'], 0.001);
    }

    public function testMapsAreIsolated(): void
    {
        $this->makeSpawn(['map_definition_id' => 'map.field']);
        $this->makeSpawn(['map_definition_id' => 'map.cave']);

        self::assertCount(1, $this->repository->loadSpawnPoints('map.field')['points']);
        self::assertCount(1, $this->repository->loadSpawnPoints('map.cave')['points']);
        self::assertCount(0, $this->repository->loadSpawnPoints('map.nowhere')['points']);
    }

    public function testMultipleSpawnPointsOnOneMapAreIndependent(): void
    {
        $this->makeSpawn(['monster_definition_id' => 'monster.orc', 'max_alive' => 5]);
        $this->makeSpawn(['monster_definition_id' => 'monster.poring', 'max_alive' => 20]);

        $points = $this->repository->loadSpawnPoints('map.field')['points'];

        self::assertCount(2, $points);
        self::assertNotSame($points[0]['spawn_point_id'], $points[1]['spawn_point_id']);
    }

    // ---- 8: disabled ------------------------------------------------------------------

    public function testADisabledSpawnPointIsNotLoadedAndIsNotReportedAsBroken(): void
    {
        $this->makeSpawn(['enabled' => 0]);

        $loaded = $this->repository->loadSpawnPoints('map.field');

        self::assertCount(0, $loaded['points']);
        self::assertCount(0, $loaded['rejected'],
            'a point somebody switched off is not a misconfigured one');
    }

    // ---- 4, 5: counts ---------------------------------------------------------------------

    public function testInitialCountExceedingMaxAliveIsRejectedRatherThanClamped(): void
    {
        $id = $this->makeSpawn(['initial_spawn_count' => 30, 'max_alive' => 10]);

        $loaded = $this->repository->loadSpawnPoints('map.field');

        self::assertCount(0, $loaded['points']);
        self::assertCount(1, $loaded['rejected']);
        self::assertSame($id, $loaded['rejected'][0]['spawn_point_id']);
        self::assertStringContainsString('exceeds max_alive', $loaded['rejected'][0]['reason']);
    }

    public function testAnInitialCountOfZeroIsPerfectlyValid(): void
    {
        // A nest that starts empty and fills by respawn is a legitimate design.
        $this->makeSpawn(['initial_spawn_count' => 0]);

        self::assertCount(1, $this->repository->loadSpawnPoints('map.field')['points']);
    }

    public function testInitialCountEqualToMaxAliveIsValid(): void
    {
        $this->makeSpawn(['initial_spawn_count' => 5, 'max_alive' => 5]);

        self::assertCount(1, $this->repository->loadSpawnPoints('map.field')['points']);
    }

    // ---- 3: a row with no monster ------------------------------------------------------------

    public function testASpawnRowWithNoMonsterIsRejected(): void
    {
        $this->makeSpawn(['monster_definition_id' => '']);

        $loaded = $this->repository->loadSpawnPoints('map.field');

        self::assertCount(0, $loaded['points']);
        self::assertCount(1, $loaded['rejected']);
    }

    // ---- the database refuses the worst rows itself --------------------------------------------

    public function testTheDatabaseItselfRefusesANonPositiveMaxAlive(): void
    {
        $this->expectException(\PDOException::class);

        $this->makeSpawn(['max_alive' => 0]);
    }

    public function testTheDatabaseItselfRefusesANegativeRadius(): void
    {
        $this->expectException(\PDOException::class);

        $this->makeSpawn(['spawn_radius' => -1.0]);
    }

    public function testTheDatabaseItselfRefusesANegativeRespawn(): void
    {
        $this->expectException(\PDOException::class);

        $this->makeSpawn(['respawn_seconds' => -5.0]);
    }

    // ---- AI configuration ------------------------------------------------------------------------

    public function testAValidAiConfigurationLoads(): void
    {
        $this->makeAi();

        $loaded = $this->repository->loadAiConfiguration();

        self::assertCount(1, $loaded['configurations']);
        self::assertSame([], $loaded['rejected']);

        $ai = $loaded['configurations'][0];

        self::assertSame('monster.goblin', $ai['monster_definition_id']);
        self::assertSame(MonsterSpawnRepository::AGGRESSION_DEFENSIVE, $ai['aggression_type']);
        self::assertEqualsWithDelta(12.0, $ai['detection_range'], 0.001);
        self::assertEqualsWithDelta(25.0, $ai['chase_range'], 0.001);
    }

    public function testTheThreeRequiredBehavioursAreAllConfigurable(): void
    {
        // Poring passive, Goblin defensive, Orc aggressive -- configuration, not code.
        $this->makeAi([
            'monster_definition_id' => 'monster.poring',
            'aggression_type' => MonsterSpawnRepository::AGGRESSION_PASSIVE,
        ]);
        $this->makeAi([
            'monster_definition_id' => 'monster.goblin',
            'aggression_type' => MonsterSpawnRepository::AGGRESSION_DEFENSIVE,
        ]);
        $this->makeAi([
            'monster_definition_id' => 'monster.orc',
            'aggression_type' => MonsterSpawnRepository::AGGRESSION_AGGRESSIVE,
        ]);

        $byMonster = [];

        foreach ($this->repository->loadAiConfiguration()['configurations'] as $row) {
            $byMonster[$row['monster_definition_id']] = $row['aggression_type'];
        }

        self::assertSame(MonsterSpawnRepository::AGGRESSION_PASSIVE, $byMonster['monster.poring']);
        self::assertSame(MonsterSpawnRepository::AGGRESSION_DEFENSIVE, $byMonster['monster.goblin']);
        self::assertSame(MonsterSpawnRepository::AGGRESSION_AGGRESSIVE, $byMonster['monster.orc']);
    }

    public function testAnAbsentOverrideIsReportedAsAbsentRatherThanAsZero(): void
    {
        // NULL means "use the authored value". Reporting it as 0 would silently make a
        // monster blind, deaf and stationary.
        $this->makeAi([
            'detection_range' => null,
            'chase_range'     => null,
            'attack_range'    => null,
            'attack_cooldown' => null,
            'move_speed'      => null,
        ]);

        $ai = $this->repository->loadAiConfiguration()['configurations'][0];

        self::assertLessThan(0, $ai['detection_range']);
        self::assertLessThan(0, $ai['chase_range']);
        self::assertLessThan(0, $ai['move_speed']);
        self::assertSame(MonsterSpawnRepository::AGGRESSION_DEFENSIVE, $ai['aggression_type'],
            'the one value that was set is still set');
    }

    public function testAnAbsentAggressionIsReportedAsAbsent(): void
    {
        $this->makeAi(['aggression_type' => null]);

        self::assertLessThan(0,
            $this->repository->loadAiConfiguration()['configurations'][0]['aggression_type']);
    }

    public function testADisabledAiConfigurationIsNotLoaded(): void
    {
        $this->makeAi(['enabled' => 0]);

        self::assertCount(0, $this->repository->loadAiConfiguration()['configurations']);
    }

    public function testTheDatabaseRefusesAnUnknownAggressionType(): void
    {
        $this->expectException(\PDOException::class);

        $this->makeAi(['aggression_type' => 99]);
    }

    public function testTheDatabaseRefusesNegativeAiValues(): void
    {
        $this->expectException(\PDOException::class);

        $this->makeAi(['detection_range' => -1.0]);
    }

    // ---- through the API ----------------------------------------------------------------------------

    public function testTheEndpointReturnsBothConfigurations(): void
    {
        $this->makeSpawn();
        $this->makeAi();

        $response = $this->get('/api/world/spawn-configuration', ['map_id' => 'map.field']);

        self::assertSame(200, $response->status);
        self::assertSame('map.field', $response->body['map_id']);
        self::assertCount(1, $response->body['spawn_points']);
        self::assertCount(1, $response->body['ai_configurations']);
    }

    public function testTheEndpointRequiresAMap(): void
    {
        $response = $this->get('/api/world/spawn-configuration');

        self::assertSame(400, $response->status);
        self::assertSame('invalid_map_id', $response->body['code']);
    }

    public function testTheEndpointCarriesNoAccountOrCharacterData(): void
    {
        $this->makeSpawn();

        $body = json_encode($this->get('/api/world/spawn-configuration',
            ['map_id' => 'map.field'])->body);

        // It is readable without a session, so it must carry nothing that a session
        // would have protected.
        foreach (['account', 'character_id', 'token', 'password', 'session'] as $forbidden) {
            self::assertStringNotContainsString($forbidden, $body);
        }
    }

    public function testTheEndpointReportsRejectedRowsRatherThanHidingThem(): void
    {
        $this->makeSpawn(['initial_spawn_count' => 99, 'max_alive' => 1]);

        $response = $this->get('/api/world/spawn-configuration', ['map_id' => 'map.field']);

        self::assertCount(0, $response->body['spawn_points']);
        self::assertCount(1, $response->body['rejected_spawn_points'],
            'a misconfigured nest belongs in an operator log, not in silence');
    }

    public function testThereIsNoWriteEndpointForSpawnConfiguration(): void
    {
        // Configuration is an operator's to change. A client has no route that mutates it.
        foreach ([['POST'], ['PUT'], ['DELETE']] as [$method]) {
            $response = $this->api->handle(new \ChibiFantasy\Http\Request(
                $method, '/api/world/spawn-configuration', [], [], ['map_id' => 'map.field']
            ));

            self::assertSame(404, $response->status, $method . ' must not be routed');
        }
    }
}
