using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Implemented by a definition that wants its outgoing references checked.
    /// </summary>
    /// <remarks>
    /// Opt-in and explicit. The alternative, reflecting over every field to find anything
    /// that looks like a <see cref="DefinitionId"/>, would be unable to tell a required
    /// reference from an optional one, or a content reference from a placeholder, and would
    /// silently start checking new fields the moment someone added one. A definition that
    /// states its own references stays in control of what "valid" means for it.
    ///
    /// No definition implements this yet. The mechanism is established here; deciding which
    /// of an equipment's or a quest's references are mandatory is content-rule work for a
    /// later step, and adding it now would mean guessing those rules.
    /// </remarks>
    public interface IReferencingDefinition
    {
        /// <summary>
        /// The identities this definition requires to resolve.
        /// </summary>
        /// <remarks>Return only references that must exist. Optional ones left unset are
        /// reported as missing if included here.</remarks>
        IEnumerable<DefinitionId> GetRequiredReferences();
    }
}
