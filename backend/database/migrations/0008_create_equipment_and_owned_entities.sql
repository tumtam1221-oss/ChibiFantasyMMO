-- Equipment, its two independent socket sets, and the owned entities that are
-- deliberately not items.
--
-- An equipment instance IS an item instance: the row below extends it rather than
-- copying it, so ownership, lock state and revision have exactly one home. The
-- shared primary key enforces the one-to-one.
--
-- Nothing here duplicates EquipmentDefinition. Base stats, slot, category and
-- socket counts are authored content in Unity; copying them into instance rows
-- would go stale on the next content patch.

CREATE TABLE equipment_instance (
    instance_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    enhancement_level INT UNSIGNED NOT NULL DEFAULT 0,
    -- The per-copy rarity override. Empty means "whatever the definition says",
    -- which is the normal case and costs nothing to store.
    rarity_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    PRIMARY KEY (instance_id),
    CONSTRAINT fk_equipment_instance
        FOREIGN KEY (instance_id) REFERENCES item_instance (instance_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- What a character is wearing.
--
-- UNIQUE on (owner_id, slot) means one item per slot. UNIQUE on instance_id means
-- one piece cannot be worn in two places or by two people -- and, together with
-- container_slot, that a worn item is in no bag. That is why an equipped item
-- cannot be traded: there is nothing in a container to offer.
CREATE TABLE character_equipment (
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- Mirrors Phase 04 EquipmentSlot by ordinal.
    slot            TINYINT UNSIGNED NOT NULL,
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (owner_id, slot),
    UNIQUE KEY uq_equipped_instance (instance_id),
    CONSTRAINT fk_equipped_instance
        FOREIGN KEY (instance_id) REFERENCES item_instance (instance_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Status stone sockets, from Phase 09.
--
-- A separate table from cards below, deliberately: the two have different
-- capacities, different compatibility rules and different removal behaviour, and
-- sharing one table would let a card consume a stone socket.
CREATE TABLE equipment_enchant (
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    socket_index    INT UNSIGNED NOT NULL,
    stone_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- Named stone_rank, not rank: RANK is a reserved word in MySQL 8.0 (it is a
    -- window function), and a column that needs backticks everywhere it appears is
    -- a column somebody will eventually forget to quote.
    stone_rank      INT UNSIGNED NOT NULL DEFAULT 1,
    PRIMARY KEY (instance_id, socket_index),
    CONSTRAINT fk_enchant_equipment
        FOREIGN KEY (instance_id) REFERENCES equipment_instance (instance_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Card sockets, from Phase 12.
--
-- card_instance_id is UNIQUE across the table: one card sits in at most one piece,
-- anywhere. Combined with container_slot's unique instance_id, that makes "a
-- socketed card is in no container and in exactly one socket" a database
-- guarantee -- and therefore why a socketed card cannot be traded or sold.
CREATE TABLE equipment_card_socket (
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    socket_index    INT UNSIGNED NOT NULL,
    card_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    card_instance_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (instance_id, socket_index),
    UNIQUE KEY uq_card_socket_instance (card_instance_id),
    CONSTRAINT fk_card_socket_equipment
        FOREIGN KEY (instance_id) REFERENCES equipment_instance (instance_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_card_socket_card
        FOREIGN KEY (card_instance_id) REFERENCES item_instance (instance_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Devil fruit, from Phase 12: at most one active per character.
--
-- The primary key on owner_id IS the one-fruit rule, enforced by the database
-- rather than by a check somebody could remove. source_instance_id records the
-- copy that was spent, for audit; that copy is gone from every container by then,
-- which is why an active fruit cannot be traded -- there is nothing left to offer.
CREATE TABLE character_devil_fruit (
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    fruit_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    source_instance_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    activated_at    DATETIME(3) NOT NULL,
    PRIMARY KEY (owner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pets, from Phase 12.
--
-- A pet is an owned entity, not an inventory item. It has no container_slot row
-- and no item_instance row, so it cannot enter a bag, a trade or a shop listing --
-- the same structural argument the Unity side already relies on, expressed here
-- as the absence of a relationship rather than as a rule.
CREATE TABLE pet_instance (
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    level           INT UNSIGNED NOT NULL DEFAULT 1,
    experience      INT UNSIGNED NOT NULL DEFAULT 0,
    evolution_stage INT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (instance_id),
    KEY ix_pet_owner (owner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
