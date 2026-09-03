using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One character's currency balances.
    /// </summary>
    /// <remarks>
    /// <b>Integers, always.</b> Currency is a count. A float balance would make
    /// <c>a + b - b != a</c> for perfectly ordinary values, and in an economy that is not a
    /// rounding curiosity -- it is a duplication exploit. Nothing in this file is a
    /// floating-point number.
    ///
    /// <b>Balances are keyed by <see cref="DefinitionId"/>.</b> There is no gold field. A
    /// second currency is an asset, and this holds as many as it is given.
    ///
    /// <b>It stores; it does not decide.</b> <see cref="EconomyService"/> is the only thing
    /// that should call <see cref="TryApplyDelta"/>, and it is the only place a source, a
    /// ceiling policy or an audit record is decided. Keeping the arithmetic here and the
    /// rules there is what lets a server apply a decision it made elsewhere -- and an
    /// architecture test asserts nothing else calls it.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>character_currency</c>
    /// table is an owner, a currency id and an amount, plus this state's revision.
    /// </remarks>
    public sealed class CharacterWalletState : IPersistentState
    {
        private readonly Dictionary<DefinitionId, int> _balances =
            new Dictionary<DefinitionId, int>();

        private Revision _revision;

        public CharacterWalletState(OwnerId owner, CharacterId characterId = default)
        {
            Owner = owner;
            CharacterId = characterId;
            _revision = Revision.Initial;
        }

        /// <summary>Who the balances belong to.</summary>
        public OwnerId Owner { get; }

        public CharacterId CharacterId { get; }

        public Revision Revision => _revision;

        /// <summary>How many currencies this wallet holds a non-zero balance in.</summary>
        public int CurrencyCount => _balances.Count;

        /// <summary>
        /// The balance in one currency.
        /// </summary>
        /// <remarks>Zero for a currency never held, which is the same answer as a balance
        /// that reached zero. There is no distinction to draw: a wallet holding no gold and
        /// a wallet that has never seen gold are the same wallet.</remarks>
        public int BalanceOf(DefinitionId currency)
        {
            int balance;
            return _balances.TryGetValue(currency, out balance) ? balance : 0;
        }

        /// <summary>Every currency currently held, for a panel or a save.</summary>
        public IEnumerable<KeyValuePair<DefinitionId, int>> Balances => _balances;

        /// <summary>
        /// Adds a signed amount to a balance.
        /// </summary>
        /// <remarks>
        /// <b>Arithmetic only.</b> Whether a debit was allowed, what ceiling applies and
        /// what the audit says are <see cref="EconomyService"/>'s decisions; this refuses
        /// only what it can see: an invalid currency, a result below zero, and a result past
        /// the supplied ceiling.
        ///
        /// <b>Overflow is detected, never wrapped.</b> The sum is computed as a
        /// <c>long</c> and compared before it is narrowed, so a credit that would push a
        /// balance past <see cref="int.MaxValue"/> is refused rather than turning a fortune
        /// into a negative number.
        ///
        /// A delta of zero changes nothing and does not advance the revision, so a no-op
        /// cannot look like a mutation to anything watching.
        /// </remarks>
        public bool TryApplyDelta(DefinitionId currency, int delta, int maximum,
            out int balanceBefore, out int balanceAfter)
        {
            balanceBefore = BalanceOf(currency);
            balanceAfter = balanceBefore;

            if (!currency.IsValid) return false;
            if (delta == 0) return true;

            long result = (long)balanceBefore + delta;

            if (result < 0L) return false;

            long ceiling = maximum > 0 ? maximum : int.MaxValue;
            if (result > ceiling) return false;

            balanceAfter = (int)result;
            _balances[currency] = balanceAfter;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Whether a debit of this size could be taken.
        /// </summary>
        /// <remarks>What a plan asks before anything is written, so a multi-party
        /// transaction can establish that every leg will succeed before any leg runs.</remarks>
        public bool CanAfford(DefinitionId currency, int amount)
        {
            if (!currency.IsValid || amount < 0) return false;
            return BalanceOf(currency) >= amount;
        }

        /// <summary>
        /// Whether a credit of this size would fit.
        /// </summary>
        /// <remarks>The mirror of <see cref="CanAfford"/>, and just as necessary: a
        /// transaction that debits one side and cannot credit the other has to be refused
        /// before either happens.</remarks>
        public bool CanReceive(DefinitionId currency, int amount, int maximum)
        {
            if (!currency.IsValid || amount < 0) return false;

            long ceiling = maximum > 0 ? maximum : int.MaxValue;
            return (long)BalanceOf(currency) + amount <= ceiling;
        }
    }
}
