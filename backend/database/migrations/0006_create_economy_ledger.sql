-- The append-only ledger.
--
-- There is no UPDATE and no DELETE path against these tables anywhere in the
-- backend, and a test asserts that. A record that could be edited afterwards is
-- not an audit trail.
--
-- Header plus lines: `economy_transaction` says what happened, the entry tables
-- say who gained and lost what. That shape is what lets a reader sum a wallet's
-- history and check it against the balance -- an invariant the tests assert after
-- every kind of movement.

CREATE TABLE economy_transaction (
    transaction_id  VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    request_id      VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,
    -- Mirrors EconomyTransactionType: 0 None 1 Credit 2 Debit 3 Transfer 4 Exchange
    type            TINYINT UNSIGNED NOT NULL,
    -- Mirrors EconomySource: 0 Unknown 1 MonsterLoot 2 QuestReward 3 NpcShop
    -- 4 PlayerTrade 5 PlayerShop 6 AdminAdjustment 7 SystemReward 8 Other
    source          TINYINT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (transaction_id),
    -- One request produces at most one transaction. The unique index is the real
    -- protection against a double-committed retry; a check in PHP would not
    -- survive two concurrent requests.
    UNIQUE KEY uq_economy_transaction_request (request_id),
    KEY ix_economy_transaction_time (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One currency movement for one owner.
--
-- balance_before and balance_after are both recorded so a line is checkable on
-- its own: a reader can confirm before + delta = after without replaying the game.
CREATE TABLE economy_transaction_entry (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    transaction_id  VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    currency_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    delta           BIGINT NOT NULL,
    balance_before  BIGINT NOT NULL,
    balance_after   BIGINT NOT NULL,
    related_item_instance_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,
    PRIMARY KEY (id),
    KEY ix_entry_owner_currency (owner_id, currency_id, id),
    KEY ix_entry_transaction (transaction_id),
    CONSTRAINT fk_entry_transaction
        FOREIGN KEY (transaction_id) REFERENCES economy_transaction (transaction_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One item changing hands. Both revisions are recorded, so a reader can see that
-- the copy that arrived is the copy that left and that exactly one mutation
-- happened to it.
CREATE TABLE item_transaction_entry (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    transaction_id  VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    item_instance_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    item_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    from_owner_id   VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    to_owner_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    quantity        INT UNSIGNED NOT NULL DEFAULT 1,
    from_revision   INT UNSIGNED NOT NULL DEFAULT 0,
    to_revision     INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    KEY ix_item_history (item_instance_id, id),
    KEY ix_item_entry_transaction (transaction_id),
    CONSTRAINT fk_item_entry_transaction
        FOREIGN KEY (transaction_id) REFERENCES economy_transaction (transaction_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
