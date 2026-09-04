-- Currencies and the wallets that hold them.
--
-- Balances are BIGINT, never DECIMAL and never a float. Currency has been an
-- integer count since Phase 13 for one reason: a float balance makes
-- `a + b - b != a` true for ordinary values, which in an economy is not a rounding
-- curiosity but a duplication exploit. BIGINT rather than INT so the column can
-- hold more than the domain's int32 ceiling and an overflow is caught by a range
-- check rather than by wrapping.
--
-- `balance >= 0` is a CHECK constraint, so a negative balance is impossible even
-- if every layer above it were wrong. MySQL 8.0.16+ enforces CHECK.

CREATE TABLE currency_definition (
    currency_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name_key        VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    icon_ref        VARCHAR(190) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    -- Zero means no authored ceiling, matching CurrencyDefinition.MaximumBalance.
    maximum_balance BIGINT UNSIGNED NOT NULL DEFAULT 0,
    backing_item_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    enabled         TINYINT(1) NOT NULL DEFAULT 1,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (currency_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One row per owner per currency. The composite primary key is what makes a
-- second balance for the same pair impossible, and it is also the lookup path,
-- so no separate index is needed.
--
-- Keyed by owner_id rather than character_id: ownership has been an OwnerId since
-- Phase 08, and an account projects onto one. A second ownership notion here
-- would have to be reconciled with every check written in Phases 08-13.
CREATE TABLE character_currency (
    owner_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    currency_id     VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    balance         BIGINT NOT NULL DEFAULT 0,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    updated_at      DATETIME(3) NOT NULL,
    PRIMARY KEY (owner_id, currency_id),
    CONSTRAINT ck_wallet_non_negative CHECK (balance >= 0),
    CONSTRAINT fk_wallet_currency
        FOREIGN KEY (currency_id) REFERENCES currency_definition (currency_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
