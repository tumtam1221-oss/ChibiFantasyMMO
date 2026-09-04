<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Database\Connection;
use ChibiFantasy\Session\IdempotencyStore;
use ChibiFantasy\Support\Env;
use PDO;

/**
 * Properties that only a real database can demonstrate.
 *
 * Every test here opens a *second* connection, because that is the only way to
 * observe what MySQL does when two callers race. A single connection sees its own
 * uncommitted work and would make every one of these pass for the wrong reason.
 */
final class ConcurrencyTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    /** A genuinely separate connection, so the two can contend. */
    private function secondConnection(): PDO
    {
        return new PDO(
            sprintf(
                'mysql:host=%s;port=%d;dbname=%s;charset=utf8mb4',
                Env::get('DB_HOST', '127.0.0.1'),
                Env::getInt('DB_PORT', 3306),
                Env::require('DB_TEST_DATABASE')
            ),
            Env::require('DB_USERNAME'),
            Env::get('DB_PASSWORD', ''),
            [
                PDO::ATTR_ERRMODE          => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_EMULATE_PREPARES => false,
            ]
        );
    }

    // ---- optimistic concurrency ---------------------------------------------

    public function testAStaleWriterLosesRatherThanOverwriting(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeServer('srv-2');
        $this->makeChannel('ch-1', 'srv-1');

        $token = $this->login('ayla@test', self::PASSWORD);

        // Two callers read the session at the same revision.
        $sessions = new \ChibiFantasy\Session\SessionRepository($this->pdo);
        $sessionId = (string) $this->pdo->query('SELECT session_id FROM account_session')->fetchColumn();
        $revision = (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn();

        $first = $sessions->applyTransition($sessionId, $revision, 2, ['selected_server_id' => 'srv-1']);
        $second = $sessions->applyTransition($sessionId, $revision, 2, ['selected_server_id' => 'srv-2']);

        self::assertTrue($first, 'the first writer wins');
        self::assertFalse($second, 'the second is refused rather than silently overwriting');

        self::assertSame(
            'srv-1',
            $this->pdo->query('SELECT selected_server_id FROM account_session')->fetchColumn()
        );

        self::assertSame(
            $revision + 1,
            (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn(),
            'exactly one increment, not two'
        );
    }

    // ---- idempotency under a race -------------------------------------------

    public function testTwoConcurrentRetriesRecordOneOutcome(): void
    {
        $requestId = self::newRequestId();

        $a = new IdempotencyStore($this->pdo);
        $b = new IdempotencyStore($this->secondConnection());

        // Both find nothing, both do the work, both try to record it.
        self::assertNull($a->find($requestId, 'test'));
        self::assertNull($b->find($requestId, 'test'));

        $aWon = $a->remember($requestId, 'test', 'acc-a', ['who' => 'a']);
        $bWon = $b->remember($requestId, 'test', 'acc-a', ['who' => 'b']);

        self::assertTrue($aWon xor $bWon, 'exactly one insert survives the unique index');

        self::assertSame(
            1,
            (int) $this->pdo->query('SELECT COUNT(*) FROM request_result')->fetchColumn()
        );
    }

    public function testTheLoserOfAnIdempotencyRaceReturnsTheWinnersAnswer(): void
    {
        $requestId = self::newRequestId();

        $winner = new IdempotencyStore($this->pdo);
        $winner->remember($requestId, 'test', 'acc-a', ['who' => 'winner']);

        $loser = new IdempotencyStore($this->secondConnection());

        $outcome = $loser->once($requestId, 'test', 'acc-a', static fn (): array => [
            'recordable' => true,
            'response'   => ['who' => 'loser'],
        ]);

        self::assertTrue($outcome['replayed']);
        self::assertSame('winner', $outcome['response']['who'], 'the winner is authoritative');
    }

    // ---- row locking ---------------------------------------------------------

    public function testOnlyOneOfTwoConcurrentEntriesClaimsTheCharacter(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');

        $characters = new \ChibiFantasy\Character\CharacterRepository($this->pdo);
        $revision = (int) $this->pdo
            ->query("SELECT revision FROM `character` WHERE character_id = 'char-1'")
            ->fetchColumn();

        // Two callers both read revision N and both try to claim the character.
        $first = $characters->updateAvailability('char-1', 4, $revision);
        $second = $characters->updateAvailability('char-1', 4, $revision);

        self::assertTrue($first);
        self::assertFalse($second, 'the revision guard stops a double claim');
    }

    public function testSelectForUpdateBlocksASecondWriter(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');

        $other = $this->secondConnection();

        // Hold a lock on the character row.
        $this->pdo->beginTransaction();
        $this->pdo->query("SELECT character_id FROM `character` WHERE character_id = 'char-1' FOR UPDATE")
            ->fetchAll();

        // A second connection asking for the same lock must not get it immediately.
        $other->exec('SET SESSION innodb_lock_wait_timeout = 1');
        $other->beginTransaction();

        $blocked = false;

        try {
            $other->query("SELECT character_id FROM `character` WHERE character_id = 'char-1' FOR UPDATE")
                ->fetchAll();
        } catch (\PDOException $e) {
            $blocked = true;
        }

        $other->rollBack();
        $this->pdo->rollBack();

        self::assertTrue($blocked, 'FOR UPDATE must actually exclude a second writer');
    }

    // ---- transaction rollback ------------------------------------------------

    public function testAFailureMidTransactionLeavesNothingBehind(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');

        $before = (int) $this->pdo->query('SELECT COUNT(*) FROM `character`')->fetchColumn();

        try {
            Connection::transactional($this->pdo, function (PDO $pdo): void {
                $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');
                $this->makeCharacter('char-2', 'acc-a', 'srv-1', 'Aren');

                // Something fails after two rows were written.
                throw new \RuntimeException('simulated failure halfway');
            });

            self::fail('the exception should have propagated');
        } catch (\RuntimeException $e) {
            self::assertSame('simulated failure halfway', $e->getMessage());
        }

        self::assertSame(
            $before,
            (int) $this->pdo->query('SELECT COUNT(*) FROM `character`')->fetchColumn(),
            'no partial write survived'
        );
    }

    public function testASuccessfulTransactionCommitsEverything(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');

        Connection::transactional($this->pdo, function (PDO $pdo): void {
            $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');
            $this->makeCharacter('char-2', 'acc-a', 'srv-1', 'Aren');
        });

        self::assertSame(
            2,
            (int) $this->pdo->query('SELECT COUNT(*) FROM `character`')->fetchColumn()
        );
    }

    public function testUncommittedWorkIsInvisibleToAnotherConnection(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');

        $other = $this->secondConnection();

        $this->pdo->beginTransaction();
        $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');

        $seenByOther = (int) $other
            ->query('SELECT COUNT(*) FROM `character`')
            ->fetchColumn();

        $this->pdo->rollBack();

        self::assertSame(0, $seenByOther, 'isolation: an open transaction is private');
    }

    // ---- database-level invariants ------------------------------------------

    public function testTheDatabaseRefusesTwoGuildsWithTheSameName(): void
    {
        $insert = $this->pdo->prepare(
            'INSERT INTO guild (guild_id, name, leader_character_id, revision, created_at)
             VALUES (:id, :name, :leader, 0, NOW(3))'
        );

        $insert->execute([':id' => 'g1', ':name' => 'Wanderers', ':leader' => 'c1']);

        $this->expectException(\PDOException::class);

        // Different case, and the collation is case-insensitive: two guilds a
        // player could not tell apart are one guild too many.
        $insert->execute([':id' => 'g2', ':name' => 'wanderers', ':leader' => 'c2']);
    }

    public function testTheDatabaseRefusesAnItemInTwoContainerSlots(): void
    {
        $this->pdo->exec(
            "INSERT INTO item_instance (instance_id, definition_id, owner_id, quantity, revision, created_at, updated_at)
             VALUES ('i1', 'item.potion', 'own-a', 1, 0, NOW(3), NOW(3))"
        );

        $this->pdo->exec(
            "INSERT INTO item_container (container_id, owner_id, kind, capacity, revision, created_at, updated_at)
             VALUES ('bag-a', 'own-a', 0, 10, 0, NOW(3), NOW(3)),
                    ('bag-b', 'own-b', 0, 10, 0, NOW(3), NOW(3))"
        );

        $this->pdo->exec("INSERT INTO container_slot (container_id, slot_index, instance_id) VALUES ('bag-a', 0, 'i1')");

        $this->expectException(\PDOException::class);

        // The anti-duplication invariant the whole economy rests on.
        $this->pdo->exec("INSERT INTO container_slot (container_id, slot_index, instance_id) VALUES ('bag-b', 0, 'i1')");
    }

    public function testTheDatabaseRefusesANegativeBalance(): void
    {
        $this->pdo->exec(
            "INSERT INTO currency_definition (currency_id, name_key, maximum_balance, enabled, revision, created_at, updated_at)
             VALUES ('currency.gold', 'gold', 0, 1, 0, NOW(3), NOW(3))"
        );

        $this->pdo->exec(
            "INSERT INTO character_currency (owner_id, currency_id, balance, revision, updated_at)
             VALUES ('own-a', 'currency.gold', 100, 0, NOW(3))"
        );

        $this->expectException(\PDOException::class);

        $this->pdo->exec(
            "UPDATE character_currency SET balance = -1 WHERE owner_id = 'own-a'"
        );
    }

    public function testTheDatabaseRefusesACharacterWithoutAnAccount(): void
    {
        $this->makeServer('srv-1');

        $this->expectException(\PDOException::class);

        // The foreign key makes "every character has an owner" a guarantee rather
        // than an application convention.
        $this->makeCharacter('char-orphan', 'no-such-account', 'srv-1', 'Ghost');
    }

    public function testTheDatabaseRefusesASocketedCardInTwoPieces(): void
    {
        foreach (['sword-a', 'sword-b', 'card-1'] as $id) {
            $this->pdo->exec(
                "INSERT INTO item_instance (instance_id, definition_id, owner_id, quantity, revision, created_at, updated_at)
                 VALUES ('$id', 'def.$id', 'own-a', 1, 0, NOW(3), NOW(3))"
            );
        }

        $this->pdo->exec("INSERT INTO equipment_instance (instance_id) VALUES ('sword-a'), ('sword-b')");

        $this->pdo->exec(
            "INSERT INTO equipment_card_socket (instance_id, socket_index, card_definition_id, card_instance_id)
             VALUES ('sword-a', 0, 'card.stat', 'card-1')"
        );

        $this->expectException(\PDOException::class);

        $this->pdo->exec(
            "INSERT INTO equipment_card_socket (instance_id, socket_index, card_definition_id, card_instance_id)
             VALUES ('sword-b', 0, 'card.stat', 'card-1')"
        );
    }

    public function testTheDatabaseRefusesTwoActiveFruitsForOneOwner(): void
    {
        $insert = $this->pdo->prepare(
            'INSERT INTO character_devil_fruit (owner_id, fruit_definition_id, revision, activated_at)
             VALUES (:owner, :fruit, 0, NOW(3))'
        );

        $insert->execute([':owner' => 'own-a', ':fruit' => 'fruit.darkness']);

        $this->expectException(\PDOException::class);

        // The primary key on owner_id IS the one-fruit rule.
        $insert->execute([':owner' => 'own-a', ':fruit' => 'fruit.light']);
    }

    public function testTheDatabaseRefusesOneCharacterInTwoParties(): void
    {
        $this->pdo->exec(
            "INSERT INTO party (party_id, leader_character_id, loot_policy, revision, created_at)
             VALUES ('p1', 'c1', 0, 0, NOW(3)), ('p2', 'c2', 0, 0, NOW(3))"
        );

        $insert = $this->pdo->prepare(
            'INSERT INTO party_member (party_id, character_id, join_order, joined_at)
             VALUES (:party, :character, 0, NOW(3))'
        );

        $insert->execute([':party' => 'p1', ':character' => 'c9']);

        $this->expectException(\PDOException::class);

        $insert->execute([':party' => 'p2', ':character' => 'c9']);
    }
}
