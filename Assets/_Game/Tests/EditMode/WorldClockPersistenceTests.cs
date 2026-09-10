using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Writing the world's calendar down, and reading it back.
    /// </summary>
    /// <remarks>
    /// Two properties are defended. The first is that a restart resumes rather than
    /// re-opening the day: the date has to survive, not just the hour. The second is that
    /// every way this can fail -- no key, an unreachable API, a nonsense row, a day length
    /// that has changed underneath -- leaves the world running rather than stopping it. A
    /// server that will not start because it could not read a clock is worse than one that
    /// opens at breakfast.
    /// </remarks>
    public sealed class WorldClockPersistenceTests
    {
        private static readonly ServerId Server = new ServerId("server.one");
        private static readonly ChannelId Channel = new ChannelId("channel.one");

        private const string Key = "a-key-supplied-by-the-deployment";

        /// <summary>
        /// The exact path the store asks for, query string and all.
        /// </summary>
        /// <remarks>The scripted transport matches on the whole path, so a test that queued
        /// the bare route would get "unreachable" back and quietly prove nothing. Spelled out
        /// once here so the tests below cannot drift from what the store actually sends.</remarks>
        private const string ReadPath =
            "/api/world/clock?server_id=server.one&channel_id=channel.one";

        // ---- the clock itself ---------------------------------------------------------

        [Test]
        public void The_whole_clock_survives_a_round_trip_including_the_day()
        {
            // three and a half days in, which is what a time-of-day-only save would lose
            var original = new WorldClock(3600.0, 0.0);
            original.Advance(3600f * 3f + 1800f);

            WorldClock restored = WorldClock.Restore(3600.0, original.ElapsedSeconds);

            Assert.That(restored.Day, Is.EqualTo(3), "the date was lost");
            Assert.That(restored.TimeOfDay, Is.EqualTo(original.TimeOfDay).Within(0.0001f));
        }

        [Test]
        public void Restoring_a_nonsense_total_opens_a_fresh_world_rather_than_throwing()
        {
            foreach (double broken in new[] { -1.0, double.NaN, double.PositiveInfinity })
            {
                WorldClock clock = null;

                Assert.DoesNotThrow(() => clock = WorldClock.Restore(3600.0, broken));
                Assert.That(clock.Day, Is.EqualTo(0));
            }
        }

        // ---- what counts as a usable saved clock ---------------------------------------

        [Test]
        public void A_saved_clock_is_only_usable_when_both_halves_are()
        {
            Assert.That(new WorldClockState(100.0, 3600.0).IsUsable, Is.True);

            Assert.That(new WorldClockState(-1.0, 3600.0).IsUsable, Is.False);
            Assert.That(new WorldClockState(100.0, 0.0).IsUsable, Is.False);
            Assert.That(new WorldClockState(double.NaN, 3600.0).IsUsable, Is.False);
            Assert.That(new WorldClockState(100.0, double.PositiveInfinity).IsUsable, Is.False);
        }

        // ---- the store -----------------------------------------------------------------

        [Test]
        public void Without_a_key_the_store_does_not_even_try_to_write()
        {
            var transport = new ScriptedHttpTransport();
            var store = new HttpWorldClockStore(transport, key: string.Empty);

            Assert.That(store.CanSave, Is.False);
            Assert.That(store.Save(Server, Channel, new WorldClockState(100.0, 3600.0)),
                Is.False);
            Assert.That(transport.Calls, Is.Empty,
                "a build with no key must not send a request that will only be refused");
        }

        [Test]
        public void The_key_is_presented_as_the_bearer_and_never_in_the_body()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("POST", "/api/world/clock", "{\"saved\":true}");

            var store = new HttpWorldClockStore(transport, Key);

            Assert.That(store.Save(Server, Channel, new WorldClockState(4321.5, 3600.0)),
                Is.True);
            Assert.That(transport.LastBearerToken, Is.EqualTo(Key));
            Assert.That(transport.LastBody, Does.Not.Contain(Key),
                "a credential in the body is a credential in every request log");
        }

        [Test]
        public void A_saved_clock_is_written_as_a_number_the_api_can_read_back()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("POST", "/api/world/clock", "{\"saved\":true}");

            new HttpWorldClockStore(transport, Key)
                .Save(Server, Channel, new WorldClockState(4321.5, 3600.0));

            // The trap this pins: on a comma-decimal machine the default formatting writes
            // 4321.5 as "4321,5", which lands in the JSON as two values.
            Assert.That(transport.LastBody, Does.Contain("4321.5"));
            Assert.That(transport.LastBody, Does.Not.Contain("4321,5"));
        }

        [Test]
        public void A_world_that_has_never_been_saved_reads_back_as_nothing()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("GET", ReadPath,
                "{\"saved\":false,\"elapsed_seconds\":0,\"seconds_per_day\":0}");

            Assert.That(new HttpWorldClockStore(transport, Key).Load(Server, Channel),
                Is.Null);
        }

        [Test]
        public void A_saved_world_reads_back_whole()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("GET", ReadPath,
                "{\"saved\":true,\"elapsed_seconds\":12600.5,\"seconds_per_day\":3600}");

            WorldClockState? state = new HttpWorldClockStore(transport, Key)
                .Load(Server, Channel);

            Assert.That(state, Is.Not.Null);
            Assert.That(state.Value.ElapsedSeconds, Is.EqualTo(12600.5).Within(0.001));
            Assert.That(state.Value.SecondsPerDay, Is.EqualTo(3600.0).Within(0.001));
        }

        [Test]
        public void A_stored_row_that_is_not_a_usable_clock_reads_back_as_nothing()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("GET", ReadPath,
                "{\"saved\":true,\"elapsed_seconds\":-5,\"seconds_per_day\":3600}");

            Assert.That(new HttpWorldClockStore(transport, Key).Load(Server, Channel),
                Is.Null, "a broken row must not be resumed from");
        }

        [Test]
        public void An_unreachable_api_is_a_world_with_no_saved_clock_not_a_failure()
        {
            var transport = new ScriptedHttpTransport();   // nothing queued: nothing answers

            var store = new HttpWorldClockStore(transport, Key);

            Assert.DoesNotThrow(() => store.Load(Server, Channel));
            Assert.That(store.Load(Server, Channel), Is.Null);
            Assert.DoesNotThrow(() =>
                store.Save(Server, Channel, new WorldClockState(100.0, 3600.0)));
        }

        [Test]
        public void An_unusable_clock_is_never_sent()
        {
            var transport = new ScriptedHttpTransport();

            new HttpWorldClockStore(transport, Key)
                .Save(Server, Channel, new WorldClockState(double.NaN, 3600.0));

            Assert.That(transport.Calls, Is.Empty);
        }

        [Test]
        public void The_read_carries_no_credential()
        {
            var transport = new ScriptedHttpTransport();
            transport.EnqueueOk("GET", ReadPath,
                "{\"saved\":true,\"elapsed_seconds\":10,\"seconds_per_day\":3600}");

            new HttpWorldClockStore(transport, Key).Load(Server, Channel);

            Assert.That(transport.LastBearerToken, Is.Null.Or.Empty,
                "reading what time it is needs no key, and sending one spreads it further");
        }
    }
}
