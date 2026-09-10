using System;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Which part of the day the world is in.
    /// </summary>
    /// <remarks>
    /// Four names rather than a raw number, because the things that will ask are content
    /// decisions -- which monsters walk, whether the lanterns are lit, which grade the sky
    /// uses -- and none of them wants to re-derive "is 0.78 dusk yet" for itself. The
    /// boundaries live in <see cref="WorldClock.PhaseAt"/> and nowhere else.
    /// </remarks>
    public enum WorldTimePhase
    {
        Night = 0,
        Dawn = 1,
        Day = 2,
        Dusk = 3
    }

    /// <summary>
    /// The world's time of day, owned by the server.
    /// </summary>
    /// <remarks>
    /// <b>Why the server owns this.</b> Every player must stand under the same sky. A clock
    /// that ran on each client would drift the moment two machines disagreed about frame
    /// time, and two players in the same field would see different light -- which reads as a
    /// bug long before anyone works out it is a clock. So this advances on the server tick
    /// and the value is replicated; the client renders it and never decides it.
    ///
    /// <b>Engine-free on purpose.</b> This lives in the Gameplay assembly, which the
    /// architecture tests keep clear of UnityEngine and FishNet. Nothing here knows about a
    /// sun, a skybox or a volume profile: it answers "what time is it" and stops. The Unity
    /// side reads <see cref="TimeOfDay"/> and decides what that looks like.
    ///
    /// <b>Elapsed is a double.</b> A float holding seconds loses its last digit after a few
    /// hours of uptime, and a server is expected to run for days; the wrap would start to
    /// stutter. The delta arriving each tick is a float because that is what the tick has,
    /// but it is accumulated in double and only the wrapped 0..1 fraction is ever narrowed.
    /// </remarks>
    public sealed class WorldClock
    {
        /// <summary>One real hour per in-game day.</summary>
        /// <remarks>The shipping cadence, chosen so a player in a normal session sees the
        /// sky change at least once without the change being so fast it reads as a flicker.</remarks>
        public const double DefaultSecondsPerDay = 3600.0;

        /// <summary>Refuses a day so short that a single tick could skip a whole phase.</summary>
        public const double MinimumSecondsPerDay = 60.0;

        private readonly double _secondsPerDay;
        private double _elapsed;

        /// <param name="secondsPerDay">Real seconds in one in-game day. Clamped up to
        /// <see cref="MinimumSecondsPerDay"/>, because a day shorter than that stops being a
        /// day and starts being a strobe.</param>
        /// <param name="startTimeOfDay">Where the world begins, 0..1. A fresh server starting
        /// at midnight every time would mean nobody ever sees the town by daylight in the
        /// first minutes of a session, so this is authored rather than assumed.</param>
        public WorldClock(double secondsPerDay = DefaultSecondsPerDay,
            double startTimeOfDay = 0.30)
        {
            _secondsPerDay = Math.Max(MinimumSecondsPerDay, secondsPerDay);
            _elapsed = Wrap01(startTimeOfDay) * _secondsPerDay;
        }

        /// <summary>Real seconds in one in-game day.</summary>
        public double SecondsPerDay => _secondsPerDay;

        /// <summary>
        /// Where in the day the world is: 0 and 1 are midnight, 0.5 is noon.
        /// </summary>
        /// <remarks>Midnight at zero rather than at dawn so that the number reads like a
        /// clock face: the halfway point is the middle of the day.</remarks>
        public float TimeOfDay => (float)((_elapsed / _secondsPerDay) % 1.0);

        /// <summary>How many whole in-game days have passed since the world started.</summary>
        public long Day => (long)(_elapsed / _secondsPerDay);

        /// <summary>The current phase.</summary>
        public WorldTimePhase Phase => PhaseAt(TimeOfDay);

        /// <summary>
        /// Advances the clock.
        /// </summary>
        /// <remarks>A non-positive delta is ignored rather than rewinding: a paused or
        /// stepped-back server must not run the sky backwards, and the callers that pass a
        /// zero delta are doing so to settle state, not to move time.</remarks>
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            _elapsed += deltaSeconds;
        }

        /// <summary>
        /// Puts the clock at an exact point in the day, keeping the day count.
        /// </summary>
        /// <remarks>For an operator or a test that needs night now, and for a server that is
        /// restoring a persisted time rather than starting fresh.</remarks>
        public void SetTimeOfDay(double timeOfDay)
        {
            _elapsed = (Day * _secondsPerDay) + (Wrap01(timeOfDay) * _secondsPerDay);
        }

        /// <summary>
        /// Which phase a given 0..1 time falls in.
        /// </summary>
        /// <remarks>
        /// <b>The boundaries are here and only here.</b> Dawn and dusk are deliberately short
        /// -- an eighth of the day each -- because they are the transitions, and a transition
        /// that lasts as long as the states either side stops reading as one. Day is the
        /// longest stretch: this is a town players are meant to enjoy looking at.
        ///
        /// Static so callers that hold a replicated number rather than the clock itself --
        /// which is every client -- get the same answer without a second copy of the rule.
        /// </remarks>
        public static WorldTimePhase PhaseAt(float timeOfDay)
        {
            float t = (float)Wrap01(timeOfDay);

            if (t < 0.20f) return WorldTimePhase.Night;   // 00:00 - 04:48
            if (t < 0.30f) return WorldTimePhase.Dawn;    // 04:48 - 07:12
            if (t < 0.75f) return WorldTimePhase.Day;     // 07:12 - 18:00
            if (t < 0.85f) return WorldTimePhase.Dusk;    // 18:00 - 20:24

            return WorldTimePhase.Night;                  // 20:24 - 24:00
        }

        /// <summary>
        /// How much of the night look applies at a given time, 0 for full day and 1 for full night.
        /// </summary>
        /// <remarks>
        /// <b>A curve, not a switch.</b> The client cross-fades between a day grade and a
        /// night grade, and a hard cut at a phase boundary would show as the sky snapping.
        /// This ramps across dawn and dusk so the change is something a player notices only
        /// if they look up.
        ///
        /// It lives beside <see cref="PhaseAt"/> rather than in the presenter so that the
        /// blend and the phase can never disagree about when night is.
        /// </remarks>
        public static float NightBlendAt(float timeOfDay)
        {
            float t = (float)Wrap01(timeOfDay);

            if (t < 0.20f) return 1f;                                 // deep night
            if (t < 0.30f) return 1f - Smooth((t - 0.20f) / 0.10f);   // dawn, night fading out
            if (t < 0.75f) return 0f;                                 // day
            if (t < 0.85f) return Smooth((t - 0.75f) / 0.10f);        // dusk, night fading in

            return 1f;
        }

        /// <summary>
        /// The sun's rotation about the horizon for a given time, in degrees.
        /// </summary>
        /// <remarks>
        /// Zero at midnight and 360 at the next midnight, so the light sweeps once per day.
        /// Returned as a plain number because this assembly may not name a Quaternion; the
        /// presenter decides which axis it turns.
        /// </remarks>
        public static float SunDegreesAt(float timeOfDay) => (float)(Wrap01(timeOfDay) * 360.0);

        private static double Wrap01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;

            double wrapped = value % 1.0;

            return wrapped < 0.0 ? wrapped + 1.0 : wrapped;
        }

        /// <summary>Smoothstep, so a ramp eases in and out rather than starting with a corner.</summary>
        private static float Smooth(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            return t * t * (3f - (2f * t));
        }
    }
}
