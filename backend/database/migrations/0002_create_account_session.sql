-- Account sessions, and the idempotency record that makes retries safe.
--
-- The `state` column mirrors Phase 14's SessionState exactly, by ordinal:
--   0 Unauthenticated  1 Authenticated  2 ServerSelected  3 ChannelSelected
--   4 CharacterSelected 5 EnteringWorld 6 Active 7 Expired 8 Revoked
-- One model, shared by the domain and the database. A second, incompatible state
-- vocabulary here is exactly what the brief forbids.

CREATE TABLE account_session (
    session_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    account_id        VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    state             TINYINT UNSIGNED NOT NULL DEFAULT 1,

    -- The versions the client reported at login, re-checked at enter-world.
    client_version    VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    protocol_version  VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    content_version   VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',

    -- Selections. NULL rather than '' so "nothing chosen" is distinct from a
    -- choice, and so the foreign keys below can be enforced when one is set.
    selected_server_id  VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,
    selected_channel_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,
    selected_character_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,

    issued_at         DATETIME(3) NOT NULL,
    expires_at        DATETIME(3) NULL DEFAULT NULL,
    revoked_at        DATETIME(3) NULL DEFAULT NULL,
    revision          INT UNSIGNED NOT NULL DEFAULT 0,

    PRIMARY KEY (session_id),
    KEY ix_account_session_account (account_id, state),
    KEY ix_account_session_expiry (expires_at),
    CONSTRAINT fk_account_session_account
        FOREIGN KEY (account_id) REFERENCES account (account_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The bearer token, kept apart from the session it authenticates.
--
-- Only a hash is stored. A token is a password: if the table leaks, holding the
-- plaintext would hand an attacker every live session. SHA-256 is right here and
-- password_hash() is not -- a token already has 256 bits of CSPRNG entropy, so
-- there is nothing to brute-force and no need for a slow KDF on every request.
CREATE TABLE account_session_token (
    session_id      VARCHAR(64)  COLLATE utf8mb4_bin NOT NULL,
    token_hash      CHAR(64)     COLLATE utf8mb4_bin NOT NULL,
    issued_at       DATETIME(3)  NOT NULL,
    PRIMARY KEY (session_id),
    UNIQUE KEY uq_session_token_hash (token_hash),
    CONSTRAINT fk_session_token_session
        FOREIGN KEY (session_id) REFERENCES account_session (session_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Idempotency, following the rule Phase 13 established.
--
-- A request key maps to at most one committed outcome. The UNIQUE constraint is
-- the real protection: two concurrent retries race to insert, one wins, the loser
-- reads the winner's row. A check-then-act in PHP would not survive that race.
--
-- Only accepted outcomes are recorded. A rejection wrote nothing, so re-sending it
-- must be re-judged -- the cause (a full server, a lapsed session, a stale build)
-- may no longer hold, and a player who fixed it should succeed.
--
-- `scope` separates operations, so the same key used for a login and for a
-- purchase cannot collide.
CREATE TABLE request_result (
    request_id      VARCHAR(64)  COLLATE utf8mb4_bin NOT NULL,
    scope           VARCHAR(32)  COLLATE utf8mb4_bin NOT NULL,
    account_id      VARCHAR(64)  COLLATE utf8mb4_bin NULL DEFAULT NULL,
    response_json   TEXT         COLLATE utf8mb4_bin NOT NULL,
    created_at      DATETIME(3)  NOT NULL,
    PRIMARY KEY (request_id, scope),
    KEY ix_request_result_account (account_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Login attempts, for the rate-limiting seam.
--
-- Keyed by identifier and by address, because the two attacks differ: one account
-- hammered from everywhere, and one address spraying many accounts. Rows are
-- pruned by age, so this stays small.
--
-- The identifier is stored, the password never is -- not even a hash, not even a
-- length. A failed-login log that records what was tried is a credential leak.
CREATE TABLE login_attempt (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    login_identifier VARCHAR(190) COLLATE utf8mb4_unicode_ci NOT NULL,
    remote_address  VARCHAR(45)  COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    succeeded       TINYINT(1)   NOT NULL DEFAULT 0,
    attempted_at    DATETIME(3)  NOT NULL,
    PRIMARY KEY (id),
    KEY ix_login_attempt_identifier (login_identifier, attempted_at),
    KEY ix_login_attempt_address (remote_address, attempted_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
