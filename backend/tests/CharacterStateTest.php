<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Character\CharacterStateRepository;
use ChibiFantasy\Database\Connection;

/**
 * Loading and saving the character state a world server actually needs.
 *
 * Two properties carry the weight here. The first is that ownership is a WHERE
 * clause: another account's character is not fetched-and-rejected, it is simply not
 * found. The second is that a stale save is refused whole rather than partially
 * applied -- a world server that was replaced must not overwrite the one that
 * replaced it, and a five-table write that failed halfway would leave a character
 * that never existed.
 */
final class CharacterStateTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    private CharacterStateRepository $states;
    private string $token;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeAccount('acc-b', 'bryn@test', self::PASSWORD);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1a', 'srv-1');

        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');
        $this->makeCharacter('char-b1', 'acc-b', 'srv-1', 'Bryn');

        $this->token = $this->login('ayla@test', self::PASSWORD);
    }

    /** @return array<string,mixed> */
    private function sampleState(int $level = 12): array
    {
        return [
            'level'          => $level,
            'experience'     => 4500,
            'current_health' => 87,
            'current_mana'   => 33,
            'class_id'       => 'class.novice',
            'job_id'         => 'job.none',
            'map_id'         => 'map.town',
            'spawn_id'       => 'spawn.town.plaza',
            'stats'          => [
                ['stat_id' => 'stat.strength', 'value' => 14],
                ['stat_id' => 'stat.agility', 'value' => 9],
                ['stat_id' => 'stat.penalty', 'value' => -3],
            ],
            'appearance'     => [
                ['slot' => 1, 'option_id' => 'face.round'],
                ['slot' => 3, 'option_id' => 'hair.short'],
            ],
            'skills'         => [
                ['skill_id' => 'skill.slash', 'level' => 3],
                ['skill_id' => 'skill.guard', 'level' => 1],
            ],
            'revisions'      => [
                'identity' => 1, 'class' => 2, 'appearance' => 3,
                'progression' => 4, 'stats' => 5, 'skills' => 6,
            ],
        ];
    }

    // ---- loading --------------------------------------------------------------

    public function testAFreshCharacterLoadsWithZeroedStateRatherThanNothing(): void
    {
        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertNotNull($loaded);
        self::assertSame('char-a1', $loaded['character_id']);
        self::assertSame(0, $loaded['experience']);
        self::assertSame([], $loaded['stats']);
        self::assertSame(0, $loaded['revisions']['save'], 'never saved is revision zero');
    }

    public function testAnotherAccountsCharacterIsNotFound(): void
    {
        // Not "found and refused" -- the query is scoped by account, so there is
        // nothing to refuse.
        self::assertNull($this->states->load('acc-a', 'char-b1'));
        self::assertNull($this->states->load('acc-b', 'char-a1'));
    }

    public function testAnUnknownCharacterIsNotFound(): void
    {
        self::assertNull($this->states->load('acc-a', 'no-such-character'));
    }

    // ---- saving ----------------------------------------------------------------

    public function testAFirstSaveWritesEveryAggregate(): void
    {
        $result = $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        self::assertTrue($result['ok']);
        self::assertSame(1, $result['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame(4500, $loaded['experience']);
        self::assertSame(87, $loaded['current_health']);
        self::assertSame(33, $loaded['current_mana']);
        self::assertSame('map.town', $loaded['map_id']);
        self::assertSame('spawn.town.plaza', $loaded['spawn_id']);
        self::assertCount(3, $loaded['stats']);
        self::assertCount(2, $loaded['appearance']);
        self::assertCount(2, $loaded['skills']);
    }

    public function testANegativeStatSurvivesTheRoundTrip(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        $penalty = null;

        foreach ($loaded['stats'] as $stat) {
            if ($stat['stat_id'] === 'stat.penalty') {
                $penalty = $stat['value'];
            }
        }

        // An unsigned column would have turned this into 4294967293.
        self::assertSame(-3, $penalty, 'a debuff is a negative value, not a huge positive one');
    }

    public function testEachAggregateRevisionIsStoredSeparately(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        $revisions = $this->states->load('acc-a', 'char-a1')['revisions'];

        // Collapsing these into one would make every save look like every aggregate
        // changed, defeating the per-aggregate concurrency the domain already has.
        self::assertSame(1, $revisions['identity']);
        self::assertSame(2, $revisions['class']);
        self::assertSame(3, $revisions['appearance']);
        self::assertSame(4, $revisions['progression']);
        self::assertSame(5, $revisions['stats']);
        self::assertSame(6, $revisions['skills']);
    }

    public function testASaveCannotWriteAnotherAccountsCharacter(): void
    {
        $result = $this->states->save('acc-a', 'char-b1', $this->sampleState(), null);

        self::assertFalse($result['ok']);
        self::assertSame('character_not_owned', $result['reason']);

        // And nothing was written.
        self::assertSame(0, $this->states->load('acc-b', 'char-b1')['experience']);
    }

    public function testStatsAreReplacedWholesaleSoARemovedStatDisappears(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        $second = $this->sampleState();
        $second['stats'] = [['stat_id' => 'stat.strength', 'value' => 20]];

        $this->states->save('acc-a', 'char-a1', $second, 1);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['stats'], 'a stat nobody can see is worse than a wrong one');
        self::assertSame(20, $loaded['stats'][0]['value']);
    }

    // ---- no stale overwrite ------------------------------------------------------

    public function testASaveWithAStaleRevisionIsRefused(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), 1);

        // A world server that still thinks the revision is 1.
        $stale = $this->sampleState();
        $stale['experience'] = 999999;

        $result = $this->states->save('acc-a', 'char-a1', $stale, 1);

        self::assertFalse($result['ok']);
        self::assertSame('stale_revision', $result['reason']);
        self::assertNotSame(999999, $this->states->load('acc-a', 'char-a1')['experience'],
            'an hour of somebody else\'s progress must not disappear');
    }

    public function testASecondFirstSaveIsRefused(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        // Claiming "never been saved" is not a way past the revision check.
        $result = $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        self::assertFalse($result['ok']);
        self::assertSame('stale_revision', $result['reason']);
    }

    public function testARefusedSaveWritesNothingAtAll(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        $stale = $this->sampleState();
        $stale['stats'] = [['stat_id' => 'stat.injected', 'value' => 99]];
        $stale['skills'] = [['skill_id' => 'skill.injected', 'level' => 9]];

        $this->states->save('acc-a', 'char-a1', $stale, 99);

        $loaded = $this->states->load('acc-a', 'char-a1');

        // A five-table write that half-applied would leave a character that never
        // existed. The transaction is what stops it.
        self::assertCount(3, $loaded['stats']);
        self::assertCount(2, $loaded['skills']);

        foreach ($loaded['stats'] as $stat) {
            self::assertNotSame('stat.injected', $stat['stat_id']);
        }
    }

    public function testTheRevisionAdvancesByOnePerAcceptedSave(): void
    {
        self::assertSame(1, $this->states->save('acc-a', 'char-a1', $this->sampleState(), null)['save_revision']);
        self::assertSame(2, $this->states->save('acc-a', 'char-a1', $this->sampleState(), 1)['save_revision']);
        self::assertSame(3, $this->states->save('acc-a', 'char-a1', $this->sampleState(), 2)['save_revision']);
    }

    // ---- concurrency, on a genuine second connection --------------------------------

    public function testTwoWorldServersSavingTheSameCharacterProduceExactlyOneWinner(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        // Both load revision 1 and both intend to write. A single connection would
        // see its own uncommitted work and pass for the wrong reason.
        $other = new CharacterStateRepository(Connection::forTests());

        $mine = $this->sampleState();
        $mine['experience'] = 1000;

        $theirs = $this->sampleState();
        $theirs['experience'] = 2000;

        $first = $this->states->save('acc-a', 'char-a1', $mine, 1);
        $second = $other->save('acc-a', 'char-a1', $theirs, 1);

        self::assertTrue($first['ok']);
        self::assertFalse($second['ok'], 'the second writer lost, as it must');
        self::assertSame('stale_revision', $second['reason']);
        self::assertSame(1000, $this->states->load('acc-a', 'char-a1')['experience']);
    }

    // ---- through the API ---------------------------------------------------------------

    private function reachTheWorld(): void
    {
        foreach ([
            ['/api/session/select-server', 'server_id', 'srv-1'],
            ['/api/session/select-channel', 'channel_id', 'ch-1a'],
            ['/api/session/select-character', 'character_id', 'char-a1'],
        ] as [$path, $field, $value]) {
            $this->post($path, ['request_id' => self::newRequestId(), $field => $value],
                $this->token);
        }

        $this->post('/api/session/enter-world', [
            'request_id'   => self::newRequestId(),
            'account_id'   => 'acc-a',
            'character_id' => 'char-a1',
            'server_id'    => 'srv-1',
            'channel_id'   => 'ch-1a',
        ], $this->token);
    }

    public function testTheEndpointRefusesASessionThatHasNotReachedTheWorld(): void
    {
        // Authenticated only. Full character state is the world's business.
        $response = $this->get('/api/character/state', [], $this->token);

        // 409, matching every other invalid_transition in this API: the category is
        // "the world is not in a state where this makes sense", not "your request
        // was malformed".
        self::assertSame(409, $response->status);
        self::assertSame('invalid_transition', $response->body['code']);
    }

    public function testTheEndpointLoadsTheSessionsOwnCharacter(): void
    {
        $this->reachTheWorld();
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), null);

        $response = $this->get('/api/character/state', [], $this->token);

        self::assertSame(200, $response->status);
        self::assertSame('char-a1', $response->body['character_id']);
        self::assertSame(4500, $response->body['experience']);
        self::assertCount(3, $response->body['stats']);
    }

    public function testThereIsNoCharacterParameterToForge(): void
    {
        $this->reachTheWorld();

        // A query parameter naming somebody else's character changes nothing: the
        // character comes from the session and there is nowhere to say otherwise.
        $response = $this->get('/api/character/state', ['character_id' => 'char-b1'],
            $this->token);

        self::assertSame(200, $response->status);
        self::assertSame('char-a1', $response->body['character_id']);
    }

    public function testSavingThroughTheApiRefusesAStaleRevision(): void
    {
        $this->reachTheWorld();

        $first = $this->post('/api/character/state', [
            'request_id' => self::newRequestId(),
            'state'      => $this->sampleState(),
        ], $this->token);

        self::assertSame(200, $first->status);
        self::assertSame(1, $first->body['save_revision']);

        $stale = $this->post('/api/character/state', [
            'request_id'    => self::newRequestId(),
            'save_revision' => 0,
            'state'         => $this->sampleState(),
        ], $this->token);

        self::assertSame(409, $stale->status);
        self::assertSame('stale_revision', $stale->body['code']);
    }

    public function testSavingRequiresASession(): void
    {
        $response = $this->post('/api/character/state', [
            'request_id' => self::newRequestId(),
            'state'      => $this->sampleState(),
        ]);

        self::assertSame(401, $response->status);
    }

    public function testAnAccountIdInThePayloadIsIgnored(): void
    {
        $this->reachTheWorld();

        $state = $this->sampleState();
        $state['account_id'] = 'acc-b';
        $state['character_id'] = 'char-b1';

        $response = $this->post('/api/character/state', [
            'request_id' => self::newRequestId(),
            'account_id' => 'acc-b',
            'state'      => $state,
        ], $this->token);

        self::assertSame(200, $response->status);
        self::assertSame('char-a1', $response->body['character_id'],
            'the session decides whose character this is, not the payload');

        // Bryn's character is untouched.
        self::assertSame(0, $this->states->load('acc-b', 'char-b1')['experience']);
    }
}
