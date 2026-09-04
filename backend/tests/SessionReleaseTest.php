<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Session\SessionRepository;

/**
 * Handing a session back.
 *
 * This endpoint exists because of a failure, and the failure is worth recording.
 * Phase 15 refuses a second live session on the deliberate principle that taking
 * somebody's session away is a policy decision rather than a side effect of signing
 * in again. Nothing could ever give one up, so a player who closed the game was
 * locked out of their own account for the whole session lifetime -- and the first
 * live Unity integration run walked straight into it on its second test.
 *
 * The properties under test are therefore: a session can be ended, ending it frees
 * the character it was holding, and doing it twice is harmless -- because a
 * disconnect callback that fires twice must not corrupt anything.
 */
final class SessionReleaseTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    private string $token;

    protected function setUp(): void
    {
        parent::setUp();

        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeChannel('ch-1a', 'srv-1');
        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');

        $this->token = $this->login('ayla@test', self::PASSWORD);
    }

    private function enterWorld(): void
    {
        foreach ([
            ['/api/session/select-server', 'server_id', 'srv-1'],
            ['/api/session/select-channel', 'channel_id', 'ch-1a'],
            ['/api/session/select-character', 'character_id', 'char-a1'],
        ] as [$path, $field, $value]) {
            $response = $this->post($path, [
                'request_id' => self::newRequestId(),
                $field       => $value,
            ], $this->token);

            self::assertSame(200, $response->status, $path);
        }

        $entered = $this->post('/api/session/enter-world', [
            'request_id'   => self::newRequestId(),
            'account_id'   => 'acc-a',
            'character_id' => 'char-a1',
            'server_id'    => 'srv-1',
            'channel_id'   => 'ch-1a',
        ], $this->token);

        self::assertSame(200, $entered->status);
    }

    private function release(?string $token = null): array
    {
        return $this->post(
            '/api/session/release',
            ['request_id' => self::newRequestId()],
            $token ?? $this->token
        )->body;
    }

    private function stateOf(string $sessionId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT state FROM account_session WHERE session_id = :id'
        );

        $statement->execute([':id' => $sessionId]);

        return (int) $statement->fetchColumn();
    }

    private function availabilityOf(string $characterId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT availability FROM `character` WHERE character_id = :id'
        );

        $statement->execute([':id' => $characterId]);

        return (int) $statement->fetchColumn();
    }

    private function sessionId(): string
    {
        return (string) $this->pdo->query('SELECT session_id FROM account_session')->fetchColumn();
    }

    // ---- ending a session ----------------------------------------------------

    public function testReleasingEndsTheSession(): void
    {
        $sessionId = $this->sessionId();

        $body = $this->release();

        self::assertTrue($body['session_ended']);
        self::assertSame(SessionRepository::REVOKED, $this->stateOf($sessionId));
    }

    public function testAReleasedTokenNoLongerAuthorisesAnything(): void
    {
        $this->release();

        $response = $this->get('/api/servers', [], $this->token);

        self::assertSame(401, $response->status);
        self::assertSame('session_revoked', $response->body['code']);
    }

    public function testReleasingLetsTheAccountSignInAgain(): void
    {
        // The whole reason this endpoint exists: without it, this second login is
        // refused for the next twenty-four hours.
        $this->release();

        $again = $this->login('ayla@test', self::PASSWORD);

        self::assertNotSame('', $again);
        self::assertNotSame($this->token, $again, 'a new session gets a new token');
    }

    // ---- the character it was holding ------------------------------------------

    public function testReleasingASessionThatReachedTheWorldFreesItsCharacter(): void
    {
        $this->enterWorld();

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-a1'),
            'precondition: entering the world claimed the character'
        );

        $body = $this->release();

        self::assertTrue($body['character_released']);
        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a1'),
            'a character stranded InWorld with no session is permanently unplayable'
        );
    }

    public function testReleasingASessionThatNeverEnteredTheWorldTouchesNoCharacter(): void
    {
        $this->post('/api/session/select-server', [
            'request_id' => self::newRequestId(),
            'server_id'  => 'srv-1',
        ], $this->token);

        $body = $this->release();

        self::assertTrue($body['session_ended']);
        self::assertFalse($body['character_released'], 'there was nothing to release');
        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a1')
        );
    }

    public function testReleasingDoesNotTouchACharacterThatWasNeverThisSessions(): void
    {
        $this->makeCharacter('char-other', 'acc-a', 'srv-1', 'Other',
            CharacterRepository::AVAILABILITY_IN_WORLD);

        $this->enterWorld();
        $this->release();

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-other'),
            'releasing one session must not free another session character'
        );
    }

    // ---- idempotence ------------------------------------------------------------

    public function testReleasingTwiceIsHarmless(): void
    {
        $this->enterWorld();

        $first = $this->release();
        $second = $this->release();

        self::assertTrue($first['session_ended']);
        self::assertFalse($second['session_ended'], 'there was nothing left to end');
        self::assertFalse($second['character_released']);

        // And critically, the second call did not re-free a character that a *new*
        // session might by then have claimed.
        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a1')
        );
    }

    public function testReleasingWithNoTokenIsNotAnError(): void
    {
        // A client whose token was already gone still wants to say it is leaving. That
        // is the outcome it wanted, so reporting a failure would make it retry
        // something that has already happened.
        $response = $this->post('/api/session/release', [
            'request_id' => self::newRequestId(),
        ]);

        self::assertSame(200, $response->status);
        self::assertFalse($response->body['session_ended']);
    }

    public function testReleasingWithAForgedTokenChangesNothing(): void
    {
        $sessionId = $this->sessionId();

        $response = $this->post('/api/session/release', [
            'request_id' => self::newRequestId(),
        ], bin2hex(random_bytes(32)));

        self::assertSame(200, $response->status);
        self::assertFalse($response->body['session_ended']);

        // The real session is untouched: a forged token cannot end somebody else's.
        self::assertSame(SessionRepository::AUTHENTICATED, $this->stateOf($sessionId));
    }

    public function testAnotherAccountsTokenCannotEndThisSession(): void
    {
        $this->makeAccount('acc-b', 'bryn@test', self::PASSWORD);
        $mine = $this->sessionId();

        $theirToken = $this->login('bryn@test', self::PASSWORD);

        $this->release($theirToken);

        // Theirs ended; mine did not. A release acts on the session behind the token
        // presented, and there is no session id parameter to aim it at another.
        self::assertSame(SessionRepository::AUTHENTICATED, $this->stateOf($mine));
    }
}
