using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Loot into trade and shops, the shared seams, and the architecture rules behind them.
    /// </summary>
    /// <remarks>
    /// The load-bearing claim of this phase is that trade and player shops share one
    /// ownership seam, one economy boundary and one set of transfer rules. These tests hold
    /// it two ways: by driving both paths end to end from a monster kill, and by reading the
    /// sources for a second implementation.
    /// </remarks>
    [TestFixture]
    internal sealed class CommerceIntegrationTests : SocialTestBase
    {
        private DefinitionId GoldId => new DefinitionId(Gold);

        // ---- loot to commerce ----------------------------------------------------------

        /// <summary>Puts a dropped item into a bag the way loot pickup would.</summary>
        private ItemInstance Dropped(string id, OwnerId owner, ItemContainerState bag,
            int quantity = 1)
        {
            var item = new ItemInstance(InstanceId.New(), new DefinitionId(id), owner, quantity);
            bag.Add(item, Items);
            return item;
        }

        [Test]
        public void A_dropped_item_can_be_traded_with_no_special_handling()
        {
            ItemContainerState aliceBag = Container(AliceOwner);
            ItemContainerState bobBag = Container(BobOwner);

            ItemInstance loot = Dropped(Potion, AliceOwner, aliceBag, 3);

            TradeSession session = TradeService.Open(Alice, AliceOwner, Bob, BobOwner);

            TradeService.Context trade = TradeContext(
                Participant(Alice, AliceOwner, aliceBag, Wallet(AliceOwner, Alice)),
                Participant(Bob, BobOwner, bobBag, Wallet(BobOwner, Bob)));

            TradeService.TryOfferItem(session, Alice, loot.InstanceId, trade);
            TradeService.TryAccept(session, Alice, trade);
            TradeService.TryAccept(session, Bob, trade);

            Assert.That(TradeService.TryCommit(session, RequestId.New(), trade).IsAccepted,
                Is.True);
            Assert.That(bobBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(3));
        }

        [Test]
        public void A_dropped_item_can_be_sold_with_no_special_handling()
        {
            ItemContainerState aliceBag = Container(AliceOwner);
            ItemContainerState bobBag = Container(BobOwner);

            CharacterWalletState sellerWallet = Wallet(AliceOwner, Alice);
            CharacterWalletState buyerWallet = Wallet(BobOwner, Bob, 1000);

            ItemInstance loot = Dropped(Potion, AliceOwner, aliceBag, 3);

            PlayerShop shop = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Loot",
                new WorldPlacement(new DefinitionId(TownMap), 0f, 0f, 0f), ShopContext()).Shop;

            PlayerShopListing listing = PlayerShopService.TryCreateListing(shop, Alice, aliceBag,
                loot.InstanceId, GoldId, 200, Rules(), ShopContext()).Listing;

            ShopResult purchase = PlayerShopService.TryPurchase(shop, listing.ListingId, Bob,
                BobOwner, bobBag, buyerWallet, sellerWallet, RequestId.New(), ShopContext());

            Assert.That(purchase.IsAccepted, Is.True);
            Assert.That(bobBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(3));
            Assert.That(sellerWallet.BalanceOf(GoldId), Is.EqualTo(200));
        }

        [Test]
        public void Inventory_to_listing_to_purchase_returns_to_an_inventory()
        {
            ItemContainerState aliceBag = Container(AliceOwner);
            ItemContainerState bobBag = Container(BobOwner);

            EquipmentInstance sword = Equipment(Sword, AliceOwner);
            aliceBag.Add(sword, Items);

            PlayerShop shop = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Arms",
                new WorldPlacement(new DefinitionId(TownMap), 0f, 0f, 0f), ShopContext()).Shop;

            PlayerShopListing listing = PlayerShopService.TryCreateListing(shop, Alice, aliceBag,
                sword.InstanceId, GoldId, 300, Rules(), ShopContext()).Listing;

            // In escrow: in neither bag.
            Assert.That(aliceBag.IndexOf(sword.InstanceId), Is.EqualTo(-1));
            Assert.That(bobBag.IndexOf(sword.InstanceId), Is.EqualTo(-1));

            PlayerShopService.TryPurchase(shop, listing.ListingId, Bob, BobOwner, bobBag,
                Wallet(BobOwner, Bob, 1000), Wallet(AliceOwner, Alice), RequestId.New(),
                ShopContext());

            Assert.That(bobBag.IndexOf(sword.InstanceId), Is.GreaterThanOrEqualTo(0));
            Assert.That(aliceBag.IndexOf(sword.InstanceId), Is.EqualTo(-1),
                "one item, one bag, at every point");
        }

        [Test]
        public void Currency_is_conserved_across_a_trade_and_a_sale()
        {
            CharacterWalletState alice = Wallet(AliceOwner, Alice, 1000);
            CharacterWalletState bob = Wallet(BobOwner, Bob, 1000);

            int total = alice.BalanceOf(GoldId) + bob.BalanceOf(GoldId);

            // A trade.
            ItemContainerState aliceBag = Container(AliceOwner);
            ItemContainerState bobBag = Container(BobOwner);

            TradeSession session = TradeService.Open(Alice, AliceOwner, Bob, BobOwner);

            TradeService.Context trade = TradeContext(
                Participant(Alice, AliceOwner, aliceBag, alice),
                Participant(Bob, BobOwner, bobBag, bob));

            TradeService.TryOfferCurrency(session, Alice, GoldId, 250, trade);
            TradeService.TryAccept(session, Alice, trade);
            TradeService.TryAccept(session, Bob, trade);
            TradeService.TryCommit(session, RequestId.New(), trade);

            // A sale, the other way.
            ItemInstance potion = Dropped(Potion, BobOwner, bobBag, 1);

            PlayerShop shop = PlayerShopService.TryCreateShop(Bob, BobOwner, "Bob's",
                new WorldPlacement(new DefinitionId(TownMap), 0f, 0f, 0f), ShopContext()).Shop;

            PlayerShopListing listing = PlayerShopService.TryCreateListing(shop, Bob, bobBag,
                potion.InstanceId, GoldId, 400, Rules(), ShopContext()).Listing;

            PlayerShopService.TryPurchase(shop, listing.ListingId, Alice, AliceOwner, aliceBag,
                alice, bob, RequestId.New(), ShopContext());

            Assert.That(alice.BalanceOf(GoldId) + bob.BalanceOf(GoldId), Is.EqualTo(total),
                "money moved twice and was neither created nor destroyed");
            Assert.That(alice.BalanceOf(GoldId), Is.EqualTo(350));
            Assert.That(bob.BalanceOf(GoldId), Is.EqualTo(1650));
        }

        [Test]
        public void The_ledger_reconciles_against_every_wallet()
        {
            CharacterWalletState alice = Wallet(AliceOwner, Alice, 1000);
            CharacterWalletState bob = Wallet(BobOwner, Bob, 500);

            EconomyService.TryTransfer(alice, bob, GoldId, 300, EconomySource.PlayerTrade,
                RequestId.New(), Economy());
            EconomyService.TryTransfer(bob, alice, GoldId, 100, EconomySource.PlayerShop,
                RequestId.New(), Economy());

            Assert.That(Ledger.NetFor(AliceOwner, GoldId), Is.EqualTo(alice.BalanceOf(GoldId)));
            Assert.That(Ledger.NetFor(BobOwner, GoldId), Is.EqualTo(bob.BalanceOf(GoldId)));
        }

        // ---- phase 12 regression -------------------------------------------------------

        [Test]
        public void A_socketed_card_cannot_be_sold_either()
        {
            ItemContainerState aliceBag = Container(AliceOwner);

            ItemInstance card = Dropped(Card, AliceOwner, aliceBag);
            EquipmentInstance sword = Equipment(Sword, AliceOwner);

            sword.AddCard(new EquipmentCardSocket(new DefinitionId(Card), 0, card.InstanceId));

            PlayerShop shop = PlayerShopService.TryCreateShop(Alice, AliceOwner, "Cards",
                new WorldPlacement(new DefinitionId(TownMap), 0f, 0f, 0f), ShopContext()).Shop;

            ShopResult result = PlayerShopService.TryCreateListing(shop, Alice, aliceBag,
                card.InstanceId, GoldId, 100, Rules(null, null, new[] { sword }), ShopContext());

            Assert.That(result.Reason, Is.EqualTo(ShopRejection.ItemBlocked));
            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.Socketed));
        }

        [Test]
        public void Trade_and_shop_refuse_a_socketed_card_for_the_same_reason()
        {
            ItemContainerState aliceBag = Container(AliceOwner);

            ItemInstance card = Dropped(Card, AliceOwner, aliceBag);
            EquipmentInstance sword = Equipment(Sword, AliceOwner);
            sword.AddCard(new EquipmentCardSocket(new DefinitionId(Card), 0, card.InstanceId));

            ItemTransferRules.Context rules = Rules(null, null, new[] { sword });

            // One rule, asked once, answering for both systems.
            Assert.That(ItemTransferRules.CanTransfer(card, AliceOwner, rules),
                Is.EqualTo(ItemTransferBlock.Socketed));
        }

        [Test]
        public void Pet_ownership_is_separate_from_item_ownership()
        {
            var pet = new PetInstance(InstanceId.New(), new DefinitionId("pet.a"), AliceOwner);
            ItemContainerState bag = Container(AliceOwner);

            // A pet is not something a container accepts, so it cannot enter commerce at all.
            ItemContainerResult added = bag.Add(pet, Items);

            Assert.That(added.IsAccepted, Is.False);
            Assert.That(bag.IndexOf(pet.InstanceId), Is.EqualTo(-1));
        }

        [Test]
        public void Card_sockets_and_status_stones_are_still_separate()
        {
            EquipmentInstance sword = Equipment(Sword, AliceOwner);

            sword.AddCard(new EquipmentCardSocket(new DefinitionId(Card), 0, InstanceId.New()));

            Assert.That(sword.CardCount, Is.EqualTo(1));
            Assert.That(sword.EnchantCount, Is.EqualTo(0));
            Assert.That(sword.IsSocketOccupied(0), Is.False);
        }

        // ---- lock model ----------------------------------------------------------------

        [Test]
        public void An_instance_starts_unlocked()
        {
            ItemInstance potion = Stack(Potion, AliceOwner);

            Assert.That(potion.LockState, Is.EqualTo(ItemLockState.Available));
            Assert.That(potion.IsLocked, Is.False);
        }

        [Test]
        public void One_lock_cannot_silently_replace_another()
        {
            ItemInstance potion = Stack(Potion, AliceOwner);

            Assert.That(potion.TrySetLockState(ItemLockState.Listed), Is.True);
            Assert.That(potion.TrySetLockState(ItemLockState.Reserved), Is.False,
                "a shop must not take over an item a trade is holding");
            Assert.That(potion.LockState, Is.EqualTo(ItemLockState.Listed));

            Assert.That(potion.TrySetLockState(ItemLockState.Available), Is.True);
            Assert.That(potion.TrySetLockState(ItemLockState.Reserved), Is.True);
        }

        [Test]
        public void Setting_the_same_lock_is_not_a_mutation()
        {
            ItemInstance potion = Stack(Potion, AliceOwner);
            potion.TrySetLockState(ItemLockState.Listed);

            Revision before = potion.Revision;

            Assert.That(potion.TrySetLockState(ItemLockState.Listed), Is.False);
            Assert.That(potion.Revision, Is.EqualTo(before));
        }

        [Test]
        public void There_is_one_lock_field_and_no_boolean_flags()
        {
            System.Reflection.FieldInfo[] fields = typeof(GameInstance).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            foreach (System.Reflection.FieldInfo field in fields)
            {
                Assert.That(field.Name, Is.Not.EqualTo("_isTrading"));
                Assert.That(field.Name, Is.Not.EqualTo("_isInShop"));
                Assert.That(field.Name, Is.Not.EqualTo("_isReserved"));
                Assert.That(field.Name, Is.Not.EqualTo("_isListed"));
            }
        }

        // ---- ownership seam ------------------------------------------------------------

        [Test]
        public void There_is_one_ownership_transfer_implementation()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int seams = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class ItemOwnershipTransfer")) seams++;

                Assert.That(source, Does.Not.Contain("class TradeItemTransfer"), file);
                Assert.That(source, Does.Not.Contain("class ShopItemTransfer"), file);
                Assert.That(source, Does.Not.Contain("class GuildItemTransfer"), file);
            }

            Assert.That(seams, Is.EqualTo(1));
        }

        [Test]
        public void Both_commerce_services_go_through_the_same_seams()
        {
            string trade = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/TradeService.cs");

            string shop = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs");

            Assert.That(trade, Does.Contain("ItemTransferRules.CanTransfer"));
            Assert.That(shop, Does.Contain("ItemTransferRules.CanTransfer"));

            Assert.That(trade, Does.Contain("EconomyService."));
            Assert.That(shop, Does.Contain("EconomyService."));

            Assert.That(trade, Does.Contain("ItemOwnershipTransfer."));
            Assert.That(shop, Does.Contain("ItemOwnershipTransfer."));
        }

        [Test]
        public void Neither_commerce_service_changes_a_balance_directly()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/TradeService.cs",
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs"
            };

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("TryApplyDelta"), file);
                    Assert.That(code, Does.Not.Contain("new CharacterWalletState"), file);
                }
            }
        }

        [Test]
        public void There_is_one_inventory_implementation()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int containers = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class ItemContainerState")) containers++;

                Assert.That(source, Does.Not.Contain("class TradeInventory"), file);
                Assert.That(source, Does.Not.Contain("class ShopInventory"), file);
                Assert.That(source, Does.Not.Contain("class GuildBank"), file);
            }

            Assert.That(containers, Is.EqualTo(1));
        }

        [Test]
        public void There_is_no_special_item_type_for_any_of_this()
        {
            System.Type[] types = typeof(ItemInstance).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("TradeItem"));
                Assert.That(type.Name, Is.Not.EqualTo("ShopItem"));
                Assert.That(type.Name, Is.Not.EqualTo("LootItem"));
                Assert.That(type.Name, Is.Not.EqualTo("NpcItem"));
                Assert.That(type.Name, Is.Not.EqualTo("PartyItem"));
                Assert.That(type.Name, Is.Not.EqualTo("GuildItem"));
            }
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void Gameplay_remains_engine_free()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Gameplay",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("UnityEngine"), file);
                }
            }
        }

        [Test]
        public void The_ui_assembly_holds_no_gameplay_state()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/UI", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("ChibiFantasy.Gameplay"), file);
                    Assert.That(code, Does.Not.Contain("CharacterWalletState"), file);
                    Assert.That(code, Does.Not.Contain("TradeSession"), file);
                    Assert.That(code, Does.Not.Contain("PlayerShop "), file);
                    Assert.That(code, Does.Not.Contain("PartyState"), file);
                    Assert.That(code, Does.Not.Contain("GuildState"), file);
                }
            }
        }

        [Test]
        public void Social_and_commerce_commands_live_only_in_their_controllers()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');

                bool isSocial = normalized.EndsWith("SocialUiController.cs");
                bool isCommerce = normalized.EndsWith("CommerceUiController.cs");

                string source = System.IO.File.ReadAllText(file);

                if (!isSocial)
                {
                    Assert.That(source, Does.Not.Contain("PartyService.TryKick"), normalized);
                    Assert.That(source, Does.Not.Contain("GuildService.TryKick"), normalized);
                }

                if (isCommerce) continue;

                Assert.That(source, Does.Not.Contain("TradeService.TryCommit"), normalized);
                Assert.That(source, Does.Not.Contain("PlayerShopService.TryPurchase"), normalized);
            }
        }

        [Test]
        public void No_identity_is_written_in_any_service()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/PartyService.cs",
                "Assets/_Game/Scripts/Gameplay/GuildService.cs",
                "Assets/_Game/Scripts/Gameplay/EconomyService.cs",
                "Assets/_Game/Scripts/Gameplay/TradeService.cs",
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs",
                "Assets/_Game/Scripts/Gameplay/ItemOwnershipTransfer.cs",
                "Assets/_Game/Scripts/Gameplay/ItemTransferRules.cs"
            };

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    // Identity literals a service could route on. Prose in a ToString is not
                    // routing, so the patterns name the shapes an id actually takes.
                    Assert.That(code, Does.Not.Contain("\"char:"), file);
                    Assert.That(code, Does.Not.Contain("\"account:"), file);
                    Assert.That(code, Does.Not.Contain("\"guild:"), file);
                    Assert.That(code, Does.Not.Contain("\"guildrank."), file);
                    Assert.That(code, Does.Not.Contain("\"party:"), file);
                    Assert.That(code, Does.Not.Contain("\"item."), file);
                    Assert.That(code, Does.Not.Contain("\"equip."), file);
                    Assert.That(code, Does.Not.Contain("\"currency."), file);
                    Assert.That(code, Does.Not.Contain("\"map."), file);
                }
            }
        }

        [Test]
        public void The_party_ceiling_is_not_written_into_the_party_service()
        {
            // The ceiling is read from configuration, never compared against a literal. An
            // enum's ordinal is not a party size, so the check is for a comparison.
            foreach (string code in CodeLines("Assets/_Game/Scripts/Gameplay/PartyService.cs"))
            {
                Assert.That(code, Does.Not.Contain(">= 6"));
                Assert.That(code, Does.Not.Contain("> 6"));
                Assert.That(code, Does.Not.Contain("== 6"));
                Assert.That(code, Does.Not.Contain("MaxPartySize = "));
            }

            Assert.That(SocialConfiguration.DefaultMaxPartySize, Is.EqualTo(6),
                "six is the shipped default, and it lives on the configuration");

            System.Type[] types = typeof(PartyService).Assembly.GetTypes();

            foreach (System.Type type in types)
            {
                Assert.That(type.Name, Is.Not.EqualTo("PartyOfSix"));
                Assert.That(type.Name, Is.Not.EqualTo("SixPlayerParty"));
            }
        }

        [Test]
        public void Party_and_guild_are_identified_by_id_rather_than_by_name()
        {
            Assert.That(typeof(PartyState).GetProperty("Id").PropertyType,
                Is.EqualTo(typeof(PartyId)));

            Assert.That(typeof(GuildState).GetProperty("Id").PropertyType,
                Is.EqualTo(typeof(GuildId)));

            // A party has no name at all, so nothing can key on one.
            Assert.That(typeof(PartyState).GetProperty("Name"), Is.Null);
        }

        [Test]
        public void Guild_storage_is_declared_but_not_implemented()
        {
            // Honest bookkeeping: the permission flag exists so ranks authored now need no
            // rework later, and nothing reads it. Guild funds are not mixed with personal
            // currency because there are no guild funds.
            Assert.That(System.Enum.IsDefined(typeof(GuildPermission),
                GuildPermission.StorageAccess), Is.True);

            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("class GuildWallet"), file);
                Assert.That(source, Does.Not.Contain("class GuildStorageService"), file);
            }
        }

        [Test]
        public void Every_result_type_carries_a_typed_reason_rather_than_a_bare_bool()
        {
            System.Type[] results =
            {
                typeof(PartyResult), typeof(GuildResult), typeof(EconomyResult),
                typeof(TradeResult), typeof(ShopResult)
            };

            foreach (System.Type result in results)
            {
                Assert.That(result.GetProperty("IsAccepted"), Is.Not.Null, result.Name);
                Assert.That(result.GetProperty("Reason"), Is.Not.Null,
                    result.Name + " has no typed rejection");
                Assert.That(result.GetProperty("Reason").PropertyType.IsEnum, Is.True,
                    result.Name);
            }
        }

        [Test]
        public void The_transaction_identity_is_never_derived_from_a_clock()
        {
            foreach (string code in CodeLines("Assets/_Game/Scripts/Core/TransactionId.cs"))
            {
                Assert.That(code, Does.Not.Contain("DateTime"));
                Assert.That(code, Does.Not.Contain("Now"));
                Assert.That(code, Does.Not.Contain("Ticks"));
            }
        }

        [Test]
        public void No_service_reads_an_ambient_clock()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/EconomyService.cs",
                "Assets/_Game/Scripts/Gameplay/TradeService.cs",
                "Assets/_Game/Scripts/Gameplay/PlayerShopService.cs",
                "Assets/_Game/Scripts/Gameplay/PartyService.cs",
                "Assets/_Game/Scripts/Gameplay/GuildService.cs"
            };

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("DateTime.Now"), file);
                    Assert.That(code, Does.Not.Contain("DateTime.UtcNow"), file);
                    Assert.That(code, Does.Not.Contain("Time.time"), file);
                }
            }
        }
    }
}
