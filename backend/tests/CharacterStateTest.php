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

    // ---- inventory ------------------------------------------------------------

    /**
     * @param list<array{instance_id:string,item_id:string,quantity:int,slot:int}> $items
     * @return array<string,mixed>
     */
    private function stateWithBag(array $items, int $capacity = 30): array
    {
        $state = $this->sampleState();
        $state['items'] = $items;
        $state['inventory_capacity'] = $capacity;

        return $state;
    }

    public function testABagSurvivesTheRoundTripWithSlotsIntact(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 250, 'slot' => 0],
            ['instance_id' => 'item-2', 'item_id' => 'item.relic', 'quantity' => 1, 'slot' => 3],
        ]), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(2, $loaded['items']);
        self::assertSame(30, $loaded['inventory_capacity']);

        // Ordered by slot, because a player arranges their bag and expects to find it.
        self::assertSame(0, $loaded['items'][0]['slot']);
        self::assertSame('item-1', $loaded['items'][0]['instance_id']);
        self::assertSame(250, $loaded['items'][0]['quantity']);
        self::assertSame(3, $loaded['items'][1]['slot']);
        self::assertSame('item.relic', $loaded['items'][1]['item_id']);
    }

    public function testSavingTwiceLeavesOneCopyOfAnItemAndNotTwo(): void
    {
        $bag = [['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0]];

        $first = $this->states->save('acc-a', 'char-a1', $this->stateWithBag($bag), null);
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag($bag),
            $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);

        // The unique key on container_slot.instance_id is what makes this a database
        // guarantee rather than an application convention.
        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM container_slot WHERE instance_id = 'item-1'")
            ->fetchColumn();

        self::assertSame(1, $rows);
    }

    public function testAnItemThatMovedSlotIsNotDuplicated(): void
    {
        $first = $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
        ]), null);

        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 9, 'slot' => 7],
        ]), $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);
        self::assertSame(7, $loaded['items'][0]['slot']);
        self::assertSame(9, $loaded['items'][0]['quantity']);
    }

    public function testASaveCarryingNoCapacityLeavesAnExistingBagAlone(): void
    {
        $first = $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
        ]), null);

        // A world server composed without an item registry sends no inventory at all.
        // That must not be read as "this character's bag is now empty".
        $this->states->save('acc-a', 'char-a1', $this->sampleState(), $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items'],
            'a misconfigured server must not delete a player\'s belongings');
    }

    public function testAnItemBeyondTheBagIsRefusedRatherThanPlacedSomewhereElse(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
            ['instance_id' => 'item-2', 'item_id' => 'item.relic', 'quantity' => 1, 'slot' => 99],
        ], 10), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);
        self::assertSame('item-1', $loaded['items'][0]['instance_id']);
    }

    public function testAMalformedItemRowIsSkippedAndTheRestOfTheBagStillSaves(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => '', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
            ['instance_id' => 'item-2', 'item_id' => '', 'quantity' => 5, 'slot' => 1],
            ['instance_id' => 'item-3', 'item_id' => 'item.hide', 'quantity' => 2, 'slot' => 2],
        ]), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);
        self::assertSame('item-3', $loaded['items'][0]['instance_id']);
    }

    public function testARefusedSaveWritesNoItemEither(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
        ]), null);

        // A stale revision is refused whole: the item from this attempt must not survive.
        $refused = $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-2', 'item_id' => 'item.relic', 'quantity' => 1, 'slot' => 1],
        ]), 99);

        self::assertFalse($refused['ok']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);
        self::assertSame('item-1', $loaded['items'][0]['instance_id']);
    }

    public function testAnItemCannotBeWrittenIntoAnotherAccountsCharacter(): void
    {
        $refused = $this->states->save('acc-b', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
        ]), null);

        self::assertFalse($refused['ok']);

        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM item_instance WHERE instance_id = 'item-1'")
            ->fetchColumn();

        self::assertSame(0, $rows);
    }

    public function testTheItemBelongsToTheAccountTheSessionResolvedTo(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->stateWithBag([
            ['instance_id' => 'item-1', 'item_id' => 'item.coin', 'quantity' => 5, 'slot' => 0],
        ]), null);

        $owner = (string) $this->pdo
            ->query("SELECT owner_id FROM item_instance WHERE instance_id = 'item-1'")
            ->fetchColumn();

        self::assertSame('acc-a', $owner);
    }

    // ---- equipment ------------------------------------------------------------

    /**
     * A worn piece, with the per-copy state that no definition can supply.
     *
     * @return array<string,mixed>
     */
    private function wornSword(int $slot = 6, int $enhancement = 7): array
    {
        return [
            'instance_id'      => 'sword-1',
            'item_id'          => 'item.sword',
            'quantity'         => 1,
            'slot'             => -1,
            'equipment_slot'   => $slot,
            'enhancement_level' => $enhancement,
            'rarity_id'        => 'rarity.epic',
            'enchants'         => [
                ['stone_id' => 'stone.fire', 'socket' => 0, 'rank' => 3],
                ['stone_id' => 'stone.ice', 'socket' => 1, 'rank' => 1],
            ],
            'cards'            => [],
        ];
    }

    public function testAWornPieceSurvivesTheRoundTripWithEverythingOnIt(): void
    {
        $state = $this->sampleState();
        $state['inventory_capacity'] = 30;
        $state['items'] = [$this->wornSword()];

        $this->states->save('acc-a', 'char-a1', $state, null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items']);

        $worn = $loaded['items'][0];

        self::assertSame('sword-1', $worn['instance_id']);
        self::assertSame(6, $worn['equipment_slot'], 'still in the slot it was worn in');
        self::assertSame(-1, $worn['slot'], 'a worn piece is in no bag');
        self::assertSame(7, $worn['enhancement_level']);
        self::assertSame('rarity.epic', $worn['rarity_id']);
        self::assertCount(2, $worn['enchants']);
        self::assertSame('stone.fire', $worn['enchants'][0]['stone_id']);
        self::assertSame(3, $worn['enchants'][0]['rank']);
    }

    public function testAnOrdinaryItemCarriesNoEquipmentState(): void
    {
        $state = $this->sampleState();
        $state['inventory_capacity'] = 30;
        $state['items'] = [
            ['instance_id' => 'coin-1', 'item_id' => 'item.coin', 'quantity' => 9, 'slot' => 0],
        ];

        $this->states->save('acc-a', 'char-a1', $state, null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        // No enhancement key at all: the load's LEFT JOIN found no equipment row, which is
        // what stops every potion looking like a sword.
        self::assertArrayNotHasKey('enhancement_level', $loaded['items'][0]);

        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM equipment_instance WHERE instance_id = 'coin-1'")
            ->fetchColumn();

        self::assertSame(0, $rows);
    }

    public function testEquippingMovesAPieceOutOfTheBagAndBackAgain(): void
    {
        $bagged = $this->sampleState();
        $bagged['inventory_capacity'] = 30;
        $bagged['items'] = [
            ['instance_id' => 'sword-1', 'item_id' => 'item.sword', 'quantity' => 1,
             'slot' => 4, 'enhancement_level' => 7, 'rarity_id' => 'rarity.epic',
             'enchants' => [], 'cards' => []],
        ];

        $first = $this->states->save('acc-a', 'char-a1', $bagged, null);

        // Now worn. The same instance, in no bag slot.
        $worn = $this->sampleState();
        $worn['inventory_capacity'] = 30;
        $worn['items'] = [$this->wornSword()];

        $second = $this->states->save('acc-a', 'char-a1', $worn, $first['save_revision']);

        self::assertTrue($second['ok']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items'], 'one sword, not two');
        self::assertSame(6, $loaded['items'][0]['equipment_slot']);

        // The unique keys are what make this a database guarantee rather than a convention.
        $slots = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM container_slot WHERE instance_id = 'sword-1'")
            ->fetchColumn();

        self::assertSame(0, $slots, 'a worn piece is in no container');

        // And back to the bag.
        $back = $this->sampleState();
        $back['inventory_capacity'] = 30;
        $back['items'] = [
            ['instance_id' => 'sword-1', 'item_id' => 'item.sword', 'quantity' => 1,
             'slot' => 2, 'enhancement_level' => 7, 'rarity_id' => 'rarity.epic',
             'enchants' => [], 'cards' => []],
        ];

        $this->states->save('acc-a', 'char-a1', $back, $second['save_revision']);

        $reloaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $reloaded['items']);
        self::assertSame(2, $reloaded['items'][0]['slot']);
        self::assertSame(0, $reloaded['items'][0]['equipment_slot']);
        self::assertSame(7, $reloaded['items'][0]['enhancement_level'],
            'the upgrade followed it out of the slot');

        $equipped = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM character_equipment WHERE instance_id = 'sword-1'")
            ->fetchColumn();

        self::assertSame(0, $equipped);
    }

    public function testARemovedStoneDisappearsRatherThanLingering(): void
    {
        $state = $this->sampleState();
        $state['inventory_capacity'] = 30;
        $state['items'] = [$this->wornSword()];

        $first = $this->states->save('acc-a', 'char-a1', $state, null);

        // One stone removed. The authoritative set is what the server holds.
        $state['items'][0]['enchants'] = [
            ['stone_id' => 'stone.fire', 'socket' => 0, 'rank' => 3],
        ];

        $this->states->save('acc-a', 'char-a1', $state, $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['items'][0]['enchants']);
        self::assertSame('stone.fire', $loaded['items'][0]['enchants'][0]['stone_id']);
    }

    public function testTwoCharactersOnOneAccountDoNotWearEachOthersArmour(): void
    {
        $this->makeCharacter('char-a2', 'acc-a', 'srv-1', 'Ayla Two');

        $first = $this->sampleState();
        $first['inventory_capacity'] = 30;
        $first['items'] = [$this->wornSword()];

        $this->states->save('acc-a', 'char-a1', $first, null);

        $loaded = $this->states->load('acc-a', 'char-a2');

        self::assertSame([], $loaded['items'],
            'equipment is keyed by character, not by account');
    }

    public function testAnotherAccountCannotWriteEquipment(): void
    {
        $state = $this->sampleState();
        $state['inventory_capacity'] = 30;
        $state['items'] = [$this->wornSword()];

        $refused = $this->states->save('acc-b', 'char-a1', $state, null);

        self::assertFalse($refused['ok']);

        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM equipment_instance WHERE instance_id = 'sword-1'")
            ->fetchColumn();

        self::assertSame(0, $rows);
    }

    public function testAWornPieceWithNoEquipmentSlotAndNoBagSlotIsSkipped(): void
    {
        $state = $this->sampleState();
        $state['inventory_capacity'] = 30;
        $state['items'] = [
            ['instance_id' => 'nowhere-1', 'item_id' => 'item.sword', 'quantity' => 1,
             'slot' => -1, 'equipment_slot' => 0],
        ];

        $this->states->save('acc-a', 'char-a1', $state, null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame([], $loaded['items'],
            'an item in neither a bag nor a slot has no home');
    }
}
