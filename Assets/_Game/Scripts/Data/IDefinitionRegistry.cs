using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Read-only lookup of definitions by stable identity.
    /// </summary>
    /// <remarks>
    /// Deliberately small and dependency-light: no loading, no async, no source. How
    /// definitions are discovered is a separate concern from how they are looked up.
    /// </remarks>
    public interface IDefinitionRegistry<T> where T : IDefinition
    {
        bool TryGet(DefinitionId id, out T definition);

        bool Contains(DefinitionId id);

        IReadOnlyList<T> All { get; }
    }
}
