using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What a collectible presentation event is announcing.</summary>
    /// <remarks>
    /// Facts in the past tense, every one of them, exactly as
    /// <see cref="CombatPresentationEventKind"/> is. Nothing here is an instruction: there
    /// is no PlayAuraEffect and no ShowFruitBurst, because an event that told presentation
    /// what to do would eventually let presentation decide what happened.
    /// </remarks>
    public enum CollectibleEventKind
    {
        None = 0,

        /// <summary>A character took on a Devil Fruit's power.</summary>
        DevilFruitActivated = 1,

        /// <summary>An active fruit made a skill available to its bearer.</summary>
        DevilFruitSkillGranted = 2,

        /// <summary>A card went into a piece of equipment.</summary>
        CardInserted = 3,

        /// <summary>A card came back out.</summary>
        CardRemoved = 4,

        /// <summary>A pet came out.</summary>
        PetSummoned = 5,

        /// <summary>A pet gained at least one level.</summary>
        PetLevelUp = 6,

        /// <summary>A pet became a different form.</summary>
        PetEvolved = 7,

        /// <summary>A pet is now present as an aura on its owner rather than as a follower.</summary>
        PetAuraActivated = 8,

        /// <summary>A pet was put away.</summary>
        PetDismissed = 9
    }

    /// <summary>
    /// Something the collectible systems did, described for presentation.
    /// </summary>
    /// <remarks>
    /// <b>A report, never a command.</b> Every field is a copy of an outcome that has already
    /// happened; acting on one cannot change gameplay and ignoring one cannot either. The
    /// same boundary <see cref="CombatPresentationEvent"/> draws, drawn again rather than
    /// widened -- combat events describe a fight and these describe ownership, and merging
    /// them would give a combat presenter reasons to know about pets.
    ///
    /// <b>Identities and asset references, not objects.</b> No <see cref="PetInstance"/> and
    /// no definition object appears here. A presenter gets ids and an
    /// <see cref="AssetRef"/>, which is enough to find a prefab and not enough to mutate
    /// anything.
    ///
    /// <b>Immutable, and in the engine-free assembly.</b> No ParticleSystem, no AudioClip and
    /// no prefab type, so gameplay can publish these and a headless server can publish the
    /// same events to nobody.
    /// </remarks>
    public readonly struct CollectiblePresentationEvent
    {
        private CollectiblePresentationEvent(CollectibleEventKind kind, DefinitionId definition,
            InstanceId instance, DefinitionId related, int value, AssetRef visual, AssetRef sound)
        {
            Kind = kind;
            Definition = definition;
            Instance = instance;
            Related = related;
            Value = value;
            VisualEffect = visual;
            SoundEffect = sound;
        }

        public CollectibleEventKind Kind { get; }

        /// <summary>The fruit, card or pet this is about.</summary>
        public DefinitionId Definition { get; }

        /// <summary>The owned copy, where there is one.</summary>
        public InstanceId Instance { get; }

        /// <summary>
        /// A second id the event needs.
        /// </summary>
        /// <remarks>The granted skill, the equipment a card went into, the buff a pet
        /// applies. One field rather than three named ones, because an event carries at most
        /// one and three would be empty twice over in every case.</remarks>
        public DefinitionId Related { get; }

        /// <summary>A count the event needs: levels gained, or a socket index.</summary>
        public int Value { get; }

        public AssetRef VisualEffect { get; }

        public AssetRef SoundEffect { get; }

        public bool IsValid => Kind != CollectibleEventKind.None;

        public static CollectiblePresentationEvent DevilFruitActivated(in DevilFruitResult result,
            InstanceId source)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.DevilFruitActivated,
                result.Fruit, source, default, result.EffectsApplied, result.VisualEffect,
                result.SoundEffect);
        }

        /// <summary>
        /// An activation described from the fruit's own definition.
        /// </summary>
        /// <remarks>For the path through <see cref="ItemUseService"/>, which reports that a
        /// fruit was activated without returning the fruit service's own result. The
        /// presenter needs the same asset references either way.</remarks>
        public static CollectiblePresentationEvent DevilFruitActivated(DefinitionId fruit,
            InstanceId source, AssetRef visual, AssetRef sound)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.DevilFruitActivated,
                fruit, source, default, 0, visual, sound);
        }

        public static CollectiblePresentationEvent DevilFruitSkillGranted(DefinitionId fruit,
            DefinitionId skill)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.DevilFruitSkillGranted,
                fruit, default, skill, 0, default, default);
        }

        public static CollectiblePresentationEvent CardInserted(in CardSocketResult result,
            InstanceId equipment)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.CardInserted,
                result.Card, result.CardInstance, default, result.SocketIndex, default, default);
        }

        public static CollectiblePresentationEvent CardRemoved(in CardSocketResult result,
            InstanceId equipment)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.CardRemoved,
                result.Card, result.CardInstance, default, result.SocketIndex, default, default);
        }

        public static CollectiblePresentationEvent PetSummoned(in PetResult result, AssetRef model,
            AssetRef sound)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.PetSummoned,
                result.Definition, result.Pet == null ? default : result.Pet.InstanceId,
                result.GrantedBuff, result.Level, model, sound);
        }

        public static CollectiblePresentationEvent PetDismissed(DefinitionId pet,
            InstanceId instance)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.PetDismissed, pet,
                instance, default, 0, default, default);
        }

        public static CollectiblePresentationEvent PetLevelUp(in PetResult result)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.PetLevelUp,
                result.Definition, result.Pet == null ? default : result.Pet.InstanceId,
                default, result.Level, default, default);
        }

        public static CollectiblePresentationEvent PetEvolved(in PetResult result, AssetRef visual,
            AssetRef sound)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.PetEvolved,
                result.Definition, result.Pet == null ? default : result.Pet.InstanceId,
                result.GrantedBuff, result.Level, visual, sound);
        }

        public static CollectiblePresentationEvent PetAuraActivated(in PetResult result,
            AssetRef aura)
        {
            return new CollectiblePresentationEvent(CollectibleEventKind.PetAuraActivated,
                result.Definition, result.Pet == null ? default : result.Pet.InstanceId,
                result.GrantedBuff, 0, aura, default);
        }

        public override string ToString()
        {
            return Kind + " " + Definition;
        }
    }
}
