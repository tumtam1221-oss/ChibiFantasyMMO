-- Characters, and the ownership that governs every query about them.
--
-- `account_id` sits on the character row itself rather than in a join table.
-- A character belongs to exactly one account and always has, so a separate table
-- would allow a row with two owners or none -- states the game has no meaning for.
-- The column is NOT NULL with a foreign key, which makes "every character has an
-- owner" a database guarantee rather than an application convention.
--
-- Every character query in this backend filters on account_id server-side. The
-- index below exists to make that cheap, so nobody is ever tempted to fetch all
-- and filter afterwards.

CREATE TABLE `character` (
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    account_id      VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    server_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    name            VARCHAR(24) COLLATE utf8mb4_unicode_ci NOT NULL,

    -- Mirrors Phase 04 CharacterGender: 0 Unspecified 1 Male 2 Female
    gender          TINYINT UNSIGNED NOT NULL DEFAULT 0,
    level           INT UNSIGNED NOT NULL DEFAULT 1,

    -- References to authored content, by DefinitionId. Definitions live in Unity;
    -- duplicating their contents here would create a second source of truth that
    -- goes stale on the next content patch.
    class_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    job_definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    map_definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    appearance_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    -- Mirrors Phase 14 CharacterAvailability:
    -- 0 Unknown 1 Playable 2 PendingDeletion 3 Locked 4 InWorld
    availability    TINYINT UNSIGNED NOT NULL DEFAULT 1,

    last_played_at  DATETIME(3) NULL DEFAULT NULL,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,

    PRIMARY KEY (character_id),

    -- The character-select query: this account's characters on this server.
    KEY ix_character_owner (account_id, server_id, availability),

    -- Character names are unique per server, not globally: two servers may each
    -- have an "Ayla", and a player on one has no reason to be blocked by the other.
    UNIQUE KEY uq_character_name_per_server (server_id, name),

    CONSTRAINT fk_character_account
        FOREIGN KEY (account_id) REFERENCES account (account_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_character_server
        FOREIGN KEY (server_id) REFERENCES server_definition (server_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The session's selections can only now be constrained, once the tables they
-- point at exist. Added here rather than in 0002 because a foreign key cannot
-- reference a table that has not been created yet.
--
-- ON DELETE SET NULL, not CASCADE: retiring a server must not delete the sessions
-- of everyone who had selected it. They fall back to having chosen nothing, which
-- is exactly what the Phase 14 state machine expects.
ALTER TABLE account_session
    ADD CONSTRAINT fk_session_server
        FOREIGN KEY (selected_server_id) REFERENCES server_definition (server_id)
        ON DELETE SET NULL;

ALTER TABLE account_session
    ADD CONSTRAINT fk_session_channel
        FOREIGN KEY (selected_channel_id) REFERENCES server_channel (channel_id)
        ON DELETE SET NULL;

ALTER TABLE account_session
    ADD CONSTRAINT fk_session_character
        FOREIGN KEY (selected_character_id) REFERENCES `character` (character_id)
        ON DELETE SET NULL;
