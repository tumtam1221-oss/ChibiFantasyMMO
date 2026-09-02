using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Which class a character started as and which job they currently hold.
    /// </summary>
    /// <remarks>
    /// <b>Two references, nothing more.</b> The class is the archetype chosen at creation
    /// and never changes; the job is the specialisation, absent until the first job change.
    /// Everything else about them -- level gates, branches, allowed weapons, skills --
    /// lives on <see cref="ClassDefinition"/> and <see cref="JobDefinition"/>, so retuning
    /// progression is a content patch and a character's record stays two ids long.
    ///
    /// <b>Class and job are not the same thing.</b> A class is a root; a job is a node
    /// beneath it. Collapsing them would lose the distinction between what a character
    /// began as and what they advanced into, which is the distinction the whole tree hangs
    /// from.
    ///
    /// <b>Ids, never objects.</b> No ScriptableObject, asset GUID, array index or list
    /// position appears here, so a patch may rewrite every class asset and every existing
    /// character still points at the same class.
    ///
    /// Level lives on <see cref="CharacterProgressionState"/> and is not copied here. A
    /// second level would be a second truth.
    ///
    /// This type stores; it does not judge. Whether a job change is permitted depends on
    /// content this type deliberately cannot see, so legality is decided by the job-change
    /// rules in the gameplay layer, which is also the only thing that should call
    /// <see cref="SetJob"/>. In production that decision belongs to the server.
    /// </remarks>
    [Serializable]
    public sealed class CharacterClassState : IPersistentState
    {
        [SerializeField] private CharacterId _characterId;
        [SerializeField] private DefinitionId _baseClass;
        [SerializeField] private DefinitionId _currentJob;
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers.</summary>
        public CharacterClassState()
        {
        }

        public CharacterClassState(CharacterId characterId, DefinitionId baseClass)
        {
            if (!characterId.IsValid)
            {
                throw new ArgumentException(
                    "Class state must belong to a character.", nameof(characterId));
            }

            if (!baseClass.IsValid)
            {
                throw new ArgumentException(
                    "A character must start as some class.", nameof(baseClass));
            }

            _characterId = characterId;
            _baseClass = baseClass;
            _currentJob = DefinitionId.None;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId => _characterId;

        /// <summary>Reference to the <see cref="ClassDefinition"/> chosen at creation.</summary>
        /// <remarks>Fixed for the life of the character. No method changes it, because a
        /// class change would invalidate every job beneath it.</remarks>
        public DefinitionId BaseClass => _baseClass;

        /// <summary>
        /// Reference to the current <see cref="JobDefinition"/>, or
        /// <see cref="DefinitionId.None"/> before the first job change.
        /// </summary>
        public DefinitionId CurrentJob => _currentJob;

        /// <summary>True once the character has advanced past their starting class.</summary>
        public bool HasChangedJob => _currentJob.IsValid;

        public Revision Revision => _revision;

        /// <summary>
        /// Records a new job and advances the revision.
        /// </summary>
        /// <remarks>
        /// Applies no progression rules. Whether the target follows from the character's
        /// class, job and level is decided against content before this is called; state
        /// holds ids and cannot answer that question.
        ///
        /// Setting the job already held changes nothing and leaves the revision alone.
        /// </remarks>
        public void SetJob(DefinitionId job)
        {
            if (!job.IsValid)
            {
                throw new ArgumentException("A job change needs a target job.", nameof(job));
            }

            if (job == _currentJob)
            {
                return;
            }

            _currentJob = job;
            _revision = _revision.Next();
        }
    }
}
