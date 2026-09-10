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
        /// <summary>How long a spell of weather lasts, before the next roll.</summary>
        public const float DefaultMinimumSeconds = 180f;

        /// <summary>The longest a single spell runs before the sky is rolled again.</summary>
        public const float DefaultMaximumSeconds = 480f;

        /// <summary>How often a roll comes up rain rather than clear.</summary>
        public const float DefaultRainChance = 0.25f;

        private readonly Random _random;
        private readonly float _minimumSeconds;
        private readonly float _maximumSeconds;
        private readonly float _rainChance;

        private WorldWeather _weather;
        private float _remaining;
        private bool _held;

        /// <param name="random">Injected so the sequence is reproducible in a test.</param>
        /// <param name="rainChance">0 disables rain entirely, which is what a desert map wants.</param>
        public WeatherDirector(Random random = null,
            float rainChance = DefaultRainChance,
            float minimumSeconds = DefaultMinimumSeconds,
            float maximumSeconds = DefaultMaximumSeconds,
            WorldWeather starting = WorldWeather.Clear)
        {
            _random = random ?? new Random();
            _rainChance = Clamp01(rainChance);
            _minimumSeconds = Math.Max(1f, minimumSeconds);
            _maximumSeconds = Math.Max(_minimumSeconds, maximumSeconds);
            _weather = starting;
            _remaining = RollDuration();
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

            _remaining = RollDuration();

            Set(_random.NextDouble() < _rainChance ? WorldWeather.Rain : WorldWeather.Clear);
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
            _remaining = RollDuration();
        }

        private void Set(WorldWeather weather)
        {
            if (weather == _weather) return;

            _weather = weather;

            Changed?.Invoke(weather);
        }

        private float RollDuration()
        {
            return _minimumSeconds
                + ((float)_random.NextDouble() * (_maximumSeconds - _minimumSeconds));
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;

            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
