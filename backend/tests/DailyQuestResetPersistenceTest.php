<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;

/**
 * A daily quest knows what day it was finished on.
 *
 * **What this is for.** "Have you already done today's patrol" is a question about a date,
 * and until now no date was recorded: a quest was finished or it was not. Adding a daily
 * reset therefore needed a completion time, and needed it to survive the way this log is
 * saved -- wholesale, deleting every row and writing them all back, on a timer.
 *
 * **The two failures worth guarding.** A completion time destroyed by the next autosave
 * gives a daily quest that can be repeated immediately. A completion time re-stamped on
 * every autosave gives one that can never be repeated at all, because the reset boundary
 * keeps moving forward ahead of the clock. Both look like the quest system working until
 * somebody plays for two days.
 *
 * **One clock.** The stamp is MySQL's NOW(3) and the day numbers handed to the game are
 * MySQL's TO_DAYS. Nothing here reads a PHP clock; see OneClockTest for why.
 */
final class DailyQuestResetPersistenceTest extends BackendTestCase
{
    private const Password = 'a-password-invented-here-only';

    private const Active = 1;
    private const Completed = 3;

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

    private function quest(string $id, int $status, int $counter = 0): array
    {
        return [
            'quest_id' => $id,
            'status'   => $status,
            'counters' => [['value' => $counter]],
        ];
    }

    private function save(array $state, ?int $expected = null): array
    {
        return $this->states->save('acc-a', 'char-a1', $state, $expected);
    }

    private function storedCompletedAt(string $quest): ?string
    {
        $row = $this->pdo->prepare(
            'SELECT completed_at FROM character_quest
             WHERE character_id = :cid AND quest_definition_id = :q'
        );

        $row->execute([':cid' => 'char-a1', ':q' => $quest]);

        $found = $row->fetch();

        return $found === false ? null : $found['completed_at'];
    }

    // ---- the stamp is written ------------------------------------------------------------

    public function testFinishingAQuestRecordsWhenItWasFinished(): void
    {
        $this->save($this->stateWith([$this->quest('quest.daily', self::Completed, 5)]));

        self::assertNotNull($this->storedCompletedAt('quest.daily'),
            'a finished quest recorded no completion time, so no daily reset can be decided');
    }

    public function testAnUnfinishedQuestHasNoCompletionTime(): void
    {
        $this->save($this->stateWith([$this->quest('quest.daily', self::Active, 2)]));

        self::assertNull($this->storedCompletedAt('quest.daily'));
    }

    // ---- and it survives the way this log is saved -----------------------------------------

    public function testAnAutosaveDoesNotDestroyTheCompletionTime(): void
    {
        // The log is replaced wholesale on every save. Without carrying the stamp across
        // that, a daily quest becomes repeatable the moment anything else is saved.
        $first = $this->save($this->stateWith([
            $this->quest('quest.daily', self::Completed, 5),
        ]));

        $stamped = $this->storedCompletedAt('quest.daily');

        self::assertNotNull($stamped);

        // something unrelated changes and the character is saved again
        $state = $this->stateWith([$this->quest('quest.daily', self::Completed, 5)]);
        $state['experience'] = 9999;

        $this->save($state, $first['save_revision']);

        self::assertSame($stamped, $this->storedCompletedAt('quest.daily'),
            'the autosave rewrote the completion time and pushed the daily reset forward');
    }

    public function testAnAlreadyFinishedQuestIsNotReStampedByLaterSaves(): void
    {
        // The mirror-image failure: re-stamping on every save means the reset boundary
        // runs ahead of the clock for ever and the quest never comes back.
        $revision = null;

        for ($i = 0; $i < 3; $i++) {
            $result = $this->save(
                $this->stateWith([$this->quest('quest.daily', self::Completed, 5)]),
                $revision
            );

            self::assertTrue($result['ok'], json_encode($result));

            $revision = $result['save_revision'];

            if ($i === 0) {
                $original = $this->storedCompletedAt('quest.daily');
            }
        }

        self::assertSame($original, $this->storedCompletedAt('quest.daily'));
    }

    public function testTakingADailyAgainClearsTheStampSoFinishingItRecordsTheNewDay(): void
    {
        // Tomorrow the player accepts it again: the quest is Active, which is not finished,
        // and the old completion time must go -- otherwise finishing it again would carry
        // yesterday's date and the day after would refuse it.
        $first = $this->save($this->stateWith([
            $this->quest('quest.daily', self::Completed, 5),
        ]));

        $this->save(
            $this->stateWith([$this->quest('quest.daily', self::Active, 0)]),
            $first['save_revision']
        );

        self::assertNull($this->storedCompletedAt('quest.daily'),
            'a quest taken again is in progress, not finished');
    }

    // ---- what the game is handed -------------------------------------------------------------

    public function testTheLogCarriesTheDayAQuestWasFinishedOn(): void
    {
        $this->save($this->stateWith([$this->quest('quest.daily', self::Completed, 5)]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertArrayHasKey('completed_day', $loaded['quests'][0]);
        self::assertGreaterThan(0, $loaded['quests'][0]['completed_day']);
    }

    public function testAnUnfinishedQuestReportsDayZeroRatherThanNull(): void
    {
        // Zero rather than null so the game compares plain integers. A real day number is
        // always far above zero, so there is no value it can collide with.
        $this->save($this->stateWith([$this->quest('quest.daily', self::Active, 1)]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame(0, $loaded['quests'][0]['completed_day']);
    }

    public function testTheStateCarriesTodayFromTheSameClock(): void
    {
        // The whole reason the day is a number: the game compares it against this one, and
        // both came out of MySQL. A world server deciding "today" from its own machine
        // would reset dailies at a different midnight than the completions were stamped in.
        $this->save($this->stateWith([$this->quest('quest.daily', self::Completed, 5)]));

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertArrayHasKey('server_day', $loaded);
        self::assertGreaterThan(0, $loaded['server_day']);

        self::assertSame($loaded['server_day'], $loaded['quests'][0]['completed_day'],
            'a quest finished a moment ago was finished today');
    }

    public function testADailyFinishedYesterdayReadsAsADifferentDay(): void
    {
        // The reset itself, proven by moving the stored stamp back a day rather than by
        // waiting until tomorrow.
        $this->save($this->stateWith([$this->quest('quest.daily', self::Completed, 5)]));

        $this->pdo->prepare(
            'UPDATE character_quest SET completed_at = completed_at - INTERVAL 1 DAY
             WHERE character_id = :cid AND quest_definition_id = :q'
        )->execute([':cid' => 'char-a1', ':q' => 'quest.daily']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame($loaded['server_day'] - 1, $loaded['quests'][0]['completed_day'],
            'yesterday must read as exactly one day before today');
    }

    public function testDeletingTheCharacterTakesTheCompletionTimesWithIt(): void
    {
        $this->save($this->stateWith([$this->quest('quest.daily', self::Completed, 5)]));

        $this->pdo->prepare('DELETE FROM `character` WHERE character_id = :cid')
            ->execute([':cid' => 'char-a1']);

        $count = (int) $this->pdo->query('SELECT COUNT(*) FROM character_quest')->fetchColumn();

        self::assertSame(0, $count);
    }
}
