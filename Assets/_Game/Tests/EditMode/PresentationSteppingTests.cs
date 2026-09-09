using System.Collections.Generic;
using ChibiFantasy.Client;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// That the character is drawn moving continuously between authoritative positions.
    /// </summary>
    /// <remarks>
    /// <b>The defect these exist for shipped, and was found by eye rather than here.</b> The
    /// visible transform used to ease towards the replicated position with an exponential
    /// lerp. The arithmetic was fine; the assumption underneath it was not. A
    /// <c>SyncVar</c> is not a continuous signal -- it holds still for six frames at sixty
    /// and then jumps -- so chasing it surged on the frame a packet landed and crawled just
    /// before the next, which a player sees as walking in steps.
    ///
    /// <b>So these measure movement, not configuration.</b> Asserting that an interpolator
    /// is installed would have passed against the broken version too. What is asserted is
    /// the property that was actually wrong: how far the character moves each render frame
    /// while the server reports positions at a fixed cadence.
    ///
    /// <b>No socket, and the real code.</b> The component's per-frame step is driven
    /// directly, and that is the same method <c>Follow</c> calls with the replicated values
    /// passed in -- there is no second implementation for these to agree with while the game
    /// disagrees.
    /// </remarks>
    [TestFixture]
    internal sealed class PresentationSteppingTests
    {
        private const float SnapshotSeconds = 0.1f;   // FishNet's default SyncVar send rate
        private const float FrameSeconds = 1f / 60f;
        private const float Speed = 1.2f;

        private GameObject _object;
        private CharacterMovementInput _movement;

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("presentation-subject");
            _movement = _object.AddComponent<CharacterMovementInput>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_object != null) Object.DestroyImmediate(_object);
        }

        /// <summary>Walks the subject, and reports what each render frame actually moved.</summary>
        private float[] StepsWhileWalking(int frames, float frameSeconds = FrameSeconds,
            float snapshotSeconds = SnapshotSeconds)
        {
            var authoritative = Vector3.zero;
            var server = Vector3.zero;
            var sinceSnapshot = 0f;
            var steps = new System.Collections.Generic.List<float>();

            // Settle first: the opening frame legitimately places the character, and the
            // cadence has to be observed before it can be played back.
            for (var i = 0; i < 600; i++)
            {
                server += new Vector3(0f, 0f, Speed * frameSeconds);
                sinceSnapshot += frameSeconds;

                if (sinceSnapshot >= snapshotSeconds)
                {
                    sinceSnapshot -= snapshotSeconds;
                    authoritative = server;
                }

                _movement.AdvancePresentation(authoritative, frameSeconds);
            }

            for (var i = 0; i < frames; i++)
            {
                server += new Vector3(0f, 0f, Speed * frameSeconds);
                sinceSnapshot += frameSeconds;

                if (sinceSnapshot >= snapshotSeconds)
                {
                    sinceSnapshot -= snapshotSeconds;
                    authoritative = server;
                }

                float before = _object.transform.position.z;

                _movement.AdvancePresentation(authoritative, frameSeconds);

                steps.Add(_object.transform.position.z - before);
            }

            return steps.ToArray();
        }

        private static void Range(float[] steps, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;

            foreach (float step in steps)
            {
                min = Mathf.Min(min, step);
                max = Mathf.Max(max, step);
            }
        }

        [Test]
        public void AConstantWalkIsDrawnAsAConstantWalk()
        {
            Range(StepsWhileWalking(300), out float min, out float max);

            // The exponential chase this replaced measured 2.72 here, cycling at exactly the
            // snapshot rate. That ratio IS the visible stepping.
            Assert.That(max / min, Is.LessThan(1.35f),
                "render frames move by between " + min.ToString("F4") + "m and "
                + max.ToString("F4") + "m -- a character advancing in pulses rather than "
                + "continuously");
        }

        [Test]
        public void NoRenderFrameStallsWhileTheCharacterIsWalking()
        {
            float ideal = Speed * FrameSeconds;

            foreach (float step in StepsWhileWalking(300))
            {
                Assert.That(step, Is.GreaterThan(ideal * 0.4f),
                    "a frame moved " + step.ToString("F4") + "m against an expected "
                    + ideal.ToString("F4") + "m -- a stalled frame is the pause in "
                    + "move-pause-move");
            }
        }

        [Test]
        public void ItStaysContinuousAtAHighFrameRateToo()
        {
            // The old chase got worse with frame rate rather than better: 3.27 at 144.
            Range(StepsWhileWalking(600, 1f / 144f), out float min, out float max);

            Assert.That(max / min, Is.LessThan(1.35f),
                "a faster machine must not show more stepping, and it measured "
                + (max / min).ToString("F2") + "x");
        }

        [Test]
        public void TheDrawnCharacterKeepsUpWithTheServerRatherThanFallingBehindForever()
        {
            var authoritative = Vector3.zero;
            var server = Vector3.zero;
            var sinceSnapshot = 0f;

            for (var i = 0; i < 1800; i++)
            {
                server += new Vector3(0f, 0f, Speed * FrameSeconds);
                sinceSnapshot += FrameSeconds;

                if (sinceSnapshot >= SnapshotSeconds)
                {
                    sinceSnapshot -= SnapshotSeconds;
                    authoritative = server;
                }

                _movement.AdvancePresentation(authoritative, FrameSeconds);
            }

            float behind = server.z - _object.transform.position.z;

            // Bounded by the cadence, rather than by how long anybody has been walking.
            Assert.That(behind, Is.LessThan(Speed * SnapshotSeconds * 3f),
                "the drawn character is " + behind.ToString("F3") + "m behind after 30s, "
                + "which is drift rather than interpolation lag");
            Assert.That(behind, Is.GreaterThan(0f),
                "it must never be drawn ahead of the server -- that is showing a player "
                + "somewhere they have not been");
        }

        [Test]
        public void StoppingSettlesExactlyOnTheAuthoritativePositionRatherThanNearIt()
        {
            StepsWhileWalking(60);

            var resting = new Vector3(0f, 0f, 12.5f);

            // The server stops sending changes, because the character stopped moving.
            for (var i = 0; i < 120; i++) _movement.AdvancePresentation(resting, FrameSeconds);

            Assert.That(_object.transform.position.z, Is.EqualTo(resting.z).Within(0.0001f),
                "a character that has stopped must come to rest on the authoritative "
                + "position, not hover short of it");
        }

        [Test]
        public void WalkingAgainAfterStandingStillDoesNotLurch()
        {
            StepsWhileWalking(60);

            var resting = new Vector3(0f, 0f, 12.5f);

            for (var i = 0; i < 300; i++) _movement.AdvancePresentation(resting, FrameSeconds);

            // Five seconds later, they set off again.
            Vector3 server = resting;
            Vector3 authoritative = resting;
            var sinceSnapshot = 0f;
            var max = 0f;

            for (var i = 0; i < 60; i++)
            {
                server += new Vector3(0f, 0f, Speed * FrameSeconds);
                sinceSnapshot += FrameSeconds;

                if (sinceSnapshot >= SnapshotSeconds)
                {
                    sinceSnapshot -= SnapshotSeconds;
                    authoritative = server;
                }

                float before = _object.transform.position.z;

                _movement.AdvancePresentation(authoritative, FrameSeconds);

                max = Mathf.Max(max, _object.transform.position.z - before);
            }

            Assert.That(max, Is.LessThan(Speed * FrameSeconds * 2.5f),
                "setting off after a pause jumped " + max.ToString("F4") + "m in one frame "
                + "-- a stale segment played out all at once");
        }

        /// <summary>
        /// Walks, then holds the last reported position. Returns where that was.
        /// </summary>
        /// <remarks>The character comes to rest where the server last put it, rather than at
        /// some invented coordinate -- teleporting it to a resting place would itself be a
        /// snapshot arriving after an unnaturally short gap, which is a different thing from
        /// standing still and would be measuring the test rather than the code.</remarks>
        private Vector3 WalkThenStand(int walkFrames, int standFrames)
        {
            var authoritative = Vector3.zero;
            var server = Vector3.zero;
            var sinceSnapshot = 0f;

            for (var i = 0; i < walkFrames; i++)
            {
                server += new Vector3(0f, 0f, Speed * FrameSeconds);
                sinceSnapshot += FrameSeconds;

                if (sinceSnapshot >= SnapshotSeconds)
                {
                    sinceSnapshot -= SnapshotSeconds;
                    authoritative = server;
                }

                _movement.AdvancePresentation(authoritative, FrameSeconds);
            }

            // The server stops reporting changes, because nothing is moving.
            for (var i = 0; i < standFrames; i++)
            {
                _movement.AdvancePresentation(authoritative, FrameSeconds);
            }

            return authoritative;
        }

        /// <summary>
        /// Setting off after standing still runs at once, rather than creeping up to pace.
        /// </summary>
        /// <remarks>
        /// <b>The defect this exists for.</b> The cadence between snapshots is measured
        /// rather than assumed, and a standing character is sent nothing -- so the wait that
        /// ends when it sets off again looked like an enormously slow send rate. Folding it
        /// into the estimate stretched the first segment over most of a second: the character
        /// left at about a sixth of its speed and then hunted up to the right pace over the
        /// next several packets, which reads as a slow, stuttering start.
        ///
        /// <b>Measured on the frames that move.</b> The few frames before the first snapshot
        /// arrives are genuinely stationary -- the server has not moved anybody yet -- so
        /// they are not part of the question.
        /// </remarks>
        [Test]
        public void SettingOffAfterStandingStillLeavesAtFullPace()
        {
            Vector3 resting = WalkThenStand(720, 180);

            Vector3 server = resting;
            Vector3 authoritative = resting;
            var sinceSnapshot = 0f;
            var moved = new List<float>();

            for (var i = 0; i < 60; i++)
            {
                server += new Vector3(0f, 0f, Speed * FrameSeconds);
                sinceSnapshot += FrameSeconds;

                if (sinceSnapshot >= SnapshotSeconds)
                {
                    sinceSnapshot -= SnapshotSeconds;
                    authoritative = server;
                }

                float before = _object.transform.position.z;

                _movement.AdvancePresentation(authoritative, FrameSeconds);

                float step = _object.transform.position.z - before;

                if (step > 0f) moved.Add(step);
            }

            Assert.That(moved, Is.Not.Empty, "the character never set off at all");

            float ideal = Speed * FrameSeconds;

            // The old behaviour left at about a sixth of the ideal and hunted upwards.
            Assert.That(moved[0], Is.GreaterThan(ideal * 0.6f),
                "the first frame that moves covers " + moved[0].ToString("F4")
                + "m against an expected " + ideal.ToString("F4")
                + "m, so setting off crawls before it reaches pace");

            float behind = server.z - _object.transform.position.z;

            Assert.That(behind, Is.LessThan(Speed * SnapshotSeconds * 3f),
                "a second after setting off the character is still " + behind.ToString("F3")
                + "m behind the server, which is the start never catching up");
        }

        /// <summary>Standing still does not corrupt the measured cadence.</summary>
        /// <remarks>Stated separately from the visible symptom because this is the value the
        /// symptom came from, and a wrong number here is easier to read than a slow start.</remarks>
        [Test]
        public void StandingStillDoesNotChangeWhatTheCadenceIsBelievedToBe()
        {
            Vector3 resting = WalkThenStand(720, 0);

            float measured = _movement.SnapshotInterval;

            Assert.That(measured, Is.EqualTo(SnapshotSeconds).Within(0.02f),
                "precondition: the cadence was not learned in the first place");

            // Five seconds of standing exactly where the server last put them.
            for (var i = 0; i < 300; i++)
            {
                _movement.AdvancePresentation(resting, FrameSeconds);
            }

            // The first snapshot after five seconds of standing.
            _movement.AdvancePresentation(resting + new Vector3(0f, 0f, 0.4f), FrameSeconds);

            Assert.That(_movement.SnapshotInterval, Is.EqualTo(measured).Within(0.02f),
                "five seconds of standing still was taken for a five second send rate: the "
                + "estimate moved to " + _movement.SnapshotInterval.ToString("F3") + "s");
        }

        [Test]
        public void ARespawnIsPlacedRatherThanWalkedTo()
        {
            StepsWhileWalking(60);

            var elsewhere = new Vector3(0f, 0f, 400f);

            _movement.AdvancePresentation(elsewhere, FrameSeconds);

            Assert.That(_object.transform.position, Is.EqualTo(elsewhere),
                "easing towards a respawn flies the character across the map through walls");
        }

        /// <summary>The cadence is observed rather than assumed.</summary>
        /// <remarks>A hard-coded tenth of a second would be a guess that goes quietly wrong
        /// the moment the send rate changes, and the symptom would be this same stepping.</remarks>
        [Test]
        public void TheSnapshotCadenceIsMeasuredFromWhatActuallyArrives()
        {
            StepsWhileWalking(300, FrameSeconds, 0.05f);

            Assert.That(_movement.SnapshotInterval, Is.EqualTo(0.05f).Within(0.02f),
                "the client decided the cadence instead of observing it: measured "
                + _movement.SnapshotInterval.ToString("F4"));
        }

        [Test]
        public void AHalfRateServerIsStillDrawnContinuously()
        {
            Range(StepsWhileWalking(300, FrameSeconds, 0.2f), out float min, out float max);

            Assert.That(max / min, Is.LessThan(1.35f),
                "a slower send rate must show as more lag, never as more stepping");
        }

        /// <summary>
        /// Walks the subject the way the live game actually does, and reports each frame.
        /// </summary>
        /// <remarks>
        /// <b>The difference from <see cref="StepsWhileWalking"/> matters.</b> That helper
        /// advances the server smoothly and samples it on the snapshot beat, so every
        /// snapshot is the same distance from the last -- which is the one case this class
        /// was never able to fail on, and the reason the stutter survived it.
        ///
        /// The real server does not work that way. It advances a character only inside
        /// <c>CharacterMovementAuthority.Submit</c>, which runs when an input packet lands,
        /// and FishNet replicates on a different clock at a different rate. So a snapshot
        /// carries one input step or two, and the distances between consecutive snapshots
        /// differ by up to three to one. The clocks are deliberately given a slight drift
        /// here, because in the live game they are two different clocks and no rate-matching
        /// keeps them in phase.
        /// </remarks>
        private float[] StepsWithARealisticCadence(int frames, float frameSeconds,
            float sendSeconds, float snapshotSeconds)
        {
            var authoritative = Vector3.zero;
            var serverZ = 0f;
            var now = 0f;
            var sinceSend = 0f;
            var sinceSnapshot = 0f;
            var lastStepAt = 0f;
            const float Drift = 1.0007f;
            var steps = new List<float>();

            for (var i = 0; i < frames + 600; i++)
            {
                now += frameSeconds;
                sinceSend += frameSeconds;
                sinceSnapshot += frameSeconds * Drift;

                // the server moves on the packet, by its own clock, exactly as it does live
                while (sinceSend >= sendSeconds)
                {
                    sinceSend -= sendSeconds;
                    serverZ += Speed * (now - lastStepAt);
                    lastStepAt = now;
                }

                if (sinceSnapshot >= snapshotSeconds)
                {
                    sinceSnapshot -= snapshotSeconds;
                    authoritative = new Vector3(0f, 0f, serverZ);
                }

                float before = _object.transform.position.z;

                _movement.AdvancePresentation(authoritative, frameSeconds);

                if (i >= 600) steps.Add(_object.transform.position.z - before);
            }

            return steps.ToArray();
        }

        /// <summary>
        /// Uneven snapshots are drawn as an even walk.
        /// </summary>
        /// <remarks>
        /// <b>The defect this pins.</b> The playback span used to be the measured snapshot
        /// interval, which assumes consecutive snapshots are the same distance apart. On the
        /// shipping configuration -- input at 50 Hz, replication at 30 Hz -- they differ by
        /// three to one, so the character was drawn at anything from 0.9 to 1.9 m/s while the
        /// authored speed was 1.38. The walk cycle plays at a fixed rate, so the feet slipped
        /// and caught against ground that surged and stalled: the reported running stutter.
        /// The span is now the segment's own length divided by the measured speed, which is
        /// just the time that segment represents.
        ///
        /// The bound is on the spread of per-frame movement. It was measured at about 20%
        /// before the fix and about 3% after, so 8% fails the old behaviour comfortably while
        /// leaving room for the frame that lands exactly on a segment boundary.
        /// </remarks>
        [Test]
        public void AnUnevenSnapshotCadenceIsStillDrawnAtAnEvenPace()
        {
            foreach (float frameSeconds in new[] { 1f / 144f, 1f / 60f })
            {
                SetUp();

                float[] steps = StepsWithARealisticCadence(2000, frameSeconds,
                    1f / 50f, 1f / 30f);

                var total = 0f;

                foreach (float step in steps) total += step;

                float mean = total / steps.Length;
                var variance = 0f;

                foreach (float step in steps) variance += (step - mean) * (step - mean);

                float deviation = Mathf.Sqrt(variance / steps.Length);

                Assert.That(mean / frameSeconds, Is.EqualTo(Speed).Within(0.1f),
                    "the character is no longer being drawn at the pace the server moves it");

                Assert.That(deviation / mean, Is.LessThan(0.08f),
                    "at " + (1f / frameSeconds).ToString("F0") + " fps the drawn pace varied by "
                    + (100f * deviation / mean).ToString("F0")
                    + "% -- uneven snapshots are reaching the picture as an uneven walk, "
                    + "which is what makes the feet slip while the run cycle plays on");

                TearDown();
            }
        }

        /// <summary>The speed is observed, never assumed.</summary>
        [Test]
        public void ThePaceIsMeasuredFromWhatTheServerActuallyDoes()
        {
            StepsWithARealisticCadence(2000, FrameSeconds, 1f / 50f, 1f / 30f);

            Assert.That(_movement.AuthoritativeSpeed, Is.EqualTo(Speed).Within(0.15f),
                "the client decided the pace instead of observing it: measured "
                + _movement.AuthoritativeSpeed.ToString("F3"));
        }
    }
}
