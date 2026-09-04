-- Parties and guilds.
--
-- Neither table stores a maximum size. Party capacity is six by default and guild
-- capacity is unlimited by default, both authored on Phase 13's SocialConfiguration
-- and read at check time. A number here would be a second place to change it, and
-- the two would drift.

CREATE TABLE party (
    party_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    leader_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- Mirrors Phase 13 PartyLootPolicy: 0 Personal 1 RoundRobin 2 NeedGreed
    loot_policy     TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    disbanded_at    DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (party_id),
    KEY ix_party_leader (leader_character_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One row per member.
--
-- UNIQUE on character_id spans the whole table, which is what makes "one character
-- belongs to at most one party" a database guarantee. Phase 13 enforced it with a
-- directory index in memory; this is the same rule where it cannot be bypassed.
--
-- join_order preserves the sequence round-robin loot and successor selection
-- depend on, so both stay deterministic across a reload.
CREATE TABLE party_member (
    party_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    join_order      INT UNSIGNED NOT NULL DEFAULT 0,
    joined_at       DATETIME(3) NOT NULL,
    PRIMARY KEY (party_id, character_id),
    UNIQUE KEY uq_party_member_character (character_id),
    CONSTRAINT fk_party_member_party
        FOREIGN KEY (party_id) REFERENCES party (party_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE party_invite (
    invite_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    party_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    from_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    target_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- Mirrors Phase 13 PartyInviteState:
    -- 0 Pending 1 Accepted 2 Rejected 3 Cancelled 4 Expired
    state           TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    expires_at      DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (invite_id),
    KEY ix_party_invite_target (target_character_id, state),
    KEY ix_party_invite_open (party_id, target_character_id, state),
    CONSTRAINT fk_party_invite_party
        FOREIGN KEY (party_id) REFERENCES party (party_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Guilds.
--
-- The UNIQUE index on the name is the final authority on uniqueness, and the only
-- one that survives two simultaneous creations. Phase 13's IGuildNameAuthority is
-- a pre-check that gives a fast, friendly refusal; this is what actually decides.
-- The name is stored case-insensitively collated, so "Wanderers" and "wanderers"
-- collide -- two guilds a player could not tell apart are one guild too many.
CREATE TABLE guild (
    guild_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name            VARCHAR(24) COLLATE utf8mb4_unicode_ci NOT NULL,
    leader_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    disbanded_at    DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (guild_id),
    UNIQUE KEY uq_guild_name (name),
    KEY ix_guild_leader (leader_character_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Ranks are per guild, so one guild renaming its officers cannot affect another.
--
-- permissions is a bitmask matching Phase 13's [Flags] GuildPermission. A bitmask
-- rather than a row per permission because a rank is checked on every action and a
-- single integer answers it without a join. `rank_order` carries seniority, which
-- permission bits cannot: two ranks may hold incomparable permission sets and
-- promotion still needs a direction.
CREATE TABLE guild_rank (
    guild_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    rank_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name_key        VARCHAR(128) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    rank_order      INT NOT NULL DEFAULT 0,
    permissions     INT UNSIGNED NOT NULL DEFAULT 0,
    is_leader_rank  TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (guild_id, rank_id),
    KEY ix_guild_rank_order (guild_id, rank_order),
    CONSTRAINT fk_guild_rank_guild
        FOREIGN KEY (guild_id) REFERENCES guild (guild_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- UNIQUE on character_id: one character, one guild, enforced by the database.
CREATE TABLE guild_member (
    guild_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    rank_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    joined_at       DATETIME(3) NOT NULL,
    PRIMARY KEY (guild_id, character_id),
    UNIQUE KEY uq_guild_member_character (character_id),
    KEY ix_guild_member_rank (guild_id, rank_id),
    CONSTRAINT fk_guild_member_guild
        FOREIGN KEY (guild_id) REFERENCES guild (guild_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE guild_invite (
    invite_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    guild_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    from_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    target_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    state           TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    expires_at      DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (invite_id),
    KEY ix_guild_invite_target (target_character_id, state),
    CONSTRAINT fk_guild_invite_guild
        FOREIGN KEY (guild_id) REFERENCES guild (guild_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
