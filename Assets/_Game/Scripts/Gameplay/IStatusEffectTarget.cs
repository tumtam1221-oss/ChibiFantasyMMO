namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A combatant that can carry status effects.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not on <c>ICombatant</c>.</b> Everything that fights has health, a
    /// team and a position; not everything that fights has a status list. A world-boss
    /// summon, a training dummy and a monster whose status runtime does not exist yet are
    /// all legitimate combatants, and widening the combat interface would have forced every
    /// one of them to answer a question they have no answer to. Asking through this instead
    /// means "cannot carry status" and "carries none" stay distinguishable.
    ///
    /// <b>It exposes the state, not operations.</b> There is no Apply, no Remove and no
    /// Clear here. <see cref="StatusEffectService"/> decides whether an effect may land and
    /// <see cref="StatusEffectRuntimeState"/> records it; a target that could apply
    /// something to itself would be a second apply path, which is exactly what the status
    /// architecture avoids.
    /// </remarks>
    public interface IStatusEffectTarget
    {
        /// <summary>
        /// The status effects currently on this target, or null when none are tracked.
        /// </summary>
        /// <remarks>Null is a real answer and callers check it: a skill that would apply a
        /// debuff to a target nobody tracks status for reports itself unsupported rather
        /// than pretending it landed.</remarks>
        StatusEffectRuntimeState Status { get; }
    }
}
