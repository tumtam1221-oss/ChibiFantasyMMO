using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// What a job is: one node in a class's advancement tree.
    /// </summary>
    /// <remarks>
    /// Jobs form an arbitrary directed graph rather than a fixed three-tier ladder.
    /// <see cref="Tier"/> is an authored ordinal, <see cref="PrerequisiteJob"/> points back
    /// toward the previous node and <see cref="NextJobs"/> points forward to any number of
    /// successors. A branch is simply a node with more than one successor.
    ///
    /// The intended Base to First to Branch A/B to Third progression is therefore content,
    /// and a future class may use a deeper, shallower or differently shaped tree without
    /// touching this schema.
    /// </remarks>
    public sealed class JobDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private DefinitionId _baseClass;
        [SerializeField] private int _tier = 1;
        [SerializeField] private int _levelRequirement;
        [SerializeField] private DefinitionId _prerequisiteJob;

        [SerializeField] private EquipmentSubtype[] _allowedEquipmentSubtypes = new EquipmentSubtype[0];

        [SerializeField] private DefinitionId[] _skills = new DefinitionId[0];
        [SerializeField] private StatModifier[] _statModifiers = new StatModifier[0];

        [SerializeField] private DefinitionId[] _nextJobs = new DefinitionId[0];

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        /// <summary>Reference to the <see cref="ClassDefinition"/> this job descends from.</summary>
        public DefinitionId BaseClass => _baseClass;

        /// <summary>Authored depth in the advancement tree. Not assumed to stop at three.</summary>
        public int Tier => _tier;

        /// <summary>Character level required to take this job.</summary>
        public int LevelRequirement => _levelRequirement;

        /// <summary>Reference to the <see cref="JobDefinition"/> that must precede this one.
        /// None for a job taken directly from a base class.</summary>
        public DefinitionId PrerequisiteJob => _prerequisiteJob;

        /// <summary>Equipment subtypes this job may wield. Empty means unrestricted.</summary>
        public EquipmentSubtype[] AllowedEquipmentSubtypes => _allowedEquipmentSubtypes;

        /// <summary>References to <see cref="SkillDefinition"/> unlocked by this job.</summary>
        public DefinitionId[] Skills => _skills;

        public StatModifier[] StatModifiers => _statModifiers;

        /// <summary>References to successor <see cref="JobDefinition"/>. Multiple entries form a branch.</summary>
        public DefinitionId[] NextJobs => _nextJobs;
    }
}
