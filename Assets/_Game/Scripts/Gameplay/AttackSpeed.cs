namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What an attack-speed figure means, in seconds.
    /// </summary>
    /// <remarks>
    /// <b>The figure is content; this is only its unit.</b> ASPD is an ordinary derived
    /// stat -- a <c>StatDefinition</c> with a <c>DerivedStatFormulaDefinition</c> that the
    /// one calculator evaluates from AGI, from whatever equipment and status modifiers name
    /// it, and from the clamp on its own definition. Nothing here knows about agility or
    /// daggers. What this knows is the one thing content cannot express: that an ASPD of
    /// 100 is one swing a second, and 150 is one and a half.
    ///
    /// <b>Hundredths of a swing per second.</b> A rate rather than a delay, so that a
    /// larger number is faster (which is what a player reads "attack speed" to mean) and so
    /// that every source of it adds: +10 ASPD from a ring is +0.1 swings per second whether
    /// the wearer has 1 AGI or 99. The interval is then 100 / ASPD, and each point of AGI
    /// buys less time than the one before it -- the diminishing return the design asked for,
    /// without a curve anybody has to author.
    ///
    /// <b>Bounded here as well as in content.</b> The stat definition carries the clamp the
    /// calculator applies; these bounds are the same numbers held by code, so a mis-authored
    /// definition cannot produce a zero interval or a hundred swings a second on the wire.
    /// A test asserts the two agree.
    ///
    /// <b>Server and client compute the same thing.</b> The server turns the stat into an
    /// interval to pace the attack state machine; the client turns the same replicated stat
    /// into the same interval to pace its requests and its animation. One function, no
    /// second formula to drift.
    /// </remarks>
    public static class AttackSpeed
    {
        /// <summary>One swing a second: what a character with no attack-speed stat swings at.</summary>
        public const int Default = 100;

        /// <summary>The slowest a character can be made: one swing every two seconds.</summary>
        public const int Minimum = 50;

        /// <summary>The fastest: two swings a second. Above this the animation stops reading.</summary>
        public const int Maximum = 200;

        /// <summary>
        /// How much of the interval the swing itself occupies; the rest is recovery.
        /// </summary>
        /// <remarks>Only <see cref="AttackStateMachine"/> distinguishes the two phases, and it
        /// refuses a new swing in either, so this split changes no outcome. It is kept so the
        /// machine's phases stay meaningful for anything that later reads them.</remarks>
        public const float SwingFraction = 0.4f;

        /// <summary>Brings a figure inside the legal range. Zero or less means unset.</summary>
        public static int Clamp(int aspd)
        {
            if (aspd <= 0) aspd = Default;

            if (aspd < Minimum) return Minimum;

            return aspd > Maximum ? Maximum : aspd;
        }

        /// <summary>Seconds between two swings at this attack speed.</summary>
        public static float IntervalSeconds(int aspd)
        {
            return 100f / Clamp(aspd);
        }

        /// <summary>Swings per second, as a player would read it.</summary>
        public static float SwingsPerSecond(int aspd)
        {
            return Clamp(aspd) / 100f;
        }

        /// <summary>The state-machine timing for one swing at this attack speed.</summary>
        public static AttackTiming TimingFor(int aspd)
        {
            float interval = IntervalSeconds(aspd);

            return new AttackTiming(interval * SwingFraction, interval * (1f - SwingFraction));
        }

        /// <summary>
        /// How fast a clip of the given length has to play so one cycle fits the interval.
        /// </summary>
        /// <remarks>
        /// Never below one: a slow character finishes the swing at its natural pace and
        /// stands ready for the rest of the interval, rather than swinging in slow motion.
        /// Never above <paramref name="maxRate"/>: past that the contact pose is a blur, and
        /// the honest answer for a faster character is a shorter clip, not a faster one.
        /// </remarks>
        public static float PlaybackRate(float intervalSeconds, float clipSeconds, float maxRate)
        {
            if (clipSeconds <= 0f || intervalSeconds <= 0f) return 1f;

            float rate = clipSeconds / intervalSeconds;

            if (rate < 1f) return 1f;

            return rate > maxRate ? maxRate : rate;
        }
    }
}
