namespace ChibiFantasy.UI
{
    /// <summary>
    /// The range a split quantity may take.
    /// </summary>
    /// <remarks>
    /// <b>UX, not security.</b> This exists so a dialog can grey out a confirm button and
    /// clamp a text field instead of letting a player send a request that is certain to be
    /// refused. It mirrors what <c>ItemContainerState.Split</c> already enforces, and the
    /// container remains the authority -- a caller that skips this entirely and asks for
    /// nonsense is refused there, unchanged.
    ///
    /// <b>Why the maximum is one less than the stack.</b> Splitting a whole stack is not a
    /// split, it is a move: it would leave the source slot holding zero, which
    /// <c>ItemInstance</c> refuses to represent. That is the container's rule, restated
    /// here rather than invented.
    /// </remarks>
    public readonly struct SplitBounds
    {
        private SplitBounds(bool valid, int stackQuantity)
        {
            IsSplittable = valid;
            StackQuantity = stackQuantity;
        }

        /// <summary>Whether a split is possible at all.</summary>
        public bool IsSplittable { get; }

        /// <summary>How many are in the stack.</summary>
        public int StackQuantity { get; }

        /// <summary>Smallest split. Always one when a split is possible.</summary>
        public int Min => IsSplittable ? 1 : 0;

        /// <summary>Largest split: one less than the stack, so something is left behind.</summary>
        public int Max => IsSplittable ? StackQuantity - 1 : 0;

        /// <summary>A stack that cannot be split.</summary>
        public static SplitBounds None => default;

        /// <summary>
        /// The bounds for a slot.
        /// </summary>
        /// <remarks>A stack of one has nothing to split; so does an empty slot and so does a
        /// piece of equipment, which never stacks.</remarks>
        public static SplitBounds For(ItemSlotViewData slot)
        {
            if (slot.IsEmpty || slot.Quantity < 2) return None;
            return new SplitBounds(true, slot.Quantity);
        }

        /// <summary>Whether a requested quantity is inside the range.</summary>
        public bool Allows(int quantity)
        {
            return IsSplittable && quantity >= Min && quantity <= Max;
        }

        /// <summary>The nearest allowed quantity, for a field a player is typing into.</summary>
        public int Clamp(int quantity)
        {
            if (!IsSplittable) return 0;
            if (quantity < Min) return Min;
            return quantity > Max ? Max : quantity;
        }

        /// <summary>A sensible starting value: half the stack, rounded down, at least one.</summary>
        public int DefaultQuantity => IsSplittable ? Clamp(StackQuantity / 2) : 0;

        public override string ToString()
        {
            return IsSplittable ? Min + ".." + Max + " of " + StackQuantity : "not splittable";
        }
    }
}
