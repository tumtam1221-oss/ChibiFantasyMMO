namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a container operation was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, matching <see cref="SkillUseRejection"/>,
    /// <see cref="TargetRejection"/> and the rest of the project's vocabulary: each value
    /// needs a different message to a player and a different response from a server.
    ///
    /// Every one of these is checked <em>before</em> anything is written, which is what
    /// makes "a refused operation changes nothing" true by construction rather than by
    /// unwinding afterwards.
    /// </remarks>
    public enum ItemContainerRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No instance was supplied.</summary>
        NoItem = 1,

        /// <summary>The instance names no definition, or none could be resolved.</summary>
        UnknownDefinition = 2,

        /// <summary>A slot index was outside the container.</summary>
        SlotOutOfRange = 3,

        /// <summary>The source slot holds nothing.</summary>
        SourceEmpty = 4,

        /// <summary>The destination slot already holds something incompatible.</summary>
        DestinationOccupied = 5,

        /// <summary>A quantity was zero, negative, or larger than what is held.</summary>
        InvalidQuantity = 6,

        /// <summary>There was not enough of the item.</summary>
        InsufficientQuantity = 7,

        /// <summary>No free or compatible space remained.</summary>
        ContainerFull = 8,

        /// <summary>The two slots hold different items, or one of them does not stack.</summary>
        NotStackable = 9,

        /// <summary>An instance with this identity is already in the container.</summary>
        DuplicateInstance = 10,

        /// <summary>Source and destination were the same slot.</summary>
        SameSlot = 11
    }

    /// <summary>
    /// What a container operation did.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Remainder"/> is the field that matters.</b> Adding sixty potions to a
    /// container with room for forty is not a failure and not a success: forty go in and
    /// twenty are still in the caller's hands. A boolean would force the container to
    /// either refuse the whole thing or silently destroy the rest, and quietly destroying
    /// player items is the worst outcome available. The caller is told exactly what is
    /// left and decides.
    ///
    /// Shaped after <see cref="AttackResult"/> and <see cref="SkillExecutionResult"/>:
    /// accepted or refused, with a typed reason and the figures that explain it.
    /// </remarks>
    public readonly struct ItemContainerResult
    {
        private ItemContainerResult(bool accepted, ItemContainerRejection reason,
            int affectedQuantity, int remainder, int primarySlot, int secondarySlot)
        {
            IsAccepted = accepted;
            Reason = reason;
            AffectedQuantity = affectedQuantity;
            Remainder = remainder;
            PrimarySlot = primarySlot;
            SecondarySlot = secondarySlot;
        }

        /// <summary>True when the container changed. See <see cref="Remainder"/> for how much.</summary>
        public bool IsAccepted { get; }

        /// <summary><see cref="ItemContainerRejection.None"/> when accepted.</summary>
        public ItemContainerRejection Reason { get; }

        /// <summary>How many units actually moved, were added or were taken.</summary>
        public int AffectedQuantity { get; }

        /// <summary>
        /// How many the container could not take.
        /// </summary>
        /// <remarks>Zero on a complete operation. Non-zero means the caller still holds
        /// this many and must decide what to do with them; nothing was destroyed.</remarks>
        public int Remainder { get; }

        /// <summary>The slot chiefly affected, or -1.</summary>
        public int PrimarySlot { get; }

        /// <summary>The second slot in a two-slot operation, or -1.</summary>
        public int SecondarySlot { get; }

        /// <summary>Whether some of the operation completed but not all of it.</summary>
        public bool IsPartial => IsAccepted && Remainder > 0;

        public static ItemContainerResult Accepted(int affected, int remainder = 0,
            int primarySlot = -1, int secondarySlot = -1)
        {
            return new ItemContainerResult(true, ItemContainerRejection.None,
                affected, remainder, primarySlot, secondarySlot);
        }

        public static ItemContainerResult Rejected(ItemContainerRejection reason,
            int remainder = 0)
        {
            return new ItemContainerResult(false, reason, 0, remainder, -1, -1);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;

            return "accepted: " + AffectedQuantity
                + (Remainder > 0 ? " (remainder " + Remainder + ")" : string.Empty)
                + (PrimarySlot >= 0 ? " slot " + PrimarySlot : string.Empty);
        }
    }
}
