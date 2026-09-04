-- Accounts and their credentials.
--
-- Conventions established here and followed by every later migration:
--
--   Identity columns are VARCHAR(64) COLLATE utf8mb4_bin. Binary collation because
--   an identifier is opaque: 'A1B2' and 'a1b2' are different accounts, and a
--   case-insensitive collation would silently merge them. 64 characters holds a
--   GUID in "N" form (32) with room for another authority's scheme.
--
--   Display text is utf8mb4_unicode_ci, so a player name sorts and compares the
--   way a human expects and survives characters outside the BMP.
--
--   Every row that can change under optimistic concurrency carries `revision`,
--   matching the Revision type used since Phase 08.
--
--   Timestamps are DATETIME(3): millisecond precision, and not TIMESTAMP, whose
--   2038 limit is inside this game's plausible lifetime.

CREATE TABLE account (
    account_id      VARCHAR(64)  COLLATE utf8mb4_bin NOT NULL,
    display_name    VARCHAR(64)  COLLATE utf8mb4_unicode_ci NOT NULL,
    status          TINYINT UNSIGNED NOT NULL DEFAULT 1,
    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3)  NOT NULL,
    updated_at      DATETIME(3)  NOT NULL,
    PRIMARY KEY (account_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Credentials live in their own table, not on `account`.
--
-- Three reasons. A query that lists accounts must not accidentally select a hash;
-- keeping them apart makes `SELECT *` on `account` safe by construction. An
-- account may later hold several credentials (password, then an OAuth link) and a
-- column per method would not survive that. And the hash column can be granted
-- separately, so a reporting user can read accounts without reading secrets.
--
-- `login_identifier` is what a player types. It is unique and case-insensitively
-- collated, because "Ayla" and "ayla" must not be two accounts a player confuses.
CREATE TABLE account_credential (
    account_id      VARCHAR(64)  COLLATE utf8mb4_bin NOT NULL,
    login_identifier VARCHAR(190) COLLATE utf8mb4_unicode_ci NOT NULL,
    password_hash   VARCHAR(255) COLLATE utf8mb4_bin NOT NULL,
    updated_at      DATETIME(3)  NOT NULL,
    PRIMARY KEY (account_id),
    UNIQUE KEY uq_account_credential_login (login_identifier),
    CONSTRAINT fk_account_credential_account
        FOREIGN KEY (account_id) REFERENCES account (account_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
