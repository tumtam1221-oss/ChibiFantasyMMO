using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Checks that authored monsters, drop tables and quests are coherent.
    /// </summary>
    /// <remarks>
    /// The services already refuse malformed content at runtime, so this is not a safety
    /// net for the game -- it is one for whoever authors it. A quest whose reward names a
    /// deleted item should fail in the content pass pointing at the row, not turn up as a
    /// player who cannot claim it.
    ///
    /// Plugs into the existing <see cref="DefinitionValidator"/>, so a broken drop table is
    /// caught in the same pass as everything else.
    ///
    /// Error versus warning is whether content is <em>wrong</em> or merely <em>inert</em>: a
    /// quest with no objectives is an error because it can never be finished; a monster with
    /// no loot table is perfectly normal.
    /// </remarks>
    public sealed class WorldContentValidationRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup,
            ValidationReport report)
        {
            var monster = definition as MonsterDefinition;
            if (monster != null)
            {
                ValidateMonster(monster, lookup, report);
                return;
            }

            var table = definition as DropTableDefinition;
            if (table != null)
            {
                ValidateDropTable(table, lookup, report);
                return;
            }

            var quest = definition as QuestDefinition;
            if (quest != null) ValidateQuest(quest, lookup, report);
        }

        // ---- monsters ------------------------------------------------------------------

        private static void ValidateMonster(MonsterDefinition monster, IDefinitionLookup lookup,
            ValidationReport report)
        {
            if (monster.Level < 1)
            {
                report.AddError(ValidationCode.ValueOutOfRange, monster.Id,
                    "Level " + monster.Level + " is below one.");
            }

            if (monster.ExperienceReward < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, monster.Id,
                    "Experience reward is negative.");
            }

            if (monster.CurrencyReward < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, monster.Id,
                    "Currency reward is negative.");
            }

            if (monster.DetectionRange < 0f || monster.AttackRange < 0f
                || monster.LeashRange < 0f || monster.MoveSpeed < 0f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, monster.Id,
                    "A range or speed is negative.");
            }

            // An aggressive monster that notices nothing will stand still forever, which
            // looks like a bug rather than a design.
            if (monster.AggressionType != MonsterAggressionType.Passive
                && monster.DetectionRange <= 0f)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, monster.Id,
                    "Aggressive but has no detection range, so it will never engage.");
            }

            // A leash inside the detection range means it gives up before it arrives.
            if (monster.LeashRange > 0f && monster.LeashRange < monster.AttackRange)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, monster.Id,
                    "The leash is shorter than the attack range, so it can never reach a target.");
            }

            if (monster.Respawn.RespawnDelaySeconds < 0f)
            {
                report.AddError(ValidationCode.ValueOutOfRange, monster.Id,
                    "Respawn delay is negative.");
            }

            if (monster.LootTable.IsValid)
            {
                Require(lookup, monster.Id, monster.LootTable, "Loot table", report);
            }

            DefinitionId[] maps = monster.AllowedMaps;
            for (int i = 0; i < maps.Length; i++)
            {
                Require(lookup, monster.Id, maps[i], "Allowed map", report);
            }
        }

        // ---- drop tables ---------------------------------------------------------------

        private static void ValidateDropTable(DropTableDefinition table, IDefinitionLookup lookup,
            ValidationReport report)
        {
            DropEntry[] entries = table.Entries;

            if (entries.Length == 0)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, table.Id,
                    "The table has no entries, so nothing using it will ever drop anything.");
                return;
            }

            if (table.MaxEntries < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, table.Id,
                    "Maximum entries is negative.");
            }

            for (int i = 0; i < entries.Length; i++)
            {
                DropEntry entry = entries[i];

                if (!entry.Item.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, table.Id,
                        "Entry " + i + " names no item.");
                    continue;
                }

                Require(lookup, table.Id, entry.Item, "Entry " + i + " item", report);

                if (entry.MinQuantity <= 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, table.Id,
                        "Entry '" + entry.Item + "' drops " + entry.MinQuantity
                        + ", which is not a quantity.");
                }

                if (entry.Chance < 0f || entry.Chance > 1f)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, table.Id,
                        "Entry '" + entry.Item + "' has chance " + entry.Chance
                        + ", which is outside zero to one.");
                }

                if (entry.MinKillerLevel > 0 && entry.MaxKillerLevel > 0
                    && entry.MaxKillerLevel < entry.MinKillerLevel)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, table.Id,
                        "Entry '" + entry.Item + "' has a level band no killer can be inside.");
                }

                if (entry.RarityOverride.IsValid)
                {
                    Require(lookup, table.Id, entry.RarityOverride,
                        "Entry " + i + " rarity", report);
                }
            }
        }

        // ---- quests --------------------------------------------------------------------

        private static void ValidateQuest(QuestDefinition quest, IDefinitionLookup lookup,
            ValidationReport report)
        {
            QuestObjective[] objectives = quest.Objectives ?? new QuestObjective[0];

            if (objectives.Length == 0)
            {
                report.AddError(ValidationCode.InvalidConfiguration, quest.Id,
                    "The quest has no objectives, so it could never be completed.");
            }

            for (int i = 0; i < objectives.Length; i++)
            {
                QuestObjective objective = objectives[i];

                if (objective.Type == QuestObjectiveType.None)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, quest.Id,
                        "Objective " + i + " has no type.");
                }

                if (objective.RequiredAmount <= 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, quest.Id,
                        "Objective " + i + " requires " + objective.RequiredAmount
                        + ", which is already satisfied.");
                }

                // An untargeted objective is legal -- "kill ten of anything" -- so only a
                // named target that does not exist is a problem.
                if (objective.Target.IsValid)
                {
                    Require(lookup, quest.Id, objective.Target,
                        "Objective " + i + " target", report);
                }
            }

            QuestReward[] rewards = quest.Rewards ?? new QuestReward[0];

            for (int i = 0; i < rewards.Length; i++)
            {
                QuestReward reward = rewards[i];

                if (reward.Type == QuestRewardType.None)
                {
                    report.AddWarning(ValidationCode.InvalidConfiguration, quest.Id,
                        "Reward " + i + " has no type and will pay nothing.");
                    continue;
                }

                if (reward.Amount <= 0)
                {
                    report.AddError(ValidationCode.ValueOutOfRange, quest.Id,
                        "Reward " + i + " pays " + reward.Amount + ".");
                }

                bool needsTarget = reward.Type == QuestRewardType.Item
                    || reward.Type == QuestRewardType.Skill
                    || reward.Type == QuestRewardType.JobUnlock;

                if (needsTarget && !reward.Target.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, quest.Id,
                        "Reward " + i + " is a " + reward.Type + " but names nothing.");
                    continue;
                }

                if (reward.Target.IsValid)
                {
                    Require(lookup, quest.Id, reward.Target, "Reward " + i + " target", report);
                }
            }

            DefinitionId[] prerequisites = quest.PrerequisiteQuests ?? new DefinitionId[0];

            for (int i = 0; i < prerequisites.Length; i++)
            {
                if (!prerequisites[i].IsValid) continue;

                if (prerequisites[i] == quest.Id)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, quest.Id,
                        "The quest requires itself, so it could never be taken.");
                    continue;
                }

                Require(lookup, quest.Id, prerequisites[i], "Prerequisite", report);
            }

            if (quest.LevelRequirement < 0)
            {
                report.AddError(ValidationCode.ValueOutOfRange, quest.Id,
                    "Level requirement is negative.");
            }

            if (!quest.Repeatable && quest.RepeatCooldownSeconds > 0f)
            {
                report.AddWarning(ValidationCode.InvalidConfiguration, quest.Id,
                    "A repeat cooldown is authored, but the quest is not repeatable.");
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
