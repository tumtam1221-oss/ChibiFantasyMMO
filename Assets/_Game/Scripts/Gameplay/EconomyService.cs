using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a currency movement was refused.</summary>
    public enum EconomyRejection
    {
        None = 0,

        /// <summary>No wallet, no registry or no ledger was supplied.</summary>
        MissingContext = 1,

        /// <summary>The currency could not be resolved.</summary>
        UnknownCurrency = 2,

        /// <summary>Content turned the currency off.</summary>
        CurrencyDisabled = 3,

        /// <summary>A negative or zero amount where a positive one was required.</summary>
        InvalidAmount = 4,

        /// <summary>The payer does not have that much.</summary>
        InsufficientFunds = 5,

        /// <summary>The credit would push the balance past its authored ceiling.</summary>
        BalanceOverflow = 6,

        /// <summary>Source and destination are the same wallet.</summary>
        SameWallet = 7,

        /// <summary>The request key was already used by a different operation.</summary>
        RequestConflict = 8
    }

    /// <summary>
    /// What a currency movement did.
    /// </summary>
    /// <remarks>
    /// Never a bare bool. A caller needs the typed reason to tell a player, the transaction
    /// identity to reconcile against, and the resulting balances to redraw -- and a server
    /// needs all three to answer a retry.
    /// </remarks>
    public readonly struct EconomyResult
    {
        private EconomyResult(bool accepted, EconomyRejection reason, EconomyTransaction transaction,
            bool wasReplay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Transaction = transaction;
            IsReplay = wasReplay;
        }

        public bool IsAccepted { get; }

        public EconomyRejection Reason { get; }

        /// <summary>The applied transaction, or null on refusal.</summary>
        public EconomyTransaction Transaction { get; }

        /// <summary>
        /// Whether this answer came from the ledger rather than from new work.
        /// </summary>
        /// <remarks>True when the same <see cref="RequestId"/> had already been applied. The
        /// caller is told, because "it worked" and "it had already worked" are the same
        /// outcome for a player and different information for a diagnostician.</remarks>
        public bool IsReplay { get; }

        public TransactionId TransactionId =>
            Transaction == null ? Core.TransactionId.None : Transaction.Id;

        public static EconomyResult Accepted(EconomyTransaction transaction, bool replay = false)
        {
            return new EconomyResult(true, EconomyRejection.None, transaction, replay);
        }

        public static EconomyResult Rejected(EconomyRejection reason)
        {
            return new EconomyResult(false, reason, null, false);
        }

        public override string ToString()
        {
            return IsAccepted
                ? (IsReplay ? "replayed " : "applied ") + Transaction
                : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// The only thing that changes a currency balance.
    /// </summary>
    /// <remarks>
    /// <b>One boundary, and it is this one.</b> <see cref="CharacterWalletState"/> holds the
    /// numbers and knows how to add; every rule about whether an addition is allowed, and
    /// every audit record, is decided here. Trade and player shops do not touch a wallet --
    /// they call <see cref="TryTransfer"/> -- and an architecture test asserts no other file
    /// calls <c>TryApplyDelta</c>.
    ///
    /// <b>Validate fully, then mutate.</b> A transfer checks that the payer can afford it
    /// <em>and</em> that the receiver can hold it before either wallet is written, so there
    /// is no state in which money has left one side and not arrived at the other. That is
    /// the strongest atomicity a single process can offer, and it is stated as exactly that:
    /// this is not a database transaction and nothing here claims to be one.
    ///
    /// <b>Retries are safe.</b> Every call takes a <see cref="RequestId"/>. If the ledger
    /// has already applied it, the original transaction is returned and nothing moves. A
    /// dropped reply therefore costs a player nothing and gains them nothing.
    ///
    /// <b>It knows no currency and no amount.</b> No <see cref="DefinitionId"/> is compared
    /// to a literal and no price appears below; ceilings come from
    /// <see cref="CurrencyDefinition.MaximumBalance"/>. The <see cref="EconomySource"/> is
    /// recorded and never branched on.
    /// </remarks>
    public static class EconomyService
    {
        /// <summary>Everything a movement needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<CurrencyDefinition> currencies,
                TransactionLedger ledger, long timestampTicks = 0L)
            {
                Currencies = currencies;
                Ledger = ledger;
                TimestampTicks = timestampTicks;
            }

            public IDefinitionRegistry<CurrencyDefinition> Currencies { get; }

            /// <summary>Where audit records go and where retries are recognised.</summary>
            public TransactionLedger Ledger { get; }

            /// <summary>
            /// When the caller says this is happening.
            /// </summary>
            /// <remarks>Supplied, never read from a clock: this assembly is engine-free and
            /// an ambient clock would make an audit record irreproducible in a test.</remarks>
            public long TimestampTicks { get; }

            public bool IsUsable => Currencies != null && Ledger != null;
        }

        /// <summary>The balance one wallet holds. Reads nothing else and changes nothing.</summary>
        public static int GetBalance(CharacterWalletState wallet, DefinitionId currency)
        {
            return wallet == null ? 0 : wallet.BalanceOf(currency);
        }

        /// <summary>
        /// Puts currency into a wallet.
        /// </summary>
        /// <remarks>The boundary where money enters the economy -- a quest payout, a monster
        /// drop. It deliberately does not balance to zero, and <see cref="TryTransfer"/> is
        /// the operation that must.</remarks>
        public static EconomyResult TryCredit(CharacterWalletState wallet, DefinitionId currency,
            int amount, EconomySource source, RequestId request, in Context context)
        {
            EconomyTransaction replay;
            if (TryReplay(request, context, out replay)) return EconomyResult.Accepted(replay, true);

            if (wallet == null || !context.IsUsable)
                return EconomyResult.Rejected(EconomyRejection.MissingContext);

            if (amount <= 0) return EconomyResult.Rejected(EconomyRejection.InvalidAmount);

            CurrencyDefinition definition;
            EconomyRejection resolved = Resolve(currency, context, out definition);
            if (resolved != EconomyRejection.None) return EconomyResult.Rejected(resolved);

            if (!wallet.CanReceive(currency, amount, definition.MaximumBalance))
                return EconomyResult.Rejected(EconomyRejection.BalanceOverflow);

            // ---- everything is resolved and nothing below can fail ---------------------

            int before;
            int after;
            wallet.TryApplyDelta(currency, amount, definition.MaximumBalance, out before, out after);

            var entries = new[]
            {
                new EconomyTransactionEntry(wallet.Owner, currency, amount, before, after)
            };

            return Commit(EconomyTransactionType.Credit, source, request, context, entries, null);
        }

        /// <summary>
        /// Takes currency out of a wallet.
        /// </summary>
        /// <remarks>The boundary where money leaves -- an NPC purchase, a repair. Refuses
        /// rather than allowing a balance to go negative, which is the invariant the whole
        /// economy rests on.</remarks>
        public static EconomyResult TryDebit(CharacterWalletState wallet, DefinitionId currency,
            int amount, EconomySource source, RequestId request, in Context context)
        {
            EconomyTransaction replay;
            if (TryReplay(request, context, out replay)) return EconomyResult.Accepted(replay, true);

            if (wallet == null || !context.IsUsable)
                return EconomyResult.Rejected(EconomyRejection.MissingContext);

            if (amount <= 0) return EconomyResult.Rejected(EconomyRejection.InvalidAmount);

            CurrencyDefinition definition;
            EconomyRejection resolved = Resolve(currency, context, out definition);
            if (resolved != EconomyRejection.None) return EconomyResult.Rejected(resolved);

            if (!wallet.CanAfford(currency, amount))
                return EconomyResult.Rejected(EconomyRejection.InsufficientFunds);

            // ---- everything is resolved and nothing below can fail ---------------------

            int before;
            int after;
            wallet.TryApplyDelta(currency, -amount, definition.MaximumBalance, out before, out after);

            var entries = new[]
            {
                new EconomyTransactionEntry(wallet.Owner, currency, -amount, before, after)
            };

            return Commit(EconomyTransactionType.Debit, source, request, context, entries, null);
        }

        /// <summary>
        /// Moves currency from one wallet to another.
        /// </summary>
        /// <remarks>
        /// <b>Both legs are checked before either runs.</b> The payer must be able to afford
        /// it and the receiver must be able to hold it; failing the second after applying the
        /// first would destroy a player's money, so the ceiling check happens up front. This
        /// is the shape every multi-party operation in this phase takes.
        ///
        /// The resulting transaction sums to zero, which is what distinguishes moving money
        /// from creating it.
        /// </remarks>
        public static EconomyResult TryTransfer(CharacterWalletState from, CharacterWalletState to,
            DefinitionId currency, int amount, EconomySource source, RequestId request,
            in Context context, InstanceId relatedItem = default)
        {
            EconomyTransaction replay;
            if (TryReplay(request, context, out replay)) return EconomyResult.Accepted(replay, true);

            EconomyRejection planned = PlanTransfer(from, to, currency, amount, context);
            if (planned != EconomyRejection.None) return EconomyResult.Rejected(planned);

            CurrencyDefinition definition;
            Resolve(currency, context, out definition);

            // ---- everything is resolved and nothing below can fail ---------------------

            int fromBefore;
            int fromAfter;
            from.TryApplyDelta(currency, -amount, definition.MaximumBalance, out fromBefore,
                out fromAfter);

            int toBefore;
            int toAfter;
            to.TryApplyDelta(currency, amount, definition.MaximumBalance, out toBefore, out toAfter);

            var entries = new[]
            {
                new EconomyTransactionEntry(from.Owner, currency, -amount, fromBefore, fromAfter,
                    relatedItem),
                new EconomyTransactionEntry(to.Owner, currency, amount, toBefore, toAfter,
                    relatedItem)
            };

            return Commit(EconomyTransactionType.Transfer, source, request, context, entries, null);
        }

        /// <summary>
        /// Whether a transfer would be accepted, without moving anything.
        /// </summary>
        /// <remarks>
        /// Exposed because a trade or a purchase has to know every leg will succeed before
        /// any leg runs. Trade and shop call this during planning and then call
        /// <see cref="ApplyPlannedTransfer"/> inside their own mutation boundary, so currency
        /// and items commit together rather than one before the other.
        /// </remarks>
        public static EconomyRejection PlanTransfer(CharacterWalletState from,
            CharacterWalletState to, DefinitionId currency, int amount, in Context context)
        {
            if (from == null || to == null || !context.IsUsable)
                return EconomyRejection.MissingContext;

            if (amount <= 0) return EconomyRejection.InvalidAmount;
            if (from.Owner == to.Owner) return EconomyRejection.SameWallet;

            CurrencyDefinition definition;
            EconomyRejection resolved = Resolve(currency, context, out definition);
            if (resolved != EconomyRejection.None) return resolved;

            if (!from.CanAfford(currency, amount)) return EconomyRejection.InsufficientFunds;

            if (!to.CanReceive(currency, amount, definition.MaximumBalance))
                return EconomyRejection.BalanceOverflow;

            return EconomyRejection.None;
        }

        /// <summary>
        /// Applies a transfer whose plan already passed, appending its ledger lines.
        /// </summary>
        /// <remarks>
        /// <b>Records no transaction of its own.</b> It writes into the caller's entry list,
        /// so a trade that moves two items and two currency amounts produces <em>one</em>
        /// transaction rather than four. That is what makes the audit read as one event and
        /// what a future database maps to one row.
        ///
        /// It must only be called after <see cref="PlanTransfer"/> returned
        /// <see cref="EconomyRejection.None"/> against the same wallets, inside a boundary
        /// where nothing else can fail. It re-checks anyway and returns false rather than
        /// half-applying, because a silent partial transfer is the one failure the economy
        /// cannot survive.
        /// </remarks>
        public static bool ApplyPlannedTransfer(CharacterWalletState from, CharacterWalletState to,
            DefinitionId currency, int amount, in Context context,
            List<EconomyTransactionEntry> into, InstanceId relatedItem = default)
        {
            if (into == null) return false;
            if (PlanTransfer(from, to, currency, amount, context) != EconomyRejection.None)
            {
                return false;
            }

            CurrencyDefinition definition;
            Resolve(currency, context, out definition);

            int fromBefore;
            int fromAfter;
            from.TryApplyDelta(currency, -amount, definition.MaximumBalance, out fromBefore,
                out fromAfter);

            int toBefore;
            int toAfter;
            to.TryApplyDelta(currency, amount, definition.MaximumBalance, out toBefore, out toAfter);

            into.Add(new EconomyTransactionEntry(from.Owner, currency, -amount, fromBefore,
                fromAfter, relatedItem));
            into.Add(new EconomyTransactionEntry(to.Owner, currency, amount, toBefore, toAfter,
                relatedItem));

            return true;
        }

        /// <summary>
        /// Writes one transaction covering currency and items together.
        /// </summary>
        /// <remarks>What trade and shop call at the end of their mutation boundary, so a
        /// completed exchange is one auditable event rather than a scatter of movements a
        /// reader has to correlate.</remarks>
        public static EconomyResult CommitExchange(EconomySource source, RequestId request,
            in Context context, EconomyTransactionEntry[] currencyEntries,
            ItemTransactionEntry[] itemEntries)
        {
            return Commit(EconomyTransactionType.Exchange, source, request, context,
                currencyEntries, itemEntries);
        }

        private static EconomyResult Commit(EconomyTransactionType type, EconomySource source,
            RequestId request, in Context context, EconomyTransactionEntry[] currencyEntries,
            ItemTransactionEntry[] itemEntries)
        {
            var transaction = new EconomyTransaction(TransactionId.New(), request, type, source,
                context.TimestampTicks, currencyEntries, itemEntries);

            context.Ledger.Record(transaction);

            return EconomyResult.Accepted(transaction);
        }

        private static bool TryReplay(RequestId request, in Context context,
            out EconomyTransaction transaction)
        {
            transaction = null;
            if (!context.IsUsable || !request.IsValid) return false;

            return context.Ledger.TryGetByRequest(request, out transaction);
        }

        private static EconomyRejection Resolve(DefinitionId currency, in Context context,
            out CurrencyDefinition definition)
        {
            definition = null;

            if (!currency.IsValid || !context.Currencies.TryGet(currency, out definition)
                || definition == null)
            {
                return EconomyRejection.UnknownCurrency;
            }

            return definition.Enabled ? EconomyRejection.None : EconomyRejection.CurrencyDisabled;
        }
    }
}
