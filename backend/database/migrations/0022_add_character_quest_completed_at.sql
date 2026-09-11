-- When a quest was finished, so a daily one can come back tomorrow.
--
-- Until now a quest was either finished or not, with no record of when. That is all a
-- one-time quest needs, and it is exactly what a daily quest cannot work without: "have
-- you already done today's patrol" is a question about a date, and there was no date.
--
-- Written by MySQL, never by PHP and never by the game. The project settled this once
-- already -- see 0021 and the OneClockTest -- because every datetime column here is a
-- naked DATETIME with no offset, so a row written by a process in a different timezone is
-- unrecoverably ambiguous. NOW(3) on the insert keeps one clock writing the times down.
--
-- NULL means "not finished", which is what every existing row means today. No backfill:
-- a character who has never completed a quest has no completion time, and inventing one
-- would make yesterday's quests look done.
--
-- The reset boundary is midnight in the deployment's timezone. Rather than ship a
-- timezone conversion into the game, the reads below hand out TO_DAYS() of this column
-- and of NOW(3) together -- two integers from the same clock, which the game can compare
-- without knowing what a timezone is.

ALTER TABLE character_quest
    ADD COLUMN completed_at DATETIME(3) NULL DEFAULT NULL AFTER status;

-- "Which dailies has this character finished today" is the query a quest log runs on
-- every login, and it is answered from this index without touching the row.
ALTER TABLE character_quest
    ADD KEY ix_character_quest_completed (character_id, completed_at);
