-- Which pet a character currently has out.
--
-- On the character rather than on the pet, because "zero or one active pet" is then a
-- property of the schema rather than a rule the application has to remember. A flag on
-- `pet_instance` would let two rows claim to be active at once, and MySQL has no partial
-- unique index to forbid it -- the invariant would live only in code, which is where
-- invariants go to rot.
--
-- Nullable, because having no pet out is the ordinary state and not a missing value.
--
-- Extends the character row exactly as migration 0011 extended it with the rest of a
-- character's world state. Migration 0008, which created `pet_instance`, is untouched.
ALTER TABLE `character`
    ADD COLUMN active_pet_instance_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL
        AFTER spawn_definition_id;

-- The reference cannot outlive the pet it points at.
--
-- ON DELETE SET NULL rather than CASCADE: a pet going away means the character has no pet
-- out, not that the character goes away. This is what makes a dangling active selection
-- impossible rather than merely unlikely, and it is why the column is nullable.
--
-- Nothing here says the pet belongs to this character; ownership lives on
-- `pet_instance.owner_id` and is checked by the server before anything is activated. A
-- foreign key cannot express "and it must be yours", so it is not asked to.
ALTER TABLE `character`
    ADD CONSTRAINT fk_character_active_pet
        FOREIGN KEY (active_pet_instance_id) REFERENCES pet_instance (instance_id)
        ON DELETE SET NULL;
