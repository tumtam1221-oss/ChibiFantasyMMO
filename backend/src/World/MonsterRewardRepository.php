<?php

declare(strict_types=1);

namespace ChibiFantasy\World;

use PDO;

/**
 * A monster defeat's decision, kept until every part of it has been handed over.
 *
 * **Written before anything is paid.** The drop roll, the rare chance and the experience
 * split all happen once, in the server, at the moment the monster dies. Until this row
 * exists that decision lives only in memory, and a world that stops there loses it -- along
 * with a one in ten million roll that cannot honestly be run a second time. So the decision
 * is recorded first and delivered afterwards.
 *
 * **Idempotent on the defeat, not on the monster.** `defeat_id` is the monster's runtime
 * instance id and carries a UNIQUE, so a world that wrote the row, failed to hear the
 * answer and tried again gets the row it already has rather than a second reward. A
 * respawned monster is a different instance, so farming a boss twice honestly produces two
 * rewards.
 *
 * **Progress is recorded per side effect.** Experience is per recipient, loot is per entry,
 * and the party cursor is its own flag, because each of those can land while the others
 * have not. Inferring one from another is how a crash between two of them turns into a
 * double payment.
 *
 * This is a recovery record. It decides nothing: the server decides, and this remembers.
 */
final class MonsterRewardRepository
{
    /** Pending, and therefore still owed to somebody. */
    public const STATE_PENDING = 0;

    /** Everything this defeat owed has been handed over. */
    public const STATE_COMPLETE = 1;

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * Records a decided defeat, or hands back the one already recorded for it.
     *
     * The envelope and its children go in together: an envelope whose experience rows
     * failed to insert would pay nobody and look complete, and loot rows without an
     * envelope would never be found.
     *
     * @param list<array{character_id:string,experience:int}> $experience
     * @param list<array{item_definition_id:string,quantity:int,rarity_definition_id?:string}> $loot
     * @return array{ok:bool,reason?:string,reward_id?:string,existing?:bool,revision?:int}
     */
    public function record(array $envelope, array $experience, array $loot): array
    {
        foreach (['reward_id', 'defeat_id', 'server_id', 'channel_id',
                  'monster_definition_id', 'map_definition_id',
                  'killer_character_id'] as $required) {
            if (trim((string) ($envelope[$required] ?? '')) === '') {
                return ['ok' => false, 'reason' => 'invalid_reward'];
            }
        }

        $cursor = $envelope['party_cursor'] ?? null;

        if ($cursor !== null && (int) $cursor < 0) {
            return ['ok' => false, 'reason' => 'invalid_party_cursor'];
        }

        foreach ($loot as $entry) {
            if (trim((string) ($entry['item_definition_id'] ?? '')) === '') {
                return ['ok' => false, 'reason' => 'invalid_loot_entry'];
            }

            if ((int) ($entry['quantity'] ?? 0) <= 0) {
                return ['ok' => false, 'reason' => 'invalid_loot_quantity'];
            }
        }

        foreach ($experience as $grant) {
            if (trim((string) ($grant['character_id'] ?? '')) === '') {
                return ['ok' => false, 'reason' => 'invalid_experience_grant'];
            }

            if ((int) ($grant['experience'] ?? 0) < 0) {
                return ['ok' => false, 'reason' => 'invalid_experience_grant'];
            }
        }

        // A pile the world says it produced but listed nothing for is a decision that
        // contradicts itself, and recovering it would republish an empty object.
        if (trim((string) ($envelope['loot_id'] ?? '')) !== '' && $loot === []) {
            return ['ok' => false, 'reason' => 'invalid_loot_entry'];
        }

        $existing = $this->findByDefeat((string) $envelope['defeat_id']);

        if ($existing !== null) {
            // Already decided. The caller is retrying a save it never heard the answer to.
            return [
                'ok'        => true,
                'existing'  => true,
                'reward_id' => (string) $existing['reward_id'],
                'revision'  => (int) $existing['revision'],
            ];
        }

        $this->pdo->beginTransaction();

        try {
            $this->pdo->prepare(
                'INSERT INTO monster_reward
                    (reward_id, defeat_id, server_id, channel_id, monster_definition_id,
                     map_definition_id, killer_character_id, loot_id, loot_policy,
                     claimant_character_id, position_x, position_y, position_z,
                     party_id, party_cursor, state, revision, created_at, updated_at)
                 VALUES
                    (:reward, :defeat, :server, :channel, :monster,
                     :map, :killer, :loot, :policy,
                     :claimant, :x, :y, :z,
                     :party, :cursor, :state, 1, NOW(3), NOW(3))'
            )->execute([
                ':reward'   => (string) $envelope['reward_id'],
                ':defeat'   => (string) $envelope['defeat_id'],
                ':server'   => (string) $envelope['server_id'],
                ':channel'  => (string) $envelope['channel_id'],
                ':monster'  => (string) $envelope['monster_definition_id'],
                ':map'      => (string) $envelope['map_definition_id'],
                ':killer'   => (string) $envelope['killer_character_id'],
                ':loot'     => (string) ($envelope['loot_id'] ?? ''),
                ':policy'   => (int) ($envelope['loot_policy'] ?? 0),
                ':claimant' => (string) ($envelope['claimant_character_id'] ?? ''),
                ':x'        => (float) ($envelope['position_x'] ?? 0),
                ':y'        => (float) ($envelope['position_y'] ?? 0),
                ':z'        => (float) ($envelope['position_z'] ?? 0),
                ':party'    => (string) ($envelope['party_id'] ?? ''),
                ':cursor'   => $cursor === null ? null : (int) $cursor,
                ':state'    => self::STATE_PENDING,
            ]);

            $grant = $this->pdo->prepare(
                'INSERT INTO monster_reward_experience
                    (reward_id, character_id, experience)
                 VALUES (:reward, :cid, :xp)'
            );

            foreach ($experience as $row) {
                $grant->execute([
                    ':reward' => (string) $envelope['reward_id'],
                    ':cid'    => (string) $row['character_id'],
                    ':xp'     => (int) $row['experience'],
                ]);
            }

            $item = $this->pdo->prepare(
                'INSERT INTO monster_reward_loot
                    (reward_id, entry_index, item_definition_id, quantity,
                     rarity_definition_id)
                 VALUES (:reward, :idx, :item, :qty, :rarity)'
            );

            foreach (array_values($loot) as $index => $row) {
                $item->execute([
                    ':reward' => (string) $envelope['reward_id'],
                    ':idx'    => $index,
                    ':item'   => (string) $row['item_definition_id'],
                    ':qty'    => (int) $row['quantity'],
                    ':rarity' => (string) ($row['rarity_definition_id'] ?? ''),
                ]);
            }

            $this->pdo->commit();

            return ['ok' => true, 'existing' => false,
                'reward_id' => (string) $envelope['reward_id'], 'revision' => 1];
        } catch (\PDOException $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            if ($e->getCode() === '23000') {
                // Two worlds raced to record the same defeat, or the same world did.
                // Whoever lost reads the row the winner wrote; nobody gets a second reward.
                $winner = $this->findByDefeat((string) $envelope['defeat_id']);

                if ($winner !== null) {
                    return ['ok' => true, 'existing' => true,
                        'reward_id' => (string) $winner['reward_id'],
                        'revision'  => (int) $winner['revision']];
                }

                return ['ok' => false, 'reason' => 'duplicate_reward'];
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
     * Everything this world still owes, oldest first.
     *
     * Scoped to one server and channel: a pending reward belongs to the world the monster
     * died in, and another channel running the same map has no business delivering it.
     *
     * @return list<array<string,mixed>>
     */
    public function pending(string $serverId, string $channelId, int $limit = 200): array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM monster_reward
             WHERE server_id = :server AND channel_id = :channel AND state = :state
             ORDER BY created_at ASC, reward_id ASC
             LIMIT ' . max(1, min(1000, $limit))
        );

        $statement->execute([
            ':server'  => $serverId,
            ':channel' => $channelId,
            ':state'   => self::STATE_PENDING,
        ]);

        $rewards = [];

        foreach ($statement as $row) {
            $rewards[] = $this->hydrate($row);
        }

        return $rewards;
    }

    /** One reward, whatever its state, or null. */
    public function find(string $rewardId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM monster_reward WHERE reward_id = :id'
        );

        $statement->execute([':id' => $rewardId]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrate($row);
    }

    /**
     * Records that part of a reward has been handed over.
     *
     * One entry point rather than one endpoint per column: every step is "this side effect
     * landed", and they all have to be checked against the same revision so two recovering
     * workers cannot both believe they were first.
     *
     * @param list<string> $experienceDelivered character ids now paid
     * @param list<array{entry_index:int,character_id:string}> $lootClaimed
     * @return array{ok:bool,reason?:string,revision?:int,state?:int}
     */
    public function progress(string $rewardId, int $expectedRevision,
        array $experienceDelivered = [], array $lootClaimed = [],
        ?bool $cursorCommitted = null, ?bool $lootPublished = null,
        bool $complete = false): array
    {
        if ($rewardId === '') {
            return ['ok' => false, 'reason' => 'invalid_reward'];
        }

        $this->pdo->beginTransaction();

        try {
            $current = $this->lock($rewardId);

            if ($current === null) {
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'unknown_reward'];
            }

            if ((int) $current['revision'] !== $expectedRevision) {
                // Somebody else moved this reward on. Theirs stands; this one re-reads.
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'stale_revision'];
            }

            if ((int) $current['state'] === self::STATE_COMPLETE) {
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'already_complete'];
            }

            // Only ever stamped, never cleared: a delivery that happened cannot un-happen,
            // and a second attempt to mark it must change nothing.
            $paid = $this->pdo->prepare(
                'UPDATE monster_reward_experience SET delivered_at = NOW(3)
                 WHERE reward_id = :reward AND character_id = :cid
                   AND delivered_at IS NULL'
            );

            foreach ($experienceDelivered as $characterId) {
                $paid->execute([':reward' => $rewardId, ':cid' => (string) $characterId]);
            }

            $taken = $this->pdo->prepare(
                'UPDATE monster_reward_loot
                 SET claimed_at = NOW(3), claimed_by_character_id = :cid
                 WHERE reward_id = :reward AND entry_index = :idx AND claimed_at IS NULL'
            );

            foreach ($lootClaimed as $entry) {
                $taken->execute([
                    ':reward' => $rewardId,
                    ':idx'    => (int) ($entry['entry_index'] ?? -1),
                    ':cid'    => (string) ($entry['character_id'] ?? ''),
                ]);
            }

            $next = $expectedRevision + 1;

            $this->pdo->prepare(
                'UPDATE monster_reward
                 SET cursor_committed = :cursor, loot_published = :published,
                     state = :state, revision = :rev, updated_at = NOW(3),
                     completed_at = :completed
                 WHERE reward_id = :reward'
            )->execute([
                ':reward'    => $rewardId,
                ':cursor'    => $cursorCommitted === null
                    ? (int) $current['cursor_committed'] : ($cursorCommitted ? 1 : 0),
                ':published' => $lootPublished === null
                    ? (int) $current['loot_published'] : ($lootPublished ? 1 : 0),
                ':state'     => $complete ? self::STATE_COMPLETE : self::STATE_PENDING,
                ':rev'       => $next,
                ':completed' => $complete ? date('Y-m-d H:i:s.v') : null,
            ]);

            $this->pdo->commit();

            return ['ok' => true, 'revision' => $next,
                'state' => $complete ? self::STATE_COMPLETE : self::STATE_PENDING];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /** The reward's revision, locked, or null when there is no such reward. */
    private function lock(string $rewardId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT revision, state, cursor_committed, loot_published
             FROM monster_reward WHERE reward_id = :id FOR UPDATE'
        );

        $statement->execute([':id' => $rewardId]);

        $row = $statement->fetch();

        return $row === false ? null : $row;
    }

    private function findByDefeat(string $defeatId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT reward_id, revision FROM monster_reward WHERE defeat_id = :id'
        );

        $statement->execute([':id' => $defeatId]);

        $row = $statement->fetch();

        return $row === false ? null : $row;
    }

    /** @return array<string,mixed> */
    private function hydrate(array $row): array
    {
        $rewardId = (string) $row['reward_id'];

        return [
            'reward_id'             => $rewardId,
            'defeat_id'             => (string) $row['defeat_id'],
            'server_id'             => (string) $row['server_id'],
            'channel_id'            => (string) $row['channel_id'],
            'monster_definition_id' => (string) $row['monster_definition_id'],
            'map_definition_id'     => (string) $row['map_definition_id'],
            'killer_character_id'   => (string) $row['killer_character_id'],
            'loot_id'               => (string) $row['loot_id'],
            'loot_policy'           => (int) $row['loot_policy'],
            'claimant_character_id' => (string) $row['claimant_character_id'],
            'position_x'            => (float) $row['position_x'],
            'position_y'            => (float) $row['position_y'],
            'position_z'            => (float) $row['position_z'],
            'party_id'              => (string) $row['party_id'],
            'party_cursor'          => $row['party_cursor'] === null
                ? null : (int) $row['party_cursor'],
            'cursor_committed'      => (int) $row['cursor_committed'] === 1,
            'loot_published'        => (int) $row['loot_published'] === 1,
            'state'                 => (int) $row['state'],
            'revision'              => (int) $row['revision'],
            'experience'            => $this->experienceOf($rewardId),
            'loot'                  => $this->lootOf($rewardId),
        ];
    }

    /** @return list<array{character_id:string,experience:int,delivered:bool}> */
    private function experienceOf(string $rewardId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT character_id, experience, delivered_at
             FROM monster_reward_experience WHERE reward_id = :id
             ORDER BY character_id ASC'
        );

        $statement->execute([':id' => $rewardId]);

        $grants = [];

        foreach ($statement as $row) {
            $grants[] = [
                'character_id' => (string) $row['character_id'],
                'experience'   => (int) $row['experience'],
                'delivered'    => $row['delivered_at'] !== null,
            ];
        }

        return $grants;
    }

    /**
     * @return list<array{entry_index:int,item_definition_id:string,quantity:int,
     *                    rarity_definition_id:string,claimed:bool,claimed_by:string}>
     */
    private function lootOf(string $rewardId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT entry_index, item_definition_id, quantity, rarity_definition_id,
                    claimed_at, claimed_by_character_id
             FROM monster_reward_loot WHERE reward_id = :id
             ORDER BY entry_index ASC'
        );

        $statement->execute([':id' => $rewardId]);

        $entries = [];

        foreach ($statement as $row) {
            $entries[] = [
                'entry_index'          => (int) $row['entry_index'],
                'item_definition_id'   => (string) $row['item_definition_id'],
                'quantity'             => (int) $row['quantity'],
                'rarity_definition_id' => (string) $row['rarity_definition_id'],
                'claimed'              => $row['claimed_at'] !== null,
                'claimed_by'           => (string) $row['claimed_by_character_id'],
            ];
        }

        return $entries;
    }
}
