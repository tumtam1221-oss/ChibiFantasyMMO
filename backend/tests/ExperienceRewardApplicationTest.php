<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;
use ChibiFantasy\World\MonsterRewardRepository;

/**
 * Durable evidence that one reward has already changed one recipient.
 *
 * **Why a marker was not enough.** Two rewards can be outstanding for the same character
 * and the same pet at once, and a recipient that remembers only the last one it received
 * has lost the evidence for the earlier one -- which is still owed, and would be paid a
 * second time. So the evidence is a row per reward, and these tests are mostly about pairs
 * of rewards coexisting without erasing each other.
 *
 * **Two transactions, and which owns what.** The character's save writes the progression
 * and the application together; the reward's progress stamps the delivery and retires the
 * application together. Every test below checks one of those two boundaries.
 *
 * **Never keyed on a number.** Two rewards can be worth exactly the same and leave a
 * recipient on exactly the same total, so amounts and totals appear here only as things
 * that must *not* be able to tell rewards apart.
 */
final class ExperienceRewardApplicationTest extends BackendTestCase
{
    private CharacterStateRepository $states;
    private MonsterRewardRepository $rewards;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);
        $this->rewards = new MonsterRewardRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'ledger-a', 'not-a-real-password');

        $this->makeCharacter('char-ann', 'acc-a', 'srv-1', 'Ann');
        $this->makeCharacter('char-ben', 'acc-a', 'srv-1', 'Ben');
    }

    /**
     * @param list<array<string,mixed>> $applications
     * @param list<array<string,mixed>> $pets
     * @return array<string,mixed>
     */
    private function state(array $applications = [], array $pets = [],
        int $experience = 0): array
    {
        return [
            'level' => 10, 'experience' => $experience,
            'current_health' => 100, 'current_mana' => 50,
            'class_id' => 'class.novice', 'job_id' => 'job.none',
            'map_id' => 'map.town', 'spawn_id' => 'spawn.town.plaza',
            'stats' => [], 'skills' => [], 'appearance' => [], 'items' => [],
            'inventory_capacity' => 30,
            'pets' => $pets,
            'active_pet_instance_id' => '',
            'reward_applications' => $applications,
        ];
    }

    /** @return array<string,mixed> */
    private function pet(string $instance, int $experience = 0): array
    {
        return [
            'instance_id' => $instance, 'definition_id' => 'pet.lumi_slime',
            'level' => 1, 'experience' => $experience, 'evolution_stage' => 0,
            'revision' => 0, 'applied_reward_id' => '',
        ];
    }

    /** @return array<string,mixed> */
    private function applied(string $rewardId, string $pet = '', int $level = 0,
        int $experience = 0): array
    {
        return [
            'reward_id' => $rewardId,
            'pet_instance_id' => $pet,
            'resulting_level' => $level,
            'resulting_experience' => $experience,
        ];
    }

    /** @return array<string,mixed> */
    private function envelope(string $reward, string $defeat): array
    {
        return [
            'reward_id' => $reward, 'defeat_id' => $defeat,
            'server_id' => 'srv-1', 'channel_id' => 'ch-1',
            'monster_definition_id' => 'monster.ancient_slime_king',
            'map_definition_id' => 'map.harbor_town',
            'killer_character_id' => 'char-ann',
            'loot_id' => '', 'loot_policy' => 1, 'claimant_character_id' => 'char-ann',
            'position_x' => 0.0, 'position_y' => 0.0, 'position_z' => 0.0,
            'party_id' => '', 'party_cursor' => null,
        ];
    }

    /** @return list<string> */
    private function applications(string $character = 'char-ann'): array
    {
        $keys = [];

        foreach ($this->states->load('acc-a', $character)['reward_applications'] as $row) {
            $keys[] = $row['reward_id'] . '/' . $row['pet_instance_id'];
        }

        sort($keys);

        return $keys;
    }

    // ---- written with the progression ------------------------------------------------

    public function testACharacterApplicationIsStoredWithTheExperienceItDescribes(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1', '', 12, 340)], [], 340), null);

        $loaded = $this->states->load('acc-a', 'char-ann');

        self::assertSame(['reward-1/'], $this->applications());
        self::assertSame(340, $loaded['experience'],
            'the progression and its evidence did not arrive together');

        $row = $this->pdo->query(
            'SELECT resulting_level, resulting_experience
             FROM character_experience_application WHERE reward_id = "reward-1"')
            ->fetch(\PDO::FETCH_ASSOC);

        self::assertSame(12, (int) $row['resulting_level']);
        self::assertSame(340, (int) $row['resulting_experience']);
    }

    public function testAPetApplicationIsStoredAgainstTheExactInstance(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state(
            [$this->applied('reward-1', 'pet-1')],
            [$this->pet('pet-1', 25), $this->pet('pet-2')]), null);

        self::assertSame(['reward-1/pet-1'], $this->applications());

        $row = $this->pdo->query(
            'SELECT pet_instance_id, character_id FROM pet_experience_application')
            ->fetch(\PDO::FETCH_ASSOC);

        self::assertSame('pet-1', (string) $row['pet_instance_id']);
        self::assertSame('char-ann', (string) $row['character_id']);
    }

    public function testRecordingTheSameApplicationTwiceChangesNothing(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        $when = $this->pdo->query(
            'SELECT applied_at FROM character_experience_application')->fetchColumn();

        // The same save sent again, as a retry does.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), $first['save_revision']);

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM character_experience_application')->fetchColumn());
        self::assertSame($when, $this->pdo->query(
            'SELECT applied_at FROM character_experience_application')->fetchColumn(),
            'a repeat rewrote the moment the reward was applied');
    }

    public function testARefusedSaveRecordsNoApplicationAtAll(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([], [], 100), null);

        // A stale revision: the whole transaction rolls back, evidence included.
        $stale = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')], [], 500), 99);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        self::assertSame([], $this->applications(),
            'a refused save claimed a reward had been applied');
        self::assertSame(100, $this->states->load('acc-a', 'char-ann')['experience'],
            'a refused save changed the progression');
    }

    // ---- two rewards at once ------------------------------------------------------------

    public function testTwoRewardsForOneCharacterCoexist(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        // The second reward arrives while the first is still unstamped. Nothing about the
        // first may be erased by it.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1'), $this->applied('reward-2')]),
            $first['save_revision']);

        self::assertSame(['reward-1/', 'reward-2/'], $this->applications());
    }

    public function testTwoRewardsWorthTheSameAreTwoApplications(): void
    {
        // Identical resulting numbers, different reward ids. Only the identity can tell
        // them apart, which is the property being pinned.
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1', '', 10, 500),
            $this->applied('reward-2', '', 10, 500),
        ]), null);

        self::assertSame(['reward-1/', 'reward-2/'], $this->applications());
        self::assertSame(2, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM character_experience_application')->fetchColumn());
    }

    public function testOnePetCanCarryTwoRewardsAtOnce(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1', 'pet-1'),
            $this->applied('reward-2', 'pet-1'),
        ], [$this->pet('pet-1', 50)]), null);

        self::assertSame(['reward-1/pet-1', 'reward-2/pet-1'], $this->applications(),
            'a later reward erased the evidence for an earlier one');
    }

    public function testTwoPetsOfOneKindKeepTheirOwnApplications(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1', 'pet-1'),
            $this->applied('reward-2', 'pet-2'),
        ], [$this->pet('pet-1', 25), $this->pet('pet-2', 25)]), null);

        self::assertSame(['reward-1/pet-1', 'reward-2/pet-2'], $this->applications());
    }

    public function testACharacterAndTheirPetAreTwoRecipientsOfOneReward(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1'),
            $this->applied('reward-1', 'pet-1'),
        ], [$this->pet('pet-1', 25)]), null);

        self::assertSame(['reward-1/', 'reward-1/pet-1'], $this->applications(),
            'one reward cannot be applied to a character and their pet independently');
    }

    public function testOneCharactersApplicationsAreNotAnothers(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        $this->states->save('acc-a', 'char-ben', $this->state([]), null);

        self::assertSame(['reward-1/'], $this->applications('char-ann'));
        self::assertSame([], $this->applications('char-ben'));
    }

    // ---- retired by the stamp ---------------------------------------------------------------

    public function testStampingADeliveryRetiresExactlyThatApplication(): void
    {
        $this->rewards->record($this->envelope('reward-1', 'defeat-1'),
            [['character_id' => 'char-ann', 'experience' => 100]], [],
            [['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
              'experience' => 25]]);

        $saved = $this->rewards->record($this->envelope('reward-2', 'defeat-2'),
            [['character_id' => 'char-ann', 'experience' => 100]], [], []);

        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1'),
            $this->applied('reward-1', 'pet-1'),
            $this->applied('reward-2'),
        ], [$this->pet('pet-1', 25)]), null);

        $reward = $this->rewards->find('reward-1');

        $this->rewards->progress('reward-1', $reward['revision'], ['char-ann'], [],
            null, null, true, ['pet-1']);

        // Reward one's evidence is gone, because its delivery is now stamped. Reward
        // two's is untouched: it is still owed.
        self::assertSame(['reward-2/'], $this->applications(),
            'stamping one reward retired another reward\'s evidence');
    }

    public function testAnUnstampedDeliveryKeepsItsEvidence(): void
    {
        $saved = $this->rewards->record($this->envelope('reward-1', 'defeat-1'),
            [['character_id' => 'char-ann', 'experience' => 100]], [],
            [['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
              'experience' => 25]]);

        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->applied('reward-1'),
            $this->applied('reward-1', 'pet-1'),
        ], [$this->pet('pet-1', 25)]), null);

        // The character is stamped; the pet is not.
        $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, false, []);

        self::assertSame(['reward-1/pet-1'], $this->applications(),
            'the pet lost the evidence that it had already been paid');

        $pending = $this->rewards->pending('srv-1', 'ch-1');

        self::assertCount(1, $pending);
        self::assertFalse($pending[0]['pet_experience'][0]['delivered']);
    }

    public function testAStaleStampRetiresNothing(): void
    {
        $saved = $this->rewards->record($this->envelope('reward-1', 'defeat-1'),
            [['character_id' => 'char-ann', 'experience' => 100]], [], []);

        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, []);

        // A second worker holding the old revision. It must change nothing at all.
        $stale = $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, false, []);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        self::assertSame(['reward-1/'], $this->applications(),
            'a stale worker retired evidence the winner still needs');
    }

    public function testTwoWorkersStampingTheSameDeliveryRetireItOnce(): void
    {
        $saved = $this->rewards->record($this->envelope('reward-1', 'defeat-1'),
            [['character_id' => 'char-ann', 'experience' => 100]], [], []);

        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        $first = $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, false, []);

        self::assertTrue($first['ok']);

        // The winner's own retry, with the revision it now holds. Idempotent, and there is
        // nothing left to retire.
        $again = $this->rewards->progress('reward-1', $first['revision'], ['char-ann'], [],
            null, null, false, []);

        self::assertTrue($again['ok']);
        self::assertSame([], $this->applications());
        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM character_experience_application')->fetchColumn());
    }

    public function testACompletedRewardIsNotHandedBackAsPending(): void
    {
        $saved = $this->rewards->record($this->envelope('reward-1', 'defeat-1'),
            [['character_id' => 'char-ann', 'experience' => 100]], [], []);

        $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, true, []);

        self::assertSame([], $this->rewards->pending('srv-1', 'ch-1'));
    }

    // ---- shape --------------------------------------------------------------------------------

    public function testAnApplicationCarriesNoAmountToDecideWith(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1', '', 5, 50)]), null);

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM character_experience_application')
            ->fetch(\PDO::FETCH_ASSOC));

        // The identity is the key. The numbers beside it are diagnostic, named so that
        // nothing reads them as "the amount this reward was worth".
        self::assertContains('reward_id', $columns);
        self::assertContains('character_id', $columns);
        self::assertNotContains('experience', $columns);
        self::assertNotContains('amount', $columns);

        foreach (['token', 'password', 'secret', 'session'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column);
            }
        }
    }

    public function testTheIdentityIsRefusedTwiceByTheDatabaseItself(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->applied('reward-1')]), null);

        // Straight at the table, as a second worker racing the first would be. The primary
        // key is what makes one reward one application, not a check in PHP.
        $this->expectException(\PDOException::class);

        $this->pdo->prepare(
            'INSERT INTO character_experience_application
                (reward_id, character_id, resulting_level, resulting_experience, applied_at)
             VALUES ("reward-1", "char-ann", 0, 0, NOW(3))'
        )->execute();
    }
}
