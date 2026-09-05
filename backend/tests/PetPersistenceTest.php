<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;

/**
 * A character's pets, and which one is out.
 *
 * **Owned, not carried.** A pet has no bag slot and no item row -- Phase 12 made it an owned
 * entity, and the schema says the same thing by the absence of the relationship. Nothing
 * here turns one into an item.
 *
 * **Level one is still a pet.** Whether a row exists is decided by the character owning one,
 * never by its numbers being interesting. Phase 18.16A found exactly that mistake in
 * equipment serialization, so it is pinned here rather than trusted.
 *
 * **A save is a revision.** `save()` reads a null expected revision as "never saved before",
 * so every second save threads the revision the first returned, as the running server does.
 */
final class PetPersistenceTest extends BackendTestCase
{
    private CharacterStateRepository $states;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'pets-a', 'not-a-real-password');
        $this->makeAccount('acc-b', 'pets-b', 'not-a-real-password');

        $this->makeCharacter('char-a1', 'acc-a', 'srv-1', 'Ayla');
        $this->makeCharacter('char-a2', 'acc-a', 'srv-1', 'Alma');
        $this->makeCharacter('char-b1', 'acc-b', 'srv-1', 'Bryn');
    }

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
            'stats' => [['stat_id' => 'stat.strength', 'value' => 10]],
            'skills' => [], 'appearance' => [], 'items' => [],
            'inventory_capacity' => 30,
            'pets' => $pets,
            'active_pet_instance_id' => $active,
        ];
    }

    /** @return array<string,mixed> */
    private function pet(string $instance, string $definition = 'pet.lumi_slime',
        int $level = 1, int $experience = 0, int $stage = 0): array
    {
        return [
            'instance_id'     => $instance,
            'definition_id'   => $definition,
            'level'           => $level,
            'experience'      => $experience,
            'evolution_stage' => $stage,
            'revision'        => 0,
        ];
    }

    // ---- owning one ------------------------------------------------------------------

    public function testAPetSurvivesTheRoundTrip(): void
    {
        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 3, 260, 1)]), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['pets']);

        $pet = $loaded['pets'][0];

        self::assertSame('pet-1', $pet['instance_id']);
        self::assertSame('pet.lumi_slime', $pet['definition_id']);
        self::assertSame(3, $pet['level']);
        self::assertSame(260, $pet['experience']);
        self::assertSame(1, $pet['evolution_stage']);
    }

    public function testAPetAtLevelOneWithNothingEarnedIsStillWrittenDown(): void
    {
        // The Phase 18.16A defect, in its pet-shaped form: a row must exist because the
        // character owns a pet, never because its numbers are interesting.
        $this->states->save('acc-a', 'char-a1', $this->state([$this->pet('pet-1')]), null);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['pets'], 'a default-valued pet was not persisted');
        self::assertSame(1, $loaded['pets'][0]['level']);
        self::assertSame(0, $loaded['pets'][0]['experience']);
        self::assertSame(0, $loaded['pets'][0]['evolution_stage']);
    }

    public function testTwoPetsOfTheSameKindStayDistinct(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->state([
            $this->pet('pet-1', 'pet.lumi_slime', 4, 500),
            $this->pet('pet-2', 'pet.lumi_slime', 1, 0),
        ]), null);

        $pets = $this->states->load('acc-a', 'char-a1')['pets'];

        self::assertCount(2, $pets);

        $byId = [];

        foreach ($pets as $pet) {
            $byId[$pet['instance_id']] = $pet;
        }

        self::assertSame(4, $byId['pet-1']['level']);
        self::assertSame(1, $byId['pet-2']['level'],
            'two copies of one kind were collapsed into each other');
    }

    public function testAPetNoLongerOwnedIsRemoved(): void
    {
        $first = $this->states->save('acc-a', 'char-a1', $this->state([
            $this->pet('pet-1'), $this->pet('pet-2'),
        ]), null);

        $this->states->save('acc-a', 'char-a1', $this->state([$this->pet('pet-1')]),
            $first['save_revision']);

        $pets = $this->states->load('acc-a', 'char-a1')['pets'];

        self::assertCount(1, $pets, 'a released pet was still owned');
        self::assertSame('pet-1', $pets[0]['instance_id']);
    }

    public function testARowWithNoIdentityOrNoKindIsNotAPet(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->state([
            ['instance_id' => '', 'definition_id' => 'pet.lumi_slime'],
            ['instance_id' => 'pet-9', 'definition_id' => ''],
        ]), null);

        self::assertSame(0, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM pet_instance')->fetchColumn(),
            'a malformed pet row was written anyway');
    }

    // ---- which one is out --------------------------------------------------------------

    public function testTheActivePetSurvivesTheRoundTrip(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->state([
            $this->pet('pet-1'), $this->pet('pet-2'),
        ], 'pet-2'), null);

        self::assertSame('pet-2',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id']);
    }

    public function testOwningPetsWithNoneOutIsAnAnswerAndNotAMissingValue(): void
    {
        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], ''), null);

        self::assertSame('',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id']);
    }

    public function testPuttingAPetAwayPersists(): void
    {
        $first = $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), null);

        self::assertSame('pet-1',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id']);

        $this->states->save('acc-a', 'char-a1', $this->state([$this->pet('pet-1')], ''),
            $first['save_revision']);

        self::assertSame('',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id'],
            'the pet was still out after being put away');
    }

    public function testSwitchingWhichPetIsOutPersists(): void
    {
        $first = $this->states->save('acc-a', 'char-a1', $this->state([
            $this->pet('pet-1'), $this->pet('pet-2'),
        ], 'pet-1'), null);

        $this->states->save('acc-a', 'char-a1', $this->state([
            $this->pet('pet-1'), $this->pet('pet-2'),
        ], 'pet-2'), $first['save_revision']);

        self::assertSame('pet-2',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id']);
    }

    public function testAPetThisCharacterDoesNotOwnCannotBeTheOneThatIsOut(): void
    {
        // Somebody else's pet, named as active. The foreign key could only say the row
        // exists; ownership is what actually decides, and it is checked here.
        $this->states->save('acc-b', 'char-b1', $this->state([$this->pet('pet-b')]), null);

        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-b'), null);

        self::assertSame('',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id'],
            "a character was given somebody else's pet to hold");
    }

    public function testAnActiveReferenceToANonexistentPetIsDropped(): void
    {
        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-nowhere'), null);

        self::assertSame('',
            $this->states->load('acc-a', 'char-a1')['active_pet_instance_id']);
    }

    public function testReleasingTheActivePetLeavesNothingDangling(): void
    {
        $first = $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), null);

        // The pet is gone from the collection, so the selection cannot survive it.
        $this->states->save('acc-a', 'char-a1', $this->state([], ''),
            $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertSame([], $loaded['pets']);
        self::assertSame('', $loaded['active_pet_instance_id'],
            'the active selection outlived the pet it pointed at');
    }

    // ---- isolation and repetition ---------------------------------------------------------

    public function testTwoCharactersOnOneAccountDoNotShareAPet(): void
    {
        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), null);

        $this->states->save('acc-a', 'char-a2', $this->state([]), null);

        self::assertCount(1, $this->states->load('acc-a', 'char-a1')['pets']);
        self::assertCount(0, $this->states->load('acc-a', 'char-a2')['pets'],
            'a pet followed the account instead of the character');
    }

    public function testSavingTheSamePetsTwiceDuplicatesNothing(): void
    {
        $first = $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), null);

        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), $first['save_revision']);

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM pet_instance')->fetchColumn());
    }

    public function testARefusedSaveLeavesThePetsExactlyAsTheyWere(): void
    {
        $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 5, 900)], 'pet-1'), null);

        $stale = $this->states->save('acc-a', 'char-a1', $this->state([]), 99);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['pets'], 'a refused save released a pet');
        self::assertSame(5, $loaded['pets'][0]['level']);
        self::assertSame('pet-1', $loaded['active_pet_instance_id']);
    }

    public function testASaveThatSaysNothingAboutPetsLeavesThemAlone(): void
    {
        // An older caller, or one that only touched stats. Absent is not the same as empty.
        $first = $this->states->save('acc-a', 'char-a1',
            $this->state([$this->pet('pet-1')], 'pet-1'), null);

        $withoutPets = $this->state([], '');
        unset($withoutPets['pets'], $withoutPets['active_pet_instance_id']);

        $this->states->save('acc-a', 'char-a1', $withoutPets, $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-a1');

        self::assertCount(1, $loaded['pets'],
            'a save that never mentioned pets released them');
        self::assertSame('pet-1', $loaded['active_pet_instance_id']);
    }

    public function testNoGameplayRuntimeStateIsStoredOnAPet(): void
    {
        $this->states->save('acc-a', 'char-a1', $this->state([$this->pet('pet-1')]), null);

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM pet_instance')->fetch(\PDO::FETCH_ASSOC));

        foreach (['position', 'transform', 'connection', 'networkobject', 'offset',
                  'vfx', 'renderer', 'prefab'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'a pet row carries runtime state');
            }
        }
    }
}
