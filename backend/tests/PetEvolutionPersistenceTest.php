<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Character\CharacterStateRepository;

/**
 * A pet that has evolved, written down and read back.
 *
 * **The same creature, changed.** An evolution repoints a pet at a new authored form and
 * counts how far it has come; it does not mint a second pet. So the instance id is what
 * every test here holds on to, and the definition and stage are what move.
 *
 * **Storage keeps no opinion about forms.** There is no evolved-pet table, no chain, and no
 * validation of which form may follow which -- that is authored content, and the world
 * refuses a row that disagrees with it. What the backend owes is an exact round trip.
 */
final class PetEvolutionPersistenceTest extends BackendTestCase
{
    private CharacterStateRepository $states;

    protected function setUp(): void
    {
        parent::setUp();

        $this->states = new CharacterStateRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeAccount('acc-a', 'evolve-a', 'not-a-real-password');

        $this->makeCharacter('char-ann', 'acc-a', 'srv-1', 'Ann');
        $this->makeCharacter('char-ben', 'acc-a', 'srv-1', 'Ben');
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
            'stats' => [], 'skills' => [], 'appearance' => [], 'items' => [],
            'inventory_capacity' => 30,
            'pets' => $pets,
            'active_pet_instance_id' => $active,
        ];
    }

    /** @return array<string,mixed> */
    private function pet(string $instance, string $definition, int $level = 1,
        int $experience = 0, int $stage = 0): array
    {
        return [
            'instance_id' => $instance, 'definition_id' => $definition,
            'level' => $level, 'experience' => $experience, 'evolution_stage' => $stage,
            'revision' => 0, 'applied_reward_id' => '',
        ];
    }

    // ---- the round trip -----------------------------------------------------------------

    public function testAnEvolvedPetKeepsItsIdentityAndCarriesItsNewForm(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 4, 300)]), null);

        // The same pet, evolved: one row, a new definition, a stage of one.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)]),
            $first['save_revision']);

        $pets = $this->states->load('acc-a', 'char-ann')['pets'];

        self::assertCount(1, $pets, 'evolving produced a second pet row');
        self::assertSame('pet-1', $pets[0]['instance_id'],
            'the evolved pet is not the pet that evolved');
        self::assertSame('pet.lumi_slime_evolved', $pets[0]['definition_id']);
        self::assertSame(1, $pets[0]['evolution_stage']);
        self::assertSame(300, $pets[0]['experience'],
            'the pet lost what it had earned');
        self::assertSame(4, $pets[0]['level']);
    }

    public function testAnActiveEvolvedPetIsStillTheActivePet(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), null);

        $loaded = $this->states->load('acc-a', 'char-ann');

        self::assertSame('pet-1', $loaded['active_pet_instance_id']);
        self::assertSame(1, $loaded['pets'][0]['evolution_stage']);
    }

    public function testAnEvolvedPetThatWasPutAwayStaysEvolved(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), null);

        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)], ''),
            $first['save_revision']);

        $loaded = $this->states->load('acc-a', 'char-ann');

        self::assertSame('', $loaded['active_pet_instance_id']);
        self::assertSame(1, $loaded['pets'][0]['evolution_stage'],
            'putting an evolved pet away undid its evolution');
    }

    public function testAnEvolvedAndAnUnevolvedPetCoexist(): void
    {
        $this->states->save('acc-a', 'char-ann', $this->state([
            $this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1),
            $this->pet('pet-2', 'pet.lumi_slime'),
        ], 'pet-1'), null);

        $byId = [];

        foreach ($this->states->load('acc-a', 'char-ann')['pets'] as $pet) {
            $byId[$pet['instance_id']] = $pet;
        }

        self::assertSame(1, $byId['pet-1']['evolution_stage']);
        self::assertSame(0, $byId['pet-2']['evolution_stage']);
        self::assertSame('pet.lumi_slime', $byId['pet-2']['definition_id'],
            'one pet evolving changed another');
    }

    public function testSavingAnEvolvedPetTwiceChangesNothing(): void
    {
        $first = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), null);

        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), $first['save_revision']);

        self::assertSame(1, (int) $this->pdo
            ->query('SELECT COUNT(*) FROM pet_instance')->fetchColumn());
        self::assertSame(1,
            $this->states->load('acc-a', 'char-ann')['pets'][0]['evolution_stage'],
            'a repeat advanced the stage');
    }

    public function testARefusedSaveLeavesThePetUnevolved(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 4, 300)]), null);

        $stale = $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)]), 99);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        $pets = $this->states->load('acc-a', 'char-ann')['pets'];

        self::assertSame('pet.lumi_slime', $pets[0]['definition_id'],
            'a refused save evolved the pet anyway');
        self::assertSame(0, $pets[0]['evolution_stage']);
    }

    public function testAnImpossibleStageIsStoredAsGivenForTheWorldToRefuse(): void
    {
        // Storage keeps no chain and cannot know which form follows which. It stores what
        // it is told, and the world refuses a row that disagrees with content -- which is
        // asserted where that decision lives, in the spawn path.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 4, 300, 7)]), null);

        self::assertSame(7,
            $this->states->load('acc-a', 'char-ann')['pets'][0]['evolution_stage'],
            'the backend quietly changed a stage it was given');
    }

    public function testANegativeStageIsFlooredRatherThanStoredAsNonsense(): void
    {
        // The column is unsigned; the repository floors rather than letting MySQL refuse
        // the whole save. The world still refuses the row on the way in.
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime', 4, 300, -3)]), null);

        self::assertSame(0,
            $this->states->load('acc-a', 'char-ann')['pets'][0]['evolution_stage']);
    }

    public function testOneCharactersEvolvedPetIsNotAnothers(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), null);

        $this->states->save('acc-a', 'char-ben',
            $this->state([$this->pet('pet-2', 'pet.lumi_slime')]), null);

        self::assertSame(1,
            $this->states->load('acc-a', 'char-ann')['pets'][0]['evolution_stage']);
        self::assertSame(0,
            $this->states->load('acc-a', 'char-ben')['pets'][0]['evolution_stage'],
            'an evolution followed the account instead of the character');
    }

    public function testNoBuffOrFormStateIsStoredBesideThePet(): void
    {
        $this->states->save('acc-a', 'char-ann',
            $this->state([$this->pet('pet-1', 'pet.lumi_slime_evolved', 4, 300, 1)],
                'pet-1'), null);

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM pet_instance')->fetch(\PDO::FETCH_ASSOC));

        // The buff is a status effect the world applies from content while the pet is out.
        // Storing one here would be a second source of truth for what a pet grants.
        foreach (['buff', 'status', 'aura', 'effect', 'modifier'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'pet storage carries ' . $forbidden);
            }
        }
    }
}
