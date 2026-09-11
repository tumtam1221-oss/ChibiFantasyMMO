<?php

declare(strict_types=1);

/**
 * Deterministic development seed data.
 *
 *   php bin/seed.php            seed the application database
 *   php bin/seed.php --test     seed the test database
 *
 * What this deliberately does NOT create: an account with a known password.
 * A development fixture that ships credentials is a production breach waiting for
 * somebody to copy the file, and "it is only for dev" has never once stopped that.
 * Accounts are created by the tests, which invent their own passwords at runtime.
 *
 * What it does create is the content an operator would otherwise click in by hand:
 * a currency, a server, and its channels -- one with PK off and one with PK on, so
 * the configuration seam is exercised rather than merely declared.
 */

require_once dirname(__DIR__) . '/src/bootstrap.php';

use ChibiFantasy\Database\Connection;
use ChibiFantasy\Support\Env;

$useTest = in_array('--test', array_slice($argv, 1), true);

$environment = strtolower((string) Env::get('APP_ENV', ''));

if (!in_array($environment, ['development', 'testing', 'local'], true)) {
    fwrite(STDERR, "refusing: APP_ENV is '{$environment}', not a development environment." . PHP_EOL);
    exit(1);
}

try {
    $pdo = $useTest ? Connection::forTests() : Connection::get();

    // Every insert is idempotent, so seeding twice changes nothing and a partially
    // seeded database can be completed rather than reset.
    $currency = $pdo->prepare(
        'INSERT INTO currency_definition
            (currency_id, name_key, maximum_balance, enabled, revision, created_at, updated_at)
         VALUES (:id, :name, :max, 1, 0, NOW(3), NOW(3))
         ON DUPLICATE KEY UPDATE name_key = VALUES(name_key)'
    );

    $currency->execute([
        ':id'   => 'currency.gold',
        ':name' => 'currency.gold.name',
        ':max'  => 2000000000,
    ]);

    $server = $pdo->prepare(
        'INSERT INTO server_definition
            (server_id, name_key, region, status, enabled, capacity,
             min_client_version, latest_client_version, required_protocol_version,
             min_content_version, latest_content_version, content_is_advisory,
             revision, created_at, updated_at)
         VALUES (:id, :name, :region, 1, 1, :capacity,
                 :minc, :latestc, :proto, :minct, :latestct, 0,
                 0, NOW(3), NOW(3))
         ON DUPLICATE KEY UPDATE name_key = VALUES(name_key), region = VALUES(region)'
    );

    $server->execute([
        ':id'       => 'server.dev.aurora',
        ':name'     => 'server.aurora.name',
        ':region'   => 'dev',
        ':capacity' => 1000,
        ':minc'     => '1.0.0',
        ':latestc'  => '1.0.0',
        ':proto'    => '1.0.0',
        ':minct'    => '1.0.0',
        ':latestct' => '1.0.0',
    ]);

    $channel = $pdo->prepare(
        'INSERT INTO server_channel
            (channel_id, server_id, name_key, status, enabled, capacity, pk_enabled,
             revision, created_at, updated_at)
         VALUES (:id, :server, :name, 1, 1, :capacity, :pk, 0, NOW(3), NOW(3))
         ON DUPLICATE KEY UPDATE name_key = VALUES(name_key), pk_enabled = VALUES(pk_enabled)'
    );

    // Two channels of one server differing only in PK, so nothing can be deriving
    // it from a channel number.
    $channel->execute([
        ':id'       => 'channel.dev.aurora.1',
        ':server'   => 'server.dev.aurora',
        ':name'     => 'channel.one.name',
        ':capacity' => 200,
        ':pk'       => 0,
    ]);

    $channel->execute([
        ':id'       => 'channel.dev.aurora.2',
        ':server'   => 'server.dev.aurora',
        ':name'     => 'channel.two.name',
        ':capacity' => 200,
        ':pk'       => 1,
    ]);

    // The world boss, where a running world expects to find it.
    //
    // The database owns where and how often a monster appears; Unity owns what it is.
    // So only the placement is here -- the boss's stats, rank, loot table and reward all
    // stay in the authored MonsterDefinition, and nothing below repeats them.
    //
    // Idempotent, because a seed that fails the second time it runs is a seed nobody runs.
    $spawn = $pdo->prepare(
        'INSERT INTO monster_spawn_point
            (spawn_point_id, map_definition_id, monster_definition_id,
             position_x, position_y, position_z, spawn_radius,
             initial_spawn_count, max_alive, respawn_seconds, enabled,
             spawn_group_id, created_at, updated_at)
         VALUES (:id, :map, :monster, :x, :y, :z, :radius,
                 :initial, :max_alive, :respawn, 1, :grp, NOW(3), NOW(3))
         ON DUPLICATE KEY UPDATE
            map_definition_id = VALUES(map_definition_id),
            monster_definition_id = VALUES(monster_definition_id),
            position_x = VALUES(position_x),
            position_y = VALUES(position_y),
            position_z = VALUES(position_z),
            spawn_radius = VALUES(spawn_radius),
            initial_spawn_count = VALUES(initial_spawn_count),
            max_alive = VALUES(max_alive),
            respawn_seconds = VALUES(respawn_seconds),
            enabled = VALUES(enabled),
            updated_at = NOW(3)'
    );

    // The camps around the walled town. Every position is outside the map's authored safe
    // zones (the town wall, r 30 at the origin; the mountain path, r 24 at (-8, 55)) -- the
    // server refuses a row inside one, so a typo here empties a camp rather than putting
    // a slime in the plaza. Y is 0 on purpose: the server drops each monster onto the
    // baked ground when it spawns.
    $camps = [
        // id, monster, x, z, radius, initial, max alive, respawn seconds
        ['spawn.harbor_town.south_meadow', 'monster.training_slime',    0.0,  -58.0, 6.0, 4, 6, 20.0],
        ['spawn.harbor_town.west_shore',   'monster.training_slime',  -58.0,   -8.0, 6.0, 3, 5, 25.0],
        ['spawn.harbor_town.east_woods',   'monster.training_slime',   54.0,   12.0, 6.0, 3, 5, 25.0],
        ['spawn.harbor_town.river_bend',   'monster.training_slime',   38.0,  -52.0, 6.0, 3, 5, 30.0],
        // The world boss keeps the far knoll. Thirty minutes, matching the authored
        // RespawnSettings the runtime already uses.
        ['spawn.harbor_town.slime_king',   'monster.ancient_slime_king', -46.0, 40.0, 0.0, 1, 1, 1800.0],
    ];

    foreach ($camps as [$id, $monster, $x, $z, $radius, $initial, $maxAlive, $respawn]) {
        $spawn->execute([
            ':id'        => $id,
            ':map'       => 'map.harbor_town',
            ':monster'   => $monster,
            ':x'         => $x,
            ':y'         => 0.0,
            ':z'         => $z,
            ':radius'    => $radius,
            ':initial'   => $initial,
            ':max_alive' => $maxAlive,
            ':respawn'   => $respawn,
            ':grp'       => null,
        ]);
    }

    echo 'seeded ' . ($useTest ? 'test' : 'application') . ' database' . PHP_EOL;
    echo '  1 currency, 1 server, 2 channels (one with PK enabled)' . PHP_EOL;
    echo '  5 monster camps on map.harbor_town (4 slime camps outside the wall + the slime king on the far knoll)' . PHP_EOL;
    echo '  no accounts: fixtures never ship credentials' . PHP_EOL;

    exit(0);
} catch (\Throwable $e) {
    fwrite(STDERR, 'seed failed: ' . $e->getMessage() . PHP_EOL);
    exit(1);
}
