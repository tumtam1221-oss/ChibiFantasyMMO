using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>One objective line, as a tracker draws it.</summary>
    /// <remarks>A copied snapshot. The panel holding it cannot advance a counter, because
    /// there is no counter here to advance.</remarks>
    public readonly struct QuestObjectiveViewData
    {
        public QuestObjectiveViewData(QuestObjectiveType type, DefinitionId target,
            LocalizationKey targetNameKey, int current, int required)
        {
            Type = type;
            Target = target;
            TargetNameKey = targetNameKey;
            Current = current;
            Required = required;
        }

        public QuestObjectiveType Type { get; }

        public DefinitionId Target { get; }

        /// <summary>
        /// The target's own name key, resolved by the Client.
        /// </summary>
        /// <remarks>Carried rather than derived: a key is authored content, and a UI that
        /// built one from an id would invent a key nobody wrote. Invalid for an objective
        /// with no specific target -- "kill ten of anything".</remarks>
        public LocalizationKey TargetNameKey { get; }

        public int Current { get; }

        public int Required { get; }

        public bool IsComplete => Required <= 0 || Current >= Required;

        public override string ToString()
        {
            return Type + " " + Current + "/" + Required;
        }
    }

    /// <summary>One reward line.</summary>
    public readonly struct QuestRewardViewData
    {
        public QuestRewardViewData(QuestRewardType type, DefinitionId target,
            LocalizationKey targetNameKey, AssetRef icon, int amount)
        {
            Type = type;
            Target = target;
            TargetNameKey = targetNameKey;
            Icon = icon;
            Amount = amount;
        }

        public QuestRewardType Type { get; }

        public DefinitionId Target { get; }

        public LocalizationKey TargetNameKey { get; }

        public AssetRef Icon { get; }

        public int Amount { get; }

        public override string ToString()
        {
            return Type + " " + Amount;
        }
    }

    /// <summary>
    /// One quest, as the tracker and the detail panel draw it.
    /// </summary>
    /// <remarks>
    /// <b>A snapshot, like every other view type here.</b> It holds no
    /// <c>CharacterQuestState</c> and no <c>QuestProgress</c>, so a panel cannot advance a
    /// quest or complete one by writing to what it was handed.
    ///
    /// <b>Status is carried, not derived.</b> Whether a quest is ready to hand in is
    /// <c>QuestService</c>'s answer; recomputing it here from the counters would be a
    /// second implementation of the same rule, and the two would eventually disagree.
    /// </remarks>
    public readonly struct QuestViewData
    {
        private static readonly QuestObjectiveViewData[] NoObjectives =
            new QuestObjectiveViewData[0];

        private static readonly QuestRewardViewData[] NoRewards = new QuestRewardViewData[0];

        private readonly QuestObjectiveViewData[] _objectives;
        private readonly QuestRewardViewData[] _rewards;

        private QuestViewData(bool valid, DefinitionId questId, LocalizationKey nameKey,
            LocalizationKey descriptionKey, QuestType questType, QuestStatusView status,
            int levelRequirement, bool repeatable,
            QuestObjectiveViewData[] objectives, QuestRewardViewData[] rewards)
        {
            IsValid = valid;
            QuestId = questId;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            QuestType = questType;
            Status = status;
            LevelRequirement = levelRequirement;
            Repeatable = repeatable;
            _objectives = objectives ?? NoObjectives;
            _rewards = rewards ?? NoRewards;
        }

        public bool IsValid { get; }

        public DefinitionId QuestId { get; }

        public LocalizationKey NameKey { get; }

        public LocalizationKey DescriptionKey { get; }

        public QuestType QuestType { get; }

        /// <summary>Where it stands, as gameplay reported it.</summary>
        public QuestStatusView Status { get; }

        public int LevelRequirement { get; }

        public bool Repeatable { get; }

        public IReadOnlyList<QuestObjectiveViewData> Objectives => _objectives;

        public IReadOnlyList<QuestRewardViewData> Rewards => _rewards;

        public bool IsReadyToComplete => Status == QuestStatusView.ReadyToComplete;

        public bool IsCompleted => Status == QuestStatusView.Completed;

        /// <summary>Nothing to show.</summary>
        public static QuestViewData None => default;

        public static QuestViewData From(DefinitionId questId, LocalizationKey nameKey,
            LocalizationKey descriptionKey, QuestType questType, QuestStatusView status,
            int levelRequirement, bool repeatable,
            QuestObjectiveViewData[] objectives, QuestRewardViewData[] rewards)
        {
            return new QuestViewData(true, questId, nameKey, descriptionKey, questType, status,
                levelRequirement, repeatable, objectives, rewards);
        }

        public override string ToString()
        {
            return IsValid ? QuestId + " (" + Status + ")" : "no quest";
        }
    }

    /// <summary>
    /// A quest's state, as the UI sees it.
    /// </summary>
    /// <remarks>
    /// A mirror of the Gameplay enum rather than a reference to it: the UI assembly does
    /// not reference Gameplay, and that boundary is what stops a panel reaching into a
    /// quest. The Client translates, which is the same shape every other view type here
    /// already uses.
    /// </remarks>
    public enum QuestStatusView
    {
        NotStarted = 0,
        Active = 1,
        ReadyToComplete = 2,
        Completed = 3
    }

    /// <summary>What a monster's health bar needs.</summary>
    /// <remarks>Two numbers and a name. Deliberately not the monster: a bar that held one
    /// could damage it.</remarks>
    public readonly struct MonsterHealthViewData
    {
        public MonsterHealthViewData(InstanceId instanceId, DefinitionId definitionId,
            LocalizationKey nameKey, int level, int currentHealth, int maxHealth, bool isBoss)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            NameKey = nameKey;
            Level = level;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            IsBoss = isBoss;
        }

        public InstanceId InstanceId { get; }

        public DefinitionId DefinitionId { get; }

        public LocalizationKey NameKey { get; }

        public int Level { get; }

        public int CurrentHealth { get; }

        public int MaxHealth { get; }

        /// <summary>Drives presentation only. The rank is authored; nothing here decides it.</summary>
        public bool IsBoss { get; }

        public bool IsValid => InstanceId.IsValid && MaxHealth > 0;

        public bool IsAlive => CurrentHealth > 0;

        /// <summary>Zero to one. Zero when there is no ceiling, rather than dividing by it.</summary>
        public float Fraction => MaxHealth <= 0
            ? 0f
            : (float)CurrentHealth / MaxHealth;

        public static MonsterHealthViewData None => default;

        public override string ToString()
        {
            return IsValid ? DefinitionId + " " + CurrentHealth + "/" + MaxHealth : "no monster";
        }
    }

    /// <summary>One line of a loot pile.</summary>
    public readonly struct LootEntryViewData
    {
        public LootEntryViewData(int index, DefinitionId item, LocalizationKey nameKey,
            AssetRef icon, int quantity, bool taken)
        {
            Index = index;
            Item = item;
            NameKey = nameKey;
            Icon = icon;
            Quantity = quantity;
            IsTaken = taken;
        }

        /// <summary>Position in the pile. What a pickup command names.</summary>
        public int Index { get; }

        public DefinitionId Item { get; }

        public LocalizationKey NameKey { get; }

        public AssetRef Icon { get; }

        public int Quantity { get; }

        /// <summary>Already gone. Shown greyed rather than removed, so the list does not jump.</summary>
        public bool IsTaken { get; }

        public bool ShowQuantity => Quantity > 1;

        public override string ToString()
        {
            return Item + " x" + Quantity + (IsTaken ? " (taken)" : string.Empty);
        }
    }
}
