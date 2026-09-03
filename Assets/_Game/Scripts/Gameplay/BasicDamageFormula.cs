namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Turns an attack figure and a defence figure into damage.
    /// </summary>
    /// <remarks>
    /// <b>Integers throughout, on purpose.</b> Health is an int in
    /// <see cref="CharacterResourceState"/>, so damage that was not would have to round
    /// somewhere, and where it rounds would decide whether a fight is won. Integers also
    /// make "no NaN, no infinity" true by construction rather than by checking: neither
    /// value exists in the integer domain.
    ///
    /// <b>Deterministic and stateless.</b> No Unity time, no random source, no field. The
    /// same inputs give the same output on any machine, on any frame, in any order, which
    /// is the property a server-authoritative rewrite will need to reproduce a client's
    /// arithmetic exactly.
    ///
    /// <b>Subtractive, and only that.</b> Attack minus defence, floored at a supplied
    /// minimum. No critical hit, no element, no resistance, no penetration, no variance;
    /// those are later systems and each would change the shape of this signature, so
    /// guessing at them now would be inventing balance nobody asked for. The whole formula
    /// is one method so replacing it is one edit.
    ///
    /// <b>The floor is a parameter, not a constant.</b> Whether a hopeless attack chips for
    /// one or does nothing at all is a balance decision, and a constant here would make it
    /// silently and permanently.
    /// </remarks>
    public static class BasicDamageFormula
    {
        /// <summary>
        /// Computes final damage.
        /// </summary>
        /// <remarks>
        /// Negative inputs are treated as zero rather than rejected. A negative attack
        /// power or defence can only arrive from a mis-authored stat or a debuff that
        /// overshot, and the useful behaviour there is a harmless number, not an exception
        /// thrown in the middle of a fight. Arithmetic runs in long so that
        /// <c>int.MaxValue</c> attack against <c>int.MaxValue</c> defence cannot overflow
        /// before it is clamped.
        /// </remarks>
        /// <param name="attackPower">Offensive figure. Values below zero count as zero.</param>
        /// <param name="defense">Defensive figure. Values below zero count as zero.</param>
        /// <param name="minimumDamage">Floor applied after subtraction. Values below zero count as zero.</param>
        /// <returns>Damage, never negative and never above <see cref="int.MaxValue"/>.</returns>
        public static int Calculate(int attackPower, int defense, int minimumDamage)
        {
            long attack = attackPower > 0 ? attackPower : 0L;
            long defence = defense > 0 ? defense : 0L;
            long floor = minimumDamage > 0 ? minimumDamage : 0L;

            long raw = attack - defence;

            if (raw < floor)
            {
                raw = floor;
            }

            if (raw > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)raw;
        }
    }
}
