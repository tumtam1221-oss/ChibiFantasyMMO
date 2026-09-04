-- Where monsters come from, and how they behave, as configuration rather than code.
--
-- Both tables reference authored content by DefinitionId and copy none of it. There is
-- deliberately no map table and no monster table here: maps and monsters are Unity
-- content, and duplicating them in MySQL would create a second source of truth that
-- goes stale on the next content patch. The server validates that a referenced id
-- actually resolves against the loaded registries before anything reaches the runtime,
-- which is a stronger guarantee than a foreign key to a copy would give.
--
-- Additive only: two new tables, nothing existing altered.

-- One spawn point: what appears, where, how many, and how often.
--
-- Mirrors the Phase 10 `MonsterSpawnPoint` field for field, because that is the type
-- the runtime already consumes. A shape that did not match would need a translation
-- layer, and a translation layer is where spawn rules quietly diverge.
CREATE TABLE monster_spawn_point (
    spawn_point_id       VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Authored DefinitionIds. Validated against the content registries at load, never
    -- against a copy of the content held here.
    map_definition_id    VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,
    monster_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- The centre of the nest. Authoritative: a client never supplies a spawn position,
    -- and the server never derives one from anything a client sent.
    --
    -- FLOAT rather than DECIMAL: this is a position in a world, not money. The rounding
    -- that makes DECIMAL right for a balance is irrelevant to a coordinate, and FLOAT is
    -- what the runtime uses.
    position_x           FLOAT NOT NULL DEFAULT 0,
    position_y           FLOAT NOT NULL DEFAULT 0,
    position_z           FLOAT NOT NULL DEFAULT 0,

    -- How far from the centre a monster may appear. Zero means exactly on the point.
    spawn_radius         FLOAT NOT NULL DEFAULT 0,

    -- How many appear when a map first populates, and the ceiling thereafter.
    -- initial_spawn_count <= max_alive is enforced by the server, not by a CHECK:
    -- a cross-column CHECK would refuse the row outright, and an operator who typed one
    -- number wrong should get a named validation failure rather than a SQL error.
    initial_spawn_count  INT UNSIGNED NOT NULL DEFAULT 0,
    max_alive            INT UNSIGNED NOT NULL DEFAULT 1,

    -- Seconds before a defeated monster is replaced. Zero means immediately.
    respawn_seconds      FLOAT NOT NULL DEFAULT 0,

    -- A disabled point stops producing new monsters. It does not remove the ones already
    -- standing -- deleting a player's target because a designer unticked a box would be
    -- a surprising way to lose a fight.
    enabled              TINYINT(1) NOT NULL DEFAULT 1,

    -- Free-text grouping so a designer can enable or disable a set together. Nullable
    -- because most points belong to no group, and a mandatory group would invent one.
    spawn_group_id       VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL,

    created_at           DATETIME(3) NOT NULL,
    updated_at           DATETIME(3) NOT NULL,

    PRIMARY KEY (spawn_point_id),

    -- The load query: every enabled point on one map. Maps are loaded independently, so
    -- this is the only access pattern that matters.
    KEY ix_monster_spawn_map (map_definition_id, enabled),
    KEY ix_monster_spawn_group (spawn_group_id),

    CONSTRAINT ck_monster_spawn_radius CHECK (spawn_radius >= 0),
    CONSTRAINT ck_monster_spawn_respawn CHECK (respawn_seconds >= 0),
    CONSTRAINT ck_monster_spawn_max_alive CHECK (max_alive > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- How a monster behaves, overriding what the definition was authored with.
--
-- Keyed by monster, not by spawn point, and that is the design. Behaviour is a property
-- of the creature: a Poring is placid wherever it stands. Tying aggression to a spawn
-- point would let the same monster be placid in one clearing and hostile in the next,
-- which is a different feature and one nobody asked for.
--
-- Every column is nullable and NULL means "use the authored value". That way a row can
-- change one thing -- make Goblins defensive -- without an operator having to restate
-- five numbers they did not intend to touch, and without this table becoming a second
-- copy of MonsterDefinition.
CREATE TABLE monster_ai_configuration (
    monster_definition_id VARCHAR(64) COLLATE utf8mb4_bin NOT NULL,

    -- Mirrors the existing Phase 10 MonsterAggressionType:
    --   0 Passive  1 Defensive  2 Aggressive  3 AssistOnly
    -- The enum is not redefined here; this column stores its value. A CHECK keeps a
    -- typo from reaching the runtime as an unknown behaviour.
    aggression_type      TINYINT UNSIGNED NULL DEFAULT NULL,

    detection_range      FLOAT NULL DEFAULT NULL,

    -- The project's "how far will it follow you" bound is LeashRange, authored on the
    -- monster since Phase 10. Named chase_range here because that is what an operator
    -- calls it; it maps onto LeashRange rather than introducing a second bound.
    chase_range          FLOAT NULL DEFAULT NULL,

    attack_range         FLOAT NULL DEFAULT NULL,
    attack_cooldown      FLOAT NULL DEFAULT NULL,
    move_speed           FLOAT NULL DEFAULT NULL,

    enabled              TINYINT(1) NOT NULL DEFAULT 1,

    created_at           DATETIME(3) NOT NULL,
    updated_at           DATETIME(3) NOT NULL,

    PRIMARY KEY (monster_definition_id),

    CONSTRAINT ck_monster_ai_aggression
        CHECK (aggression_type IS NULL OR aggression_type BETWEEN 0 AND 3),
    CONSTRAINT ck_monster_ai_detection CHECK (detection_range IS NULL OR detection_range >= 0),
    CONSTRAINT ck_monster_ai_chase CHECK (chase_range IS NULL OR chase_range >= 0),
    CONSTRAINT ck_monster_ai_attack CHECK (attack_range IS NULL OR attack_range >= 0),
    CONSTRAINT ck_monster_ai_cooldown CHECK (attack_cooldown IS NULL OR attack_cooldown >= 0),
    CONSTRAINT ck_monster_ai_speed CHECK (move_speed IS NULL OR move_speed >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
