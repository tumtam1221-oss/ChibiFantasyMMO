<?php

declare(strict_types=1);

namespace ChibiFantasy\Party;

use PDO;

/**
 * The durable half of a party.
 *
 * **Only what outlives a session.** Who leads, who belongs, in what order they joined
 * and which loot policy the party chose. Not a connection, not a position, not a health
 * bar, and not one byte of the gameplay state character persistence already owns -- a
 * party row that carried a level would be a second place to change it, and the two would
 * drift the first time somebody levelled up.
 *
 * **One character, one party, enforced by the database.** `party_member` has a UNIQUE on
 * `character_id` across the whole table, so two concurrent joins into different parties
 * cannot both commit no matter how the callers race. Phase 13 enforced the same rule with
 * an in-memory directory; this is that rule where it cannot be bypassed.
 *
 * **Disband is a tombstone, not a delete.** `disbanded_at` is stamped and the memberships
 * are removed, so a party that ended stays distinguishable from one that never existed --
 * which is what lets a reconnect answer "you are in no party" instead of silently
 * rebuilding the one somebody deliberately left.
 */
final class PartyRepository
{
    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The party this character belongs to, or null when they belong to none.
     *
     * Members come back in join order, because round-robin loot and successor
     * selection both depend on that sequence and neither may change across a reload.
     *
     * @return array{party_id:string,leader_character_id:string,loot_policy:int,
     *               round_robin_cursor:int,revision:int,
     *               members:list<array{character_id:string,join_order:int}>}|null
     */
    public function loadByCharacter(string $characterId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT p.party_id, p.leader_character_id, p.loot_policy,
                    p.round_robin_cursor, p.revision
             FROM party p
             INNER JOIN party_member m ON m.party_id = p.party_id
             WHERE m.character_id = :cid AND p.disbanded_at IS NULL'
        );

        $statement->execute([':cid' => $characterId]);

        $row = $statement->fetch();

        if ($row === false) {
            return null;
        }

        return [
            'party_id'            => (string) $row['party_id'],
            'leader_character_id' => (string) $row['leader_character_id'],
            'loot_policy'         => (int) $row['loot_policy'],
            'round_robin_cursor'  => (int) $row['round_robin_cursor'],
            'revision'            => (int) $row['revision'],
            'members'             => $this->loadMembers((string) $row['party_id']),
        ];
    }

    /** @return list<array{character_id:string,join_order:int}> */
    private function loadMembers(string $partyId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT character_id, join_order FROM party_member
             WHERE party_id = :pid
             ORDER BY join_order ASC, character_id ASC'
        );

        $statement->execute([':pid' => $partyId]);

        $members = [];

        foreach ($statement as $row) {
            $members[] = [
                'character_id' => (string) $row['character_id'],
                'join_order'   => (int) $row['join_order'],
            ];
        }

        return $members;
    }

    /**
     * Writes a whole party, membership and all, in one transaction.
     *
     * The member list replaces whatever was stored: a join, a leave and a kick are all
     * "the party now looks like this", which is one code path instead of three that
     * could disagree. Rows are removed before they are inserted so a member moving
     * position cannot collide with themselves on the way past.
     *
     * A party whose member list arrives empty is disbanded rather than stored as an
     * empty party, because a party with nobody in it is not a state the game has a
     * meaning for.
     *
     * @param list<string> $memberIds in join order, leader included
     * @param int $roundRobinCursor index into $memberIds; must address a member
     * @return array{ok:bool,reason?:string,revision?:int}
     */
    public function save(string $partyId, string $leaderCharacterId, int $lootPolicy,
        array $memberIds, ?int $expectedRevision = null, int $roundRobinCursor = 0): array
    {
        if ($partyId === '' || $leaderCharacterId === '') {
            return ['ok' => false, 'reason' => 'invalid_party'];
        }

        if ($lootPolicy < 0 || $lootPolicy > 2) {
            // Refused rather than clamped: a policy nobody authored must not silently
            // become Personal, because the party would then loot by a rule they never
            // chose and nothing would say so.
            return ['ok' => false, 'reason' => 'invalid_loot_policy'];
        }

        if ($memberIds === []) {
            return $this->disband($partyId);
        }

        if (!in_array($leaderCharacterId, $memberIds, true)) {
            return ['ok' => false, 'reason' => 'leader_not_a_member'];
        }

        if ($roundRobinCursor < 0 || $roundRobinCursor >= count($memberIds)) {
            // Refused for the same reason an unknown policy is: a cursor that does not
            // address a member names nobody's turn, and quietly taking it modulo the
            // party size would hand the next drop to whoever happened to land there.
            return ['ok' => false, 'reason' => 'invalid_round_robin_cursor'];
        }

        $this->pdo->beginTransaction();

        try {
            $current = $this->lockParty($partyId);

            if ($current !== null && $expectedRevision !== null
                && $current !== $expectedRevision) {
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'stale_revision'];
            }

            $next = ($current ?? 0) + 1;

            if ($current === null) {
                $this->pdo->prepare(
                    'INSERT INTO party
                        (party_id, leader_character_id, loot_policy, round_robin_cursor,
                         revision, created_at)
                     VALUES (:pid, :leader, :policy, :cursor, :rev, NOW(3))'
                )->execute([
                    ':pid'    => $partyId,
                    ':leader' => $leaderCharacterId,
                    ':policy' => $lootPolicy,
                    ':cursor' => $roundRobinCursor,
                    ':rev'    => $next,
                ]);
            } else {
                $this->pdo->prepare(
                    'UPDATE party
                     SET leader_character_id = :leader, loot_policy = :policy,
                         round_robin_cursor = :cursor, revision = :rev,
                         disbanded_at = NULL
                     WHERE party_id = :pid'
                )->execute([
                    ':pid'    => $partyId,
                    ':leader' => $leaderCharacterId,
                    ':policy' => $lootPolicy,
                    ':cursor' => $roundRobinCursor,
                    ':rev'    => $next,
                ]);
            }

            $this->pdo->prepare('DELETE FROM party_member WHERE party_id = :pid')
                ->execute([':pid' => $partyId]);

            $insert = $this->pdo->prepare(
                'INSERT INTO party_member (party_id, character_id, join_order, joined_at)
                 VALUES (:pid, :cid, :ord, NOW(3))'
            );

            foreach (array_values($memberIds) as $order => $characterId) {
                $insert->execute([
                    ':pid' => $partyId,
                    ':cid' => $characterId,
                    ':ord' => $order,
                ]);
            }

            $this->pdo->commit();

            return ['ok' => true, 'revision' => $next];
        } catch (\PDOException $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            // 23000 is the integrity-constraint family, which here means the UNIQUE on
            // character_id refused a member who is already in another party. That is a
            // rule being enforced, not a fault, so it is reported rather than thrown.
            if ($e->getCode() === '23000') {
                return ['ok' => false, 'reason' => 'character_already_in_a_party'];
            }

            throw $e;
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * Ends a party: every membership removed, the row tombstoned.
     *
     * In one transaction, because a party whose members were deleted but whose row
     * survived would be loadable, empty, and impossible to leave.
     *
     * @return array{ok:bool,reason?:string}
     */
    public function disband(string $partyId): array
    {
        if ($partyId === '') {
            return ['ok' => false, 'reason' => 'invalid_party'];
        }

        $this->pdo->beginTransaction();

        try {
            $this->pdo->prepare('DELETE FROM party_member WHERE party_id = :pid')
                ->execute([':pid' => $partyId]);

            $this->pdo->prepare(
                'UPDATE party SET disbanded_at = NOW(3), revision = revision + 1
                 WHERE party_id = :pid'
            )->execute([':pid' => $partyId]);

            $this->pdo->commit();

            return ['ok' => true];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /** The party's current revision, locked for update, or null when it does not exist. */
    private function lockParty(string $partyId): ?int
    {
        $statement = $this->pdo->prepare(
            'SELECT revision FROM party WHERE party_id = :pid FOR UPDATE'
        );

        $statement->execute([':pid' => $partyId]);

        $row = $statement->fetch();

        return $row === false ? null : (int) $row['revision'];
    }
}
