<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;

/**
 * A quest survives logging out.
 *
 * **The bug this exists for.** Quest state lived only in the running world server. Taking
 * a quest, walking outside and logging back in lost it, because a character spawning built
 * an empty log -- which did not make the feature incomplete, it made it unusable: no quest
 * spanning two sessions could ever be finished, and the first thing a player does with a
 * new quest is log out.
 *
 * **What has to hold.** The status and every objective counter come back exactly as they
 * went in; the log is replaced rather than merged, so an abandoned quest really leaves; and
 * a world that carries no quests at all does not wipe the ones already stored.
 */
final class CharacterQuestPersistenceTest extends BackendTestCase
{
    private const Password = 'a-password-invented-here-only';

    private CharacterStateRepository $states;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeAccount('acc-a', 'ayla@test', self::Password);
        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');
    }

    /** @return array<string,mixed> */
    private function stateWith(array $quests): array
    {
        return [
            'level'          => 12,
            'experience'     => 4500,
            'current_health' => 40,
            'current_mana'   => 20,
            'class_id'       => 'class.swordsman',
            'job_id'         => 'job.none',
            'map_id'         => 'map.harbor_town',
            'spawn_id'       => 'spawn.harbor_town.plaza',
            'stats'          => [],
            'appearance'     => [],
            'skills'         => [],
            'quests'         => $quests,
            'revisions'      => ['identity' => 1],
        ];
    }

    /**
     * Saves, presenting the revision a caller would have loaded.
     *
     * Null rather than zero for a first save: the contract for "never saved" is an absent
     * revision, and zero means "I read revision zero", which matches nothing and is refused
     * as stale forever. The same trap the Unity store documents at length.
     */
    private function save(array $state, ?int $expected = null): array
    {
        return $this->states->save('acc-a', 'char-a1', $state, $expected);
    }

    // ---- the round trip -----------------------------------------------------------------

    public function testAQuestAndItsProgressComeBackExactlyAsTheyWentIn(): void
    {
        $result = $this->save($this->stateWith([
            [
                'quest_id' => 'quest.harbor_first_hunt',
                'status'   => 1,
                'counters' => [['value' => 2]],
            ],
        ]));

        self::assertTrue($result['ok'], json_encode($result));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests']);
        self::assertSame('quest.harbor_first_hunt', $loaded['quests'][0]['quest_id']);
        self::assertSame(1, $loaded['quests'][0]['status']);
        self::assertSame([['value' => 2]], $loaded['quests'][0]['counters']);
    }

    public function testEveryObjectiveKeepsItsOwnCounterAndItsPosition(): void
    {
        // Objectives are matched by position and by nothing else. If the order came back
        // differently, a player would see the wrong count against the wrong objective.
        $this->save($this->stateWith([
            [
                'quest_id' => 'quest.many',
                'status'   => 1,
                'counters' => [['value' => 7], ['value' => 0], ['value' => 3]],
            ],
        ]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame([['value' => 7], ['value' => 0], ['value' => 3]],
            $loaded['quests'][0]['counters']);
    }

    public function testAFinishedQuestIsRememberedRatherThanDeleted(): void
    {
        // Prerequisites and non-repeatable quests both need the history. A quest that
        // vanished on completion could be taken again forever.
        $this->save($this->stateWith([
            ['quest_id' => 'quest.done', 'status' => 3, 'counters' => [['value' => 3]]],
        ]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests']);
        self::assertSame(3, $loaded['quests'][0]['status']);
    }

    public function testACharacterWhoHasTakenNothingLoadsAnEmptyLog(): void
    {
        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame([], $loaded['quests'],
            'a character with no rows has taken no quests, which is not the same as broken');
    }

    // ---- the log is replaced, not merged ------------------------------------------------

    public function testAbandoningAQuestRemovesItRatherThanLeavingItBehind(): void
    {
        $first = $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 1]]],
            ['quest_id' => 'quest.two', 'status' => 1, 'counters' => [['value' => 0]]],
        ]));

        $this->save($this->stateWith([
            ['quest_id' => 'quest.two', 'status' => 1, 'counters' => [['value' => 0]]],
        ]), $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests']);
        self::assertSame('quest.two', $loaded['quests'][0]['quest_id']);
    }

    public function testProgressOverwritesRatherThanAccumulates(): void
    {
        $first = $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 1]]],
        ]));

        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 2, 'counters' => [['value' => 3]]],
        ]), $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame([['value' => 3]], $loaded['quests'][0]['counters']);
        self::assertSame(2, $loaded['quests'][0]['status']);
    }

    public function testAWorldCarryingNoQuestLogLeavesTheStoredOneAlone(): void
    {
        // A world composed without quest content must not wipe what a player has taken --
        // the same rule the bag already follows for an absent inventory.
        $first = $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 2]]],
        ]));

        $without = $this->stateWith([]);
        unset($without['quests']);

        $this->save($without, $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests'],
            'a save with no quest log deleted the stored one');
        self::assertSame([['value' => 2]], $loaded['quests'][0]['counters']);
    }

    public function testAnEmptyQuestLogReallyClearsIt(): void
    {
        // The difference from the test above: an empty array is an answer, an absent key
        // is a silence. They must not mean the same thing.
        $first = $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 2]]],
        ]));

        $this->save($this->stateWith([]), $first['save_revision']);

        self::assertSame([], $this->states->load('acc-a', 'char-a1')['quests']);
    }

    // ---- the shape the world actually sends ---------------------------------------------

    public function testTheExactJsonTheWorldWritesRoundTripsThroughThisRepository(): void
    {
        // The bug every test above missed. Each of them used this repository's own shape on
        // both sides, so the two halves agreed with each other and disagreed with the game:
        // the world wrote counters as {"value":N} objects, this returned bare integers, and
        // every counter came back zero. Nothing failed -- the quest persisted, the progress
        // silently did not.
        //
        // The literal below is what HttpCharacterStateStore.WithCollections produces. If
        // either side changes shape, this fails rather than the player losing progress.
        $json = <<<'JSON'
        {
            "quests": [
                {
                    "quest_id": "quest.harbor_first_hunt",
                    "status": 1,
                    "counters": [{"value": 2}]
                }
            ]
        }
        JSON;

        $fromTheWorld = json_decode($json, true);

        self::assertIsArray($fromTheWorld, 'the pinned request body is not valid JSON');

        $state = $this->stateWith($fromTheWorld['quests']);

        $result = $this->save($state);

        self::assertTrue($result['ok'], json_encode($result));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests']);
        self::assertSame(1, $loaded['quests'][0]['status']);

        // Two, not zero. Zero is what the mismatch produced and what nothing noticed.
        self::assertSame(2, $loaded['quests'][0]['counters'][0]['value'],
            'progress was stored but did not come back: the wire and the store disagree');
    }

    public function testABareNumberIsUnderstoodTooRatherThanCastToOne(): void
    {
        // Casting an array to int quietly yields 1 in PHP, which is exactly why the
        // original mismatch stored something plausible instead of failing.
        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [5]],
        ]));

        self::assertSame(5,
            $this->states->load('acc-a', 'char-a1')['quests'][0]['counters'][0]['value']);
    }

    // ---- what nonsense does -------------------------------------------------------------

    public function testAStatusOutsideTheGamesOwnIsRefusedRatherThanStored(): void
    {
        // A status nothing knows how to leave would strand the quest in the log forever.
        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 99, 'counters' => [['value' => 0]]],
        ]));

        self::assertSame(0, $this->states->load('acc-a', 'char-a1')['quests'][0]['status']);
    }

    public function testANegativeCounterIsStoredAsZero(): void
    {
        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => -5]]],
        ]));

        self::assertSame([['value' => 0]],
            $this->states->load('acc-a', 'char-a1')['quests'][0]['counters']);
    }

    public function testAQuestWithNoIdIsSkipped(): void
    {
        $this->save($this->stateWith([
            ['quest_id' => '', 'status' => 1, 'counters' => [['value' => 1]]],
            ['quest_id' => 'quest.real', 'status' => 1, 'counters' => [['value' => 1]]],
        ]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['quests']);
        self::assertSame('quest.real', $loaded['quests'][0]['quest_id']);
    }

    public function testAnothersAccountCannotReadTheLog(): void
    {
        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 1]]],
        ]));

        $this->makeAccount('acc-b', 'brik@test', self::Password);

        self::assertNull($this->states->load('acc-b', 'char-a1'),
            'the query is scoped by account, so there is nothing to refuse');
    }

    public function testDeletingTheCharacterTakesTheQuestsWithIt(): void
    {
        $this->save($this->stateWith([
            ['quest_id' => 'quest.one', 'status' => 1, 'counters' => [['value' => 1], ['value' => 2]]],
        ]));

        $this->pdo->prepare('DELETE FROM `character` WHERE character_id = :cid')
            ->execute([':cid' => 'char-a1']);

        $quests = (int) $this->pdo->query(
            'SELECT COUNT(*) FROM character_quest'
        )->fetchColumn();

        $counters = (int) $this->pdo->query(
            'SELECT COUNT(*) FROM character_quest_objective'
        )->fetchColumn();

        self::assertSame(0, $quests, 'a deleted character left its quests behind');
        self::assertSame(0, $counters, 'a deleted character left its counters behind');
    }
}
