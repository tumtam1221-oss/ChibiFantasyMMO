using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The narrowest question the validator needs to ask of content: does this id exist.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IDefinitionRegistry{T}"/> because reference checking is
    /// inherently cross-type. A quest may reference an item, a monster and a map, which
    /// live in three differently typed registries, so the validator cannot take any single
    /// one of them. A composite implementation spanning several registries satisfies this
    /// without the validator knowing how many there are.
    /// </remarks>
    public interface IDefinitionLookup
    {
        bool Contains(DefinitionId id);
    }
}
