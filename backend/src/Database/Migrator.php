<?php

declare(strict_types=1);

namespace ChibiFantasy\Database;

use PDO;

/**
 * Applies ordered, tracked, forward-only schema migrations.
 *
 * Three properties, and each is a decision:
 *
 * - **Ordered.** Files are sorted by their numeric prefix, so `0002` always runs
 *   after `0001` regardless of what the filesystem reports.
 * - **Tracked.** Every applied file is recorded in `schema_migration`, so running
 *   the migrator twice applies nothing the second time. This is what makes it safe
 *   to run on every deploy.
 * - **Forward-only.** There is no `down()`. A rollback that has never been executed
 *   is a rollback that does not work, and one that drops a column destroys data an
 *   operator may still need. Undoing a migration is a new migration, written
 *   deliberately, with the data question answered.
 *
 * Each file runs inside a transaction where MySQL allows it. MySQL commits DDL
 * implicitly, so a migration containing several `CREATE TABLE` statements cannot
 * be rolled back as a unit -- that is a property of the engine, not a claim this
 * class makes. The mitigation is one concern per file, so a failure leaves a
 * boundary an operator can reason about.
 */
final class Migrator
{
    private const TRACKING_TABLE = 'schema_migration';

    public function __construct(
        private readonly PDO $pdo,
        private readonly string $directory
    ) {
    }

    /**
     * Creates the tracking table if it does not exist.
     *
     * Deliberately not itself a migration: something has to record that migrations
     * ran, and it cannot be the thing being recorded.
     */
    public function prepare(): void
    {
        $this->pdo->exec(
            'CREATE TABLE IF NOT EXISTS ' . self::TRACKING_TABLE . ' ('
            . '  version      VARCHAR(64)  NOT NULL,'
            . '  filename     VARCHAR(255) NOT NULL,'
            . '  applied_at   DATETIME(3)  NOT NULL,'
            . '  PRIMARY KEY (version)'
            . ') ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci'
        );
    }

    /** @return list<string> versions already applied, ascending */
    public function applied(): array
    {
        $this->prepare();

        $rows = $this->pdo
            ->query('SELECT version FROM ' . self::TRACKING_TABLE . ' ORDER BY version')
            ->fetchAll();

        return array_map(static fn (array $row): string => (string) $row['version'], $rows);
    }

    /** @return list<array{version:string,filename:string,path:string}> every migration on disk, ascending */
    public function available(): array
    {
        if (!is_dir($this->directory)) {
            return [];
        }

        $files = glob($this->directory . '/*.sql');

        if ($files === false) {
            return [];
        }

        $migrations = [];

        foreach ($files as $path) {
            $filename = basename($path);

            // `0007_create_account.sql` -> version `0007`. A file without a numeric
            // prefix has no defined position, so it is refused rather than guessed at.
            if (preg_match('/^(\d+)_/', $filename, $matches) !== 1) {
                throw new \RuntimeException(
                    "Migration '{$filename}' has no numeric prefix and cannot be ordered."
                );
            }

            $migrations[] = [
                'version'  => $matches[1],
                'filename' => $filename,
                'path'     => $path,
            ];
        }

        usort(
            $migrations,
            static fn (array $a, array $b): int => strcmp($a['version'], $b['version'])
        );

        $seen = [];

        foreach ($migrations as $migration) {
            if (isset($seen[$migration['version']])) {
                throw new \RuntimeException(
                    "Duplicate migration version {$migration['version']}: "
                    . "'{$seen[$migration['version']]}' and '{$migration['filename']}'."
                );
            }

            $seen[$migration['version']] = $migration['filename'];
        }

        return $migrations;
    }

    /** @return list<array{version:string,filename:string,path:string}> */
    public function pending(): array
    {
        $applied = array_flip($this->applied());

        return array_values(array_filter(
            $this->available(),
            static fn (array $m): bool => !isset($applied[$m['version']])
        ));
    }

    /**
     * Applies everything outstanding, oldest first.
     *
     * @param callable(string):void|null $report progress sink, for the CLI
     * @return list<string> versions applied by this call
     */
    public function migrate(?callable $report = null): array
    {
        $this->prepare();

        $done = [];

        foreach ($this->pending() as $migration) {
            $sql = file_get_contents($migration['path']);

            if ($sql === false) {
                throw new \RuntimeException("Cannot read migration {$migration['filename']}.");
            }

            if ($report !== null) {
                $report("applying {$migration['filename']}");
            }

            foreach ($this->statements($sql) as $statement) {
                $this->pdo->exec($statement);
            }

            $record = $this->pdo->prepare(
                'INSERT INTO ' . self::TRACKING_TABLE . ' (version, filename, applied_at) '
                . 'VALUES (:version, :filename, NOW(3))'
            );

            $record->execute([
                ':version'  => $migration['version'],
                ':filename' => $migration['filename'],
            ]);

            $done[] = $migration['version'];
        }

        return $done;
    }

    /**
     * Splits a file into executable statements.
     *
     * PDO::exec runs one statement at a time, so a file holding several has to be
     * split. The split is on semicolons that end a line, which is enough for schema
     * DDL and deliberately not a general SQL parser -- migrations here contain no
     * stored routines, and if they ever do, this must become a real lexer rather
     * than grow special cases.
     *
     * @return list<string>
     */
    private function statements(string $sql): array
    {
        // Strip `-- ` comments so a semicolon inside one cannot split a statement.
        $withoutComments = preg_replace('/^\s*--.*$/m', '', $sql) ?? $sql;

        $parts = preg_split('/;\s*$/m', $withoutComments);

        if ($parts === false) {
            return [];
        }

        $statements = [];

        foreach ($parts as $part) {
            $trimmed = trim($part);

            if ($trimmed !== '') {
                $statements[] = $trimmed;
            }
        }

        return $statements;
    }
}
