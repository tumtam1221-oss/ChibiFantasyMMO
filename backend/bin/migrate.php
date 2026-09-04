<?php

declare(strict_types=1);

/**
 * Applies outstanding schema migrations.
 *
 *   php bin/migrate.php            apply to the configured database
 *   php bin/migrate.php --status   list applied and pending, change nothing
 *   php bin/migrate.php --test     apply to DB_TEST_DATABASE instead
 *
 * Forward-only by design; see Migrator for why there is no rollback command.
 */

require_once dirname(__DIR__) . '/src/bootstrap.php';

use ChibiFantasy\Database\Connection;
use ChibiFantasy\Database\Migrator;

$args = array_slice($argv, 1);
$useTest = in_array('--test', $args, true);
$statusOnly = in_array('--status', $args, true);

try {
    $pdo = $useTest ? Connection::forTests() : Connection::get();
} catch (\Throwable $e) {
    fwrite(STDERR, 'error: ' . $e->getMessage() . PHP_EOL);
    exit(1);
}

$migrator = new Migrator($pdo, dirname(__DIR__) . '/database/migrations');

$target = $useTest ? 'test' : 'application';

try {
    if ($statusOnly) {
        $applied = $migrator->applied();
        $pending = $migrator->pending();

        echo "database: {$target}" . PHP_EOL;
        echo 'applied : ' . count($applied) . PHP_EOL;

        foreach ($applied as $version) {
            echo "  [x] {$version}" . PHP_EOL;
        }

        echo 'pending : ' . count($pending) . PHP_EOL;

        foreach ($pending as $migration) {
            echo "  [ ] {$migration['filename']}" . PHP_EOL;
        }

        exit(0);
    }

    $done = $migrator->migrate(static function (string $message): void {
        echo '  ' . $message . PHP_EOL;
    });

    if ($done === []) {
        echo "nothing to apply; {$target} database is up to date" . PHP_EOL;
    } else {
        echo 'applied ' . count($done) . " migration(s) to {$target}" . PHP_EOL;
    }

    exit(0);
} catch (\Throwable $e) {
    fwrite(STDERR, 'migration failed: ' . $e->getMessage() . PHP_EOL);
    exit(1);
}
