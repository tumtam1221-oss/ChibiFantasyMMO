using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A player's actual copy of a piece of equipment.
    /// </summary>
    /// <remarks>
    /// Enhancement level lives here, not on <see cref="EquipmentDefinition"/>. The
    /// definition describes the base item every copy shares; two players holding the same
    /// sword may have enhanced it to different levels, so that number is per-copy state.
    ///
    /// Note the deliberate asymmetry with the definition layer: EquipmentDefinition extends
    /// ItemDefinition, but EquipmentInstance extends <see cref="GameInstance"/> directly
    /// rather than <see cref="ItemInstance"/>. The definitions share authored fields
    /// (rarity, price, tradable) so inheritance pays off there. The instances do not:
    /// ItemInstance exists to carry a stack quantity, and equipment does not stack, so
    /// inheriting would give every sword a meaningless quantity field.
    ///
    /// No enhancement is performed here. Rolling against the odds authored on an
    /// EnhancementDefinition, consuming materials and applying failure behaviour are
    /// server-authoritative gameplay.
    /// </remarks>
    [Serializable]
    public sealed class EquipmentInstance : GameInstance
    {
        [SerializeField] private int _enhancementLevel;
        [SerializeField] private DefinitionId _rarity;
        [SerializeField] private List<EquipmentEnchant> _enchants = new List<EquipmentEnchant>();
        [SerializeField] private List<EquipmentCardSocket> _cards = new List<EquipmentCardSocket>();

        /// <summary>Exists for deserializers.</summary>
        public EquipmentInstance()
        {
        }

        public EquipmentInstance(InstanceId instanceId, DefinitionId equipmentDefinitionId, OwnerId owner)
            : base(instanceId, equipmentDefinitionId, owner)
        {
            _enhancementLevel = 0;
        }

        public EquipmentInstance(InstanceId instanceId, DefinitionId equipmentDefinitionId, OwnerId owner,
            int enhancementLevel)
            : base(instanceId, equipmentDefinitionId, owner)
        {
            ValidateEnhancementLevel(enhancementLevel);
            _enhancementLevel = enhancementLevel;
        }

        /// <summary>Current enhancement level. Zero means unenhanced.</summary>
        public int EnhancementLevel => _enhancementLevel;

        /// <summary>
        /// Sets the enhancement level and advances the revision.
        /// </summary>
        /// <remarks>
        /// Enforces only the floor of zero. The ceiling is authored on
        /// <see cref="EquipmentDefinition.MaxEnhancementLevel"/> and enforced by the
        /// server, which is the only party that may decide an enhancement succeeded.
        /// </remarks>
        public void SetEnhancementLevel(int enhancementLevel)
        {
            ValidateEnhancementLevel(enhancementLevel);
            _enhancementLevel = enhancementLevel;
            AdvanceRevision();
        }

        /// <summary>
        /// This copy's rarity, when it differs from the authored one.
        /// </summary>
        /// <remarks>
        /// An <em>override</em>, not a duplicate. Invalid means "whatever
        /// <see cref="ItemDefinition.Rarity"/> says", which is the normal case and costs
        /// nothing to store. Copying the definition's rarity onto every instance would give
        /// every owned sword a stale copy of a number content owns, and re-tiering an item
        /// would then miss every sword already in the world.
        ///
        /// The override exists because rarity is per-copy progression in most MMOs: two
        /// identical swords can be Normal and Legendary. Resolving the effective value is
        /// <c>EquipmentModifierResolver</c>'s job, not a caller's.
        /// </remarks>
        public DefinitionId Rarity => _rarity;

        /// <summary>
        /// Stones socketed into this copy, in application order.
        /// </summary>
        /// <remarks>Read-only to callers: only a Gameplay service may change the set, so a
        /// panel holding this cannot socket a stone by writing to a list.</remarks>
        public IReadOnlyList<EquipmentEnchant> Enchants
        {
            get
            {
                if (_enchants == null) _enchants = new List<EquipmentEnchant>();
                return _enchants;
            }
        }

        public int EnchantCount => _enchants == null ? 0 : _enchants.Count;

        /// <summary>
        /// Sets this copy's rarity and advances the revision.
        /// </summary>
        /// <remarks>
        /// State assignment only, matching <see cref="SetEnhancementLevel"/> and
        /// <see cref="GameInstance.SetOwner"/>. Whether a re-tier is permitted, what it
        /// costs and whether it may happen at all is server-authoritative logic elsewhere.
        ///
        /// Passing an invalid id clears the override, returning the copy to the authored
        /// rarity. The revision advances only when the value actually changes, so a
        /// no-op assignment cannot look like a mutation to anything watching.
        /// </remarks>
        public void SetRarity(DefinitionId rarity)
        {
            if (_rarity == rarity) return;

            _rarity = rarity;
            AdvanceRevision();
        }

        /// <summary>
        /// Sockets a stone and advances the revision.
        /// </summary>
        /// <remarks>Assignment only: capacity, compatibility, duplicates and cost are
        /// validated by <c>EnchantService</c>, which is the only thing that should call
        /// this. Refuses an invalid record rather than storing an unusable row.</remarks>
        public bool AddEnchant(EquipmentEnchant enchant)
        {
            if (!enchant.IsValid) return false;

            if (_enchants == null) _enchants = new List<EquipmentEnchant>();

            _enchants.Add(enchant);
            AdvanceRevision();
            return true;
        }

        /// <summary>Removes whatever is in a socket. Advances the revision only if something was.</summary>
        public bool RemoveEnchantAt(int socketIndex)
        {
            if (_enchants == null) return false;

            for (int i = 0; i < _enchants.Count; i++)
            {
                if (_enchants[i].SocketIndex != socketIndex) continue;

                _enchants.RemoveAt(i);
                AdvanceRevision();
                return true;
            }

            return false;
        }

        /// <summary>Whether a socket already holds something.</summary>
        public bool IsSocketOccupied(int socketIndex)
        {
            if (_enchants == null) return false;

            for (int i = 0; i < _enchants.Count; i++)
            {
                if (_enchants[i].SocketIndex == socketIndex) return true;
            }

            return false;
        }

        /// <summary>Whether a given stone is already socketed, for duplicate rules.</summary>
        public bool HasStone(DefinitionId stone)
        {
            if (_enchants == null || !stone.IsValid) return false;

            for (int i = 0; i < _enchants.Count; i++)
            {
                if (_enchants[i].Stone == stone) return true;
            }

            return false;
        }

        /// <summary>The lowest socket index below <paramref name="capacity"/> that is free.</summary>
        /// <remarks>Minus one when the piece is full. Sockets are filled lowest-first so a
        /// player sees stones accumulate left to right.</remarks>
        public int FirstFreeSocket(int capacity)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (!IsSocketOccupied(i)) return i;
            }

            return -1;
        }

        // ---- cards ---------------------------------------------------------------------
        //
        // A separate set from the enchants above, deliberately. Cards and status stones have
        // different capacities, different compatibility rules and different removal
        // behaviour; sharing one list would make a card consume a stone's socket and would
        // put Phase 09's semantics at the mercy of a Phase 12 change.

        /// <summary>
        /// Cards socketed into this copy, in insertion order.
        /// </summary>
        /// <remarks>Read-only to callers: only a Gameplay service may change the set, so a
        /// panel holding this cannot socket a card by writing to a list.</remarks>
        public IReadOnlyList<EquipmentCardSocket> Cards
        {
            get
            {
                if (_cards == null) _cards = new List<EquipmentCardSocket>();
                return _cards;
            }
        }

        public int CardCount => _cards == null ? 0 : _cards.Count;

        /// <summary>
        /// Sockets a card and advances the revision.
        /// </summary>
        /// <remarks>Assignment only: capacity, compatibility, duplicates and ownership are
        /// validated by <c>CardSocketService</c>, which is the only thing that should call
        /// this.</remarks>
        public bool AddCard(EquipmentCardSocket card)
        {
            if (!card.IsValid) return false;

            if (_cards == null) _cards = new List<EquipmentCardSocket>();

            _cards.Add(card);
            AdvanceRevision();
            return true;
        }

        /// <summary>
        /// Takes the card out of a socket.
        /// </summary>
        /// <remarks>
        /// Reports what was removed rather than just whether something was, because the
        /// caller has to put that exact copy back into a bag. A removal that returned only
        /// true would leave the service guessing which card it had just destroyed.
        /// </remarks>
        public bool RemoveCardAt(int socketIndex, out EquipmentCardSocket removed)
        {
            removed = default;
            if (_cards == null) return false;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].SocketIndex != socketIndex) continue;

                removed = _cards[i];
                _cards.RemoveAt(i);
                AdvanceRevision();
                return true;
            }

            return false;
        }

        /// <summary>Whether a card socket already holds something.</summary>
        public bool IsCardSocketOccupied(int socketIndex)
        {
            if (_cards == null) return false;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].SocketIndex == socketIndex) return true;
            }

            return false;
        }

        /// <summary>How many copies of one card are socketed, for per-card limits.</summary>
        public int CountOfCard(DefinitionId card)
        {
            if (_cards == null || !card.IsValid) return 0;

            int count = 0;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Card == card) count++;
            }

            return count;
        }

        /// <summary>Whether a specific owned copy is already in this piece.</summary>
        /// <remarks>What stops one card being socketed into the same piece twice; the
        /// service asks the same of every other piece to stop it being in two at once.</remarks>
        public bool HasCardInstance(InstanceId cardInstance)
        {
            if (_cards == null || !cardInstance.IsValid) return false;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].CardInstance == cardInstance) return true;
            }

            return false;
        }

        /// <summary>The lowest card socket below <paramref name="capacity"/> that is free.</summary>
        /// <remarks>Minus one when the piece is full. Filled lowest-first, so a player sees
        /// cards accumulate left to right.</remarks>
        public int FirstFreeCardSocket(int capacity)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (!IsCardSocketOccupied(i)) return i;
            }

            return -1;
        }

        private static void ValidateEnhancementLevel(int enhancementLevel)
        {
            if (enhancementLevel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(enhancementLevel), enhancementLevel, "Enhancement level cannot be negative.");
            }
        }
    }
}
