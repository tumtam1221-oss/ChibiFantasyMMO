using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why using an item was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, matching <see cref="ItemContainerRejection"/>,
    /// <see cref="EquipRejection"/> and <see cref="SkillUseRejection"/>: each value needs a
    /// different message to a player and a different response from a server.
    ///
    /// Every one is checked <em>before</em> anything is written or consumed, which is what
    /// makes "a refused use costs nothing" true by construction.
    /// </remarks>
    public enum ItemUseRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No container, no registry, or no resource state was supplied.</summary>
        MissingContext = 1,

        /// <summary>The slot index was outside the container.</summary>
        SlotOutOfRange = 2,

        /// <summary>The slot holds nothing.</summary>
        SourceEmpty = 3,

        /// <summary>The instance names a definition that could not be resolved.</summary>
        UnknownDefinition = 4,

        /// <summary>The definition is not authored as usable.</summary>
        NotUsable = 5,

        /// <summary>The item is in a container the acting character does not own.</summary>
        NotOwned = 6,

        /// <summary>There was less than one to spend.</summary>
        InsufficientQuantity = 7,

        /// <summary>The authored target is not one this phase can act on.</summary>
        InvalidTarget = 8,

        /// <summary>
        /// The item would have done nothing.
        /// </summary>
        /// <remarks>A full-health potion on a full-health character. Refused rather than
        /// consumed, because quietly spending a player's item for no benefit is the one
        /// outcome they will never accept.</remarks>
        NoEffect = 9,

        /// <summary>An effect is missing a field its kind requires, or has a bad value.</summary>
        InvalidEffect = 10,

        /// <summary>The authored classification and the authored effects disagree.</summary>
        UnknownUseType = 11,

        /// <summary>A referenced status effect could not be resolved.</summary>
        UnknownStatusEffect = 12,

        /// <summary>A warp destination could not be resolved.</summary>
        InvalidDestination = 13,

        /// <summary>The destination resolved, but is not somewhere a scroll may reach.</summary>
        WarpNotAllowed = 14
    }

    /// <summary>
    /// One buff an accepted use granted.
    /// </summary>
    /// <remarks>
    /// <b>A resolved grant, not an applied effect.</b> The project has authored status
    /// effects (<see cref="StatusEffectDefinition"/>) but no runtime state that holds an
    /// effect on a character with a remaining duration -- that is server-owned and does
    /// not exist yet. Building one here would be a status engine smuggled into an
    /// inventory phase.
    ///
    /// So the use service does the part it can prove correct: it resolves the configured
    /// effect, its duration and its authored modifiers from data, and reports them. Whoever
    /// owns runtime status later consumes these and applies them. The contract is small on
    /// purpose and the limitation is real: <em>a buff item's modifiers do not yet reach a
    /// character's stats.</em>
    /// </remarks>
    public readonly struct ItemBuffGrant
    {
        public ItemBuffGrant(DefinitionId statusEffect, float durationSeconds, int maxStacks,
            StatusEffectStackBehavior stackBehavior)
        {
            StatusEffect = statusEffect;
            DurationSeconds = durationSeconds;
            MaxStacks = maxStacks;
            StackBehavior = stackBehavior;
        }

        /// <summary>The <see cref="StatusEffectDefinition"/> that was granted.</summary>
        public DefinitionId StatusEffect { get; }

        /// <summary>
        /// How long it lasts.
        /// </summary>
        /// <remarks>The item's authored override when it set one, otherwise the status
        /// effect's own duration. Resolved here so a consumer never has to know which of
        /// the two won.</remarks>
        public float DurationSeconds { get; }

        public int MaxStacks { get; }

        public StatusEffectStackBehavior StackBehavior { get; }

        public override string ToString()
        {
            return StatusEffect + " for " + DurationSeconds + "s";
        }
    }

    /// <summary>
    /// What using an item did.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="ItemContainerResult"/> and <see cref="EquipResult"/>:
    /// accepted or refused, with a typed reason and the figures that explain it.
    ///
    /// <see cref="ConsumedQuantity"/> is zero or one and nothing else. A use spends exactly
    /// one, or it spends nothing; there is no partial use to represent.
    /// </remarks>
    public readonly struct ItemUseResult
    {
        private ItemUseResult(bool accepted, ItemUseRejection reason, DefinitionId definitionId,
            InstanceId instanceId, int consumed, int health, int mana, int buffs,
            DefinitionId destination, DefinitionId destinationSpawn)
        {
            IsAccepted = accepted;
            Reason = reason;
            DefinitionId = definitionId;
            InstanceId = instanceId;
            ConsumedQuantity = consumed;
            HealthRestored = health;
            ManaRestored = mana;
            BuffsGranted = buffs;
            WarpDestination = destination;
            WarpDestinationSpawn = destinationSpawn;
        }

        public bool IsAccepted { get; }

        public ItemUseRejection Reason { get; }

        /// <summary>What was used. Set on rejection too, when it got far enough to know.</summary>
        public DefinitionId DefinitionId { get; }

        /// <summary>Which owned copy was used.</summary>
        public InstanceId InstanceId { get; }

        /// <summary>One on success, zero on refusal. Never anything else.</summary>
        public int ConsumedQuantity { get; }

        /// <summary>Health actually gained, after clamping. Not the authored amount.</summary>
        public int HealthRestored { get; }

        /// <summary>Mana actually gained, after clamping.</summary>
        public int ManaRestored { get; }

        /// <summary>How many <see cref="ItemBuffGrant"/> entries were written to the caller's list.</summary>
        public int BuffsGranted { get; }

        /// <summary>
        /// Where an accepted warp sends the character.
        /// </summary>
        /// <remarks>A resolved, validated destination -- not a completed journey. Executing
        /// travel is a later system and, in a served game, the server's call.</remarks>
        public DefinitionId WarpDestination { get; }

        /// <summary>
        /// The authored place an accepted warp arrives at.
        /// </summary>
        /// <remarks>Resolved by the service from the destination map, so the client places
        /// the traveller at a point content authored rather than inventing one. Invalid when
        /// no spawn registry was supplied, in which case only the map was validated.</remarks>
        public DefinitionId WarpDestinationSpawn { get; }

        public bool HasWarp => WarpDestination.IsValid;

        public bool RestoredAnything => HealthRestored > 0 || ManaRestored > 0;

        public static ItemUseResult Accepted(DefinitionId definitionId, InstanceId instanceId,
            int health = 0, int mana = 0, int buffs = 0, DefinitionId destination = default,
            DefinitionId destinationSpawn = default)
        {
            return new ItemUseResult(true, ItemUseRejection.None, definitionId, instanceId,
                1, health, mana, buffs, destination, destinationSpawn);
        }

        public static ItemUseResult Rejected(ItemUseRejection reason,
            DefinitionId definitionId = default, InstanceId instanceId = default)
        {
            return new ItemUseResult(false, reason, definitionId, instanceId,
                0, 0, 0, 0, default, default);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;

            return "used " + DefinitionId + " hp+" + HealthRestored + " mp+" + ManaRestored
                + " buffs=" + BuffsGranted + (HasWarp ? " warp=" + WarpDestination : string.Empty);
        }
    }
}
