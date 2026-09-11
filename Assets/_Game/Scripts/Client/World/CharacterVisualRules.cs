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
        /// Eases the number the blend tree is shown towards the number just measured.
        /// </summary>
        /// <remarks>
        /// <b>Why the raw measurement is not good enough.</b> Speed is inferred from how far
        /// the visible transform moved since the last frame, and that distance arrives in
        /// steps: the server replicates position roughly ten times a second, so a frame that
        /// lands between two snapshots measures a smaller gap than one that lands on top of
        /// a fresh one. Fed straight to a blend tree the result is a walk cycle that
        /// stutters in and out even though the character is travelling at a constant pace.
        /// Easing turns that staircase into a ramp.
        ///
        /// <b>Why it is computed here rather than by the animator.</b> Unity offers
        /// <c>Animator.SetFloat(hash, value, dampTime, deltaTime)</c>, which does the same
        /// arithmetic inside the animator -- and stops doing it when the animator is culled,
        /// which happens to every character the camera is not looking at. The parameter then
        /// freezes part-way through a blend and the character is found mid-stride when the
        /// camera comes back. Doing it out here means the value handed over is already
        /// correct, and culling cannot reach it. It also makes the easing something a test
        /// can assert on without an <c>Animator</c>.
        ///
        /// <b>Soft at the start, but it has to actually arrive.</b> An exponential alone
        /// approaches its target without ever reaching it, and the tail is long: at a
        /// smoothing of 0.15 s a character who stopped walking still carries a tenth of a
        /// walk in the blend a third of a second later, and a residue of it for the best
        /// part of a second. So the exponential is paired with a floor rate, and whichever
        /// of the two is further along wins. The exponential is the faster of the two while
        /// the gap is wide, which is where the softness is wanted; the floor takes over once
        /// the gap is small, which is where the exponential has nothing left to give. The
        /// result reaches its target in a bounded time -- about twice the smoothing -- and
        /// still leans in rather than snapping.
        ///
        /// <b>Frame-rate independent.</b> Both halves are expressed against elapsed time
        /// rather than per frame, so a machine at thirty frames and one at two hundred ease
        /// at the same visible rate.
        ///
        /// <b>Presentation only.</b> This changes which frame of which clip is drawn. It has
        /// no path to position, which the server owns and replicates.
        /// </remarks>
        public static float DampedSpeed(float current, float target, float deltaSeconds,
            float smoothingSeconds)
        {
            if (deltaSeconds <= 0f) return current;

            // No smoothing configured is the honest identity, not a division by zero.
            if (smoothingSeconds <= 0.0001f) return target;

            float t = 1f - Mathf.Exp(-deltaSeconds / smoothingSeconds);

            float eased = Mathf.Lerp(current, target, Mathf.Clamp01(t));

            // The floor: the whole range crossed in twice the smoothing time, no slower.
            float floored = Mathf.MoveTowards(current, target,
                deltaSeconds / (2f * smoothingSeconds));

            // Whichever got closer. Standing still has to end up exactly zero, because a
            // blend tree fed a residue is a character shuffling on the spot forever -- the
            // same reason SpeedFor floors small measurements rather than passing them on.
            return Mathf.Abs(target - floored) < Mathf.Abs(target - eased) ? floored : eased;
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
