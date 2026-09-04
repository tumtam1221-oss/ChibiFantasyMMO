<?php

declare(strict_types=1);

namespace ChibiFantasy\World;

use PDO;

/**
 * Reads the monster spawn and AI configuration a world server needs for one map.
 *
 * **It reads and validates; it never guesses.** A row that cannot become a working
 * spawn point is reported as rejected, with the field that was wrong, rather than
 * silently corrected. A designer who typed 0 for max_alive should be told, not
 * quietly given 1 -- the second is how a map ends up half-populated and nobody
 * knows why.
 *
 * **It copies no content.** Maps and monsters are Unity content referenced here by
 * DefinitionId. This repository does not know whether `monster.orc` exists; that is
 * checked by the server against its loaded registries, which is the only place the
 * answer is actually known.
 *
 * **Read-only by construction.** There is no write method. Spawn configuration is
 * changed by an operator against the database, and nothing a player can reach
 * mutates it.
 */
final class MonsterSpawnRepository
{
    /** Mirrors Phase 10 MonsterAggressionType. */
    public const AGGRESSION_PASSIVE = 0;
    public const AGGRESSION_DEFENSIVE = 1;
    public const AGGRESSION_AGGRESSIVE = 2;
    public const AGGRESSION_ASSIST_ONLY = 3;

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * Every enabled spawn point on one map, with the invalid ones separated out.
     *
     * Disabled rows are excluded in SQL rather than filtered afterwards: a disabled
     * point is not a rejected point, and reporting it as one would fill an
     * operator's log with things they switched off on purpose.
     *
     * @return array{points:list<array<string,mixed>>,rejected:list<array<string,string>>}
     */
    public function loadSpawnPoints(string $mapId): array
    {
        if ($mapId === '') {
            return ['points' => [], 'rejected' => []];
        }

        $statement = $this->pdo->prepare(
            'SELECT spawn_point_id, map_definition_id, monster_definition_id,
                    position_x, position_y, position_z, spawn_radius,
                    initial_spawn_count, max_alive, respawn_seconds, spawn_group_id
             FROM monster_spawn_point
             WHERE map_definition_id = :map AND enabled = 1
             ORDER BY spawn_point_id ASC'
        );

        $statement->execute([':map' => $mapId]);

        $points = [];
        $rejected = [];

        foreach ($statement->fetchAll() as $row) {
            $reason = $this->rejectSpawn($row);

            if ($reason !== null) {
                $rejected[] = [
                    'spawn_point_id' => (string) $row['spawn_point_id'],
                    'reason'         => $reason,
                ];

                continue;
            }

            $points[] = [
                'spawn_point_id'        => (string) $row['spawn_point_id'],
                'map_definition_id'     => (string) $row['map_definition_id'],
                'monster_definition_id' => (string) $row['monster_definition_id'],
                'position_x'            => (float) $row['position_x'],
                'position_y'            => (float) $row['position_y'],
                'position_z'            => (float) $row['position_z'],
                'spawn_radius'          => (float) $row['spawn_radius'],
                'initial_spawn_count'   => (int) $row['initial_spawn_count'],
                'max_alive'             => (int) $row['max_alive'],
                'respawn_seconds'       => (float) $row['respawn_seconds'],
                'spawn_group_id'        => $row['spawn_group_id'] === null
                    ? '' : (string) $row['spawn_group_id'],
            ];
        }

        return ['points' => $points, 'rejected' => $rejected];
    }

    /**
     * Why a row cannot become a spawn point, or null if it can.
     *
     * The CHECK constraints already refuse a negative radius, a non-positive
     * max_alive and a negative respawn at write time. These are checked again on
     * read because a database restored from an older dump, or one whose checks were
     * disabled during an import, is exactly the situation where invalid
     * configuration reaches a running server.
     *
     * @param array<string,mixed> $row
     */
    private function rejectSpawn(array $row): ?string
    {
        if ((string) $row['monster_definition_id'] === '') {
            return 'monster_definition_id is empty';
        }

        if ((string) $row['map_definition_id'] === '') {
            return 'map_definition_id is empty';
        }

        $maxAlive = (int) $row['max_alive'];

        if ($maxAlive <= 0) {
            return 'max_alive must be greater than zero';
        }

        $initial = (int) $row['initial_spawn_count'];

        if ($initial < 0) {
            return 'initial_spawn_count must not be negative';
        }

        if ($initial > $maxAlive) {
            // Refused rather than clamped: an operator who typed 30 into a nest that
            // holds 10 meant something, and silently spawning 10 hides the mistake.
            return 'initial_spawn_count exceeds max_alive';
        }

        if ((float) $row['spawn_radius'] < 0) {
            return 'spawn_radius must not be negative';
        }

        if ((float) $row['respawn_seconds'] < 0) {
            return 'respawn_seconds must not be negative';
        }

        foreach (['position_x', 'position_y', 'position_z'] as $axis) {
            $value = (float) $row[$axis];

            if (is_nan($value) || is_infinite($value)) {
                return $axis . ' is not a finite number';
            }
        }

        return null;
    }

    /**
     * AI overrides, keyed by monster.
     *
     * Every field is nullable and NULL means "use what the monster was authored
     * with", so a row that only changes aggression does not have to restate five
     * numbers. That is what keeps this table from becoming a second copy of
     * MonsterDefinition.
     *
     * @return array{configurations:list<array<string,mixed>>,rejected:list<array<string,string>>}
     */
    public function loadAiConfiguration(): array
    {
        $statement = $this->pdo->query(
            'SELECT monster_definition_id, aggression_type, detection_range, chase_range,
                    attack_range, attack_cooldown, move_speed
             FROM monster_ai_configuration
             WHERE enabled = 1
             ORDER BY monster_definition_id ASC'
        );

        $configurations = [];
        $rejected = [];

        foreach ($statement->fetchAll() as $row) {
            $reason = $this->rejectAi($row);

            if ($reason !== null) {
                $rejected[] = [
                    'monster_definition_id' => (string) $row['monster_definition_id'],
                    'reason'                => $reason,
                ];

                continue;
            }

            $configurations[] = [
                'monster_definition_id' => (string) $row['monster_definition_id'],
                // -1 carries "not overridden" across a wire that has no null for a
                // number the client reads as an int. The server treats any negative
                // as absent, so an authored value is used instead.
                'aggression_type'  => $this->orAbsentInt($row['aggression_type']),
                'detection_range'  => $this->orAbsentFloat($row['detection_range']),
                'chase_range'      => $this->orAbsentFloat($row['chase_range']),
                'attack_range'     => $this->orAbsentFloat($row['attack_range']),
                'attack_cooldown'  => $this->orAbsentFloat($row['attack_cooldown']),
                'move_speed'       => $this->orAbsentFloat($row['move_speed']),
            ];
        }

        return ['configurations' => $configurations, 'rejected' => $rejected];
    }

    /** @param array<string,mixed> $row */
    private function rejectAi(array $row): ?string
    {
        if ((string) $row['monster_definition_id'] === '') {
            return 'monster_definition_id is empty';
        }

        if ($row['aggression_type'] !== null) {
            $aggression = (int) $row['aggression_type'];

            if ($aggression < self::AGGRESSION_PASSIVE
                || $aggression > self::AGGRESSION_ASSIST_ONLY) {
                return 'aggression_type is not a known behaviour';
            }
        }

        foreach (['detection_range', 'chase_range', 'attack_range', 'attack_cooldown',
                  'move_speed'] as $field) {
            if ($row[$field] === null) {
                continue;
            }

            $value = (float) $row[$field];

            if (is_nan($value) || is_infinite($value)) {
                return $field . ' is not a finite number';
            }

            if ($value < 0) {
                return $field . ' must not be negative';
            }
        }

        return null;
    }

    private function orAbsentInt(mixed $value): int
    {
        return $value === null ? -1 : (int) $value;
    }

    private function orAbsentFloat(mixed $value): float
    {
        return $value === null ? -1.0 : (float) $value;
    }
}
