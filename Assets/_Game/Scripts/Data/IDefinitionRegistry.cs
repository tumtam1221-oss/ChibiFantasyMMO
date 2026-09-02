using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Read-only lookup of definitions by stable identity.
    /// </summary>
    /// <remarks>
    /// Deliberately small and dependency-light: no loading, no async, no source. How
    /// definitions are discovered is a separate concern from how they are looked up, which
    /// is what lets content arrive later from local assets, bundles, a patch or a server
    /// without this contract changing.
    ///
    /// Read-only on purpose. Registration lives on the concrete registry so that handing a
    /// system an IDefinitionRegistry does not also hand it the ability to alter content.
    ///
    /// Identity uniqueness is scoped to one registry, not to the whole project. See
    /// <see cref="DefinitionRegistry{T}"/> for why.
    /// </remarks>
    public interface IDefinitionRegistry<T> : IDefinitionLookup where T : IDefinition
    {
        bool TryGet(DefinitionId id, out T definition);

        IReadOnlyList<T> All { get; }

        int Count { get; }
    }
}
