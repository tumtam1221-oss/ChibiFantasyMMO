using System;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Which way an attacker is pointing when it swings at something.
    /// </summary>
    /// <remarks>
    /// <b>Computed by the server, for the presentation.</b> A client drawing a swing needs
    /// to turn the character towards what it hit, and it has no trustworthy way of knowing
    /// what that was: the attacker's own client knows its selection, an observer's client
    /// knows nothing. The server knows both positions at the moment it accepts the attack, so
    /// it works the heading out once and every client draws the same one.
    ///
    /// <b>A number, not a target id.</b> Sending the target's identity would let a client
    /// resolve the heading itself, and also let it resolve a great deal else; the heading
    /// alone is the whole of what a swing animation needs, and it is four bytes.
    ///
    /// <b>Yaw in degrees, Unity's way round.</b> Zero faces +Z, positive turns towards +X.
    /// The same convention the monster's replicated facing already uses, so one
    /// <c>Euler(0, yaw, 0)</c> reads both.
    /// </remarks>
    public static class CombatFacing
    {
        /// <summary>Heading from one position to another, in degrees about the vertical.</summary>
        /// <remarks>
        /// Two combatants standing on the same spot have no heading between them; rather
        /// than return an arbitrary one, the caller's fallback is handed back so a swing at
        /// point-blank range keeps the way the attacker was already facing.
        /// </remarks>
        public static float YawDegrees(CombatPosition from, CombatPosition to, float fallback)
        {
            float dx = to.X - from.X;
            float dz = to.Z - from.Z;

            if (dx * dx + dz * dz < 1e-8f) return fallback;

            return (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
        }
    }
}
