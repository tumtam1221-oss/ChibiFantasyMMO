-- The character state the world needs, which Phase 15 did not store.
--
-- Phase 15 stored who a character is and where they are, because that is all
-- character select needed. A world server needs what they are made of: how much
-- experience they have, what their stats are, how much health is left and which
-- authored spawn they last stood on.
--
-- Additive only. Every column below has a default, every table is new, and nothing
-- existing is dropped, renamed or narrowed -- an existing row is valid the moment
-- this runs.

-- Experience, resources and the spawn point, on the character row itself.
--
-- On the row rather than in a side table because there is exactly one of each per
-- character and always has been. A one-to-one side table would allow "a character
-- with no experience row", a state the game has no meaning for and every reader
-- would have to handle.
ALTER TABLE `character`
    -- BIGINT because experience accumulates for the life of an account and an INT
    -- ends at about two billion, which a long-lived MMO character reaches.
    ADD COLUMN experience BIGINT UNSIGNED NOT NULL DEFAULT 0 AFTER level,

    -- Current, not maximum. Maximum is derived from stats and equipment by
    -- DerivedStatsCalculator and would go stale here the moment a ring changed.
    ADD COLUMN current_health INT UNSIGNED NOT NULL DEFAULT 0 AFTER experience,
    ADD COLUMN current_mana INT UNSIGNED NOT NULL DEFAULT 0 AFTER current_health,

    -- Where they last stood, as an authored SpawnPointDefinition. Never coordinates:
    -- a stored x/y/z survives a map being re-authored and drops the player into
    -- terrain, and Phase 11 already decided a character arrives at a spawn.
    ADD COLUMN spawn_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT ''
        AFTER map_definition_id;

-- Character stats, one row per stat.
--
-- A row per stat rather than a column per stat, because the stat list is authored
-- content: adding CRIT_RATE must be a definition change, not a migration. The
-- primary key is the pair, so a character cannot hold one stat twice.
CREATE TABLE character_stat (
    character_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    stat_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Signed: a debuff or an authored penalty is a negative value, and an unsigned
    -- column would turn one into a very large positive one.
    value              INT NOT NULL DEFAULT 0,

    PRIMARY KEY (character_id, stat_definition_id),

    CONSTRAINT fk_character_stat_character
        FOREIGN KEY (character_id) REFERENCES `character` (character_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Appearance, one row per slot.
--
-- Phase 04's CharacterAppearanceState has five slots and the Phase 15 character row
-- has a single appearance_definition_id, which cannot express them. That column is
-- left alone -- character select still reads it -- and the slots live here.
--
-- Same shape as character_stat and for the same reason: AppearanceSlot is authored,
-- and a sixth slot must not require a schema change.
CREATE TABLE character_appearance (
    character_id      VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Mirrors Phase 04 AppearanceSlot: 0 None 1 Face 2 Eyes 3 Hair 4 HairColor 5 SkinTone
    slot              TINYINT UNSIGNED NOT NULL,
    option_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    PRIMARY KEY (character_id, slot),

    CONSTRAINT fk_character_appearance_character
        FOREIGN KEY (character_id) REFERENCES `character` (character_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Learned skills and their levels.
--
-- Phase 06 gives a character a list of skills with a level each; without this a
-- player who learned a skill loses it at logout, and the server would have no way to
-- refuse a skill the character never learned -- which is the check that makes
-- "forged skill" a rejection rather than a possibility.
CREATE TABLE character_skill (
    character_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    skill_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    skill_level         INT UNSIGNED NOT NULL DEFAULT 1,

    PRIMARY KEY (character_id, skill_definition_id),

    CONSTRAINT fk_character_skill_character
        FOREIGN KEY (character_id) REFERENCES `character` (character_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The revisions each aggregate was last saved at.
--
-- Unity's states each carry their own Revision and advance independently: stats can
-- change without progression changing. One revision column on the character row
-- would collapse them and make every save look like every other aggregate changed
-- too, defeating the optimistic-concurrency check the domain already has.
--
-- A save presents the revisions it loaded; a row whose stored revision has moved on
-- is refused rather than overwritten. That is the "no stale overwrite" rule, and it
-- is enforced here rather than remembered by the caller.
CREATE TABLE character_save_revision (
    character_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    identity_revision    INT UNSIGNED NOT NULL DEFAULT 0,
    class_revision       INT UNSIGNED NOT NULL DEFAULT 0,
    appearance_revision  INT UNSIGNED NOT NULL DEFAULT 0,
    progression_revision INT UNSIGNED NOT NULL DEFAULT 0,
    stats_revision       INT UNSIGNED NOT NULL DEFAULT 0,
    skills_revision      INT UNSIGNED NOT NULL DEFAULT 0,

    -- Bumped on every accepted save, whatever changed. The value a concurrent writer
    -- competes on.
    save_revision        INT UNSIGNED NOT NULL DEFAULT 0,
    saved_at             DATETIME(3) NOT NULL,

    PRIMARY KEY (character_id),

    CONSTRAINT fk_character_save_revision_character
        FOREIGN KEY (character_id) REFERENCES `character` (character_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
