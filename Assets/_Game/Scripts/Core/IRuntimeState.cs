namespace ChibiFantasy.Core
{
    /// <summary>
    /// State that exists only while something is active, and can be rebuilt.
    /// </summary>
    /// <remarks>
    /// Current position, an active buff list, a follow target, a presentation cache. Losing
    /// it on reconnect or restart is acceptable because it can be reconstructed from
    /// persistent state and the world.
    ///
    /// Deliberately carries no serialization requirement. Forcing every ephemeral value
    /// through a persistence-shaped API is how runtime concerns end up in save data.
    /// </remarks>
    public interface IRuntimeState : IVersionedState
    {
    }
}
