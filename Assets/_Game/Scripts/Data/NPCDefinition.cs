using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Something an NPC can do for a player.
    /// </summary>
    /// <remarks>
    /// Closed technical category: each value opens a different screen and is validated
    /// differently. One enum rather than an NPC class per role, so a shopkeeper who also
    /// gives quests is one definition with two capabilities rather than a type that has to
    /// exist for the combination.
    ///
    /// Not stored on the definition -- see <see cref="NPCDefinition.HasRole"/>, which
    /// derives it from the capability flags Phase 04 already authored.
    /// </remarks>
    public enum NpcRole
    {
        /// <summary>Talking, and nothing more. Every NPC has this.</summary>
        Generic = 0,

        /// <summary>Offers and accepts quests.</summary>
        Quest = 1,

        /// <summary>Sells and buys. A system vendor, never a player shop.</summary>
        Shop = 2,

        /// <summary>Opens the character's storage.</summary>
        Storage = 3,

        /// <summary>Changes a character's class or job.</summary>
        JobChange = 4,

        /// <summary>Offers travel to somewhere else.</summary>
        Warp = 5
    }

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

        [Header("Placement")]
        [Tooltip("Map this NPC stands on. Invalid means it is not placed anywhere.")]
        [SerializeField] private DefinitionId _map;

        [Tooltip("Where on that map. Should be an Npc spawn point.")]
        [SerializeField] private DefinitionId _spawnPoint;

        [Tooltip("Whether the NPC may be interacted with at all.")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("How close a player must stand. Zero or less falls back to the default.")]
        [SerializeField] private float _interactionRadius = 3f;

        [Header("Role content")]
        [Tooltip("Quests this NPC offers or accepts.")]
        [SerializeField] private DefinitionId[] _quests = new DefinitionId[0];

        [Tooltip("Classes this NPC can change a character into.")]
        [SerializeField] private DefinitionId[] _classesOffered = new DefinitionId[0];

        [Tooltip("Jobs this NPC can change a character into.")]
        [SerializeField] private DefinitionId[] _jobsOffered = new DefinitionId[0];

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

        /// <summary>Reference to the <see cref="MapDefinition"/> this NPC stands on.</summary>
        public DefinitionId Map => _map;

        /// <summary>Reference to a <see cref="SpawnPointDefinition"/>.</summary>
        public DefinitionId SpawnPoint => _spawnPoint;

        /// <summary>
        /// Whether the NPC may be interacted with.
        /// </summary>
        /// <remarks>Content's switch for one that exists but is unavailable -- an event
        /// vendor out of season. Disabled is a refusal, not an absence, so a UI can still
        /// draw it greyed.</remarks>
        public bool Enabled => _enabled;

        /// <summary>Zero or less means the caller's default applies.</summary>
        public float InteractionRadius => _interactionRadius;

        /// <summary>References to <see cref="QuestDefinition"/>. Never null.</summary>
        public DefinitionId[] Quests => _quests ?? NoIds;

        /// <summary>References to <see cref="ClassDefinition"/>. Never null.</summary>
        public DefinitionId[] ClassesOffered => _classesOffered ?? NoIds;

        /// <summary>References to <see cref="JobDefinition"/>. Never null.</summary>
        public DefinitionId[] JobsOffered => _jobsOffered ?? NoIds;

        /// <summary>
        /// Whether this NPC offers a role.
        /// </summary>
        /// <remarks>
        /// <b>Derived, never stored.</b> The capability flags Phase 04 authored already
        /// <em>are</em> the role model; adding a parallel <c>Roles</c> array would be a
        /// second source of the same truth, and the two would drift the first time one was
        /// edited without the other. Asking here means there is exactly one answer.
        ///
        /// <see cref="NpcRole.Shop"/> is the one role with a content requirement rather than
        /// a flag: an NPC is a vendor because it references a <see cref="ShopDefinition"/>,
        /// since a merchant with no stock list has nothing to open.
        /// </remarks>
        public bool HasRole(NpcRole role)
        {
            switch (role)
            {
                case NpcRole.Generic:
                    return true;
                case NpcRole.Quest:
                    return _isQuestGiver;
                case NpcRole.Shop:
                    return _shop.IsValid;
                case NpcRole.Storage:
                    return _providesStorage;
                case NpcRole.JobChange:
                    return _isJobChanger;
                case NpcRole.Warp:
                    return _providesWarp;
                default:
                    return false;
            }
        }

        private static readonly DefinitionId[] NoIds = new DefinitionId[0];
    }
}
