-- Where a character was standing when they left.
--
-- Until now the row remembered only which map and which authored spawn a character
-- belonged to, so every login put them back on the spawn. That is correct for a first
-- entry and wrong for a returning player, who expects to pick up where they stopped.
--
-- Nullable on purpose. NULL means "this character has never been saved with a position",
-- which is exactly the case for every row that already exists, and the world falls back
-- to the authored spawn for those. No existing data is rewritten by this migration.
--
-- DOUBLE rather than DECIMAL because these are engine coordinates, not money: a metre is
-- not a unit anyone counts exactly, and the world is 240 m across.
ALTER TABLE `character`
    ADD COLUMN position_x DOUBLE NULL DEFAULT NULL AFTER spawn_definition_id,
    ADD COLUMN position_y DOUBLE NULL DEFAULT NULL AFTER position_x,
    ADD COLUMN position_z DOUBLE NULL DEFAULT NULL AFTER position_y;
