<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Http\Api;
use ChibiFantasy\Session\SessionRepository;

/**
 * What a world server hands back when it starts, having died without handing anything back.
 *
 * **The bug.** A world server releases everyone in it when it stops cleanly and nobody at
 * all when it does not. The character stays `availability = InWorld`, the player is
 * refused at the door with `character_unavailable`, and nothing in the system ever
 * revisits that column -- so the refusal is permanent. Expiring the session does not
 * help: expiry moves the session's own state and leaves the character exactly where it
 * was. Restarting the world did not help either, which is what made it look like data
 * loss rather than a missing line of code.
 *
 * **The two ways to be stranded, both tested here.** A session left Active still holds
 * its character legitimately, so freeing the character means ending the session. A
 * character can also outlive its session entirely -- the state an operator produces by
 * clearing sessions by hand after a crash -- and then nothing owns it and nothing would
 * ever find it.
 *
 * **The property that must not break.** Reclaiming is destructive by nature: it throws
 * sessions out of a world. Every test below that proves it frees something is matched by
 * one proving it leaves a live player alone, because a reclaim that evicts the wrong
 * person is worse than the bug it fixes.
 */
final class WorldReclaimTest extends BackendTestCase
{
    private const Key = 'a-deployment-key-that-is-not-in-git';
    private const Password = 'a-password-invented-here-only';

    protected function setUp(): void
    {
        parent::setUp();

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1a', 'srv-1');
        $this->makeChannel('ch-1b', 'srv-1');

        $this->makeAccount('acc-a', 'ayla@test', self::Password);
        $this->makeAccount('acc-b', 'brik@test', self::Password);

        $this->makeCharacter('char-a', 'acc-a', 'srv-1', 'Ayla');
        $this->makeCharacter('char-b', 'acc-b', 'srv-1', 'Brik');

        // Handed to the API directly rather than through the environment, so these tests
        // say the same thing whatever the machine running them happens to have configured.
        $this->api = new Api($this->pdo, self::Key);
    }

    // ---- the bug, and that it is fixed --------------------------------------------------

    public function testACharacterLeftInACrashedWorldIsHandedBack(): void
    {
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-a'),
            'entering the world must mark the character as being in it'
        );

        // No release: this is what a killed server leaves behind.
        $response = $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(200, $response->status, json_encode($response->body));
        self::assertSame(1, $response->body['sessions_released']);

        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a'),
            'the character must be playable again'
        );
    }

    public function testTheGhostSessionIsEndedTooRatherThanLeftHolding(): void
    {
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(
            SessionRepository::REVOKED,
            $this->stateOfSessionFor('acc-a'),
            'a session left holding a character it cannot reach locks the account out too'
        );
    }

    public function testAPlayerCanSignBackInAfterAReclaim(): void
    {
        // The whole point, stated as the thing the player actually experiences.
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        $this->reclaim('srv-1', 'ch-1a');

        $token = $this->login('ayla@test', self::Password);

        $selected = $this->post('/api/session/select-server',
            ['request_id' => self::newRequestId(), 'server_id' => 'srv-1'], $token);

        self::assertTrue($selected->isSuccess(), json_encode($selected->body));

        $this->post('/api/session/select-channel',
            ['request_id' => self::newRequestId(), 'channel_id' => 'ch-1a'], $token);

        $character = $this->post('/api/session/select-character',
            ['request_id' => self::newRequestId(), 'character_id' => 'char-a'], $token);

        self::assertTrue($character->isSuccess(),
            'the character must be selectable again: ' . json_encode($character->body));
    }

    public function testACharacterWhoseSessionAlreadyEndedIsStillFreed(): void
    {
        // The state an operator reaches by clearing sessions by hand after a crash: the
        // session is gone and the character is still marked in a world that is not there.
        // Nothing owns it, so nothing but this would ever find it.
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        $this->pdo->exec(
            'UPDATE account_session SET state = ' . SessionRepository::EXPIRED
        );

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-a'),
            'expiring a session must not have freed the character -- that is the bug'
        );

        $response = $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(0, $response->body['sessions_released'],
            'there is no live session left to release');
        self::assertSame(1, $response->body['characters_released']);

        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a')
        );
    }

    // ---- and that it evicts nobody it should not ----------------------------------------

    public function testAnotherChannelOfTheSameServerIsUntouched(): void
    {
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');
        $this->enterWorld('acc-b', 'char-b', 'ch-1b', 'brik@test');

        $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-b'),
            'restarting one channel must not throw the other channel out of the world'
        );

        self::assertSame(
            SessionRepository::ENTERING_WORLD,
            $this->stateOfSessionFor('acc-b'),
            'the other channel\'s session must be left exactly as it was'
        );
    }

    public function testTheCharacterSweepSpareAnyoneHeldByALiveSession(): void
    {
        // The second pass looks at characters rather than sessions and is scoped to the
        // server, not the channel -- because a character belongs to a server. What keeps
        // it from freeing a player on another channel is that a live session holds them.
        $this->enterWorld('acc-b', 'char-b', 'ch-1b', 'brik@test');

        $response = $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(0, $response->body['characters_released']);

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-b')
        );
    }

    public function testAQuietWorldReclaimsNothing(): void
    {
        $response = $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(200, $response->status);
        self::assertSame(0, $response->body['sessions_released']);
        self::assertSame(0, $response->body['characters_released']);
    }

    public function testReclaimingTwiceFindsNothingTheSecondTime(): void
    {
        // Every server start calls this, so the ordinary case is the second one.
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        $this->reclaim('srv-1', 'ch-1a');

        $again = $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(0, $again->body['sessions_released']);
        self::assertSame(0, $again->body['characters_released']);

        self::assertSame(
            CharacterRepository::AVAILABILITY_PLAYABLE,
            $this->availabilityOf('char-a')
        );
    }

    public function testACharacterThatIsNotInAWorldIsLeftAlone(): void
    {
        // Pending deletion and locked are somebody's deliberate decisions, and a server
        // starting up is not a reason to undo them.
        $this->makeCharacter('char-locked', 'acc-a', 'srv-1', 'Locked',
            CharacterRepository::AVAILABILITY_LOCKED);

        $this->reclaim('srv-1', 'ch-1a');

        self::assertSame(
            CharacterRepository::AVAILABILITY_LOCKED,
            $this->availabilityOf('char-locked')
        );
    }

    // ---- who may do it ------------------------------------------------------------------

    public function testReclaimingNeedsTheDeploymentKey(): void
    {
        $this->enterWorld('acc-a', 'char-a', 'ch-1a', 'ayla@test');

        $response = $this->post('/api/world/reclaim',
            ['server_id' => 'srv-1', 'channel_id' => 'ch-1a']);

        self::assertSame(401, $response->status);

        self::assertSame(
            CharacterRepository::AVAILABILITY_IN_WORLD,
            $this->availabilityOf('char-a'),
            'an unauthorised call must change nothing'
        );
    }

    public function testAWrongKeyIsRefused(): void
    {
        $response = $this->post('/api/world/reclaim',
            ['server_id' => 'srv-1', 'channel_id' => 'ch-1a'], 'not-the-key');

        self::assertSame(401, $response->status);
    }

    public function testAPlayersSessionTokenIsNotEnough(): void
    {
        // This endpoint throws a channel out of the world. A signed-in player holding a
        // valid token is still not the deployment, and must not be able to use it on
        // anybody -- including themselves.
        $token = $this->login('brik@test', self::Password);

        $response = $this->post('/api/world/reclaim',
            ['server_id' => 'srv-1', 'channel_id' => 'ch-1a'], $token);

        self::assertSame(401, $response->status);
    }

    public function testWithNoKeyConfiguredReclaimIsRefusedRatherThanOpened(): void
    {
        $this->api = new Api($this->pdo, '');

        $response = $this->post('/api/world/reclaim',
            ['server_id' => 'srv-1', 'channel_id' => 'ch-1a'], self::Key);

        self::assertSame(403, $response->status,
            'a forgotten key must close the door, not open it');
    }

    public function testAWorldMustBeNamed(): void
    {
        $response = $this->post('/api/world/reclaim',
            ['server_id' => '', 'channel_id' => ''], self::Key);

        self::assertSame(400, $response->status);
    }

    // ---- helpers -------------------------------------------------------------------------

    private function reclaim(string $server, string $channel): \ChibiFantasy\Http\Response
    {
        return $this->post('/api/world/reclaim',
            ['server_id' => $server, 'channel_id' => $channel], self::Key);
    }

    /** Signs in and walks a character all the way into a world. */
    private function enterWorld(
        string $account,
        string $character,
        string $channel,
        string $login
    ): void {
        $token = $this->login($login, self::Password);

        $this->post('/api/session/select-server',
            ['request_id' => self::newRequestId(), 'server_id' => 'srv-1'], $token);

        $this->post('/api/session/select-channel',
            ['request_id' => self::newRequestId(), 'channel_id' => $channel], $token);

        $this->post('/api/session/select-character',
            ['request_id' => self::newRequestId(), 'character_id' => $character], $token);

        $entered = $this->post('/api/session/enter-world', [
            'request_id'   => self::newRequestId(),
            'account_id'   => $account,
            'character_id' => $character,
            'server_id'    => 'srv-1',
            'channel_id'   => $channel,
        ], $token);

        self::assertTrue($entered->isSuccess(),
            'enter-world failed: ' . json_encode($entered->body));
    }

    private function availabilityOf(string $characterId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT availability FROM `character` WHERE character_id = :cid'
        );

        $statement->execute([':cid' => $characterId]);

        return (int) $statement->fetchColumn();
    }

    private function stateOfSessionFor(string $accountId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT state FROM account_session WHERE account_id = :aid
             ORDER BY issued_at DESC LIMIT 1'
        );

        $statement->execute([':aid' => $accountId]);

        return (int) $statement->fetchColumn();
    }
}
