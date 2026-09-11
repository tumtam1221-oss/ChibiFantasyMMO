-- The world's calendar, so a restart does not put the sun back at breakfast.
--
-- Until now the time of day lived only in the running server process. Every restart began
-- the day again at 08:00 on day zero, which is invisible while nothing reads the date and
-- becomes wrong the moment anything does -- a festival calendar being the obvious case.
--
-- One row per world, keyed by the server and channel the process serves, because that is
-- the unit a world server already is. Two channels are two worlds and may legitimately be
-- at different hours; a single row keyed by nothing would make them fight over it.
--
-- elapsed_seconds is the whole clock, not a time of day: the day number is derived from it
-- by division, so storing the hour alone would lose the date every night at midnight. It is
-- DOUBLE for the same reason positions are -- this is a measurement, not money -- and a
-- double holds a century of seconds without losing a millisecond.
--
-- seconds_per_day is stored beside it because the elapsed total means nothing without the
-- rate it was measured against. An operator who lengthens the day and restarts would
-- otherwise reinterpret every stored second and move the world by weeks.
--
-- No existing data is touched. A world with no row here has simply never been saved, and
-- the server starts it at the authored time exactly as it does today.
CREATE TABLE world_clock
(
    server_id       VARCHAR(64)  NOT NULL,
    channel_id      VARCHAR(64)  NOT NULL,
    elapsed_seconds DOUBLE       NOT NULL,
    seconds_per_day DOUBLE       NOT NULL,
    updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
        ON UPDATE CURRENT_TIMESTAMP,

    PRIMARY KEY (server_id, channel_id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
