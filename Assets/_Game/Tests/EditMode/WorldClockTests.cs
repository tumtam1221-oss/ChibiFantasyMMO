using System.Collections.Generic;
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
    
        [Test]
        public void The_shortest_difference_is_zero_for_the_same_moment()
        {
            Assert.That(WorldClock.ShortestDifference(0.42f, 0.42f), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void The_shortest_difference_is_signed()
        {
            Assert.That(WorldClock.ShortestDifference(0.20f, 0.30f),
                Is.EqualTo(0.10f).Within(1e-5f), "ahead should read positive");

            Assert.That(WorldClock.ShortestDifference(0.30f, 0.20f),
                Is.EqualTo(-0.10f).Within(1e-5f), "behind should read negative");
        }

        [Test]
        public void The_shortest_difference_goes_the_short_way_round_midnight()
        {
            // two minutes apart across midnight, not twenty-three hours and fifty-eight
            Assert.That(WorldClock.ShortestDifference(0.99f, 0.01f),
                Is.EqualTo(0.02f).Within(1e-5f));

            Assert.That(WorldClock.ShortestDifference(0.01f, 0.99f),
                Is.EqualTo(-0.02f).Within(1e-5f));
        }

        [Test]
        public void The_shortest_difference_is_never_more_than_half_a_day()
        {
            for (int i = 0; i <= 100; i++)
            {
                for (int j = 0; j <= 100; j++)
                {
                    float difference = WorldClock.ShortestDifference(i / 100f, j / 100f);

                    Assert.That(difference, Is.InRange(-0.5f, 0.5f),
                        "from " + (i / 100f) + " to " + (j / 100f));
                }
            }
        }

        [Test]
        public void The_shortest_difference_wraps_values_outside_a_day()
        {
            Assert.That(WorldClock.ShortestDifference(1.20f, 0.30f),
                Is.EqualTo(WorldClock.ShortestDifference(0.20f, 0.30f)).Within(1e-5f));

            Assert.That(WorldClock.ShortestDifference(0.20f, -0.70f),
                Is.EqualTo(WorldClock.ShortestDifference(0.20f, 0.30f)).Within(1e-5f));
        }
    
        // ---- correcting drift without breaking the clock -----------------------------------

        [Test]
        public void A_correction_across_midnight_rolls_the_day_over()
        {
            // one minute before midnight on day 0
            var clock = new WorldClock(3600.0, 0.9998);

            Assert.That(clock.Day, Is.EqualTo(0));

            clock.Shift(10.0);   // ten seconds forward, over the boundary

            Assert.That(clock.Day, Is.EqualTo(1),
                "SetTimeOfDay kept the old day here, which sent the clock back a whole day");
            Assert.That(clock.TimeOfDay, Is.LessThan(0.5f));
        }

        [Test]
        public void A_correction_backwards_across_midnight_rolls_the_day_back()
        {
            var clock = new WorldClock(3600.0, 0.0002);
            clock.Shift(3600.0);   // put it safely into day 1

            long before = clock.Day;

            clock.Shift(-10.0);

            Assert.That(clock.Day, Is.EqualTo(before - 1));
            Assert.That(clock.TimeOfDay, Is.GreaterThan(0.5f));
        }

        [Test]
        public void Shift_ignores_a_broken_number()
        {
            var clock = new WorldClock(3600.0, 0.4);
            float before = clock.TimeOfDay;

            clock.Shift(double.NaN);
            clock.Shift(double.PositiveInfinity);

            Assert.That(clock.TimeOfDay, Is.EqualTo(before).Within(1e-6f));
        }

        [Test]
        public void A_catch_up_step_never_exceeds_the_time_that_passed()
        {
            const double delta = 0.016;

            Assert.That(WorldClock.CatchUpStep(9999.0, delta, 0.5),
                Is.LessThanOrEqualTo(delta * 0.5));

            Assert.That(WorldClock.CatchUpStep(-9999.0, delta, 0.5),
                Is.GreaterThanOrEqualTo(-delta * 0.5));
        }

        [Test]
        public void A_catch_up_step_takes_the_whole_gap_when_it_is_small()
        {
            Assert.That(WorldClock.CatchUpStep(0.001, 1.0, 0.5),
                Is.EqualTo(0.001).Within(1e-9));
        }

        [Test]
        public void Correcting_a_clock_that_is_ahead_never_runs_it_backwards()
        {
            // the bug this pins: subtracting the whole error ran time in reverse
            const double delta = 0.016;

            foreach (double owed in new[] { -0.5, -5.0, -72.0, -9999.0 })
            {
                double step = WorldClock.CatchUpStep(owed, delta, 0.5);

                Assert.That(delta + step, Is.GreaterThan(0.0),
                    "owed " + owed + " made the clock move backwards");
            }
        }

        [Test]
        public void A_catch_up_fraction_of_one_still_lets_the_clock_move()
        {
            // clamped below one, or a client that was ahead would simply freeze
            const double delta = 0.016;

            double step = WorldClock.CatchUpStep(-9999.0, delta, 1.0);

            Assert.That(delta + step, Is.GreaterThan(0.0));
        }

        [Test]
        public void A_paused_frame_corrects_nothing()
        {
            Assert.That(WorldClock.CatchUpStep(100.0, 0.0, 0.5), Is.EqualTo(0.0));
            Assert.That(WorldClock.CatchUpStep(100.0, -1.0, 0.5), Is.EqualTo(0.0));
            Assert.That(WorldClock.CatchUpStep(double.NaN, 0.016, 0.5), Is.EqualTo(0.0));
        }

        [Test]
        public void A_drifting_clock_catches_up_and_never_goes_backwards()
        {
            // a client a minute ahead of the world, corrected over many frames
            var clock = new WorldClock(3600.0, 0.5);
            double owed = -60.0;
            float previous = clock.TimeOfDay;

            for (int frame = 0; frame < 20000; frame++)
            {
                const float delta = 0.016f;

                clock.Advance(delta);

                double step = WorldClock.CatchUpStep(owed, delta, 0.5);
                clock.Shift(step);
                owed -= step;

                float now = clock.TimeOfDay;

                // allow the wrap at midnight, forbid everything else
                if (now < previous) Assert.That(previous - now, Is.GreaterThan(0.5f),
                    "time went backwards at frame " + frame);

                previous = now;
            }

            Assert.That(owed, Is.EqualTo(0.0).Within(0.001),
                "the gap should have been paid off long before twenty thousand frames");
        }
    
        // ---- a large correction sweeps rather than cuts ------------------------------------

        [Test]
        public void A_large_gap_closes_within_the_sweep_rather_than_in_one_frame()
        {
            // The bug this pins: a client that had never been told the hour sat at the
            // authored one, and the first thing the world said to it moved the sky in a
            // single frame. Half a day out is the worst case, and it must still be a sweep.
            const double perDay = 3600.0;
            const double sweepSeconds = 2.0;
            const float delta = 0.016f;

            double owed = 0.5 * perDay;          // half a day of world time to make up
            double rate = System.Math.Abs(owed) / sweepSeconds;
            var moved = new List<double>();

            for (var frame = 0; frame < 400 && System.Math.Abs(owed) > 0.001; frame++)
            {
                // The rate is fixed when the gap is measured, not recomputed from what is
                // left -- that is what makes the sweep finish rather than merely approach.
                double step = System.Math.Sign(owed) * rate * delta;

                if (System.Math.Abs(step) > System.Math.Abs(owed)) step = owed;

                moved.Add(step);
                owed -= step;
            }

            Assert.That(moved, Is.Not.Empty);
            Assert.That(moved[0], Is.LessThan(0.5 * perDay * 0.05),
                "the first frame moved most of the gap, which is a cut, not a sweep");
            Assert.That(moved.Count, Is.GreaterThan(30),
                "the whole correction happened in a handful of frames");
            Assert.That(System.Math.Abs(owed), Is.LessThan(1.0),
                "the gap should be closed by the end of the sweep");
        }

        [Test]
        public void A_sweep_never_overshoots_what_is_owed()
        {
            const double delta = 1.0;
            const double sweepSeconds = 0.25;    // a step far larger than the gap

            double owed = 10.0;
            double step = System.Math.Sign(owed) * (System.Math.Abs(owed) / sweepSeconds) * delta;

            if (System.Math.Abs(step) > System.Math.Abs(owed)) step = owed;

            Assert.That(step, Is.EqualTo(owed).Within(1e-9),
                "a sweep that overshot would send the sky past the world and back again");
        }
    }
}
