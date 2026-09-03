using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a quest operation was refused.</summary>
    public enum QuestRejection
    {
        None = 0,

        /// <summary>No state or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such quest could be resolved.</summary>
        UnknownQuest = 2,

        /// <summary>It is already taken.</summary>
        AlreadyActive = 3,

        /// <summary>It is finished and not repeatable.</summary>
        AlreadyCompleted = 4,

        /// <summary>The character is not high enough level.</summary>
        LevelTooLow = 5,

        /// <summary>A prerequisite quest has not been completed.</summary>
        PrerequisiteNotMet = 6,

        /// <summary>The quest is not taken, so there is nothing to advance or turn in.</summary>
        NotActive = 7,

        /// <summary>Objectives are outstanding.</summary>
        ObjectivesIncomplete = 8,

        /// <summary>The quest authors no objectives, so it could never be finished.</summary>
        NoObjectives = 9,

        /// <summary>A reward names something that does not resolve.</summary>
        InvalidReward = 10,

        /// <summary>There is not enough room for the item rewards.</summary>
        InventoryFull = 11
    }

    /// <summary>What a quest operation did.</summary>
    public readonly struct QuestResult
    {
        private static readonly QuestReward[] NoRewards = new QuestReward[0];

        private readonly QuestReward[] _granted;

        private QuestResult(bool accepted, QuestRejection reason, DefinitionId questId,
            QuestStatus status, int experience, int currency, QuestReward[] granted)
        {
            IsAccepted = accepted;
            Reason = reason;
            QuestId = questId;
            Status = status;
            ExperienceGranted = experience;
            CurrencyGranted = currency;
            _granted = granted ?? NoRewards;
        }

        public bool IsAccepted { get; }

        public QuestRejection Reason { get; }

        public DefinitionId QuestId { get; }

        /// <summary>Where the quest stands after the operation.</summary>
        public QuestStatus Status { get; }

        /// <summary>
        /// Experience owed by a turn-in.
        /// </summary>
        /// <remarks>Reported rather than granted: no character progression system is touched
        /// here, for the same reason a monster's defeat reports its experience instead of
        /// awarding it. The caller decides who receives it.</remarks>
        public int ExperienceGranted { get; }

        public int CurrencyGranted { get; }

        /// <summary>Everything the turn-in paid, item rewards included.</summary>
        public IReadOnlyList<QuestReward> GrantedRewards => _granted;

        public static QuestResult Accepted(DefinitionId questId, QuestStatus status,
            int experience = 0, int currency = 0, QuestReward[] granted = null)
        {
            return new QuestResult(true, QuestRejection.None, questId, status, experience,
                currency, granted);
        }

        public static QuestResult Rejected(QuestRejection reason, DefinitionId questId = default)
        {
            return new QuestResult(false, reason, questId, QuestStatus.NotStarted, 0, 0, null);
        }

        public override string ToString()
        {
            return IsAccepted ? QuestId + " -> " + Status : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Accepting quests, advancing them and turning them in.
    /// </summary>
    /// <remarks>
    /// <b>One data-driven objective model.</b> There is no kill-quest class and no
    /// collect-quest class. An objective is a <see cref="QuestObjectiveType"/>, a target
    /// and a required amount; progress is a counter. Adding an objective type is a switch
    /// arm, not a hierarchy, and no <see cref="DefinitionId"/> is compared to a literal
    /// anywhere here.
    ///
    /// <b>Progress is reported, never polled.</b> A kill, a pickup or an NPC conversation
    /// calls in with what happened. Nothing scans an inventory or a monster list, which is
    /// what keeps quest tracking off the frame budget entirely.
    ///
    /// <b>Turn-in pays exactly once.</b> The status moves to
    /// <see cref="QuestStatus.Completed"/> inside the same call that grants, and a completed
    /// quest is refused. A UI that refreshes a hundred times cannot pay a hundred times.
    ///
    /// <b>Reward capacity is checked first.</b> Room for every item reward is confirmed
    /// before anything is granted and before the status moves, so a full bag is a refusal
    /// rather than a quest completed for nothing. This is not a transaction and is not
    /// claimed to be -- see the remarks on <see cref="TryTurnIn"/>.
    /// </remarks>
    public static class QuestService
    {
        /// <summary>Everything a quest operation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<QuestDefinition> quests,
                IDefinitionRegistry<ItemDefinition> items = null,
                int characterLevel = 1, OwnerId owner = default)
            {
                Quests = quests;
                Items = items;
                CharacterLevel = characterLevel;
                Owner = owner;
            }

            public IDefinitionRegistry<QuestDefinition> Quests { get; }

            /// <summary>Needed only to pay item rewards.</summary>
            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public int CharacterLevel { get; }

            /// <summary>Stamped on reward items, so they arrive already owned.</summary>
            public OwnerId Owner { get; }

            public bool IsUsable => Quests != null;
        }

        // ---- accepting -----------------------------------------------------------------

        /// <summary>Takes a quest, if the character may.</summary>
        public static QuestResult TryAccept(CharacterQuestState state, DefinitionId questId,
            in Context context)
        {
            if (state == null || !context.IsUsable)
                return QuestResult.Rejected(QuestRejection.MissingContext, questId);

            QuestDefinition quest;
            if (!questId.IsValid || !context.Quests.TryGet(questId, out quest) || quest == null)
                return QuestResult.Rejected(QuestRejection.UnknownQuest, questId);

            QuestStatus status = state.StatusOf(questId);

            if (status == QuestStatus.Active || status == QuestStatus.ReadyToComplete)
                return QuestResult.Rejected(QuestRejection.AlreadyActive, questId);

            if (status == QuestStatus.Completed && !quest.Repeatable)
                return QuestResult.Rejected(QuestRejection.AlreadyCompleted, questId);

            if (quest.LevelRequirement > 0 && context.CharacterLevel < quest.LevelRequirement)
                return QuestResult.Rejected(QuestRejection.LevelTooLow, questId);

            DefinitionId[] prerequisites = quest.PrerequisiteQuests;

            if (prerequisites != null)
            {
                for (int i = 0; i < prerequisites.Length; i++)
                {
                    if (!prerequisites[i].IsValid) continue;
                    if (state.IsCompleted(prerequisites[i])) continue;

                    return QuestResult.Rejected(QuestRejection.PrerequisiteNotMet, questId);
                }
            }

            QuestObjective[] objectives = quest.Objectives;

            if (objectives == null || objectives.Length == 0)
            {
                // A quest with nothing to do could never be finished, so taking it would
                // leave it stuck in the log for good.
                return QuestResult.Rejected(QuestRejection.NoObjectives, questId);
            }

            state.Begin(questId, objectives.Length);
            Reevaluate(state, quest, questId);

            return QuestResult.Accepted(questId, state.StatusOf(questId));
        }

        /// <summary>Abandons an active quest, losing its progress.</summary>
        public static QuestResult TryAbandon(CharacterQuestState state, DefinitionId questId,
            in Context context)
        {
            if (state == null || !context.IsUsable)
                return QuestResult.Rejected(QuestRejection.MissingContext, questId);

            QuestProgress progress;
            if (!state.TryGet(questId, out progress) || !state.IsActive(questId))
                return QuestResult.Rejected(QuestRejection.NotActive, questId);

            progress.Status = QuestStatus.NotStarted;
            progress.Reset();
            progress.Status = QuestStatus.NotStarted;
            state.Touch();

            return QuestResult.Accepted(questId, QuestStatus.NotStarted);
        }

        // ---- progress ------------------------------------------------------------------

        /// <summary>
        /// Reports something that happened, and advances whatever it satisfies.
        /// </summary>
        /// <remarks>
        /// The one entry point for progress. A kill calls in with
        /// <see cref="QuestObjectiveType.KillMonster"/> and a monster id; a pickup with
        /// <see cref="QuestObjectiveType.CollectItem"/>; an NPC with
        /// <see cref="QuestObjectiveType.TalkToNpc"/>. Every active quest is checked, so one
        /// kill can advance three quests at once, and nothing is polled to find out.
        /// </remarks>
        /// <returns>How many objectives across all quests were advanced.</returns>
        public static int ReportProgress(CharacterQuestState state, QuestObjectiveType type,
            DefinitionId target, int amount, in Context context)
        {
            if (state == null || !context.IsUsable) return 0;
            if (type == QuestObjectiveType.None || amount <= 0) return 0;

            int advanced = 0;

            foreach (KeyValuePair<DefinitionId, QuestProgress> pair in state.All)
            {
                QuestProgress progress = pair.Value;
                if (progress.Status != QuestStatus.Active) continue;

                QuestDefinition quest;
                if (!context.Quests.TryGet(pair.Key, out quest) || quest == null) continue;

                QuestObjective[] objectives = quest.Objectives;
                if (objectives == null) continue;

                bool moved = false;

                for (int i = 0; i < objectives.Length && i < progress.ObjectiveCount; i++)
                {
                    QuestObjective objective = objectives[i];

                    if (objective.Type != type) continue;

                    // An objective with no target matches anything of its type, which is how
                    // "kill ten of anything" is authored.
                    if (objective.Target.IsValid && objective.Target != target) continue;

                    if (progress.Advance(i, amount, objective.RequiredAmount) <= 0) continue;

                    moved = true;
                    advanced++;
                }

                if (!moved) continue;

                Reevaluate(state, quest, pair.Key);
                state.Touch();
            }

            return advanced;
        }

        /// <summary>
        /// Recomputes whether a quest is ready to hand in.
        /// </summary>
        /// <remarks>Derived from the counters every time rather than tracked separately, so
        /// the status cannot drift from the progress it describes.</remarks>
        private static void Reevaluate(CharacterQuestState state, QuestDefinition quest,
            DefinitionId questId)
        {
            QuestProgress progress;
            if (!state.TryGet(questId, out progress)) return;
            if (progress.Status == QuestStatus.Completed) return;

            QuestObjective[] objectives = quest.Objectives;

            for (int i = 0; i < objectives.Length; i++)
            {
                int required = objectives[i].RequiredAmount;
                if (required <= 0) continue;

                if (progress.CountAt(i) < required)
                {
                    progress.Status = QuestStatus.Active;
                    return;
                }
            }

            progress.Status = QuestStatus.ReadyToComplete;
        }

        // ---- turn-in -------------------------------------------------------------------

        /// <summary>
        /// Hands a finished quest in and pays it out.
        /// </summary>
        /// <remarks>
        /// <b>Exactly once.</b> The status moves to <see cref="QuestStatus.Completed"/> in
        /// the same call that grants, and a completed quest is refused, so a UI refreshing
        /// repeatedly cannot pay repeatedly.
        ///
        /// <b>Capacity first.</b> Room for every item reward is confirmed before anything is
        /// created and before the status moves. A full bag refuses the turn-in and leaves
        /// the quest ready, rather than completing it for nothing.
        ///
        /// <b>Not a transaction.</b> The architecture has no rollback. The design goal is
        /// that nothing after the capacity check can fail: room was verified against the
        /// same container the items go into, and experience and currency are reported rather
        /// than written. If a container were mutated concurrently between the check and the
        /// grant, a reward could still fail to fit -- that is the exact limitation, stated
        /// rather than papered over.
        /// </remarks>
        public static QuestResult TryTurnIn(CharacterQuestState state, DefinitionId questId,
            ItemContainerState inventory, in Context context)
        {
            if (state == null || !context.IsUsable)
                return QuestResult.Rejected(QuestRejection.MissingContext, questId);

            QuestDefinition quest;
            if (!questId.IsValid || !context.Quests.TryGet(questId, out quest) || quest == null)
                return QuestResult.Rejected(QuestRejection.UnknownQuest, questId);

            QuestStatus status = state.StatusOf(questId);

            if (status == QuestStatus.Completed)
                return QuestResult.Rejected(QuestRejection.AlreadyCompleted, questId);

            if (status == QuestStatus.NotStarted)
                return QuestResult.Rejected(QuestRejection.NotActive, questId);

            if (status != QuestStatus.ReadyToComplete)
                return QuestResult.Rejected(QuestRejection.ObjectivesIncomplete, questId);

            QuestReward[] rewards = quest.Rewards ?? new QuestReward[0];

            // ---- validate every reward before granting any of them ---------------------

            int itemRewards = 0;

            for (int i = 0; i < rewards.Length; i++)
            {
                QuestReward reward = rewards[i];

                if (reward.Type != QuestRewardType.Item) continue;

                if (!reward.Target.IsValid || reward.Amount <= 0)
                    return QuestResult.Rejected(QuestRejection.InvalidReward, questId);

                if (context.Items == null || inventory == null)
                    return QuestResult.Rejected(QuestRejection.MissingContext, questId);

                ItemDefinition item;
                if (!context.Items.TryGet(reward.Target, out item) || item == null)
                    return QuestResult.Rejected(QuestRejection.InvalidReward, questId);

                itemRewards++;
            }

            if (itemRewards > 0 && !HasRoomForAll(rewards, inventory, context))
                return QuestResult.Rejected(QuestRejection.InventoryFull, questId);

            // ---- grant -----------------------------------------------------------------

            int experience = 0;
            int currency = 0;

            for (int i = 0; i < rewards.Length; i++)
            {
                QuestReward reward = rewards[i];

                switch (reward.Type)
                {
                    case QuestRewardType.Experience:
                        experience += reward.Amount;
                        break;

                    case QuestRewardType.Currency:
                        currency += reward.Amount;
                        break;

                    case QuestRewardType.Item:
                        inventory.Add(new ItemInstance(InstanceId.New(), reward.Target,
                            context.Owner, reward.Amount), context.Items);
                        break;

                    // Skill and job unlocks are reported in GrantedRewards for the caller
                    // to act on: granting a skill would reach into progression, which is
                    // not this service's business.
                }
            }

            QuestProgress progress;
            if (state.TryGet(questId, out progress)) progress.Status = QuestStatus.Completed;
            state.Touch();

            return QuestResult.Accepted(questId, QuestStatus.Completed, experience, currency,
                rewards);
        }

        /// <summary>
        /// Whether every item reward will fit.
        /// </summary>
        /// <remarks>
        /// Checked against the container as it is, counting the free slots each reward would
        /// need. Simulated rather than attempted, because attempting is the mistake: by then
        /// the quest is completed and a reward has nowhere to go.
        /// </remarks>
        private static bool HasRoomForAll(QuestReward[] rewards, ItemContainerState inventory,
            in Context context)
        {
            int freeSlots = inventory.FreeSlots;

            for (int i = 0; i < rewards.Length; i++)
            {
                if (rewards[i].Type != QuestRewardType.Item) continue;

                int room = inventory.RoomFor(rewards[i].Target, context.Items);

                if (room >= rewards[i].Amount) continue;

                ItemDefinition item;
                context.Items.TryGet(rewards[i].Target, out item);

                int perSlot = item != null && item.Stackable && item.MaxStackSize > 0
                    ? item.MaxStackSize
                    : 1;

                int shortfall = rewards[i].Amount - room;
                int slotsNeeded = (shortfall + perSlot - 1) / perSlot;

                if (slotsNeeded > freeSlots) return false;

                freeSlots -= slotsNeeded;
            }

            return true;
        }
    }
}
