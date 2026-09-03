using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored NPCs and shops are coherent.
    /// </summary>
    /// <remarks>
    /// A safety net for whoever authors content, not for the game -- the interaction
    /// service already refuses what it cannot resolve. A quest giver whose quest list points
    /// at a deleted quest should fail in the content pass, not turn up as an NPC a player
    /// cannot talk to.
    ///
    /// <b>Capability and content must agree.</b> An NPC marked a merchant with no shop, or
    /// carrying a shop it never offers, is authored wrong in a way nothing at runtime can
    /// repair. Those are the checks that matter most here.
    /// </remarks>
    public sealed class NpcContentValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var npc = definition as NPCDefinition;
            if (npc != null)
            {
                ValidateNpc(npc, lookup, report);
                return;
            }

            var shop = definition as ShopDefinition;
            if (shop != null) ValidateShop(shop, lookup, report);
        }

        private static void ValidateNpc(NPCDefinition npc, IDefinitionLookup lookup,
            ValidationReport report)
        {
            if (!npc.Map.IsValid)
            {
                // Not an error: an NPC may exist as content before it is placed.
                report.AddWarning(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC is on no map, so nobody can reach it.");
            }
            else
            {
                Require(lookup, npc.Id, npc.Map, "Map", report);
            }

            if (npc.SpawnPoint.IsValid)
            {
                Require(lookup, npc.Id, npc.SpawnPoint, "Spawn point", report);
            }
            else if (npc.Map.IsValid)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC has no spawn point, so its distance cannot be checked and it "
                    + "can be reached from anywhere on its map.");
            }

            if (npc.InteractionRadius < 0f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, npc.Id,
                    "The interaction radius is negative.");
            }

            // ---- capability against content --------------------------------------------

            if (npc.Shop.IsValid)
            {
                Require(lookup, npc.Id, npc.Shop, "Shop", report);
            }
            else if (npc.Category == NPCCategory.Merchant)
            {
                report.AddError(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC is a merchant but references no shop, so it has nothing to open.");
            }

            if (npc.IsQuestGiver && npc.Quests.Length == 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC is a quest giver but offers no quests.");
            }

            if (!npc.IsQuestGiver && npc.Quests.Length > 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC lists quests but is not marked a quest giver, so the role is "
                    + "never offered.");
            }

            for (int i = 0; i < npc.Quests.Length; i++)
            {
                Require(lookup, npc.Id, npc.Quests[i], "Quest " + i, report);
            }

            if (npc.IsJobChanger && npc.ClassesOffered.Length == 0 && npc.JobsOffered.Length == 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, npc.Id,
                    "The NPC is a job changer but offers no class or job.");
            }

            for (int i = 0; i < npc.ClassesOffered.Length; i++)
            {
                Require(lookup, npc.Id, npc.ClassesOffered[i], "Class " + i, report);
            }

            for (int i = 0; i < npc.JobsOffered.Length; i++)
            {
                Require(lookup, npc.Id, npc.JobsOffered[i], "Job " + i, report);
            }

            if (npc.Dialogue.IsValid)
            {
                Require(lookup, npc.Id, npc.Dialogue, "Dialogue", report);
            }
        }

        private static void ValidateShop(ShopDefinition shop, IDefinitionLookup lookup,
            ValidationReport report)
        {
            ShopEntry[] entries = shop.Entries;

            if (entries.Length == 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, shop.Id,
                    "The shop sells nothing.");
            }

            if (shop.BuyBackRate < 0f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, shop.Id,
                    "The buy-back rate is negative.");
            }

            for (int i = 0; i < entries.Length; i++)
            {
                ShopEntry entry = entries[i];

                if (!entry.Item.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, shop.Id,
                        "Entry " + i + " names no item.");
                    continue;
                }

                Require(lookup, shop.Id, entry.Item, "Entry " + i + " item", report);

                if (entry.Price < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, shop.Id,
                        "Entry '" + entry.Item + "' has a negative price.");
                }
            }
        }

        private static void Require(IDefinitionLookup lookup, DefinitionId owner,
            DefinitionId reference, string what, ValidationReport report)
        {
            if (lookup == null || !reference.IsValid) return;
            if (lookup.Contains(reference)) return;

            report.AddError(ValidationCode.MissingReference, owner,
                what + " '" + reference + "' does not resolve to any definition.");
        }
    }
}
