using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Player shops: listings, reservation, purchase and cancellation.
    /// </summary>
    /// <remarks>
    /// The escrow model carries these tests. A listed item is in no container, so the
    /// invariants -- it cannot be equipped, traded, listed twice or sold twice -- hold because
    /// there is nothing for another system to find, not because every system remembers to
    /// check a flag. Several tests below check that absence directly.
    /// </remarks>
    [TestFixture]
    internal sealed class PlayerShopTests : SocialTestBase
    {
        private ItemContainerState _sellerBag;
        private ItemContainerState _buyerBag;
        private CharacterWalletState _sellerWallet;
        private CharacterWalletState _buyerWallet;
        private PlayerShop _shop;

        private DefinitionId GoldId => new DefinitionId(Gold);

        [SetUp]
        public void SetUpShop()
        {
            _sellerBag = Container(AliceOwner);
            _buyerBag = Container(BobOwner);
            _sellerWallet = Wallet(AliceOwner, Alice, 100);
            _buyerWallet = Wallet(BobOwner, Bob, 1000);

            ShopResult created = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Alice's Wares",
                new WorldPlacement(new DefinitionId(TownMap), 1f, 0f, 2f), ShopContext());

            _shop = created.Shop;
        }

        private ItemInstance GiveSeller(string id, int quantity = 1)
        {
            ItemInstance item = Stack(id, AliceOwner, quantity);
            _sellerBag.Add(item, Items);
            return item;
        }

        private EquipmentInstance GiveSellerEquipment(string id)
        {
            EquipmentInstance item = Equipment(id, AliceOwner);
            _sellerBag.Add(item, Items);
            return item;
        }

        private PlayerShopListing List(InstanceId item, int price = 250)
        {
            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag, item,
                GoldId, price, Rules(), ShopContext());

            return result.Listing;
        }

        // ---- shop ----------------------------------------------------------------------

        [Test]
        public void A_shop_opens_at_an_authored_place()
        {
            Assert.That(_shop, Is.Not.Null);
            Assert.That(_shop.Owner, Is.EqualTo(Alice));
            Assert.That(_shop.IsOpen, Is.True);
            Assert.That(_shop.Placement.Map, Is.EqualTo(new DefinitionId(TownMap)));
            Assert.That(_shop.ShopId.IsValid, Is.True);
        }

        [Test]
        public void A_shop_cannot_open_where_content_forbids_it()
        {
            ShopResult result = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Bad Spot",
                new WorldPlacement(new DefinitionId(BossMap), 0f, 0f, 0f), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InvalidLocation));
        }

        [Test]
        public void A_shop_cannot_open_on_a_map_that_does_not_exist()
        {
            ShopResult result = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Nowhere",
                new WorldPlacement(new DefinitionId("map.gone"), 0f, 0f, 0f), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InvalidLocation));
        }

        [Test]
        public void A_shop_holds_no_engine_type()
        {
            // Structural: gameplay must stay engine-free, so a shop's position is data.
            System.Reflection.PropertyInfo[] properties = typeof(PlayerShop).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("Transform"), property.Name);
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("Vector3"), property.Name);
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("GameObject"),
                    property.Name);
            }
        }

        // ---- listings ------------------------------------------------------------------

        [Test]
        public void Listing_an_item_takes_it_out_of_the_bag()
        {
            ItemInstance potion = GiveSeller(Potion, 5);

            PlayerShopListing listing = List(potion.InstanceId);

            Assert.That(listing, Is.Not.Null);
            Assert.That(listing.IsActive, Is.True);
            Assert.That(listing.Item, Is.EqualTo(potion.InstanceId));
            Assert.That(listing.Quantity, Is.EqualTo(5));
            Assert.That(_sellerBag.IndexOf(potion.InstanceId), Is.EqualTo(-1),
                "the item is in exactly one place, and that place is the listing");
            Assert.That(potion.LockState, Is.EqualTo(ItemLockState.Listed));
        }

        [Test]
        public void A_listed_item_cannot_be_listed_again()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            List(potion.InstanceId);

            ShopResult again = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                potion.InstanceId, GoldId, 100, Rules(), ShopContext());

            Assert.That(again.Reason, Is.EqualTo(ShopRejection.ItemNotHeld),
                "it is no longer in the bag, so there is nothing to list");
        }

        [Test]
        public void A_listed_item_cannot_be_traded()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            List(potion.InstanceId);

            TradeSession session = TradeService.Open(Alice, AliceOwner, Bob, BobOwner);

            TradeService.Context trade = TradeContext(
                Participant(Alice, AliceOwner, _sellerBag, _sellerWallet),
                Participant(Bob, BobOwner, _buyerBag, _buyerWallet));

            Assert.That(TradeService.TryOfferItem(session, Alice, potion.InstanceId, trade)
                .Reason, Is.EqualTo(TradeRejection.ItemNotHeld));
        }

        [Test]
        public void The_lock_state_also_refuses_a_listed_item_directly()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            List(potion.InstanceId);

            // Second line of defence: even holding the object, the rules refuse it.
            Assert.That(ItemTransferRules.CanTransfer(potion, AliceOwner, Rules()),
                Is.EqualTo(ItemTransferBlock.Listed));
        }

        [Test]
        public void Only_the_shop_owner_may_list()
        {
            ItemInstance potion = GiveSeller(Potion, 5);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Bob, _sellerBag,
                potion.InstanceId, GoldId, 100, Rules(), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.NotTheOwner));
            Assert.That(_sellerBag.IndexOf(potion.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void A_non_tradable_item_cannot_be_listed()
        {
            ItemInstance bound = GiveSeller(Bound);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                bound.InstanceId, GoldId, 100, Rules(), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.ItemBlocked));
            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.NotTradable));
            Assert.That(_sellerBag.IndexOf(bound.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void A_price_below_the_authored_minimum_is_refused()
        {
            ItemInstance potion = GiveSeller(Potion, 5);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                potion.InstanceId, GoldId, 0, Rules(), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InvalidPrice));
            Assert.That(_sellerBag.IndexOf(potion.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void A_price_above_an_authored_maximum_is_refused()
        {
            SocialConfiguration capped = AddConfiguration(maxPrice: 500);
            var context = new PlayerShopService.Context(Items, Currencies, Ledger, Maps, capped);

            ItemInstance potion = GiveSeller(Potion, 5);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                potion.InstanceId, GoldId, 5000, Rules(), context);

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InvalidPrice));
        }

        [Test]
        public void An_unknown_currency_is_refused()
        {
            ItemInstance potion = GiveSeller(Potion, 5);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                potion.InstanceId, new DefinitionId("currency.gone"), 100, Rules(),
                ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InvalidCurrency));
        }

        [Test]
        public void The_listing_limit_is_authored()
        {
            SocialConfiguration limited = AddConfiguration(maxListings: 1);
            var context = new PlayerShopService.Context(Items, Currencies, Ledger, Maps, limited);

            ItemInstance first = GiveSeller(Potion, 1);
            EquipmentInstance second = GiveSellerEquipment(Sword);

            PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag, first.InstanceId,
                GoldId, 100, Rules(), context);

            ShopResult result = PlayerShopService.TryCreateListing(_shop, Alice, _sellerBag,
                second.InstanceId, GoldId, 100, Rules(), context);

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.TooManyListings));
        }

        // ---- cancel --------------------------------------------------------------------

        [Test]
        public void Cancelling_returns_the_same_item_to_the_seller()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            InstanceId identity = potion.InstanceId;

            PlayerShopListing listing = List(potion.InstanceId);

            ShopResult result = PlayerShopService.TryCancelListing(_shop, Alice,
                listing.ListingId, _sellerBag, ShopContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(listing.State, Is.EqualTo(ShopListingState.Cancelled));
            Assert.That(_sellerBag.IndexOf(identity), Is.GreaterThanOrEqualTo(0),
                "the same copy comes back, never a new one");
            Assert.That(potion.LockState, Is.EqualTo(ItemLockState.Available));
            Assert.That(potion.Owner, Is.EqualTo(AliceOwner));
        }

        [Test]
        public void Cancelling_never_destroys_the_item()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            PlayerShopService.TryCancelListing(_shop, Alice, listing.ListingId, _sellerBag,
                ShopContext());

            Assert.That(_sellerBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));
        }

        [Test]
        public void A_full_bag_refuses_the_cancel_rather_than_losing_the_item()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            // Fill every slot while the item is in escrow.
            for (int i = 0; i < _sellerBag.Capacity; i++)
            {
                _sellerBag.Add(Equipment(Sword, AliceOwner), Items);
            }

            ShopResult result = PlayerShopService.TryCancelListing(_shop, Alice,
                listing.ListingId, _sellerBag, ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InventoryFull));
            Assert.That(listing.IsActive, Is.True, "the listing survives, and so does the item");
        }

        [Test]
        public void Only_the_owner_may_cancel()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            Assert.That(PlayerShopService.TryCancelListing(_shop, Bob, listing.ListingId,
                _buyerBag, ShopContext()).Reason, Is.EqualTo(ShopRejection.NotTheOwner));
            Assert.That(listing.IsActive, Is.True);
        }

        [Test]
        public void A_cancelled_listing_cannot_be_cancelled_again()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            PlayerShopService.TryCancelListing(_shop, Alice, listing.ListingId, _sellerBag,
                ShopContext());

            Assert.That(PlayerShopService.TryCancelListing(_shop, Alice, listing.ListingId,
                _sellerBag, ShopContext()).Reason, Is.EqualTo(ShopRejection.ListingNotActive));
            Assert.That(_sellerBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5),
                "the item did not come back twice");
        }

        // ---- purchase ------------------------------------------------------------------

        [Test]
        public void A_purchase_moves_the_item_and_the_money_exactly_once()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            InstanceId identity = potion.InstanceId;
            Revision before = potion.Revision;

            PlayerShopListing listing = List(potion.InstanceId, 250);

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(listing.State, Is.EqualTo(ShopListingState.Sold));

            Assert.That(_buyerBag.IndexOf(identity), Is.GreaterThanOrEqualTo(0));
            Assert.That(potion.Owner, Is.EqualTo(BobOwner));
            Assert.That(potion.InstanceId, Is.EqualTo(identity), "the exact copy, not a clone");

            Assert.That(_buyerWallet.BalanceOf(GoldId), Is.EqualTo(750));
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(350),
                "the seller is credited exactly once");

            // Lock released, ownership changed: two mutations on the escrowed object.
            Assert.That(potion.LockState, Is.EqualTo(ItemLockState.Available));
            Assert.That(potion.Revision.Value, Is.GreaterThan(before.Value));
        }

        [Test]
        public void A_buyer_who_cannot_pay_gets_nothing_and_pays_nothing()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 5000);

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InsufficientFunds));
            Assert.That(_buyerWallet.BalanceOf(GoldId), Is.EqualTo(1000));
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(100));
            Assert.That(listing.IsActive, Is.True);
            Assert.That(potion.Owner, Is.EqualTo(AliceOwner));
        }

        [Test]
        public void A_buyer_with_a_full_bag_pays_nothing()
        {
            // The brief's worked example: money and capacity must fail together.
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 500);

            var fullBag = new ItemContainerState(BobOwner, 1);
            fullBag.Add(Stack(Bound, BobOwner), Items);

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, fullBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.InventoryFull));
            Assert.That(_buyerWallet.BalanceOf(GoldId), Is.EqualTo(1000),
                "the buyer must not lose the price for an item they did not receive");
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(100));
            Assert.That(listing.IsActive, Is.True, "and the listing is still for sale");
        }

        [Test]
        public void A_seller_cannot_buy_their_own_listing()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Alice,
                AliceOwner, _sellerBag, _sellerWallet, _sellerWallet, RequestId.New(),
                ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.CannotBuyOwnListing));
        }

        [Test]
        public void Only_one_of_two_competing_buyers_succeeds()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 250);

            var carolBag = Container(CarolOwner);
            CharacterWalletState carolWallet = Wallet(CarolOwner, Carol, 1000);

            ShopResult first = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            ShopResult second = PlayerShopService.TryPurchase(_shop, listing.ListingId, Carol,
                CarolOwner, carolBag, carolWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Reason, Is.EqualTo(ShopRejection.ListingNotActive));

            Assert.That(carolWallet.BalanceOf(GoldId), Is.EqualTo(1000), "the loser paid nothing");
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(350),
                "the seller was paid once, not twice");
            Assert.That(carolBag.IndexOf(potion.InstanceId), Is.EqualTo(-1));
            Assert.That(_buyerBag.IndexOf(potion.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void A_listing_cannot_sell_twice_even_to_the_same_buyer()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 100);

            PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob, BobOwner, _buyerBag,
                _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            ShopResult again = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(again.Reason, Is.EqualTo(ShopRejection.ListingNotActive));
            Assert.That(_buyerWallet.BalanceOf(GoldId), Is.EqualTo(900), "charged once");
        }

        [Test]
        public void The_same_purchase_request_twice_buys_once()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 300);

            RequestId request = RequestId.New();

            ShopResult first = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, request, ShopContext());

            ShopResult second = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, request, ShopContext());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.TransactionId, Is.EqualTo(first.TransactionId));
            Assert.That(_buyerWallet.BalanceOf(GoldId), Is.EqualTo(700), "charged once");
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(400), "credited once");
        }

        [Test]
        public void A_cancelled_listing_cannot_be_bought()
        {
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId);

            PlayerShopService.TryCancelListing(_shop, Alice, listing.ListingId, _sellerBag,
                ShopContext());

            Assert.That(PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob, BobOwner,
                _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext()).Reason,
                Is.EqualTo(ShopRejection.ListingNotActive));
        }

        [Test]
        public void Equipment_sells_like_any_other_item()
        {
            EquipmentInstance sword = GiveSellerEquipment(Sword);
            PlayerShopListing listing = List(sword.InstanceId, 400);

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(sword.Owner, Is.EqualTo(BobOwner));
            Assert.That(_buyerBag.IndexOf(sword.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void The_seller_need_not_be_present()
        {
            // Nothing about a purchase consults the seller's session; only their wallet and
            // the authoritative listing.
            ItemInstance potion = GiveSeller(Potion, 5);
            PlayerShopListing listing = List(potion.InstanceId, 200);

            _sellerBag = null;   // the seller is gone entirely

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_sellerWallet.BalanceOf(GoldId), Is.EqualTo(300));
        }

        // ---- audit ---------------------------------------------------------------------

        [Test]
        public void A_purchase_produces_one_auditable_transaction()
        {
            ItemInstance potion = GiveSeller(Potion, 3);
            PlayerShopListing listing = List(potion.InstanceId, 250);

            int before = Ledger.Count;

            ShopResult result = PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob,
                BobOwner, _buyerBag, _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            Assert.That(Ledger.Count, Is.EqualTo(before + 1));

            EconomyTransaction audit = result.Transaction;

            Assert.That(audit.Source, Is.EqualTo(EconomySource.PlayerShop));
            Assert.That(audit.CurrencyEntries.Count, Is.EqualTo(2));
            Assert.That(audit.ItemEntries.Count, Is.EqualTo(1));
            Assert.That(audit.CurrencyBalances(), Is.True);
            Assert.That(audit.CurrencyEntries[0].RelatedItem, Is.EqualTo(potion.InstanceId),
                "the money is tied to the item it paid for");
        }

        [Test]
        public void An_items_ownership_history_survives_a_sale()
        {
            ItemInstance potion = GiveSeller(Potion, 3);
            PlayerShopListing listing = List(potion.InstanceId, 100);

            PlayerShopService.TryPurchase(_shop, listing.ListingId, Bob, BobOwner, _buyerBag,
                _buyerWallet, _sellerWallet, RequestId.New(), ShopContext());

            var history = new List<ItemTransactionEntry>();
            Ledger.CollectItemHistory(potion.InstanceId, history);

            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].From, Is.EqualTo(AliceOwner));
            Assert.That(history[0].To, Is.EqualTo(BobOwner));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void A_player_shop_is_not_an_npc_shop()
        {
            // Phase 11's ShopDefinition is authored content; this is runtime state. Neither
            // service knows the other's type.
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs"))
            {
                Assert.That(code, Does.Not.Contain("ShopDefinition"));
                Assert.That(code, Does.Not.Contain("ShopEntry"));
                Assert.That(code, Does.Not.Contain("NpcInteractionService"));
            }

            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/NpcInteractionService.cs"))
            {
                Assert.That(code, Does.Not.Contain("PlayerShop"));
            }
        }

        [Test]
        public void No_price_or_item_is_written_in_the_shop_service()
        {
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"item."));
                Assert.That(code, Does.Not.Contain("\"currency."));
                Assert.That(code, Does.Not.Contain("\"map."));
                Assert.That(code, Does.Not.Contain("\"shop."));
            }
        }

        [Test]
        public void There_is_no_shop_specific_item_or_inventory_type()
        {
            System.Type[] types = typeof(PlayerShopService).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("ShopItem"));
                Assert.That(type.Name, Is.Not.EqualTo("ShopInventory"));
                Assert.That(type.Name, Is.Not.EqualTo("ShopItemTransfer"));
                Assert.That(type.Name, Is.Not.EqualTo("TradeInventory"));
            }
        }
    }
}
