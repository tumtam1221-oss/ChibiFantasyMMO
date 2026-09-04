<?php

declare(strict_types=1);

/**
 * Seeds one account, server, channel and character for a live end-to-end run, and
 * writes what a client needs to sign in to a file that is never committed.
 *
 * **Why a file rather than a fixed password.** The Unity integration test runs in a
 * different process from this script, so it has to learn the credential somehow. A
 * password written into either program would be a known credential living in the
 * repository forever, which is exactly the thing the phase rules forbid. Instead one
 * is invented here on every run, used once, and handed over through
 * `storage/`, which `.gitignore` already refuses.
 *
 * **Guarded like a destructive tool, because it writes an account.** It refuses
 * unless APP_ENV is a development environment, refuses a database whose name looks
 * production-ish, and targets DB_INTEGRATION_DATABASE -- never the development
 * database, and no longer the PHPUnit database either: PHPUnit truncates that on
 * every run, which silently destroyed this account and produced a dozen Unity
 * failures that looked exactly like an authentication regression.
 *
 *   php bin/integration-fixture.php
 */

require __DIR__ . '/../src/bootstrap.php';

use ChibiFantasy\Auth\AccountRepository;
use ChibiFantasy\Database\Connection;
use ChibiFantasy\Support\Env;

$environment = strtolower(Env::get('APP_ENV', ''));

if (!in_array($environment, ['development', 'testing', 'local'], true)) {
    fwrite(STDERR, "refused: APP_ENV is '{$environment}', not a development environment\n");
    exit(1);
}

$database = Env::get('DB_INTEGRATION_DATABASE', '') ?: Env::get('DB_TEST_DATABASE', '');

foreach (['prod', 'production', 'live', 'master'] as $forbidden) {
    if ($database !== '' && str_contains(strtolower($database), $forbidden)) {
        fwrite(STDERR, "refused: '{$database}' looks like a production database\n");
        exit(1);
    }
}

$pdo = Connection::forIntegration();

// Deterministic ids so a re-run replaces the same rows rather than accumulating
// players nobody deletes. The password is the only thing that changes each time.
$accountId   = 'itest-account';
$login       = 'itest-player';
$serverId    = 'itest-server';
$channelId   = 'itest-channel';
$characterId = 'itest-character';

// 32 bytes of CSPRNG. Long enough that nothing is gained by knowing the shape.
$password = bin2hex(random_bytes(32));

$pdo->exec('SET FOREIGN_KEY_CHECKS = 0');

foreach (['account_session_token', 'account_session', 'request_result', 'login_attempt',
          '`character`', 'server_channel', 'server_definition', 'account_credential',
          'account', 'monster_spawn_point', 'monster_ai_configuration'] as $table) {
    $pdo->exec("DELETE FROM {$table}");
}

$pdo->exec('SET FOREIGN_KEY_CHECKS = 1');

(new AccountRepository($pdo))->create(
    $accountId,
    'Integration Player',
    $login,
    $password,
    AccountRepository::STATUS_ACTIVE
);

$pdo->prepare(
    'INSERT INTO server_definition
        (server_id, name_key, region, status, enabled, capacity, cached_population,
         min_client_version, latest_client_version, required_protocol_version,
         min_content_version, latest_content_version, content_is_advisory,
         revision, created_at, updated_at)
     VALUES (:id, :name, :region, 1, 1, 100, 7,
             "1.0.0", "1.0.0", "1.0.0", "1.0.0", "1.0.0", 0, 0, NOW(3), NOW(3))'
)->execute([':id' => $serverId, ':name' => 'server.itest.name', ':region' => 'test']);

$pdo->prepare(
    'INSERT INTO server_channel
        (channel_id, server_id, name_key, status, enabled, capacity, cached_population,
         pk_enabled, revision, created_at, updated_at)
     VALUES (:id, :server, :name, 1, 1, 50, 3, 0, 0, NOW(3), NOW(3))'
)->execute([':id' => $channelId, ':server' => $serverId, ':name' => 'channel.itest.name']);

$pdo->prepare(
    'INSERT INTO `character`
        (character_id, account_id, server_id, name, gender, level,
         class_definition_id, job_definition_id, map_definition_id,
         appearance_definition_id, availability, revision, created_at, updated_at)
     VALUES (:cid, :aid, :sid, :name, 2, 12, "class.novice", "job.none",
             "map.town", "appearance.default", 1, 0, NOW(3), NOW(3))'
)->execute([
    ':cid'  => $characterId,
    ':aid'  => $accountId,
    ':sid'  => $serverId,
    ':name' => 'Itest',
]);

// Monster spawn configuration for the same map the character stands on, so a live world
// server reading /api/world/spawn-configuration finds something real.
//
// Three rows on purpose. Two are valid and differ in every field a designer would change,
// which is what proves the numbers travel rather than defaults. The third is valid SQL and
// invalid configuration -- it asks for more monsters than the nest holds -- because the
// endpoint reporting a named rejection is a behaviour a live test should cover, and it is
// the kind of mistake a spreadsheet actually produces.
$spawn = $pdo->prepare(
    'INSERT INTO monster_spawn_point
        (spawn_point_id, map_definition_id, monster_definition_id,
         position_x, position_y, position_z, spawn_radius,
         initial_spawn_count, max_alive, respawn_seconds, enabled,
         spawn_group_id, created_at, updated_at)
     VALUES (:id, :map, :monster, :x, :y, :z, :radius,
             :initial, :max_alive, :respawn, :enabled, :grp, NOW(3), NOW(3))'
);

foreach ([
    ['itest-spawn-a', 'monster.poring', 12.5, 0.0, -7.25, 4.0, 2, 5, 30.0, 1, 'itest-group'],
    ['itest-spawn-b', 'monster.lunatic', -3.0, 1.5, 8.0, 0.0, 1, 1, 0.0, 1, null],
    ['itest-spawn-over', 'monster.poring', 0.0, 0.0, 0.0, 0.0, 9, 2, 10.0, 1, null],
    ['itest-spawn-off', 'monster.hidden', 0.0, 0.0, 0.0, 0.0, 1, 1, 0.0, 0, null],
] as [$id, $monster, $x, $y, $z, $radius, $initial, $maxAlive, $respawn, $enabled, $group]) {
    $spawn->execute([
        ':id'        => $id,
        ':map'       => 'map.town',
        ':monster'   => $monster,
        ':x'         => $x,
        ':y'         => $y,
        ':z'         => $z,
        ':radius'    => $radius,
        ':initial'   => $initial,
        ':max_alive' => $maxAlive,
        ':respawn'   => $respawn,
        ':enabled'   => $enabled,
        ':grp'       => $group,
    ]);
}

// One AI override per behaviour worth distinguishing, including the defensive one this
// phase completed. Nulls are left where nothing is overridden, which is the case the
// endpoint has to carry as "use the authored value" rather than as zero.
$ai = $pdo->prepare(
    'INSERT INTO monster_ai_configuration
        (monster_definition_id, aggression_type, detection_range, chase_range,
         attack_range, attack_cooldown, move_speed, enabled, created_at, updated_at)
     VALUES (:monster, :aggression, :detection, :chase, :attack_range, :cooldown,
             :speed, 1, NOW(3), NOW(3))'
);

foreach ([
    ['monster.poring', 0, 0.0, null, null, null, 1.5],
    ['monster.lunatic', 1, 9.0, 18.0, 1.75, 2.5, 3.25],
    ['monster.hidden', 2, 14.0, null, null, null, null],
] as [$monster, $aggression, $detection, $chase, $attackRange, $cooldown, $speed]) {
    $ai->execute([
        ':monster'      => $monster,
        ':aggression'   => $aggression,
        ':detection'    => $detection,
        ':chase'        => $chase,
        ':attack_range' => $attackRange,
        ':cooldown'     => $cooldown,
        ':speed'        => $speed,
    ]);
}

$storage = __DIR__ . '/../storage';

if (!is_dir($storage) && !mkdir($storage, 0770, true) && !is_dir($storage)) {
    fwrite(STDERR, "refused: could not create {$storage}\n");
    exit(1);
}

$handoff = [
    'login_identifier' => $login,
    'password'         => $password,
    'account_id'       => $accountId,
    'server_id'        => $serverId,
    'channel_id'       => $channelId,
    'character_id'     => $characterId,
    'map_id'           => 'map.town',
    'database'         => $database,
];

file_put_contents(
    $storage . '/integration-fixture.json',
    json_encode($handoff, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES) . "\n"
);

// The password is written to the handoff file and deliberately not echoed: a
// console transcript is a place secrets outlive their usefulness.
echo "fixture ready in {$database}\n";
echo "  account   {$accountId}\n";
echo "  server    {$serverId}\n";
echo "  channel   {$channelId}\n";
echo "  character {$characterId}\n";
echo "  spawns    4 rows on map.town (one invalid on purpose, one disabled)\n";
echo "  ai        3 monster_ai_configuration rows\n";
echo "  credential written to storage/integration-fixture.json (gitignored)\n";
