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

        /// <summary>
        /// The last durable reward whose experience is already part of this pet's
        /// progression. Empty for a pet no reward has ever paid.
        /// </summary>
        /// <remarks>
        /// <b>Bookkeeping, not gameplay.</b> Nothing about what a pet <em>is</em> depends on
        /// it: no rule reads it, no buff comes from it, and clearing it would change no
        /// number a player can see. It sits here, beside <see cref="GameInstance.Revision"/>,
        /// because it has to travel with the progression it describes -- the two are written
        /// in one transaction, so they cannot disagree about what has been paid.
        ///
        /// <b>An opaque id.</b> This assembly does not know what a reward is and does not
        /// need to; it stores the string the server hands it and gives it back.
        ///
        /// <b>Why a total cannot answer the question.</b> "Has reward R been applied?" is
        /// not answerable from experience, because two different rewards can leave a pet on
        /// the same number. This is the only durable evidence there is.
        /// </remarks>
        [SerializeField] private string _appliedRewardId;

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

        /// <summary>The last reward already included in this pet's progression, or empty.</summary>
        public string AppliedRewardId => _appliedRewardId ?? string.Empty;

        /// <summary>
        /// Records that a reward's experience is now part of this pet's progression.
        /// </summary>
        /// <remarks>
        /// Called with the same mutation that applied the experience, so that the marker and
        /// the number it describes are saved together. It deliberately does not advance the
        /// revision on its own: this says something about a change rather than being one.
        /// </remarks>
        public void SetAppliedReward(string rewardId)
        {
            _appliedRewardId = string.IsNullOrEmpty(rewardId) ? null : rewardId;
        }

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

        /// <summary>
        /// Records level and experience together, advancing the revision once.
        /// </summary>
        /// <remarks>
        /// Awarding experience usually moves both numbers, and calling
        /// <see cref="SetLevel"/> then <see cref="SetExperience"/> would count one award as
        /// two mutations -- which anything watching the revision would read as two awards.
        /// Whether the new level is one the pet's authored curve actually reaches is
        /// <c>PetService</c>'s decision.
        ///
        /// Returns false when nothing changed, so a zero-experience award is not a mutation.
        /// </remarks>
        public bool SetProgress(int level, int experience)
        {
            ValidateLevel(level);
            ValidateExperience(experience);

            if (_level == level && _experience == experience) return false;

            _level = level;
            _experience = experience;
            AdvanceRevision();
            return true;
        }

        /// <summary>
        /// Becomes an evolved form, advancing the revision once.
        /// </summary>
        /// <remarks>
        /// <b>The same pet, changed.</b> The instance id, the owner and the accumulated
        /// experience all survive; only what the pet <em>is</em> and how far it has come
        /// change. No second <see cref="PetInstance"/> is created, so nothing has to
        /// reconcile two records of one creature and no owner has to be reassigned.
        ///
        /// Assignment only. Whether the level requirement was met, whether the material was
        /// paid and whether the target form exists at all are <c>PetService</c>'s decisions,
        /// made in full before this is called.
        /// </remarks>
        public bool Evolve(DefinitionId evolvedForm, int stage)
        {
            ValidateEvolutionStage(stage);

            if (!ReplaceDefinitionId(evolvedForm)) return false;

            _evolutionStage = stage;
            AdvanceRevision();
            return true;
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
