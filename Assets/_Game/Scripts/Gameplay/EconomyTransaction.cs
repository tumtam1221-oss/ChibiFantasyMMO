using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a change to the economy came from.</summary>
    /// <remarks>
    /// Closed technical category, and an audit field rather than a behaviour switch:
    /// <see cref="EconomyService"/> does not branch on it, and a test asserts that. It
    /// exists so an operator reading the ledger can tell a quest payout from a player trade
    /// without inferring it from amounts.
    ///
    /// <see cref="AdminAdjustment"/> is present so a future tool has an honest label to
    /// write. It is never reachable from a client request.
    /// </remarks>
    public enum EconomySource
    {
        Unknown = 0,
        MonsterLoot = 1,
        QuestReward = 2,
        NpcShop = 3,
        PlayerTrade = 4,
        PlayerShop = 5,
        AdminAdjustment = 6,
        SystemReward = 7,
        Other = 8
    }

    /// <summary>What kind of change a transaction made.</summary>
    public enum EconomyTransactionType
    {
        None = 0,
        Credit = 1,
        Debit = 2,
        Transfer = 3,

        /// <summary>Currency and items moving together, as a trade or a purchase does.</summary>
        Exchange = 4
    }

    /// <summary>
    /// One character's side of one currency movement.
    /// </summary>
    /// <remarks>
    /// <b>A ledger line.</b> Signed delta, balance before, balance after. Recording all
    /// three rather than just the delta is what makes the ledger auditable on its own: a
    /// reader can check that every line's arithmetic holds and that consecutive lines for
    /// one character join up, without replaying the game.
    ///
    /// Flat because it has to persist: one row of a future
    /// <c>economy_transaction_entry</c> table. No JSON blob, and no nested object -- an
    /// accounting record that needs parsing to be summed is not an accounting record.
    /// </remarks>
    public readonly struct EconomyTransactionEntry
    {
        public EconomyTransactionEntry(OwnerId owner, DefinitionId currency, int delta,
            int balanceBefore, int balanceAfter, InstanceId relatedItem = default)
        {
            Owner = owner;
            Currency = currency;
            Delta = delta;
            BalanceBefore = balanceBefore;
            BalanceAfter = balanceAfter;
            RelatedItem = relatedItem;
        }

        public OwnerId Owner { get; }

        public DefinitionId Currency { get; }

        /// <summary>Signed. Negative is money leaving.</summary>
        public int Delta { get; }

        public int BalanceBefore { get; }

        public int BalanceAfter { get; }

        /// <summary>The item this movement paid for, when there was one.</summary>
        public InstanceId RelatedItem { get; }

        public bool IsValid => Currency.IsValid;

        public override string ToString()
        {
            return Owner + " " + Currency + " " + (Delta >= 0 ? "+" : string.Empty) + Delta
                + " (" + BalanceBefore + " -> " + BalanceAfter + ")";
        }
    }

    /// <summary>
    /// One item changing hands.
    /// </summary>
    /// <remarks>
    /// The item counterpart of <see cref="EconomyTransactionEntry"/>, and the seam an
    /// ownership history is built from later. Both revisions are recorded, so a reader can
    /// tell that the copy that arrived is the copy that left and that exactly one mutation
    /// happened to it.
    ///
    /// Flat: one row of a future <c>item_transaction_entry</c> table.
    /// </remarks>
    public readonly struct ItemTransactionEntry
    {
        public ItemTransactionEntry(InstanceId item, DefinitionId definition, OwnerId from,
            OwnerId to, int quantity, Revision fromRevision, Revision toRevision)
        {
            Item = item;
            Definition = definition;
            From = from;
            To = to;
            Quantity = quantity;
            FromRevision = fromRevision;
            ToRevision = toRevision;
        }

        public InstanceId Item { get; }

        public DefinitionId Definition { get; }

        public OwnerId From { get; }

        public OwnerId To { get; }

        public int Quantity { get; }

        /// <summary>The item's revision before the transfer.</summary>
        public Revision FromRevision { get; }

        /// <summary>The item's revision after it. Exactly one step on from the other.</summary>
        public Revision ToRevision { get; }

        public bool IsValid => Item.IsValid;

        public override string ToString()
        {
            return Item + " x" + Quantity + " " + From + " -> " + To;
        }
    }

    /// <summary>
    /// One applied transaction, as it will be read back.
    /// </summary>
    /// <remarks>
    /// <b>Immutable, and written by the authority.</b> Every field is read-only and there is
    /// no setter, so a record cannot be edited after the fact -- which is the only property
    /// that makes an audit trail worth keeping. A client never supplies one; it supplies a
    /// <see cref="RequestId"/> and is handed this back.
    ///
    /// <b>Header plus lines.</b> The header says what happened and when; the lines say who
    /// gained and lost what. That shape is a ledger, and it is what maps onto
    /// <c>economy_transaction</c> and <c>economy_transaction_entry</c> without translation.
    ///
    /// <see cref="TimestampTicks"/> is data beside the identity, never the identity itself --
    /// see <see cref="TransactionId"/> for why.
    /// </remarks>
    public sealed class EconomyTransaction
    {
        private readonly EconomyTransactionEntry[] _currencyEntries;
        private readonly ItemTransactionEntry[] _itemEntries;

        public EconomyTransaction(TransactionId id, RequestId request,
            EconomyTransactionType type, EconomySource source, long timestampTicks,
            EconomyTransactionEntry[] currencyEntries = null,
            ItemTransactionEntry[] itemEntries = null)
        {
            Id = id;
            Request = request;
            Type = type;
            Source = source;
            TimestampTicks = timestampTicks;
            _currencyEntries = currencyEntries ?? NoCurrency;
            _itemEntries = itemEntries ?? NoItems;
        }

        public TransactionId Id { get; }

        /// <summary>The request that produced it, for recognising a retry.</summary>
        public RequestId Request { get; }

        public EconomyTransactionType Type { get; }

        public EconomySource Source { get; }

        /// <summary>When it was applied. Supplied by the caller, never read from a clock here.</summary>
        public long TimestampTicks { get; }

        public IReadOnlyList<EconomyTransactionEntry> CurrencyEntries => _currencyEntries;

        public IReadOnlyList<ItemTransactionEntry> ItemEntries => _itemEntries;

        /// <summary>
        /// Whether every currency line in this transaction sums to zero.
        /// </summary>
        /// <remarks>
        /// The invariant a transfer has to keep: money moves, it is not created. A credit or
        /// a debit deliberately does not balance -- those are the boundary where currency
        /// enters or leaves the economy -- so this is asked of transfers and exchanges, and
        /// a test asserts it holds for both.
        /// </remarks>
        public bool CurrencyBalances()
        {
            long total = 0;

            for (int i = 0; i < _currencyEntries.Length; i++) total += _currencyEntries[i].Delta;

            return total == 0;
        }

        public override string ToString()
        {
            return Type + " " + Id + " (" + Source + ", " + _currencyEntries.Length
                + " currency, " + _itemEntries.Length + " item)";
        }

        private static readonly EconomyTransactionEntry[] NoCurrency = new EconomyTransactionEntry[0];
        private static readonly ItemTransactionEntry[] NoItems = new ItemTransactionEntry[0];
    }
}
