-- What a character has taken, is doing, and has finished.
--
-- Until now quest state lived only in the running world server. Accepting a quest, walking
-- outside and logging back in lost it, because a character spawning built an empty log --
-- which made the whole feature unusable rather than merely incomplete: no quest could ever
-- be finished across two sessions.
--
-- Two tables rather than one, for the reason character_skill is its own table: a quest has
-- a status, and each of its objectives has a counter. Packing the counters into a string
-- would make "how far along is everybody on objective two" unanswerable in SQL and would
-- need parsing on both sides of the wire.
--
-- Status mirrors QuestStatus by number: 0 NotStarted, 1 Active, 2 ReadyToComplete,
-- 3 Completed. Stored as the number the game already uses rather than as a name, for the
-- same reason availability and session state are: one vocabulary, no translation table that
-- can drift.
--
-- Completed quests are kept, not deleted. Prerequisites and non-repeatable quests both need
-- the history, and a quest that vanished on completion could be taken again forever.
--
-- No reward and no required amount. Both are authored content the game ships; a copy here
-- would go stale the first time a quest was retuned and a player would be paid last patch's
-- reward from a table nobody thought to update.
--
-- No existing data is touched. A character with no rows here has taken no quests, which is
-- exactly what every character looked like before this existed.

CREATE TABLE character_quest
(
    character_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quest_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Mirrors QuestStatus: 0 NotStarted 1 Active 2 ReadyToComplete 3 Completed
    status              TINYINT UNSIGNED NOT NULL DEFAULT 0,

    updated_at          DATETIME(3) NOT NULL,

    PRIMARY KEY (character_id, quest_definition_id),

    -- The quest log query: everything this character has touched, in one seek.
    KEY ix_character_quest_owner (character_id, status),

    CONSTRAINT fk_character_quest_character
        FOREIGN KEY (character_id) REFERENCES `character` (character_id)
        ON DELETE CASCADE
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;

-- One counter per authored objective, matched by position exactly as the game matches them.
--
-- objective_index is the position in the definition's objective array, which is the only
-- identity an objective has -- there is no objective id to key on, and inventing one here
-- would create a second identity for the two sides to disagree about.
CREATE TABLE character_quest_objective
(
    character_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quest_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    objective_index     TINYINT UNSIGNED NOT NULL,

    counter             INT UNSIGNED NOT NULL DEFAULT 0,

    PRIMARY KEY (character_id, quest_definition_id, objective_index),

    -- Deleted with its quest. A counter whose quest is gone counts nothing.
    CONSTRAINT fk_character_quest_objective_quest
        FOREIGN KEY (character_id, quest_definition_id)
        REFERENCES character_quest (character_id, quest_definition_id)
        ON DELETE CASCADE
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
