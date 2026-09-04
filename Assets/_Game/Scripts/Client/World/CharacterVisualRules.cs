using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Every decision the character presentation makes, as arithmetic.
    /// </summary>
    /// <remarks>
    /// <b>Separated so the decisions can be tested without a socket.</b> Whether a character
    /// is walking, which way it is facing and what may be written above its head are
    /// questions with exact answers, and answering them inside a <c>MonoBehaviour</c> that
    /// needs a spawned network object would mean they could only ever be checked by running a
    /// server. The same split the HUD already uses.
    ///
    /// <b>Nothing here is authoritative and nothing here can become authoritative.</b> These
    /// are functions of values the server already sent. There is no state, no clock and no
    /// way to reach anything that decides gameplay.
    /// </remarks>
    public static class CharacterVisualRules
    {
        /// <summary>
        /// How fast the character appears to be going, as the animator wants it: 0 to 1.
        /// </summary>
        /// <remarks>
        /// <b>Measured from the picture, not asked of the server.</b> The wire carries where
        /// a character is, not how fast; so walking is how far the visible transform actually
        /// moved. That is what makes a remote player -- for whom this client sends no input
        /// at all -- walk correctly: their position moved, so their legs move.
        ///
        /// <b>Horizontal only.</b> Falling is not walking, and including the vertical
        /// component would put a character into a walk cycle on the way down a slope.
        ///
        /// <b>Below the threshold is exactly zero, not nearly zero.</b> A blend tree fed
        /// 0.004 is a character shuffling on the spot forever.
        /// </remarks>
        public static float SpeedFor(Vector3 delta, float deltaSeconds, float threshold,
            float referenceWalkSpeed)
        {
            if (deltaSeconds <= 0f) return 0f;

            delta.y = 0f;

            float speed = delta.magnitude / deltaSeconds;

            if (speed < threshold) return 0f;

            if (referenceWalkSpeed <= 0.0001f) return 1f;

            return Mathf.Clamp01(speed / referenceWalkSpeed);
        }

        /// <summary>
        /// Which way to face, in degrees.
        /// </summary>
        /// <remarks>
        /// <b>The way they are going, and when they stop, the way they were going.</b>
        /// Standing still has no direction, so keeping the last one is the only answer that
        /// does not snap a character round to face north the instant they let go of a key.
        ///
        /// <b>Presentation, and the gate says so.</b> Facing is not replicated anywhere in
        /// this project, so this is what the local client believes rather than what the
        /// server knows. Combat validates range, never angle -- there are no directional
        /// hitboxes for this to feed, and adding one would make a client's guess about facing
        /// into a gameplay input.
        /// </remarks>
        public static float FacingFor(Vector3 delta, float deltaSeconds, float threshold,
            float previousFacing)
        {
            if (deltaSeconds <= 0f) return previousFacing;

            delta.y = 0f;

            if (delta.magnitude / deltaSeconds < threshold) return previousFacing;

            return Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y;
        }

        /// <summary>
        /// What may appear above a character's head.
        /// </summary>
        /// <remarks>
        /// The name the server replicated, trimmed, and nothing else. Not the character id,
        /// not the account, not the connection -- an identifier above a head is an identifier
        /// in every screenshot, and any of those three is how somebody else's account gets
        /// found. A character with no name shows no nameplate rather than falling back to an
        /// id, because a blank plate is a cosmetic bug and a leaked id is not.
        /// </remarks>
        public static string NameplateFor(string displayName)
        {
            return string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();
        }

        /// <summary>
        /// Whether the gap between the picture and the server is too big to be movement.
        /// </summary>
        /// <remarks>A respawn, a reconnect and a map change all arrive as one enormous
        /// position change. Easing towards it flies the character across the world in front
        /// of the player, through walls, for several seconds. Past the threshold the visible
        /// character is placed instead. Both branches draw the same authoritative
        /// position.</remarks>
        public static bool ShouldSnap(Vector3 current, Vector3 authoritative, float snapDistance)
        {
            if (snapDistance <= 0f) return false;

            return (current - authoritative).sqrMagnitude >= snapDistance * snapDistance;
        }
    }
}
