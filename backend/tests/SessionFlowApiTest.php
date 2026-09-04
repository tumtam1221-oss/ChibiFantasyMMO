<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Directory\DirectoryRepository;
use ChibiFantasy\Session\SessionRepository;

/**
 * Server, channel and character selection, and the enter-world handoff.
 *
 * The property under test throughout is that the database is re-read at every
 * step and again at the end: a server that closed, a channel that filled or a
 * character that got locked between choosing and entering must all be caught.
 */
final class SessionFlowApiTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    private string $token;

    protected function setUp(): void
    {
        parent::setUp();

        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeAccount('acc-b', 'bryn@test', self::PASSWORD);

        $this->makeServer('srv-1');
        $this->makeServer('srv-2');

        $this->makeChannel('ch-1a', 'srv-1', pkEnabled: false);
        $this->makeChannel('ch-2a', 'srv-1', pkEnabled: true);
        $this->makeChannel('ch-1b', 'srv-2');

        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');
        $this->makeCharacter('char-a2', 'acc-a', 'srv-1', 'Aren');
        $this->makeCharacter('char-b1', 'acc-b', 'srv-1', 'Bryn');

        $this->token = $this->login('ayla@test', self::PASSWORD);
    }

    // ---- authorisation -------------------------------------------------------

    public function testEveryProtectedEndpointRefusesWithoutAToken(): void
    {
        self::assertSame(401, $this->get('/api/servers')->status);
        self::assertSame(401, $this->get('/api/characters', ['server_id' => 'srv-1'])->status);

        $refused = $this->post('/api/session/select-server', [
            'request_id' => self::newRequestId(),
            'server_id'  => 'srv-1',
        ]);

        self::assertSame(401, $refused->status);
        self::assertSame('missing_token', $refused->body['code']);
    }

    public function testAForgedTokenIsRefused(): void
    {
        $response = $this->get('/api/servers', [], bin2hex(random_bytes(32)));

        self::assertSame(401, $response->status);
        self::assertSame('session_invalid', $response->body['code']);
    }

    public function testARevokedSessionIsRefused(): void
    {
        $sessionId = (string) $this->pdo->query('SELECT session_id FROM account_session')->fetchColumn();

        (new SessionRepository($this->pdo))->revoke($sessionId);

        $response = $this->get('/api/servers', [], $this->token);

        self::assertSame(401, $response->status);
        self::assertSame('session_revoked', $response->body['code']);
    }

    public function testAnExpiredSessionIsRefused(): void
    {
        $this->pdo->exec(
            'UPDATE account_session SET expires_at = NOW(3) - INTERVAL 10 SECOND'
        );

        $response = $this->get('/api/servers', [], $this->token);

        self::assertSame(401, $response->status);
        self::assertSame('session_expired', $response->body['code']);
    }

    // ---- listing -------------------------------------------------------------

    public function testHiddenServersAreAbsentFromTheList(): void
    {
        $this->makeServer('srv-hidden', status: DirectoryRepository::SERVER_HIDDEN);

        $ids = array_column($this->get('/api/servers', [], $this->token)->body['servers'], 'server_id');

        self::assertContains('srv-1', $ids);
        self::assertNotContains('srv-hidden', $ids, 'hidden means absent, not greyed out');
    }

    public function testAnUnknownPopulationIsReportedAsUnknownNotZero(): void
    {
        $servers = $this->get('/api/servers', [], $this->token)->body['servers'];
        $row = current(array_filter($servers, static fn ($s) => $s['server_id'] === 'srv-1'));

        self::assertFalse($row['population_known']);
        self::assertNull($row['population']);
        self::assertTrue($row['selectable'], 'unknown must not read as full');
    }

    public function testPkIsReportedFromTheDatabaseAndDiffersWithinOneServer(): void
    {
        $channels = $this->get('/api/channels', ['server_id' => 'srv-1'], $this->token)
            ->body['channels'];

        $byId = array_column($channels, null, 'channel_id');

        self::assertFalse($byId['ch-1a']['pk_enabled']);
        self::assertTrue($byId['ch-2a']['pk_enabled'], 'two channels of one server differ');
    }

    public function testAnAccountOnlyEverSeesItsOwnCharacters(): void
    {
        $characters = $this->get('/api/characters', ['server_id' => 'srv-1'], $this->token)
            ->body['characters'];

        $ids = array_column($characters, 'character_id');

        self::assertCount(2, $ids);
        self::assertContains('char-a1', $ids);
        self::assertNotContains('char-b1', $ids, 'filtered in SQL, not after sending');
    }

    public function testACharacterRowCarriesNoHeavyState(): void
    {
        $character = $this->get('/api/characters', ['server_id' => 'srv-1'], $this->token)
            ->body['characters'][0];

        foreach (['inventory', 'equipment', 'stats', 'skills', 'quests', 'wallet'] as $absent) {
            self::assertArrayNotHasKey($absent, $character);
        }

        self::assertSame('map.town', $character['map_id'], 'location is a map reference');
    }

    // ---- selection sequence --------------------------------------------------

    public function testTheFullSequenceReachesTheWorld(): void
    {
        $server = $this->select('/api/session/select-server', ['server_id' => 'srv-1']);
        self::assertSame(SessionRepository::SERVER_SELECTED, $server->body['state']);

        $channel = $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a']);
        self::assertSame(SessionRepository::CHANNEL_SELECTED, $channel->body['state']);

        $character = $this->select('/api/session/select-character', ['character_id' => 'char-a1']);
        self::assertSame(SessionRepository::CHARACTER_SELECTED, $character->body['state']);

        $entered = $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a');

        self::assertTrue($entered->isSuccess(), json_encode($entered->body));
        self::assertSame(1, $entered->body['world_entry_state'], 'authorised, not connected');
        self::assertSame('map.town', $entered->body['map_id']);
    }

    public function testSkippingAStageIsRefused(): void
    {
        $channelFirst = $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a']);
        self::assertSame('invalid_transition', $channelFirst->body['code']);

        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);

        $characterNext = $this->select('/api/session/select-character', ['character_id' => 'char-a1']);
        self::assertSame('invalid_transition', $characterNext->body['code']);
    }

    public function testAChannelOfAnotherServerIsRefused(): void
    {
        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);

        $wrong = $this->select('/api/session/select-channel', ['channel_id' => 'ch-1b']);

        self::assertSame('channel_server_mismatch', $wrong->body['code']);
    }

    public function testChoosingAnotherServerClearsWhatWasBeneathIt(): void
    {
        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);
        $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a']);
        $this->select('/api/session/select-character', ['character_id' => 'char-a1']);

        $again = $this->select('/api/session/select-server', ['server_id' => 'srv-2']);

        self::assertSame('srv-2', $again->body['server_id']);
        self::assertNull($again->body['channel_id']);
        self::assertNull($again->body['character_id']);
    }

    public function testAnotherAccountsCharacterIsRefusedAndIndistinguishableFromMissing(): void
    {
        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);
        $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a']);

        $foreign = $this->select('/api/session/select-character', ['character_id' => 'char-b1']);
        $missing = $this->select('/api/session/select-character', ['character_id' => 'char-nope']);

        self::assertSame(404, $foreign->status);
        self::assertSame($missing->body['code'], $foreign->body['code']);
    }

    public function testAMaintenanceServerIsRefused(): void
    {
        $this->makeServer('srv-down', status: DirectoryRepository::SERVER_MAINTENANCE);

        $refused = $this->select('/api/session/select-server', ['server_id' => 'srv-down']);

        self::assertSame(503, $refused->status);
        self::assertSame('server_maintenance', $refused->body['code']);
    }

    public function testAFullServerIsRefused(): void
    {
        $this->makeServer('srv-full', capacity: 10, population: 10);

        $refused = $this->select('/api/session/select-server', ['server_id' => 'srv-full']);

        self::assertSame('server_full', $refused->body['code']);
    }

    public function testAStaleClientIsRefusedByTheServersVersionFloor(): void
    {
        $this->makeServer('srv-new', minClient: '9.0.0');

        $refused = $this->select('/api/session/select-server', ['server_id' => 'srv-new']);

        self::assertSame('version_mismatch', $refused->body['code']);
    }

    // ---- enter world ---------------------------------------------------------

    public function testASpoofedIdentityInTheRequestIsRefused(): void
    {
        $this->reachCharacterSelected();

        self::assertSame(401, $this->enterWorld('acc-b', 'char-a1', 'srv-1', 'ch-1a')->status);
        self::assertSame(403, $this->enterWorld('acc-a', 'char-b1', 'srv-1', 'ch-1a')->status);
        self::assertSame(409, $this->enterWorld('acc-a', 'char-a1', 'srv-2', 'ch-1a')->status);
        self::assertSame(409, $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1b')->status);

        $state = (int) $this->pdo->query('SELECT state FROM account_session')->fetchColumn();

        self::assertSame(
            SessionRepository::CHARACTER_SELECTED,
            $state,
            'a refused entry changes nothing'
        );
    }

    public function testAServerThatClosedAfterSelectionBlocksEntry(): void
    {
        $this->reachCharacterSelected();

        $this->pdo->exec(
            'UPDATE server_definition SET status = ' . DirectoryRepository::SERVER_MAINTENANCE
            . " WHERE server_id = 'srv-1'"
        );

        $refused = $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a');

        self::assertSame('server_maintenance', $refused->body['code']);
    }

    public function testACharacterLockedAfterSelectionBlocksEntry(): void
    {
        $this->reachCharacterSelected();

        $this->pdo->exec("UPDATE `character` SET availability = 3 WHERE character_id = 'char-a1'");

        self::assertSame(
            'character_unavailable',
            $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a')->body['code']
        );
    }

    public function testEnteringTheWorldMarksTheCharacterInWorld(): void
    {
        $this->reachCharacterSelected();
        $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a');

        $availability = (int) $this->pdo
            ->query("SELECT availability FROM `character` WHERE character_id = 'char-a1'")
            ->fetchColumn();

        self::assertSame(4, $availability, 'InWorld');
    }

    public function testEnteringTwiceIsRefused(): void
    {
        $this->reachCharacterSelected();

        self::assertTrue($this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a')->isSuccess());

        $again = $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a');

        self::assertSame('already_in_world', $again->body['code']);
    }

    public function testTheSameEntryRequestEntersOnce(): void
    {
        $this->reachCharacterSelected();

        $requestId = self::newRequestId();

        $first = $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a', $requestId);
        $second = $this->enterWorld('acc-a', 'char-a1', 'srv-1', 'ch-1a', $requestId);

        self::assertTrue($first->isSuccess());
        self::assertTrue($second->isSuccess());
        self::assertTrue($second->body['replayed']);
        self::assertSame($first->body['character_revision'], $second->body['character_revision']);
    }

    // ---- revision ------------------------------------------------------------

    public function testEachAcceptedSelectionAdvancesTheRevisionOnce(): void
    {
        self::assertSame(1, $this->select('/api/session/select-server', ['server_id' => 'srv-1'])->body['revision']);
        self::assertSame(2, $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a'])->body['revision']);
        self::assertSame(3, $this->select('/api/session/select-character', ['character_id' => 'char-a1'])->body['revision']);
    }

    public function testARefusedSelectionAdvancesNoRevision(): void
    {
        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);

        $before = (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn();

        $this->select('/api/session/select-channel', ['channel_id' => 'ch-1b']);
        $this->select('/api/session/select-character', ['character_id' => 'char-b1']);

        $after = (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn();

        self::assertSame($before, $after);
    }

    public function testReadingAListAdvancesNoRevision(): void
    {
        $before = (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn();

        $this->get('/api/servers', [], $this->token);
        $this->get('/api/channels', ['server_id' => 'srv-1'], $this->token);
        $this->get('/api/characters', ['server_id' => 'srv-1'], $this->token);

        self::assertSame(
            $before,
            (int) $this->pdo->query('SELECT revision FROM account_session')->fetchColumn(),
            'a query must not mutate'
        );
    }

    // ---- helpers -------------------------------------------------------------

    /** @param array<string,string> $fields */
    private function select(string $path, array $fields): \ChibiFantasy\Http\Response
    {
        return $this->post(
            $path,
            array_merge(['request_id' => self::newRequestId()], $fields),
            $this->token
        );
    }

    private function reachCharacterSelected(): void
    {
        $this->select('/api/session/select-server', ['server_id' => 'srv-1']);
        $this->select('/api/session/select-channel', ['channel_id' => 'ch-1a']);
        $this->select('/api/session/select-character', ['character_id' => 'char-a1']);
    }

    private function enterWorld(
        string $account,
        string $character,
        string $server,
        string $channel,
        ?string $requestId = null
    ): \ChibiFantasy\Http\Response {
        return $this->post('/api/session/enter-world', [
            'request_id'   => $requestId ?? self::newRequestId(),
            'account_id'   => $account,
            'character_id' => $character,
            'server_id'    => $server,
            'channel_id'   => $channel,
        ], $this->token);
    }
}
