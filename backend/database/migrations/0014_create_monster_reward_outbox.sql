-- What a monster defeat decided, written down before any of it is handed over.
--
-- A defeat is resolved exactly once: the drop table is rolled, the rare chance is spent,
-- the experience split is worked out and, in a party, the claimant is chosen. All of that
-- used to live only in server memory until it had been delivered, so a world that stopped
-- between deciding and paying lost the decision -- and the roll behind it, which for the
-- one in ten million fruit is not something that can honestly be run again.
--
-- These rows are that decision. They are not a second reward system: MonsterRewardAuthority
-- still decides and still delivers, and this is only what lets it finish after a restart.
--
-- Content is referenced by authored DefinitionId and never copied, the same rule the spawn
-- configuration tables follow. No monster, item or map is duplicated here.

-- One row per defeat.
--
-- `defeat_id` is the monster's runtime instance id, and the UNIQUE on it is what makes
-- recording a decision idempotent: a world that saved, crashed before it knew the save had
-- landed, and tried again gets the row it already wrote rather than a second reward. A
-- respawned monster is a new instance and therefore a new defeat, so a boss farmed twice
-- honestly produces two rows.
CREATE TABLE monster_reward (
    reward_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    defeat_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Which world owns this. A pending reward belongs to the channel the monster died in,
    -- so another channel running the same map cannot pick it up and deliver it twice.
    server_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    channel_id           VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    monster_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    map_definition_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    killer_character_id  VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- The pile this defeat produced, decided when the monster died and kept so a recovered
    -- world republishes the same object rather than minting a second one. Empty when the
    -- defeat dropped nothing, which is the common case.
    loot_id              VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    -- Mirrors Phase 12 LootPolicy, and the single character the pile was attributed to.
    -- Frozen at the defeat: a party that disbands, changes policy or loses the claimant
    -- afterwards does not get to rewrite who this drop belonged to.
    loot_policy          TINYINT UNSIGNED NOT NULL DEFAULT 0,
    claimant_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    -- Where the pile goes back. FLOAT because this is a position in a world, not money.
    position_x           FLOAT NOT NULL DEFAULT 0,
    position_y           FLOAT NOT NULL DEFAULT 0,
    position_z           FLOAT NOT NULL DEFAULT 0,

    -- The party whose turn this defeat spends, and the turn it must land on. NULL cursor
    -- means this defeat owes no rotation anything -- solo, Personal or NeedGreed -- which
    -- is different from a cursor of zero, a real position naming the first member.
    party_id             VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    party_cursor         INT UNSIGNED NULL DEFAULT NULL,

    -- Delivery progress. Each is a side effect that must happen at most once, so each is
    -- recorded as it lands rather than inferred from the others.
    cursor_committed     TINYINT(1) NOT NULL DEFAULT 0,
    loot_published       TINYINT(1) NOT NULL DEFAULT 0,

    -- 0 pending, 1 complete. A complete reward is never handed back as pending again.
    state                TINYINT UNSIGNED NOT NULL DEFAULT 0,

    revision             INT UNSIGNED NOT NULL DEFAULT 0,
    created_at           DATETIME(3) NOT NULL,
    updated_at           DATETIME(3) NOT NULL,
    completed_at         DATETIME(3) NULL DEFAULT NULL,

    PRIMARY KEY (reward_id),
    UNIQUE KEY uq_monster_reward_defeat (defeat_id),

    -- How a restarting world finds its own unfinished work, and nobody else's.
    KEY ix_monster_reward_pending (server_id, channel_id, state, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Who this defeat owes experience to, and whether they have had it.
--
-- A row per recipient rather than a total on the envelope, because a party split is
-- already decided per member and re-deriving it after a restart would mean re-running
-- Phase 13's arithmetic against a party that may since have changed size.
--
-- `delivered_at` is what makes payment idempotent across a crash: a recovered world pays
-- the members that have no timestamp and leaves the rest alone.
CREATE TABLE monster_reward_experience (
    reward_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    experience           INT UNSIGNED NOT NULL DEFAULT 0,
    delivered_at         DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (reward_id, character_id),
    CONSTRAINT fk_monster_reward_experience_reward
        FOREIGN KEY (reward_id) REFERENCES monster_reward (reward_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- What the drop tables actually produced, in the order they produced it.
--
-- `entry_index` preserves that order because a pickup names a slot by index, so a pile
-- that came back shuffled would let a player take a different item than the one they
-- clicked.
--
-- No item instance id: instances are minted when an item enters an inventory, not when it
-- hits the ground, so there is nothing allocated at this point to record. What identifies
-- a recovered drop is the reward's `loot_id` plus this index, and `claimed_by_character_id`
-- is the evidence that stops a restart putting an already-taken item back on the floor.
CREATE TABLE monster_reward_loot (
    reward_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    entry_index          INT UNSIGNED NOT NULL,
    item_definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quantity             INT UNSIGNED NOT NULL DEFAULT 1,

    -- A drop table may override the rarity an item rolls at. Empty means "as authored".
    rarity_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    claimed_by_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    claimed_at           DATETIME(3) NULL DEFAULT NULL,

    PRIMARY KEY (reward_id, entry_index),
    CONSTRAINT fk_monster_reward_loot_reward
        FOREIGN KEY (reward_id) REFERENCES monster_reward (reward_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
