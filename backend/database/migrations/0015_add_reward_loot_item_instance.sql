-- The identity a decided drop will have once somebody is carrying it.
--
-- Item instances used to be minted at pickup, which left one window open that nothing
-- could close: the inventory containing the new item committed, the reward's delivery
-- stamp did not, and the world stopped in between. Recovery then saw an unclaimed entry,
-- put the pile back, and a second pickup minted a *second* item -- one drop, two items,
-- and no way afterwards to tell which was real.
--
-- Deciding the identity with the reward turns it into an idempotency key. The same entry
-- delivered twice produces the same instance id, so the second attempt can recognise what
-- it is looking at and reconcile instead of duplicating.
--
-- Not a second kind of id: this is the same InstanceId the pickup would have minted,
-- chosen earlier. Nothing new generates it and nothing client-side ever supplies it.
--
-- Additive only: one column on a table added by 0014, which is not rewritten.
--
-- NULL rather than empty for "no identity decided". A UNIQUE index counts '' as a value
-- and would refuse the second such row, so an empty default would make rows written
-- before this column existed collide with each other. NULL repeats freely under UNIQUE,
-- which is exactly the meaning wanted: unknown, and not equal to any other unknown.
ALTER TABLE monster_reward_loot
    ADD COLUMN item_instance_id VARCHAR(64) COLLATE utf8mb4_bin NULL DEFAULT NULL
        AFTER rarity_definition_id;

-- One identity, one item, anywhere in the game.
--
-- The UNIQUE is what makes the guarantee a database guarantee rather than a convention:
-- two reward entries cannot be written claiming to become the same item, however they
-- race, and a corrupt payload that repeats an identity is refused by MySQL rather than
-- by a check somebody might forget to write.
CREATE UNIQUE INDEX uq_monster_reward_loot_instance
    ON monster_reward_loot (item_instance_id);
