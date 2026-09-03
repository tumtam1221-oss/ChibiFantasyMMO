using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a planned ownership move could not be made.</summary>
    public enum TransferRejection
    {
        None = 0,

        /// <summary>No container, no registry or no instances were supplied.</summary>
        MissingContext = 1,

        /// <summary>The source container does not hold one of the instances.</summary>
        NotHeld = 2,

        /// <summary>The destination has nowhere to put what would arrive.</summary>
        DestinationFull = 3,

        /// <summary>One of the instances may not change hands. See <see cref="ItemTransferBlock"/>.</summary>
        Blocked = 4,

        /// <summary>The same instance appears twice in one move.</summary>
        DuplicateInstance = 5
    }

    /// <summary>
    /// One side of a planned exchange.
    /// </summary>
    /// <remarks>
    /// Everything one container gives away and everything it will receive. Both are needed
    /// together because a bag that is full can still trade: its own items leave before the
    /// incoming ones arrive, and a capacity check that ignored the outgoing side would refuse
    /// a perfectly good swap.
    /// </remarks>
    public sealed class TransferLeg
    {
        private readonly List<GameInstance> _outgoing = new List<GameInstance>();
        private readonly List<GameInstance> _incoming = new List<GameInstance>();

        public TransferLeg(ItemContainerState container, OwnerId owner)
        {
            Container = container;
            Owner = owner;
        }

        public ItemContainerState Container { get; }

        /// <summary>Who owns what is in the container, and who will own what arrives.</summary>
        public OwnerId Owner { get; }

        public IReadOnlyList<GameInstance> Outgoing => _outgoing;

        public IReadOnlyList<GameInstance> Incoming => _incoming;

        public void Give(GameInstance instance)
        {
            if (instance != null) _outgoing.Add(instance);
        }

        /// <summary>Called by the planner; the other side's outgoing set is this side's incoming.</summary>
        internal void Receive(GameInstance instance)
        {
            if (instance != null) _incoming.Add(instance);
        }

        internal void ClearIncoming()
        {
            _incoming.Clear();
        }
    }

    /// <summary>
    /// Moving owned objects between containers. The only thing that changes ownership.
    /// </summary>
    /// <remarks>
    /// <b>One seam, used by everything.</b> Trade and player shops both come through here.
    /// There is no <c>TradeItemTransfer</c> and no <c>ShopItemTransfer</c>, so the rules
    /// about capacity, ownership and revisions are written once and cannot diverge -- and an
    /// architecture test asserts no second implementation exists.
    ///
    /// <b>Plan, then apply.</b> <see cref="Plan"/> simulates the whole exchange against a
    /// copy of each container's slots and writes nothing. Only if every leg fits does
    /// <see cref="Apply"/> run, and by then nothing it does can fail. That is what makes
    /// "never remove from A and then discover B is full" true by construction rather than by
    /// care.
    ///
    /// <b>The simulation is the real placement rule, not an approximation.</b> It tops up
    /// existing stacks in slot order and then fills the lowest empty slots, exactly as
    /// <see cref="ItemContainerState.Add"/> does, reading stack sizes from the same authored
    /// <see cref="ItemDefinition"/>. A cheaper "count the free slots" check would pass an
    /// exchange that then failed halfway.
    ///
    /// <b>Not a database transaction, and it does not claim to be.</b> Everything here
    /// happens in one process against objects already in memory, so the mutation boundary
    /// cannot be interrupted. A future server maps one <see cref="Apply"/> onto one database
    /// transaction; until then, no rollback exists because none is needed and none is
    /// pretended.
    ///
    /// <b>Exactly one revision per moved object.</b> Ownership is reassigned once, through
    /// <see cref="GameInstance.SetOwner"/>, and the before and after revisions are recorded
    /// in an <see cref="ItemTransactionEntry"/> so an auditor can see that one step happened.
    /// </remarks>
    public static class ItemOwnershipTransfer
    {
        /// <summary>What a planned exchange would do, and why it cannot.</summary>
        public readonly struct PlanResult
        {
            public PlanResult(TransferRejection reason, ItemTransferBlock block,
                InstanceId offending)
            {
                Reason = reason;
                Block = block;
                Offending = offending;
            }

            public TransferRejection Reason { get; }

            /// <summary>The transfer rule that refused, when <see cref="Reason"/> is Blocked.</summary>
            public ItemTransferBlock Block { get; }

            /// <summary>Which instance caused the refusal, for a message a player can act on.</summary>
            public InstanceId Offending { get; }

            public bool IsAccepted => Reason == TransferRejection.None;

            public static PlanResult Accepted => new PlanResult(TransferRejection.None,
                ItemTransferBlock.None, default);

            public static PlanResult Rejected(TransferRejection reason,
                InstanceId offending = default,
                ItemTransferBlock block = ItemTransferBlock.None)
            {
                return new PlanResult(reason, block, offending);
            }

            public override string ToString()
            {
                return IsAccepted
                    ? "transfer plan ok"
                    : "rejected: " + Reason + (Block == ItemTransferBlock.None
                        ? string.Empty : " (" + Block + ")");
            }
        }

        /// <summary>
        /// Checks a two-sided exchange without moving anything.
        /// </summary>
        /// <param name="a">One side. Its outgoing set is filled by the caller.</param>
        /// <param name="b">The other side.</param>
        /// <param name="items">Where stack rules are read from.</param>
        /// <param name="rules">
        /// The transfer rules to apply to each outgoing object, or null to skip them. A trade
        /// supplies them per side; a shop supplies its own, because who owns what differs.
        /// </param>
        /// <remarks>
        /// Fills each side's incoming set from the other's outgoing set, so a caller states
        /// only what each side gives. A one-sided move -- a purchase, a listing being
        /// reclaimed -- is this with one empty outgoing set.
        /// </remarks>
        public static PlanResult Plan(TransferLeg a, TransferLeg b,
            IDefinitionRegistry<ItemDefinition> items,
            ItemTransferRules.Context? rulesForA = null,
            ItemTransferRules.Context? rulesForB = null)
        {
            if (a == null || b == null || items == null)
                return PlanResult.Rejected(TransferRejection.MissingContext);

            a.ClearIncoming();
            b.ClearIncoming();

            for (int i = 0; i < a.Outgoing.Count; i++) b.Receive(a.Outgoing[i]);
            for (int i = 0; i < b.Outgoing.Count; i++) a.Receive(b.Outgoing[i]);

            PlanResult side = PlanSide(a, items, rulesForA);
            if (!side.IsAccepted) return side;

            side = PlanSide(b, items, rulesForB);
            if (!side.IsAccepted) return side;

            return PlanResult.Accepted;
        }

        /// <summary>
        /// Whether a container could take a set of objects that are in no container.
        /// </summary>
        /// <remarks>
        /// The one-sided case where the source is not a bag at all: a shop listing holds its
        /// item in escrow, and a purchase or a cancellation has to know the destination can
        /// take it before the listing is settled. Expressed against the same simulation the
        /// two-sided plan uses, so stack top-up behaves exactly as it will when the object
        /// actually arrives.
        /// </remarks>
        public static bool CanReceive(ItemContainerState destination,
            IReadOnlyList<GameInstance> incoming, IDefinitionRegistry<ItemDefinition> items)
        {
            if (destination == null || items == null) return false;
            if (incoming == null || incoming.Count == 0) return true;

            var leg = new TransferLeg(destination, destination.Owner);

            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i] == null) return false;
                leg.Receive(incoming[i]);
            }

            return Simulate(leg, items);
        }

        /// <summary>Whether a container could take one object held outside any container.</summary>
        public static bool CanReceive(ItemContainerState destination, GameInstance incoming,
            IDefinitionRegistry<ItemDefinition> items)
        {
            return CanReceive(destination, new[] { incoming }, items);
        }

        private static PlanResult PlanSide(TransferLeg leg,
            IDefinitionRegistry<ItemDefinition> items, ItemTransferRules.Context? rules)
        {
            if (leg.Container == null && leg.Outgoing.Count == 0 && leg.Incoming.Count == 0)
            {
                return PlanResult.Accepted;
            }

            if (leg.Container == null) return PlanResult.Rejected(TransferRejection.MissingContext);

            var seen = new HashSet<InstanceId>();

            for (int i = 0; i < leg.Outgoing.Count; i++)
            {
                GameInstance instance = leg.Outgoing[i];

                if (instance == null)
                    return PlanResult.Rejected(TransferRejection.MissingContext);

                if (!seen.Add(instance.InstanceId))
                {
                    return PlanResult.Rejected(TransferRejection.DuplicateInstance,
                        instance.InstanceId);
                }

                // It has to actually be there. An offer naming something the bag no longer
                // holds is the shape a stale client produces.
                if (leg.Container.IndexOf(instance.InstanceId) < 0)
                {
                    return PlanResult.Rejected(TransferRejection.NotHeld, instance.InstanceId);
                }

                if (!rules.HasValue) continue;

                ItemTransferBlock block = ItemTransferRules.CanTransfer(instance, leg.Owner,
                    rules.Value);

                if (block != ItemTransferBlock.None)
                {
                    return PlanResult.Rejected(TransferRejection.Blocked, instance.InstanceId,
                        block);
                }
            }

            return Simulate(leg, items)
                ? PlanResult.Accepted
                : PlanResult.Rejected(TransferRejection.DestinationFull);
        }

        /// <summary>
        /// Replays the exchange against a copy of the container's slots.
        /// </summary>
        /// <remarks>
        /// Outgoing leaves first, then incoming arrives under the same placement rule the
        /// real container uses. Nothing real is touched: the simulation is two small arrays.
        /// </remarks>
        private static bool Simulate(TransferLeg leg, IDefinitionRegistry<ItemDefinition> items)
        {
            int capacity = leg.Container.Capacity;

            var slotDefinition = new DefinitionId[capacity];
            var slotQuantity = new int[capacity];

            for (int i = 0; i < capacity; i++)
            {
                ItemSlot slot = leg.Container.GetSlot(i);
                if (slot.IsEmpty) continue;

                slotDefinition[i] = slot.Content.DefinitionId;
                slotQuantity[i] = slot.Quantity;
            }

            for (int i = 0; i < leg.Outgoing.Count; i++)
            {
                int index = leg.Container.IndexOf(leg.Outgoing[i].InstanceId);
                if (index < 0 || index >= capacity) return false;

                slotDefinition[index] = DefinitionId.None;
                slotQuantity[index] = 0;
            }

            for (int i = 0; i < leg.Incoming.Count; i++)
            {
                if (!Place(leg.Incoming[i], items, slotDefinition, slotQuantity)) return false;
            }

            return true;
        }

        private static bool Place(GameInstance instance, IDefinitionRegistry<ItemDefinition> items,
            DefinitionId[] slotDefinition, int[] slotQuantity)
        {
            ItemDefinition definition;
            if (!items.TryGet(instance.DefinitionId, out definition) || definition == null)
            {
                return false;
            }

            var stackable = instance as ItemInstance;

            if (stackable == null || !definition.Stackable || definition.MaxStackSize <= 1)
            {
                return TakeEmptySlot(instance.DefinitionId, 1, slotDefinition, slotQuantity);
            }

            int outstanding = stackable.Quantity;
            int max = definition.MaxStackSize;

            // Top up existing stacks in slot order, exactly as the container does.
            for (int i = 0; i < slotDefinition.Length && outstanding > 0; i++)
            {
                if (slotDefinition[i] != instance.DefinitionId) continue;

                int headroom = max - slotQuantity[i];
                if (headroom <= 0) continue;

                int moved = headroom < outstanding ? headroom : outstanding;
                slotQuantity[i] += moved;
                outstanding -= moved;
            }

            while (outstanding > 0)
            {
                int take = outstanding < max ? outstanding : max;

                if (!TakeEmptySlot(instance.DefinitionId, take, slotDefinition, slotQuantity))
                {
                    return false;
                }

                outstanding -= take;
            }

            return true;
        }

        private static bool TakeEmptySlot(DefinitionId definition, int quantity,
            DefinitionId[] slotDefinition, int[] slotQuantity)
        {
            for (int i = 0; i < slotDefinition.Length; i++)
            {
                if (slotDefinition[i].IsValid) continue;

                slotDefinition[i] = definition;
                slotQuantity[i] = quantity;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Carries out an exchange whose plan already passed.
        /// </summary>
        /// <remarks>
        /// <b>The mutation boundary.</b> Everything leaves both containers first, then
        /// ownership is reassigned, then everything is added. Splitting it that way is what
        /// makes the middle step safe: by the time anything is added, every slot that will be
        /// needed is already free, and <see cref="Plan"/> established that they suffice.
        ///
        /// Returns false and writes nothing if it is called without a passing plan, rather
        /// than half-applying. A caller that reaches this state has a bug; it should not also
        /// have a corrupted inventory.
        /// </remarks>
        public static bool Apply(TransferLeg a, TransferLeg b,
            IDefinitionRegistry<ItemDefinition> items, List<ItemTransactionEntry> into)
        {
            if (a == null || b == null || items == null) return false;

            // Re-run the simulation against the current state. Cheap, and it closes the gap
            // between planning and applying for a caller that did something in between.
            if (!Simulate(a, items) || !Simulate(b, items)) return false;

            Detach(a);
            Detach(b);

            AttachAll(a, b.Outgoing, items, into);
            AttachAll(b, a.Outgoing, items, into);

            return true;
        }

        private static void Detach(TransferLeg leg)
        {
            if (leg.Container == null) return;

            for (int i = 0; i < leg.Outgoing.Count; i++)
            {
                int index = leg.Container.IndexOf(leg.Outgoing[i].InstanceId);
                if (index < 0) continue;

                int quantity = leg.Container.GetSlot(index).Quantity;
                leg.Container.RemoveAt(index, quantity);
            }
        }

        private static void AttachAll(TransferLeg destination,
            IReadOnlyList<GameInstance> instances, IDefinitionRegistry<ItemDefinition> items,
            List<ItemTransactionEntry> into)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                GameInstance instance = instances[i];

                OwnerId from = instance.Owner;
                Revision before = instance.Revision;

                // Exactly one mutation per moved object: ownership. The revision moves once,
                // and the entry below records both sides of that single step.
                instance.SetOwner(destination.Owner);

                if (destination.Container != null) destination.Container.Add(instance, items);

                if (into == null) continue;

                var stackable = instance as ItemInstance;

                into.Add(new ItemTransactionEntry(instance.InstanceId, instance.DefinitionId,
                    from, destination.Owner, stackable == null ? 1 : stackable.Quantity,
                    before, instance.Revision));
            }
        }

        /// <summary>
        /// Moves objects one way only.
        /// </summary>
        /// <remarks>What a purchase and a listing cancellation are: one side gives, the other
        /// receives, and nothing comes back. Expressed as a two-sided plan with one empty
        /// side so there is still only one implementation of the rules.
        /// </remarks>
        public static PlanResult PlanOneWay(ItemContainerState from, OwnerId fromOwner,
            ItemContainerState to, OwnerId toOwner, IReadOnlyList<GameInstance> instances,
            IDefinitionRegistry<ItemDefinition> items,
            ItemTransferRules.Context? rules, out TransferLeg source, out TransferLeg destination)
        {
            source = new TransferLeg(from, fromOwner);
            destination = new TransferLeg(to, toOwner);

            if (instances != null)
            {
                for (int i = 0; i < instances.Count; i++) source.Give(instances[i]);
            }

            return Plan(source, destination, items, rules, null);
        }
    }
}
