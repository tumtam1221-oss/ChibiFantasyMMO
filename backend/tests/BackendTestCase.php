<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Auth\AccountRepository;
use ChibiFantasy\Database\Connection;
use ChibiFantasy\Http\Api;
use ChibiFantasy\Http\Request;
use ChibiFantasy\Http\Response;
use PDO;
use PHPUnit\Framework\TestCase;

/**
 * Shared fixture: a truncated test database and a request helper.
 *
 * Tests run against the real MySQL instance rather than a mock. The properties
 * this phase exists to prove -- row locking, transaction rollback, unique
 * constraints under concurrency -- are properties of the database, and a mock
 * would assert only that the mock behaves as the author imagined.
 *
 * `Connection::forTests()` refuses when DB_TEST_DATABASE is unset or equal to
 * DB_DATABASE, so the truncation below cannot reach development data.
 */
abstract class BackendTestCase extends TestCase
{
    protected PDO $pdo;
    protected Api $api;

    /** Tables emptied before each test, children first so foreign keys stay happy. */
    private const TABLES = [
        'request_result',
        'login_attempt',
        'account_session_token',
        'account_session',
        'item_transaction_entry',
        'economy_transaction_entry',
        'economy_transaction',
        'container_slot',
        'equipment_card_socket',
        'equipment_enchant',
        'equipment_instance',
        'character_equipment',
        'character_devil_fruit',
        'pet_instance',
        'item_container',
        'item_instance',
        'character_currency',
        'trade_offer_currency',
        'trade_offer_item',
        'trade_session',
        'player_shop_listing',
        'player_shop',
        'party_invite',
        'party_member',
        'party',
        'guild_invite',
        'guild_member',
        'guild_rank',
        'guild',
        'character',
        'server_channel',
        'server_definition',
        'account_credential',
        'account',
        'currency_definition',
    ];

    protected function setUp(): void
    {
        parent::setUp();

        Connection::reset();
        $this->pdo = Connection::forTests();

        $this->pdo->exec('SET FOREIGN_KEY_CHECKS = 0');

        foreach (self::TABLES as $table) {
            $this->pdo->exec('TRUNCATE TABLE `' . $table . '`');
        }

        $this->pdo->exec('SET FOREIGN_KEY_CHECKS = 1');

        $this->api = new Api($this->pdo);
    }

    // ---- fixtures ------------------------------------------------------------

    /**
     * Creates an account with a password invented at call time.
     *
     * The password is a parameter with no default, so no test can accidentally
     * rely on a shared well-known secret and no credential is written down here.
     */
    protected function makeAccount(
        string $accountId,
        string $login,
        string $password,
        int $status = AccountRepository::STATUS_ACTIVE
    ): void {
        (new AccountRepository($this->pdo))
            ->create($accountId, 'Test ' . $accountId, $login, $password, $status);
    }

    protected function makeServer(
        string $serverId,
        int $status = 1,
        bool $enabled = true,
        int $capacity = 100,
        ?int $population = null,
        string $minClient = '1.0.0',
        string $protocol = '1.0.0'
    ): void {
        $statement = $this->pdo->prepare(
            'INSERT INTO server_definition
                (server_id, name_key, region, status, enabled, capacity, cached_population,
                 min_client_version, latest_client_version, required_protocol_version,
                 min_content_version, latest_content_version, content_is_advisory,
                 revision, created_at, updated_at)
             VALUES (:id, :name, :region, :status, :enabled, :capacity, :population,
                     :minc, :latestc, :proto, :minct, :latestct, 0, 0, NOW(3), NOW(3))'
        );

        $statement->execute([
            ':id'         => $serverId,
            ':name'       => $serverId . '.name',
            ':region'     => 'test',
            ':status'     => $status,
            ':enabled'    => $enabled ? 1 : 0,
            ':capacity'   => $capacity,
            ':population' => $population,
            ':minc'       => $minClient,
            ':latestc'    => $minClient,
            ':proto'      => $protocol,
            ':minct'      => '1.0.0',
            ':latestct'   => '1.0.0',
        ]);
    }

    protected function makeChannel(
        string $channelId,
        string $serverId,
        int $status = 1,
        bool $enabled = true,
        int $capacity = 50,
        ?int $population = null,
        bool $pkEnabled = false
    ): void {
        $statement = $this->pdo->prepare(
            'INSERT INTO server_channel
                (channel_id, server_id, name_key, status, enabled, capacity,
                 cached_population, pk_enabled, revision, created_at, updated_at)
             VALUES (:id, :server, :name, :status, :enabled, :capacity, :population, :pk,
                     0, NOW(3), NOW(3))'
        );

        $statement->execute([
            ':id'         => $channelId,
            ':server'     => $serverId,
            ':name'       => $channelId . '.name',
            ':status'     => $status,
            ':enabled'    => $enabled ? 1 : 0,
            ':capacity'   => $capacity,
            ':population' => $population,
            ':pk'         => $pkEnabled ? 1 : 0,
        ]);
    }

    protected function makeCharacter(
        string $characterId,
        string $accountId,
        string $serverId,
        string $name,
        int $availability = 1,
        string $mapId = 'map.town'
    ): void {
        $statement = $this->pdo->prepare(
            'INSERT INTO `character`
                (character_id, account_id, server_id, name, gender, level,
                 class_definition_id, job_definition_id, map_definition_id,
                 appearance_definition_id, availability, revision, created_at, updated_at)
             VALUES (:cid, :aid, :sid, :name, 2, 10, :class, "", :map, "", :avail,
                     0, NOW(3), NOW(3))'
        );

        $statement->execute([
            ':cid'   => $characterId,
            ':aid'   => $accountId,
            ':sid'   => $serverId,
            ':name'  => $name,
            ':class' => 'class.novice',
            ':map'   => $mapId,
            ':avail' => $availability,
        ]);
    }

    // ---- request helpers -----------------------------------------------------

    /** @param array<string,mixed> $body */
    protected function post(string $path, array $body, ?string $token = null): Response
    {
        return $this->api->handle(new Request(
            'POST',
            $path,
            $body,
            $token === null ? [] : ['authorization' => 'Bearer ' . $token],
            [],
            '10.0.0.1'
        ));
    }

    /** @param array<string,string> $query */
    protected function get(string $path, array $query = [], ?string $token = null): Response
    {
        return $this->api->handle(new Request(
            'GET',
            $path,
            [],
            $token === null ? [] : ['authorization' => 'Bearer ' . $token],
            $query,
            '10.0.0.1'
        ));
    }

    /** Signs in and returns the bearer token. */
    protected function login(string $login, string $password, string $client = '1.0.0'): string
    {
        $response = $this->post('/api/auth/login', [
            'request_id'       => 'req-' . bin2hex(random_bytes(8)),
            'login_identifier' => $login,
            'password'         => $password,
            'versions'         => ['client' => $client, 'protocol' => '1.0.0', 'content' => '1.0.0'],
        ]);

        self::assertTrue(
            $response->isSuccess(),
            'login failed: ' . json_encode($response->body)
        );

        return (string) $response->body['token'];
    }

    protected static function newRequestId(): string
    {
        return 'req-' . bin2hex(random_bytes(8));
    }
}
