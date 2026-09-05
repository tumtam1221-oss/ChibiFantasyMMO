using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>What a pickup attempt did, and whether it reached the database.</summary>
    /// <remarks>
    /// <see cref="Pickup"/> is Phase 10's answer -- eligibility, capacity, stacking,
    /// remainder -- and <see cref="IsPersisted"/> is a separate fact about the write. They
    /// are kept apart for the same reason experience keeps them apart: an item is in the
    /// player's bag the moment the container accepted it, and a database that has not caught
    /// up yet is a retry, not a refusal.
    /// </remarks>
    public readonly struct LootPickupOutcome
    {
        public LootPickupOutcome(LootPickupResult pickup, bool persisted,
            CharacterPersistenceFailure persistenceFailure = CharacterPersistenceFailure.None)
        {
            Pickup = pickup;
            IsPersisted = persisted;
            PersistenceFailure = persistenceFailure;
        }

        public LootPickupResult Pickup { get; }

        public bool IsPersisted { get; }

        public CharacterPersistenceFailure PersistenceFailure { get; }

        public bool IsAccepted => Pickup.IsAccepted;

        public LootPickupRejection Reason => Pickup.Reason;

        public static LootPickupOutcome Rejected(LootPickupRejection reason,
            InstanceId lootId = default)
        {
            return new LootPickupOutcome(LootPickupResult.Rejected(reason, lootId), false);
        }

        public override string ToString()
        {
            return Pickup + (IsAccepted && !IsPersisted
                ? " (not yet saved: " + PersistenceFailure + ")"
                : string.Empty);
        }
    }

    /// <summary>
    /// The loot lying in the world, and the only way anything gets out of it.
    /// </summary>
    /// <remarks>
    /// <b>It composes Phase 10 and Phase 08; it reimplements neither.</b> The pile is
    /// <see cref="LootObjectState"/>, eligibility is that pile's own decision, the transfer
    /// is <see cref="LootPickupService"/>, and the bag is <see cref="ItemContainerState"/>
    /// with its existing stacking and capacity rules. What is added here is the part that
    /// only a server can do: knowing which piles exist, which map each is on, resolving a
    /// character from an id it trusts, and writing the result down.
    ///
    /// <b>A client names a pile and an entry, and nothing else.</b> Not an item, not a
    /// quantity, not an owner, not a chance. Every one of those is read from state the
    /// server already holds, so a forged request can at most ask for something it is not
    /// allowed to have and be told no.
    ///
    /// <b>Nothing is destroyed before it is delivered.</b> The claim, the transfer and the
    /// put-back on failure are Phase 10's, deliberately unchanged: a full bag leaves the
    /// pile exactly as it was, and a partial pickup returns the remainder to the world.
    ///
    /// <b>Piles are runtime, and that is a limitation, not a design.</b> They live for a
    /// session; a server restart loses whatever nobody picked up. The items themselves are
    /// persisted the moment they enter a bag, which is the point at which losing one would
    /// actually matter.
    /// </remarks>
    public sealed class MonsterLootRegistry
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly IDefinitionRegistry<ItemDefinition> _items;

        private readonly Dictionary<string, LootObjectState> _piles =
            new Dictionary<string, LootObjectState>();

        /// <summary>Which map each pile fell on, so a pickup from elsewhere is refused.</summary>
        private readonly Dictionary<string, DefinitionId> _maps =
            new Dictionary<string, DefinitionId>();

        /// <summary>Reused when sweeping, so a steady-state server allocates nothing.</summary>
        private readonly List<string> _sweeping = new List<string>();

        public MonsterLootRegistry(WorldCharacterRegistry characters,
            IDefinitionRegistry<ItemDefinition> items)
        {
            _characters = characters;
            _items = items;
        }

        public int Count => _piles.Count;

        public bool TryGet(InstanceId loot, out LootObjectState pile)
        {
            pile = null;

            return loot.IsValid && !string.IsNullOrEmpty(loot.Value)
                && _piles.TryGetValue(loot.Value, out pile);
        }

        /// <summary>Which map a pile is on, for callers that scope by world.</summary>
        public bool TryGetMap(InstanceId loot, out DefinitionId map)
        {
            map = default;

            return loot.IsValid && !string.IsNullOrEmpty(loot.Value)
                && _maps.TryGetValue(loot.Value, out map);
        }

        /// <summary>Every pile currently in the world.</summary>
        public IReadOnlyList<LootObjectState> All()
        {
            var all = new List<LootObjectState>(_piles.Count);

            foreach (KeyValuePair<string, LootObjectState> pair in _piles) all.Add(pair.Value);

            return all;
        }

        /// <summary>
        /// Puts a pile into the world.
        /// </summary>
        /// <remarks>Called by whoever resolved the defeat, never by a client. Refuses a
        /// duplicate id rather than replacing: two piles with one identity would let a
        /// pickup take from whichever happened to be found.</remarks>
        public bool Add(LootObjectState pile, DefinitionId map)
        {
            if (pile == null || !pile.LootId.IsValid) return false;

            if (_piles.ContainsKey(pile.LootId.Value)) return false;

            _piles[pile.LootId.Value] = pile;
            _maps[pile.LootId.Value] = map;

            return true;
        }

        /// <summary>
        /// Ages every pile and clears away what nobody can take any more.
        /// </summary>
        /// <remarks>
        /// Expiry is <see cref="LootObjectState"/>'s own rule, read rather than
        /// reimplemented. An emptied pile goes too: a pile with nothing in it is a pickup
        /// request that can only ever be refused.
        /// </remarks>
        public int Tick(float deltaSeconds)
        {
            _sweeping.Clear();

            foreach (KeyValuePair<string, LootObjectState> pair in _piles)
            {
                pair.Value.Tick(deltaSeconds);

                if (pair.Value.IsExpired || pair.Value.IsEmpty) _sweeping.Add(pair.Key);
            }

            for (int i = 0; i < _sweeping.Count; i++)
            {
                _piles.Remove(_sweeping[i]);
                _maps.Remove(_sweeping[i]);
            }

            return _sweeping.Count;
        }

        /// <summary>
        /// Takes one entry from a pile, on behalf of a character the server resolved.
        /// </summary>
        /// <remarks>
        /// The order is the safety argument. Everything that can refuse is asked before the
        /// pile is claimed, so a rejected request leaves the loot exactly where it was and
        /// the bag untouched. The claim, the transfer and the put-back are Phase 10's, so
        /// this cannot get the atomicity wrong by getting it different.
        ///
        /// The save comes last and its failure does not undo the pickup: the item is in the
        /// bag, the character is dirty, and the existing lifecycle writes it. Reporting a
        /// success the database never saw would be the lie; taking the item back out of a
        /// player's bag because a web server hiccuped would be worse.
        /// </remarks>
        /// <param name="loot">Which pile. An id the server minted.</param>
        /// <param name="index">Which entry in it.</param>
        /// <param name="character">Who is taking it, as the server resolved them.</param>
        public LootPickupOutcome Pickup(InstanceId loot, int index, CharacterId character)
        {
            if (_characters == null || _items == null)
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.MissingContext);
            }

            if (!TryGet(loot, out LootObjectState pile))
            {
                // No such pile: it was taken, it expired, or it never existed. All three are
                // the same answer to whoever is asking.
                return LootPickupOutcome.Rejected(LootPickupRejection.AlreadyTaken, loot);
            }

            if (!_characters.TryGetByCharacter(character, out LivingCharacter taker))
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.NotEligible, loot);
            }

            if (taker.Inventory == null)
            {
                // A world with no item registry cannot honestly hold items.
                return LootPickupOutcome.Rejected(LootPickupRejection.MissingContext, loot);
            }

            if (!IsOnTheSameMap(loot, taker))
            {
                // Credit belongs to the world the monster died in. Looting a pile on a map
                // you are not standing on is not a thing a player can do legitimately.
                return LootPickupOutcome.Rejected(LootPickupRejection.NotEligible, loot);
            }

            var context = new LootPickupService.Context(_items, taker.Owner, character);

            LootPickupResult result = LootPickupService.TryPickUp(pile, index,
                taker.Inventory, context);

            if (!result.IsAccepted) return new LootPickupOutcome(result, false);

            // It is in the bag. Everything from here is about writing it down.
            taker.MarkDirty();

            CharacterPersistenceResult saved = _characters.Save(taker);

            // And telling whoever is holding this defeat's decision that the entry is gone.
            // Without it a restart would republish an item that is already being carried.
            _takenObserver?.NoteLootTaken(loot, index, character);

            if (pile.IsEmpty) Remove(loot);

            return new LootPickupOutcome(result, saved.IsOk, saved.Failure);
        }

        /// <summary>Told when an entry leaves a pile for somebody's bag.</summary>
        /// <remarks>An interface rather than a direct reference so this registry stays
        /// ignorant of rewards and persistence: it knows a thing was taken, and something
        /// else decides what that means.</remarks>
        public interface ILootTakenObserver
        {
            void NoteLootTaken(InstanceId loot, int index, CharacterId taker);
        }

        private ILootTakenObserver _takenObserver;

        /// <summary>Starts telling an observer what leaves these piles.</summary>
        public void Observe(ILootTakenObserver observer)
        {
            _takenObserver = observer;
        }

        /// <summary>Takes a pile out of the world.</summary>
        public bool Remove(InstanceId loot)
        {
            if (!loot.IsValid || string.IsNullOrEmpty(loot.Value)) return false;

            _maps.Remove(loot.Value);

            return _piles.Remove(loot.Value);
        }

        /// <summary>Empties the world of loot, for a shutdown or an area reset.</summary>
        public int Clear()
        {
            int cleared = _piles.Count;

            _piles.Clear();
            _maps.Clear();

            return cleared;
        }

        /// <summary>
        /// Whether the taker is standing where the loot fell.
        /// </summary>
        /// <remarks>A pile with no recorded map is not checked, matching every other map
        /// rule in this project: an unset map means unrestricted rather than forbidden, so
        /// content and tests written before maps existed keep working.</remarks>
        private bool IsOnTheSameMap(InstanceId loot, LivingCharacter taker)
        {
            if (!TryGetMap(loot, out DefinitionId map) || !map.IsValid) return true;

            return taker.Location != null && taker.Location.CurrentMap == map;
        }
    }
}
