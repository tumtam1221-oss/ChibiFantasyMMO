namespace ChibiFantasy.Core
{
    /// <summary>
    /// State that must outlive the process holding it.
    /// </summary>
    /// <remarks>
    /// Survives reconnect, server restart and patching, and is therefore what a future
    /// persistence layer stores. Implementations must stay serializable and must identify
    /// themselves with stable identities such as <see cref="InstanceId"/>,
    /// <see cref="DefinitionId"/> and <see cref="OwnerId"/>, never with a field order, an
    /// array index, an asset path or a Unity instance ID.
    ///
    /// The marker is what makes the boundary real rather than a comment: a persistence API
    /// can accept this and refuse <see cref="IRuntimeState"/>, so ephemeral values cannot
    /// drift into database models by accident.
    /// </remarks>
    public interface IPersistentState : IVersionedState
    {
    }
}
