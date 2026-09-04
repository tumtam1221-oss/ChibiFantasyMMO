<?php

declare(strict_types=1);

/**
 * Drops every table in a development or test database, then re-migrates.
 *
 *   php bin/db-reset.php --test --force
 *   php bin/db-reset.php --force
 *
 * Why this exists: MySQL commits DDL implicitly, so a migration that fails
 * halfway leaves the tables it already created behind. Re-running then fails on
 * "table already exists" and the operator is stuck. Rather than sprinkle
 * `IF NOT EXISTS` through the migrations -- which would hide genuine conflicts --
 * development gets an explicit way back to zero, and migrations stay strict.
 *
 * This is NOT a migration and never runs as part of one. It is guarded three ways:
 *
 *   - refuses unless APP_ENV is development or testing;
 *   - refuses a database whose name looks production-ish;
 *   - refuses without an explicit --force, so it cannot happen by reflex.
 *
 * It drops tables rather than the database, so grants, character set and collation
 * survive and the operator does not have to re-create the schema container.
 */

require_once dirname(__DIR__) . '/src/bootstrap.php';

use ChibiFantasy\Database\Connection;
use ChibiFantasy\Database\Migrator;
use ChibiFantasy\Support\Env;

$args = array_slice($argv, 1);
$useTest = in_array('--test', $args, true);
$force = in_array('--force', $args, true);

$environment = strtolower((string) Env::get('APP_ENV', ''));

if (!in_array($environment, ['development', 'testing', 'local'], true)) {
    fwrite(STDERR, "refusing: APP_ENV is '{$environment}', not a development environment." . PHP_EOL);
    exit(1);
}

$database = $useTest
    ? (string) Env::get('DB_TEST_DATABASE', '')
    : (string) Env::require('DB_DATABASE');

foreach (['prod', 'production', 'live', 'master'] as $forbidden) {
    if (str_contains(strtolower($database), $forbidden)) {
        fwrite(STDERR, "refusing: database name '{$database}' looks like production." . PHP_EOL);
        exit(1);
    }
}

if (!$force) {
    fwrite(STDERR, "refusing: this drops every table in '{$database}'. Pass --force." . PHP_EOL);
    exit(1);
}

try {
    $pdo = $useTest ? Connection::forTests() : Connection::get();

    $tables = $pdo->query('SHOW TABLES')->fetchAll(PDO::FETCH_COLUMN);

    // Foreign keys make drop order matter. Suspending the checks for the duration
    // is simpler and safer than topologically sorting a schema that changes.
    $pdo->exec('SET FOREIGN_KEY_CHECKS = 0');

    foreach ($tables as $table) {
        $pdo->exec('DROP TABLE IF EXISTS `' . str_replace('`', '``', (string) $table) . '`');
    }

    $pdo->exec('SET FOREIGN_KEY_CHECKS = 1');

    echo 'dropped ' . count($tables) . " table(s) from {$database}" . PHP_EOL;

    $migrator = new Migrator($pdo, dirname(__DIR__) . '/database/migrations');

    $applied = $migrator->migrate(static function (string $message): void {
        echo '  ' . $message . PHP_EOL;
    });

    echo 'applied ' . count($applied) . " migration(s)" . PHP_EOL;
    exit(0);
} catch (\Throwable $e) {
    fwrite(STDERR, 'reset failed: ' . $e->getMessage() . PHP_EOL);
    exit(1);
}
