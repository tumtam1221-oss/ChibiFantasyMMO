using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad kind of quest, driving availability and UI grouping.</summary>
    public enum QuestType
    {
        Normal = 0,
        Main = 1,
        Daily = 2,
        Weekly = 3,
        Event = 4,
        JobChange = 5
    }

    /// <summary>What a player must do to satisfy one objective.</summary>
    public enum QuestObjectiveType
    {
        None = 0,
        KillMonster = 1,
        CollectItem = 2,
        TalkToNpc = 3,
        ReachMap = 4,
        ReachLevel = 5,
        DeliverItem = 6
    }

    /// <summary>What a quest pays out.</summary>
    public enum QuestRewardType
    {
        None = 0,
        Item = 1,
        Experience = 2,
        Currency = 3,
        Skill = 4,
        JobUnlock = 5
    }

    /// <summary>One authored objective. Progress toward it is runtime state.</summary>
    [Serializable]
    public struct QuestObjective
    {
        [SerializeField] private QuestObjectiveType _type;
        [SerializeField] private DefinitionId _target;
        [SerializeField] private int _requiredAmount;

        public QuestObjective(QuestObjectiveType type, DefinitionId target, int requiredAmount)
        {
            _type = type;
            _target = target;
            _requiredAmount = requiredAmount;
        }

        public QuestObjectiveType Type => _type;

        /// <summary>Monster, item, NPC or map referenced, according to Type.</summary>
        public DefinitionId Target => _target;

        public int RequiredAmount => _requiredAmount;
    }

    /// <summary>One authored reward.</summary>
    [Serializable]
    public struct QuestReward
    {
        [SerializeField] private QuestRewardType _type;
        [SerializeField] private DefinitionId _target;
        [SerializeField] private int _amount;

        public QuestReward(QuestRewardType type, DefinitionId target, int amount)
        {
            _type = type;
            _target = target;
            _amount = amount;
        }

        public QuestRewardType Type => _type;

        /// <summary>Item, skill or job granted. Unused for pure experience or currency.</summary>
        public DefinitionId Target => _target;

        public int Amount => _amount;
    }

    /// <summary>
    /// What a quest is: its authored objectives and rewards.
    /// </summary>
    /// <remarks>
    /// Accepted quests, objective counters and completion history are per-player runtime
    /// state and ultimately server-authoritative. None of that appears here.
    /// </remarks>
    public sealed class QuestDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private QuestType _questType = QuestType.Normal;

        [SerializeField] private int _levelRequirement;
        [SerializeField] private DefinitionId[] _prerequisiteQuests = new DefinitionId[0];

        [SerializeField] private QuestObjective[] _objectives = new QuestObjective[0];
        [SerializeField] private QuestReward[] _rewards = new QuestReward[0];

        [SerializeField] private bool _repeatable;
        [SerializeField] private float _repeatCooldownSeconds;
        [SerializeField] private float _expirationSeconds;

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public QuestType QuestType => _questType;

        public int LevelRequirement => _levelRequirement;

        public DefinitionId[] PrerequisiteQuests => _prerequisiteQuests;

        public QuestObjective[] Objectives => _objectives;

        public QuestReward[] Rewards => _rewards;

        public bool Repeatable => _repeatable;

        /// <summary>Delay before a repeatable quest may be taken again.</summary>
        public float RepeatCooldownSeconds => _repeatCooldownSeconds;

        /// <summary>Time limit once accepted. Zero or less means no limit.</summary>
        public float ExpirationSeconds => _expirationSeconds;
    }
}
