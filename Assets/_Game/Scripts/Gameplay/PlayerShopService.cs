using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a player shop operation was refused.</summary>
    public enum ShopRejection
    {
        None = 0,

        /// <summary>No shop, container, wallet or registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The shop is closed or has been taken down.</summary>
        ShopUnavailable = 2,

        /// <summary>The operation is the shop owner's alone.</summary>
        NotTheOwner = 3,

        /// <summary>No such listing.</summary>
        UnknownListing = 4,

        /// <summary>The listing is no longer on sale.</summary>
        ListingNotActive = 5,

        /// <summary>The item is not in the seller's bag.</summary>
        ItemNotHeld = 6,

        /// <summary>The item may not be sold. See the result's block.</summary>
        ItemBlocked = 7,

        /// <summary>The price is below the authored minimum or above the maximum.</summary>
        InvalidPrice = 8,

        /// <summary>The currency does not resolve, or is turned off.</summary>
        InvalidCurrency = 9,

        /// <summary>The shop already holds as many listings as it may.</summary>
        TooManyListings = 10,

        /// <summary>The buyer cannot cover the price.</summary>
        InsufficientFunds = 11,

        /// <summary>The seller's wallet cannot hold the proceeds.</summary>
        BalanceOverflow = 12,

        /// <summary>The buyer's bag has nowhere to put it.</summary>
        InventoryFull = 13,

        /// <summary>A player may not buy from their own shop.</summary>
        CannotBuyOwnListing = 14,

        /// <summary>The map does not resolve, or does not permit player shops.</summary>
        InvalidLocation = 15,

        /// <summary>The item is already listed somewhere.</summary>
        AlreadyListed = 16
    }

    /// <summary>What a player shop operation did.</summary>
    public readonly struct ShopResult
    {
        private ShopResult(bool accepted, ShopRejection reason, ItemTransferBlock block,
            PlayerShop shop, PlayerShopListing listing, EconomyTransaction transaction,
            bool replay)
        {
            IsAccepted = accepted;
            Reason = reason;
            Block = block;
            Shop = shop;
            Listing = listing;
            Transaction = transaction;
            IsReplay = replay;
        }

        public bool IsAccepted { get; }

        public ShopRejection Reason { get; }

        /// <summary>Why the item itself was refused, when that is the reason.</summary>
        public ItemTransferBlock Block { get; }

        public PlayerShop Shop { get; }

        public PlayerShopListing Listing { get; }

        /// <summary>The audit record a completed purchase produced.</summary>
        public EconomyTransaction Transaction { get; }

        /// <summary>Whether this answer came from the ledger rather than from new work.</summary>
        public bool IsReplay { get; }

        public TransactionId TransactionId =>
            Transaction == null ? Core.TransactionId.None : Transaction.Id;

        public static ShopResult Accepted(PlayerShop shop, PlayerShopListing listing = null,
            EconomyTransaction transaction = null, bool replay = false)
        {
            return new ShopResult(true, ShopRejection.None, ItemTransferBlock.None, shop, listing,
                transaction, replay);
        }

        public static ShopResult Rejected(ShopRejection reason, PlayerShop shop = null,
            PlayerShopListing listing = null, ItemTransferBlock block = ItemTransferBlock.None)
        {
            return new ShopResult(false, reason, block, shop, listing, null, false);
        }

        public override string ToString()
        {
            return IsAccepted ? "shop ok " + TransactionId : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Player-run shops.
    /// </summary>
    /// <remarks>
    /// <b>Listing moves the item into escrow.</b> It leaves the seller's bag and is held by
    /// the listing, marked <see cref="ItemLockState.Listed"/>. While it is there it is in no
    /// container, so it cannot be equipped, consumed, socketed, traded or listed again -- not
    /// because every other system remembers to check a flag, but because there is nothing for
    /// them to find. Cancelling puts the same object back.
    ///
    /// <b>A purchase is one boundary.</b> Price, funds, the seller's ceiling, the buyer's
    /// capacity and the listing's state are all established before anything moves. A buyer
    /// with the money and a full bag is refused with <see cref="ShopRejection.InventoryFull"/>
    /// and pays nothing -- the partial outcome the brief names is not reachable, because the
    /// money and the item move inside the same block after every check has passed.
    ///
    /// <b>A listing sells once.</b> <see cref="PlayerShopListing.TrySetState"/> refuses to
    /// leave a terminal state, so two buyers racing the same item produce one sale and one
    /// typed refusal, and a repeated request is answered from the ledger.
    ///
    /// <b>The seller need not be present.</b> Nothing here consults the seller's session:
    /// the shop, its listings and the escrowed items are authoritative state, and a purchase
    /// credits a wallet that exists whether or not anybody is looking at it.
    ///
    /// <b>It shares the ownership seam with trade.</b> Items move through
    /// <see cref="ItemOwnershipTransfer"/> and currency through
    /// <see cref="EconomyService"/>. There is no shop-specific transfer and no shop-specific
    /// wallet arithmetic.
    /// </remarks>
    public static class PlayerShopService
    {
        /// <summary>Everything a shop operation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<CurrencyDefinition> currencies = null,
                TransactionLedger ledger = null,
                IDefinitionRegistry<MapDefinition> maps = null,
                SocialConfiguration configuration = null,
                long timestampTicks = 0L)
            {
                Items = items;
                Currencies = currencies;
                Ledger = ledger;
                Maps = maps;
                Limits = SocialConfiguration.Resolve(configuration);
                TimestampTicks = timestampTicks;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<CurrencyDefinition> Currencies { get; }

            public TransactionLedger Ledger { get; }

            /// <summary>Needed only to validate where a shop stands.</summary>
            public IDefinitionRegistry<MapDefinition> Maps { get; }

            public SocialConfiguration.Limits Limits { get; }

            public long TimestampTicks { get; }

            public bool IsUsable => Items != null;

            public EconomyService.Context Economy =>
                new EconomyService.Context(Currencies, Ledger, TimestampTicks);
        }

        // ---- shop ----------------------------------------------------------------------

        /// <summary>
        /// Opens a shop at a place in the world.
        /// </summary>
        /// <remarks>
        /// The map is validated when a registry is supplied: it must resolve, and it must not
        /// be a boss area, where a shop would be nonsense. No map id is named in code -- the
        /// rule is read off <see cref="MapDefinition"/>, so which maps allow shops is content.
        /// </remarks>
        public static ShopResult TryCreateShop(CharacterId owner, OwnerId ownerId, string name,
            WorldPlacement placement, in Context context)
        {
            if (!context.IsUsable || !owner.IsValid)
                return ShopResult.Rejected(ShopRejection.MissingContext);

            ShopRejection location = ValidatePlacement(placement, context);
            if (location != ShopRejection.None) return ShopResult.Rejected(location);

            var shop = new PlayerShop(InstanceId.New(), owner, ownerId, name, placement,
                context.TimestampTicks);

            return ShopResult.Accepted(shop);
        }

        /// <summary>
        /// Whether a shop may stand somewhere.
        /// </summary>
        /// <remarks>Content-driven. A caller with no map registry gets the shape check only,
        /// which is less information rather than a wrong answer.</remarks>
        public static ShopRejection ValidatePlacement(WorldPlacement placement, in Context context)
        {
            if (!placement.IsValid) return ShopRejection.InvalidLocation;
            if (context.Maps == null) return ShopRejection.None;

            MapDefinition map;
            if (!context.Maps.TryGet(placement.Map, out map) || map == null)
                return ShopRejection.InvalidLocation;

            // A boss arena is not a marketplace. Read off the map's authored classification,
            // never off its id.
            return map.IsBossArea ? ShopRejection.InvalidLocation : ShopRejection.None;
        }

        // ---- listings ------------------------------------------------------------------

        /// <summary>
        /// Puts an owned item up for sale.
        /// </summary>
        /// <param name="shop">The seller's shop.</param>
        /// <param name="seller">Who is listing. Must own the shop.</param>
        /// <param name="inventory">Where the item currently is.</param>
        /// <param name="instance">The exact copy to list.</param>
        /// <param name="currency">What it is priced in.</param>
        /// <param name="unitPrice">The price. Integer, within the authored bounds.</param>
        /// <param name="rules">The authorities the item is checked against.</param>
        /// <param name="context">Registries and limits.</param>
        /// <remarks>
        /// On success the item leaves the bag. That is the reservation: there is no second
        /// lock mechanism and no flag for another system to miss.
        /// </remarks>
        public static ShopResult TryCreateListing(PlayerShop shop, CharacterId seller,
            ItemContainerState inventory, InstanceId instance, DefinitionId currency,
            int unitPrice, in ItemTransferRules.Context rules, in Context context)
        {
            if (shop == null || inventory == null || !context.IsUsable)
                return ShopResult.Rejected(ShopRejection.MissingContext, shop);

            if (!shop.IsOpen) return ShopResult.Rejected(ShopRejection.ShopUnavailable, shop);

            if (shop.Owner != seller)
                return ShopResult.Rejected(ShopRejection.NotTheOwner, shop);

            if (shop.ActiveListingCount >= context.Limits.MaxShopListings)
                return ShopResult.Rejected(ShopRejection.TooManyListings, shop);

            if (unitPrice < context.Limits.MinListingPrice
                || unitPrice > context.Limits.MaxListingPrice)
            {
                return ShopResult.Rejected(ShopRejection.InvalidPrice, shop);
            }

            ShopRejection resolved = ResolveCurrency(currency, context);
            if (resolved != ShopRejection.None) return ShopResult.Rejected(resolved, shop);

            int index = inventory.IndexOf(instance);
            if (index < 0) return ShopResult.Rejected(ShopRejection.ItemNotHeld, shop);

            ItemSlot slot = inventory.GetSlot(index);
            GameInstance held = slot.Content;

            if (shop.HasActiveListingFor(instance))
                return ShopResult.Rejected(ShopRejection.AlreadyListed, shop);

            ItemTransferBlock block = ItemTransferRules.CanTransfer(held, shop.OwnerId, rules);

            if (block != ItemTransferBlock.None)
            {
                return ShopResult.Rejected(ShopRejection.ItemBlocked, shop, null, block);
            }

            // ---- everything is resolved and nothing below can fail ---------------------

            int quantity = slot.Quantity;

            inventory.RemoveAt(index, quantity);
            held.TrySetLockState(ItemLockState.Listed);

            var listing = new PlayerShopListing(InstanceId.New(), shop.ShopId, seller,
                shop.OwnerId, held, quantity, currency, unitPrice, context.TimestampTicks);

            shop.TryAddListing(listing);

            return ShopResult.Accepted(shop, listing);
        }

        /// <summary>
        /// Withdraws a listing and returns the item.
        /// </summary>
        /// <remarks>
        /// The item comes back, never destroyed. Room is confirmed before the listing is
        /// settled, so a seller with a full bag is refused and keeps the listing rather than
        /// losing the item -- the same ordering the card-removal path uses, and for the same
        /// reason.
        /// </remarks>
        public static ShopResult TryCancelListing(PlayerShop shop, CharacterId seller,
            InstanceId listingId, ItemContainerState inventory, in Context context)
        {
            if (shop == null || inventory == null || !context.IsUsable)
                return ShopResult.Rejected(ShopRejection.MissingContext, shop);

            if (shop.Owner != seller)
                return ShopResult.Rejected(ShopRejection.NotTheOwner, shop);

            PlayerShopListing listing = shop.FindListing(listingId);
            if (listing == null) return ShopResult.Rejected(ShopRejection.UnknownListing, shop);

            if (!listing.IsActive)
                return ShopResult.Rejected(ShopRejection.ListingNotActive, shop, listing);

            GameInstance escrow = listing.Escrow;
            if (escrow == null) return ShopResult.Rejected(ShopRejection.MissingContext, shop);

            // Somewhere to land, established before the listing is taken down.
            if (!HasRoomFor(inventory, escrow, context.Items))
                return ShopResult.Rejected(ShopRejection.InventoryFull, shop, listing);

            // ---- everything is resolved and nothing below can fail ---------------------

            listing.TrySetState(ShopListingState.Cancelled);
            escrow.TrySetLockState(ItemLockState.Available);
            inventory.Add(escrow, context.Items);

            return ShopResult.Accepted(shop, listing);
        }

        // ---- purchase ------------------------------------------------------------------

        /// <summary>
        /// Buys a listing.
        /// </summary>
        /// <param name="shop">The shop holding it.</param>
        /// <param name="listingId">Which listing.</param>
        /// <param name="buyer">Who is buying.</param>
        /// <param name="buyerOwner">Their ownership identity.</param>
        /// <param name="buyerInventory">Where the item will go.</param>
        /// <param name="buyerWallet">Where the price comes from.</param>
        /// <param name="sellerWallet">Where the price goes.</param>
        /// <param name="request">The idempotency key. A repeat returns the original result.</param>
        /// <param name="context">Registries and the ledger.</param>
        /// <remarks>
        /// Every check runs first: the listing is still active, the buyer is not the seller,
        /// the currency resolves, the buyer can pay, the seller can receive, and the buyer's
        /// bag has room for this exact object. Only then does the boundary run, and inside it
        /// the money and the item move together.
        /// </remarks>
        public static ShopResult TryPurchase(PlayerShop shop, InstanceId listingId,
            CharacterId buyer, OwnerId buyerOwner, ItemContainerState buyerInventory,
            CharacterWalletState buyerWallet, CharacterWalletState sellerWallet,
            RequestId request, in Context context)
        {
            if (shop == null || buyerInventory == null || !context.IsUsable)
                return ShopResult.Rejected(ShopRejection.MissingContext, shop);

            // A retry finds the answer the first attempt already produced.
            if (context.Ledger != null && request.IsValid)
            {
                EconomyTransaction previous;
                if (context.Ledger.TryGetByRequest(request, out previous))
                {
                    return ShopResult.Accepted(shop, shop.FindListing(listingId), previous, true);
                }
            }

            if (!shop.IsOpen) return ShopResult.Rejected(ShopRejection.ShopUnavailable, shop);

            PlayerShopListing listing = shop.FindListing(listingId);
            if (listing == null) return ShopResult.Rejected(ShopRejection.UnknownListing, shop);

            // The first of two buyers takes it; the second finds this.
            if (!listing.IsActive)
                return ShopResult.Rejected(ShopRejection.ListingNotActive, shop, listing);

            if (listing.Seller == buyer)
                return ShopResult.Rejected(ShopRejection.CannotBuyOwnListing, shop, listing);

            GameInstance escrow = listing.Escrow;
            if (escrow == null) return ShopResult.Rejected(ShopRejection.MissingContext, shop);

            ShopRejection resolved = ResolveCurrency(listing.Currency, context);
            if (resolved != ShopRejection.None)
                return ShopResult.Rejected(resolved, shop, listing);

            if (buyerWallet == null || sellerWallet == null || context.Ledger == null)
                return ShopResult.Rejected(ShopRejection.MissingContext, shop, listing);

            EconomyRejection money = EconomyService.PlanTransfer(buyerWallet, sellerWallet,
                listing.Currency, listing.UnitPrice, context.Economy);

            if (money != EconomyRejection.None)
                return ShopResult.Rejected(Translate(money), shop, listing);

            // Capacity before payment. This is the ordering the brief's worked example
            // demands: a buyer with the gold and a full bag pays nothing.
            if (!HasRoomFor(buyerInventory, escrow, context.Items))
                return ShopResult.Rejected(ShopRejection.InventoryFull, shop, listing);

            // ---- everything is proven and nothing below can fail -----------------------

            listing.TrySetState(ShopListingState.Sold);

            var currencyEntries = new List<EconomyTransactionEntry>();

            EconomyService.ApplyPlannedTransfer(buyerWallet, sellerWallet, listing.Currency,
                listing.UnitPrice, context.Economy, currencyEntries, escrow.InstanceId);

            OwnerId from = escrow.Owner;
            Revision before = escrow.Revision;

            escrow.TrySetLockState(ItemLockState.Available);
            escrow.SetOwner(buyerOwner);
            buyerInventory.Add(escrow, context.Items);

            var stackable = escrow as ItemInstance;

            var itemEntries = new[]
            {
                new ItemTransactionEntry(escrow.InstanceId, escrow.DefinitionId, from, buyerOwner,
                    stackable == null ? 1 : stackable.Quantity, before, escrow.Revision)
            };

            EconomyResult audit = EconomyService.CommitExchange(EconomySource.PlayerShop, request,
                context.Economy, currencyEntries.ToArray(), itemEntries);

            return ShopResult.Accepted(shop, listing, audit.Transaction);
        }

        /// <summary>
        /// Whether a container could take one specific object.
        /// </summary>
        /// <remarks>Planned through the one ownership seam rather than by counting slots, so
        /// stack top-up behaves exactly as it will when the item actually arrives.</remarks>
        private static bool HasRoomFor(ItemContainerState inventory, GameInstance instance,
            IDefinitionRegistry<ItemDefinition> items)
        {
            // The escrowed object is in no container, so this is the incoming-only case.
            return ItemOwnershipTransfer.CanReceive(inventory, instance, items);
        }

        private static ShopRejection ResolveCurrency(DefinitionId currency, in Context context)
        {
            if (!currency.IsValid || context.Currencies == null)
                return ShopRejection.InvalidCurrency;

            CurrencyDefinition definition;
            if (!context.Currencies.TryGet(currency, out definition) || definition == null)
                return ShopRejection.InvalidCurrency;

            return definition.Enabled ? ShopRejection.None : ShopRejection.InvalidCurrency;
        }

        private static ShopRejection Translate(EconomyRejection reason)
        {
            switch (reason)
            {
                case EconomyRejection.InsufficientFunds: return ShopRejection.InsufficientFunds;
                case EconomyRejection.BalanceOverflow: return ShopRejection.BalanceOverflow;
                case EconomyRejection.SameWallet: return ShopRejection.CannotBuyOwnListing;
                case EconomyRejection.UnknownCurrency:
                case EconomyRejection.CurrencyDisabled: return ShopRejection.InvalidCurrency;
                default: return ShopRejection.MissingContext;
            }
        }
    }
}
