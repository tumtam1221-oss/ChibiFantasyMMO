<?php

declare(strict_types=1);

namespace ChibiFantasy\Character;

use PDO;

/**
 * Loading and saving everything a world server needs a character to be.
 *
 * **Why this is separate from CharacterRepository.** That one answers character
 * select: a list, scoped by account, cheap and read-only. This one is the world's
 * -- one character, every aggregate, written back under optimistic concurrency.
 * Merging them would put a five-table transaction behind the query that runs on
 * every login screen.
 *
 * **Three rules govern every method here.**
 *
 * *Ownership is a WHERE clause, never a check.* Every statement that touches a
 * character is scoped by `account_id` in SQL. There is no method that loads a
 * character by id alone, so "load somebody else's character" is not something the
 * caller can express, let alone something a validation could forget.
 *
 * *A save presents the revisions it loaded.* Each Unity aggregate carries its own
 * Revision and they advance independently. A save that arrives with a stale
 * `save_revision` is refused whole -- it does not overwrite the newer state and it
 * does not partially apply.
 *
 * *Nothing is written outside a transaction.* A character is five tables. Writing
 * stats and failing on skills would leave a character that never existed.
 */
final class CharacterStateRepository
{
    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The whole character, or null if this account does not own it.
     *
     * Returns null both for "no such character" and "somebody else's character",
     * exactly as `CharacterRepository::findOwned` does, so a caller cannot tell the
     * two apart and a response cannot confirm that another player's character exists.
     *
     * @return array<string,mixed>|null
     */
    public function load(string $accountId, string $characterId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT character_id, account_id, server_id, name, gender, level, experience,
                    current_health, current_mana, class_definition_id, job_definition_id,
                    map_definition_id, spawn_definition_id, appearance_definition_id,
                    availability, revision
             FROM `character`
             WHERE character_id = :cid AND account_id = :aid'
        );

        $statement->execute([':cid' => $characterId, ':aid' => $accountId]);

        $row = $statement->fetch();

        if ($row === false) {
            return null;
        }

        return [
            'character_id'  => (string) $row['character_id'],
            'account_id'    => (string) $row['account_id'],
            'server_id'     => (string) $row['server_id'],
            'name'          => (string) $row['name'],
            'gender'        => (int) $row['gender'],
            'level'         => (int) $row['level'],
            'experience'    => (int) $row['experience'],
            'current_health' => (int) $row['current_health'],
            'current_mana'  => (int) $row['current_mana'],
            'class_id'      => (string) $row['class_definition_id'],
            'job_id'        => (string) $row['job_definition_id'],
            'map_id'        => (string) $row['map_definition_id'],
            'spawn_id'      => (string) $row['spawn_definition_id'],
            'appearance_id' => (string) $row['appearance_definition_id'],
            'availability'  => (int) $row['availability'],
            'revision'      => (int) $row['revision'],

            'stats'         => $this->loadStats($characterId),
            'appearance'    => $this->loadAppearance($characterId),
            'skills'        => $this->loadSkills($characterId),
            'revisions'     => $this->loadRevisions($characterId),
        ];
    }

    /** @return list<array{stat_id:string,value:int}> */
    private function loadStats(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT stat_definition_id, value FROM character_stat
             WHERE character_id = :cid ORDER BY stat_definition_id ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'stat_id' => (string) $r['stat_definition_id'],
                'value'   => (int) $r['value'],
            ],
            $statement->fetchAll()
        );
    }

    /** @return list<array{slot:int,option_id:string}> */
    private function loadAppearance(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT slot, option_definition_id FROM character_appearance
             WHERE character_id = :cid ORDER BY slot ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'slot'      => (int) $r['slot'],
                'option_id' => (string) $r['option_definition_id'],
            ],
            $statement->fetchAll()
        );
    }

    /** @return list<array{skill_id:string,level:int}> */
    private function loadSkills(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT skill_definition_id, skill_level FROM character_skill
             WHERE character_id = :cid ORDER BY skill_definition_id ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'skill_id' => (string) $r['skill_definition_id'],
                'level'    => (int) $r['skill_level'],
            ],
            $statement->fetchAll()
        );
    }

    /**
     * The revisions this character was last saved at.
     *
     * A character that has never been saved has no row, and reports zeros. That is
     * the correct answer rather than an error: a freshly created character genuinely
     * is at revision zero, and making the first save a special case would mean every
     * caller handling it.
     *
     * @return array<string,int>
     */
    public function loadRevisions(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT identity_revision, class_revision, appearance_revision,
                    progression_revision, stats_revision, skills_revision, save_revision
             FROM character_save_revision WHERE character_id = :cid'
        );

        $statement->execute([':cid' => $characterId]);

        $row = $statement->fetch();

        if ($row === false) {
            return [
                'identity' => 0, 'class' => 0, 'appearance' => 0, 'progression' => 0,
                'stats' => 0, 'skills' => 0, 'save' => 0,
            ];
        }

        return [
            'identity'    => (int) $row['identity_revision'],
            'class'       => (int) $row['class_revision'],
            'appearance'  => (int) $row['appearance_revision'],
            'progression' => (int) $row['progression_revision'],
            'stats'       => (int) $row['stats_revision'],
            'skills'      => (int) $row['skills_revision'],
            'save'        => (int) $row['save_revision'],
        ];
    }

    /**
     * Writes the character back, or refuses because somebody else already did.
     *
     * **The whole point is `expectedSaveRevision`.** A world server that crashed and
     * restarted, a reconnect racing a shutdown, or two processes that both believe
     * they own a character all end the same way: the second writer presents a
     * revision that has moved on and is refused. Without it the later write wins by
     * accident, which is how an hour of progress disappears.
     *
     * **Passing null means "first save".** It succeeds only if no revision row
     * exists. A caller that does not know the revision cannot use that to bypass the
     * check -- it can only claim the character has never been saved, which is false
     * the moment it has been.
     *
     * **Idempotent by revision, not by request id.** Saving the same state twice with
     * the same expected revision succeeds once and is then refused, which is the
     * correct answer: the second call's view of the world is genuinely stale.
     *
     * @param array<string,mixed> $state
     * @return array{ok:bool,reason?:string,save_revision?:int}
     */
    public function save(
        string $accountId,
        string $characterId,
        array $state,
        ?int $expectedSaveRevision
    ): array {
        $this->pdo->beginTransaction();

        try {
            // The character row is locked first and scoped by account. Everything
            // below writes only after this succeeds, so an unowned character never
            // reaches a single UPDATE.
            $owned = $this->pdo->prepare(
                'SELECT character_id FROM `character`
                 WHERE character_id = :cid AND account_id = :aid
                 FOR UPDATE'
            );

            $owned->execute([':cid' => $characterId, ':aid' => $accountId]);

            if ($owned->fetch() === false) {
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'character_not_owned'];
            }

            $current = $this->lockRevision($characterId);

            if ($expectedSaveRevision === null) {
                if ($current !== null) {
                    $this->pdo->rollBack();

                    return ['ok' => false, 'reason' => 'stale_revision'];
                }

                $next = 1;
            } else {
                if ($current === null || $current !== $expectedSaveRevision) {
                    $this->pdo->rollBack();

                    return ['ok' => false, 'reason' => 'stale_revision'];
                }

                $next = $current + 1;
            }

            $this->writeCharacterRow($characterId, $state);
            $this->replaceStats($characterId, $state['stats'] ?? []);
            $this->replaceAppearance($characterId, $state['appearance'] ?? []);
            $this->replaceSkills($characterId, $state['skills'] ?? []);
            $this->writeRevisions($characterId, $state['revisions'] ?? [], $next);

            $this->pdo->commit();

            return ['ok' => true, 'save_revision' => $next];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * The current save revision, locked, or null if this character has never saved.
     *
     * `FOR UPDATE` on a row that may not exist does not block a concurrent insert,
     * so the primary key on `character_save_revision` is what actually makes two
     * first-saves race safely: one inserts, the other's insert fails and its
     * transaction rolls back.
     */
    private function lockRevision(string $characterId): ?int
    {
        $statement = $this->pdo->prepare(
            'SELECT save_revision FROM character_save_revision
             WHERE character_id = :cid FOR UPDATE'
        );

        $statement->execute([':cid' => $characterId]);

        $value = $statement->fetchColumn();

        return $value === false ? null : (int) $value;
    }

    /** @param array<string,mixed> $state */
    private function writeCharacterRow(string $characterId, array $state): void
    {
        $statement = $this->pdo->prepare(
            'UPDATE `character` SET
                level = :level,
                experience = :experience,
                current_health = :health,
                current_mana = :mana,
                class_definition_id = :class,
                job_definition_id = :job,
                map_definition_id = :map,
                spawn_definition_id = :spawn,
                revision = revision + 1,
                updated_at = NOW(3)
             WHERE character_id = :cid'
        );

        $statement->execute([
            ':level'      => max(1, (int) ($state['level'] ?? 1)),
            ':experience' => max(0, (int) ($state['experience'] ?? 0)),
            ':health'     => max(0, (int) ($state['current_health'] ?? 0)),
            ':mana'       => max(0, (int) ($state['current_mana'] ?? 0)),
            ':class'      => (string) ($state['class_id'] ?? ''),
            ':job'        => (string) ($state['job_id'] ?? ''),
            ':map'        => (string) ($state['map_id'] ?? ''),
            ':spawn'      => (string) ($state['spawn_id'] ?? ''),
            ':cid'        => $characterId,
        ]);
    }

    /**
     * Replaces a character's stats wholesale.
     *
     * Delete-then-insert rather than a diff, because the authoritative set is what
     * the server holds and a stat that vanished from it must vanish here. A merge
     * would leave a removed stat behind forever, and a stat nobody can see is worse
     * than one that is wrong.
     *
     * Both statements are inside the caller's transaction, so there is no moment at
     * which a reader sees a character with no stats.
     *
     * @param list<array{stat_id:string,value:int}> $stats
     */
    private function replaceStats(string $characterId, array $stats): void
    {
        $this->pdo->prepare('DELETE FROM character_stat WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($stats === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_stat (character_id, stat_definition_id, value)
             VALUES (:cid, :stat, :value)'
        );

        foreach ($stats as $stat) {
            $id = (string) ($stat['stat_id'] ?? '');

            if ($id === '') {
                continue;
            }

            $insert->execute([
                ':cid'   => $characterId,
                ':stat'  => $id,
                ':value' => (int) ($stat['value'] ?? 0),
            ]);
        }
    }

    /** @param list<array{slot:int,option_id:string}> $appearance */
    private function replaceAppearance(string $characterId, array $appearance): void
    {
        $this->pdo->prepare('DELETE FROM character_appearance WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($appearance === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_appearance (character_id, slot, option_definition_id)
             VALUES (:cid, :slot, :option)'
        );

        foreach ($appearance as $entry) {
            $insert->execute([
                ':cid'    => $characterId,
                ':slot'   => (int) ($entry['slot'] ?? 0),
                ':option' => (string) ($entry['option_id'] ?? ''),
            ]);
        }
    }

    /** @param list<array{skill_id:string,level:int}> $skills */
    private function replaceSkills(string $characterId, array $skills): void
    {
        $this->pdo->prepare('DELETE FROM character_skill WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($skills === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_skill (character_id, skill_definition_id, skill_level)
             VALUES (:cid, :skill, :level)'
        );

        foreach ($skills as $skill) {
            $id = (string) ($skill['skill_id'] ?? '');

            if ($id === '') {
                continue;
            }

            $insert->execute([
                ':cid'   => $characterId,
                ':skill' => $id,
                ':level' => max(1, (int) ($skill['level'] ?? 1)),
            ]);
        }
    }

    /** @param array<string,int> $revisions */
    private function writeRevisions(string $characterId, array $revisions, int $next): void
    {
        $statement = $this->pdo->prepare(
            'INSERT INTO character_save_revision
                (character_id, identity_revision, class_revision, appearance_revision,
                 progression_revision, stats_revision, skills_revision, save_revision, saved_at)
             VALUES (:cid, :identity, :class, :appearance, :progression, :stats, :skills,
                     :save, NOW(3))
             ON DUPLICATE KEY UPDATE
                identity_revision = VALUES(identity_revision),
                class_revision = VALUES(class_revision),
                appearance_revision = VALUES(appearance_revision),
                progression_revision = VALUES(progression_revision),
                stats_revision = VALUES(stats_revision),
                skills_revision = VALUES(skills_revision),
                save_revision = VALUES(save_revision),
                saved_at = NOW(3)'
        );

        $statement->execute([
            ':cid'         => $characterId,
            ':identity'    => max(0, (int) ($revisions['identity'] ?? 0)),
            ':class'       => max(0, (int) ($revisions['class'] ?? 0)),
            ':appearance'  => max(0, (int) ($revisions['appearance'] ?? 0)),
            ':progression' => max(0, (int) ($revisions['progression'] ?? 0)),
            ':stats'       => max(0, (int) ($revisions['stats'] ?? 0)),
            ':skills'      => max(0, (int) ($revisions['skills'] ?? 0)),
            ':save'        => $next,
        ]);
    }
}
