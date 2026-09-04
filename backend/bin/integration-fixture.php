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
          'account'] as $table) {
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
echo "  credential written to storage/integration-fixture.json (gitignored)\n";
