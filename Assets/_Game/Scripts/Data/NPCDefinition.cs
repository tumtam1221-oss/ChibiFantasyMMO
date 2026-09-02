using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Broad role of an NPC.</summary>
    public enum NPCCategory
    {
        Generic = 0,
        Merchant = 1,
        QuestGiver = 2,
        JobChanger = 3,
        StorageKeeper = 4,
        WarpMaster = 5,
        Guard = 6,
        Trainer = 7
    }

    /// <summary>
    /// What an NPC is: static placement-independent content.
    /// </summary>
    /// <remarks>
    /// Capability flags declare what an NPC may offer; they do not implement it. Dialogue
    /// trees, shop stock, storage contents and warp execution are later systems.
    /// </remarks>
    public sealed class NPCDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private NPCCategory _category = NPCCategory.Generic;
        [SerializeField] private AssetRef _model;

        [SerializeField] private DefinitionId _dialogue;
        [SerializeField] private DefinitionId _shop;

        [SerializeField] private bool _isQuestGiver;
        [SerializeField] private bool _isJobChanger;
        [SerializeField] private bool _providesStorage;
        [SerializeField] private bool _providesWarp;

        public LocalizationKey NameKey => _nameKey;

        public NPCCategory Category => _category;

        public AssetRef Model => _model;

        /// <summary>Reference to a dialogue definition.</summary>
        public DefinitionId Dialogue => _dialogue;

        /// <summary>Reference to a shop definition, where this NPC trades.</summary>
        public DefinitionId Shop => _shop;

        public bool IsQuestGiver => _isQuestGiver;

        public bool IsJobChanger => _isJobChanger;

        public bool ProvidesStorage => _providesStorage;

        public bool ProvidesWarp => _providesWarp;
    }
}
