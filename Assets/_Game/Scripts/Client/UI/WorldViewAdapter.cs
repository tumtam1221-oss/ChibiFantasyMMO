using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns monsters, loot and quests into view data. The read half.
    /// </summary>
    /// <remarks>
    /// <b>It lives in Client for the reason the other adapters do.</b> The UI assembly does
    /// not reference Gameplay -- that boundary is what stops a panel advancing a quest or
    /// taking loot -- so something has to bridge them, and the Client is where both are
    /// already visible.
    ///
    /// <b>It copies out, it does not hand over.</b> Every output is a snapshot. No panel
    /// ever holds a <c>MonsterRuntimeState</c>, a <c>LootObjectState</c> or a
    /// <c>QuestProgress</c>.
    ///
    /// <b>It reads; it never decides.</b> A quest's status comes from gameplay rather than
    /// being recomputed from the counters here, because a second implementation of that
    /// rule would eventually disagree with the first.
    /// </remarks>
    public static class WorldViewAdapter
    {
        /// <summary>The registries these views need.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<MonsterDefinition> monsters = null,
                IDefinitionRegistry<QuestDefinition> quests = null,
                IDefinitionRegistry<NPCDefinition> npcs = null,
                IDefinitionRegistry<MapDefinition> maps = null)
            {
                Items = items;
                Monsters = monsters;
                Quests = quests;
                Npcs = npcs;
                Maps = maps;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<MonsterDefinition> Monsters { get; }

            public IDefinitionRegistry<QuestDefinition> Quests { get; }

            /// <summary>
            /// Where an NPC objective's name comes from.
            /// </summary>
            /// <remarks>Phase 10 could not resolve these because it was never given the
            /// registry, and quest state deliberately stores an id rather than a name.
            /// Supplying it here is the whole fix: nothing in Gameplay changed, and no name
            /// was copied into quest state.</remarks>
            public IDefinitionRegistry<NPCDefinition> Npcs { get; }

            /// <summary>Where a map objective's name comes from.</summary>
            public IDefinitionRegistry<MapDefinition> Maps { get; }
        }

        /// <summary>What a health bar should draw for a monster.</summary>
        public static MonsterHealthViewData BuildMonsterHealth(MonsterRuntimeState monster)
        {
            if (monster == null) return MonsterHealthViewData.None;

            MonsterDefinition definition = monster.Definition;

            return new MonsterHealthViewData(monster.InstanceId, monster.DefinitionId,
                definition.NameKey, monster.Level, monster.CurrentHealth, monster.MaxHealth,
                IsBoss(definition.Rank));
        }

        /// <summary>
        /// Whether a rank should be presented as a boss.
        /// </summary>
        /// <remarks>Presentation only. The rank is authored and nothing here decides what a
        /// boss <em>is</em>; this only decides which colour a bar gets.</remarks>
        private static bool IsBoss(MonsterRank rank)
        {
            return rank == MonsterRank.MiniBoss || rank == MonsterRank.Boss
                || rank == MonsterRank.WorldBoss;
        }

        /// <summary>Fills <paramref name="into"/> with one line per loot entry, taken ones included.</summary>
        /// <remarks>Taken entries are produced so the list keeps its shape: removing them
        /// would make it jump under a player's cursor mid-click.</remarks>
        public static void BuildLoot(LootObjectState loot, in Context context,
            List<LootEntryViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (loot == null || context.Items == null) return;

            for (int i = 0; i < loot.Count; i++)
            {
                LootResult entry = loot.Contents[i];

                ItemDefinition item;
                context.Items.TryGet(entry.Item, out item);

                into.Add(new LootEntryViewData(i, entry.Item,
                    item == null ? default : item.NameKey,
                    item == null ? AssetRef.None : item.Icon,
                    entry.Quantity, loot.IsTaken(i)));
            }
        }

        /// <summary>What a detail panel should draw for one quest.</summary>
        public static QuestViewData BuildQuest(CharacterQuestState state, DefinitionId questId,
            in Context context)
        {
            if (context.Quests == null) return QuestViewData.None;

            QuestDefinition quest;
            if (!questId.IsValid || !context.Quests.TryGet(questId, out quest) || quest == null)
                return QuestViewData.None;

            QuestStatus status = state == null ? QuestStatus.NotStarted : state.StatusOf(questId);

            QuestProgress progress = null;
            if (state != null) state.TryGet(questId, out progress);

            QuestObjective[] objectives = quest.Objectives ?? new QuestObjective[0];
            var objectiveViews = new QuestObjectiveViewData[objectives.Length];

            for (int i = 0; i < objectives.Length; i++)
            {
                QuestObjective objective = objectives[i];

                objectiveViews[i] = new QuestObjectiveViewData(objective.Type, objective.Target,
                    NameKeyOf(objective.Type, objective.Target, context),
                    progress == null ? 0 : progress.CountAt(i),
                    objective.RequiredAmount);
            }

            QuestReward[] rewards = quest.Rewards ?? new QuestReward[0];
            var rewardViews = new QuestRewardViewData[rewards.Length];

            for (int i = 0; i < rewards.Length; i++)
            {
                QuestReward reward = rewards[i];

                ItemDefinition item = null;
                if (reward.Type == QuestRewardType.Item && context.Items != null)
                {
                    context.Items.TryGet(reward.Target, out item);
                }

                rewardViews[i] = new QuestRewardViewData(reward.Type, reward.Target,
                    item == null ? default : item.NameKey,
                    item == null ? AssetRef.None : item.Icon,
                    reward.Amount);
            }

            return QuestViewData.From(questId, quest.NameKey, quest.DescriptionKey,
                quest.QuestType, Translate(status), quest.LevelRequirement, quest.Repeatable,
                objectiveViews, rewardViews);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every quest a character is carrying.
        /// </summary>
        /// <remarks>Completed quests are included: which of them belong in a tracker is the
        /// controller's decision, and an adapter that filtered would be making one.</remarks>
        public static void BuildQuestLog(CharacterQuestState state, in Context context,
            List<QuestViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (state == null || context.Quests == null) return;

            foreach (KeyValuePair<DefinitionId, QuestProgress> pair in state.All)
            {
                QuestViewData view = BuildQuest(state, pair.Key, context);
                if (view.IsValid) into.Add(view);
            }
        }

        /// <summary>
        /// The name key of whatever an objective points at.
        /// </summary>
        /// <remarks>
        /// Which registry to ask depends on the objective's type, and only the Client can
        /// ask -- a key is authored content, and a UI that built one from an id would
        /// invent a key nobody wrote. Unresolvable targets fall back to showing the id.
        /// </remarks>
        private static LocalizationKey NameKeyOf(QuestObjectiveType type, DefinitionId target,
            in Context context)
        {
            if (!target.IsValid) return default;

            switch (type)
            {
                case QuestObjectiveType.KillMonster:
                    if (context.Monsters == null) return default;

                    MonsterDefinition monster;
                    return context.Monsters.TryGet(target, out monster) && monster != null
                        ? monster.NameKey
                        : default;

                case QuestObjectiveType.CollectItem:
                case QuestObjectiveType.DeliverItem:
                    if (context.Items == null) return default;

                    ItemDefinition item;
                    return context.Items.TryGet(target, out item) && item != null
                        ? item.NameKey
                        : default;

                case QuestObjectiveType.TalkToNpc:
                    if (context.Npcs == null) return default;

                    NPCDefinition npc;
                    return context.Npcs.TryGet(target, out npc) && npc != null
                        ? npc.NameKey
                        : default;

                case QuestObjectiveType.ReachMap:
                    if (context.Maps == null) return default;

                    MapDefinition map;
                    return context.Maps.TryGet(target, out map) && map != null
                        ? map.NameKey
                        : default;

                default:
                    // A level objective names no content, so there is nothing to resolve.
                    // Showing the id remains the honest fallback for anything unresolvable.
                    return default;
            }
        }

        /// <summary>
        /// Translates the gameplay status into the UI's mirror of it.
        /// </summary>
        /// <remarks>The mirror exists because the UI assembly cannot see Gameplay. Doing it
        /// in one place means a panel never has to know there are two enums.</remarks>
        private static QuestStatusView Translate(QuestStatus status)
        {
            switch (status)
            {
                case QuestStatus.Active: return QuestStatusView.Active;
                case QuestStatus.ReadyToComplete: return QuestStatusView.ReadyToComplete;
                case QuestStatus.Completed: return QuestStatusView.Completed;
                default: return QuestStatusView.NotStarted;
            }
        }
    }
}
