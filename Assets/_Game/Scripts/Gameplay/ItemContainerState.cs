using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A fixed set of numbered slots holding a character's items.
    /// </summary>
    /// <remarks>
    /// <b>One type, used twice.</b> A backpack and a warehouse differ in how big they are
    /// and who opens them, not in what adding a potion means. There is deliberately no
    /// InventoryState class and no StorageState class inheriting from this: two subclasses
    /// that added nothing but a name would be abstraction bought for nothing, and moving
    /// items between two containers is then just a transfer between two of these. See
    /// <see cref="ItemContainerTransfer"/>.
    ///
    /// <b>Persistent, and identified by stable ids.</b> Implements
    /// <see cref="IPersistentState"/>: what a player is carrying must survive a restart.
    /// Nothing is keyed by array position across a save -- a slot index is meaningful
    /// <em>within</em> a container and each occupant carries its own
    /// <see cref="InstanceId"/>.
    ///
    /// <b>Nothing is written until everything has been checked.</b> Every operation
    /// validates to completion first, so a refused one leaves the container byte-identical
    /// rather than half-applied. There is no rollback because there is no partial path.
    ///
    /// <b>Stack rules are content, not code.</b> Whether an item stacks and how high comes
    /// from <see cref="ItemDefinition.Stackable"/> and
    /// <see cref="ItemDefinition.MaxStackSize"/>, read through a registry the caller
    /// supplies. No stack size appears anywhere in this file and no item id is named.
    ///
    /// <b>Placement is deterministic.</b> Existing compatible stacks are topped up in slot
    /// order, then the lowest empty slots are used. Two identical adds against two
    /// identical containers always land in the same places, which is what lets a server
    /// and a client agree without replicating positions.
    /// </remarks>
    public sealed class ItemContainerState : IPersistentState
    {
        private readonly GameInstance[] _slots;
        private Revision _revision;

        public ItemContainerState(OwnerId owner, int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), capacity, "A container cannot have negative capacity.");
            }

            Owner = owner;
            _slots = new GameInstance[capacity];
            _revision = Revision.Initial;
        }

        /// <summary>Who the container belongs to.</summary>
        public OwnerId Owner { get; }

        /// <summary>
        /// How many slots exist. Valid indices are 0 to Capacity-1.
        /// </summary>
        /// <remarks>Supplied at construction rather than fixed in code, because how big a
        /// bag is will be a character, account or purchase rule and none of those are
        /// decided here.</remarks>
        public int Capacity => _slots.Length;

        public Revision Revision => _revision;

        /// <summary>How many slots hold something.</summary>
        public int OccupiedSlots
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Length; i++) if (_slots[i] != null) count++;
                return count;
            }
        }

        public int FreeSlots => Capacity - OccupiedSlots;

        public bool IsFull => FreeSlots == 0;

        /// <summary>Every slot, in index order.</summary>
        public IReadOnlyList<ItemSlot> Slots
        {
            get
            {
                var view = new ItemSlot[_slots.Length];
                for (int i = 0; i < _slots.Length; i++) view[i] = new ItemSlot(i, _slots[i]);
                return view;
            }
        }

        public bool IsValidIndex(int index) => index >= 0 && index < _slots.Length;

        /// <summary>Reads one slot. Returns an empty slot view for an out-of-range index.</summary>
        public ItemSlot GetSlot(int index)
        {
            return IsValidIndex(index) ? new ItemSlot(index, _slots[index]) : new ItemSlot(index, null);
        }

        /// <summary>Finds the slot holding an instance, or -1.</summary>
        public int IndexOf(InstanceId instanceId)
        {
            if (!instanceId.IsValid) return -1;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].InstanceId == instanceId) return i;
            }

            return -1;
        }

        /// <summary>Total units of one definition held across every slot.</summary>
        public int CountOf(DefinitionId definitionId)
        {
            int total = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null || _slots[i].DefinitionId != definitionId) continue;
                total += new ItemSlot(i, _slots[i]).Quantity;
            }

            return total;
        }

        // ----------------------------------------------------------------- add

        /// <summary>
        /// Places an instance, topping up compatible stacks before using empty slots.
        /// </summary>
        /// <remarks>
        /// The instance's quantity is treated as the amount to place. What will not fit is
        /// reported as <see cref="ItemContainerResult.Remainder"/> and the supplied
        /// instance is left holding exactly that much, so nothing is lost and the caller
        /// can put it somewhere else, drop it or refuse the pickup.
        ///
        /// A non-stackable occupant, equipment included, always takes a whole slot.
        /// </remarks>
        public ItemContainerResult Add(GameInstance instance, IDefinitionRegistry<ItemDefinition> items)
        {
            if (instance == null) return ItemContainerResult.Rejected(ItemContainerRejection.NoItem);

            if (!instance.DefinitionId.IsValid || items == null
                || !items.TryGet(instance.DefinitionId, out ItemDefinition definition)
                || definition == null)
            {
                return ItemContainerResult.Rejected(ItemContainerRejection.UnknownDefinition);
            }

            if (IndexOf(instance.InstanceId) >= 0)
            {
                return ItemContainerResult.Rejected(ItemContainerRejection.DuplicateInstance);
            }

            var stackable = instance as ItemInstance;

            // Non-stackable content occupies exactly one free slot.
            if (stackable == null || !definition.Stackable || definition.MaxStackSize <= 1)
            {
                int free = FirstEmptySlot();
                if (free < 0) return ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);

                _slots[free] = instance;
                Advance();
                return ItemContainerResult.Accepted(
                    new ItemSlot(free, instance).Quantity, 0, free);
            }

            int max = definition.MaxStackSize;
            int outstanding = stackable.Quantity;
            int placed = 0;
            int firstTouched = -1;

            // Top up existing stacks first, in slot order, so placement is reproducible.
            for (int i = 0; i < _slots.Length && outstanding > 0; i++)
            {
                var occupant = _slots[i] as ItemInstance;
                if (occupant == null || occupant.DefinitionId != instance.DefinitionId) continue;

                int room = max - occupant.Quantity;
                if (room <= 0) continue;

                int move = room < outstanding ? room : outstanding;
                occupant.SetQuantity(occupant.Quantity + move);
                outstanding -= move;
                placed += move;
                if (firstTouched < 0) firstTouched = i;
            }

            // Whatever is left goes into empty slots, lowest index first.
            while (outstanding > 0)
            {
                int free = FirstEmptySlot();
                if (free < 0) break;

                int move = max < outstanding ? max : outstanding;

                if (move == outstanding && placed == 0)
                {
                    // The whole amount fits and nothing was merged: keep the caller's
                    // instance so its identity survives into the container.
                    stackable.SetQuantity(move);
                    _slots[free] = stackable;
                }
                else
                {
                    _slots[free] = new ItemInstance(
                        InstanceId.New(), instance.DefinitionId, Owner, move);
                }

                outstanding -= move;
                placed += move;
                if (firstTouched < 0) firstTouched = free;
            }

            if (placed == 0) return ItemContainerResult.Rejected(ItemContainerRejection.ContainerFull);

            // Leave the caller's instance holding exactly what did not fit.
            if (outstanding > 0 && IndexOf(stackable.InstanceId) < 0) stackable.SetQuantity(outstanding);

            Advance();
            return ItemContainerResult.Accepted(placed, outstanding, firstTouched);
        }

        // -------------------------------------------------------------- remove

        /// <summary>Takes a quantity out of one slot, clearing it when it empties.</summary>
        public ItemContainerResult RemoveAt(int index, int quantity)
        {
            if (!IsValidIndex(index)) return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);
            if (quantity <= 0) return ItemContainerResult.Rejected(ItemContainerRejection.InvalidQuantity);

            GameInstance occupant = _slots[index];
            if (occupant == null) return ItemContainerResult.Rejected(ItemContainerRejection.SourceEmpty);

            int held = new ItemSlot(index, occupant).Quantity;
            if (quantity > held) return ItemContainerResult.Rejected(ItemContainerRejection.InsufficientQuantity);

            var stackable = occupant as ItemInstance;

            if (stackable == null || quantity == held)
            {
                _slots[index] = null;
            }
            else
            {
                stackable.SetQuantity(held - quantity);
            }

            Advance();
            return ItemContainerResult.Accepted(quantity, 0, index);
        }

        /// <summary>
        /// Takes a quantity of one definition from wherever it is held.
        /// </summary>
        /// <remarks>Counts the whole container before touching any of it, so asking for
        /// more than is held removes nothing at all rather than emptying the first few
        /// stacks and then failing.</remarks>
        public ItemContainerResult RemoveByDefinition(DefinitionId definitionId, int quantity)
        {
            if (!definitionId.IsValid) return ItemContainerResult.Rejected(ItemContainerRejection.UnknownDefinition);
            if (quantity <= 0) return ItemContainerResult.Rejected(ItemContainerRejection.InvalidQuantity);

            int held = CountOf(definitionId);
            if (held < quantity) return ItemContainerResult.Rejected(ItemContainerRejection.InsufficientQuantity);

            int outstanding = quantity;

            for (int i = 0; i < _slots.Length && outstanding > 0; i++)
            {
                if (_slots[i] == null || _slots[i].DefinitionId != definitionId) continue;

                int inSlot = new ItemSlot(i, _slots[i]).Quantity;
                int take = inSlot < outstanding ? inSlot : outstanding;

                var stackable = _slots[i] as ItemInstance;

                if (stackable == null || take == inSlot) _slots[i] = null;
                else stackable.SetQuantity(inSlot - take);

                outstanding -= take;
            }

            Advance();
            return ItemContainerResult.Accepted(quantity);
        }

        // ---------------------------------------------------------------- move

        /// <summary>
        /// Moves a slot's contents onto another slot.
        /// </summary>
        /// <remarks>
        /// Three outcomes, all explicit: an empty destination takes the contents, a
        /// compatible stack merges (see <see cref="Merge"/>), and anything else swaps.
        /// Swap is chosen rather than refusal because it is what dragging one item onto
        /// another means everywhere a player has seen it, and refusing would leave them
        /// unable to reorder a full bag.
        /// </remarks>
        public ItemContainerResult Move(int from, int to, IDefinitionRegistry<ItemDefinition> items)
        {
            if (!IsValidIndex(from) || !IsValidIndex(to))
                return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);

            if (from == to) return ItemContainerResult.Rejected(ItemContainerRejection.SameSlot);
            if (_slots[from] == null) return ItemContainerResult.Rejected(ItemContainerRejection.SourceEmpty);

            if (_slots[to] == null)
            {
                int moved = new ItemSlot(from, _slots[from]).Quantity;
                _slots[to] = _slots[from];
                _slots[from] = null;
                Advance();
                return ItemContainerResult.Accepted(moved, 0, from, to);
            }

            if (CanStackTogether(from, to, items)) return Merge(from, to, items);

            GameInstance swap = _slots[to];
            _slots[to] = _slots[from];
            _slots[from] = swap;
            Advance();
            return ItemContainerResult.Accepted(0, 0, from, to);
        }

        // --------------------------------------------------------------- split

        /// <summary>
        /// Takes part of a stack into an empty slot.
        /// </summary>
        /// <remarks>The quantity must leave something behind: splitting a whole stack is a
        /// move, and letting it through here would produce a slot holding zero, which
        /// <see cref="ItemInstance"/> refuses to represent anyway.</remarks>
        public ItemContainerResult Split(int from, int quantity, int to,
            IDefinitionRegistry<ItemDefinition> items)
        {
            if (!IsValidIndex(from) || !IsValidIndex(to))
                return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);

            if (from == to) return ItemContainerResult.Rejected(ItemContainerRejection.SameSlot);

            var source = _slots[from] as ItemInstance;
            if (_slots[from] == null) return ItemContainerResult.Rejected(ItemContainerRejection.SourceEmpty);
            if (source == null) return ItemContainerResult.Rejected(ItemContainerRejection.NotStackable);

            if (!items.TryGet(source.DefinitionId, out ItemDefinition definition) || definition == null)
                return ItemContainerResult.Rejected(ItemContainerRejection.UnknownDefinition);

            if (!definition.Stackable) return ItemContainerResult.Rejected(ItemContainerRejection.NotStackable);
            if (quantity <= 0 || quantity >= source.Quantity)
                return ItemContainerResult.Rejected(ItemContainerRejection.InvalidQuantity);

            if (_slots[to] != null) return ItemContainerResult.Rejected(ItemContainerRejection.DestinationOccupied);

            source.SetQuantity(source.Quantity - quantity);
            _slots[to] = new ItemInstance(InstanceId.New(), source.DefinitionId, Owner, quantity);

            Advance();
            return ItemContainerResult.Accepted(quantity, 0, from, to);
        }

        // --------------------------------------------------------------- merge

        /// <summary>
        /// Pours one stack into another, leaving any overflow behind.
        /// </summary>
        /// <remarks>Overflow stays in the source rather than being discarded, which is why
        /// this reports a remainder instead of refusing when the two will not fit in
        /// one.</remarks>
        public ItemContainerResult Merge(int from, int to, IDefinitionRegistry<ItemDefinition> items)
        {
            if (!IsValidIndex(from) || !IsValidIndex(to))
                return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);

            if (from == to) return ItemContainerResult.Rejected(ItemContainerRejection.SameSlot);
            if (_slots[from] == null) return ItemContainerResult.Rejected(ItemContainerRejection.SourceEmpty);
            if (_slots[to] == null) return ItemContainerResult.Rejected(ItemContainerRejection.DestinationOccupied);

            if (!CanStackTogether(from, to, items))
                return ItemContainerResult.Rejected(ItemContainerRejection.NotStackable);

            var source = (ItemInstance)_slots[from];
            var destination = (ItemInstance)_slots[to];
            items.TryGet(destination.DefinitionId, out ItemDefinition definition);

            int room = definition.MaxStackSize - destination.Quantity;
            if (room <= 0) return ItemContainerResult.Rejected(ItemContainerRejection.NotStackable);

            int moved = room < source.Quantity ? room : source.Quantity;
            destination.SetQuantity(destination.Quantity + moved);

            int left = source.Quantity - moved;
            if (left <= 0) _slots[from] = null;
            else source.SetQuantity(left);

            Advance();
            return ItemContainerResult.Accepted(moved, left, from, to);
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// Puts an instance into a specific slot. For transfers, which have already validated.
        /// </summary>
        internal ItemContainerResult PlaceAt(int index, GameInstance instance)
        {
            if (!IsValidIndex(index)) return ItemContainerResult.Rejected(ItemContainerRejection.SlotOutOfRange);
            if (instance == null) return ItemContainerResult.Rejected(ItemContainerRejection.NoItem);
            if (_slots[index] != null) return ItemContainerResult.Rejected(ItemContainerRejection.DestinationOccupied);
            if (IndexOf(instance.InstanceId) >= 0)
                return ItemContainerResult.Rejected(ItemContainerRejection.DuplicateInstance);

            _slots[index] = instance;
            Advance();
            return ItemContainerResult.Accepted(new ItemSlot(index, instance).Quantity, 0, index);
        }

        /// <summary>Lowest empty slot, or -1 when the container is full.</summary>
        public int FirstEmptySlot()
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] == null) return i;
            return -1;
        }

        /// <summary>
        /// Whether two occupied slots hold the same stackable item with room to spare.
        /// </summary>
        /// <remarks>Two stacks of the same definition are interchangeable and may merge;
        /// two swords with different identities are not, which is why equipment and any
        /// non-stackable item answer false here regardless of sharing a definition.</remarks>
        public bool CanStackTogether(int a, int b, IDefinitionRegistry<ItemDefinition> items)
        {
            if (!IsValidIndex(a) || !IsValidIndex(b) || a == b || items == null) return false;

            var first = _slots[a] as ItemInstance;
            var second = _slots[b] as ItemInstance;

            if (first == null || second == null) return false;
            if (first.DefinitionId != second.DefinitionId) return false;

            if (!items.TryGet(first.DefinitionId, out ItemDefinition definition) || definition == null)
                return false;

            return definition.Stackable && definition.MaxStackSize > 1;
        }

        /// <summary>
        /// How many more of a definition would fit, counting stack headroom and empty slots.
        /// </summary>
        /// <remarks>Used by transfers to decide atomically, before anything moves, whether
        /// the whole amount will land.</remarks>
        public int RoomFor(DefinitionId definitionId, IDefinitionRegistry<ItemDefinition> items)
        {
            if (!definitionId.IsValid || items == null) return 0;
            if (!items.TryGet(definitionId, out ItemDefinition definition) || definition == null) return 0;

            int empty = FreeSlots;

            if (!definition.Stackable || definition.MaxStackSize <= 1) return empty;

            long room = (long)empty * definition.MaxStackSize;

            for (int i = 0; i < _slots.Length; i++)
            {
                var occupant = _slots[i] as ItemInstance;
                if (occupant == null || occupant.DefinitionId != definitionId) continue;

                int headroom = definition.MaxStackSize - occupant.Quantity;
                if (headroom > 0) room += headroom;
            }

            return room > int.MaxValue ? int.MaxValue : (int)room;
        }

        /// <summary>Empties every slot. Runtime and test convenience; identities are not reused.</summary>
        public void Clear()
        {
            bool changed = false;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null) continue;
                _slots[i] = null;
                changed = true;
            }

            if (changed) Advance();
        }

        private void Advance() => _revision = _revision.Next();
    }
}
