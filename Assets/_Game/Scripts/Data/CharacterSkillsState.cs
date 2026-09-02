using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The skills a character has learned, and at what rank.
    /// </summary>
    /// <remarks>
    /// <b>Not a SkillInstance.</b> The name was considered and rejected. An
    /// <see cref="ItemInstance"/> or <see cref="PetInstance"/> is an independent object: it
    /// carries its own <see cref="InstanceId"/>, can be traded, stacked, dropped and can
    /// exist while owned by nobody. A learned skill is none of those things. It cannot be
    /// separated from the character who learned it, two characters knowing the same skill
    /// share nothing, and there is no question a per-skill identity would answer. Minting
    /// one would be a second identity system bought for nothing and paid for in every
    /// database row. The character plus the <see cref="DefinitionId"/> already identify a
    /// learned skill uniquely, and this type enforces that by refusing duplicates.
    ///
    /// <b>A sibling aggregate.</b> Linked to its character by <see cref="CharacterId"/> and
    /// carrying its own <see cref="Revision"/>, exactly like appearance, progression and
    /// stats, so <see cref="CharacterState"/> stays small and a skill change does not
    /// version the whole character. The ownership chain is account to character to learned
    /// skills to definition, and every link in it is an existing identity type; no
    /// SkillOwnerId was invented.
    ///
    /// <b>A list, not a dictionary.</b> Unity does not serialize dictionaries. Insertion
    /// order makes the persisted form deterministic and therefore diffable, and lookup is a
    /// short scan over the handful of skills one character knows. Duplicates are prevented
    /// on the way in rather than tolerated and cleaned up later.
    ///
    /// <b>State, not rules.</b> Nothing here decides whether a character is allowed to
    /// learn a skill. Class and job availability, prerequisites, level gates and skill
    /// points all depend on content this type deliberately cannot see, and on acquisition
    /// rules that do not exist yet. This records what it is told; in production only the
    /// server tells it. Nothing here is combat either: no cooldown, no cast progress, no
    /// resource spend. Those are runtime concerns and are not stored, because storing them
    /// would put combat timing into save data.
    /// </remarks>
    [Serializable]
    public sealed class CharacterSkillsState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private List<CharacterSkillEntry> _skills = new List<CharacterSkillEntry>();
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers, which construct before populating.</summary>
        public CharacterSkillsState()
        {
        }

        public CharacterSkillsState(CharacterId characterId)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException(
                    "Skills must belong to a character.", nameof(characterId));
            }

            _characterId = characterId;
            _skills = new List<CharacterSkillEntry>();
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId => _characterId;

        public Revision Revision => _revision;

        /// <summary>How many distinct skills the character knows.</summary>
        public int Count => Entries.Count;

        /// <summary>
        /// Every learned skill and its rank, in the order they were learned.
        /// </summary>
        /// <remarks>A fresh read-only view each call rather than a cached one, so it can
        /// never be cast back to the backing list and can never go stale after
        /// deserialization replaces that list.</remarks>
        public IReadOnlyList<CharacterSkillEntry> Skills => Entries.AsReadOnly();

        /// <summary>Whether the character knows a skill at all.</summary>
        public bool Knows(DefinitionId skill)
        {
            return IndexOf(skill) >= 0;
        }

        /// <summary>Reads the rank of a skill, reporting whether it is known.</summary>
        public bool TryGetRank(DefinitionId skill, out int rank)
        {
            int index = IndexOf(skill);

            if (index < 0)
            {
                rank = 0;
                return false;
            }

            rank = Entries[index].Rank;
            return true;
        }

        /// <summary>The rank of a skill, or <paramref name="fallback"/> if it is unknown.</summary>
        public int GetRankOrDefault(DefinitionId skill, int fallback)
        {
            return TryGetRank(skill, out int rank) ? rank : fallback;
        }

        /// <summary>
        /// Learns a skill at rank one, advancing the revision.
        /// </summary>
        /// <remarks>
        /// Applies no acquisition rules. Whether this character may learn this skill is
        /// decided against content before this is called.
        /// </remarks>
        /// <returns>False, leaving the revision alone, when the skill is already known.
        /// Re-learning is not a change, and a caller that wants a higher rank wants
        /// <see cref="SetRank"/>.</returns>
        public bool Learn(DefinitionId skill)
        {
            if (!skill.IsValid)
            {
                throw new ArgumentException("Learning a skill needs a valid skill id.", nameof(skill));
            }

            if (IndexOf(skill) >= 0)
            {
                return false;
            }

            Entries.Add(new CharacterSkillEntry(skill, 1));
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Records the rank of a skill, learning it if it is not yet known, and advances
        /// the revision.
        /// </summary>
        /// <remarks>
        /// The single path for upgrades, so a skill can never appear twice. The rank is
        /// checked only against the universal floor of one; the ceiling is authored on
        /// <see cref="SkillDefinition"/>, which this type cannot see because it holds ids
        /// rather than definitions, and is enforced by
        /// <see cref="CharacterSkillsValidator"/> against the content registry. That is the
        /// same split stats already use, and it is what keeps state from reaching into
        /// content.
        ///
        /// Setting the rank a skill already holds changes nothing and leaves the revision
        /// alone, so the counter tracks real transitions rather than call volume.
        /// </remarks>
        public void SetRank(DefinitionId skill, int rank)
        {
            if (!skill.IsValid)
            {
                throw new ArgumentException("A skill entry needs a valid skill id.", nameof(skill));
            }

            if (rank < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rank), rank, "A known skill is held at rank one or greater.");
            }

            int index = IndexOf(skill);

            if (index < 0)
            {
                Entries.Add(new CharacterSkillEntry(skill, rank));
                _revision = _revision.Next();
                return;
            }

            if (Entries[index].Rank == rank)
            {
                return;
            }

            Entries[index] = new CharacterSkillEntry(skill, rank);
            _revision = _revision.Next();
        }

        /// <summary>
        /// Removes a skill, advancing the revision only if one was known.
        /// </summary>
        /// <remarks>Exists for migration and administration, not for gameplay. Whether a
        /// player may unlearn anything is a product decision this type does not make.</remarks>
        public bool Forget(DefinitionId skill)
        {
            int index = IndexOf(skill);

            if (index < 0)
            {
                return false;
            }

            Entries.RemoveAt(index);
            _revision = _revision.Next();
            return true;
        }

        private List<CharacterSkillEntry> Entries
        {
            get
            {
                // Deserialization can leave the list null when the field is absent.
                return _skills ?? (_skills = new List<CharacterSkillEntry>());
            }
        }

        private int IndexOf(DefinitionId skill)
        {
            List<CharacterSkillEntry> entries = Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Skill == skill)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
