<?php

declare(strict_types=1);

namespace ChibiFantasy\Database;

use ChibiFantasy\Support\Env;
use PDO;
use PDOException;

/**
 * The one place a database handle is created.
 *
 * Every option below is a decision the rest of the backend then relies on:
 *
 * - ERRMODE_EXCEPTION, because a silently failing write inside a transaction is
 *   how money goes missing. Every failure becomes a throw the caller must handle.
 * - EMULATE_PREPARES false, so placeholders are sent to the server as real bound
 *   parameters rather than interpolated by the driver. This is what makes SQL
 *   injection structurally impossible rather than a matter of remembering to
 *   escape, and it is asserted by a test.
 * - utf8mb4, so a player name outside the basic multilingual plane stores
 *   correctly instead of being truncated at the first emoji.
 * - STRINGIFY_FETCHES false, so integers come back as integers. Currency is an
 *   integer everywhere in this project and a string that looks like one would
 *   defeat every type check above.
 *
 * Nothing here caches a connection across requests: PHP tears the process down
 * between them, and pretending otherwise would hide a bug that only appears
 * under a persistent runtime.
 */
final class Connection
{
    private static ?PDO $shared = null;

    /**
     * A handle for the configured database.
     *
     * Shared within one request because a transaction is only meaningful on a
     * single connection: two handles would mean a commit on one and an open
     * transaction on the other.
     */
    public static function get(): PDO
    {
        if (self::$shared instanceof PDO) {
            return self::$shared;
        }

        self::$shared = self::open(Env::require('DB_DATABASE'));

        return self::$shared;
    }

    /**
     * A handle for the test database.
     *
     * Separate on purpose. Tests truncate tables, and a test suite that could be
     * pointed at the development database by a missing environment variable would
     * eventually be pointed at something worse.
     */
    public static function forTests(): PDO
    {
        $database = Env::get('DB_TEST_DATABASE');

        if ($database === null || $database === '') {
            throw new \RuntimeException(
                'DB_TEST_DATABASE is not configured. Refusing to run tests against '
                . 'the application database.'
            );
        }

        if ($database === Env::get('DB_DATABASE')) {
            throw new \RuntimeException(
                'DB_TEST_DATABASE must differ from DB_DATABASE. Refusing to truncate '
                . 'the application database.'
            );
        }

        self::$shared = self::open($database);

        return self::$shared;
    }

    private static function open(string $database): PDO
    {
        $host = Env::get('DB_HOST', '127.0.0.1');
        $port = Env::getInt('DB_PORT', 3306);
        $user = Env::require('DB_USERNAME');
        $password = Env::get('DB_PASSWORD', '');

        $dsn = sprintf(
            'mysql:host=%s;port=%d;dbname=%s;charset=utf8mb4',
            $host,
            $port,
            $database
        );

        try {
            return new PDO($dsn, $user, $password, [
                PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::ATTR_EMULATE_PREPARES   => false,
                PDO::ATTR_STRINGIFY_FETCHES  => false,
            ]);
        } catch (PDOException $e) {
            // The message from PDO can carry the DSN, which carries the host and
            // database name. That is fine in a log and unacceptable in a response,
            // so the exception is reshaped here and the HTTP layer never sees the
            // original.
            throw new \RuntimeException(
                'Database connection failed for ' . $host . ':' . $port . '/' . $database,
                0,
                $e
            );
        }
    }

    /** Test seam: drop the shared handle so the next call reopens. */
    public static function reset(): void
    {
        self::$shared = null;
    }

    /**
     * Runs a closure inside a transaction, committing or rolling back.
     *
     * The point of putting this here rather than at each call site is that there
     * is then exactly one place where a rollback can be forgotten. Any throw
     * unwinds the whole unit of work; nothing is left half applied.
     *
     * Nested calls join the outer transaction rather than starting a second one,
     * because MySQL has no real nested transactions and a naive inner commit
     * would publish the outer one's partial work.
     *
     * @template T
     * @param callable(PDO):T $work
     * @return T
     */
    public static function transactional(PDO $pdo, callable $work): mixed
    {
        if ($pdo->inTransaction()) {
            return $work($pdo);
        }

        $pdo->beginTransaction();

        try {
            $result = $work($pdo);
            $pdo->commit();

            return $result;
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) {
                $pdo->rollBack();
            }

            throw $e;
        }
    }
}
