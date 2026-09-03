using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Currency: balances, movement, overflow, audit and idempotency.
    /// </summary>
    /// <remarks>
    /// The property under test throughout is that money is neither created nor destroyed
    /// except at the two boundaries that exist for it -- credit and debit. Every transfer
    /// sums to zero, every balance matches the ledger that describes it, and every retry
    /// leaves both unchanged.
    /// </remarks>
    [TestFixture]
    internal sealed class EconomyTests : SocialTestBase
    {
        private DefinitionId GoldId => new DefinitionId(Gold);

        // ---- balances ------------------------------------------------------------------

        [Test]
        public void A_new_wallet_holds_nothing()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(0));
            Assert.That(wallet.CurrencyCount, Is.EqualTo(0));
            Assert.That(wallet.Revision, Is.EqualTo(Revision.Initial));
        }

        [Test]
        public void Crediting_raises_the_balance_and_advances_the_revision_once()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);
            Revision before = wallet.Revision;

            EconomyResult result = EconomyService.TryCredit(wallet, GoldId, 500,
                EconomySource.QuestReward, RequestId.New(), Economy());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(500));
            Assert.That(wallet.Revision.Value, Is.EqualTo(before.Value + 1));
        }

        [Test]
        public void Debiting_lowers_the_balance()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 500);

            EconomyResult result = EconomyService.TryDebit(wallet, GoldId, 200,
                EconomySource.NpcShop, RequestId.New(), Economy());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(300));
        }

        [Test]
        public void A_debit_beyond_the_balance_is_refused_and_changes_nothing()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 100);
            Revision before = wallet.Revision;

            EconomyResult result = EconomyService.TryDebit(wallet, GoldId, 200,
                EconomySource.NpcShop, RequestId.New(), Economy());

            Assert.That(result.Reason, Is.EqualTo(EconomyRejection.InsufficientFunds));
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(100));
            Assert.That(wallet.Revision, Is.EqualTo(before));
        }

        [Test]
        public void A_balance_can_never_go_negative()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            int before;
            int after;

            Assert.That(wallet.TryApplyDelta(GoldId, -1, int.MaxValue, out before, out after),
                Is.False, "arithmetic itself refuses, not only the service");
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(0));
        }

        [Test]
        public void A_negative_or_zero_amount_is_refused()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            Assert.That(EconomyService.TryCredit(wallet, GoldId, -5, EconomySource.SystemReward,
                RequestId.New(), Economy()).Reason, Is.EqualTo(EconomyRejection.InvalidAmount));

            Assert.That(EconomyService.TryCredit(wallet, GoldId, 0, EconomySource.SystemReward,
                RequestId.New(), Economy()).Reason, Is.EqualTo(EconomyRejection.InvalidAmount));
        }

        [Test]
        public void An_authored_ceiling_is_enforced()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);
            var token = new DefinitionId(Token);   // ceiling of 1000

            Assert.That(EconomyService.TryCredit(wallet, token, 1000, EconomySource.SystemReward,
                RequestId.New(), Economy()).IsAccepted, Is.True);

            EconomyResult over = EconomyService.TryCredit(wallet, token, 1,
                EconomySource.SystemReward, RequestId.New(), Economy());

            Assert.That(over.Reason, Is.EqualTo(EconomyRejection.BalanceOverflow));
            Assert.That(wallet.BalanceOf(token), Is.EqualTo(1000));
        }

        [Test]
        public void Integer_overflow_is_detected_rather_than_wrapped()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            EconomyService.TryCredit(wallet, GoldId, int.MaxValue, EconomySource.AdminAdjustment,
                RequestId.New(), Economy());

            EconomyResult over = EconomyService.TryCredit(wallet, GoldId, 1,
                EconomySource.AdminAdjustment, RequestId.New(), Economy());

            Assert.That(over.Reason, Is.EqualTo(EconomyRejection.BalanceOverflow));
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(int.MaxValue),
                "a fortune must not become a negative number");
        }

        [Test]
        public void An_unknown_or_disabled_currency_is_refused()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            Assert.That(EconomyService.TryCredit(wallet, new DefinitionId("currency.gone"), 5,
                EconomySource.Other, RequestId.New(), Economy()).Reason,
                Is.EqualTo(EconomyRejection.UnknownCurrency));

            Assert.That(EconomyService.TryCredit(wallet, new DefinitionId(OffCurrency), 5,
                EconomySource.Other, RequestId.New(), Economy()).Reason,
                Is.EqualTo(EconomyRejection.CurrencyDisabled));
        }

        [Test]
        public void A_wallet_holds_several_currencies_independently()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            EconomyService.TryCredit(wallet, GoldId, 100, EconomySource.SystemReward,
                RequestId.New(), Economy());
            EconomyService.TryCredit(wallet, new DefinitionId(Token), 5,
                EconomySource.SystemReward, RequestId.New(), Economy());

            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(100));
            Assert.That(wallet.BalanceOf(new DefinitionId(Token)), Is.EqualTo(5));
            Assert.That(wallet.CurrencyCount, Is.EqualTo(2));
        }

        // ---- transfer ------------------------------------------------------------------

        [Test]
        public void A_transfer_conserves_the_total_exactly()
        {
            CharacterWalletState from = Wallet(AliceOwner, Alice, 500);
            CharacterWalletState to = Wallet(BobOwner, Bob, 100);

            EconomyResult result = EconomyService.TryTransfer(from, to, GoldId, 300,
                EconomySource.PlayerTrade, RequestId.New(), Economy());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(from.BalanceOf(GoldId), Is.EqualTo(200));
            Assert.That(to.BalanceOf(GoldId), Is.EqualTo(400));
            Assert.That(from.BalanceOf(GoldId) + to.BalanceOf(GoldId), Is.EqualTo(600));
        }

        [Test]
        public void A_transfer_transaction_sums_to_zero()
        {
            CharacterWalletState from = Wallet(AliceOwner, Alice, 500);
            CharacterWalletState to = Wallet(BobOwner, Bob);

            EconomyResult result = EconomyService.TryTransfer(from, to, GoldId, 300,
                EconomySource.PlayerTrade, RequestId.New(), Economy());

            Assert.That(result.Transaction.CurrencyBalances(), Is.True,
                "moving money is not creating it");
        }

        [Test]
        public void A_transfer_the_payer_cannot_cover_moves_nothing()
        {
            CharacterWalletState from = Wallet(AliceOwner, Alice, 100);
            CharacterWalletState to = Wallet(BobOwner, Bob, 50);

            EconomyResult result = EconomyService.TryTransfer(from, to, GoldId, 300,
                EconomySource.PlayerTrade, RequestId.New(), Economy());

            Assert.That(result.Reason, Is.EqualTo(EconomyRejection.InsufficientFunds));
            Assert.That(from.BalanceOf(GoldId), Is.EqualTo(100));
            Assert.That(to.BalanceOf(GoldId), Is.EqualTo(50));
        }

        [Test]
        public void A_transfer_the_receiver_cannot_hold_moves_nothing()
        {
            var token = new DefinitionId(Token);   // ceiling of 1000

            var from = new CharacterWalletState(AliceOwner, Alice);
            var to = new CharacterWalletState(BobOwner, Bob);

            EconomyService.TryCredit(from, token, 500, EconomySource.SystemReward,
                RequestId.New(), Economy());
            EconomyService.TryCredit(to, token, 900, EconomySource.SystemReward,
                RequestId.New(), Economy());

            EconomyResult result = EconomyService.TryTransfer(from, to, token, 500,
                EconomySource.PlayerTrade, RequestId.New(), Economy());

            Assert.That(result.Reason, Is.EqualTo(EconomyRejection.BalanceOverflow));
            Assert.That(from.BalanceOf(token), Is.EqualTo(500),
                "the payer's money must not vanish because the receiver was full");
            Assert.That(to.BalanceOf(token), Is.EqualTo(900));
        }

        [Test]
        public void A_transfer_to_oneself_is_refused()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 500);

            Assert.That(EconomyService.TryTransfer(wallet, wallet, GoldId, 100,
                EconomySource.PlayerTrade, RequestId.New(), Economy()).Reason,
                Is.EqualTo(EconomyRejection.SameWallet));
        }

        // ---- audit ---------------------------------------------------------------------

        [Test]
        public void Every_movement_produces_an_audit_record()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 100);

            Assert.That(Ledger.Count, Is.EqualTo(1), "the fixture's own credit is recorded");

            EconomyService.TryDebit(wallet, GoldId, 50, EconomySource.NpcShop, RequestId.New(),
                Economy());

            Assert.That(Ledger.Count, Is.EqualTo(2));
        }

        [Test]
        public void An_audit_entry_records_the_balance_on_both_sides_of_the_change()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 100);

            EconomyResult result = EconomyService.TryDebit(wallet, GoldId, 40,
                EconomySource.NpcShop, RequestId.New(), Economy());

            EconomyTransactionEntry entry = result.Transaction.CurrencyEntries[0];

            Assert.That(entry.Delta, Is.EqualTo(-40));
            Assert.That(entry.BalanceBefore, Is.EqualTo(100));
            Assert.That(entry.BalanceAfter, Is.EqualTo(60));
            Assert.That(entry.BalanceBefore + entry.Delta, Is.EqualTo(entry.BalanceAfter),
                "a ledger line has to be checkable on its own");
        }

        [Test]
        public void The_ledger_and_the_balance_agree()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 500);

            EconomyService.TryDebit(wallet, GoldId, 120, EconomySource.NpcShop, RequestId.New(),
                Economy());
            EconomyService.TryCredit(wallet, GoldId, 30, EconomySource.MonsterLoot,
                RequestId.New(), Economy());

            Assert.That(Ledger.NetFor(AliceOwner, GoldId), Is.EqualTo(wallet.BalanceOf(GoldId)),
                "a ledger that has drifted from the balances it describes is worse than none");
        }

        [Test]
        public void The_source_is_recorded_and_never_branched_on()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice);

            EconomyResult admin = EconomyService.TryCredit(wallet, GoldId, 10,
                EconomySource.AdminAdjustment, RequestId.New(), Economy());

            Assert.That(admin.Transaction.Source, Is.EqualTo(EconomySource.AdminAdjustment));

            // Structural: nothing in the service reads the source to decide anything.
            foreach (string code in CodeLines("Assets/_Game/Scripts/Gameplay/EconomyService.cs"))
            {
                Assert.That(code, Does.Not.Contain("EconomySource."),
                    "the source is an audit field, not a behaviour switch");
            }
        }

        [Test]
        public void Every_transaction_has_a_distinct_identity()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 1000);
            var seen = new HashSet<TransactionId>();

            for (int i = 0; i < 20; i++)
            {
                EconomyResult result = EconomyService.TryDebit(wallet, GoldId, 1,
                    EconomySource.NpcShop, RequestId.New(), Economy());

                Assert.That(seen.Add(result.TransactionId), Is.True, "identities must be unique");
            }
        }

        [Test]
        public void A_transaction_identity_is_not_a_timestamp()
        {
            CharacterWalletState wallet = Wallet(AliceOwner, Alice, 100);

            // Two movements at the same caller-supplied instant.
            EconomyResult first = EconomyService.TryDebit(wallet, GoldId, 1,
                EconomySource.NpcShop, RequestId.New(), Economy(500L));

            EconomyResult second = EconomyService.TryDebit(wallet, GoldId, 1,
                EconomySource.NpcShop, RequestId.New(), Economy(500L));

            Assert.That(first.TransactionId, Is.Not.EqualTo(second.TransactionId));
            Assert.That(first.Transaction.TimestampTicks,
                Is.EqualTo(second.Transaction.TimestampTicks),
                "the same instant, and still distinct identities");
        }

        [Test]
        public void The_ledger_refuses_to_record_the_same_identity_twice()
        {
            var transaction = new EconomyTransaction(TransactionId.New(), RequestId.New(),
                EconomyTransactionType.Credit, EconomySource.Other, 0L);

            Assert.That(Ledger.Record(transaction), Is.True);
            Assert.That(Ledger.Record(transaction), Is.False);
            Assert.That(Ledger.Count, Is.EqualTo(1));
        }

        // ---- idempotency ---------------------------------------------------------------

        [Test]
        public void The_same_credit_request_twice_credits_once()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);
            RequestId request = RequestId.New();

            EconomyResult first = EconomyService.TryCredit(wallet, GoldId, 500,
                EconomySource.QuestReward, request, Economy());

            EconomyResult second = EconomyService.TryCredit(wallet, GoldId, 500,
                EconomySource.QuestReward, request, Economy());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True, "a retry succeeds; it does not fail");
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.TransactionId, Is.EqualTo(first.TransactionId));
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(500), "credited once");
            Assert.That(Ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void The_same_transfer_request_twice_transfers_once()
        {
            CharacterWalletState from = Wallet(AliceOwner, Alice, 500);
            CharacterWalletState to = Wallet(BobOwner, Bob);
            RequestId request = RequestId.New();

            EconomyService.TryTransfer(from, to, GoldId, 200, EconomySource.PlayerTrade, request,
                Economy());

            EconomyResult replay = EconomyService.TryTransfer(from, to, GoldId, 200,
                EconomySource.PlayerTrade, request, Economy());

            Assert.That(replay.IsReplay, Is.True);
            Assert.That(from.BalanceOf(GoldId), Is.EqualTo(300));
            Assert.That(to.BalanceOf(GoldId), Is.EqualTo(200));
        }

        [Test]
        public void A_refused_request_is_re_evaluated_rather_than_cached()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);
            RequestId request = RequestId.New();

            EconomyResult refused = EconomyService.TryDebit(wallet, GoldId, 100,
                EconomySource.NpcShop, request, Economy());

            Assert.That(refused.Reason, Is.EqualTo(EconomyRejection.InsufficientFunds));

            // The player finds some money and tries the same action again.
            EconomyService.TryCredit(wallet, GoldId, 100, EconomySource.MonsterLoot,
                RequestId.New(), Economy());

            EconomyResult retried = EconomyService.TryDebit(wallet, GoldId, 100,
                EconomySource.NpcShop, request, Economy());

            Assert.That(retried.IsAccepted, Is.True,
                "a rejection wrote nothing, so re-sending must be re-judged");
            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(0));
        }

        [Test]
        public void Different_requests_are_separate_movements()
        {
            var wallet = new CharacterWalletState(AliceOwner, Alice);

            EconomyService.TryCredit(wallet, GoldId, 100, EconomySource.QuestReward,
                RequestId.New(), Economy());
            EconomyService.TryCredit(wallet, GoldId, 100, EconomySource.QuestReward,
                RequestId.New(), Economy());

            Assert.That(wallet.BalanceOf(GoldId), Is.EqualTo(200));
            Assert.That(Ledger.Count, Is.EqualTo(2));
        }

        // ---- architecture --------------------------------------------------------------

        [Test]
        public void Only_the_economy_service_changes_a_balance()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');

                if (normalized.EndsWith("EconomyService.cs")) continue;
                if (normalized.EndsWith("CharacterWalletState.cs")) continue;

                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("TryApplyDelta"),
                        normalized + " changes a balance outside the one boundary");
                }
            }
        }

        [Test]
        public void There_is_one_currency_system()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int wallets = 0;
            int services = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class CharacterWalletState")) wallets++;
                if (source.Contains("class EconomyService")) services++;

                Assert.That(source, Does.Not.Contain("class CurrencyService"), file);
                Assert.That(source, Does.Not.Contain("class GoldService"), file);
                Assert.That(source, Does.Not.Contain("class WalletService"), file);
            }

            Assert.That(wallets, Is.EqualTo(1));
            Assert.That(services, Is.EqualTo(1));
        }

        [Test]
        public void No_currency_amount_is_written_in_the_service()
        {
            foreach (string code in CodeLines("Assets/_Game/Scripts/Gameplay/EconomyService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"currency."));
                Assert.That(code, Does.Not.Contain("Gold"));
            }
        }

        [Test]
        public void Currency_is_never_a_floating_point_number()
        {
            string[] files =
            {
                "Assets/_Game/Scripts/Gameplay/CharacterWalletState.cs",
                "Assets/_Game/Scripts/Gameplay/EconomyService.cs",
                "Assets/_Game/Scripts/Gameplay/EconomyTransaction.cs"
            };

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("float "), file);
                    Assert.That(code, Does.Not.Contain("double "), file);
                    Assert.That(code, Does.Not.Contain("decimal "), file);
                }
            }
        }

        [Test]
        public void The_ledger_has_no_way_to_edit_or_delete_a_record()
        {
            System.Reflection.MethodInfo[] methods = typeof(TransactionLedger).GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (System.Reflection.MethodInfo method in methods)
            {
                Assert.That(method.Name, Is.Not.EqualTo("Remove"));
                Assert.That(method.Name, Is.Not.EqualTo("Clear"));
                Assert.That(method.Name, Is.Not.EqualTo("Update"));
                Assert.That(method.Name, Is.Not.EqualTo("Delete"));
            }
        }

        [Test]
        public void An_audit_record_has_no_setters()
        {
            System.Reflection.PropertyInfo[] properties =
                typeof(EconomyTransaction).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.CanWrite, Is.False,
                    property.Name + " can be edited after the fact");
            }
        }
    }
}
