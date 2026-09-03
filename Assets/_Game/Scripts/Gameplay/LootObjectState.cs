using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Who may take a piece of loot.</summary>
    /// <remarks>
    /// Closed technical category: each value is a different eligibility check the server
    /// must implement explicitly.
    ///
    /// The contract is authored now even though party distribution is a later phase, so
    /// content and persistence do not have to change when it arrives. What is missing is
    /// the party system, not the policy.
    /// </remarks>
    public enum LootPolicy
    {
        /// <summary>Anyone may take it.</summary>
        FreeForAll = 0,

        /// <summary>Only the eligible character, and only until it expires.</summary>
        Personal = 1,

        /// <summary>
        /// Only members of the eligible party.
        /// </summary>
        /// <remarks>Behaves as <see cref="Personal"/> until a party system exists: the
        /// stricter reading, so nobody can take what they should not. Documented as a
        /// limitation rather than left to look complete.</remarks>
        Party = 2,

        /// <summary>Only the owner, with no expiry into free-for-all.</summary>
        OwnerOnly = 3
    }

    /// <summary>Why a pickup was refused.</summary>
    public enum LootPickupRejection
    {
        None = 0,

        /// <summary>No loot, no container, or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The loot has already been taken.</summary>
        AlreadyTaken = 2,

        /// <summary>The loot timed out.</summary>
        Expired = 3,

        /// <summary>This character may not take it.</summary>
        NotEligible = 4,

        /// <summary>The item no longer resolves to anything.</summary>
        UnknownItem = 5,

        /// <summary>The stack is not a quantity anything can hold.</summary>
        InvalidQuantity = 6,

        /// <summary>There is no room. The loot stays where it is.</summary>
        InventoryFull = 7
    }

    /// <summary>What a pickup attempt did.</summary>
    public readonly struct LootPickupResult
    {
        private LootPickupResult(bool accepted, LootPickupRejection reason, InstanceId lootId,
            DefinitionId item, int quantity, int remainder)
        {
            IsAccepted = accepted;
            Reason = reason;
            LootId = lootId;
            Item = item;
            QuantityTaken = quantity;
            Remainder = remainder;
        }

        public bool IsAccepted { get; }

        public LootPickupRejection Reason { get; }

        public InstanceId LootId { get; }

        public DefinitionId Item { get; }

        public int QuantityTaken { get; }

        /// <summary>
        /// What would not fit.
        /// </summary>
        /// <remarks>Mirrors <see cref="ItemContainerResult.Remainder"/>: a partial pickup is
        /// neither a success nor a failure, and the rest stays in the world rather than
        /// being destroyed.</remarks>
        public int Remainder { get; }

        public bool IsPartial => IsAccepted && Remainder > 0;

        public static LootPickupResult Accepted(InstanceId lootId, DefinitionId item,
            int quantity, int remainder)
        {
            return new LootPickupResult(true, LootPickupRejection.None, lootId, item,
                quantity, remainder);
        }

        public static LootPickupResult Rejected(LootPickupRejection reason,
            InstanceId lootId = default)
        {
            return new LootPickupResult(false, reason, lootId, default, 0, 0);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;
            return "took " + Item + " x" + QuantityTaken
                + (Remainder > 0 ? " (" + Remainder + " left)" : string.Empty);
        }
    }

    /// <summary>
    /// A pile of loot lying in the world.
    /// </summary>
    /// <remarks>
    /// <b>It holds definitions and quantities, not items.</b> Nothing exists yet: the
    /// <see cref="ItemInstance"/> is minted at pickup, by the character who takes it, so
    /// there is never an owned item floating unowned in the world. That also means the
    /// item a player receives is an ordinary instance from the moment it exists -- with a
    /// real <see cref="InstanceId"/>, owner, quantity and revision -- which is what future
    /// trade and player shops will need to operate on.
    ///
    /// <b>Taken once.</b> <see cref="TryClaim"/> is the guard, for the same reason
    /// <c>MonsterRuntimeState.TryClaimDefeat</c> exists: two players clicking the same pile
    /// in the same frame must not both receive it.
    ///
    /// Runtime, server-owned. Caller-supplied time, so nothing here reads a clock.
    /// </remarks>
    public sealed class LootObjectState : IRuntimeState
    {
        private readonly List<LootResult> _contents = new List<LootResult>();
        private readonly bool[] _taken;
        private Revision _revision;
        private float _age;

        public LootObjectState(InstanceId lootId, InstanceId source, CombatPosition position,
            IReadOnlyList<LootResult> contents, LootPolicy policy = LootPolicy.FreeForAll,
            CharacterId eligible = default, float lifetimeSeconds = 0f,
            float personalWindowSeconds = 0f)
        {
            LootId = lootId;
            Source = source;
            Position = position;
            Policy = policy;
            EligibleCharacter = eligible;
            LifetimeSeconds = lifetimeSeconds;
            PersonalWindowSeconds = personalWindowSeconds;

            if (contents != null)
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    if (contents[i].IsValid) _contents.Add(contents[i]);
                }
            }

            _taken = new bool[_contents.Count];
            _revision = Revision.Initial;
        }

        public InstanceId LootId { get; }

        /// <summary>What it fell from.</summary>
        public InstanceId Source { get; }

        public CombatPosition Position { get; }

        public LootPolicy Policy { get; }

        /// <summary>Who it belongs to, for the policies that care.</summary>
        public CharacterId EligibleCharacter { get; }

        /// <summary>Seconds before it disappears. Zero or less means it never does.</summary>
        public float LifetimeSeconds { get; }

        /// <summary>
        /// Seconds a personal claim holds before anyone may take it.
        /// </summary>
        /// <remarks>The usual MMO courtesy window. Zero means the claim never lapses, and
        /// <see cref="LootPolicy.OwnerOnly"/> ignores this entirely.</remarks>
        public float PersonalWindowSeconds { get; }

        /// <summary>What is in it, taken or not.</summary>
        public IReadOnlyList<LootResult> Contents => _contents;

        public int Count => _contents.Count;

        public float Age => _age;

        public Revision Revision => _revision;

        public bool IsExpired => LifetimeSeconds > 0f && _age >= LifetimeSeconds;

        /// <summary>Whether a given entry is still there.</summary>
        public bool IsTaken(int index)
        {
            return index < 0 || index >= _taken.Length || _taken[index];
        }

        /// <summary>Whether anything is left.</summary>
        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _taken.Length; i++)
                {
                    if (!_taken[i]) return false;
                }

                return true;
            }
        }

        /// <summary>Advances the clock. Caller-supplied, like every other timer here.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            _age += deltaSeconds;
        }

        /// <summary>
        /// Claims one entry, exactly once.
        /// </summary>
        /// <remarks>Called only after every other check has passed, so a claimed entry is
        /// one that is definitely being handed over.</remarks>
        public bool TryClaim(int index)
        {
            if (index < 0 || index >= _taken.Length || _taken[index]) return false;

            _taken[index] = true;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Puts an entry back.
        /// </summary>
        /// <remarks>The one place a claim is undone, and it exists because a pickup can
        /// fail after the claim -- there is no rollback in the architecture, so the
        /// container's own answer is what decides whether the claim stands.</remarks>
        public void Release(int index)
        {
            if (index < 0 || index >= _taken.Length || !_taken[index]) return;

            _taken[index] = false;
            _revision = _revision.Next();
        }

        /// <summary>
        /// Replaces one entry, leaving it available.
        /// </summary>
        /// <remarks>How a partial pickup puts back what would not fit. Destroying the
        /// remainder is the one outcome a player never accepts, so the entry is rewritten
        /// at the lower quantity instead.</remarks>
        public void Replace(int index, LootResult entry)
        {
            if (index < 0 || index >= _contents.Count || !entry.IsValid) return;

            _contents[index] = entry;
            _taken[index] = false;
            _revision = _revision.Next();
        }

        /// <summary>
        /// Whether a character may take from this pile right now.
        /// </summary>
        /// <remarks>
        /// The policy decides, not the caller. A client claiming eligibility proves
        /// nothing: this is the check a server runs, and it is deliberately the only place
        /// the question is answered.
        /// </remarks>
        public bool IsEligible(CharacterId character)
        {
            switch (Policy)
            {
                case LootPolicy.FreeForAll:
                    return true;

                case LootPolicy.OwnerOnly:
                    return EligibleCharacter.IsValid && character == EligibleCharacter;

                case LootPolicy.Personal:
                case LootPolicy.Party:
                    // Party has no party system yet, so it reads as personal: the stricter
                    // answer, because the looser one would let anyone take it.
                    if (!EligibleCharacter.IsValid) return true;
                    if (character == EligibleCharacter) return true;

                    // The courtesy window lapsed and it is anyone's.
                    return PersonalWindowSeconds > 0f && _age >= PersonalWindowSeconds;

                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return "loot " + LootId + " x" + _contents.Count + " (" + Policy + ")";
        }
    }
}
