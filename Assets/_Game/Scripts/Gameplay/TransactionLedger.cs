using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Every applied transaction, and what each request produced.
    /// </summary>
    /// <remarks>
    /// <b>Append-only.</b> There is no update and no delete. A record that could be edited
    /// afterwards would not be an audit trail, and the absence of any mutating method is a
    /// stronger guarantee than a comment asking nobody to write one.
    ///
    /// <b>It is the idempotency boundary.</b> A <see cref="RequestId"/> maps to at most one
    /// <see cref="EconomyTransaction"/>. A second arrival of the same request finds the
    /// first answer and is handed it back instead of doing the work again -- which is what
    /// makes a dropped reply, a double-click and a network retry safe rather than a
    /// duplication bug. Every service in this phase asks here first and records here last.
    ///
    /// <b>Refusals are deliberately not recorded.</b> A rejected request wrote nothing, so
    /// re-sending it must be re-evaluated rather than answered from a cache: the reason it
    /// failed -- a full bag, a missing coin -- may no longer hold, and a player retrying
    /// after fixing it should succeed.
    ///
    /// <b>In-memory here, a table later.</b> This is the local domain foundation. A future
    /// server replaces it with <c>economy_transaction</c> plus a unique index on the request
    /// key, and every caller is unchanged because they all go through these three methods.
    /// Nothing here claims database durability.
    /// </remarks>
    public sealed class TransactionLedger
    {
        private readonly List<EconomyTransaction> _transactions = new List<EconomyTransaction>();

        private readonly Dictionary<RequestId, EconomyTransaction> _byRequest =
            new Dictionary<RequestId, EconomyTransaction>();

        private readonly Dictionary<TransactionId, EconomyTransaction> _byId =
            new Dictionary<TransactionId, EconomyTransaction>();

        /// <summary>Everything applied, oldest first.</summary>
        public IReadOnlyList<EconomyTransaction> Transactions => _transactions;

        public int Count => _transactions.Count;

        /// <summary>
        /// The transaction a request already produced, if it has been seen.
        /// </summary>
        /// <remarks>Asked before any work is planned. A hit means the caller is a retry and
        /// must be handed the original answer, not a second execution.</remarks>
        public bool TryGetByRequest(RequestId request, out EconomyTransaction transaction)
        {
            transaction = null;
            if (!request.IsValid) return false;

            return _byRequest.TryGetValue(request, out transaction);
        }

        public bool TryGetById(TransactionId id, out EconomyTransaction transaction)
        {
            transaction = null;
            if (!id.IsValid) return false;

            return _byId.TryGetValue(id, out transaction);
        }

        /// <summary>
        /// Records an applied transaction.
        /// </summary>
        /// <remarks>
        /// Refuses a duplicate identity and a duplicate request rather than overwriting
        /// either. Overwriting would let a second execution hide the first, which is exactly
        /// the accident the request key exists to prevent -- so the ledger refuses to be
        /// part of it. A caller that gets false here has a bug, not a retry.
        ///
        /// A transaction with no request key is still recorded: an internal or system-sourced
        /// movement has no client to retry it, and refusing it would mean the ledger held
        /// less than the truth.
        /// </remarks>
        public bool Record(EconomyTransaction transaction)
        {
            if (transaction == null || !transaction.Id.IsValid) return false;
            if (_byId.ContainsKey(transaction.Id)) return false;

            if (transaction.Request.IsValid && _byRequest.ContainsKey(transaction.Request))
            {
                return false;
            }

            _transactions.Add(transaction);
            _byId[transaction.Id] = transaction;

            if (transaction.Request.IsValid) _byRequest[transaction.Request] = transaction;

            return true;
        }

        /// <summary>
        /// Every recorded movement of one currency for one owner.
        /// </summary>
        /// <remarks>What a statement is built from, and what a test uses to prove a balance
        /// and its history agree.</remarks>
        public void CollectEntriesFor(OwnerId owner, DefinitionId currency,
            List<EconomyTransactionEntry> into)
        {
            if (into == null) return;

            into.Clear();

            for (int i = 0; i < _transactions.Count; i++)
            {
                IReadOnlyList<EconomyTransactionEntry> entries = _transactions[i].CurrencyEntries;

                for (int e = 0; e < entries.Count; e++)
                {
                    if (entries[e].Owner != owner) continue;
                    if (currency.IsValid && entries[e].Currency != currency) continue;

                    into.Add(entries[e]);
                }
            }
        }

        /// <summary>
        /// The sum of every recorded delta for one owner and currency.
        /// </summary>
        /// <remarks>Should equal the wallet's balance. That the two agree is checked by a
        /// test rather than assumed, because a ledger that has drifted from the balances it
        /// describes is worse than no ledger.</remarks>
        public long NetFor(OwnerId owner, DefinitionId currency)
        {
            long total = 0;

            for (int i = 0; i < _transactions.Count; i++)
            {
                IReadOnlyList<EconomyTransactionEntry> entries = _transactions[i].CurrencyEntries;

                for (int e = 0; e < entries.Count; e++)
                {
                    if (entries[e].Owner != owner || entries[e].Currency != currency) continue;
                    total += entries[e].Delta;
                }
            }

            return total;
        }

        /// <summary>Every recorded movement of one item, oldest first.</summary>
        public void CollectItemHistory(InstanceId item, List<ItemTransactionEntry> into)
        {
            if (into == null) return;

            into.Clear();

            for (int i = 0; i < _transactions.Count; i++)
            {
                IReadOnlyList<ItemTransactionEntry> entries = _transactions[i].ItemEntries;

                for (int e = 0; e < entries.Count; e++)
                {
                    if (entries[e].Item != item) continue;
                    into.Add(entries[e]);
                }
            }
        }
    }
}
