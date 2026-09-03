using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a trade operation was refused.</summary>
    public enum TradeRejection
    {
        None = 0,

        /// <summary>No session, container, wallet or registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The character is not in this trade.</summary>
        NotAParticipant = 2,

        /// <summary>The session is no longer open to changes.</summary>
        SessionNotOpen = 3,

        /// <summary>The session has already finished.</summary>
        SessionFinished = 4,

        /// <summary>The session lapsed.</summary>
        SessionExpired = 5,

        /// <summary>The item is not in the offering player's bag.</summary>
        ItemNotHeld = 6,

        /// <summary>The item may not change hands. See <see cref="Block"/> on the result.</summary>
        ItemBlocked = 7,

        /// <summary>That item is already on the table.</summary>
        ItemAlreadyOffered = 8,

        /// <summary>The item changed since it was offered.</summary>
        StaleItem = 9,

        /// <summary>More items than one side may offer.</summary>
        TooManyItems = 10,

        /// <summary>The currency amount is negative, or the currency does not resolve.</summary>
        InvalidCurrency = 11,

        /// <summary>The offering player cannot cover the currency they offered.</summary>
        InsufficientFunds = 12,

        /// <summary>A receiving wallet cannot hold what would arrive.</summary>
        BalanceOverflow = 13,

        /// <summary>A receiving bag has nowhere to put what would arrive.</summary>
        InventoryFull = 14,

        /// <summary>Both sides have not agreed.</summary>
        NotBothAccepted = 15,

        /// <summary>Nothing was offered by either side.</summary>
        NothingOffered = 16,

        /// <summary>A trade may not be with oneself.</summary>
        InvalidCounterparty = 17,

        /// <summary>One of the players is already in another trade.</summary>
        AlreadyTrading = 18
    }

    /// <summary>What a trade operation did.</summary>
    public readonly struct TradeResult
    {
        private TradeResult(bool accepted, TradeRejection reason, ItemTransferBlock block,
            TradeSession session, EconomyTransaction transaction, InstanceId offending,
            bool replay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Block = block;
            Session = session;
            Transaction = transaction;
            Offending = offending;
            IsReplay = replay;
        }

        public bool IsAccepted { get; }

        public TradeRejection Reason { get; }

        /// <summary>Why a specific item was refused, when that is the reason.</summary>
        public ItemTransferBlock Block { get; }

        public TradeSession Session { get; }

        /// <summary>The audit record a completed trade produced.</summary>
        public EconomyTransaction Transaction { get; }

        /// <summary>Which item caused the refusal, when one did.</summary>
        public InstanceId Offending { get; }

        /// <summary>Whether this answer came from the ledger rather than from new work.</summary>
        public bool IsReplay { get; }

        public TransactionId TransactionId =>
            Transaction == null ? Core.TransactionId.None : Transaction.Id;

        public static TradeResult Accepted(TradeSession session,
            EconomyTransaction transaction = null, bool replay = false)
        {
            return new TradeResult(true, TradeRejection.None, ItemTransferBlock.None, session,
                transaction, default, replay);
        }

        public static TradeResult Rejected(TradeRejection reason, TradeSession session = null,
            InstanceId offending = default, ItemTransferBlock block = ItemTransferBlock.None)
        {
            return new TradeResult(false, reason, block, session, null, offending, false);
        }

        public override string ToString()
        {
            return IsAccepted ? "trade ok " + TransactionId : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Trading between two players.
    /// </summary>
    /// <remarks>
    /// <b>Everything is revalidated at commit.</b> What was true when an item was offered is
    /// not trusted at the moment the trade applies: ownership, revision, whether the item is
    /// still in the bag, whether it has since been equipped or listed, whether both wallets
    /// still cover their offers, and whether both bags have room. A trade is the point where
    /// two players' worlds meet, and everything either of them did in between has to be
    /// accounted for.
    ///
    /// <b>Nothing moves until everything is proven.</b> Items are planned through
    /// <see cref="ItemOwnershipTransfer"/> and currency through
    /// <see cref="EconomyService.PlanTransfer"/>, both of which write nothing. Only when
    /// every leg passes does the mutation boundary run, and inside it nothing can fail. There
    /// is no state in which one player has handed something over and the other has not.
    ///
    /// <b>One transaction, not four.</b> The whole exchange commits as a single
    /// <see cref="EconomyTransaction"/> carrying both currency lines and every item line, so
    /// the audit reads as one event and a future database maps it to one row.
    ///
    /// <b>It is not a database transaction and does not claim to be.</b> Everything happens
    /// in one process against objects in memory, which cannot be interrupted partway. What a
    /// future server gains is durability, not atomicity: the boundary is already here.
    ///
    /// <b>Retries are safe.</b> The commit takes a <see cref="RequestId"/>; if the ledger has
    /// already applied it, the original transaction comes back and nothing moves again.
    /// </remarks>
    public static class TradeService
    {
        /// <summary>One participant's side, as the service sees it.</summary>
        public readonly struct Participant
        {
            public Participant(CharacterId character, OwnerId owner, ItemContainerState inventory,
                CharacterWalletState wallet, ItemTransferRules.Context rules)
            {
                Character = character;
                Owner = owner;
                Inventory = inventory;
                Wallet = wallet;
                Rules = rules;
            }

            public CharacterId Character { get; }

            public OwnerId Owner { get; }

            public ItemContainerState Inventory { get; }

            public CharacterWalletState Wallet { get; }

            /// <summary>The authorities this player's items are checked against.</summary>
            public ItemTransferRules.Context Rules { get; }

            public bool IsUsable => Inventory != null;
        }

        /// <summary>Everything a trade needs.</summary>
        public readonly struct Context
        {
            public Context(Participant a, Participant b,
                IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<CurrencyDefinition> currencies = null,
                TransactionLedger ledger = null,
                SocialConfiguration configuration = null,
                long timestampTicks = 0L)
            {
                A = a;
                B = b;
                Items = items;
                Currencies = currencies;
                Ledger = ledger;
                Limits = SocialConfiguration.Resolve(configuration);
                TimestampTicks = timestampTicks;
            }

            public Participant A { get; }

            public Participant B { get; }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<CurrencyDefinition> Currencies { get; }

            public TransactionLedger Ledger { get; }

            public SocialConfiguration.Limits Limits { get; }

            public long TimestampTicks { get; }

            public bool IsUsable => Items != null && A.IsUsable && B.IsUsable;

            /// <summary>The participant a character is, or an unusable one.</summary>
            public Participant Of(CharacterId character)
            {
                if (A.Character == character) return A;
                if (B.Character == character) return B;
                return default;
            }

            public EconomyService.Context Economy =>
                new EconomyService.Context(Currencies, Ledger, TimestampTicks);
        }

        // ---- session -------------------------------------------------------------------

        /// <summary>Opens a session between two players.</summary>
        /// <remarks>Nothing is reserved yet: an empty session holds no claim on anything, so
        /// opening one costs a player nothing.</remarks>
        public static TradeSession Open(CharacterId a, OwnerId ownerA, CharacterId b,
            OwnerId ownerB, long createdTicks = 0L, long expiresTicks = 0L)
        {
            if (!a.IsValid || !b.IsValid || a == b) return null;

            return new TradeSession(InstanceId.New(), a, ownerA, b, ownerB, createdTicks,
                expiresTicks);
        }

        /// <summary>Calls the trade off. Nothing has moved, so nothing is undone.</summary>
        public static TradeResult TryCancel(TradeSession session, CharacterId character)
        {
            if (session == null) return TradeResult.Rejected(TradeRejection.MissingContext);
            if (!session.Involves(character))
                return TradeResult.Rejected(TradeRejection.NotAParticipant, session);

            if (session.IsTerminal)
                return TradeResult.Rejected(TradeRejection.SessionFinished, session);

            session.TrySetState(TradeSessionState.Cancelled);
            return TradeResult.Accepted(session);
        }

        // ---- offers --------------------------------------------------------------------

        /// <summary>
        /// Puts an item on the table.
        /// </summary>
        /// <remarks>
        /// Checked immediately as a courtesy so a player is told at once, and checked again
        /// at commit because the world moves. Both acceptances reset, because the offer the
        /// other player agreed to no longer exists.
        /// </remarks>
        public static TradeResult TryOfferItem(TradeSession session, CharacterId character,
            InstanceId instance, in Context context)
        {
            TradeRejection open = CheckOpen(session, character, context);
            if (open != TradeRejection.None) return TradeResult.Rejected(open, session);

            Participant participant = context.Of(character);
            TradeOffer offer = session.OfferOf(character);

            if (offer.Contains(instance))
                return TradeResult.Rejected(TradeRejection.ItemAlreadyOffered, session, instance);

            if (offer.ItemCount >= context.Limits.MaxTradeItems)
                return TradeResult.Rejected(TradeRejection.TooManyItems, session, instance);

            int index = participant.Inventory.IndexOf(instance);
            if (index < 0)
                return TradeResult.Rejected(TradeRejection.ItemNotHeld, session, instance);

            ItemSlot slot = participant.Inventory.GetSlot(index);
            GameInstance held = slot.Content;

            ItemTransferBlock block = ItemTransferRules.CanTransfer(held, participant.Owner,
                participant.Rules);

            if (block != ItemTransferBlock.None)
            {
                return TradeResult.Rejected(TradeRejection.ItemBlocked, session, instance, block);
            }

            offer.TryAddItem(new TradeOfferItem(instance, held.DefinitionId, slot.Quantity,
                held.Revision));

            session.MarkChanged();

            return TradeResult.Accepted(session);
        }

        /// <summary>Takes an item back off the table.</summary>
        public static TradeResult TryWithdrawItem(TradeSession session, CharacterId character,
            InstanceId instance, in Context context)
        {
            TradeRejection open = CheckOpen(session, character, context);
            if (open != TradeRejection.None) return TradeResult.Rejected(open, session);

            if (!session.OfferOf(character).TryRemoveItem(instance))
                return TradeResult.Rejected(TradeRejection.ItemNotHeld, session, instance);

            session.MarkChanged();

            return TradeResult.Accepted(session);
        }

        /// <summary>
        /// Sets how much currency this side is offering.
        /// </summary>
        /// <remarks>Affordability is checked now and again at commit; a player may spend
        /// their gold elsewhere in between.</remarks>
        public static TradeResult TryOfferCurrency(TradeSession session, CharacterId character,
            DefinitionId currency, int amount, in Context context)
        {
            TradeRejection open = CheckOpen(session, character, context);
            if (open != TradeRejection.None) return TradeResult.Rejected(open, session);

            if (!currency.IsValid || amount < 0)
                return TradeResult.Rejected(TradeRejection.InvalidCurrency, session);

            Participant participant = context.Of(character);

            if (amount > 0)
            {
                if (participant.Wallet == null)
                    return TradeResult.Rejected(TradeRejection.MissingContext, session);

                if (!participant.Wallet.CanAfford(currency, amount))
                    return TradeResult.Rejected(TradeRejection.InsufficientFunds, session);
            }

            if (!session.OfferOf(character).TrySetCurrency(currency, amount))
            {
                // Setting the amount it already holds is not a change, so acceptance stands.
                return TradeResult.Accepted(session);
            }

            session.MarkChanged();

            return TradeResult.Accepted(session);
        }

        /// <summary>
        /// Agrees to the offers as they stand.
        /// </summary>
        /// <remarks>The first stage of the two-stage confirmation: agreeing does not commit.
        /// Any subsequent change by either player clears this, so the agreement always refers
        /// to what was actually on the table.</remarks>
        public static TradeResult TryAccept(TradeSession session, CharacterId character,
            in Context context)
        {
            TradeRejection open = CheckOpen(session, character, context);
            if (open != TradeRejection.None) return TradeResult.Rejected(open, session);

            if (session.OfferA.IsEmpty && session.OfferB.IsEmpty)
                return TradeResult.Rejected(TradeRejection.NothingOffered, session);

            session.OfferOf(character).Accept();

            return TradeResult.Accepted(session);
        }

        /// <summary>Withdraws agreement without changing the offer.</summary>
        public static TradeResult TryRetract(TradeSession session, CharacterId character,
            in Context context)
        {
            TradeRejection open = CheckOpen(session, character, context);
            if (open != TradeRejection.None) return TradeResult.Rejected(open, session);

            session.OfferOf(character).Touch();

            return TradeResult.Accepted(session);
        }

        // ---- commit --------------------------------------------------------------------

        /// <summary>
        /// Revalidates everything and applies the trade.
        /// </summary>
        /// <param name="session">The trade.</param>
        /// <param name="request">The idempotency key. A repeat returns the original result.</param>
        /// <param name="context">Both participants, the registries and the ledger.</param>
        /// <remarks>
        /// The second stage of the two-stage confirmation. It runs only when both sides have
        /// agreed, locks the session against further offers, revalidates from scratch, and
        /// then applies everything in one boundary.
        ///
        /// A failure at revalidation marks the session failed rather than leaving it open:
        /// the players agreed to something that is no longer possible, and silently reverting
        /// to "open" would leave two stale acceptances standing.
        /// </remarks>
        public static TradeResult TryCommit(TradeSession session, RequestId request,
            in Context context)
        {
            if (session == null || !context.IsUsable)
                return TradeResult.Rejected(TradeRejection.MissingContext, session);

            // A retry finds the answer the first attempt already produced.
            if (context.Ledger != null && request.IsValid)
            {
                EconomyTransaction previous;
                if (context.Ledger.TryGetByRequest(request, out previous))
                {
                    return TradeResult.Accepted(session, previous, true);
                }
            }

            if (session.IsTerminal)
                return TradeResult.Rejected(TradeRejection.SessionFinished, session);

            if (session.HasExpired(context.TimestampTicks))
            {
                session.TrySetState(TradeSessionState.Failed);
                return TradeResult.Rejected(TradeRejection.SessionExpired, session);
            }

            if (!session.BothAccepted)
                return TradeResult.Rejected(TradeRejection.NotBothAccepted, session);

            if (session.OfferA.IsEmpty && session.OfferB.IsEmpty)
                return TradeResult.Rejected(TradeRejection.NothingOffered, session);

            Participant a = context.Of(session.OfferA.Character);
            Participant b = context.Of(session.OfferB.Character);

            if (!a.IsUsable || !b.IsUsable)
                return TradeResult.Rejected(TradeRejection.MissingContext, session);

            // Offers are locked from here. Nothing may change while the trade is deciding.
            session.TrySetState(TradeSessionState.Confirming);

            var legA = new TransferLeg(a.Inventory, a.Owner);
            var legB = new TransferLeg(b.Inventory, b.Owner);

            TradeResult gathered = Gather(session.OfferA, a, legA, session);
            if (!gathered.IsAccepted) return Fail(session, gathered);

            gathered = Gather(session.OfferB, b, legB, session);
            if (!gathered.IsAccepted) return Fail(session, gathered);

            ItemOwnershipTransfer.PlanResult plan = ItemOwnershipTransfer.Plan(legA, legB,
                context.Items, a.Rules, b.Rules);

            if (!plan.IsAccepted)
            {
                return Fail(session, TradeResult.Rejected(Translate(plan.Reason), session,
                    plan.Offending, plan.Block));
            }

            // Every currency leg is proven before any of them runs.
            TradeResult currency = PlanCurrency(session, a, b, context);
            if (!currency.IsAccepted) return Fail(session, currency);

            // ---- everything is proven and nothing below can fail -----------------------

            var currencyEntries = new List<EconomyTransactionEntry>();
            var itemEntries = new List<ItemTransactionEntry>();

            ApplyCurrency(session.OfferA, a, b, context, currencyEntries);
            ApplyCurrency(session.OfferB, b, a, context, currencyEntries);

            ItemOwnershipTransfer.Apply(legA, legB, context.Items, itemEntries);

            EconomyResult audit = EconomyService.CommitExchange(EconomySource.PlayerTrade,
                request, context.Economy, currencyEntries.ToArray(), itemEntries.ToArray());

            session.TrySetState(TradeSessionState.Completed);

            return TradeResult.Accepted(session, audit.Transaction);
        }

        /// <summary>
        /// Re-checks one side's offered items and loads them into its transfer leg.
        /// </summary>
        /// <remarks>
        /// This is where a stale offer dies. The revision recorded when the item was put on
        /// the table is compared against the item now, so anything that happened to it in
        /// between -- an enhancement, a card socketed, an owner change -- is caught.
        /// </remarks>
        private static TradeResult Gather(TradeOffer offer, Participant participant,
            TransferLeg leg, TradeSession session)
        {
            IReadOnlyList<TradeOfferItem> items = offer.Items;

            for (int i = 0; i < items.Count; i++)
            {
                TradeOfferItem offered = items[i];

                int index = participant.Inventory.IndexOf(offered.Instance);
                if (index < 0)
                {
                    return TradeResult.Rejected(TradeRejection.ItemNotHeld, session,
                        offered.Instance);
                }

                GameInstance held = participant.Inventory.GetSlot(index).Content;

                ItemTransferBlock block = ItemTransferRules.CanTransfer(held, participant.Owner,
                    participant.Rules, offered.OfferedRevision);

                if (block == ItemTransferBlock.StaleRevision)
                {
                    return TradeResult.Rejected(TradeRejection.StaleItem, session,
                        offered.Instance, block);
                }

                if (block != ItemTransferBlock.None)
                {
                    return TradeResult.Rejected(TradeRejection.ItemBlocked, session,
                        offered.Instance, block);
                }

                leg.Give(held);
            }

            return TradeResult.Accepted(session);
        }

        private static TradeResult PlanCurrency(TradeSession session, Participant a,
            Participant b, in Context context)
        {
            TradeResult planned = PlanCurrencySide(session.OfferA, a, b, context, session);
            if (!planned.IsAccepted) return planned;

            return PlanCurrencySide(session.OfferB, b, a, context, session);
        }

        private static TradeResult PlanCurrencySide(TradeOffer offer, Participant from,
            Participant to, in Context context, TradeSession session)
        {
            IReadOnlyList<TradeOfferCurrency> offered = offer.Currency;
            if (offered.Count == 0) return TradeResult.Accepted(session);

            if (from.Wallet == null || to.Wallet == null || context.Currencies == null
                || context.Ledger == null)
            {
                return TradeResult.Rejected(TradeRejection.MissingContext, session);
            }

            for (int i = 0; i < offered.Count; i++)
            {
                EconomyRejection reason = EconomyService.PlanTransfer(from.Wallet, to.Wallet,
                    offered[i].Currency, offered[i].Amount, context.Economy);

                if (reason == EconomyRejection.None) continue;

                return TradeResult.Rejected(Translate(reason), session);
            }

            return TradeResult.Accepted(session);
        }

        private static void ApplyCurrency(TradeOffer offer, Participant from, Participant to,
            in Context context, List<EconomyTransactionEntry> into)
        {
            IReadOnlyList<TradeOfferCurrency> offered = offer.Currency;

            for (int i = 0; i < offered.Count; i++)
            {
                EconomyService.ApplyPlannedTransfer(from.Wallet, to.Wallet, offered[i].Currency,
                    offered[i].Amount, context.Economy, into);
            }
        }

        private static TradeResult Fail(TradeSession session, TradeResult reason)
        {
            session.TrySetState(TradeSessionState.Failed);
            return reason;
        }

        private static TradeRejection CheckOpen(TradeSession session, CharacterId character,
            in Context context)
        {
            if (session == null || !context.IsUsable) return TradeRejection.MissingContext;
            if (!session.Involves(character)) return TradeRejection.NotAParticipant;
            if (session.IsTerminal) return TradeRejection.SessionFinished;
            if (!session.IsOpen) return TradeRejection.SessionNotOpen;

            if (session.HasExpired(context.TimestampTicks)) return TradeRejection.SessionExpired;

            return TradeRejection.None;
        }

        /// <summary>
        /// Reports a transfer refusal in the vocabulary a trade speaks.
        /// </summary>
        /// <remarks>The enums stay separate because a transfer can be refused for reasons a
        /// trade has no word for, and merging them would make every caller of either learn
        /// the other's vocabulary.</remarks>
        private static TradeRejection Translate(TransferRejection reason)
        {
            switch (reason)
            {
                case TransferRejection.DestinationFull: return TradeRejection.InventoryFull;
                case TransferRejection.NotHeld: return TradeRejection.ItemNotHeld;
                case TransferRejection.Blocked: return TradeRejection.ItemBlocked;
                case TransferRejection.DuplicateInstance: return TradeRejection.ItemAlreadyOffered;
                default: return TradeRejection.MissingContext;
            }
        }

        private static TradeRejection Translate(EconomyRejection reason)
        {
            switch (reason)
            {
                case EconomyRejection.InsufficientFunds: return TradeRejection.InsufficientFunds;
                case EconomyRejection.BalanceOverflow: return TradeRejection.BalanceOverflow;
                case EconomyRejection.SameWallet: return TradeRejection.InvalidCounterparty;
                case EconomyRejection.UnknownCurrency:
                case EconomyRejection.CurrencyDisabled:
                case EconomyRejection.InvalidAmount: return TradeRejection.InvalidCurrency;
                default: return TradeRejection.MissingContext;
            }
        }
    }
}
