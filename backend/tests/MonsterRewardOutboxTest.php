<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\World\MonsterRewardRepository;

/**
 * A monster defeat's decision, kept until it has actually been paid.
 *
 * The failure every test here circles is the same one: a defeat is resolved exactly once,
 * and the roll behind it -- a one in ten million fruit -- cannot honestly be run again. So
 * the decision has to survive being written down, read back, half delivered, and raced for
 * by two recovering workers, without ever being made twice.
 */
final class MonsterRewardOutboxTest extends BackendTestCase
{
    private MonsterRewardRepository $rewards;

    protected function setUp(): void
    {
        parent::setUp();

        $this->rewards = new MonsterRewardRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'reward-a', 'not-a-real-password');

        foreach (['char-ann', 'char-ben'] as $id) {
            $this->makeCharacter($id, 'acc-a', 'srv-1', ucfirst(substr($id, 5)));
        }
    }

    /** @return array<string,mixed> */
    private function envelope(string $defeat = 'defeat-1', string $reward = 'reward-1',
        string $loot = 'loot-1', ?int $cursor = null): array
    {
        return [
            'reward_id'             => $reward,
            'defeat_id'             => $defeat,
            'server_id'             => 'srv-1',
            'channel_id'            => 'ch-1',
            'monster_definition_id' => 'monster.ancient_slime_king',
            'map_definition_id'     => 'map.harbor_town',
            'killer_character_id'   => 'char-ann',
            'loot_id'               => $loot,
            'loot_policy'           => 1,
            'claimant_character_id' => 'char-ann',
            'position_x'            => 1.5,
            'position_y'            => 2.5,
            'position_z'            => -3.5,
            'party_id'              => 'party-1',
            'party_cursor'          => $cursor,
        ];
    }

    /** @return list<array{character_id:string,experience:int}> */
    private function split(): array
    {
        return [
            ['character_id' => 'char-ann', 'experience' => 450],
            ['character_id' => 'char-ben', 'experience' => 450],
        ];
    }

    /** @return list<array{item_definition_id:string,quantity:int}> */
    private function darkness(): array
    {
        return [['item_definition_id' => 'item.devil_fruit.darkness', 'quantity' => 1]];
    }

    // ---- recording a decision ----------------------------------------------------------

    public function testADecidedDefeatIsRecordedWholeAndComesBackWhole(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(),
            $this->darkness());

        self::assertTrue($saved['ok']);
        self::assertFalse($saved['existing']);

        $loaded = $this->rewards->find('reward-1');

        self::assertNotNull($loaded);
        self::assertSame('defeat-1', $loaded['defeat_id']);
        self::assertSame('monster.ancient_slime_king', $loaded['monster_definition_id']);
        self::assertSame('char-ann', $loaded['claimant_character_id']);
        self::assertSame('loot-1', $loaded['loot_id']);
        self::assertSame(MonsterRewardRepository::STATE_PENDING, $loaded['state']);

        self::assertCount(2, $loaded['experience']);
        self::assertSame(450, $loaded['experience'][0]['experience']);
        self::assertFalse($loaded['experience'][0]['delivered']);

        self::assertCount(1, $loaded['loot']);
        self::assertSame('item.devil_fruit.darkness',
            $loaded['loot'][0]['item_definition_id']);
        self::assertFalse($loaded['loot'][0]['claimed']);
    }

    public function testOnlyDefinitionIdsAreStoredAndNoContentIsCopied(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM monster_reward')->fetch(\PDO::FETCH_ASSOC));

        // Nothing about a connection, a scene object, a random generator or a body.
        foreach (['connection', 'networkobject', 'network_object', 'seed', 'rng',
                  'damage', 'health', 'ai_', 'prefab', 'sprite'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'a reward row carries runtime state');
            }
        }
    }

    public function testRecordingTheSameDefeatTwiceReturnsTheFirstRewardRatherThanASecond(): void
    {
        $first = $this->rewards->record($this->envelope(), $this->split(),
            $this->darkness());

        // The world saved, never heard the answer, and asked again with a new reward id.
        $second = $this->rewards->record(
            $this->envelope('defeat-1', 'reward-2', 'loot-2'),
            $this->split(), $this->darkness());

        self::assertTrue($second['ok']);
        self::assertTrue($second['existing'], 'a second reward was minted for one defeat');
        self::assertSame($first['reward_id'], $second['reward_id']);

        self::assertNull($this->rewards->find('reward-2'));

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward')->fetchColumn());
    }

    public function testADifferentDefeatIsADifferentRewardBecauseARespawnIsANewMonster(): void
    {
        $this->rewards->record($this->envelope('defeat-1', 'reward-1', 'loot-1'),
            $this->split(), $this->darkness());

        $second = $this->rewards->record($this->envelope('defeat-2', 'reward-2', 'loot-2'),
            $this->split(), $this->darkness());

        self::assertTrue($second['ok']);
        self::assertFalse($second['existing']);
        self::assertSame(2, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward')->fetchColumn());
    }

    public function testAFailedRareRollIsRecordedJustAsFirmlyAsASuccessfulOne(): void
    {
        // The point: a reward with no fruit is still a decision. Without the row, a restart
        // would resolve the defeat again and hand out a second chance at the fruit.
        $saved = $this->rewards->record(
            $this->envelope('defeat-empty', 'reward-empty', ''), $this->split(), []);

        self::assertTrue($saved['ok']);

        $loaded = $this->rewards->find('reward-empty');

        self::assertSame('', $loaded['loot_id']);
        self::assertSame([], $loaded['loot']);
        self::assertCount(2, $loaded['experience'],
            'a defeat that dropped nothing still owes experience');
    }

    public function testACursorOfZeroIsATurnAndAMissingCursorIsNoTurnAtAll(): void
    {
        $this->rewards->record($this->envelope('defeat-1', 'reward-1', 'loot-1', 0),
            $this->split(), $this->darkness());

        $this->rewards->record($this->envelope('defeat-2', 'reward-2', 'loot-2', null),
            $this->split(), $this->darkness());

        self::assertSame(0, $this->rewards->find('reward-1')['party_cursor'],
            'the first member\'s turn was read as no turn');

        self::assertNull($this->rewards->find('reward-2')['party_cursor']);
    }

    // ---- refusals ------------------------------------------------------------------------

    public function testARewardWithNoDefeatIsRefused(): void
    {
        $envelope = $this->envelope();
        $envelope['defeat_id'] = '';

        $result = $this->rewards->record($envelope, $this->split(), $this->darkness());

        self::assertFalse($result['ok']);
        self::assertSame('invalid_reward', $result['reason']);
    }

    public function testALootEntryWithNoItemIsRefusedRatherThanSubstituted(): void
    {
        $result = $this->rewards->record($this->envelope(), $this->split(),
            [['item_definition_id' => '', 'quantity' => 1]]);

        self::assertFalse($result['ok']);
        self::assertSame('invalid_loot_entry', $result['reason']);
        self::assertNull($this->rewards->find('reward-1'), 'a refused reward was written');
    }

    public function testALootEntryWithNoQuantityIsRefused(): void
    {
        $result = $this->rewards->record($this->envelope(), $this->split(),
            [['item_definition_id' => 'item.devil_fruit.darkness', 'quantity' => 0]]);

        self::assertFalse($result['ok']);
        self::assertSame('invalid_loot_quantity', $result['reason']);
    }

    public function testAPileWithNothingInItIsRefusedBecauseItContradictsItself(): void
    {
        // A reward that names a pile but lists no contents would republish an empty object
        // on recovery, which is a pickup request that can only ever be refused.
        $result = $this->rewards->record($this->envelope('defeat-1', 'reward-1', 'loot-1'),
            $this->split(), []);

        self::assertFalse($result['ok']);
        self::assertSame('invalid_loot_entry', $result['reason']);
    }

    public function testANegativeExperienceGrantIsRefused(): void
    {
        $result = $this->rewards->record($this->envelope(),
            [['character_id' => 'char-ann', 'experience' => -1]], $this->darkness());

        self::assertFalse($result['ok']);
        self::assertSame('invalid_experience_grant', $result['reason']);
    }

    // ---- delivery progress ----------------------------------------------------------------

    public function testPendingRewardsComeBackForTheirOwnWorldAndNobodyElses(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        self::assertCount(1, $this->rewards->pending('srv-1', 'ch-1'));

        // Another channel running the same map has no business delivering this.
        self::assertCount(0, $this->rewards->pending('srv-1', 'ch-2'));
        self::assertCount(0, $this->rewards->pending('srv-2', 'ch-1'));
    }

    public function testExperienceIsMarkedPerRecipientSoAHalfPaidRewardResumes(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $moved = $this->rewards->progress('reward-1', 1, ['char-ann']);

        self::assertTrue($moved['ok']);
        self::assertSame(2, $moved['revision']);

        $loaded = $this->rewards->find('reward-1');

        $paid = [];

        foreach ($loaded['experience'] as $grant) {
            $paid[$grant['character_id']] = $grant['delivered'];
        }

        self::assertTrue($paid['char-ann']);
        self::assertFalse($paid['char-ben'], 'an unpaid member was marked paid');
    }

    public function testMarkingTheSameExperienceTwiceChangesNothing(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $this->rewards->progress('reward-1', 1, ['char-ann']);

        $stamp = $this->pdo->query(
            "SELECT delivered_at FROM monster_reward_experience
             WHERE reward_id = 'reward-1' AND character_id = 'char-ann'")->fetchColumn();

        // Re-delivered by a retry that could not tell it had already landed.
        $this->rewards->progress('reward-1', 2, ['char-ann']);

        self::assertSame($stamp, $this->pdo->query(
            "SELECT delivered_at FROM monster_reward_experience
             WHERE reward_id = 'reward-1' AND character_id = 'char-ann'")->fetchColumn(),
            'a repeated delivery re-stamped a payment that had already happened');
    }

    public function testLootIsMarkedTakenAndByWhom(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $this->rewards->progress('reward-1', 1, [],
            [['entry_index' => 0, 'character_id' => 'char-ann']]);

        $entry = $this->rewards->find('reward-1')['loot'][0];

        self::assertTrue($entry['claimed']);
        self::assertSame('char-ann', $entry['claimed_by']);
    }

    public function testAStaleRevisionIsRefusedAndChangesNothing(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $this->rewards->progress('reward-1', 1, ['char-ann']);

        $stale = $this->rewards->progress('reward-1', 1, ['char-ben']);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        $paid = [];

        foreach ($this->rewards->find('reward-1')['experience'] as $grant) {
            $paid[$grant['character_id']] = $grant['delivered'];
        }

        self::assertFalse($paid['char-ben'],
            'a stale write delivered experience anyway');
    }

    public function testTwoRecoveriesRacingForOneRewardDeliverItOnce(): void
    {
        // Both read revision 1 and both try to pay. The revision check is what makes the
        // loser re-read instead of paying a second time.
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $first = $this->rewards->progress('reward-1', 1, ['char-ann', 'char-ben'],
            [['entry_index' => 0, 'character_id' => 'char-ann']], true, true, true);

        $second = $this->rewards->progress('reward-1', 1, ['char-ann', 'char-ben'],
            [['entry_index' => 0, 'character_id' => 'char-ben']], true, true, true);

        self::assertTrue($first['ok']);
        self::assertFalse($second['ok'], 'both recoveries believed they were first');
        self::assertSame('stale_revision', $second['reason']);

        self::assertSame('char-ann', $this->rewards->find('reward-1')['loot'][0]['claimed_by'],
            'the loser of the race overwrote who took the item');
    }

    public function testACompletedRewardIsNoLongerPendingAndIsNotRetried(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $done = $this->rewards->progress('reward-1', 1, ['char-ann', 'char-ben'],
            [['entry_index' => 0, 'character_id' => 'char-ann']], true, true, true);

        self::assertTrue($done['ok']);
        self::assertSame(MonsterRewardRepository::STATE_COMPLETE, $done['state']);

        self::assertCount(0, $this->rewards->pending('srv-1', 'ch-1'),
            'a completed reward came back as still owed');

        // Kept rather than deleted, so an operator can still see what was paid.
        self::assertNotNull($this->rewards->find('reward-1'));

        $again = $this->rewards->progress('reward-1', 2, ['char-ann']);

        self::assertFalse($again['ok']);
        self::assertSame('already_complete', $again['reason']);
    }

    public function testProgressOnARewardThatDoesNotExistIsRefused(): void
    {
        $result = $this->rewards->progress('reward-nowhere', 1, ['char-ann']);

        self::assertFalse($result['ok']);
        self::assertSame('unknown_reward', $result['reason']);
    }

    public function testTheCursorAndPublishFlagsAreCarriedAcrossProgress(): void
    {
        $this->rewards->record($this->envelope('defeat-1', 'reward-1', 'loot-1', 1),
            $this->split(), $this->darkness());

        $this->rewards->progress('reward-1', 1, [], [], true, null);

        $loaded = $this->rewards->find('reward-1');

        self::assertTrue($loaded['cursor_committed']);
        self::assertFalse($loaded['loot_published'],
            'a flag nobody set was set anyway');

        // And a later call that names only the other flag leaves the first alone.
        $this->rewards->progress('reward-1', 2, [], [], null, true);

        $after = $this->rewards->find('reward-1');

        self::assertTrue($after['cursor_committed'],
            'a committed cursor was forgotten by the next progress call');
        self::assertTrue($after['loot_published']);
    }

    public function testARefusedRecordLeavesNoChildRowsBehind(): void
    {
        $this->rewards->record($this->envelope(), $this->split(),
            [['item_definition_id' => 'item.devil_fruit.darkness', 'quantity' => 1],
             ['item_definition_id' => '', 'quantity' => 1]]);

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward_loot')->fetchColumn());

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward_experience')->fetchColumn());
    }

    public function testTheDecidedPositionSurvivesSoARecoveredPileGoesBackWhereItFell(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), $this->darkness());

        $loaded = $this->rewards->find('reward-1');

        self::assertEqualsWithDelta(1.5, $loaded['position_x'], 0.001);
        self::assertEqualsWithDelta(2.5, $loaded['position_y'], 0.001);
        self::assertEqualsWithDelta(-3.5, $loaded['position_z'], 0.001,
            'a fractional coordinate was rounded on the way to MySQL');
    }
}
