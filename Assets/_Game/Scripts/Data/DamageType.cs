namespace ChibiFantasy.Data
{
    /// <summary>
    /// Which defence a damage effect is resisted by.
    /// </summary>
    /// <remarks>
    /// <b>Not the same thing as <see cref="ElementType"/>.</b> An element is fire, water or
    /// dark and will one day meet an elemental resistance; this says whether a blow is
    /// answered by armour or by magic defence. A fire sword swing and a fire spell share an
    /// element and are resisted by different stats, which is exactly why one field cannot
    /// serve both and why this was added rather than overloading the element.
    ///
    /// <b>Two values, and no more.</b> Hybrid, true and pure damage are all real ideas and
    /// none of them is authored yet; adding one later is a value here plus a branch in the
    /// executor, and no existing effect data changes. That is the same additive rule
    /// <see cref="SkillEffect"/> already states for its kinds.
    ///
    /// <b><see cref="None"/> means unclassified, and resolves as physical.</b> It exists
    /// because zero is what every damage effect authored before this field arrives will
    /// deserialize to, and every one of those was written as an ordinary attack. Treating
    /// it as physical is stated here rather than assumed silently in the executor; content
    /// authored from now on names its type explicitly.
    /// </remarks>
    public enum DamageType
    {
        /// <summary>Unclassified. Resolves as <see cref="Physical"/>; see the remarks.</summary>
        None = 0,

        /// <summary>Resisted by the physical defence stat named in the combat rules.</summary>
        Physical = 1,

        /// <summary>Resisted by the magic defence stat named in the combat rules.</summary>
        Magic = 2
    }
}
