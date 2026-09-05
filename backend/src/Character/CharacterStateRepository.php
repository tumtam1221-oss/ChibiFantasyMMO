<?php

declare(strict_types=1);

namespace ChibiFantasy\Character;

use PDO;

/**
 * Loading and saving everything a world server needs a character to be.
 *
 * **Why this is separate from CharacterRepository.** That one answers character
 * select: a list, scoped by account, cheap and read-only. This one is the world's
 * -- one character, every aggregate, written back under optimistic concurrency.
 * Merging them would put a five-table transaction behind the query that runs on
 * every login screen.
 *
 * **Three rules govern every method here.**
 *
 * *Ownership is a WHERE clause, never a check.* Every statement that touches a
 * character is scoped by `account_id` in SQL. There is no method that loads a
 * character by id alone, so "load somebody else's character" is not something the
 * caller can express, let alone something a validation could forget.
 *
 * *A save presents the revisions it loaded.* Each Unity aggregate carries its own
 * Revision and they advance independently. A save that arrives with a stale
 * `save_revision` is refused whole -- it does not overwrite the newer state and it
 * does not partially apply.
 *
 * *Nothing is written outside a transaction.* A character is five tables. Writing
 * stats and failing on skills would leave a character that never existed.
 */
final class CharacterStateRepository
{
    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The whole character, or null if this account does not own it.
     *
     * Returns null both for "no such character" and "somebody else's character",
     * exactly as `CharacterRepository::findOwned` does, so a caller cannot tell the
     * two apart and a response cannot confirm that another player's character exists.
     *
     * @return array<string,mixed>|null
     */
    public function load(string $accountId, string $characterId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT character_id, account_id, server_id, name, gender, level, experience,
                    current_health, current_mana, class_definition_id, job_definition_id,
                    map_definition_id, spawn_definition_id, appearance_definition_id,
                    active_pet_instance_id, availability, revision
             FROM `character`
             WHERE character_id = :cid AND account_id = :aid'
        );

        $statement->execute([':cid' => $characterId, ':aid' => $accountId]);

        $row = $statement->fetch();

        if ($row === false) {
            return null;
        }

        return [
            'character_id'  => (string) $row['character_id'],
            'account_id'    => (string) $row['account_id'],
            'server_id'     => (string) $row['server_id'],
            'name'          => (string) $row['name'],
            'gender'        => (int) $row['gender'],
            'level'         => (int) $row['level'],
            'experience'    => (int) $row['experience'],
            'current_health' => (int) $row['current_health'],
            'current_mana'  => (int) $row['current_mana'],
            'class_id'      => (string) $row['class_definition_id'],
            'job_id'        => (string) $row['job_definition_id'],
            'map_id'        => (string) $row['map_definition_id'],
            'spawn_id'      => (string) $row['spawn_definition_id'],
            'appearance_id' => (string) $row['appearance_definition_id'],
            'availability'  => (int) $row['availability'],
            'revision'      => (int) $row['revision'],

            'stats'         => $this->loadStats($characterId),
            'appearance'    => $this->loadAppearance($characterId),
            'skills'        => $this->loadSkills($characterId),
            'items'         => array_merge(
                $this->loadInventory($characterId),
                $this->loadEquipment($accountId, $characterId)
            ),
            'inventory_capacity' => $this->loadInventoryCapacity($characterId),
            'devil_fruit'   => $this->loadDevilFruit($characterId)['fruit'],
            'devil_fruit_source' => $this->loadDevilFruit($characterId)['source'],

            'pets'          => $this->loadPets($characterId),
            'active_pet_instance_id' => $row['active_pet_instance_id'] === null
                ? '' : (string) $row['active_pet_instance_id'],

            'revisions'     => $this->loadRevisions($characterId),
        ];
    }

    /**
     * The Devil Fruit this character owns, or empty strings for none.
     *
     * Keyed by a character-scoped owner id, the same shape equipment already uses. A
     * fruit belongs to the character who ate it and not to the account: keying this by
     * account would give every character on it the same power, and the primary key would
     * silently stop a second character from ever eating one.
     *
     * Only the stable definition id and the spent instance come back. The modifiers, the
     * ability and the immunities live in authored content, so a balance change reaches
     * every existing owner instead of leaving copies behind in rows nobody rewrites.
     *
     * @return array{fruit:string,source:string}
     */
    private function loadDevilFruit(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT fruit_definition_id, source_instance_id
             FROM character_devil_fruit
             WHERE owner_id = :oid'
        );

        $statement->execute([':oid' => $this->devilFruitOwnerId($characterId)]);

        $row = $statement->fetch();

        if ($row === false) {
            return ['fruit' => '', 'source' => ''];
        }

        return [
            'fruit'  => (string) $row['fruit_definition_id'],
            'source' => (string) $row['source_instance_id'],
        ];
    }

    /**
     * Writes the character's fruit, or removes it when they own none.
     *
     * Inside the caller's transaction, like every other write in a save: a character
     * whose stats were stored but whose fruit was not would be a character who paid for
     * something they no longer have.
     */
    private function writeDevilFruit(string $characterId, array $state): void
    {
        $fruit = trim((string) ($state['devil_fruit'] ?? ''));
        $owner = $this->devilFruitOwnerId($characterId);

        if ($fruit === '') {
            $delete = $this->pdo->prepare(
                'DELETE FROM character_devil_fruit WHERE owner_id = :oid'
            );

            $delete->execute([':oid' => $owner]);

            return;
        }

        // Upsert rather than delete-then-insert: the row's revision and activated_at are
        // a record of when a permanent thing happened, and re-inserting would reset both
        // every time the character was saved for any reason at all.
        $statement = $this->pdo->prepare(
            'INSERT INTO character_devil_fruit
                (owner_id, fruit_definition_id, source_instance_id, revision, activated_at)
             VALUES (:oid, :fid, :sid, 1, NOW(3))
             ON DUPLICATE KEY UPDATE
                fruit_definition_id = VALUES(fruit_definition_id),
                source_instance_id = VALUES(source_instance_id),
                revision = revision + 1'
        );

        $statement->execute([
            ':oid' => $owner,
            ':fid' => $fruit,
            ':sid' => (string) ($state['devil_fruit_source'] ?? ''),
        ]);
    }

    /** Character-scoped, exactly as equipment ownership already is. */
    private function devilFruitOwnerId(string $characterId): string
    {
        return 'df:' . $characterId;
    }

    /** @return list<array{stat_id:string,value:int}> */
    private function loadStats(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT stat_definition_id, value FROM character_stat
             WHERE character_id = :cid ORDER BY stat_definition_id ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'stat_id' => (string) $r['stat_definition_id'],
                'value'   => (int) $r['value'],
            ],
            $statement->fetchAll()
        );
    }

    /** @return list<array{slot:int,option_id:string}> */
    private function loadAppearance(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT slot, option_definition_id FROM character_appearance
             WHERE character_id = :cid ORDER BY slot ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'slot'      => (int) $r['slot'],
                'option_id' => (string) $r['option_definition_id'],
            ],
            $statement->fetchAll()
        );
    }

    /** @return list<array{skill_id:string,level:int}> */
    private function loadSkills(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT skill_definition_id, skill_level FROM character_skill
             WHERE character_id = :cid ORDER BY skill_definition_id ASC'
        );

        $statement->execute([':cid' => $characterId]);

        return array_map(
            static fn (array $r): array => [
                'skill_id' => (string) $r['skill_definition_id'],
                'level'    => (int) $r['skill_level'],
            ],
            $statement->fetchAll()
        );
    }

    /**
     * What is in a character's bag, in slot order.
     *
     * A join rather than a blob: the container is rows, so one item can be located,
     * locked and moved without rewriting a whole inventory. Ordering by slot means the
     * bag arrives arranged as the player left it.
     *
     * @return list<array{instance_id:string,item_id:string,quantity:int,slot:int,lock_state:int}>
     */
    private function loadInventory(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT s.slot_index, i.instance_id, i.definition_id, i.quantity, i.lock_state,
                    e.enhancement_level, e.rarity_definition_id
             FROM container_slot s
             INNER JOIN item_instance i ON i.instance_id = s.instance_id
             LEFT JOIN equipment_instance e ON e.instance_id = i.instance_id
             WHERE s.container_id = :cid
             ORDER BY s.slot_index ASC'
        );

        $statement->execute([':cid' => $this->containerId($characterId)]);

        $items = [];

        foreach ($statement->fetchAll() as $row) {
            $items[] = $this->itemRow($row, (int) $row['slot_index'], 0);
        }

        return $items;
    }

    /**
     * What a character is wearing, as rows in the same shape as bagged items.
     *
     * One list, two homes: a worn piece and a bagged item are both `item_instance`
     * rows, and what separates them is the equipment slot. The unique keys on
     * `container_slot.instance_id` and `character_equipment.instance_id` are what make
     * "an item is in a bag or worn, never both" a database guarantee.
     *
     * Scoped by owner rather than character, because that is what the table is keyed
     * by -- and the caller has already proved the character belongs to that account.
     *
     * @return list<array<string,mixed>>
     */
    private function loadEquipment(string $accountId, string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT c.slot, i.instance_id, i.definition_id, i.quantity, i.lock_state,
                    e.enhancement_level, e.rarity_definition_id
             FROM character_equipment c
             INNER JOIN item_instance i ON i.instance_id = c.instance_id
             LEFT JOIN equipment_instance e ON e.instance_id = i.instance_id
             WHERE c.owner_id = :owner
             ORDER BY c.slot ASC'
        );

        $statement->execute([':owner' => $this->equipmentOwnerId($accountId, $characterId)]);

        $items = [];

        foreach ($statement->fetchAll() as $row) {
            $items[] = $this->itemRow($row, -1, (int) $row['slot']);
        }

        return $items;
    }

    /**
     * One item row, with its per-copy equipment state if it has any.
     *
     * Enhancement, rarity, stones and cards are facts about this copy that no
     * definition can supply. A load that dropped them would silently strip every
     * upgrade a player paid for.
     *
     * @param array<string,mixed> $row
     * @return array<string,mixed>
     */
    private function itemRow(array $row, int $containerSlot, int $equipmentSlot): array
    {
        $instanceId = (string) $row['instance_id'];

        $item = [
            'instance_id'    => $instanceId,
            'item_id'        => (string) $row['definition_id'],
            'quantity'       => (int) $row['quantity'],
            'slot'           => $containerSlot,
            'lock_state'     => (int) $row['lock_state'],
            'equipment_slot' => $equipmentSlot,
        ];

        if ($row['enhancement_level'] === null) {
            // Not equipment. No enhancement row, no sockets, nothing to look up.
            return $item;
        }

        $item['enhancement_level'] = (int) $row['enhancement_level'];
        $item['rarity_id'] = (string) $row['rarity_definition_id'];
        $item['enchants'] = $this->loadEnchants($instanceId);
        $item['cards'] = $this->loadCards($instanceId);

        return $item;
    }

    /** @return list<array{stone_id:string,socket:int,rank:int}> */
    private function loadEnchants(string $instanceId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT socket_index, stone_definition_id, stone_rank
             FROM equipment_enchant WHERE instance_id = :iid ORDER BY socket_index ASC'
        );

        $statement->execute([':iid' => $instanceId]);

        return array_map(
            static fn (array $r): array => [
                'stone_id' => (string) $r['stone_definition_id'],
                'socket'   => (int) $r['socket_index'],
                'rank'     => (int) $r['stone_rank'],
            ],
            $statement->fetchAll()
        );
    }

    /** @return list<array{card_id:string,socket:int,card_instance_id:string}> */
    private function loadCards(string $instanceId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT socket_index, card_definition_id, card_instance_id
             FROM equipment_card_socket WHERE instance_id = :iid ORDER BY socket_index ASC'
        );

        $statement->execute([':iid' => $instanceId]);

        return array_map(
            static fn (array $r): array => [
                'card_id'          => (string) $r['card_definition_id'],
                'socket'           => (int) $r['socket_index'],
                'card_instance_id' => (string) $r['card_instance_id'],
            ],
            $statement->fetchAll()
        );
    }

    /**
     * The owner key a character's worn equipment is stored under.
     *
     * Derived from the character rather than the account, so two characters on one
     * account do not wear each other's armour. `character_equipment` is keyed by
     * owner_id, and this is what that owner means here.
     */
    private function equipmentOwnerId(string $accountId, string $characterId): string
    {
        return 'eq:' . $characterId;
    }

    /**
     * How many slots the bag has, or zero for a character that has never had one.
     *
     * Zero rather than a number, so the default lives on the server instead of being
     * copied into every row -- raising it later is then one change, not a migration.
     */
    private function loadInventoryCapacity(string $characterId): int
    {
        $statement = $this->pdo->prepare(
            'SELECT capacity FROM item_container WHERE container_id = :cid'
        );

        $statement->execute([':cid' => $this->containerId($characterId)]);

        $value = $statement->fetchColumn();

        return $value === false ? 0 : (int) $value;
    }

    /**
     * A character's inventory container id.
     *
     * Derived from the character rather than stored, so there is exactly one inventory
     * per character and no lookup can return a second one. The account owns the items;
     * the character owns the bag.
     */
    private function containerId(string $characterId): string
    {
        return 'inv:' . $characterId;
    }

    /**
     * The revisions this character was last saved at.
     *
     * A character that has never been saved has no row, and reports zeros. That is
     * the correct answer rather than an error: a freshly created character genuinely
     * is at revision zero, and making the first save a special case would mean every
     * caller handling it.
     *
     * @return array<string,int>
     */
    public function loadRevisions(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT identity_revision, class_revision, appearance_revision,
                    progression_revision, stats_revision, skills_revision, save_revision
             FROM character_save_revision WHERE character_id = :cid'
        );

        $statement->execute([':cid' => $characterId]);

        $row = $statement->fetch();

        if ($row === false) {
            return [
                'identity' => 0, 'class' => 0, 'appearance' => 0, 'progression' => 0,
                'stats' => 0, 'skills' => 0, 'save' => 0,
            ];
        }

        return [
            'identity'    => (int) $row['identity_revision'],
            'class'       => (int) $row['class_revision'],
            'appearance'  => (int) $row['appearance_revision'],
            'progression' => (int) $row['progression_revision'],
            'stats'       => (int) $row['stats_revision'],
            'skills'      => (int) $row['skills_revision'],
            'save'        => (int) $row['save_revision'],
        ];
    }

    /**
     * Writes the character back, or refuses because somebody else already did.
     *
     * **The whole point is `expectedSaveRevision`.** A world server that crashed and
     * restarted, a reconnect racing a shutdown, or two processes that both believe
     * they own a character all end the same way: the second writer presents a
     * revision that has moved on and is refused. Without it the later write wins by
     * accident, which is how an hour of progress disappears.
     *
     * **Passing null means "first save".** It succeeds only if no revision row
     * exists. A caller that does not know the revision cannot use that to bypass the
     * check -- it can only claim the character has never been saved, which is false
     * the moment it has been.
     *
     * **Idempotent by revision, not by request id.** Saving the same state twice with
     * the same expected revision succeeds once and is then refused, which is the
     * correct answer: the second call's view of the world is genuinely stale.
     *
     * @param array<string,mixed> $state
     * @return array{ok:bool,reason?:string,save_revision?:int}
     */
    public function save(
        string $accountId,
        string $characterId,
        array $state,
        ?int $expectedSaveRevision
    ): array {
        $this->pdo->beginTransaction();

        try {
            // The character row is locked first and scoped by account. Everything
            // below writes only after this succeeds, so an unowned character never
            // reaches a single UPDATE.
            $owned = $this->pdo->prepare(
                'SELECT character_id FROM `character`
                 WHERE character_id = :cid AND account_id = :aid
                 FOR UPDATE'
            );

            $owned->execute([':cid' => $characterId, ':aid' => $accountId]);

            if ($owned->fetch() === false) {
                $this->pdo->rollBack();

                return ['ok' => false, 'reason' => 'character_not_owned'];
            }

            $current = $this->lockRevision($characterId);

            if ($expectedSaveRevision === null) {
                if ($current !== null) {
                    $this->pdo->rollBack();

                    return ['ok' => false, 'reason' => 'stale_revision'];
                }

                $next = 1;
            } else {
                if ($current === null || $current !== $expectedSaveRevision) {
                    $this->pdo->rollBack();

                    return ['ok' => false, 'reason' => 'stale_revision'];
                }

                $next = $current + 1;
            }

            $this->writeCharacterRow($characterId, $state);
            $this->writePets($accountId, $characterId, $state);
            $this->replaceStats($characterId, $state['stats'] ?? []);
            $this->replaceAppearance($characterId, $state['appearance'] ?? []);
            $this->replaceSkills($characterId, $state['skills'] ?? []);
            $this->writeInventory(
                $characterId,
                $accountId,
                $state['items'] ?? [],
                (int) ($state['inventory_capacity'] ?? 0)
            );
            $this->writeDevilFruit($characterId, $state);
            $this->writeRevisions($characterId, $state['revisions'] ?? [], $next);

            $this->pdo->commit();

            return ['ok' => true, 'save_revision' => $next];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * The current save revision, locked, or null if this character has never saved.
     *
     * `FOR UPDATE` on a row that may not exist does not block a concurrent insert,
     * so the primary key on `character_save_revision` is what actually makes two
     * first-saves race safely: one inserts, the other's insert fails and its
     * transaction rolls back.
     */
    private function lockRevision(string $characterId): ?int
    {
        $statement = $this->pdo->prepare(
            'SELECT save_revision FROM character_save_revision
             WHERE character_id = :cid FOR UPDATE'
        );

        $statement->execute([':cid' => $characterId]);

        $value = $statement->fetchColumn();

        return $value === false ? null : (int) $value;
    }

    /** @param array<string,mixed> $state */
    /**
     * Replaces this character's pets, then points them at whichever is out.
     *
     * **Replacement, like every other owned collection here.** The server holds the
     * authoritative set, so a pet missing from it is a pet the character no longer has.
     * Merging would leave a released pet owned forever.
     *
     * **Pets before the pointer.** `character.active_pet_instance_id` has a foreign key to
     * `pet_instance`, so the row it names has to exist by the time it is written -- and the
     * selection is cleared first so a pet about to be deleted cannot hold the write hostage.
     *
     * **Ownership is checked here, not only in the world.** A selection naming a pet this
     * character does not own is dropped rather than stored: the foreign key can say the pet
     * exists, but only this can say it is theirs.
     *
     * @param array<string,mixed> $state
     */
    private function writePets(string $accountId, string $characterId, array $state): void
    {
        // Absent means "this save is not about pets" -- an older client, or a caller that
        // only touched stats. Present but empty means "this character owns none", which is
        // a real answer and does clear them.
        if (!array_key_exists('pets', $state)) {
            return;
        }

        $owner = $this->petOwnerId($accountId, $characterId);

        // Released first, so a pet that is about to be deleted is not still being pointed at.
        $this->pdo->prepare(
            'UPDATE `character` SET active_pet_instance_id = NULL WHERE character_id = :cid'
        )->execute([':cid' => $characterId]);

        $keep = [];

        $upsert = $this->pdo->prepare(
            'INSERT INTO pet_instance
                (instance_id, definition_id, owner_id, level, experience,
                 evolution_stage, revision, created_at, updated_at)
             VALUES (:iid, :did, :owner, :level, :xp, :stage, :rev, NOW(3), NOW(3))
             ON DUPLICATE KEY UPDATE
                definition_id = VALUES(definition_id),
                owner_id = VALUES(owner_id),
                level = VALUES(level),
                experience = VALUES(experience),
                evolution_stage = VALUES(evolution_stage),
                revision = VALUES(revision),
                updated_at = NOW(3)'
        );

        foreach ((array) ($state['pets'] ?? []) as $row) {
            $instanceId = trim((string) ($row['instance_id'] ?? ''));
            $definitionId = trim((string) ($row['definition_id'] ?? ''));

            // A row with no identity or no kind is not a pet. Skipped rather than
            // defaulted: inventing either would be inventing somebody's companion.
            if ($instanceId === '' || $definitionId === '') {
                continue;
            }

            $upsert->execute([
                ':iid'   => $instanceId,
                ':did'   => $definitionId,
                ':owner' => $owner,
                ':level' => max(1, (int) ($row['level'] ?? 1)),
                ':xp'    => max(0, (int) ($row['experience'] ?? 0)),
                ':stage' => max(0, (int) ($row['evolution_stage'] ?? 0)),
                ':rev'   => max(0, (int) ($row['revision'] ?? 0)),
            ]);

            $keep[$instanceId] = true;
        }

        // Anything this character owned and no longer does.
        $existing = $this->pdo->prepare(
            'SELECT instance_id FROM pet_instance WHERE owner_id = :owner'
        );

        $existing->execute([':owner' => $owner]);

        $drop = $this->pdo->prepare(
            'DELETE FROM pet_instance WHERE instance_id = :iid AND owner_id = :owner'
        );

        foreach ($existing->fetchAll() as $row) {
            $instanceId = (string) $row['instance_id'];

            if (isset($keep[$instanceId])) {
                continue;
            }

            $drop->execute([':iid' => $instanceId, ':owner' => $owner]);
        }

        $active = trim((string) ($state['active_pet_instance_id'] ?? ''));

        // Only a pet this character actually owns may be the one that is out.
        if ($active === '' || !isset($keep[$active])) {
            return;
        }

        $this->pdo->prepare(
            'UPDATE `character` SET active_pet_instance_id = :pid WHERE character_id = :cid'
        )->execute([':pid' => $active, ':cid' => $characterId]);
    }

    /**
     * The owner id pets are filed under.
     *
     * Pets belong to a character rather than to an account: two characters on one account
     * do not share a companion, which is the same rule equipment already follows.
     */
    private function petOwnerId(string $accountId, string $characterId): string
    {
        return $characterId;
    }

    /**
     * Every pet this character owns, oldest first.
     *
     * @return list<array{instance_id:string,definition_id:string,level:int,
     *                    experience:int,evolution_stage:int,revision:int}>
     */
    private function loadPets(string $characterId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT instance_id, definition_id, level, experience, evolution_stage, revision
             FROM pet_instance WHERE owner_id = :owner
             ORDER BY created_at ASC, instance_id ASC'
        );

        $statement->execute([':owner' => $characterId]);

        $pets = [];

        foreach ($statement as $row) {
            $pets[] = [
                'instance_id'     => (string) $row['instance_id'],
                'definition_id'   => (string) $row['definition_id'],
                'level'           => (int) $row['level'],
                'experience'      => (int) $row['experience'],
                'evolution_stage' => (int) $row['evolution_stage'],
                'revision'        => (int) $row['revision'],
            ];
        }

        return $pets;
    }

    private function writeCharacterRow(string $characterId, array $state): void
    {
        $statement = $this->pdo->prepare(
            'UPDATE `character` SET
                level = :level,
                experience = :experience,
                current_health = :health,
                current_mana = :mana,
                class_definition_id = :class,
                job_definition_id = :job,
                map_definition_id = :map,
                spawn_definition_id = :spawn,
                revision = revision + 1,
                updated_at = NOW(3)
             WHERE character_id = :cid'
        );

        $statement->execute([
            ':level'      => max(1, (int) ($state['level'] ?? 1)),
            ':experience' => max(0, (int) ($state['experience'] ?? 0)),
            ':health'     => max(0, (int) ($state['current_health'] ?? 0)),
            ':mana'       => max(0, (int) ($state['current_mana'] ?? 0)),
            ':class'      => (string) ($state['class_id'] ?? ''),
            ':job'        => (string) ($state['job_id'] ?? ''),
            ':map'        => (string) ($state['map_id'] ?? ''),
            ':spawn'      => (string) ($state['spawn_id'] ?? ''),
            ':cid'        => $characterId,
        ]);
    }

    /**
     * Replaces a character's stats wholesale.
     *
     * Delete-then-insert rather than a diff, because the authoritative set is what
     * the server holds and a stat that vanished from it must vanish here. A merge
     * would leave a removed stat behind forever, and a stat nobody can see is worse
     * than one that is wrong.
     *
     * Both statements are inside the caller's transaction, so there is no moment at
     * which a reader sees a character with no stats.
     *
     * @param list<array{stat_id:string,value:int}> $stats
     */
    private function replaceStats(string $characterId, array $stats): void
    {
        $this->pdo->prepare('DELETE FROM character_stat WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($stats === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_stat (character_id, stat_definition_id, value)
             VALUES (:cid, :stat, :value)'
        );

        foreach ($stats as $stat) {
            $id = (string) ($stat['stat_id'] ?? '');

            if ($id === '') {
                continue;
            }

            $insert->execute([
                ':cid'   => $characterId,
                ':stat'  => $id,
                ':value' => (int) ($stat['value'] ?? 0),
            ]);
        }
    }

    /** @param list<array{slot:int,option_id:string}> $appearance */
    private function replaceAppearance(string $characterId, array $appearance): void
    {
        $this->pdo->prepare('DELETE FROM character_appearance WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($appearance === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_appearance (character_id, slot, option_definition_id)
             VALUES (:cid, :slot, :option)'
        );

        foreach ($appearance as $entry) {
            $insert->execute([
                ':cid'    => $characterId,
                ':slot'   => (int) ($entry['slot'] ?? 0),
                ':option' => (string) ($entry['option_id'] ?? ''),
            ]);
        }
    }

    /** @param list<array{skill_id:string,level:int}> $skills */
    private function replaceSkills(string $characterId, array $skills): void
    {
        $this->pdo->prepare('DELETE FROM character_skill WHERE character_id = :cid')
            ->execute([':cid' => $characterId]);

        if ($skills === []) {
            return;
        }

        $insert = $this->pdo->prepare(
            'INSERT INTO character_skill (character_id, skill_definition_id, skill_level)
             VALUES (:cid, :skill, :level)'
        );

        foreach ($skills as $skill) {
            $id = (string) ($skill['skill_id'] ?? '');

            if ($id === '') {
                continue;
            }

            $insert->execute([
                ':cid'   => $characterId,
                ':skill' => $id,
                ':level' => max(1, (int) ($skill['level'] ?? 1)),
            ]);
        }
    }

    /**
     * Writes a character's bag: the container, the item rows, and where each one sits.
     *
     * **Slots are replaced; item rows are not deleted.** Emptying and refilling
     * `container_slot` is safe because a slot row is only ever a placement. Deleting
     * `item_instance` rows that are no longer in the bag would not be: an item can
     * legitimately have left the inventory for a trade window or a shop listing, and
     * this endpoint knows nothing about either. An instance that is no longer placed
     * stays owned and unplaced, which is exactly what it is.
     *
     * **Nothing is written for a payload with no capacity.** A save from a world server
     * composed without an item registry carries no inventory at all, and must not be
     * read as "this character's bag is now empty" -- that would delete a player's
     * belongings because a server was misconfigured.
     *
     * Runs inside the caller's transaction, so a bag is never half-written.
     *
     * @param list<array{instance_id?:string,item_id?:string,quantity?:int,slot?:int,lock_state?:int}> $items
     */
    private function writeInventory(
        string $characterId,
        string $accountId,
        array $items,
        int $capacity
    ): void {
        if ($capacity <= 0) {
            return;
        }

        $containerId = $this->containerId($characterId);

        $this->pdo->prepare(
            'INSERT INTO item_container
                (container_id, owner_id, kind, capacity, revision, created_at, updated_at)
             VALUES (:cid, :owner, 0, :capacity, 0, NOW(3), NOW(3))
             ON DUPLICATE KEY UPDATE
                capacity = VALUES(capacity),
                revision = revision + 1,
                updated_at = NOW(3)'
        )->execute([
            ':cid'      => $containerId,
            ':owner'    => $accountId,
            ':capacity' => $capacity,
        ]);

        $this->pdo->prepare('DELETE FROM container_slot WHERE container_id = :cid')
            ->execute([':cid' => $containerId]);

        // Worn pieces are placements too, and are replaced the same way. Deleting the
        // rows does not delete the items: character_equipment references item_instance,
        // not the other way round.
        $this->pdo->prepare('DELETE FROM character_equipment WHERE owner_id = :owner')
            ->execute([':owner' => $this->equipmentOwnerId($accountId, $characterId)]);

        if ($items === []) {
            return;
        }

        $instance = $this->pdo->prepare(
            'INSERT INTO item_instance
                (instance_id, definition_id, owner_id, quantity, lock_state, revision,
                 created_at, updated_at)
             VALUES (:iid, :did, :owner, :quantity, :lock, 0, NOW(3), NOW(3))
             ON DUPLICATE KEY UPDATE
                definition_id = VALUES(definition_id),
                owner_id = VALUES(owner_id),
                quantity = VALUES(quantity),
                lock_state = VALUES(lock_state),
                revision = revision + 1,
                updated_at = NOW(3)'
        );

        $slot = $this->pdo->prepare(
            'INSERT INTO container_slot (container_id, slot_index, instance_id)
             VALUES (:cid, :slot, :iid)'
        );

        $worn = $this->pdo->prepare(
            'INSERT INTO character_equipment (owner_id, slot, instance_id)
             VALUES (:owner, :slot, :iid)'
        );

        $equipmentOwner = $this->equipmentOwnerId($accountId, $characterId);

        foreach ($items as $item) {
            $instanceId = (string) ($item['instance_id'] ?? '');
            $definitionId = (string) ($item['item_id'] ?? '');
            $slotIndex = (int) ($item['slot'] ?? -1);
            $equipmentSlot = (int) ($item['equipment_slot'] ?? 0);

            // A row missing an identity or a definition is not an item, and a row that is
            // in neither a bag nor a slot has no home. Skipped rather than defaulted:
            // inventing a place would move somebody's belongings.
            if ($instanceId === '' || $definitionId === '') {
                continue;
            }

            if ($equipmentSlot <= 0 && ($slotIndex < 0 || $slotIndex >= $capacity)) {
                continue;
            }

            $instance->execute([
                ':iid'      => $instanceId,
                ':did'      => $definitionId,
                ':owner'    => $accountId,
                ':quantity' => max(1, (int) ($item['quantity'] ?? 1)),
                ':lock'     => max(0, min(3, (int) ($item['lock_state'] ?? 0))),
            ]);

            $this->writeEquipmentDetail($instanceId, $item);

            if ($equipmentSlot > 0) {
                $worn->execute([
                    ':owner' => $equipmentOwner,
                    ':slot'  => $equipmentSlot,
                    ':iid'   => $instanceId,
                ]);

                continue;
            }

            $slot->execute([
                ':cid'  => $containerId,
                ':slot' => $slotIndex,
                ':iid'  => $instanceId,
            ]);
        }
    }

    /**
     * Writes the per-copy equipment state of one item, if it has any.
     *
     * **Only for rows that carry it.** An ordinary item has no enhancement row, and
     * writing a zeroed one would make every potion look like a piece of equipment to
     * the load's LEFT JOIN. The presence of the key is the signal, which is why this
     * checks for the key rather than for a zero value -- a +0 sword is still equipment.
     *
     * Sockets are replaced wholesale, like stats and skills: the server holds the
     * authoritative set, and a stone that vanished from it must vanish here. A merge
     * would leave a removed stone behind forever.
     *
     * @param array<string,mixed> $item
     */
    private function writeEquipmentDetail(string $instanceId, array $item): void
    {
        if (!array_key_exists('enhancement_level', $item)) {
            return;
        }

        $this->pdo->prepare(
            'INSERT INTO equipment_instance
                (instance_id, enhancement_level, rarity_definition_id)
             VALUES (:iid, :enhancement, :rarity)
             ON DUPLICATE KEY UPDATE
                enhancement_level = VALUES(enhancement_level),
                rarity_definition_id = VALUES(rarity_definition_id)'
        )->execute([
            ':iid'         => $instanceId,
            ':enhancement' => max(0, (int) ($item['enhancement_level'] ?? 0)),
            ':rarity'      => (string) ($item['rarity_id'] ?? ''),
        ]);

        $this->pdo->prepare('DELETE FROM equipment_enchant WHERE instance_id = :iid')
            ->execute([':iid' => $instanceId]);

        $enchant = $this->pdo->prepare(
            'INSERT INTO equipment_enchant
                (instance_id, socket_index, stone_definition_id, stone_rank)
             VALUES (:iid, :socket, :stone, :rank)'
        );

        foreach ((array) ($item['enchants'] ?? []) as $row) {
            $stone = (string) ($row['stone_id'] ?? '');
            $socket = (int) ($row['socket'] ?? -1);

            if ($stone === '' || $socket < 0) {
                continue;
            }

            $enchant->execute([
                ':iid'    => $instanceId,
                ':socket' => $socket,
                ':stone'  => $stone,
                ':rank'   => max(1, (int) ($row['rank'] ?? 1)),
            ]);
        }

        $this->pdo->prepare('DELETE FROM equipment_card_socket WHERE instance_id = :iid')
            ->execute([':iid' => $instanceId]);

        $card = $this->pdo->prepare(
            'INSERT INTO equipment_card_socket
                (instance_id, socket_index, card_definition_id, card_instance_id)
             VALUES (:iid, :socket, :card, :card_instance)'
        );

        foreach ((array) ($item['cards'] ?? []) as $row) {
            $definition = (string) ($row['card_id'] ?? '');
            $socket = (int) ($row['socket'] ?? -1);
            $cardInstance = (string) ($row['card_instance_id'] ?? '');

            if ($definition === '' || $socket < 0 || $cardInstance === '') {
                continue;
            }

            // The socketed card is an owned item in its own right, and the socket row
            // has a foreign key to it. Its own row is written by whatever owns it.
            $exists = $this->pdo->prepare(
                'SELECT instance_id FROM item_instance WHERE instance_id = :iid'
            );

            $exists->execute([':iid' => $cardInstance]);

            if ($exists->fetch() === false) {
                continue;
            }

            $card->execute([
                ':iid'           => $instanceId,
                ':socket'        => $socket,
                ':card'          => $definition,
                ':card_instance' => $cardInstance,
            ]);
        }
    }

    /** @param array<string,int> $revisions */
    private function writeRevisions(string $characterId, array $revisions, int $next): void
    {
        $statement = $this->pdo->prepare(
            'INSERT INTO character_save_revision
                (character_id, identity_revision, class_revision, appearance_revision,
                 progression_revision, stats_revision, skills_revision, save_revision, saved_at)
             VALUES (:cid, :identity, :class, :appearance, :progression, :stats, :skills,
                     :save, NOW(3))
             ON DUPLICATE KEY UPDATE
                identity_revision = VALUES(identity_revision),
                class_revision = VALUES(class_revision),
                appearance_revision = VALUES(appearance_revision),
                progression_revision = VALUES(progression_revision),
                stats_revision = VALUES(stats_revision),
                skills_revision = VALUES(skills_revision),
                save_revision = VALUES(save_revision),
                saved_at = NOW(3)'
        );

        $statement->execute([
            ':cid'         => $characterId,
            ':identity'    => max(0, (int) ($revisions['identity'] ?? 0)),
            ':class'       => max(0, (int) ($revisions['class'] ?? 0)),
            ':appearance'  => max(0, (int) ($revisions['appearance'] ?? 0)),
            ':progression' => max(0, (int) ($revisions['progression'] ?? 0)),
            ':stats'       => max(0, (int) ($revisions['stats'] ?? 0)),
            ':skills'      => max(0, (int) ($revisions['skills'] ?? 0)),
            ':save'        => $next,
        ]);
    }
}
