<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;

/**
 * A card in a sword, and which sword it is in.
 *
 * **The instance is the point, not the definition.** Two swords of the same kind are two
 * objects, and a card socketed into one must not appear in the other after a reload. The
 * socket rows are keyed by the equipment's instance id for exactly that reason, and these
 * tests exist to keep it that way.
 *
 * **The card is a real item.** It has its own instance row and the socket has a foreign key
 * to it, so a socketed card cannot point at an item nobody owns.
 *
 * **A save is a revision, not a repeat.** `save()` reads a null expected revision as "this
 * character has never been saved", and refuses with `stale_revision` if one already exists.
 * A second save must therefore carry the revision the first returned. Phase 18.16 mistook a
 * refused second save for a stale socket row; every test here threads the revision, which is
 * what the running server does.
 */
final class EquipmentCardSocketTest extends BackendTestCase
{
    private CharacterStateRepository $states;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'cards-a', 'not-a-real-password');
        $this->makeAccount('acc-b', 'cards-b', 'not-a-real-password');

        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');
        $this->makeCharacter('char-a2', 'acc-a', 'srv-1', 'Alma');
        $this->makeCharacter('char-b1', 'acc-b', 'srv-1', 'Bryn');
    }

    /**
     * A bag holding whatever the caller describes.
     *
     * @param list<array<string,mixed>> $items
     * @return array<string,mixed>
     */
    private function bag(array $items): array
    {
        return [
            'level'          => 10,
            'experience'     => 0,
            'current_health' => 100,
            'current_mana'   => 50,
            'class_id'       => 'class.novice',
            'job_id'         => 'job.none',
            'map_id'         => 'map.town',
            'spawn_id'       => 'spawn.town.plaza',
            'stats'          => [['stat_id' => 'stat.strength', 'value' => 10]],
            'skills'         => [],
            'appearance'     => [],
            'items'          => $items,
            'inventory_capacity' => 30,
        ];
    }

    /**
     * One sword, one card item, and the socket that joins them.
     *
     * @return list<array<string,mixed>>
     */
    private function swordWithCard(string $sword = 'equip-1', string $card = 'card-1',
        int $socket = 0): array
    {
        return [
            [
                'instance_id' => $card,
                'item_id'     => 'card.ancient_slime_king',
                'quantity'    => 1,
                'slot'        => 1,
            ],
            [
                'instance_id' => $sword,
                'item_id'     => 'item.apprentice_cutlass',
                'quantity'    => 1,
                'slot'        => 0,
                'enhancement_level' => 0,
                'rarity_id'   => 'rarity.common',
                'cards'       => [
                    [
                        'card_id'          => 'card.ancient_slime_king',
                        'socket'           => $socket,
                        'card_instance_id' => $card,
                    ],
                ],
            ],
        ];
    }

    // ---- one socket, there and back ----------------------------------------------------

    public function testASocketedCardSurvivesTheRoundTrip(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        $sword = $this->itemNamed($loaded, 'equip-1');

        self::assertNotNull($sword, 'the sword did not come back');
        self::assertCount(1, $sword['cards'], 'the socket did not survive');

        self::assertSame('card.ancient_slime_king', $sword['cards'][0]['card_id']);
        self::assertSame(0, $sword['cards'][0]['socket']);
        self::assertSame('card-1', $sword['cards'][0]['card_instance_id'],
            'the exact card that was consumed was not remembered');
    }

    public function testOnlyTheAuthoredIdIsStoredAndNoEffectValuesAreCopied(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM equipment_card_socket')->fetch(\PDO::FETCH_ASSOC));

        // A copied modifier would be a second source of truth that goes stale on the next
        // content patch.
        foreach (['value', 'modifier', 'stat', 'percent', 'name', 'icon', 'effect']
            as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'a socket row copies authored card content');
            }
        }
    }

    public function testTwoSwordsOfTheSameKindDoNotShareOneCard(): void
    {
        $items = $this->swordWithCard();

        // A second, identical sword with nothing in it.
        $items[] = [
            'instance_id' => 'equip-2',
            'item_id'     => 'item.apprentice_cutlass',
            'quantity'    => 1,
            'slot'        => 2,
            'enhancement_level' => 0,
            'rarity_id'   => 'rarity.common',
        ];

        $this->states->save('acc-a', 'char-a1', $this->bag($items), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $this->itemNamed($loaded, 'equip-1')['cards'],
            'the carded sword lost its card');

        self::assertCount(0, $this->itemNamed($loaded, 'equip-2')['cards'],
            'the card spread to the other sword');
    }

    public function testSocketsComeBackInOrderBecauseTheOrderIsWhatAPlayerSees(): void
    {
        $items = [
            ['instance_id' => 'card-1', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 1],
            ['instance_id' => 'card-2', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 2],
            [
                'instance_id' => 'equip-1',
                'item_id'     => 'item.apprentice_cutlass',
                'quantity'    => 1,
                'slot'        => 0,
                'enhancement_level' => 0,
                'rarity_id'   => 'rarity.common',
                'cards'       => [
                    ['card_id' => 'card.ancient_slime_king', 'socket' => 1,
                     'card_instance_id' => 'card-2'],
                    ['card_id' => 'card.ancient_slime_king', 'socket' => 0,
                     'card_instance_id' => 'card-1'],
                ],
            ],
        ];

        $this->states->save('acc-a', 'char-a1', $this->bag($items), null);

        $cards = $this->itemNamed($this->states->load('acc-a', 'char-a1'), 'equip-1')['cards'];

        self::assertCount(2, $cards);
        self::assertSame(0, $cards[0]['socket']);
        self::assertSame('card-1', $cards[0]['card_instance_id']);
        self::assertSame(1, $cards[1]['socket']);
        self::assertSame('card-2', $cards[1]['card_instance_id']);
    }

    public function testOneCardCannotBeInTwoPiecesAtOnce(): void
    {
        // The UNIQUE on card_instance_id is what makes this a database guarantee: one
        // physical card, one socket, however the writes race.
        $items = $this->swordWithCard();

        $items[] = [
            'instance_id' => 'equip-2',
            'item_id'     => 'item.apprentice_cutlass',
            'quantity'    => 1,
            'slot'        => 2,
            'enhancement_level' => 0,
            'rarity_id'   => 'rarity.common',
            'cards'       => [
                ['card_id' => 'card.ancient_slime_king', 'socket' => 0,
                 'card_instance_id' => 'card-1'],
            ],
        ];

        $threw = false;

        try {
            $this->states->save('acc-a', 'char-a1', $this->bag($items), null);
        } catch (\PDOException $e) {
            $threw = true;
        }

        $sockets = (int) $this->pdo
            ->query('SELECT COUNT(*) FROM equipment_card_socket')->fetchColumn();

        self::assertTrue($threw || $sockets <= 1,
            'one card ended up in two pieces at once');
    }

    public function testARewrittenBagReplacesTheSocketsRatherThanAddingToThem(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM equipment_card_socket')->fetchColumn(),
            'saving twice doubled the sockets');
    }

    public function testAMalformedSocketIsSkippedRatherThanStored(): void
    {
        $items = [
            ['instance_id' => 'card-1', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 1],
            [
                'instance_id' => 'equip-1',
                'item_id'     => 'item.apprentice_cutlass',
                'quantity'    => 1,
                'slot'        => 0,
                'enhancement_level' => 0,
                'rarity_id'   => 'rarity.common',
                'cards'       => [
                    // No card id, and a socket index that is not a socket.
                    ['card_id' => '', 'socket' => 0, 'card_instance_id' => 'card-1'],
                    ['card_id' => 'card.ancient_slime_king', 'socket' => -1,
                     'card_instance_id' => 'card-1'],
                ],
            ],
        ];

        $this->states->save('acc-a', 'char-a1', $this->bag($items), null);

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM equipment_card_socket')->fetchColumn(),
            'a malformed socket was written anyway');
    }

    public function testTwoCharactersOnOneAccountKeepTheirOwnSockets(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        $this->states->save('acc-a', 'char-a2', $this->bag([
            ['instance_id' => 'equip-9', 'item_id' => 'item.apprentice_cutlass',
             'quantity' => 1, 'slot' => 0, 'enhancement_level' => 0,
             'rarity_id' => 'rarity.common'],
        ]), null);

        self::assertCount(1,
            $this->itemNamed($this->states->load('acc-a', 'char-a1'), 'equip-1')['cards']);

        self::assertCount(0,
            $this->itemNamed($this->states->load('acc-a', 'char-a2'), 'equip-9')['cards'],
            'another character on the same account inherited a socket');
    }

    public function testAnotherAccountSeesNothingOfThisOne(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        self::assertNull($this->states->load('acc-b', 'char-a1'),
            'another account read a character it does not own');
    }

    public function testARolledBackSaveLeavesNoSocketsBehind(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        // A stale write, refused. Whatever it would have changed must not be there.
        $this->states->save('acc-a', 'char-a1', $this->bag([
            ['instance_id' => 'card-2', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 5],
        ]), 99);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $this->itemNamed($loaded, 'equip-1')['cards'],
            'a refused save changed the sockets anyway');

        self::assertNull($this->itemNamed($loaded, 'card-2'),
            'a refused save wrote its items');
    }


    // ---- taking the card back out -------------------------------------------------------

    public function testSavingAPieceWithNoCardsRemovesTheSocketThatWasThere(): void
    {
        $first = $this->states->save('acc-a', 'char-a1',
            $this->bag($this->swordWithCard()), null);

        self::assertTrue($first['ok'], 'the first save was refused');

        // The same bag, with the card loose again and the sword empty -- exactly what the
        // world writes after CardSocketService.TryRemove has put the card back.
        $second = $this->states->save('acc-a', 'char-a1', $this->bag([
            ['instance_id' => 'card-1', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 1],
            ['instance_id' => 'equip-1', 'item_id' => 'item.apprentice_cutlass',
             'quantity' => 1, 'slot' => 0, 'enhancement_level' => 0,
             'rarity_id' => 'rarity.common'],
        ]), $first['save_revision']);

        self::assertTrue($second['ok'], 'the second save was refused: '
            . ($second['reason'] ?? ''));

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM equipment_card_socket')->fetchColumn(),
            'the socket row outlived the card being taken out');

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(0, $this->itemNamed($loaded, 'equip-1')['cards'],
            'the removed card came back on load');

        self::assertNotNull($this->itemNamed($loaded, 'card-1'),
            'the card did not return to the bag');
    }

    public function testRemovingOneOfTwoCardsLeavesTheOtherExactlyWhereItWas(): void
    {
        $both = [
            ['instance_id' => 'card-1', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 1],
            ['instance_id' => 'card-2', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 2],
            [
                'instance_id' => 'equip-1', 'item_id' => 'item.apprentice_cutlass',
                'quantity' => 1, 'slot' => 0,
                'enhancement_level' => 0, 'rarity_id' => 'rarity.common',
                'cards' => [
                    ['card_id' => 'card.ancient_slime_king', 'socket' => 0,
                     'card_instance_id' => 'card-1'],
                    ['card_id' => 'card.ancient_slime_king', 'socket' => 1,
                     'card_instance_id' => 'card-2'],
                ],
            ],
        ];

        $first = $this->states->save('acc-a', 'char-a1', $this->bag($both), null);

        // Socket zero emptied, socket one untouched.
        $remaining = $both;
        $remaining[2]['cards'] = [
            ['card_id' => 'card.ancient_slime_king', 'socket' => 1,
             'card_instance_id' => 'card-2'],
        ];

        $this->states->save('acc-a', 'char-a1', $this->bag($remaining),
            $first['save_revision']);

        $cards = $this->itemNamed($this->states->load('acc-a', 'char-a1'),
            'equip-1')['cards'];

        self::assertCount(1, $cards, 'removing one card did not leave exactly one');
        self::assertSame(1, $cards[0]['socket'], 'the wrong socket was emptied');
        self::assertSame('card-2', $cards[0]['card_instance_id'],
            'the wrong card was removed');
    }

    public function testEmptyingOneSwordLeavesTheOtherOnesCardAlone(): void
    {
        // Deletion is by equipment instance, never by definition: two identical swords
        // must not be cleared together.
        $items = $this->swordWithCard();

        $items[] = ['instance_id' => 'card-2', 'item_id' => 'card.ancient_slime_king',
                    'quantity' => 1, 'slot' => 3];

        $items[] = [
            'instance_id' => 'equip-2', 'item_id' => 'item.apprentice_cutlass',
            'quantity' => 1, 'slot' => 2,
            'enhancement_level' => 0, 'rarity_id' => 'rarity.common',
            'cards' => [
                ['card_id' => 'card.ancient_slime_king', 'socket' => 0,
                 'card_instance_id' => 'card-2'],
            ],
        ];

        $first = $this->states->save('acc-a', 'char-a1', $this->bag($items), null);

        // Only the first sword is emptied.
        $after = $items;
        $after[1]['cards'] = [];

        $this->states->save('acc-a', 'char-a1', $this->bag($after),
            $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(0, $this->itemNamed($loaded, 'equip-1')['cards'],
            'the emptied sword kept its card');

        self::assertCount(1, $this->itemNamed($loaded, 'equip-2')['cards'],
            'emptying one sword cleared the other');

        self::assertSame('card-2',
            $this->itemNamed($loaded, 'equip-2')['cards'][0]['card_instance_id']);
    }

    public function testSavingTheEmptiedPieceAgainKeepsItEmpty(): void
    {
        $first = $this->states->save('acc-a', 'char-a1',
            $this->bag($this->swordWithCard()), null);

        $empty = [
            ['instance_id' => 'card-1', 'item_id' => 'card.ancient_slime_king',
             'quantity' => 1, 'slot' => 1],
            ['instance_id' => 'equip-1', 'item_id' => 'item.apprentice_cutlass',
             'quantity' => 1, 'slot' => 0, 'enhancement_level' => 0,
             'rarity_id' => 'rarity.common'],
        ];

        $second = $this->states->save('acc-a', 'char-a1', $this->bag($empty),
            $first['save_revision']);

        $third = $this->states->save('acc-a', 'char-a1', $this->bag($empty),
            $second['save_revision']);

        self::assertTrue($third['ok']);

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM equipment_card_socket')->fetchColumn(),
            'a socket row came back on a later save');
    }

    public function testARefusedSaveLeavesTheSocketExactlyAsItWas(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->bag($this->swordWithCard()), null);

        // A stale write that would have emptied the sword. It must change nothing.
        $refused = $this->states->save('acc-a', 'char-a1', $this->bag([
            ['instance_id' => 'equip-1', 'item_id' => 'item.apprentice_cutlass',
             'quantity' => 1, 'slot' => 0, 'enhancement_level' => 0,
             'rarity_id' => 'rarity.common'],
        ]), 99);

        self::assertFalse($refused['ok']);
        self::assertSame('stale_revision', $refused['reason']);

        self::assertCount(1,
            $this->itemNamed($this->states->load('acc-a', 'char-a1'), 'equip-1')['cards'],
            'a refused save emptied the socket anyway');
    }

    public function testASecondFirstSaveIsRefusedRatherThanTreatedAsAnUpdate(): void
    {
        // The mistake Phase 18.16 made, pinned so it cannot be made again: a null expected
        // revision means "never saved", not "do not check".
        $first = $this->states->save('acc-a', 'char-a1',
            $this->bag($this->swordWithCard()), null);

        self::assertTrue($first['ok']);

        $again = $this->states->save('acc-a', 'char-a1', $this->bag([]), null);

        self::assertFalse($again['ok'],
            'a second first-save was accepted and would have written a partial bag');

        self::assertSame('stale_revision', $again['reason']);
    }

    /** @return array<string,mixed>|null */
    private function itemNamed(?array $state, string $instanceId): ?array
    {
        foreach ((array) ($state['items'] ?? []) as $item) {
            if (($item['instance_id'] ?? '') === $instanceId) return $item;
        }

        return null;
    }
}