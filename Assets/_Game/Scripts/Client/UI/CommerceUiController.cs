using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the trade, player shop and wallet panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for trade, shops and currency.</b> Every change these panels
    /// can cause goes through a submit method here, and each calls
    /// <see cref="TradeService"/>, <see cref="PlayerShopService"/> or
    /// <see cref="EconomyService"/>. No view holds a wallet, a session or a listing, so there
    /// is nowhere else money could move.
    ///
    /// <b>It never mutates a balance itself.</b> Not one call to a wallet appears below;
    /// currency moves only as part of a trade or a purchase, through the one economy
    /// boundary. An architecture test asserts that.
    ///
    /// <b>Requests carry an idempotency key.</b> Every commit and purchase mints a
    /// <see cref="RequestId"/> and keeps it, so a retry of the same user action returns the
    /// original result instead of buying twice. The key is reset only when the operation
    /// finishes, which is what makes a double-clicked button safe.
    ///
    /// <b>Nothing is polled.</b> Panels rebuild when a revision moves.
    /// </remarks>
    public sealed class CommerceUiController : MonoBehaviour
    {
        private readonly List<CurrencyViewData> _wallet = new List<CurrencyViewData>();
        private readonly List<ShopListingViewData> _listings = new List<ShopListingViewData>();
        private readonly List<InstanceId> _myOffer = new List<InstanceId>();
        private readonly List<InstanceId> _theirOffer = new List<InstanceId>();

        private CharacterId _viewer;
        private OwnerId _owner;
        private ItemContainerState _inventory;
        private CharacterWalletState _wallets;
        private TradeSession _trade;
        private PlayerShop _shop;

        private IDefinitionRegistry<ItemDefinition> _items;
        private IDefinitionRegistry<CurrencyDefinition> _currencies;
        private IDefinitionRegistry<MapDefinition> _maps;
        private TransactionLedger _ledger;
        private SocialConfiguration _configuration;

        private ItemTransferRules.Context _rules;
        private TradeService.Context _tradeContext;
        private bool _tradeBound;

        private RequestId _pendingTrade;
        private RequestId _pendingPurchase;
        private bool _bound;
        private Revision _lastTradeRevision;
        private Revision _lastShopRevision;
        private Revision _lastWalletRevision;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Resolves display names. Optional.</summary>
        public SocialAdapter.NameResolver Names { get; set; }

        /// <summary>Caller-supplied time. Nothing here reads a clock.</summary>
        public long TimestampTicks { get; set; }

        public TradeResult LastTradeResult { get; private set; }

        public ShopResult LastShopResult { get; private set; }

        public TradeViewData Trade { get; private set; }

        public PlayerShopViewData Shop { get; private set; }

        public IReadOnlyList<CurrencyViewData> Wallet => _wallet;

        public IReadOnlyList<ShopListingViewData> Listings => _listings;

        /// <summary>The item instances the viewer has put on the table.</summary>
        public IReadOnlyList<InstanceId> MyOffer => _myOffer;

        /// <summary>The item instances the other side has put on the table.</summary>
        public IReadOnlyList<InstanceId> TheirOffer => _theirOffer;

        /// <summary>Raised when a trade completes, carrying the audit record.</summary>
        public event System.Action<EconomyTransaction> TradeCompleted;

        /// <summary>Raised when a purchase completes, carrying the audit record.</summary>
        public event System.Action<EconomyTransaction> PurchaseCompleted;

        /// <summary>Points the UI at a character's commerce state.</summary>
        public void Bind(CharacterId viewer, OwnerId owner, ItemContainerState inventory,
            CharacterWalletState wallet, IDefinitionRegistry<ItemDefinition> items,
            IDefinitionRegistry<CurrencyDefinition> currencies = null,
            TransactionLedger ledger = null,
            IDefinitionRegistry<MapDefinition> maps = null,
            SocialConfiguration configuration = null,
            ItemTransferRules.Context rules = default)
        {
            _viewer = viewer;
            _owner = owner;
            _inventory = inventory;
            _wallets = wallet;
            _items = items;
            _currencies = currencies;
            _ledger = ledger;
            _maps = maps;
            _configuration = configuration;
            _rules = rules;

            _bound = true;
            Refresh();
        }

        /// <summary>
        /// Points the UI at an open trade.
        /// </summary>
        /// <remarks>The whole trade context is supplied by the caller because it names the
        /// <em>other</em> player's inventory and wallet, which this client does not own and
        /// must not assemble for itself.</remarks>
        public void BindTrade(TradeSession session, in TradeService.Context context)
        {
            _trade = session;
            _tradeContext = context;
            _tradeBound = session != null;
            _pendingTrade = RequestId.New();

            Refresh();
        }

        /// <summary>Points the UI at a shop being browsed or run.</summary>
        public void BindShop(PlayerShop shop)
        {
            _shop = shop;
            _pendingPurchase = RequestId.None;
            Refresh();
        }

        /// <summary>The registries the adapter reads through.</summary>
        public SocialAdapter.Context ViewContext =>
            new SocialAdapter.Context(_currencies, null, _configuration, Names);

        private PlayerShopService.Context ShopContext =>
            new PlayerShopService.Context(_items, _currencies, _ledger, _maps, _configuration,
                TimestampTicks);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current gameplay state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            SocialAdapter.BuildWallet(_wallets, ViewContext, _wallet);

            Trade = SocialAdapter.BuildTrade(_trade, _viewer, ViewContext);
            Shop = SocialAdapter.BuildShop(_shop, _viewer, ViewContext);

            SocialAdapter.BuildListings(_shop, _viewer, _wallets, ViewContext, _listings);

            if (_trade != null)
            {
                SocialAdapter.BuildTradeItems(_trade.OfferOf(_viewer), _myOffer);
                SocialAdapter.BuildTradeItems(_trade.CounterpartyOf(_viewer), _theirOffer);
                _lastTradeRevision = _trade.Revision;
            }
            else
            {
                _myOffer.Clear();
                _theirOffer.Clear();
            }

            if (_shop != null) _lastShopRevision = _shop.Revision;
            if (_wallets != null) _lastWalletRevision = _wallets.Revision;
        }

        /// <summary>Redraws only if something actually changed.</summary>
        public bool RefreshIfChanged()
        {
            if (!_bound) return false;

            bool tradeMoved = _trade != null && _trade.Revision != _lastTradeRevision;
            bool shopMoved = _shop != null && _shop.Revision != _lastShopRevision;
            bool walletMoved = _wallets != null && _wallets.Revision != _lastWalletRevision;

            if (!tradeMoved && !shopMoved && !walletMoved) return false;

            Refresh();
            return true;
        }

        // ---- trade commands ------------------------------------------------------------

        /// <summary>Puts one of the viewer's items on the table.</summary>
        public TradeResult SubmitOfferItem(InstanceId instance)
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryOfferItem(_trade, _viewer, instance, _tradeContext);
            Refresh();
            return LastTradeResult;
        }

        /// <summary>Takes one of the viewer's items back off the table.</summary>
        public TradeResult SubmitWithdrawItem(InstanceId instance)
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryWithdrawItem(_trade, _viewer, instance,
                _tradeContext);

            Refresh();
            return LastTradeResult;
        }

        /// <summary>Sets how much of one currency the viewer is offering.</summary>
        public TradeResult SubmitOfferCurrency(DefinitionId currency, int amount)
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryOfferCurrency(_trade, _viewer, currency, amount,
                _tradeContext);

            Refresh();
            return LastTradeResult;
        }

        /// <summary>Agrees to the offers as they stand. The first of two stages.</summary>
        public TradeResult SubmitAccept()
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryAccept(_trade, _viewer, _tradeContext);
            Refresh();
            return LastTradeResult;
        }

        /// <summary>Withdraws agreement without changing the offer.</summary>
        public TradeResult SubmitRetract()
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryRetract(_trade, _viewer, _tradeContext);
            Refresh();
            return LastTradeResult;
        }

        /// <summary>
        /// Confirms and commits. The second of two stages.
        /// </summary>
        /// <remarks>The same request key is used for every attempt at this trade, so a
        /// double-click or a retried message returns the original result rather than trading
        /// twice.</remarks>
        public TradeResult SubmitConfirm()
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            if (!_pendingTrade.IsValid) _pendingTrade = RequestId.New();

            LastTradeResult = TradeService.TryCommit(_trade, _pendingTrade, _tradeContext);

            Refresh();

            if (LastTradeResult.IsAccepted && LastTradeResult.Transaction != null)
            {
                var handler = TradeCompleted;
                if (handler != null) handler(LastTradeResult.Transaction);
            }

            return LastTradeResult;
        }

        /// <summary>Calls the trade off.</summary>
        public TradeResult SubmitCancelTrade()
        {
            if (!_tradeBound) return TradeResult.Rejected(TradeRejection.MissingContext);

            LastTradeResult = TradeService.TryCancel(_trade, _viewer);
            Refresh();
            return LastTradeResult;
        }

        // ---- shop commands -------------------------------------------------------------

        /// <summary>Opens a shop for the viewer at a place in the world.</summary>
        public ShopResult SubmitCreateShop(string name, WorldPlacement placement)
        {
            LastShopResult = PlayerShopService.TryCreateShop(_viewer, _owner, name, placement,
                ShopContext);

            if (LastShopResult.IsAccepted) _shop = LastShopResult.Shop;

            Refresh();
            return LastShopResult;
        }

        /// <summary>Puts one of the viewer's items up for sale.</summary>
        public ShopResult SubmitCreateListing(InstanceId instance, DefinitionId currency,
            int unitPrice)
        {
            LastShopResult = PlayerShopService.TryCreateListing(_shop, _viewer, _inventory,
                instance, currency, unitPrice, _rules, ShopContext);

            Refresh();
            return LastShopResult;
        }

        /// <summary>Withdraws a listing and takes the item back.</summary>
        public ShopResult SubmitCancelListing(InstanceId listingId)
        {
            LastShopResult = PlayerShopService.TryCancelListing(_shop, _viewer, listingId,
                _inventory, ShopContext);

            Refresh();
            return LastShopResult;
        }

        /// <summary>
        /// Buys a listing from somebody else's shop.
        /// </summary>
        /// <remarks>The seller's wallet is supplied by the caller: this client does not own
        /// it and must not reach for it. A repeated request returns the original result.</remarks>
        public ShopResult SubmitPurchase(InstanceId listingId, CharacterWalletState sellerWallet)
        {
            if (!_pendingPurchase.IsValid) _pendingPurchase = RequestId.New();

            LastShopResult = PlayerShopService.TryPurchase(_shop, listingId, _viewer, _owner,
                _inventory, _wallets, sellerWallet, _pendingPurchase, ShopContext);

            Refresh();

            if (LastShopResult.IsAccepted && LastShopResult.Transaction != null)
            {
                var handler = PurchaseCompleted;
                if (handler != null) handler(LastShopResult.Transaction);

                // The next purchase is a different request; this one is settled.
                _pendingPurchase = RequestId.None;
            }

            return LastShopResult;
        }
    }
}
