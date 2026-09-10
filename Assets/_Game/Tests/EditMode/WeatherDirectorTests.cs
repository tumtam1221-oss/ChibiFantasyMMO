using System;
using System.Collections.Generic;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The weather: it rolls between clear and rain on its own, it never rolls snow, and a
    /// festival can pin it until somebody says otherwise.
    /// </summary>
    /// <remarks>
    /// Every rule that matters here is about what the server may do without being asked,
    /// because that is what every player sees at once. The roll is seeded so these are
    /// statements about behaviour rather than about luck.
    /// </remarks>
    public sealed class WeatherDirectorTests
    {
        /// <summary>Runs the director for a long while and reports everything it became.</summary>
        private static List<WorldWeather> Observe(WeatherDirector director, float seconds)
        {
            var seen = new List<WorldWeather>();
            director.Changed += w => seen.Add(w);

            for (float t = 0f; t < seconds; t += 1f) director.Tick(1f);

            return seen;
        }

        [Test]
        public void It_starts_clear_unless_told_otherwise()
        {
            Assert.That(new WeatherDirector().Weather, Is.EqualTo(WorldWeather.Clear));
        }

        [Test]
        public void Rain_arrives_on_its_own()
        {
            var director = new WeatherDirector(new Random(1), rainChance: 1f,
                minimumSeconds: 10f, maximumSeconds: 10f);

            List<WorldWeather> seen = Observe(director, 30f);

            Assert.That(seen, Has.Some.EqualTo(WorldWeather.Rain));
        }

        [Test]
        public void Snow_never_arrives_on_its_own()
        {
            // a thousand rolls, every one of which could have been anything
            var director = new WeatherDirector(new Random(7), rainChance: 0.5f,
                minimumSeconds: 1f, maximumSeconds: 1f);

            List<WorldWeather> seen = Observe(director, 1000f);

            Assert.That(seen, Is.Not.Empty, "the test is worthless if nothing rolled at all");
            Assert.That(seen, Has.None.EqualTo(WorldWeather.Snow),
                "snow is a festival that is switched on, never weather that happens");
        }

        [Test]
        public void A_zero_rain_chance_keeps_the_sky_clear()
        {
            var director = new WeatherDirector(new Random(3), rainChance: 0f,
                minimumSeconds: 1f, maximumSeconds: 1f);

            List<WorldWeather> seen = Observe(director, 500f);

            Assert.That(seen, Is.Empty, "nothing should have changed at all");
            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Clear));
        }

        [Test]
        public void The_same_seed_produces_the_same_weather()
        {
            var a = new WeatherDirector(new Random(42), rainChance: 0.5f,
                minimumSeconds: 2f, maximumSeconds: 6f);
            var b = new WeatherDirector(new Random(42), rainChance: 0.5f,
                minimumSeconds: 2f, maximumSeconds: 6f);

            Assert.That(Observe(a, 300f), Is.EqualTo(Observe(b, 300f)),
                "a reproducible sky is what makes a weather bug reproducible");
        }

        [Test]
        public void Nothing_is_announced_when_nothing_changed()
        {
            // every roll lands on rain, so after the first there is nothing new to say
            var director = new WeatherDirector(new Random(5), rainChance: 1f,
                minimumSeconds: 1f, maximumSeconds: 1f);

            List<WorldWeather> seen = Observe(director, 100f);

            Assert.That(seen.Count, Is.EqualTo(1),
                "a broadcast saying 'it is still raining' would wake every client for nothing");
        }

        [Test]
        public void A_festival_pins_the_sky()
        {
            var director = new WeatherDirector(new Random(9), rainChance: 1f,
                minimumSeconds: 1f, maximumSeconds: 1f);

            director.Hold(WorldWeather.Snow);

            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Snow));
            Assert.That(director.IsHeld, Is.True);

            for (float t = 0f; t < 600f; t += 1f) director.Tick(1f);

            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Snow),
                "a held sky must not roll itself away mid-festival");
            Assert.That(director.SecondsRemaining, Is.EqualTo(0f),
                "a held sky is not counting down to anything");
        }

        [Test]
        public void Releasing_lets_the_weather_roll_again()
        {
            var director = new WeatherDirector(new Random(11), rainChance: 1f,
                minimumSeconds: 5f, maximumSeconds: 5f);

            director.Hold(WorldWeather.Snow);
            director.Release();

            Assert.That(director.IsHeld, Is.False);
            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Snow),
                "releasing should not blink the snow off on the spot");

            for (float t = 0f; t < 10f; t += 1f) director.Tick(1f);

            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Rain),
                "once released the sky should roll normally again");
        }

        [Test]
        public void Holding_announces_the_change_like_any_other()
        {
            var director = new WeatherDirector(new Random(13));
            var seen = new List<WorldWeather>();
            director.Changed += w => seen.Add(w);

            director.Hold(WorldWeather.Snow);

            Assert.That(seen, Is.EqualTo(new[] { WorldWeather.Snow }),
                "clients learn about a festival through the same channel as any weather");
        }

        [Test]
        public void A_paused_or_rewound_tick_does_not_move_the_weather()
        {
            var director = new WeatherDirector(new Random(17), rainChance: 1f,
                minimumSeconds: 5f, maximumSeconds: 5f);
            float before = director.SecondsRemaining;

            director.Tick(0f);
            director.Tick(-100f);
            director.Tick(float.NaN);
            director.Tick(float.PositiveInfinity);

            Assert.That(director.SecondsRemaining, Is.EqualTo(before).Within(0.0001f));
            Assert.That(director.Weather, Is.EqualTo(WorldWeather.Clear));
        }

        [Test]
        public void A_spell_lasts_somewhere_between_the_authored_bounds()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var director = new WeatherDirector(new Random(seed),
                    minimumSeconds: 30f, maximumSeconds: 90f);

                Assert.That(director.SecondsRemaining, Is.InRange(30f, 90f));
            }
        }
    }
}
