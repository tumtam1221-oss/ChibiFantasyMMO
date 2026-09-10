using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The world's clock: one real hour is one in-game day, it only ever runs forwards, and
    /// the phase and the night blend never disagree about when night is.
    /// </summary>
    /// <remarks>
    /// The clock is the one piece of the day/night feature that is pure arithmetic, so it is
    /// the piece worth pinning. Everything above it -- the sun angle, the grade, the skybox --
    /// is a rendering decision that reads these numbers; if these are right and stable, two
    /// players standing together cannot see different skies.
    /// </remarks>
    public sealed class WorldClockTests
    {
        [Test]
        public void A_day_is_one_real_hour_by_default()
        {
            var clock = new WorldClock();

            Assert.That(clock.SecondsPerDay, Is.EqualTo(3600.0).Within(0.001));
        }

        [Test]
        public void Half_a_day_of_ticks_lands_half_a_day_later()
        {
            var clock = new WorldClock(secondsPerDay: 3600.0, startTimeOfDay: 0.0);

            // thirty real minutes, delivered the way the server delivers them
            for (int i = 0; i < 1800; i++) clock.Advance(1f);

            Assert.That(clock.TimeOfDay, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Time_wraps_past_midnight_and_counts_the_day()
        {
            var clock = new WorldClock(secondsPerDay: 100.0, startTimeOfDay: 0.90);

            Assert.That(clock.Day, Is.EqualTo(0));

            clock.Advance(20f);   // ten seconds short of the day, then ten past it

            Assert.That(clock.TimeOfDay, Is.EqualTo(0.10f).Within(0.001f),
                "the fraction should wrap rather than run past one");
            Assert.That(clock.Day, Is.EqualTo(1), "crossing midnight should start a new day");
        }

        [Test]
        public void The_sky_never_runs_backwards()
        {
            var clock = new WorldClock(secondsPerDay: 100.0, startTimeOfDay: 0.5);
            float before = clock.TimeOfDay;

            clock.Advance(-10f);
            clock.Advance(0f);
            clock.Advance(float.NaN);
            clock.Advance(float.PositiveInfinity);

            Assert.That(clock.TimeOfDay, Is.EqualTo(before).Within(0.0001f),
                "a paused or rewound tick must not move the world's time");
        }

        [Test]
        public void A_day_shorter_than_a_minute_is_refused()
        {
            var clock = new WorldClock(secondsPerDay: 1.0);

            Assert.That(clock.SecondsPerDay, Is.EqualTo(WorldClock.MinimumSecondsPerDay),
                "a day so short that one tick skips a phase is not a day");
        }

        [Test]
        public void Setting_the_time_keeps_the_day_count()
        {
            var clock = new WorldClock(secondsPerDay: 100.0, startTimeOfDay: 0.0);
            clock.Advance(250f);                      // two and a half days in

            Assert.That(clock.Day, Is.EqualTo(2));

            clock.SetTimeOfDay(0.9);

            Assert.That(clock.TimeOfDay, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(clock.Day, Is.EqualTo(2), "jumping to dusk should not rewind the calendar");
        }

        [TestCase(0.00f, WorldTimePhase.Night)]
        [TestCase(0.15f, WorldTimePhase.Night)]
        [TestCase(0.25f, WorldTimePhase.Dawn)]
        [TestCase(0.50f, WorldTimePhase.Day)]
        [TestCase(0.74f, WorldTimePhase.Day)]
        [TestCase(0.80f, WorldTimePhase.Dusk)]
        [TestCase(0.95f, WorldTimePhase.Night)]
        public void The_phase_boundaries_are_where_they_are_authored(float time, WorldTimePhase expected)
        {
            Assert.That(WorldClock.PhaseAt(time), Is.EqualTo(expected));
        }

        [Test]
        public void A_time_outside_zero_to_one_wraps_rather_than_clamping()
        {
            Assert.That(WorldClock.PhaseAt(1.5f), Is.EqualTo(WorldClock.PhaseAt(0.5f)));
            Assert.That(WorldClock.PhaseAt(-0.5f), Is.EqualTo(WorldClock.PhaseAt(0.5f)));
        }

        [Test]
        public void Night_is_fully_on_at_midnight_and_fully_off_at_noon()
        {
            Assert.That(WorldClock.NightBlendAt(0.00f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(WorldClock.NightBlendAt(0.50f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void The_night_blend_never_jumps()
        {
            // a hard step anywhere would show in game as the sky snapping between frames
            float previous = WorldClock.NightBlendAt(0f);

            for (int i = 1; i <= 1000; i++)
            {
                float blend = WorldClock.NightBlendAt(i / 1000f);

                Assert.That(blend, Is.InRange(0f, 1f));
                Assert.That(System.Math.Abs(blend - previous), Is.LessThan(0.05f),
                    "the day/night blend stepped at t=" + (i / 1000f));

                previous = blend;
            }
        }

        [Test]
        public void The_blend_agrees_with_the_phase_it_belongs_to()
        {
            for (int i = 0; i < 1000; i++)
            {
                float t = i / 1000f;
                WorldTimePhase phase = WorldClock.PhaseAt(t);
                float blend = WorldClock.NightBlendAt(t);

                if (phase == WorldTimePhase.Night)
                {
                    Assert.That(blend, Is.EqualTo(1f).Within(0.001f), "night should be fully dark at t=" + t);
                }
                else if (phase == WorldTimePhase.Day)
                {
                    Assert.That(blend, Is.EqualTo(0f).Within(0.001f), "day should carry no night at t=" + t);
                }
            }
        }

        [Test]
        public void The_sun_sweeps_once_a_day()
        {
            Assert.That(WorldClock.SunDegreesAt(0.00f), Is.EqualTo(0f).Within(0.01f));
            Assert.That(WorldClock.SunDegreesAt(0.25f), Is.EqualTo(90f).Within(0.01f));
            Assert.That(WorldClock.SunDegreesAt(0.50f), Is.EqualTo(180f).Within(0.01f));
            Assert.That(WorldClock.SunDegreesAt(0.75f), Is.EqualTo(270f).Within(0.01f));
        }
    }
}
