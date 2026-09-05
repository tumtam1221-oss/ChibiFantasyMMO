-- What a defeat owes somebody's pet, and the evidence that the pet already had it.
--
-- Pet experience joins the reward decision that already exists rather than starting a
-- second one. MonsterRewardAuthority still decides, the monster_reward envelope from 0014
-- is still the only thing recovery reads, and these rows are simply another kind of thing
-- one defeat can owe -- the same shape monster_reward_experience already has for
-- characters.
--
-- Additive only. 0014 is not rewritten and its primary keys are untouched: generalising
-- monster_reward_experience to "any recipient" would have meant changing the key of a
-- table that already has rows and a foreign key, and every query that reads it, to store
-- something the existing key cannot express -- a character owning two pets means two rows
-- for one character.

-- One row per pet this defeat owes experience to.
--
-- `pet_instance_id`, not `pet_definition_id`: a character may own two of the same kind,
-- and the one that was out at the defeat is the one that earned this. The definition is
-- not stored at all -- it is on the pet, and copying it here would let the two disagree.
--
-- The pet is named by instance and NOT constrained by a foreign key, deliberately. A
-- reward is history: it records what was decided about a pet that existed at the defeat.
-- If that pet is later gone the reward must become an operator-visible blocked delivery,
-- not silently vanish with the row -- which is exactly what ON DELETE CASCADE would do.
--
-- `delivered_at` is what makes payment idempotent across a crash, the same way it does for
-- characters: a recovered world pays the pets with no timestamp and leaves the rest alone.
CREATE TABLE monster_reward_pet_experience (
    reward_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Whose pet it is. Stored because delivery needs the owner in the world to apply and
    -- persist it, and re-deriving the owner from a pet that may be gone is not possible.
    character_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    pet_instance_id      VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    experience           INT UNSIGNED NOT NULL DEFAULT 0,
    delivered_at         DATETIME(3) NULL DEFAULT NULL,

    PRIMARY KEY (reward_id, pet_instance_id),

    -- How delivery finds the rows for a character who just walked back in.
    KEY ix_monster_reward_pet_owner (character_id),

    CONSTRAINT fk_monster_reward_pet_experience_reward
        FOREIGN KEY (reward_id) REFERENCES monster_reward (reward_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The last reward whose experience is already part of this pet's progression.
--
-- This is the column that closes the crash window between "the pet's experience is
-- durable" and "the reward says so". Pet progression is written by
-- CharacterStateRepository::save() inside one transaction, so this marker commits with the
-- experience it describes: they cannot disagree, whatever happens next.
--
-- Recovery can therefore answer "has reward R already been applied to pet P?" from stored
-- facts rather than from P's experience total -- which could never answer it, because two
-- different rewards can leave a pet on the same number.
--
-- One id, not a list, and that is sufficient: delivery refuses to apply a second reward to
-- a pet whose marker names a reward that is not yet stamped delivered. At most one reward
-- per pet is ever applied-but-unstamped, so remembering the last one is remembering all of
-- them that matter.
--
-- NULL means "no reward has been applied to this pet", which is what every pet acquired
-- some other way is.
ALTER TABLE pet_instance
    ADD COLUMN applied_reward_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL
        AFTER revision;
