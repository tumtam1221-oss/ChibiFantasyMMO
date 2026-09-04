-- Owned items and the containers holding them.
--
-- One row per owned copy, keyed by its InstanceId. The lock state lives on this
-- row and nowhere else: Phase 13 replaced four booleans (IsTrading, IsListed,
-- IsReserved and friends) with a single value precisely because flags can
-- contradict each other, and splitting it across tables here would reintroduce it.
--
-- There is no serialized ItemContainerState blob. A container is rows, so one item
-- can be located, locked and moved without rewriting a whole inventory -- which is
-- what makes row-level locking possible at all.

CREATE TABLE item_instance (
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quantity        INT UNSIGNED NOT NULL DEFAULT 1,
    -- Mirrors Phase 13 ItemLockState: 0 Available 1 Reserved 2 Listed 3 Bound
    lock_state      TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (instance_id),
    KEY ix_item_owner (owner_id, definition_id),
    KEY ix_item_lock (lock_state),
    CONSTRAINT ck_item_quantity_positive CHECK (quantity >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- A container is a bag, a warehouse, or anything else with numbered slots. One
-- type serving both is the Phase 08 decision, unchanged.
CREATE TABLE item_container (
    container_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- 0 Inventory, 1 Storage. A closed technical category, not content.
    kind            TINYINT UNSIGNED NOT NULL DEFAULT 0,
    capacity        INT UNSIGNED NOT NULL,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (container_id),
    KEY ix_container_owner (owner_id, kind)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Occupancy, as rows.
--
-- Two unique constraints carry the anti-duplication invariants the whole economy
-- rests on. (container_id, slot_index) means one slot holds at most one item.
-- instance_id being unique across the whole table means one item sits in at most
-- one slot of one container anywhere -- so "an item cannot exist in two
-- inventories" is a database guarantee, not an application convention.
CREATE TABLE container_slot (
    container_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    slot_index      INT UNSIGNED NOT NULL,
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (container_id, slot_index),
    UNIQUE KEY uq_slot_instance (instance_id),
    CONSTRAINT fk_slot_container
        FOREIGN KEY (container_id) REFERENCES item_container (container_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_slot_instance
        FOREIGN KEY (instance_id) REFERENCES item_instance (instance_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
