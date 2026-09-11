using System;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What the sky is doing.</summary>
    /// <remarks>
    /// <b>Snow is not in the rotation.</b> It is here as a state the world can be put into,
    /// not one it wanders into: the brief was a festival that is switched on, not weather that
    /// surprises people in a harbour town. <see cref="WeatherDirector"/> never picks it.
    /// </remarks>
    public enum WorldWeather
    {
        Clear = 0,
        Rain = 1,
        Snow = 2
    }

    /// <summary>
    /// Decides what the weather is doing and when it changes.
    /// </summary>
    /// <remarks>
    /// <b>Server-owned, like the clock.</b> Two players standing together must be equally
    /// wet. Unlike the time of day this cannot be handed out as a seed and re-derived, because
    /// a change happens at a moment nobody can compute in advance -- so the server says so
    /// when it happens, and <see cref="Changed"/> is the one place that fires.
    ///
    /// <b>Deterministic given a seed.</b> The roll uses an injected <see cref="Random"/> so a
    /// test can pin the sequence rather than wait for a coin to land the right way. Two
    /// servers with the same seed produce the same season, which also makes a bug reproducible
    /// instead of "it rained that one time".
    ///
    /// <b>Forcing is not a special case bolted on.</b> A festival, an operator or a quest sets
    /// the weather through the same door the roll uses, so nothing can be in a state the rest
    /// of the system does not know how to leave.
    /// </remarks>
    public sealed class WeatherDirector
    {
        /// <summary>The shortest spell of clear sky before the weather is rolled again.</summary>
        public const float DefaultClearMinimumSeconds = 1200f;

        /// <summary>The longest spell of clear sky before the next roll.</summary>
        public const float DefaultClearMaximumSeconds = 2700f;

        /// <summary>
        /// The shortest a shower runs. Fifteen minutes, and deliberately not three.
        /// </summary>
        /// <remarks>
        /// <b>Rain gets its own length because it is not the same event as clear sky.</b>
        /// Rolling one duration for both meant a shower ran for a clear-sky spell and then
        /// took its chances -- three minutes of rain that could stop as abruptly as it
        /// started, which reads as a bug in the weather rather than as weather. A day here is
        /// one real hour, so this is a wet afternoon rather than a passing cloud.
        /// </remarks>
        public const float DefaultRainMinimumSeconds = 900f;

        /// <summary>The longest a shower runs before the sky is rolled again.</summary>
        public const float DefaultRainMaximumSeconds = 1800f;

        /// <summary>
        /// How often a roll comes up rain rather than clear.
        /// </summary>
        /// <remarks>
        /// <b>This is a chance per roll, not a share of the day.</b> Because a shower lasts
        /// far longer than a clear spell, the share of time spent wet is much higher than this
        /// number: with the defaults above it works out around a fifth of the time. Raising
        /// the rain length without lowering this is what turns a harbour town into a swamp.
        /// </remarks>
        public const float DefaultRainChance = 0.25f;

        private readonly Random _random;
        private readonly float _clearMinimumSeconds;
        private readonly float _clearMaximumSeconds;
        private readonly float _rainMinimumSeconds;
        private readonly float _rainMaximumSeconds;
        private readonly float _rainChance;

        private WorldWeather _weather;
        private float _remaining;
        private bool _held;

        /// <param name="random">Injected so the sequence is reproducible in a test.</param>
        /// <param name="rainChance">0 disables rain entirely, which is what a desert map wants.</param>
        /// <param name="clearMinimumSeconds">How long dry weather lasts, at the shortest.</param>
        /// <param name="rainMinimumSeconds">How long a shower lasts, at the shortest.</param>
        public WeatherDirector(Random random = null,
            float rainChance = DefaultRainChance,
            float clearMinimumSeconds = DefaultClearMinimumSeconds,
            float clearMaximumSeconds = DefaultClearMaximumSeconds,
            float rainMinimumSeconds = DefaultRainMinimumSeconds,
            float rainMaximumSeconds = DefaultRainMaximumSeconds,
            WorldWeather starting = WorldWeather.Clear)
        {
            _random = random ?? new Random();
            _rainChance = Clamp01(rainChance);
            _clearMinimumSeconds = Math.Max(1f, clearMinimumSeconds);
            _clearMaximumSeconds = Math.Max(_clearMinimumSeconds, clearMaximumSeconds);
            _rainMinimumSeconds = Math.Max(1f, rainMinimumSeconds);
            _rainMaximumSeconds = Math.Max(_rainMinimumSeconds, rainMaximumSeconds);
            _weather = starting;
            _remaining = RollDuration(_weather);
        }

        /// <summary>What the sky is doing now.</summary>
        public WorldWeather Weather => _weather;

        /// <summary>Seconds until the next roll. Zero while the weather is being held.</summary>
        public float SecondsRemaining => _held ? 0f : _remaining;

        /// <summary>
        /// Whether the weather is pinned rather than rolling.
        /// </summary>
        /// <remarks>What a festival looks like from the outside: the snow does not stop
        /// because a timer ran out, it stops when somebody ends the festival.</remarks>
        public bool IsHeld => _held;

        /// <summary>Raised when, and only when, the weather actually becomes something else.</summary>
        /// <remarks>Not raised on a re-roll that lands on the same weather. A broadcast that
        /// said "it is still raining" would wake every client to tell them nothing.</remarks>
        public event Action<WorldWeather> Changed;

        /// <summary>
        /// Advances the weather clock.
        /// </summary>
        /// <remarks>A held sky does not count down: that is what held means.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (_held) return;
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;

            _remaining -= deltaSeconds;

            if (_remaining > 0f) return;

            // The weather is chosen before its length, because the length depends on which
            // weather it turned out to be. Rolling the duration first -- as this used to --
            // handed a shower whatever spell a clear sky would have got.
            WorldWeather next = _random.NextDouble() < _rainChance
                ? WorldWeather.Rain
                : WorldWeather.Clear;

            _remaining = RollDuration(next);

            Set(next);
        }

        /// <summary>
        /// Puts the world into a weather and keeps it there until released.
        /// </summary>
        /// <remarks>The festival door. Snow only ever arrives through here.</remarks>
        public void Hold(WorldWeather weather)
        {
            _held = true;

            Set(weather);
        }

        /// <summary>
        /// Lets the weather roll again, starting from a fresh spell of whatever it is now.
        /// </summary>
        /// <remarks>Deliberately does not clear the sky on the spot: a festival ending at
        /// midnight should let the snow run out naturally rather than blink off mid-flake.</remarks>
        public void Release()
        {
            _held = false;
            _remaining = RollDuration(_weather);
        }

        private void Set(WorldWeather weather)
        {
            if (weather == _weather) return;

            _weather = weather;

            Changed?.Invoke(weather);
        }

        /// <summary>
        /// How long this weather gets before the sky is rolled again.
        /// </summary>
        /// <remarks>Snow takes the clear spell rather than the rain one. It is normally held
        /// by a festival and never counting down at all; the only time this is asked about
        /// snow is the moment after a festival is released, and a released festival should
        /// hand the world back promptly rather than half an hour later.</remarks>
        private float RollDuration(WorldWeather weather)
        {
            float minimum = weather == WorldWeather.Rain
                ? _rainMinimumSeconds
                : _clearMinimumSeconds;

            float maximum = weather == WorldWeather.Rain
                ? _rainMaximumSeconds
                : _clearMaximumSeconds;

            return minimum + ((float)_random.NextDouble() * (maximum - minimum));
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;

            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
