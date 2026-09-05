-- Whose turn it is to receive round-robin loot, kept across a world restart.
--
-- On the party row rather than in a table of its own: it is one integer per party,
-- written by the same statement that already writes the party's leader and policy,
-- so it commits or rolls back with them and cannot end up describing a membership
-- that was never stored.
--
-- The value is an INDEX into the member list ordered by join_order, not a running
-- count of piles handed out. A counter would grow without bound and would mean
-- nothing on its own; an index is bounded by the party size, and the world
-- normalises it against the membership it is writing, so `0 <= cursor < members`
-- holds for every row this column ever receives. A row outside that range is
-- corrupt, and the world refuses to restore it rather than quietly taking a
-- modulo and giving somebody else's turn away.
ALTER TABLE party
    ADD COLUMN round_robin_cursor INT UNSIGNED NOT NULL DEFAULT 0 AFTER loot_policy;
