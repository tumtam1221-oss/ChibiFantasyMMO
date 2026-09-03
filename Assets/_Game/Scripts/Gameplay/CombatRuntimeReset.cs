namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Puts a combatant back into a known combat-ready state.
    /// </summary>
    /// <remarks>
    /// <b>This is not respawn.</b> There is no timer, no corpse, no town, no penalty and no
    /// resurrection. It exists so a test or a sandbox can run a second scenario against the
    /// same combatant without rebuilding the world, and that is the whole of it.
    ///
    /// <b>It resets runtime state and nothing else.</b> Identity, ownership, class, job,
    /// learned skills, authored stats and every other persistent value are left exactly as
    /// they are; the only things touched are the ones combat itself created -- health and
    /// mana levels, the active action, and cooldowns. A reset that quietly restored a
    /// learned skill or minted a new identity would be a save-data bug wearing a test
    /// helper's clothes.
    ///
    /// <b>Health and mana are restored through the existing state.</b> The same
    /// <see cref="CharacterResourceState"/> path every other caller uses, so clamping and
    /// the revision behave identically. No value is written directly.
    /// </remarks>
    public static class CombatRuntimeReset
    {
        /// <summary>
        /// Restores a combatant to full and clears its combat runtime.
        /// </summary>
        /// <param name="combatant">Who to reset. Ignored when null.</param>
        /// <param name="runner">Their action runner, or null when they have none.</param>
        /// <param name="cooldowns">Their runtime cooldowns, or null when not tracked.</param>
        public static void Restore(ICombatant combatant, CombatActionRunner runner = null,
            SkillCooldownState cooldowns = null)
        {
            if (runner != null) runner.Reset();
            if (cooldowns != null) cooldowns.Reset();

            if (combatant == null) return;

            // Health through the combatant's own mutation path. A delta rather than a set,
            // because the interface deliberately exposes no setter and the clamp at the
            // ceiling makes an over-large delta land exactly on full.
            int missing = combatant.MaxHealth - combatant.CurrentHealth;
            if (missing > 0) combatant.ApplyHealthDelta(missing);

            var pool = combatant as ICombatantResourcePool;
            if (pool == null) return;

            if (pool.TryGetResource(Data.SkillResourceType.Mana, out int mana, out int maxMana)
                && maxMana > mana)
            {
                pool.TryApplyResourceDelta(Data.SkillResourceType.Mana, maxMana - mana);
            }
        }
    }
}
