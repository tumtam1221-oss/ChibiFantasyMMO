using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A combatant that has resource pools beyond health.
    /// </summary>
    /// <remarks>
    /// <b>Optional, and deliberately separate from <see cref="ICombatant"/>.</b> Health is
    /// the one thing every fighter must have, so it stays on the base contract. Mana is
    /// not: a training dummy, a destructible crate and most monsters have none, and
    /// widening <see cref="ICombatant"/> would force every one of them to answer a question
    /// that has no meaning for it. Adding this as a second interface also leaves the 07.2
    /// contract untouched.
    ///
    /// <b>Absence is reported, never faked.</b> A skill whose cost or effect needs a pool
    /// the combatant does not have is rejected or reported unsupported by name. Nothing
    /// treats a missing pool as an empty one, because "you have no mana" and "you have zero
    /// mana" call for different answers.
    ///
    /// <b>No pool is stored by combat.</b> Implementations forward to whatever already owns
    /// the value -- for a character that is <see cref="CharacterResourceState"/> together
    /// with the current <see cref="ResourceLimits"/> -- so clamping and the revision behave
    /// exactly as they do for every other caller.
    ///
    /// <see cref="SkillResourceType"/> is reused rather than a new pool enum, so the pools
    /// a skill may cost and the pools a combatant may hold are named by the same type.
    /// </remarks>
    public interface ICombatantResourcePool
    {
        /// <summary>Whether this combatant has the pool at all.</summary>
        bool HasResource(SkillResourceType resource);

        /// <summary>
        /// Reads a pool.
        /// </summary>
        /// <returns>False when the combatant has no such pool, which is different from a
        /// pool that happens to be empty.</returns>
        bool TryGetResource(SkillResourceType resource, out int current, out int max);

        /// <summary>
        /// Moves a pool by a delta, clamped by the implementation.
        /// </summary>
        /// <returns>False when the combatant has no such pool, in which case nothing
        /// changed.</returns>
        bool TryApplyResourceDelta(SkillResourceType resource, long delta);
    }
}
