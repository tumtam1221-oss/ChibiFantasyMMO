using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One skill a character knows, and how far they have taken it.
    /// </summary>
    /// <remarks>
    /// <b>Two fields, and that is the whole point.</b> Everything else about the skill --
    /// its name, description, category, target type, resource type, cost, cooldown, cast
    /// time, range, effects, scaling, icon, animation, VFX and SFX -- lives on
    /// <see cref="SkillDefinition"/> and is deliberately not copied here. A patch that
    /// retunes a skill must change what every character's copy does, and it can only do
    /// that if no character stored a copy of the answer.
    ///
    /// <b>Presence means learned.</b> There is no learned flag. A skill a character knows
    /// is an entry; a skill they do not know is the absence of one, exactly as an unset
    /// stat is in <see cref="CharacterStatsState"/>. A flag would admit the contradictory
    /// combinations -- unlearned at rank five, learned at rank zero -- that a validator
    /// would then have to detect. The shape that cannot express the fault is better than
    /// the check that catches it.
    ///
    /// <b>Rank starts at one.</b> It matches <see cref="SkillLevelEntry.Level"/>, so a rank
    /// indexes the skill's authored level table directly and rank zero never has to be
    /// special-cased as "known but useless".
    ///
    /// An integer because ranks are counted, and a <see cref="DefinitionId"/> rather than
    /// an array index, list position, asset GUID or object reference, so a learned skill
    /// survives content being added, removed and reordered.
    /// </remarks>
    [Serializable]
    public struct CharacterSkillEntry
    {
        [SerializeField] private DefinitionId _skill;
        [SerializeField] private int _rank;

        public CharacterSkillEntry(DefinitionId skill, int rank)
        {
            _skill = skill;
            _rank = rank;
        }

        /// <summary>Reference to a <see cref="SkillDefinition"/>.</summary>
        public DefinitionId Skill => _skill;

        /// <summary>
        /// Which rank of the skill the character holds. One or greater.
        /// </summary>
        /// <remarks>Whether a rank is within the skill's authored maximum needs the
        /// definition this entry names but cannot see; that is
        /// <see cref="CharacterSkillsValidator"/>'s job.</remarks>
        public int Rank => _rank;
    }
}
