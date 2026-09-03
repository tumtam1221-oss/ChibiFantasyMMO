using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One numbered place in a container, and what is in it.
    /// </summary>
    /// <remarks>
    /// <b>Slots are the point.</b> A container is not a list of items: a player drags a
    /// potion from the fourth square to the ninth and expects it to be there next time
    /// they look, so position is state and not a rendering detail. A
    /// <c>List&lt;ItemInstance&gt;</c> would lose that the moment anything was removed.
    ///
    /// <b>The content is a <see cref="GameInstance"/>.</b> That is the existing base both
    /// <see cref="ItemInstance"/> and <see cref="EquipmentInstance"/> already share, and it
    /// carries exactly what a slot needs: a stable identity, the definition it points at,
    /// an owner and a revision. Widening it to a new common interface would have been a
    /// second item hierarchy for nothing. The asymmetry is deliberate in the schema --
    /// equipment does not stack, so it has no quantity -- and this reads a quantity through
    /// <see cref="Quantity"/> rather than assuming one.
    ///
    /// A view, not the storage. <see cref="ItemContainerState"/> owns the array; this is
    /// what a caller is handed when it asks what is where.
    /// </remarks>
    public readonly struct ItemSlot
    {
        public ItemSlot(int index, GameInstance content)
        {
            Index = index;
            Content = content;
        }

        /// <summary>Position in the container. Stable, and never negative.</summary>
        public int Index { get; }

        /// <summary>What occupies the slot, or null when it is empty.</summary>
        public GameInstance Content { get; }

        public bool IsEmpty => Content == null;

        public bool IsOccupied => Content != null;

        /// <summary>The definition the content points at, or none for an empty slot.</summary>
        public Core.DefinitionId DefinitionId =>
            Content == null ? Core.DefinitionId.None : Content.DefinitionId;

        /// <summary>The instance identity, or none for an empty slot.</summary>
        public Core.InstanceId InstanceId =>
            Content == null ? Core.InstanceId.None : Content.InstanceId;

        /// <summary>
        /// How many the slot holds.
        /// </summary>
        /// <remarks>Equipment carries no quantity because it does not stack, so a piece of
        /// equipment counts as one. Reading it here rather than at every call site is what
        /// keeps that asymmetry from leaking into the container logic.</remarks>
        public int Quantity
        {
            get
            {
                if (Content == null) return 0;

                var item = Content as ItemInstance;
                return item != null ? item.Quantity : 1;
            }
        }

        /// <summary>Whether the slot holds a stackable item instance at all.</summary>
        public bool IsStackableContent => Content is ItemInstance;

        public override string ToString()
        {
            return IsEmpty
                ? "[" + Index + "] empty"
                : "[" + Index + "] " + DefinitionId + " x" + Quantity;
        }
    }
}
