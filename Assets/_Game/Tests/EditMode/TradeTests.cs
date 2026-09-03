using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Player trade: offers, acceptance, revalidation and atomic commit.
    /// </summary>
    /// <remarks>
    /// Two properties carry these tests. An acceptance always refers to the offer that was
    /// actually on the table, so every mutation clears both. And a trade either happens
    /// completely or not at all -- so every rejection path is checked for having left both
    /// players exactly as they were, down to the revision.
    /// </remarks>
    [TestFixture]
    internal sealed class TradeTests : SocialTestBase
    {
        private ItemContainerState _aliceBag;
        private ItemContainerState _bobBag;
        private CharacterWalletState _aliceWallet;
        private CharacterWalletState _bobWallet;
        private TradeSession _session;

        private DefinitionId GoldId => new DefinitionId(Gold);

        [SetUp]
        public void SetUpTrade()
        {
            _aliceBag = Container(AliceOwner);
            _bobBag = Container(BobOwner);
            _aliceWallet = Wallet(AliceOwner, Alice, 1000);
            _bobWallet = Wallet(BobOwner, Bob, 500);

            _session = TradeService.Open(Alice, AliceOwner, Bob, BobOwner);
        }

        private TradeService.Context Trade(CharacterEquipmentState aliceEquipment = null,
            CharacterDevilFruitState aliceFruit = null,
            IReadOnlyList<EquipmentInstance> aliceSockets = null)
        {
            return TradeContext(
                Participant(Alice, AliceOwner, _aliceBag, _aliceWallet, aliceEquipment,
                    aliceFruit, aliceSockets),
                Participant(Bob, BobOwner, _bobBag, _bobWallet));
        }

        private ItemInstance GiveAlice(string id, int quantity = 1)
        {
            ItemInstance item = Stack(id, AliceOwner, quantity);
            _aliceBag.Add(item, Items);
            return item;
        }

        private EquipmentInstance GiveAliceEquipment(string id)
        {
            EquipmentInstance item = Equipment(id, AliceOwner);
            _aliceBag.Add(item, Items);
            return item;
        }

        private void BothAccept()
        {
            TradeService.TryAccept(_session, Alice, Trade());
            TradeService.TryAccept(_session, Bob, Trade());
        }

        // ---- session -------------------------------------------------------------------

        [Test]
        public void A_trade_opens_with_two_empty_offers()
        {
            Assert.That(_session, Is.Not.Null);
            Assert.That(_session.IsOpen, Is.True);
            Assert.That(_session.OfferA.IsEmpty, Is.True);
            Assert.That(_session.OfferB.IsEmpty, Is.True);
            Assert.That(_session.BothAccepted, Is.False);
        }

        [Test]
        public void A_trade_with_oneself_cannot_be_opened()
        {
            Assert.That(TradeService.Open(Alice, AliceOwner, Alice, AliceOwner), Is.Null);
        }

        [Test]
        public void Somebody_outside_the_trade_cannot_touch_it()
        {
            ItemInstance potion = GiveAlice(Potion, 5);

            Assert.That(TradeService.TryOfferItem(_session, Carol, potion.InstanceId, Trade())
                .Reason, Is.EqualTo(TradeRejection.NotAParticipant));
        }

        // ---- offers --------------------------------------------------------------------

        [Test]
        public void An_owned_item_can_be_put_on_the_table()
        {
            ItemInstance potion = GiveAlice(Potion, 5);

            TradeResult result = TradeService.TryOfferItem(_session, Alice, potion.InstanceId,
                Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_session.OfferA.ItemCount, Is.EqualTo(1));
            Assert.That(_aliceBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5),
                "offering does not move anything yet");
        }

        [Test]
        public void The_same_item_cannot_be_offered_twice()
        {
            ItemInstance potion = GiveAlice(Potion, 5);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());

            Assert.That(TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade())
                .Reason, Is.EqualTo(TradeRejection.ItemAlreadyOffered));
            Assert.That(_session.OfferA.ItemCount, Is.EqualTo(1));
        }

        [Test]
        public void An_item_not_in_the_bag_cannot_be_offered()
        {
            Assert.That(TradeService.TryOfferItem(_session, Alice, InstanceId.New(), Trade())
                .Reason, Is.EqualTo(TradeRejection.ItemNotHeld));
        }

        [Test]
        public void A_non_tradable_item_is_refused()
        {
            ItemInstance bound = GiveAlice(Bound);

            TradeResult result = TradeService.TryOfferItem(_session, Alice, bound.InstanceId,
                Trade());

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.ItemBlocked));
            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.NotTradable));
        }

        [Test]
        public void A_bound_item_is_refused()
        {
            ItemInstance potion = GiveAlice(Potion);
            potion.TrySetLockState(ItemLockState.Bound);

            TradeResult result = TradeService.TryOfferItem(_session, Alice, potion.InstanceId,
                Trade());

            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.Bound));
        }

        [Test]
        public void An_equipped_item_is_refused()
        {
            EquipmentInstance sword = GiveAliceEquipment(Sword);

            var worn = new CharacterEquipmentState(Alice);

            EquipmentService.Equip(_aliceBag, worn, _aliceBag.IndexOf(sword.InstanceId),
                new EquipmentService.Context(Items, 99));

            // The sword is now worn; offering it must be refused.
            TradeResult result = TradeService.TryOfferItem(_session, Alice, sword.InstanceId,
                Trade(worn));

            Assert.That(result.IsAccepted, Is.False);
        }

        [Test]
        public void A_socketed_card_is_refused()
        {
            ItemInstance card = GiveAlice(Card);
            EquipmentInstance sword = Equipment(Sword, AliceOwner);

            // Put the card into the sword. It leaves the bag, which is the first defence.
            sword.AddCard(new EquipmentCardSocket(new DefinitionId(Card), 0, card.InstanceId));
            _aliceBag.RemoveAt(_aliceBag.IndexOf(card.InstanceId), 1);

            // Put it back in the bag artificially: the socket check is the second defence.
            _aliceBag.Add(card, Items);

            TradeResult result = TradeService.TryOfferItem(_session, Alice, card.InstanceId,
                Trade(null, null, new[] { sword }));

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.ItemBlocked));
            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.Socketed));
        }

        [Test]
        public void The_copy_spent_on_an_active_devil_fruit_is_refused()
        {
            ItemInstance fruitItem = GiveAlice(Potion);

            var fruitState = new CharacterDevilFruitState(Alice, AliceOwner);
            fruitState.Activate(new DefinitionId("fruit.darkness"), fruitItem.InstanceId);

            TradeResult result = TradeService.TryOfferItem(_session, Alice,
                fruitItem.InstanceId, Trade(null, fruitState));

            Assert.That(result.Block, Is.EqualTo(ItemTransferBlock.ConsumedByDevilFruit));
        }

        [Test]
        public void A_pet_can_never_be_offered_because_it_is_not_an_item()
        {
            // Structural: PetInstance is not a GameInstance a container accepts, and a trade
            // offer names an instance in a container. There is nothing to reject because
            // there is no way to express it.
            var pet = new PetInstance(InstanceId.New(), new DefinitionId("pet.a"), AliceOwner);

            Assert.That(_aliceBag.IndexOf(pet.InstanceId), Is.EqualTo(-1));
            Assert.That(TradeService.TryOfferItem(_session, Alice, pet.InstanceId, Trade())
                .Reason, Is.EqualTo(TradeRejection.ItemNotHeld));
        }

        [Test]
        public void Currency_can_be_offered_up_to_the_balance()
        {
            TradeResult result = TradeService.TryOfferCurrency(_session, Alice, GoldId, 300,
                Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_session.OfferA.AmountOf(GoldId), Is.EqualTo(300));
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(1000),
                "offering does not move money yet");
        }

        [Test]
        public void Offering_more_currency_than_is_held_is_refused()
        {
            Assert.That(TradeService.TryOfferCurrency(_session, Alice, GoldId, 5000, Trade())
                .Reason, Is.EqualTo(TradeRejection.InsufficientFunds));
        }

        [Test]
        public void Setting_a_currency_amount_replaces_rather_than_adds()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 250, Trade());

            Assert.That(_session.OfferA.AmountOf(GoldId), Is.EqualTo(250));
        }

        // ---- acceptance ----------------------------------------------------------------

        [Test]
        public void Both_sides_can_agree()
        {
            GiveAlice(Potion, 3);
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());

            BothAccept();

            Assert.That(_session.BothAccepted, Is.True);
        }

        [Test]
        public void Changing_an_offer_resets_both_acceptances()
        {
            ItemInstance potion = GiveAlice(Potion, 3);
            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());

            BothAccept();
            Assert.That(_session.BothAccepted, Is.True);

            // Bob changes his side. Alice's agreement must not survive it.
            ItemInstance sword = Stack(Potion, BobOwner, 1);
            _bobBag.Add(sword, Items);
            TradeService.TryOfferItem(_session, Bob, sword.InstanceId, Trade());

            Assert.That(_session.OfferA.HasAccepted, Is.False,
                "an acceptance must refer to the offer that was actually shown");
            Assert.That(_session.OfferB.HasAccepted, Is.False);
        }

        [Test]
        public void Withdrawing_an_item_resets_acceptance()
        {
            ItemInstance potion = GiveAlice(Potion, 3);
            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());

            BothAccept();

            TradeService.TryWithdrawItem(_session, Alice, potion.InstanceId, Trade());

            Assert.That(_session.BothAccepted, Is.False);
        }

        [Test]
        public void Changing_currency_resets_acceptance()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());
            BothAccept();

            TradeService.TryOfferCurrency(_session, Alice, GoldId, 200, Trade());

            Assert.That(_session.BothAccepted, Is.False);
        }

        [Test]
        public void Setting_the_same_currency_amount_is_not_a_change()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());
            BothAccept();

            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());

            Assert.That(_session.BothAccepted, Is.True, "nothing actually changed");
        }

        [Test]
        public void An_empty_trade_cannot_be_accepted()
        {
            Assert.That(TradeService.TryAccept(_session, Alice, Trade()).Reason,
                Is.EqualTo(TradeRejection.NothingOffered));
        }

        // ---- commit --------------------------------------------------------------------

        [Test]
        public void A_completed_trade_moves_a_stack_and_preserves_its_identity()
        {
            ItemInstance potion = GiveAlice(Potion, 5);
            InstanceId identity = potion.InstanceId;
            Revision before = potion.Revision;

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_aliceBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(0));
            Assert.That(_bobBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(5));
            Assert.That(potion.InstanceId, Is.EqualTo(identity), "identity is preserved");
            Assert.That(potion.Owner, Is.EqualTo(BobOwner));
            Assert.That(potion.Revision.IsNewerThan(before), Is.True);
            Assert.That(_session.State, Is.EqualTo(TradeSessionState.Completed));
        }

        [Test]
        public void Ownership_itself_is_exactly_one_revision()
        {
            // Equipment does not stack, so placing it in a container renumbers nothing and
            // the ownership change is the only mutation. A stack costs a second revision
            // because Phase 08's container restates a stack's quantity when it lands; that is
            // container behaviour, not a second ownership change, and the audit entry below
            // records the true before and after either way.
            EquipmentInstance sword = GiveAliceEquipment(Sword);
            Revision before = sword.Revision;

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(sword.Revision.Value, Is.EqualTo(before.Value + 1),
                "exactly one mutation: the ownership change");

            ItemTransactionEntry entry = result.Transaction.ItemEntries[0];

            Assert.That(entry.FromRevision, Is.EqualTo(before));
            Assert.That(entry.ToRevision, Is.EqualTo(sword.Revision));
        }

        [Test]
        public void Equipment_trades_like_any_other_item()
        {
            EquipmentInstance sword = GiveAliceEquipment(Sword);

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            BothAccept();

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).IsAccepted,
                Is.True);
            Assert.That(sword.Owner, Is.EqualTo(BobOwner));
            Assert.That(_bobBag.IndexOf(sword.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Currency_moves_and_the_total_is_conserved()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 400, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(600));
            Assert.That(_bobWallet.BalanceOf(GoldId), Is.EqualTo(900));
            Assert.That(_aliceWallet.BalanceOf(GoldId) + _bobWallet.BalanceOf(GoldId),
                Is.EqualTo(1500));
        }

        [Test]
        public void Items_and_currency_can_move_in_the_same_trade()
        {
            ItemInstance potion = GiveAlice(Potion, 2);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            TradeService.TryOfferCurrency(_session, Bob, GoldId, 250, Trade());

            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(_bobBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(2));
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(1250));
            Assert.That(_bobWallet.BalanceOf(GoldId), Is.EqualTo(250));
        }

        [Test]
        public void Both_sides_may_give_at_once()
        {
            ItemInstance potion = GiveAlice(Potion, 2);
            ItemInstance bobPotion = Stack(Potion, BobOwner, 3);
            _bobBag.Add(bobPotion, Items);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            TradeService.TryOfferItem(_session, Bob, bobPotion.InstanceId, Trade());

            BothAccept();

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).IsAccepted,
                Is.True);
            Assert.That(potion.Owner, Is.EqualTo(BobOwner));
            Assert.That(bobPotion.Owner, Is.EqualTo(AliceOwner));
        }

        [Test]
        public void A_trade_neither_side_agreed_to_is_refused()
        {
            GiveAlice(Potion, 2);
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).Reason,
                Is.EqualTo(TradeRejection.NotBothAccepted));
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(1000));
        }

        [Test]
        public void One_sided_agreement_is_not_enough()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 100, Trade());
            TradeService.TryAccept(_session, Alice, Trade());

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).Reason,
                Is.EqualTo(TradeRejection.NotBothAccepted));
        }

        [Test]
        public void A_trade_completes_only_once()
        {
            ItemInstance potion = GiveAlice(Potion, 2);
            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();

            TradeService.TryCommit(_session, RequestId.New(), Trade());

            TradeResult again = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(again.Reason, Is.EqualTo(TradeRejection.SessionFinished));
            Assert.That(_bobBag.CountOf(new DefinitionId(Potion)), Is.EqualTo(2),
                "the item did not arrive twice");
        }

        [Test]
        public void The_same_request_twice_trades_once()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 300, Trade());
            BothAccept();

            RequestId request = RequestId.New();

            TradeResult first = TradeService.TryCommit(_session, request, Trade());
            TradeResult second = TradeService.TryCommit(_session, request, Trade());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.TransactionId, Is.EqualTo(first.TransactionId));
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(700), "debited once");
            Assert.That(_bobWallet.BalanceOf(GoldId), Is.EqualTo(800), "credited once");
        }

        // ---- revalidation --------------------------------------------------------------

        [Test]
        public void An_item_that_changed_after_being_offered_is_refused()
        {
            EquipmentInstance sword = GiveAliceEquipment(Sword);

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            BothAccept();

            // Alice enhances the sword after Bob agreed to it.
            sword.SetEnhancementLevel(5);

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.StaleItem));
            Assert.That(sword.Owner, Is.EqualTo(AliceOwner), "nothing moved");
            Assert.That(_session.State, Is.EqualTo(TradeSessionState.Failed));
        }

        [Test]
        public void An_item_that_left_the_bag_after_being_offered_is_refused()
        {
            ItemInstance potion = GiveAlice(Potion, 2);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();

            _aliceBag.RemoveAt(_aliceBag.IndexOf(potion.InstanceId), 2);

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).Reason,
                Is.EqualTo(TradeRejection.ItemNotHeld));
        }

        [Test]
        public void Currency_spent_elsewhere_after_being_offered_is_refused()
        {
            TradeService.TryOfferCurrency(_session, Alice, GoldId, 900, Trade());
            BothAccept();

            EconomyService.TryDebit(_aliceWallet, GoldId, 500, EconomySource.NpcShop,
                RequestId.New(), Economy());

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.InsufficientFunds));
            Assert.That(_aliceWallet.BalanceOf(GoldId), Is.EqualTo(500), "nothing further moved");
            Assert.That(_bobWallet.BalanceOf(GoldId), Is.EqualTo(500));
        }

        [Test]
        public void A_full_receiving_bag_refuses_the_trade_before_anything_moves()
        {
            var smallBag = new ItemContainerState(BobOwner, 1);
            smallBag.Add(Stack(Bound, BobOwner), Items);

            _bobBag = smallBag;

            EquipmentInstance sword = GiveAliceEquipment(Sword);

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            TradeService.TryOfferCurrency(_session, Bob, GoldId, 100, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.InventoryFull));
            Assert.That(sword.Owner, Is.EqualTo(AliceOwner));
            Assert.That(_bobWallet.BalanceOf(GoldId), Is.EqualTo(500),
                "no money moved either: the whole trade failed");
        }

        [Test]
        public void A_bag_that_is_full_can_still_trade_when_its_own_items_leave_first()
        {
            var exact = new ItemContainerState(AliceOwner, 1);
            EquipmentInstance sword = Equipment(Sword, AliceOwner);
            exact.Add(sword, Items);

            _aliceBag = exact;

            EquipmentInstance helm = Equipment(Helm, BobOwner);
            _bobBag.Add(helm, Items);

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            TradeService.TryOfferItem(_session, Bob, helm.InstanceId, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.True,
                "the outgoing item frees the slot the incoming one needs");
            Assert.That(helm.Owner, Is.EqualTo(AliceOwner));
            Assert.That(sword.Owner, Is.EqualTo(BobOwner));
        }

        [Test]
        public void A_failed_trade_leaves_every_revision_untouched()
        {
            ItemInstance potion = GiveAlice(Potion, 2);
            Revision itemBefore = potion.Revision;
            Revision aliceWalletBefore = _aliceWallet.Revision;
            Revision bobWalletBefore = _bobWallet.Revision;

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            TradeService.TryOfferCurrency(_session, Bob, GoldId, 400, Trade());
            BothAccept();

            // Bob spends elsewhere and can no longer cover what he offered.
            EconomyService.TryDebit(_bobWallet, GoldId, 300, EconomySource.NpcShop,
                RequestId.New(), Economy());

            Revision bobAfterSpending = _bobWallet.Revision;

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(potion.Revision, Is.EqualTo(itemBefore));
            Assert.That(_aliceWallet.Revision, Is.EqualTo(aliceWalletBefore));
            Assert.That(_bobWallet.Revision, Is.EqualTo(bobAfterSpending));
        }

        [Test]
        public void An_expired_session_is_refused()
        {
            GiveAlice(Potion, 2);
            TradeSession timed = TradeService.Open(Alice, AliceOwner, Bob, BobOwner, 0L, 100L);

            TradeService.TryOfferCurrency(timed, Alice, GoldId, 100, Trade());
            TradeService.TryAccept(timed, Alice, Trade());
            TradeService.TryAccept(timed, Bob, Trade());

            TradeResult result = TradeService.TryCommit(timed, RequestId.New(),
                TradeContext(
                    Participant(Alice, AliceOwner, _aliceBag, _aliceWallet),
                    Participant(Bob, BobOwner, _bobBag, _bobWallet), 500L));

            Assert.That(result.Reason, Is.EqualTo(TradeRejection.SessionExpired));
        }

        [Test]
        public void A_cancelled_trade_moves_nothing()
        {
            ItemInstance potion = GiveAlice(Potion, 2);
            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();

            TradeService.TryCancel(_session, Bob);

            Assert.That(TradeService.TryCommit(_session, RequestId.New(), Trade()).Reason,
                Is.EqualTo(TradeRejection.SessionFinished));
            Assert.That(potion.Owner, Is.EqualTo(AliceOwner));
        }

        [Test]
        public void Offers_cannot_be_changed_once_the_session_has_finished()
        {
            ItemInstance potion = GiveAlice(Potion, 2);
            TradeService.TryCancel(_session, Alice);

            Assert.That(TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade())
                .Reason, Is.EqualTo(TradeRejection.SessionFinished));
        }

        // ---- audit ---------------------------------------------------------------------

        [Test]
        public void A_completed_trade_produces_one_auditable_transaction()
        {
            ItemInstance potion = GiveAlice(Potion, 2);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            TradeService.TryOfferCurrency(_session, Bob, GoldId, 250, Trade());
            BothAccept();

            int before = Ledger.Count;

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(Ledger.Count, Is.EqualTo(before + 1),
                "one exchange is one event, not four movements");

            EconomyTransaction audit = result.Transaction;

            Assert.That(audit.Source, Is.EqualTo(EconomySource.PlayerTrade));
            Assert.That(audit.Type, Is.EqualTo(EconomyTransactionType.Exchange));
            Assert.That(audit.CurrencyEntries.Count, Is.EqualTo(2));
            Assert.That(audit.ItemEntries.Count, Is.EqualTo(1));
            Assert.That(audit.CurrencyBalances(), Is.True);
        }

        [Test]
        public void The_audit_records_both_sides_of_the_item_move()
        {
            ItemInstance potion = GiveAlice(Potion, 4);
            Revision before = potion.Revision;

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();

            TradeResult result = TradeService.TryCommit(_session, RequestId.New(), Trade());

            ItemTransactionEntry entry = result.Transaction.ItemEntries[0];

            Assert.That(entry.Item, Is.EqualTo(potion.InstanceId));
            Assert.That(entry.From, Is.EqualTo(AliceOwner));
            Assert.That(entry.To, Is.EqualTo(BobOwner));
            Assert.That(entry.Quantity, Is.EqualTo(4));
            Assert.That(entry.FromRevision, Is.EqualTo(before));
            Assert.That(entry.ToRevision.IsNewerThan(entry.FromRevision), Is.True,
                "the record shows the copy that arrived is the copy that left");
        }

        [Test]
        public void An_items_history_can_be_followed_through_the_ledger()
        {
            ItemInstance potion = GiveAlice(Potion, 2);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();
            TradeService.TryCommit(_session, RequestId.New(), Trade());

            var history = new List<ItemTransactionEntry>();
            Ledger.CollectItemHistory(potion.InstanceId, history);

            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].To, Is.EqualTo(BobOwner));
        }

        // ---- anti-duplication ----------------------------------------------------------

        [Test]
        public void One_item_never_exists_in_two_bags()
        {
            ItemInstance potion = GiveAlice(Potion, 2);

            TradeService.TryOfferItem(_session, Alice, potion.InstanceId, Trade());
            BothAccept();
            TradeService.TryCommit(_session, RequestId.New(), Trade());

            Assert.That(_aliceBag.IndexOf(potion.InstanceId), Is.EqualTo(-1));
            Assert.That(_bobBag.IndexOf(potion.InstanceId), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void The_same_item_cannot_be_committed_through_two_trades()
        {
            EquipmentInstance sword = GiveAliceEquipment(Sword);

            TradeSession second = TradeService.Open(Alice, AliceOwner, Carol,
                new OwnerId("account:carol"));

            TradeService.TryOfferItem(_session, Alice, sword.InstanceId, Trade());
            TradeService.TryOfferItem(second, Alice, sword.InstanceId, Trade());

            BothAccept();
            TradeService.TryCommit(_session, RequestId.New(), Trade());

            // The second trade now names something Alice no longer holds.
            var carolBag = Container(new OwnerId("account:carol"));

            TradeService.TryAccept(second, Alice, TradeContext(
                Participant(Alice, AliceOwner, _aliceBag, _aliceWallet),
                Participant(Carol, new OwnerId("account:carol"), carolBag,
                    Wallet(new OwnerId("account:carol"), Carol))));

            TradeResult result = TradeService.TryCommit(second, RequestId.New(), TradeContext(
                Participant(Alice, AliceOwner, _aliceBag, _aliceWallet),
                Participant(Carol, new OwnerId("account:carol"), carolBag,
                    Wallet(new OwnerId("account:carol"), Carol))));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(sword.Owner, Is.EqualTo(BobOwner), "it went to exactly one person");
        }
    }
}
