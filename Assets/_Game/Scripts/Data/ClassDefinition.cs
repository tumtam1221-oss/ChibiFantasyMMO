using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>Which character genders may select a class.</summary>
    public enum GenderAvailability
    {
        Any = 0,
        MaleOnly = 1,
        FemaleOnly = 2
    }

    /// <summary>
    /// What a base class is.
    /// </summary>
    /// <remarks>
    /// No class is hard-coded anywhere in code. Swordsman, Cleric, Mage and Archer are
    /// content entries authored as assets of this type, so new base classes can ship
    /// without a code change.
    ///
    /// Progression is expressed generically: <see cref="JobChangeLevel"/> and
    /// <see cref="NextJobs"/> describe this class's own advancement, so a class may branch
    /// into any number of jobs at any level. The intended 15/35/60 tiering is authored
    /// data, not a rule baked into the schema, and other classes may use entirely
    /// different trees.
    /// </remarks>
    public sealed class ClassDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private LocalizationKey _descriptionKey;
        [SerializeField] private AssetRef _icon;

        [SerializeField] private GenderAvailability _genderAvailability = GenderAvailability.Any;

        [SerializeField] private StatValue[] _baseStats = new StatValue[0];
        [SerializeField] private StatModifier[] _statModifiers = new StatModifier[0];

        [SerializeField] private DefinitionId[] _startingEquipment = new DefinitionId[0];
        [SerializeField] private DefinitionId[] _startingSkills = new DefinitionId[0];

        [SerializeField] private int _jobChangeLevel;
        [SerializeField] private DefinitionId[] _nextJobs = new DefinitionId[0];

        public LocalizationKey NameKey => _nameKey;

        public LocalizationKey DescriptionKey => _descriptionKey;

        public AssetRef Icon => _icon;

        public GenderAvailability GenderAvailability => _genderAvailability;

        /// <summary>Starting stat values for a freshly created character of this class.</summary>
        public StatValue[] BaseStats => _baseStats;

        /// <summary>Ongoing modifiers contributed by simply being this class.</summary>
        public StatModifier[] StatModifiers => _statModifiers;

        /// <summary>References to <see cref="ItemDefinition"/> granted at creation.</summary>
        public DefinitionId[] StartingEquipment => _startingEquipment;

        /// <summary>References to <see cref="SkillDefinition"/> known at creation.</summary>
        public DefinitionId[] StartingSkills => _startingSkills;

        /// <summary>Character level at which this class may advance. Authored, not assumed.</summary>
        public int JobChangeLevel => _jobChangeLevel;

        /// <summary>References to <see cref="JobDefinition"/> reachable from this class.</summary>
        public DefinitionId[] NextJobs => _nextJobs;
    }
}
