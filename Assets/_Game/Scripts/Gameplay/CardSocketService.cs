using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a card could not be socketed or taken out.</summary>
    public enum CardSocketRejection
    {
        None = 0,

        /// <summary>No registry, container or equipment was supplied.</summary>
        MissingContext = 1,

        /// <summary>The slot index is outside the container.</summary>
        SlotOutOfRange = 2,

        /// <summary>Nothing is in the slot.</summary>
        SourceEmpty = 3,

        /// <summary>The item in the slot is not a card, or does not resolve.</summary>
        NotACard = 4,

        /// <summary>Content turned the card off.</summary>
        CardDisabled = 5,

        /// <summary>The piece could not be resolved as equipment.</summary>
        UnknownEquipment = 6,

        /// <summary>The piece has no card sockets, or none free.</summary>
        NoFreeSocket = 7,

        /// <summary>That socket already holds a card.</summary>
        SocketOccupied = 8,

        /// <summary>The card does not fit this class of equipment.</summary>
        Incompatible = 9,

        /// <summary>The piece already holds as many of this card as it may.</summary>
        DuplicateNotAllowed = 10,

        /// <summary>The card and the equipment are not both this owner's.</summary>
        NotOwned = 11,

        /// <summary>The equipment changed since the caller last read it.</summary>
        StaleRevision = 12,

        /// <summary>That socket is empty, so there is nothing to take out.</summary>
        SocketEmpty = 13,

        /// <summary>The bag has nowhere to put the card being taken out.</summary>
        NoRoomForCard = 14
    }

    /// <summary>What socketing or removing a card did.</summary>
    public readonly struct CardSocketResult
    {
        private CardSocketResult(bool accepted, CardSocketRejection reason, DefinitionId card,
            InstanceId cardInstance, int socketIndex)
        {
            IsAccepted = accepted;
            Reason = reason;
            Card = card;
            CardInstance = cardInstance;
            SocketIndex = socketIndex;
        }

        public bool IsAccepted { get; }

        public CardSocketRejection Reason { get; }

        public DefinitionId Card { get; }

        /// <summary>The exact copy that moved. The same identity on the way in and out.</summary>
        public InstanceId CardInstance { get; }

        public int SocketIndex { get; }

        public static CardSocketResult Accepted(DefinitionId card, InstanceId cardInstance,
            int socketIndex)
        {
            return new CardSocketResult(true, CardSocketRejection.None, card, cardInstance,
                socketIndex);
        }

        public static CardSocketResult Rejected(CardSocketRejection reason,
            DefinitionId card = default)
        {
            return new CardSocketResult(false, reason, card, default, -1);
        }

        public override string ToString()
        {
            return IsAccepted ? Card + " -> socket " + SocketIndex : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Putting cards into equipment and taking them out again.
    /// </summary>
    /// <remarks>
    /// <b>A card is moved, never copied and never destroyed.</b> Insertion takes the exact
    /// owned copy out of the bag and records its identity in the socket; removal puts that
    /// same copy back. At no point does the card exist in two places, and no path here
    /// deletes one -- a player who sockets a card can always get it back.
    ///
    /// <b>Its own rules, not the status stone's.</b> Cards use their own sockets, their own
    /// capacity and <see cref="CardDefinition.Fits"/>. Phase 09's enchanting is untouched:
    /// this service never reads or writes <see cref="EquipmentInstance.Enchants"/>, so a
    /// card cannot consume a stone's socket and stone semantics cannot drift because a card
    /// rule changed. There is also no success chance and no failure behaviour, because
    /// inserting a card is certain -- importing those from
    /// <see cref="StatusStoneConfig"/> would have given cards the ability to be destroyed on
    /// insertion.
    ///
    /// <b>Validate fully, then mutate.</b> Every check runs before the container or the
    /// equipment is touched, so a refusal leaves both exactly as they were and needs no
    /// rollback to be safe.
    ///
    /// <b>Nothing here knows a card.</b> No <see cref="DefinitionId"/> is compared to a
    /// literal; compatibility is authored against classes of equipment.
    /// </remarks>
    public static class CardSocketService
    {
        /// <summary>Everything a socket operation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<CardDefinition> cards,
                IDefinitionRegistry<RarityDefinition> rarities = null,
                OwnerId owner = default)
            {
                Items = items;
                Cards = cards;
                Rarities = rarities;
                Owner = owner;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            /// <summary>Where the card's behaviour is authored.</summary>
            public IDefinitionRegistry<CardDefinition> Cards { get; }

            /// <summary>Only needed if a tier ever widens a piece. Reserved, unused today.</summary>
            public IDefinitionRegistry<RarityDefinition> Rarities { get; }

            /// <summary>Who is acting. Invalid skips the ownership check.</summary>
            public OwnerId Owner { get; }

            public bool IsUsable => Items != null && Cards != null;
        }

        /// <summary>
        /// How many cards a piece can hold.
        /// </summary>
        /// <remarks>The piece's authored sockets, and nothing else. Kept as a method so a
        /// tier bonus can be added in one place later, the way
        /// <see cref="EquipmentModifierResolver.EnchantCapacity"/> already handles stones.</remarks>
        public static int CardCapacity(EquipmentDefinition equipment)
        {
            if (equipment == null) return 0;
            return equipment.CardSlots < 0 ? 0 : equipment.CardSlots;
        }

        /// <summary>
        /// Sockets a card from a bag into a piece of equipment.
        /// </summary>
        /// <param name="inventory">Where the card is.</param>
        /// <param name="slotIndex">Which slot holds it.</param>
        /// <param name="equipment">The piece receiving it.</param>
        /// <param name="context">Registries and the acting owner.</param>
        /// <param name="socketIndex">Which socket, or -1 for the lowest free one.</param>
        /// <param name="expectedRevision">
        /// The revision the caller last saw. Supply it to refuse an operation built against
        /// a stale view; leave it null to skip the check. Nullable because Revision.Initial is
        /// itself the default value, so no sentinel could distinguish "not supplied" from
        /// "expects the very first revision".
        /// </param>
        public static CardSocketResult TryInsert(ItemContainerState inventory, int slotIndex,
            EquipmentInstance equipment, in Context context, int socketIndex = -1,
            Revision? expectedRevision = null)
        {
            if (inventory == null || equipment == null || !context.IsUsable)
                return CardSocketResult.Rejected(CardSocketRejection.MissingContext);

            if (!inventory.IsValidIndex(slotIndex))
                return CardSocketResult.Rejected(CardSocketRejection.SlotOutOfRange);

            ItemSlot slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty) return CardSocketResult.Rejected(CardSocketRejection.SourceEmpty);

            GameInstance held = slot.Content;
            DefinitionId cardId = held.DefinitionId;

            // The item has to be a card, by its authored category rather than by its id.
            ItemDefinition item;
            if (!context.Items.TryGet(cardId, out item) || item == null
                || item.Category != ItemCategory.Card)
            {
                return CardSocketResult.Rejected(CardSocketRejection.NotACard, cardId);
            }

            CardDefinition card;
            if (!context.Cards.TryGet(cardId, out card) || card == null)
                return CardSocketResult.Rejected(CardSocketRejection.NotACard, cardId);

            if (!card.Enabled)
                return CardSocketResult.Rejected(CardSocketRejection.CardDisabled, cardId);

            if (context.Owner.IsValid
                && (held.Owner != context.Owner || equipment.Owner != context.Owner))
            {
                return CardSocketResult.Rejected(CardSocketRejection.NotOwned, cardId);
            }

            if (expectedRevision.HasValue && equipment.Revision != expectedRevision.Value)
                return CardSocketResult.Rejected(CardSocketRejection.StaleRevision, cardId);

            EquipmentDefinition piece;
            if (!TryResolveEquipment(equipment, context, out piece))
                return CardSocketResult.Rejected(CardSocketRejection.UnknownEquipment, cardId);

            if (!card.Fits(piece))
                return CardSocketResult.Rejected(CardSocketRejection.Incompatible, cardId);

            if (equipment.CountOfCard(cardId) >= card.MaxPerEquipment)
                return CardSocketResult.Rejected(CardSocketRejection.DuplicateNotAllowed, cardId);

            int capacity = CardCapacity(piece);

            int target = socketIndex >= 0 ? socketIndex : equipment.FirstFreeCardSocket(capacity);

            if (target < 0 || target >= capacity)
                return CardSocketResult.Rejected(CardSocketRejection.NoFreeSocket, cardId);

            if (equipment.IsCardSocketOccupied(target))
                return CardSocketResult.Rejected(CardSocketRejection.SocketOccupied, cardId);

            // ---- everything is resolved and nothing below can fail ---------------------

            InstanceId identity = held.InstanceId;

            inventory.RemoveAt(slotIndex, 1);
            equipment.AddCard(new EquipmentCardSocket(cardId, target, identity));

            return CardSocketResult.Accepted(cardId, identity, target);
        }

        /// <summary>
        /// Takes a card back out and returns it to a bag.
        /// </summary>
        /// <remarks>
        /// <b>Extraction, not destruction.</b> The card goes back into the container as a
        /// normal owned item. Nothing here can consume it, and there is no path that leaves
        /// a player with an empty socket and no card.
        ///
        /// <b>Room is checked before the socket is emptied.</b> A full bag refuses the
        /// removal outright rather than pulling the card out and discovering there is
        /// nowhere to put it -- which is the exact shape of accident that loses a player's
        /// card.
        ///
        /// <b>Cost is deferred, not faked.</b> Real games usually charge for this. No cost
        /// is authored or taken in this phase; adding one is a definition field and a check
        /// here, and nothing below pretends a charge happened.
        /// </remarks>
        public static CardSocketResult TryRemove(EquipmentInstance equipment, int socketIndex,
            ItemContainerState inventory, in Context context, Revision? expectedRevision = null)
        {
            if (equipment == null || inventory == null || !context.IsUsable)
                return CardSocketResult.Rejected(CardSocketRejection.MissingContext);

            if (context.Owner.IsValid && equipment.Owner != context.Owner)
                return CardSocketResult.Rejected(CardSocketRejection.NotOwned);

            if (expectedRevision.HasValue && equipment.Revision != expectedRevision.Value)
                return CardSocketResult.Rejected(CardSocketRejection.StaleRevision);

            EquipmentCardSocket socket;
            if (!TryFindSocket(equipment, socketIndex, out socket))
                return CardSocketResult.Rejected(CardSocketRejection.SocketEmpty);

            // Somewhere to land, established before anything is taken apart.
            if (inventory.RoomFor(socket.Card, context.Items) < 1)
                return CardSocketResult.Rejected(CardSocketRejection.NoRoomForCard, socket.Card);

            // ---- everything is resolved and nothing below can fail ---------------------

            EquipmentCardSocket removed;
            equipment.RemoveCardAt(socketIndex, out removed);

            // The same identity that went in, so the copy a player gets back is the copy
            // they socketed rather than a new one wearing its name.
            InstanceId identity = removed.CardInstance.IsValid
                ? removed.CardInstance
                : InstanceId.New();

            var restored = new ItemInstance(identity, removed.Card, equipment.Owner, 1);

            inventory.Add(restored, context.Items);

            return CardSocketResult.Accepted(removed.Card, identity, socketIndex);
        }

        /// <summary>
        /// Appends everything the cards in a piece contribute.
        /// </summary>
        /// <remarks>Collected, never computed. Read off each card's definition at resolve
        /// time, so re-authoring a card updates every piece already carrying it -- the same
        /// rule the enchant resolver follows.</remarks>
        public static void CollectModifiers(EquipmentInstance worn,
            IDefinitionRegistry<CardDefinition> cards, List<StatModifier> into)
        {
            if (into == null || worn == null || cards == null) return;

            IReadOnlyList<EquipmentCardSocket> sockets = worn.Cards;

            for (int i = 0; i < sockets.Count; i++)
            {
                EquipmentCardSocket socket = sockets[i];
                if (!socket.IsValid) continue;

                CardDefinition card;
                if (!cards.TryGet(socket.Card, out card) || card == null) continue;

                StatModifier[] modifiers = card.StatModifiers;

                for (int m = 0; m < modifiers.Length; m++) into.Add(modifiers[m]);
            }
        }

        /// <summary>
        /// Appends the conditional effects the cards in a piece contribute.
        /// </summary>
        /// <remarks>Reported so a tooltip can show them and a later combat phase can consume
        /// them. See <see cref="CardEffect"/>: no damage formula reads these yet, and nothing
        /// here pretends one does.</remarks>
        public static void CollectEffects(EquipmentInstance worn,
            IDefinitionRegistry<CardDefinition> cards, List<CardEffect> into)
        {
            if (into == null || worn == null || cards == null) return;

            IReadOnlyList<EquipmentCardSocket> sockets = worn.Cards;

            for (int i = 0; i < sockets.Count; i++)
            {
                EquipmentCardSocket socket = sockets[i];
                if (!socket.IsValid) continue;

                CardDefinition card;
                if (!cards.TryGet(socket.Card, out card) || card == null) continue;

                CardEffect[] effects = card.Effects;

                for (int e = 0; e < effects.Length; e++)
                {
                    if (effects[e].IsValid) into.Add(effects[e]);
                }
            }
        }

        private static bool TryResolveEquipment(EquipmentInstance instance, in Context context,
            out EquipmentDefinition equipment)
        {
            equipment = null;

            ItemDefinition definition;
            if (!context.Items.TryGet(instance.DefinitionId, out definition)) return false;

            equipment = definition as EquipmentDefinition;
            return equipment != null;
        }

        private static bool TryFindSocket(EquipmentInstance equipment, int socketIndex,
            out EquipmentCardSocket socket)
        {
            socket = default;

            IReadOnlyList<EquipmentCardSocket> sockets = equipment.Cards;

            for (int i = 0; i < sockets.Count; i++)
            {
                if (sockets[i].SocketIndex != socketIndex) continue;

                socket = sockets[i];
                return true;
            }

            return false;
        }
    }
}
