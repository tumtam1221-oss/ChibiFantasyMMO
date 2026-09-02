using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The identity every runtime, player-owned instance carries.
    /// </summary>
    /// <remarks>
    /// Four members only. Anything an instance type does not universally need belongs on
    /// that type, not here: a pet has a level, an item has a quantity, and neither concept
    /// generalises.
    ///
    /// Depends on no UnityEngine.Object, no MonoBehaviour and no networking type, so
    /// instances remain plain data that a server can hold, validate and persist without a
    /// scene or a Unity runtime.
    /// </remarks>
    public interface IGameInstance
    {
        /// <summary>Identity of this specific copy.</summary>
        InstanceId InstanceId { get; }

        /// <summary>Identity of the authored content this is a copy of.</summary>
        DefinitionId DefinitionId { get; }

        /// <summary>Who holds it. May be <see cref="OwnerId.None"/> for unassigned state.</summary>
        OwnerId Owner { get; }

        /// <summary>Change counter, advanced whenever mutable state changes.</summary>
        Revision Revision { get; }
    }
}
