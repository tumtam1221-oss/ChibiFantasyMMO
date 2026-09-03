using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored Devil Fruits, cards and pets are coherent.
    /// </summary>
    /// <remarks>
    /// One rule for the three because they are validated the same way and against the same
    /// registries, exactly as <see cref="MapContentValidationRule"/> covers maps, spawn
    /// points and portals. Three near-identical files would drift.
    ///
    /// The services already refuse malformed content at runtime, so this is not a safety net
    /// for the game -- it is one for whoever authors it. A pet whose evolution points back at
    /// itself should fail in the content pass naming the row, not turn up as a player who can
    /// evolve forever.
    ///
    /// Deterministic and read-only: nothing here mutates a definition or any runtime state.
    /// </remarks>
    public sealed class CollectibleContentValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var fruit = definition as DevilFruitDefinition;
            if (fruit != null)
            {
                ValidateFruit(fruit, lookup, report);
                return;
            }

            var card = definition as CardDefinition;
            if (card != null)
            {
                ValidateCard(card, lookup, report);
                return;
            }

            var pet = definition as PetDefinition;
            if (pet != null)
            {
                ValidatePet(pet, lookup, report);
                return;
            }

            var table = definition as DropTableDefinition;
            if (table != null) ValidateDropTable(table, lookup, report);
        }

        // ---- devil fruits --------------------------------------------------------------

        private static void ValidateFruit(DevilFruitDefinition fruit, IDefinitionLookup lookup,
            ValidationReport report)
        {
            // A fruit that does nothing is content nobody can tell is broken from in-game.
            bool doesSomething = fruit.PassiveAbility.IsValid
                || fruit.ActiveAbility.IsValid
                || fruit.GrantedEffects.Length > 0
                || fruit.Immunities.Length > 0
                || fruit.ImmuneCategories.Length > 0
                || fruit.StatModifiers.Length > 0;

            if (!doesSomething)
            {
                report.AddError(ValidationCode.InvalidConfiguration, fruit.Id,
                    "The Devil Fruit grants nothing: no passive, active, effect, immunity or "
                    + "modifier. A player who ate it would gain nothing.");
            }

            Require(lookup, fruit.Id, fruit.PassiveAbility, "Passive ability", report);
            Require(lookup, fruit.Id, fruit.ActiveAbility, "Active ability", report);
            Require(lookup, fruit.Id, fruit.Rarity, "Rarity", report);
            Require(lookup, fruit.Id, fruit.SourceBoss, "Source boss", report);
            Require(lookup, fruit.Id, fruit.DropTable, "Drop table", report);

            DefinitionId[] effects = fruit.GrantedEffects;

            for (int i = 0; i < effects.Length; i++)
            {
                Require(lookup, fruit.Id, effects[i], "Granted effect " + i, report);
            }

            DefinitionId[] immunities = fruit.Immunities;

            for (int i = 0; i < immunities.Length; i++)
            {
                Require(lookup, fruit.Id, immunities[i], "Immunity " + i, report);
            }

            if (!fruit.VisualEffect.IsValid)
            {
                // Not an error: a fruit may exist as content before its effect does.
                report.AddWarning(ValidationCode.InvalidConfiguration, fruit.Id,
                    "The Devil Fruit references no visual effect.");
            }
        }

        // ---- cards ---------------------------------------------------------------------

        private static void ValidateCard(CardDefinition card, IDefinitionLookup lookup,
            ValidationReport report)
        {
            bool doesSomething = card.StatModifiers.Length > 0
                || card.GrantedEffects.Length > 0
                || card.Effects.Length > 0;

            if (!doesSomething)
            {
                report.AddError(ValidationCode.InvalidConfiguration, card.Id,
                    "The card grants nothing: no modifier, effect or granted status.");
            }

            Require(lookup, card.Id, card.Rarity, "Rarity", report);
            Require(lookup, card.Id, card.SourceMonster, "Source monster", report);
            Require(lookup, card.Id, card.DropTable, "Drop table", report);

            DefinitionId[] granted = card.GrantedEffects;

            for (int i = 0; i < granted.Length; i++)
            {
                Require(lookup, card.Id, granted[i], "Granted effect " + i, report);
            }

            CardEffect[] effects = card.Effects;

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].IsValid) continue;

                report.AddError(ValidationCode.InvalidConfiguration, card.Id,
                    "Effect " + i + " has no kind, or a magnitude that is not a number.");
            }

            if (card.MaxPerEquipment < 1)
            {
                report.AddError(ValidationCode.ValueOutOfRange, card.Id,
                    "The per-equipment limit is below one, so the card could never be socketed.");
            }

            // A card must be findable in the item registry too, because ownership is an item
            // concern: the definition below describes what it does, the item is what is held.
            if (lookup != null && !lookup.Contains(card.Id))
            {
                report.AddError(ValidationCode.MissingReference, card.Id,
                    "No ItemDefinition shares the card's id, so the card could never be owned.");
            }
        }

        // ---- pets ----------------------------------------------------------------------

        private static void ValidatePet(PetDefinition pet, IDefinitionLookup lookup,
            ValidationReport report)
        {
            if (!pet.Model.IsValid)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, pet.Id,
                    "The pet references no model, so presentation has nothing to show.");
            }

            if (!pet.Icon.IsValid)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, pet.Id,
                    "The pet references no icon.");
            }

            Require(lookup, pet.Id, pet.BaseBuff, "Base buff", report);

            ValidateExperienceCurve(pet, report);

            if (pet.MaxLevel < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                    "The maximum level is negative.");
            }

            PetEvolutionStage[] stages = pet.EvolutionStages;

            for (int i = 0; i < stages.Length; i++)
            {
                PetEvolutionStage stage = stages[i];

                if (stage.RequiredLevel < 1)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                        "Evolution stage " + i + " requires a level below one.");
                }

                if (stage.RequiredLevel > pet.EffectiveMaxLevel)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                        "Evolution stage " + i + " requires level " + stage.RequiredLevel
                        + ", which is past the pet's ceiling of " + pet.EffectiveMaxLevel
                        + "; it could never be reached.");
                }

                if (stage.RequiredExperience < 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                        "Evolution stage " + i + " requires negative experience.");
                }

                Require(lookup, pet.Id, stage.EvolvedForm, "Stage " + i + " evolved form", report);
                Require(lookup, pet.Id, stage.GrantedBuff, "Stage " + i + " granted buff", report);
                Require(lookup, pet.Id, stage.RequiredItem, "Stage " + i + " required item", report);
            }
        }

        private static void ValidateExperienceCurve(PetDefinition pet, ValidationReport report)
        {
            int[] thresholds = pet.ExperienceThresholds;
            int previous = 0;

            for (int i = 0; i < thresholds.Length; i++)
            {
                if (thresholds[i] <= 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                        "Experience threshold " + i + " is not positive, so the level above it "
                        + "would be reached at zero experience.");
                    continue;
                }

                // Cumulative totals only go up. A curve that dips would make a level
                // unreachable, or reachable and then lost.
                if (thresholds[i] <= previous)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, pet.Id,
                        "Experience threshold " + i + " (" + thresholds[i]
                        + ") is not above the previous one (" + previous
                        + "); thresholds are cumulative and must ascend.");
                }

                previous = thresholds[i];
            }
        }

        // ---- drop tables ---------------------------------------------------------------

        /// <summary>
        /// Checks authored probabilities.
        /// </summary>
        /// <remarks>
        /// Fail-fast rather than clamped. An operator who typed a chance above one, or an
        /// import that produced NaN, has a configuration error; silently correcting it would
        /// hide the mistake behind a drop rate nobody chose. The resolver skips such a row,
        /// so the error is reported without garbage reaching a player either way.
        ///
        /// Nothing here knows what kind of item a row drops. A Devil Fruit row and a copper
        /// coin row are checked by the same three lines.
        /// </remarks>
        private static void ValidateDropTable(DropTableDefinition table, IDefinitionLookup lookup,
            ValidationReport report)
        {
            DropEntry[] entries = table.Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                DropEntry entry = entries[i];

                if (!entry.Item.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, table.Id,
                        "Drop entry " + i + " names no item.");
                    continue;
                }

                Require(lookup, table.Id, entry.Item, "Drop entry " + i + " item", report);

                if (entry.MinQuantity < 1)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, table.Id,
                        "Drop entry " + i + " has a minimum quantity below one.");
                }

                if (!entry.IsChanceValid)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, table.Id,
                        "Drop entry " + i + " has a chance that is not a probability in 0..1. "
                        + "Chance is a fraction, never a percentage.");
                }

                Require(lookup, table.Id, entry.RarityOverride,
                    "Drop entry " + i + " rarity override", report);
            }
        }

        // ---- cross-definition checks ---------------------------------------------------

        /// <summary>
        /// Walks every pet's evolution chain looking for a loop.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Validate"/> because it is a question about the whole set,
        /// not about one definition: <c>A -&gt; B -&gt; A</c> is invisible from inside either
        /// pet. A straight chain <c>A -&gt; B -&gt; C</c> is fine and must stay fine, so this
        /// looks for a revisit rather than for depth.
        ///
        /// A cycle is an error rather than a warning because it is not merely odd content: a
        /// pet on a loop could be evolved forever, paying its material each time, which is an
        /// economy hole as much as a design mistake.
        /// </remarks>
        public static void ValidateEvolutionChains(IDefinitionRegistry<PetDefinition> pets,
            ValidationReport report)
        {
            if (pets == null || report == null) return;

            IReadOnlyList<PetDefinition> all = pets.All;
            var seen = new List<DefinitionId>();

            for (int i = 0; i < all.Count; i++)
            {
                PetDefinition pet = all[i];
                if (pet == null) continue;

                seen.Clear();
                seen.Add(pet.Id);

                PetDefinition current = pet;

                while (true)
                {
                    PetEvolutionStage[] stages = current.EvolutionStages;
                    if (stages.Length == 0) break;

                    DefinitionId next = stages[0].EvolvedForm;
                    if (!next.IsValid) break;

                    if (Contains(seen, next))
                    {
                        report.AddError(ValidationCode.InvalidConfiguration, pet.Id,
                            "The pet's evolution chain returns to '" + next
                            + "', so it could be evolved without end.");
                        break;
                    }

                    seen.Add(next);

                    PetDefinition following;
                    if (!pets.TryGet(next, out following) || following == null) break;

                    current = following;
                }
            }
        }

        /// <summary>
        /// Checks that Devil Fruits are only reachable from world bosses.
        /// </summary>
        /// <remarks>
        /// <b>Eligibility and probability are separate concerns.</b> How rare a fruit is lives
        /// in <see cref="DropEntry.Chance"/> and is an operator's to change. <em>Which</em>
        /// monsters may drop one at all is content, and this is where it is checked: a table
        /// carrying a fruit must only be referenced by a monster whose authored
        /// <see cref="MonsterRank"/> is <see cref="MonsterRank.WorldBoss"/>.
        ///
        /// The test is on the rank, never on a monster id. A new world boss inherits the
        /// permission by being authored as one, and a normal monster cannot be given a fruit
        /// table by accident without this saying so.
        ///
        /// Nothing here is a runtime branch: <c>DropResolver</c> has no idea any of this
        /// exists and rolls a fruit row exactly as it rolls a coin row.
        /// </remarks>
        public static void ValidateWorldBossOnlyDrops(
            IDefinitionRegistry<MonsterDefinition> monsters,
            IDefinitionRegistry<DropTableDefinition> tables,
            IDefinitionRegistry<ItemDefinition> items, ValidationReport report)
        {
            if (monsters == null || tables == null || items == null || report == null) return;

            IReadOnlyList<MonsterDefinition> all = monsters.All;

            for (int i = 0; i < all.Count; i++)
            {
                MonsterDefinition monster = all[i];
                if (monster == null || monster.Rank == MonsterRank.WorldBoss) continue;
                if (!monster.LootTable.IsValid) continue;

                DropTableDefinition table;
                if (!tables.TryGet(monster.LootTable, out table) || table == null) continue;

                DropEntry[] entries = table.Entries;

                for (int e = 0; e < entries.Length; e++)
                {
                    ItemDefinition item;
                    if (!items.TryGet(entries[e].Item, out item) || item == null) continue;

                    if (item.Category != ItemCategory.DevilFruit) continue;

                    report.AddError(ValidationCode.InvalidConfiguration, monster.Id,
                        "The monster is rank " + monster.Rank + " but its table '" + table.Id
                        + "' can drop Devil Fruit '" + item.Id
                        + "'. Devil Fruits are world-boss content.");
                }
            }
        }

        private static bool Contains(List<DefinitionId> ids, DefinitionId id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return true;
            }

            return false;
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
