namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Spaces a client's attack requests to the interval its character is allowed.
    /// </summary>
    /// <remarks>
    /// <b>A prediction, not a permission.</b> The server refuses any swing that arrives
    /// before its own attack state machine is idle again, whatever this says. What this
    /// decides is only when it is <i>worth asking</i>: a request sent inside the interval
    /// is refused and wasted, and a request sent every frame is the spam the brief forbids.
    /// So the next request goes out one interval after the last, plus a little slack -- a
    /// swing asked for a few milliseconds late lands a few milliseconds late, while one
    /// asked for a few milliseconds early is refused outright and costs a whole interval.
    ///
    /// <b>The interval is the character's, read each time.</b> It comes from the replicated
    /// attack speed through the same conversion the server paces with, so a ring put on
    /// mid-fight changes the cadence on the very next swing without anything here knowing
    /// what a ring is. The old fixed 0.6 s knew nothing about the character at all.
    ///
    /// <b>Plain C#.</b> Time arrives as an argument, so the cadence is asserted in a test
    /// that needs no frame to pass.
    /// </remarks>
    public sealed class AttackRequestPacer
    {
        /// <summary>Seconds added to the interval, so the request errs on the late side.</summary>
        public const float SlackSeconds = 0.03f;

        private float _nextAt = float.NegativeInfinity;

        /// <summary>When the next request may go out, on the caller's clock.</summary>
        public float NextRequestAt => _nextAt;

        /// <summary>How many requests this pacer has let through.</summary>
        public int Sent { get; private set; }

        /// <summary>Whether a request sent now is worth sending.</summary>
        public bool IsReady(float now)
        {
            return now >= _nextAt;
        }

        /// <summary>
        /// Books a request if one is due, and schedules the next.
        /// </summary>
        /// <param name="now">The caller's clock.</param>
        /// <param name="intervalSeconds">The character's own interval, from its attack speed.</param>
        /// <returns>False when the last request is still inside its interval.</returns>
        public bool TryBegin(float now, float intervalSeconds)
        {
            if (!IsReady(now)) return false;

            if (intervalSeconds < 0f) intervalSeconds = 0f;

            _nextAt = now + intervalSeconds + SlackSeconds;
            Sent++;

            return true;
        }

        /// <summary>Forgets the schedule, for a new target or a respawn.</summary>
        public void Reset()
        {
            _nextAt = float.NegativeInfinity;
        }
    }
}
