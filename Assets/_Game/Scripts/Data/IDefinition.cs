using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Anything that carries a stable content identity.
    /// </summary>
    /// <remarks>
    /// Definition-layer specific, so it lives in Data rather than Core. Core owns only
    /// the identity value object (<see cref="DefinitionId"/>).
    /// </remarks>
    public interface IDefinition
    {
        DefinitionId Id { get; }
    }
}
