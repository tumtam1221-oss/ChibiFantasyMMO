using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Anything that can take part in a fight.
    /// </summary>
    /// <remarks>
    /// <b>An interface, not a class, because combat must not own the entity.</b> A player
    /// character is a <see cref="Character"/> aggregate; a monster will be something else
    /// entirely; a destructible object may be a third thing. Combat needs the same handful
    /// of answers from all of them and has no business dictating what they are underneath.
    /// Nothing here mentions a class, a job, a monster type, a GameObject or a network
    /// identity.
    ///
    /// <b>No health is stored by combat.</b> <see cref="CurrentHealth"/> and
    /// <see cref="MaxHealth"/> are reads, and <see cref="ApplyHealthDelta"/> is expected to
    /// route straight into whatever already owns the value -- for a character that is
    /// <see cref="CharacterResourceState.ChangeHealth"/> together with the current
    /// <see cref="ResourceLimits"/>. An implementation that keeps its own copy would create
    /// the second source of truth this design exists to avoid.
    ///
    /// <b>No stats are invented either.</b> Offensive and defensive figures are looked up
    /// by <see cref="DefinitionId"/> through <see cref="TryGetCombatStat"/>, the same
    /// content-driven shape <see cref="DerivedStatsResult.TryGet"/> and
    /// <c>CharacterStatsState.TryGet</c> already use. A stat with no formula is absent
    /// rather than zero, and the caller decides what absence means.
    ///
    /// <b>Death is not a member.</b> There is no IsDead flag to fall out of step with the
    /// number; see <see cref="CombatantExtensions.IsAlive"/> for the one rule.
    /// </remarks>
    public interface ICombatant
    {
        /// <summary>Runtime identity. Reuses <see cref="InstanceId"/> so a monster and a character are identified the same way.</summary>
        InstanceId CombatantId { get; }

        /// <summary>Which side this combatant fights on.</summary>
        CombatTeam Team { get; }

        /// <summary>Current health, read from whatever already owns it. Never negative.</summary>
        int CurrentHealth { get; }

        /// <summary>The current ceiling, read from the derived-stat layer. Never negative.</summary>
        int MaxHealth { get; }

        /// <summary>Where the combatant is, for range rules only.</summary>
        CombatPosition Position { get; }

        /// <summary>
        /// Reads a combat-relevant stat.
        /// </summary>
        /// <returns>False when nothing computed this stat, which is different from a
        /// computed zero.</returns>
        bool TryGetCombatStat(DefinitionId stat, out int value);

        /// <summary>
        /// Moves health by a delta, clamped by the implementation.
        /// </summary>
        /// <remarks>Negative to wound, positive to heal. The implementation must delegate
        /// to the existing resource state rather than mutating a private field, so
        /// clamping and revision behaviour stay identical to every other caller.</remarks>
        void ApplyHealthDelta(long delta);
    }

    /// <summary>Rules about combatants that must have exactly one definition.</summary>
    public static class CombatantExtensions
    {
        /// <summary>
        /// Whether a combatant is still standing.
        /// </summary>
        /// <remarks>
        /// Written once, here, rather than as a member every implementation restates.
        /// <see cref="CharacterResourceState"/> is explicit that zero health is a number
        /// and not a state of being, so aliveness is derived from it and never stored.
        /// A null combatant is not alive, which keeps callers from null-checking twice.
        /// </remarks>
        public static bool IsAlive(this ICombatant combatant)
        {
            return combatant != null && combatant.CurrentHealth > 0;
        }
    }
}
