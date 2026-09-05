<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;
use ChibiFantasy\World\MonsterRewardRepository;

/**
 * What a defeat owed somebody's pet, and the evidence that the pet already had it.
 *
 * **The pet is named, never derived.** A reward records the exact instance that was out
 * when the monster died. Asking which pet is active at delivery is a different question
 * with a different answer, and every test here is built so that answering it wrongly fails.
 *
 * **Two pets of one kind are two pets.** The instance is the key, not the definition, so a
 * character owning two Lumi Slimes cannot have the wrong one paid.
 *
 * **The marker is the crash evidence.** `pet_instance.applied_reward_id` is written in the
 * same transaction as the pet's experience, which is what lets recovery tell "already paid"
 * from "still owed" without reading an experience total that two rewards could both explain.
 */
final class MonsterRewardPetExperienceTest extends BackendTestCase
{
    private MonsterRewardRepository $rewards;
    private CharacterStateRepository $states;

    protected function setUp(): void
    {
        parent::setUp();

        $this->rewards = new MonsterRewardRepository($this->pdo);
        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'petreward-a', 'not-a-real-password');

        $this->makeCharacter('char-ann', 'acc-a', 'srv-1', 'Ann');
        $this->makeCharacter('char-ben', 'acc-a', 'srv-1', 'Ben');
    }

    /** @return array<string,mixed> */
    private function envelope(string $defeat = 'defeat-1', string $reward = 'reward-1'): array
    {
        return [
            'reward_id'             => $reward,
            'defeat_id'             => $defeat,
            'server_id'             => 'srv-1',
            'channel_id'            => 'ch-1',
            'monster_definition_id' => 'monster.ancient_slime_king',
            'map_definition_id'     => 'map.harbor_town',
            'killer_character_id'   => 'char-ann',
            'loot_id'               => '',
            'loot_policy'           => 1,
            'claimant_character_id' => 'char-ann',
            'position_x'            => 0.0,
            'position_y'            => 0.0,
            'position_z'            => 0.0,
            'party_id'              => '',
            'party_cursor'          => null,
        ];
    }

    /** @return list<array{character_id:string,experience:int}> */
    private function split(): array
    {
        return [['character_id' => 'char-ann', 'experience' => 400]];
    }

    /** @return list<array<string,mixed>> */
    private function petGrant(string $pet = 'pet-1', int $amount = 100,
        string $owner = 'char-ann'): array
    {
        return [[
            'character_id'    => $owner,
            'pet_instance_id' => $pet,
            'experience'      => $amount,
        ]];
    }

    // ---- the decision --------------------------------------------------------------------

    public function testAPetGrantIsRecordedWholeAndComesBackWhole(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        self::assertTrue($saved['ok']);

        $loaded = $this->rewards->find('reward-1');

        self::assertCount(1, $loaded['pet_experience']);
        self::assertSame('char-ann', $loaded['pet_experience'][0]['character_id']);
        self::assertSame('pet-1', $loaded['pet_experience'][0]['pet_instance_id']);
        self::assertSame(100, $loaded['pet_experience'][0]['experience']);
        self::assertFalse($loaded['pet_experience'][0]['delivered']);
    }

    public function testADefeatWithNoPetOutStoresNoPetRowAtAll(): void
    {
        // Not a row of zero: a character with no pet is owed nothing, and a phantom row
        // would be a delivery that can never be made.
        $this->rewards->record($this->envelope(), $this->split(), [], []);

        self::assertSame([], $this->rewards->find('reward-1')['pet_experience']);
        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward_pet_experience')->fetchColumn());
    }

    public function testTwoPetsOfOneCharacterAreTwoRows(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), [], [
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
             'experience' => 100],
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-2',
             'experience' => 40],
        ]);

        $rows = $this->rewards->find('reward-1')['pet_experience'];

        self::assertCount(2, $rows);
        self::assertNotSame($rows[0]['pet_instance_id'], $rows[1]['pet_instance_id']);
    }

    public function testARowWithNoPetOrNoOwnerIsRefused(): void
    {
        $missingPet = $this->rewards->record($this->envelope(), $this->split(), [],
            [['character_id' => 'char-ann', 'pet_instance_id' => '', 'experience' => 10]]);

        self::assertFalse($missingPet['ok']);
        self::assertSame('invalid_pet_experience_grant', $missingPet['reason']);

        $missingOwner = $this->rewards->record($this->envelope(), $this->split(), [],
            [['character_id' => '', 'pet_instance_id' => 'pet-1', 'experience' => 10]]);

        self::assertFalse($missingOwner['ok']);

        self::assertNull($this->rewards->find('reward-1'),
            'a refused decision was written down anyway');
    }

    public function testTheSamePetTwiceInOneDefeatIsRefused(): void
    {
        $result = $this->rewards->record($this->envelope(), $this->split(), [], [
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
             'experience' => 100],
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
             'experience' => 100],
        ]);

        self::assertFalse($result['ok']);
        self::assertSame('duplicate_pet_experience_grant', $result['reason']);
    }

    public function testRecordingTheSameDefeatTwiceDoesNotDuplicateThePetGrant(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        $again = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        self::assertTrue($again['ok']);
        self::assertTrue($again['existing'], 'a second reward was minted for one defeat');

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM monster_reward_pet_experience')->fetchColumn());
    }

    // ---- delivery ---------------------------------------------------------------------------

    public function testStampingAPetMarksThatPetAndNoOther(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [], [
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-1',
             'experience' => 100],
            ['character_id' => 'char-ann', 'pet_instance_id' => 'pet-2',
             'experience' => 100],
        ]);

        $moved = $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, ['pet-1']);

        self::assertTrue($moved['ok']);

        $rows = [];

        foreach ($this->rewards->find('reward-1')['pet_experience'] as $row) {
            $rows[$row['pet_instance_id']] = $row['delivered'];
        }

        self::assertTrue($rows['pet-1']);
        self::assertFalse($rows['pet-2'], 'stamping one pet paid another');
    }

    public function testStampingTheSamePetTwiceChangesNothing(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        $first = $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, ['pet-1']);

        $when = $this->pdo->query(
            'SELECT delivered_at FROM monster_reward_pet_experience
             WHERE pet_instance_id = "pet-1"')->fetchColumn();

        $this->rewards->progress('reward-1', $first['revision'], [], [],
            null, null, false, ['pet-1']);

        self::assertSame($when, $this->pdo->query(
            'SELECT delivered_at FROM monster_reward_pet_experience
             WHERE pet_instance_id = "pet-1"')->fetchColumn(),
            'a repeated stamp moved a delivery that had already happened');
    }

    public function testAStaleRevisionCannotStampAPet(): void
    {
        // Two recovering workers. The one holding an old revision must lose, and must not
        // half-apply on the way out.
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, []);

        $stale = $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, ['pet-1']);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        self::assertFalse($this->rewards->find('reward-1')['pet_experience'][0]['delivered'],
            'a stale worker stamped a pet delivery anyway');
    }

    public function testACompletedRewardIsNotHandedBackAsPending(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, true, ['pet-1']);

        self::assertSame([], $this->rewards->pending('srv-1', 'ch-1'));
    }

    public function testAPetStillOwedKeepsTheRewardPending(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        // The character is paid; the pet is not. The reward is not finished.
        $this->rewards->progress('reward-1', $saved['revision'], ['char-ann'], [],
            null, null, false, []);

        $pending = $this->rewards->pending('srv-1', 'ch-1');

        self::assertCount(1, $pending);
        self::assertTrue($pending[0]['experience'][0]['delivered']);
        self::assertFalse($pending[0]['pet_experience'][0]['delivered'],
            'the pet was marked paid by somebody else being paid');
    }

    public function testAFailedPetGrantLeavesTheRowOwedForTheNextAttempt(): void
    {
        $saved = $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        // Nothing stamped: exactly what a world that could not save the pet reports.
        $this->rewards->progress('reward-1', $saved['revision'], [], [],
            null, null, false, []);

        $pending = $this->rewards->pending('srv-1', 'ch-1');

        self::assertCount(1, $pending);
        self::assertSame(100, $pending[0]['pet_experience'][0]['experience'],
            'the amount changed between attempts');
        self::assertSame('pet-1', $pending[0]['pet_experience'][0]['pet_instance_id'],
            'the recipient changed between attempts');
    }

    // ---- the marker on the pet -------------------------------------------------------------

    /**
     * @param list<array<string,mixed>> $pets
     * @return array<string,mixed>
     */
    private function state(array $pets, string $active = ''): array
    {
        return [
            'level' => 10, 'experience' => 0, 'current_health' => 100, 'current_mana' => 50,
            'class_id' => 'class.novice', 'job_id' => 'job.none',
            'map_id' => 'map.town', 'spawn_id' => 'spawn.town.plaza',
            'stats' => [], 'skills' => [], 'appearance' => [], 'items' => [],
            'inventory_capacity' => 30,
            'pets' => $pets,
            'active_pet_instance_id' => $active,
        ];
    }

    /** @return array<string,mixed> */
    private function pet(string $instance, int $experience = 0, string $applied = ''): array
    {
        return [
            'instance_id'       => $instance,
            'definition_id'     => 'pet.lumi_slime',
            'level'             => 1,
            'experience'        => $experience,
            'evolution_stage'   => 0,
            'revision'          => 0,
            'applied_reward_id' => $applied,
        ];
    }

    public function testThePetsExperienceAndTheRewardThatPaidItAreStoredTogether(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 100, 'reward-1')]), null);

        $loaded = $this->states->load('acc-a', 'char-ann');

        self::assertSame(100, $loaded['pets'][0]['experience']);
        self::assertSame('reward-1', $loaded['pets'][0]['applied_reward_id'],
            'the evidence that this reward was applied did not survive the round trip');
    }

    public function testAPetNoRewardHasPaidCarriesNoMarker(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([$this->pet('pet-1')]), null);

        self::assertSame('',
            $this->states->load('acc-a', 'char-ann')['pets'][0]['applied_reward_id']);

        // NULL in storage rather than an empty string, so "never paid" is one value and
        // not a second spelling of a reward id.
        self::assertNull($this->pdo->query(
            'SELECT applied_reward_id FROM pet_instance WHERE instance_id = "pet-1"')
            ->fetchColumn());
    }

    public function testTheMarkerMovesWithTheExperienceAndNeverOnItsOwn(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 100, 'reward-1')]), null);

        // A second reward, applied to the same pet. Both numbers move together because one
        // transaction writes them.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 250, 'reward-2')]), $first['save_revision']);

        $row = $this->pdo->query(
            'SELECT experience, applied_reward_id FROM pet_instance
             WHERE instance_id = "pet-1"')->fetch(\PDO::FETCH_ASSOC);

        self::assertSame(250, (int) $row['experience']);
        self::assertSame('reward-2', (string) $row['applied_reward_id']);
    }

    public function testARefusedSaveLeavesNeitherTheExperienceNorTheMarker(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 100, 'reward-1')]), null);

        $stale = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 250, 'reward-2')]), 99);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        $row = $this->pdo->query(
            'SELECT experience, applied_reward_id FROM pet_instance
             WHERE instance_id = "pet-1"')->fetch(\PDO::FETCH_ASSOC);

        self::assertSame(100, (int) $row['experience'],
            'a refused save applied the experience anyway');
        self::assertSame('reward-1', (string) $row['applied_reward_id'],
            'a refused save moved the evidence of what had been paid');
    }

    public function testTwoPetsOfOneKindKeepTheirOwnMarkers(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->pet('pet-1', 100, 'reward-1'),
            $this->pet('pet-2', 0),
        ]), null);

        $markers = [];

        foreach ($this->states->load('acc-a', 'char-ann')['pets'] as $pet) {
            $markers[$pet['instance_id']] = $pet['applied_reward_id'];
        }

        self::assertSame('reward-1', $markers['pet-1']);
        self::assertSame('', $markers['pet-2'],
            'a reward paid to one pet was recorded against its twin');
    }

    public function testAPetRewardCarriesNoRuntimeStateAndNoSecrets(): void
    {
        $this->rewards->record($this->envelope(), $this->split(), [],
            $this->petGrant('pet-1', 100));

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM monster_reward_pet_experience')
            ->fetch(\PDO::FETCH_ASSOC));

        foreach (['position', 'connection', 'networkobject', 'follower', 'token',
                  'password', 'secret', 'definition'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'a pet reward row carries ' . $forbidden);
            }
        }
    }
}
