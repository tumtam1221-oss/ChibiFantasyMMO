using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player's actual pet.
    /// </summary>
    /// <remarks>
    /// The definition describes the species and its possible evolution stages; this records
    /// how far one particular pet has come.
    ///
    /// Persisted state only. Awarding experience, deciding when a level is reached,
    /// choosing an evolution outcome, following the owner and applying stage buffs are all
    /// later gameplay, and all server-authoritative.
    /// </remarks>
    [Serializable]
    public sealed class PetInstance : GameInstance
    {
        [SerializeField] private int _level;
        [SerializeField] private int _experience;
        [SerializeField] private int _evolutionStage;

        /// <summary>Exists for deserializers.</summary>
        public PetInstance()
        {
        }

        public PetInstance(InstanceId instanceId, DefinitionId petDefinitionId, OwnerId owner)
            : this(instanceId, petDefinitionId, owner, 1, 0, 0)
        {
        }

        public PetInstance(InstanceId instanceId, DefinitionId petDefinitionId, OwnerId owner,
            int level, int experience, int evolutionStage)
            : base(instanceId, petDefinitionId, owner)
        {
            ValidateLevel(level);
            ValidateExperience(experience);
            ValidateEvolutionStage(evolutionStage);

            _level = level;
            _experience = experience;
            _evolutionStage = evolutionStage;
        }

        /// <summary>Current level. At least one.</summary>
        public int Level => _level;

        /// <summary>Accumulated experience. Never negative.</summary>
        public int Experience => _experience;

        /// <summary>Index into the definition's authored evolution stages. Zero is unevolved.</summary>
        public int EvolutionStage => _evolutionStage;

        public void SetLevel(int level)
        {
            ValidateLevel(level);
            _level = level;
            AdvanceRevision();
        }

        public void SetExperience(int experience)
        {
            ValidateExperience(experience);
            _experience = experience;
            AdvanceRevision();
        }

        /// <summary>
        /// Records the reached evolution stage and advances the revision.
        /// </summary>
        /// <remarks>
        /// Only the floor of zero is enforced. Whether the stage exists on the pet's
        /// definition, and whether its level and experience requirements were met, is
        /// server-side validation against
        /// <see cref="PetDefinition.EvolutionStages"/>.
        /// </remarks>
        public void SetEvolutionStage(int evolutionStage)
        {
            ValidateEvolutionStage(evolutionStage);
            _evolutionStage = evolutionStage;
            AdvanceRevision();
        }

        private static void ValidateLevel(int level)
        {
            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Pet level starts at one.");
            }
        }

        private static void ValidateExperience(int experience)
        {
            if (experience < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(experience), experience, "Experience cannot be negative.");
            }
        }

        private static void ValidateEvolutionStage(int evolutionStage)
        {
            if (evolutionStage < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(evolutionStage), evolutionStage, "Evolution stage cannot be negative.");
            }
        }
    }
}
