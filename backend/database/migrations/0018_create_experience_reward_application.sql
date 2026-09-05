-- Durable evidence that one reward's experience has already changed one recipient.
--
-- The question these rows exist to answer is "has reward R already been applied to X?",
-- and nothing else in the schema can answer it. Not the recipient's experience total: two
-- different rewards can leave a character or a pet on the same number. Not the delivery
-- stamp on the reward: the stamp is written by a different transaction, and the window
-- between the progression landing and the stamp landing is exactly the crash this closes.
-- And not a single "last reward applied" marker on the recipient, which an overlapping
-- second reward overwrites -- erasing the evidence for the first while it is still owed.
--
-- **Written with the progression, not beside it.** These rows are inserted inside
-- CharacterStateRepository::save(), the same transaction that writes the character's
-- experience and its pets'. Either both land or neither does, so the ledger can never
-- claim an application that did not happen, and an application can never happen without
-- the ledger recording it.
--
-- **Removed with the stamp, not before it.** progress() deletes a row in the same
-- transaction that marks that recipient delivered. Until the stamp is durable the evidence
-- stays, which is what makes a recovery after a lost stamp reconcile instead of pay again.
-- So the table holds only what is in flight, and is bounded by pending rewards rather than
-- by history.
--
-- Additive only. 0014 and 0017 are not rewritten, and pet_instance.applied_reward_id keeps
-- its column: it is a diagnostic of what a pet last received and is no longer the evidence
-- anything relies on.

-- One character, one reward, once.
CREATE TABLE character_experience_application (
    reward_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- What the character's progression became. Diagnostic: an operator reconciling a
    -- blocked reward can see what the world believed it had written, without this being
    -- the thing correctness reads.
    resulting_level      INT UNSIGNED NOT NULL DEFAULT 0,
    resulting_experience BIGINT UNSIGNED NOT NULL DEFAULT 0,

    applied_at   DATETIME(3) NOT NULL,

    -- The identity itself. A second attempt to record the same application is refused by
    -- MySQL rather than by a check somebody might forget to write.
    PRIMARY KEY (reward_id, character_id),

    -- How a character who just logged in finds what is still in flight for them.
    KEY ix_character_experience_application_owner (character_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One pet, one reward, once.
--
-- Keyed by the pet instance, because a character may own two of the same kind and only
-- the one that was out at the defeat earned it. The owner travels along so a character
-- can be handed everything in flight for them in one read.
CREATE TABLE pet_experience_application (
    reward_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    pet_instance_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    resulting_level      INT UNSIGNED NOT NULL DEFAULT 0,
    resulting_experience INT UNSIGNED NOT NULL DEFAULT 0,

    applied_at      DATETIME(3) NOT NULL,

    PRIMARY KEY (reward_id, pet_instance_id),

    KEY ix_pet_experience_application_owner (character_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
