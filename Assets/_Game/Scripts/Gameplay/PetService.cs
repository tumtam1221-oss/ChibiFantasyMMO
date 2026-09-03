using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a pet operation was refused.</summary>
    public enum PetRejection
    {
        None = 0,

        /// <summary>No pet, no registry or no state was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such pet definition could be resolved.</summary>
        UnknownPet = 2,

        /// <summary>Content turned the pet off.</summary>
        PetDisabled = 3,

        /// <summary>The pet is not this owner's.</summary>
        NotOwned = 4,

        /// <summary>Experience below zero. A pet is never un-levelled by an award.</summary>
        NegativeExperience = 5,

        /// <summary>The pet is already at its authored ceiling.</summary>
        MaxLevelReached = 6,

        /// <summary>The pet has no further authored stage.</summary>
        NoEvolutionAvailable = 7,

        /// <summary>The stage's level requirement is not met.</summary>
        LevelRequirementNotMet = 8,

        /// <summary>The stage's experience requirement is not met.</summary>
        ExperienceRequirementNotMet = 9,

        /// <summary>The material the stage requires is missing.</summary>
        MissingMaterial = 10,

        /// <summary>The stage names a form that does not resolve.</summary>
        UnknownEvolvedForm = 11,

        /// <summary>The state passed is behind the pet's current revision.</summary>
        StaleRevision = 12
    }

    /// <summary>What a pet operation did.</summary>
    public readonly struct PetResult
    {
        private PetResult(bool accepted, PetRejection reason, PetInstance pet,
            DefinitionId definition, int level, int experience, int levelsGained,
            DefinitionId grantedBuff, bool auraForm)
        {
            IsAccepted = accepted;
            Reason = reason;
            Pet = pet;
            Definition = definition;
            Level = level;
            Experience = experience;
            LevelsGained = levelsGained;
            GrantedBuff = grantedBuff;
            IsAuraForm = auraForm;
        }

        public bool IsAccepted { get; }

        public PetRejection Reason { get; }

        /// <summary>The pet operated on. The same object throughout its life.</summary>
        public PetInstance Pet { get; }

        /// <summary>What the pet is now. Changes on evolution.</summary>
        public DefinitionId Definition { get; }

        public int Level { get; }

        public int Experience { get; }

        /// <summary>How many levels one award produced. More than one is normal.</summary>
        public int LevelsGained { get; }

        /// <summary>Reference to the <see cref="StatusEffectDefinition"/> now granted to the owner.</summary>
        public DefinitionId GrantedBuff { get; }

        /// <summary>Whether the pet is now an aura on its owner rather than a follower.</summary>
        public bool IsAuraForm { get; }

        public static PetResult Accepted(PetInstance pet, DefinitionId definition, int level,
            int experience, int levelsGained = 0, DefinitionId grantedBuff = default,
            bool auraForm = false)
        {
            return new PetResult(true, PetRejection.None, pet, definition, level, experience,
                levelsGained, grantedBuff, auraForm);
        }

        public static PetResult Rejected(PetRejection reason, PetInstance pet = null)
        {
            return new PetResult(false, reason, pet, default, 0, 0, 0, default, false);
        }

        public override string ToString()
        {
            return IsAccepted
                ? Definition + " lv" + Level + " (" + Experience + "xp)"
                : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Owning, levelling and evolving a pet.
    /// </summary>
    /// <remarks>
    /// <b>No pet has a class.</b> There is no fire pet and no wolf evolution. A pet's
    /// growth is <see cref="PetDefinition.ExperienceThresholds"/>, its outcome is
    /// <see cref="PetEvolutionStage.EvolvedForm"/> and its buff is a
    /// <see cref="StatusEffectDefinition"/> reference. No <see cref="DefinitionId"/> is
    /// compared to a literal anywhere below, so a sixth pet is an asset.
    ///
    /// <b>Integer progression, from a cumulative total.</b> A level is derived from total
    /// experience rather than accumulated alongside it, so awarding the same experience
    /// twice cannot produce a different level than awarding it once, and a reload recomputes
    /// exactly what was there. No floating-point comparison decides a level.
    ///
    /// <b>Validate fully, then mutate.</b> Evolution resolves the stage, the requirements
    /// and the target form, and confirms the material is present, before anything is spent
    /// or written. A pet whose evolved form was deleted by a patch is refused with its
    /// materials intact -- discovering that after consuming them is exactly the failure this
    /// ordering exists to prevent.
    ///
    /// <b>One pet, changed.</b> Evolution repoints the existing
    /// <see cref="PetInstance"/>; nothing here creates a second one. The instance id, the
    /// owner and the accumulated experience are the same objects afterwards.
    /// </remarks>
    public static class PetService
    {
        /// <summary>Everything a pet operation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<PetDefinition> pets,
                IDefinitionRegistry<ItemDefinition> items = null,
                IDefinitionRegistry<StatusEffectDefinition> effects = null,
                StatusEffectRuntimeState status = null,
                OwnerId owner = default)
            {
                Pets = pets;
                Items = items;
                Effects = effects;
                Status = status;
                Owner = owner;
            }

            public IDefinitionRegistry<PetDefinition> Pets { get; }

            /// <summary>Needed only by an evolution that costs a material.</summary>
            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>Needed only to apply a pet's buff.</summary>
            public IDefinitionRegistry<StatusEffectDefinition> Effects { get; }

            /// <summary>Where a pet's buff lands. Optional.</summary>
            public StatusEffectRuntimeState Status { get; }

            /// <summary>Who is acting. Invalid skips the ownership check.</summary>
            public OwnerId Owner { get; }

            public bool IsUsable => Pets != null;
        }

        // ---- acquisition ---------------------------------------------------------------

        /// <summary>
        /// Whether a pet could be created, without creating one.
        /// </summary>
        /// <remarks>What <see cref="ItemUseService"/> asks before spending the item, so a
        /// taming item is never consumed for a pet that turns out not to exist.</remarks>
        public static PetRejection CanAcquire(DefinitionId petId, in Context context)
        {
            if (!context.IsUsable) return PetRejection.MissingContext;

            PetDefinition definition;
            if (!petId.IsValid || !context.Pets.TryGet(petId, out definition) || definition == null)
                return PetRejection.UnknownPet;

            return definition.Enabled ? PetRejection.None : PetRejection.PetDisabled;
        }

        /// <summary>
        /// Creates an owned pet.
        /// </summary>
        /// <remarks>The pet is persistent state from this moment: it has an instance id, an
        /// owner and a revision, exactly like an owned item or a piece of equipment.</remarks>
        public static PetResult TryAcquire(DefinitionId petId, OwnerId owner, in Context context)
        {
            PetRejection refusal = CanAcquire(petId, context);
            if (refusal != PetRejection.None) return PetResult.Rejected(refusal);

            var pet = new PetInstance(InstanceId.New(), petId, owner);

            return PetResult.Accepted(pet, petId, pet.Level, pet.Experience);
        }

        // ---- progression ---------------------------------------------------------------

        /// <summary>
        /// The level a total amount of experience reaches.
        /// </summary>
        /// <remarks>
        /// Pure and integer-only. Thresholds are cumulative, so this is a walk up the
        /// authored curve and nothing else; the same total always gives the same level, on
        /// any machine, in any order.
        /// </remarks>
        public static int LevelFor(PetDefinition definition, int experience)
        {
            if (definition == null || experience < 0) return 1;

            int[] thresholds = definition.ExperienceThresholds;
            int level = 1;

            for (int i = 0; i < thresholds.Length; i++)
            {
                if (experience < thresholds[i]) break;
                level++;
            }

            int cap = definition.EffectiveMaxLevel;
            return level > cap ? cap : level;
        }

        /// <summary>
        /// Total experience needed for the next level, or zero at the cap.
        /// </summary>
        /// <remarks>What a progress bar needs. Zero means there is no next level, which a
        /// bar shows as full rather than dividing by.</remarks>
        public static int ExperienceForNextLevel(PetDefinition definition, int currentLevel)
        {
            if (definition == null) return 0;

            int[] thresholds = definition.ExperienceThresholds;
            int index = currentLevel - 1;

            if (index < 0 || index >= thresholds.Length) return 0;
            if (currentLevel >= definition.EffectiveMaxLevel) return 0;

            return thresholds[index];
        }

        /// <summary>
        /// Awards experience.
        /// </summary>
        /// <remarks>
        /// <b>One mutation per award.</b> Level and experience are written together, so an
        /// award that crosses three levels advances the revision once rather than four times.
        /// Anything watching sees one award, because one award happened.
        ///
        /// <b>Overflow is kept.</b> Experience past the cap stays on the pet rather than
        /// being discarded, so raising a pet's ceiling in a later patch credits what it
        /// already earned. The level is what stops at the cap.
        ///
        /// A negative award is refused rather than clamped: something computed it, and
        /// silently treating it as zero would hide the bug that produced it.
        /// </remarks>
        public static PetResult TryGrantExperience(PetInstance pet, int amount, in Context context)
        {
            if (pet == null || !context.IsUsable) return PetResult.Rejected(PetRejection.MissingContext, pet);

            if (amount < 0) return PetResult.Rejected(PetRejection.NegativeExperience, pet);

            if (context.Owner.IsValid && pet.Owner != context.Owner)
                return PetResult.Rejected(PetRejection.NotOwned, pet);

            PetDefinition definition;
            if (!context.Pets.TryGet(pet.DefinitionId, out definition) || definition == null)
                return PetResult.Rejected(PetRejection.UnknownPet, pet);

            int before = pet.Level;

            // Guarded against a total that would wrap; a pet at int.MaxValue experience is
            // already past every authored threshold, so saturating changes no level.
            long total = (long)pet.Experience + amount;
            int experience = total > int.MaxValue ? int.MaxValue : (int)total;

            int level = LevelFor(definition, experience);

            pet.SetProgress(level, experience);

            return PetResult.Accepted(pet, pet.DefinitionId, level, experience, level - before,
                CurrentBuff(pet, definition));
        }

        // ---- evolution -----------------------------------------------------------------

        /// <summary>
        /// The stage a pet would evolve through next, if any.
        /// </summary>
        /// <remarks>
        /// Read off the pet's <em>current</em> definition, because evolution changes what the
        /// pet is: a chain <c>A -&gt; B -&gt; C</c> is three definitions, each naming the
        /// next. So the next stage is always the current form's first authored stage, and
        /// <see cref="PetInstance.EvolutionStage"/> is a count of how far the pet has come
        /// rather than an index anything looks up with.
        ///
        /// False when the form is terminal, which is what a pet at the end of its chain is.
        /// </remarks>
        public static bool TryGetNextStage(PetDefinition definition, out PetEvolutionStage stage)
        {
            stage = default;
            if (definition == null) return false;

            PetEvolutionStage[] stages = definition.EvolutionStages;
            if (stages.Length == 0) return false;

            stage = stages[0];
            return stage.EvolvedForm.IsValid;
        }

        /// <summary>
        /// Whether a pet could evolve, without evolving it or spending anything.
        /// </summary>
        /// <remarks>Every check <see cref="TryEvolve"/> makes. Exposed so a UI greys out a
        /// button by asking the service rather than by re-deriving the rules.</remarks>
        public static PetRejection CanEvolve(PetInstance pet, ItemContainerState materials,
            in Context context)
        {
            if (pet == null || !context.IsUsable) return PetRejection.MissingContext;

            if (context.Owner.IsValid && pet.Owner != context.Owner) return PetRejection.NotOwned;

            PetDefinition definition;
            if (!context.Pets.TryGet(pet.DefinitionId, out definition) || definition == null)
                return PetRejection.UnknownPet;

            PetEvolutionStage stage;
            if (!TryGetNextStage(definition, out stage))
                return PetRejection.NoEvolutionAvailable;

            if (pet.Level < stage.RequiredLevel) return PetRejection.LevelRequirementNotMet;

            if (pet.Experience < stage.RequiredExperience)
                return PetRejection.ExperienceRequirementNotMet;

            PetDefinition evolved;
            if (!context.Pets.TryGet(stage.EvolvedForm, out evolved) || evolved == null)
                return PetRejection.UnknownEvolvedForm;

            if (!evolved.Enabled) return PetRejection.PetDisabled;

            if (!stage.RequiredItem.IsValid) return PetRejection.None;

            // A material cost needs somewhere to take it from, and enough of it. Both are
            // checked here so nothing is spent before the outcome is certain.
            if (materials == null) return PetRejection.MissingMaterial;

            return materials.CountOf(stage.RequiredItem) >= stage.RequiredItemQuantity
                ? PetRejection.None
                : PetRejection.MissingMaterial;
        }

        /// <summary>
        /// Evolves a pet into its next authored form.
        /// </summary>
        /// <param name="pet">The pet. Repointed, never replaced.</param>
        /// <param name="materials">Where an authored cost is taken from. May be null when free.</param>
        /// <param name="context">Registries and the owner's status runtime.</param>
        /// <remarks>
        /// The material is spent inside the mutation boundary, after every check has passed,
        /// so a refused evolution costs a player nothing.
        /// </remarks>
        public static PetResult TryEvolve(PetInstance pet, ItemContainerState materials,
            in Context context)
        {
            PetRejection refusal = CanEvolve(pet, materials, context);
            if (refusal != PetRejection.None) return PetResult.Rejected(refusal, pet);

            PetDefinition definition;
            context.Pets.TryGet(pet.DefinitionId, out definition);

            PetEvolutionStage stage;
            TryGetNextStage(definition, out stage);

            PetDefinition evolved;
            context.Pets.TryGet(stage.EvolvedForm, out evolved);

            // ---- everything is resolved and nothing below can fail ---------------------

            if (stage.RequiredItem.IsValid && materials != null)
            {
                materials.RemoveByDefinition(stage.RequiredItem, stage.RequiredItemQuantity);
            }

            // The old form's buff goes with the old form. Matching on the definition that
            // granted it removes exactly that, and leaves anything else on the character.
            if (context.Status != null) context.Status.RemoveFrom(definition.Id);

            pet.Evolve(stage.EvolvedForm, pet.EvolutionStage + 1);

            DefinitionId buff = stage.GrantedBuff.IsValid ? stage.GrantedBuff : evolved.BaseBuff;

            if (context.Status != null && context.Effects != null && buff.IsValid)
            {
                StatusEffectService.TryApply(context.Status, buff, evolved.Id, context.Effects);
            }

            return PetResult.Accepted(pet, pet.DefinitionId, pet.Level, pet.Experience, 0, buff,
                evolved.IsAuraForm);
        }

        // ---- buffs ---------------------------------------------------------------------

        /// <summary>
        /// The buff a pet currently grants its owner.
        /// </summary>
        /// <remarks>The pet's own base buff. A pet that has evolved is already a different
        /// definition, so its evolved buff is that definition's base buff -- there is no
        /// stage lookup and no per-pet branch.</remarks>
        public static DefinitionId CurrentBuff(PetInstance pet, PetDefinition definition)
        {
            if (pet == null || definition == null) return DefinitionId.None;
            return definition.BaseBuff;
        }

        /// <summary>
        /// Brings a pet out and applies its buff.
        /// </summary>
        /// <remarks>
        /// The buff is a status effect granted by the pet's definition, applied through the
        /// one status runtime. There is no pet buff system: dismissing removes exactly what
        /// the pet granted, by grantor, and anything else on the character is untouched.
        /// </remarks>
        public static PetResult TrySummon(PetCompanionState companion, PetInstance pet,
            in Context context)
        {
            if (companion == null || pet == null || !context.IsUsable)
                return PetResult.Rejected(PetRejection.MissingContext, pet);

            if (context.Owner.IsValid && pet.Owner != context.Owner)
                return PetResult.Rejected(PetRejection.NotOwned, pet);

            PetDefinition definition;
            if (!context.Pets.TryGet(pet.DefinitionId, out definition) || definition == null)
                return PetResult.Rejected(PetRejection.UnknownPet, pet);

            if (!definition.Enabled) return PetResult.Rejected(PetRejection.PetDisabled, pet);

            // A pet already out is dismissed first, so its buff cannot be applied twice and
            // the previous pet's grant cannot outlive it.
            if (companion.IsSummoned && companion.Summoned != pet) Dismiss(companion, context);

            // Read off what the pet is now, not off how it got here.
            companion.Summon(pet, definition.IsAuraForm);

            DefinitionId buff = CurrentBuff(pet, definition);

            if (context.Status != null && context.Effects != null && buff.IsValid)
            {
                StatusEffectService.TryApply(context.Status, buff, definition.Id, context.Effects);
            }

            return PetResult.Accepted(pet, pet.DefinitionId, pet.Level, pet.Experience, 0, buff,
                companion.IsAuraForm);
        }

        /// <summary>Puts a pet away and takes back exactly what it granted.</summary>
        public static bool Dismiss(PetCompanionState companion, in Context context)
        {
            if (companion == null || !companion.IsSummoned) return false;

            PetInstance pet = companion.Summoned;

            if (context.Status != null && pet != null && context.IsUsable)
            {
                PetDefinition definition;
                if (context.Pets.TryGet(pet.DefinitionId, out definition) && definition != null)
                {
                    context.Status.RemoveFrom(definition.Id);
                }
            }

            return companion.Dismiss();
        }
    }
}
