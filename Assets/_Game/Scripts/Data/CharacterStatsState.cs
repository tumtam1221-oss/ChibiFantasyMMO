using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// A character's persisted base stats.
    /// </summary>
    /// <remarks>
    /// <b>The six core attributes</b> the game is built around, documented here as
    /// semantics only. None of them appears as a constant, an enum or a field; each is a
    /// <see cref="StatDefinition"/> asset referenced by id.
    /// <list type="bullet">
    /// <item>STR, strength</item>
    /// <item>AGI, agility</item>
    /// <item>VIT, vitality</item>
    /// <item>INT, intelligence</item>
    /// <item>DEX, dexterity</item>
    /// <item>LUK, luck</item>
    /// </list>
    /// What each one does is deliberately absent. No formula turns strength into attack
    /// power here; derived stats and combat are later layers, and writing those
    /// relationships into the persistence layer would freeze balance decisions into save
    /// data.
    ///
    /// <b>Base stats only.</b> What is stored is what the character owns before anything is
    /// added to it. Class and job modifiers, equipment, cards, pets, Devil Fruits and buffs
    /// all contribute later and none of them is stored here, so the base a player earned
    /// stays recoverable no matter how the modifier stack is rebalanced.
    ///
    /// A sibling aggregate like appearance and progression: linked by
    /// <see cref="CharacterId"/>, carrying its own <see cref="Revision"/>, so
    /// <see cref="CharacterState"/> stays small.
    ///
    /// Stored as a list rather than a dictionary because Unity does not serialize
    /// dictionaries. Order is insertion order, so the persisted form is deterministic and
    /// diffable, and lookup is a short scan over a handful of stats.
    ///
    /// Nothing here is authoritative. In production the server decides what a character's
    /// stats are; this records what it is told.
    /// </remarks>
    [Serializable]
    public sealed class CharacterStatsState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private List<CharacterStatEntry> _stats = new List<CharacterStatEntry>();
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers.</summary>
        public CharacterStatsState()
        {
        }

        public CharacterStatsState(CharacterId characterId)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException("Stats must belong to a character.", nameof(characterId));
            }

            _characterId = characterId;
            _stats = new List<CharacterStatEntry>();
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId => _characterId;

        public Revision Revision => _revision;

        /// <summary>How many stats this character has a value for.</summary>
        public int Count => Entries.Count;

        /// <summary>
        /// Every stat and value, in insertion order.
        /// </summary>
        /// <remarks>A fresh read-only view each call rather than a cached one, so it can
        /// never be cast back to the backing list and can never go stale after
        /// deserialization replaces that list.</remarks>
        public IReadOnlyList<CharacterStatEntry> Stats => Entries.AsReadOnly();

        /// <summary>Whether a value has been recorded for a stat.</summary>
        public bool Contains(DefinitionId stat)
        {
            return IndexOf(stat) >= 0;
        }

        /// <summary>Reads a stat, reporting whether the character has one.</summary>
        public bool TryGet(DefinitionId stat, out int value)
        {
            int index = IndexOf(stat);

            if (index < 0)
            {
                value = 0;
                return false;
            }

            value = Entries[index].Value;
            return true;
        }

        /// <summary>Reads a stat, falling back when the character has no value for it.</summary>
        public int GetOrDefault(DefinitionId stat, int fallback)
        {
            return TryGet(stat, out int value) ? value : fallback;
        }

        /// <summary>
        /// Records a stat value, adding it or replacing the existing one, and advances the
        /// revision.
        /// </summary>
        /// <remarks>
        /// Setting an existing stat replaces it in place; a stat can never appear twice.
        ///
        /// Enforces only the universal floor of zero. The per-stat ceiling is authored on
        /// <see cref="StatDefinition.MaxValue"/>, which this type cannot see because it
        /// holds ids rather than definitions, and is checked by
        /// <see cref="CharacterStatsValidator"/> against the content registry. That is the
        /// same split used for item quantity and enhancement level, and it keeps state from
        /// reaching into content.
        /// </remarks>
        public void Set(DefinitionId stat, int value)
        {
            if (!stat.IsValid)
            {
                throw new ArgumentException("A stat entry needs a valid stat id.", nameof(stat));
            }

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "A base stat cannot be negative.");
            }

            int index = IndexOf(stat);
            var entry = new CharacterStatEntry(stat, value);

            if (index >= 0)
            {
                Entries[index] = entry;
            }
            else
            {
                Entries.Add(entry);
            }

            _revision = _revision.Next();
        }

        /// <summary>Removes a stat, advancing the revision only if one was present.</summary>
        public bool Remove(DefinitionId stat)
        {
            int index = IndexOf(stat);

            if (index < 0)
            {
                return false;
            }

            Entries.RemoveAt(index);
            _revision = _revision.Next();
            return true;
        }

        private List<CharacterStatEntry> Entries
        {
            get
            {
                // Deserialization can leave the list null when the field is absent.
                return _stats ?? (_stats = new List<CharacterStatEntry>());
            }
        }

        private int IndexOf(DefinitionId stat)
        {
            List<CharacterStatEntry> entries = Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Stat == stat)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
