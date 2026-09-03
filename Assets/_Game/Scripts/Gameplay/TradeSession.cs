using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a trade stands.</summary>
    /// <remarks>
    /// Closed technical category, and a one-way progression: a session moves forward and
    /// never back into an earlier stage. That is what stops a completed trade being
    /// completed twice.
    /// </remarks>
    public enum TradeSessionState
    {
        /// <summary>Offers may be changed.</summary>
        Open = 0,

        /// <summary>Both sides confirmed. Offers are locked while it commits.</summary>
        Confirming = 1,

        /// <summary>Applied. Terminal.</summary>
        Completed = 2,

        /// <summary>Called off by a participant. Terminal.</summary>
        Cancelled = 3,

        /// <summary>Refused at final validation. Terminal.</summary>
        Failed = 4
    }

    /// <summary>
    /// One item on the table.
    /// </summary>
    /// <remarks>
    /// <b>A reference, never a copy.</b> The exact <see cref="InstanceId"/> plus the
    /// <see cref="Revision"/> it was offered at. Copying the item into the session would
    /// give it two homes, and the whole trade would then be an exercise in keeping them
    /// agreeing.
    ///
    /// The revision is what makes a stale offer detectable: if the sword was enhanced,
    /// socketed or re-owned between being offered and the trade committing, the number no
    /// longer matches and final validation refuses.
    ///
    /// Flat because it has to persist: one row of a future <c>trade_offer_item</c> table.
    /// </remarks>
    public readonly struct TradeOfferItem
    {
        public TradeOfferItem(InstanceId instance, DefinitionId definition, int quantity,
            Revision offeredRevision)
        {
            Instance = instance;
            Definition = definition;
            Quantity = quantity;
            OfferedRevision = offeredRevision;
        }

        public InstanceId Instance { get; }

        public DefinitionId Definition { get; }

        public int Quantity { get; }

        /// <summary>The revision the item carried when it was put on the table.</summary>
        public Revision OfferedRevision { get; }

        public bool IsValid => Instance.IsValid && Definition.IsValid;

        public override string ToString()
        {
            return Definition + " x" + Quantity + " (" + Instance + ")";
        }
    }

    /// <summary>
    /// One currency amount on the table.
    /// </summary>
    /// <remarks>Flat: one row of a future <c>trade_offer_currency</c> table. Several
    /// currencies may be offered at once, because a wallet holds several.</remarks>
    public readonly struct TradeOfferCurrency
    {
        public TradeOfferCurrency(DefinitionId currency, int amount)
        {
            Currency = currency;
            Amount = amount;
        }

        public DefinitionId Currency { get; }

        public int Amount { get; }

        public bool IsValid => Currency.IsValid && Amount > 0;

        public override string ToString()
        {
            return Amount + " " + Currency;
        }
    }

    /// <summary>
    /// What one side has put up.
    /// </summary>
    /// <remarks>
    /// <b>Acceptance lives here, beside the offer it applies to.</b> That adjacency is the
    /// point: <see cref="Touch"/> is called by every mutation and clears
    /// <see cref="HasAccepted"/>, so an acceptance can only ever refer to the offer as it
    /// stood when it was given. Storing the flag anywhere else would let the two drift, and
    /// drift is precisely how a player ends up having agreed to something they never saw.
    /// </remarks>
    public sealed class TradeOffer
    {
        private readonly List<TradeOfferItem> _items = new List<TradeOfferItem>();
        private readonly List<TradeOfferCurrency> _currency = new List<TradeOfferCurrency>();

        public TradeOffer(CharacterId character, OwnerId owner)
        {
            Character = character;
            Owner = owner;
        }

        public CharacterId Character { get; }

        public OwnerId Owner { get; }

        public IReadOnlyList<TradeOfferItem> Items => _items;

        public IReadOnlyList<TradeOfferCurrency> Currency => _currency;

        /// <summary>Whether this side has agreed to the offers as they currently stand.</summary>
        public bool HasAccepted { get; private set; }

        public bool IsEmpty => _items.Count == 0 && _currency.Count == 0;

        public int ItemCount => _items.Count;

        public bool Contains(InstanceId instance)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Instance == instance) return true;
            }

            return false;
        }

        /// <summary>Puts an item up. Refuses a duplicate, so one item cannot be offered twice.</summary>
        public bool TryAddItem(TradeOfferItem item)
        {
            if (!item.IsValid || Contains(item.Instance)) return false;

            _items.Add(item);
            return true;
        }

        public bool TryRemoveItem(InstanceId instance)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Instance != instance) continue;

                _items.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sets how much of one currency is offered.
        /// </summary>
        /// <remarks>Replaces rather than adds, so offering ten then twenty means twenty. An
        /// additive setter is how a double-clicked button becomes an unintended fortune.
        /// Zero removes the entry.</remarks>
        public bool TrySetCurrency(DefinitionId currency, int amount)
        {
            if (!currency.IsValid || amount < 0) return false;

            for (int i = 0; i < _currency.Count; i++)
            {
                if (_currency[i].Currency != currency) continue;

                if (_currency[i].Amount == amount) return false;

                if (amount == 0) _currency.RemoveAt(i);
                else _currency[i] = new TradeOfferCurrency(currency, amount);

                return true;
            }

            if (amount == 0) return false;

            _currency.Add(new TradeOfferCurrency(currency, amount));
            return true;
        }

        public int AmountOf(DefinitionId currency)
        {
            for (int i = 0; i < _currency.Count; i++)
            {
                if (_currency[i].Currency == currency) return _currency[i].Amount;
            }

            return 0;
        }

        /// <summary>Records agreement to the offer as it stands.</summary>
        public bool Accept()
        {
            if (HasAccepted) return false;

            HasAccepted = true;
            return true;
        }

        /// <summary>
        /// Withdraws agreement because something changed.
        /// </summary>
        /// <remarks>Called by the session on every mutation of <em>either</em> offer. A
        /// change on one side invalidates agreement on both, because each player agreed to
        /// the pair.</remarks>
        public bool Touch()
        {
            if (!HasAccepted) return false;

            HasAccepted = false;
            return true;
        }
    }

    /// <summary>
    /// One trade between two players.
    /// </summary>
    /// <remarks>
    /// <b>It records; it does not decide.</b> <see cref="TradeService"/> owns every rule --
    /// who may change what, whether an item is transferable, whether the commit may run.
    /// This holds the two offers, the state and the revision.
    ///
    /// <b>Any change resets both acceptances.</b> That is enforced here rather than left to
    /// callers, in <see cref="MarkChanged"/>, because it is the rule most easily forgotten
    /// and the most damaging to forget: a player who accepted a sword and received a rock
    /// has been robbed by a missing line.
    ///
    /// <b>It completes once.</b> <see cref="TrySetState"/> refuses to leave a terminal state,
    /// so a commit that arrives twice finds the session already finished.
    ///
    /// Flat because it has to persist: <c>trade_session</c> plus <c>trade_offer_item</c> and
    /// <c>trade_offer_currency</c> rows.
    /// </remarks>
    public sealed class TradeSession : IPersistentState
    {
        private TradeSessionState _state = TradeSessionState.Open;
        private Revision _revision;

        public TradeSession(InstanceId tradeId, CharacterId a, OwnerId ownerA, CharacterId b,
            OwnerId ownerB, long createdTicks = 0L, long expiresTicks = 0L)
        {
            TradeId = tradeId;
            OfferA = new TradeOffer(a, ownerA);
            OfferB = new TradeOffer(b, ownerB);
            CreatedTicks = createdTicks;
            ExpiresTicks = expiresTicks;
            _revision = Revision.Initial;
        }

        public InstanceId TradeId { get; }

        public TradeOffer OfferA { get; }

        public TradeOffer OfferB { get; }

        public long CreatedTicks { get; }

        /// <summary>When it lapses. Zero means it does not expire on its own.</summary>
        public long ExpiresTicks { get; }

        public TradeSessionState State => _state;

        public Revision Revision => _revision;

        public bool IsOpen => _state == TradeSessionState.Open;

        public bool IsTerminal => _state == TradeSessionState.Completed
            || _state == TradeSessionState.Cancelled
            || _state == TradeSessionState.Failed;

        /// <summary>Whether both sides have agreed to the offers as they stand.</summary>
        public bool BothAccepted => OfferA.HasAccepted && OfferB.HasAccepted;

        public bool HasExpired(long nowTicks)
        {
            return ExpiresTicks > 0L && nowTicks >= ExpiresTicks;
        }

        /// <summary>The offer belonging to a character, or null if they are not in this trade.</summary>
        public TradeOffer OfferOf(CharacterId character)
        {
            if (OfferA.Character == character) return OfferA;
            if (OfferB.Character == character) return OfferB;
            return null;
        }

        /// <summary>The other side's offer.</summary>
        public TradeOffer CounterpartyOf(CharacterId character)
        {
            if (OfferA.Character == character) return OfferB;
            if (OfferB.Character == character) return OfferA;
            return null;
        }

        public bool Involves(CharacterId character)
        {
            return OfferA.Character == character || OfferB.Character == character;
        }

        /// <summary>
        /// Records that an offer changed, clearing both acceptances.
        /// </summary>
        /// <remarks>The rule that makes acceptance meaningful. Both sides are cleared, not
        /// only the one that changed, because each player agreed to the pair of offers rather
        /// than to their own.</remarks>
        public void MarkChanged()
        {
            OfferA.Touch();
            OfferB.Touch();
            _revision = _revision.Next();
        }

        /// <summary>
        /// Moves the session on.
        /// </summary>
        /// <remarks>Refuses to leave a terminal state and refuses to stand still, so a
        /// duplicate commit finds the trade already completed and does nothing.</remarks>
        public bool TrySetState(TradeSessionState state)
        {
            if (IsTerminal || _state == state) return false;

            _state = state;
            _revision = _revision.Next();
            return true;
        }
    }
}
