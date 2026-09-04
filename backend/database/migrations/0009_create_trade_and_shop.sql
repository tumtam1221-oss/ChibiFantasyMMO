-- Player trade and player shops.
--
-- Both commit as one database transaction, and both take their locks in the same
-- documented order to avoid deadlocking against each other:
--
--   1. the session or listing row  (SELECT ... FOR UPDATE)
--   2. wallet rows, ordered by owner_id ascending
--   3. item rows, ordered by instance_id ascending
--
-- Ordering by identifier rather than by "mine then theirs" is what makes two
-- simultaneous trades between the same pair of players take the same locks in the
-- same sequence, which is the only reliable way to avoid a deadlock cycle.

CREATE TABLE trade_session (
    trade_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_a     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_a         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_b     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_b         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    -- Mirrors Phase 13 TradeSessionState:
    -- 0 Open 1 Confirming 2 Completed 3 Cancelled 4 Failed
    state           TINYINT UNSIGNED NOT NULL DEFAULT 0,
    accepted_a      TINYINT(1) NOT NULL DEFAULT 0,
    accepted_b      TINYINT(1) NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    expires_at      DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (trade_id),
    KEY ix_trade_participants (character_a, state),
    KEY ix_trade_participants_b (character_b, state)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- An offered item is a reference plus the revision it was offered at.
--
-- offered_revision is what makes a stale offer detectable: if the sword was
-- enhanced or socketed between being offered and the trade committing, the number
-- no longer matches and the commit is refused.
--
-- UNIQUE on (trade_id, instance_id) stops the same item being offered twice in one
-- trade. It deliberately does NOT span trades: an item may appear in two open
-- trades, and the loser fails at commit when the item is no longer in its
-- container. Locking it at offer time would let anyone freeze another player's
-- inventory by opening trades.
CREATE TABLE trade_offer_item (
    trade_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quantity        INT UNSIGNED NOT NULL DEFAULT 1,
    offered_revision INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (trade_id, instance_id),
    KEY ix_trade_offer_side (trade_id, character_id),
    CONSTRAINT fk_trade_offer_item_trade
        FOREIGN KEY (trade_id) REFERENCES trade_session (trade_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE trade_offer_currency (
    trade_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    character_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    currency_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    amount          BIGINT NOT NULL,
    PRIMARY KEY (trade_id, character_id, currency_id),
    CONSTRAINT ck_trade_currency_positive CHECK (amount > 0),
    CONSTRAINT fk_trade_offer_currency_trade
        FOREIGN KEY (trade_id) REFERENCES trade_session (trade_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE player_shop (
    shop_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name            VARCHAR(48) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    map_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    position_x      FLOAT NOT NULL DEFAULT 0,
    position_y      FLOAT NOT NULL DEFAULT 0,
    position_z      FLOAT NOT NULL DEFAULT 0,
    facing_degrees  FLOAT NOT NULL DEFAULT 0,
    -- 0 Open 1 Closed 2 Removed
    status          TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (shop_id),
    KEY ix_shop_owner (owner_character_id, status),
    KEY ix_shop_map (map_definition_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- A listing holds its item in escrow.
--
-- The listed item leaves its container: its container_slot row is deleted and its
-- lock_state becomes Listed. While listed it is in no bag, so it cannot be
-- equipped, consumed, socketed, traded or listed again -- not because every other
-- system remembers to check a flag, but because there is nothing for them to find.
--
-- UNIQUE on instance_id spans the whole table, so one item cannot be listed twice
-- even across shops. The partial-uniqueness problem (only *active* listings should
-- block) is handled by state, not by the index: a sold or cancelled listing keeps
-- its row for audit, so the index is on the item and re-listing a returned item
-- creates a new listing row with a new listing_id.
CREATE TABLE player_shop_listing (
    listing_id      VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    shop_id         VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    seller_character_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    seller_owner_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    instance_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    definition_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    quantity        INT UNSIGNED NOT NULL DEFAULT 1,
    currency_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    unit_price      BIGINT NOT NULL,
    -- Mirrors Phase 13 ShopListingState: 0 Active 1 Sold 2 Cancelled
    state           TINYINT UNSIGNED NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    settled_at      DATETIME(3) NULL DEFAULT NULL,
    PRIMARY KEY (listing_id),
    KEY ix_listing_shop (shop_id, state),
    KEY ix_listing_item (instance_id, state),
    CONSTRAINT ck_listing_price_positive CHECK (unit_price > 0),
    CONSTRAINT fk_listing_shop
        FOREIGN KEY (shop_id) REFERENCES player_shop (shop_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
