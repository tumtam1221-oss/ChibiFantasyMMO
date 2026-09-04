-- Servers and their channels.
--
-- These are configuration an operator edits, not content Unity ships. Nothing in
-- PHP names a server, and there is no default or first-server rule: the list is
-- whatever these tables hold.

CREATE TABLE server_definition (
    server_id            VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name_key             VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,
    region               VARCHAR(32)  COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',

    -- Mirrors Phase 14 ServerStatus by ordinal:
    -- 0 Unknown 1 Online 2 Busy 3 Maintenance 4 Offline 5 Hidden
    status               TINYINT UNSIGNED NOT NULL DEFAULT 1,
    enabled              TINYINT(1) NOT NULL DEFAULT 1,

    -- Zero means no authored ceiling, matching PopulationReading.
    capacity             INT UNSIGNED NOT NULL DEFAULT 0,

    -- Cached and informational only. NULL means genuinely unknown, which the API
    -- reports as unknown rather than as zero. Never written by a client.
    cached_population    INT UNSIGNED NULL DEFAULT NULL,
    population_sampled_at DATETIME(3) NULL DEFAULT NULL,

    -- The version floor, per server, so a staged rollout works.
    min_client_version   VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    latest_client_version VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    required_protocol_version VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    min_content_version  VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    latest_content_version VARCHAR(32) COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    content_is_advisory  TINYINT(1) NOT NULL DEFAULT 0,

    revision             INT UNSIGNED NOT NULL DEFAULT 0,
    created_at           DATETIME(3) NOT NULL,
    updated_at           DATETIME(3) NOT NULL,

    PRIMARY KEY (server_id),
    KEY ix_server_listing (enabled, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- A channel belongs to exactly one server, and the foreign key is what enforces
-- it. Phase 14 refuses a channel whose server does not match the selection; this
-- is the same rule one level down, where it cannot be bypassed by a bug above.
--
-- `pk_enabled` is the configuration seam the game design asked for: an
-- administrator sets it here, the API reports it, the client only displays it.
-- Nothing derives PK from a channel number.
CREATE TABLE server_channel (
    channel_id      VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    server_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    name_key        VARCHAR(128) COLLATE utf8mb4_bin NOT NULL,

    -- Mirrors Phase 14 ChannelStatus: 0 Unknown 1 Online 2 Busy 3 Maintenance 4 Offline
    status          TINYINT UNSIGNED NOT NULL DEFAULT 1,
    enabled         TINYINT(1) NOT NULL DEFAULT 1,
    capacity        INT UNSIGNED NOT NULL DEFAULT 0,
    cached_population INT UNSIGNED NULL DEFAULT NULL,
    population_sampled_at DATETIME(3) NULL DEFAULT NULL,

    pk_enabled      TINYINT(1) NOT NULL DEFAULT 0,

    revision        INT UNSIGNED NOT NULL DEFAULT 0,
    created_at      DATETIME(3) NOT NULL,
    updated_at      DATETIME(3) NOT NULL,

    PRIMARY KEY (channel_id),
    KEY ix_channel_by_server (server_id, enabled, status),
    CONSTRAINT fk_channel_server
        FOREIGN KEY (server_id) REFERENCES server_definition (server_id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
