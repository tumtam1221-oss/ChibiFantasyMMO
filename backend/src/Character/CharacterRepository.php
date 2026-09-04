<?php

declare(strict_types=1);

namespace ChibiFantasy\Character;

use PDO;

/**
 * Reads characters, always scoped by the account that owns them.
 *
 * Every method here takes an account id and puts it in the WHERE clause. There is
 * deliberately no `findAll` and no `findById` that ignores ownership: a method
 * that returned a character without checking who asked would eventually be called
 * by something that forgot to check, and the brief's rule -- never select
 * everything and filter in the client -- would be broken one call site at a time.
 *
 * `ownsCharacter` exists separately from `listForAccount` because a list is a
 * snapshot and ownership has to be re-established at the moment it is acted on.
 * Phase 14 makes the same distinction for the same reason.
 */
final class CharacterRepository
{
    public const AVAILABILITY_UNKNOWN = 0;
    public const AVAILABILITY_PLAYABLE = 1;
    public const AVAILABILITY_PENDING_DELETION = 2;
    public const AVAILABILITY_LOCKED = 3;
    public const AVAILABILITY_IN_WORLD = 4;

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * This account's characters on one server.
     *
     * The index ix_character_owner (account_id, server_id, availability) exists to
     * make this cheap, so nobody is ever tempted to widen it.
     *
     * @return list<array<string,mixed>>
     */
    public function listForAccount(string $accountId, string $serverId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM `character`
             WHERE account_id = :aid AND server_id = :sid
             ORDER BY last_played_at DESC, name ASC'
        );

        $statement->execute([':aid' => $accountId, ':sid' => $serverId]);

        return array_map([$this, 'hydrate'], $statement->fetchAll());
    }

    /**
     * One character, but only if this account owns it.
     *
     * Returns null both for "no such character" and "somebody else's character".
     * The caller therefore cannot tell the two apart, which is what stops a
     * response confirming that another player's character exists.
     *
     * @return array<string,mixed>|null
     */
    public function findOwned(string $accountId, string $characterId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM `character` WHERE character_id = :cid AND account_id = :aid'
        );

        $statement->execute([':cid' => $characterId, ':aid' => $accountId]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrate($row);
    }

    /** Whether this account owns this character, asked at the moment it matters. */
    public function ownsCharacter(string $accountId, string $characterId): bool
    {
        $statement = $this->pdo->prepare(
            'SELECT COUNT(*) FROM `character` WHERE character_id = :cid AND account_id = :aid'
        );

        $statement->execute([':cid' => $characterId, ':aid' => $accountId]);

        return ((int) $statement->fetchColumn()) === 1;
    }

    public function countForAccount(string $accountId, string $serverId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT COUNT(*) FROM `character` WHERE account_id = :aid AND server_id = :sid'
        );

        $statement->execute([':aid' => $accountId, ':sid' => $serverId]);

        return (int) $statement->fetchColumn();
    }

    /**
     * Changes a character's availability, guarded by its revision.
     *
     * Used to mark a character in-world on entry and playable again on exit. The
     * revision guard is what stops two concurrent enter-world attempts both
     * believing they claimed the character.
     */
    public function updateAvailability(
        string $characterId,
        int $availability,
        int $expectedRevision
    ): bool {
        $statement = $this->pdo->prepare(
            'UPDATE `character`
             SET availability = :availability, revision = revision + 1, updated_at = NOW(3)
             WHERE character_id = :cid AND revision = :revision'
        );

        $statement->execute([
            ':availability' => $availability,
            ':cid'          => $characterId,
            ':revision'     => $expectedRevision,
        ]);

        return $statement->rowCount() === 1;
    }

    public function markPlayed(string $characterId): void
    {
        $statement = $this->pdo->prepare(
            'UPDATE `character` SET last_played_at = NOW(3), updated_at = NOW(3)
             WHERE character_id = :cid'
        );

        $statement->execute([':cid' => $characterId]);
    }

    /**
     * Shapes a row into the character-select summary.
     *
     * Deliberately thin. It is what a list needs to draw and to let a player
     * choose -- no stats, no inventory, no equipment, no skills. The full state is
     * loaded by the game server after enter-world, once. Sending a player's whole
     * estate to draw a name would be both a performance problem and a second,
     * diverging copy of authoritative state.
     *
     * @param array<string,mixed> $row
     * @return array<string,mixed>
     */
    private function hydrate(array $row): array
    {
        $availability = (int) $row['availability'];

        return [
            'character_id'  => (string) $row['character_id'],
            'name'          => (string) $row['name'],
            'gender'        => (int) $row['gender'],
            'level'         => (int) $row['level'],
            'class_id'      => (string) $row['class_definition_id'],
            'job_id'        => (string) $row['job_definition_id'],
            'map_id'        => (string) $row['map_definition_id'],
            'appearance_id' => (string) $row['appearance_definition_id'],
            'availability'  => $availability,
            'is_playable'   => $availability === self::AVAILABILITY_PLAYABLE,
            'last_played_at' => $row['last_played_at'] === null
                ? null
                : (string) $row['last_played_at'],
            'revision'      => (int) $row['revision'],
        ];
    }
}
