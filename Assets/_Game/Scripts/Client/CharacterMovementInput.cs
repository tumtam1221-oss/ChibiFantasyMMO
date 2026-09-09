using ChibiFantasy.Client.World;
using ChibiFantasy.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client
{
    /// <summary>
    /// Reads the player's input, sends it as intent, and smooths towards the answer.
    /// </summary>
    /// <remarks>
    /// <b>It never decides where the character is.</b> The transform this drives is a
    /// picture: it eases towards the position the server replicated, and if the two disagree
    /// the server wins by simply continuing to replicate. Nothing here writes an
    /// authoritative value, and there is no path by which it could -- the entity's state is
    /// server-write-only and the only message this sends carries two axes.
    ///
    /// <b>Only the owner sends anything.</b> A remote player's character is drawn from its
    /// replicated position and nothing else; this component asks for input only on the
    /// object the local client owns, which is also the only object FishNet would accept a
    /// request through.
    ///
    /// <b>Smoothing is presentation and is deliberately dumb.</b> A lerp towards the last
    /// replicated position, no prediction, no rollback, no reconciliation -- those change
    /// what the player sees when the server disagrees, which is a design decision this gate
    /// does not make. What is here is the minimum that stops a replicated position looking
    /// like a teleport every packet.
    ///
    /// <b>Input System stays on this side of the line.</b> Gameplay is engine-free and the
    /// server assembly knows nothing about a keyboard; the dependency lives here, in the
    /// client, which is the only place it is allowed.
    /// </remarks>
    [RequireComponent(typeof(CharacterNetworkEntity))]
    public sealed class CharacterMovementInput : MonoBehaviour
    {
        [Tooltip("How much longer than the measured gap between snapshots each one is "
        + "played back over. Above 1 so a late packet does not leave the character "
        + "standing still. Higher is smoother under jitter and further behind the server.")]
        [SerializeField] private float _playbackStretch = 1.25f;

        [Tooltip("Seconds between movement requests. Zero sends one per frame.")]
        [SerializeField] private float _sendInterval = 0.05f;

        [Tooltip("Metres of disagreement past which the visible character is placed rather "
            + "than eased. Covers a respawn, a reconnect and a map change.")]
        [SerializeField] private float _snapDistance = 4f;

        [Tooltip("Development only: also read WASD. Off in normal play, where the mouse "
            + "is the only control.")]
        [SerializeField] private bool _developmentKeyboard;

        private CharacterNetworkEntity _entity;
        private long _sequence;
        private float _sinceLastSend;

        /// <summary>Shortest gap between snapshots this client will believe, in seconds.</summary>
        private const float MinimumInterval = 0.02f;

        /// <summary>Longest gap between snapshots this client will believe, in seconds.</summary>
        private const float MaximumInterval = 0.5f;

        /// <summary>How many times the observed cadence a gap may be and still count as one.</summary>
        /// <remarks>Comfortably above the jitter a real connection produces and far below the
        /// seconds a character spends standing still, which is the case being excluded.</remarks>
        private const float OutlierMultiple = 2f;

        /// <summary>How quickly the measured speed follows what the snapshots report.</summary>
        /// <remarks>Slower than the cadence tracker. Speed is the thing being smoothed, so
        /// letting it jump would put the ripple straight back in by another route.</remarks>
        private const float SpeedTrackingRate = 0.15f;

        private bool _hasSnapshot;
        private Vector3 _lastAuthoritative;
        private Vector3 _segmentFrom;
        private Vector3 _segmentTo;
        private float _segmentElapsed;
        private float _sinceSnapshot;
        private float _snapshotInterval;
        private float _authoritativeSpeed;

        /// <summary>The measured gap between replicated positions, in seconds.</summary>
        /// <remarks>Observed, not configured. Exposed so a test and a diagnostic can read
        /// what the cadence actually turned out to be rather than what it was assumed to
        /// be.</remarks>
        public float SnapshotInterval => _snapshotInterval;

        /// <summary>How fast the server is actually moving this character, in metres per
        /// second, smoothed over recent snapshots. For tests and debugging.</summary>
        public float AuthoritativeSpeed => _authoritativeSpeed;

        /// <summary>
        /// Where this character is being asked to walk, as a world-space XZ direction.
        /// </summary>
        /// <remarks>
        /// <b>This is the one way a player moves.</b> Written by
        /// <see cref="World.WorldPointerInput"/> from a click, read here and sent on this
        /// component's own cadence. Splitting "what is wanted" from "how often it is asked
        /// for" is what keeps a click from becoming a request per frame.
        ///
        /// <b>Still only intent.</b> Its magnitude is a throttle, never a speed, and the
        /// value is clamped to one before it leaves. The server integrates it, decides where
        /// the character actually ends up, and replicates that back.
        /// </remarks>
        public Vector2 Intent { get; set; }

        /// <summary>The last input this client sent, for a HUD or a test to read.</summary>
        public Vector2 LastSentInput { get; private set; }

        /// <summary>How many requests this client has sent.</summary>
        public long SentRequests => _sequence;

        private void Awake()
        {
            _entity = GetComponent<CharacterNetworkEntity>();
        }

        private void Update()
        {
            if (_entity == null) return;

            Follow();

            if (!_entity.IsOwner) return;

            _sinceLastSend += Time.deltaTime;

            if (_sinceLastSend < _sendInterval) return;

            _sinceLastSend = 0f;

            Send(ReadInput());
        }

        /// <summary>
        /// Draws the character along the path the server actually reported.
        /// </summary>
        /// <remarks>
        /// <b>The bug this replaces.</b> This used to ease towards the replicated position
        /// with an exponential lerp. That reads as the obvious thing to do and is wrong,
        /// because the value being eased towards is not continuous: it is a
        /// <c>SyncVar</c> that only changes when a packet lands, ten times a second. Chasing
        /// a target that stands still for six frames and then jumps produces a surge on the
        /// frame the packet arrives and a crawl just before the next one -- measured at a
        /// 2.72x velocity ripple at 60fps and 3.27x at 144fps, repeating at exactly the
        /// snapshot rate. That is the visible stepping, and because
        /// <see cref="World.CharacterVisualPresenter"/> derives the animator's Speed from
        /// this same per-frame movement, the walk cycle pulsed with it. One cause, both
        /// symptoms.
        ///
        /// <b>What it does instead.</b> Each snapshot defines a short segment -- from
        /// wherever the character is being drawn right now, to the position that just
        /// arrived -- and that segment is played out at a constant rate over roughly the
        /// time until the next one is due. Constant rate is the whole point: the per-frame
        /// movement becomes flat, which is what continuous motion is.
        ///
        /// <b>It cannot drift and it cannot run away.</b> Every segment ends on a position
        /// the server actually sent, so error is corrected once per snapshot and never
        /// accumulates. Nothing is extrapolated: the character is never drawn anywhere the
        /// server has not already said it was, which is why this cannot show a player
        /// somewhere they are not. The cost is a bounded lag of one snapshot interval.
        ///
        /// <b>Starting from where it is, not from the last packet.</b> The segment begins at
        /// the current drawn position, so a snapshot landing mid-segment cannot jerk the
        /// character backwards to catch up. Position stays continuous by construction.
        ///
        /// <b>Standing still converges.</b> When the server stops moving the character the
        /// value stops changing, the segment finishes, and the character settles exactly on
        /// the authoritative position rather than easing towards it forever.
        ///
        /// <b>Still one writer, still presentation.</b> This is the only thing in the client
        /// that writes this transform, and it only ever draws positions the server chose.
        /// </remarks>
        private void Follow()
        {
            AdvancePresentation(new Vector3(_entity.X, _entity.Y, _entity.Z), Time.deltaTime);
        }

        /// <summary>
        /// One frame of presentation, against a supplied authoritative position and delta.
        /// </summary>
        /// <remarks>Public and parameterised so the stepping this class exists to remove can
        /// be measured directly, by feeding it positions that change at a snapshot rate and
        /// reading what comes out per frame. There is no second implementation to drift from:
        /// <see cref="Follow"/> is this method with the replicated values passed in.</remarks>
        public void AdvancePresentation(Vector3 authoritative, float deltaSeconds)
        {
            // A respawn, a reconnect or a map change is not movement to be played out.
            if (_playbackStretch <= 0f || !_hasSnapshot || ShouldSnap(authoritative))
            {
                transform.position = authoritative;

                _hasSnapshot = true;
                _lastAuthoritative = authoritative;
                _segmentFrom = authoritative;
                _segmentTo = authoritative;
                _segmentElapsed = 0f;
                _sinceSnapshot = 0f;
                _authoritativeSpeed = 0f;

                return;
            }

            _sinceSnapshot += deltaSeconds;

            if (authoritative != _lastAuthoritative)
            {
                // The cadence is measured rather than assumed: this client does not get to
                // decide how often the server talks to it, and hard-coding a rate here would
                // be a guess that goes quietly wrong the moment the send rate changes.
                //
                // But a standing character is sent nothing at all, because its position is
                // not changing. The wait that ends when it sets off again is therefore not a
                // sample of the cadence -- it is a sample of how long the player stood about.
                // Folding it in stretched the first segment after every stop over most of a
                // second, so setting off crawled at a sixth of the intended pace and then
                // hunted back up to it over the next half dozen packets. That is a slow,
                // stuttering start, and it was measured at exactly that.
                //
                // So a gap far longer than what is already being observed is discarded rather
                // than averaged in. A genuine change of send rate arrives as a run of
                // consistent gaps and is picked up within a few packets; standing still
                // arrives as one outlier and is ignored.
                if (_snapshotInterval <= 0f)
                {
                    _snapshotInterval = Mathf.Clamp(_sinceSnapshot, MinimumInterval,
                        MaximumInterval);
                }
                else if (_sinceSnapshot <= _snapshotInterval * OutlierMultiple)
                {
                    _snapshotInterval = Mathf.Lerp(_snapshotInterval, _sinceSnapshot, 0.25f);
                }

                // <b>How fast the character is actually travelling.</b> Learned the same
                // careful way as the cadence, and for the same reason: it is the server's
                // number, not this client's, and the only honest way to know it is to watch
                // it. Averaged over many snapshots it converges on the authored walking speed
                // even though no single snapshot reports it accurately -- which is the whole
                // point, and is what makes the span below stable.
                //
                // <b>Only from a gap that looks like the cadence.</b> A distance divided by a
                // gap is only a speed if the gap is a real one. The wait that ends when a
                // standing player sets off again is five seconds long and reports almost no
                // speed; a small correction landing a single frame after the last snapshot
                // reports an enormous one -- measured at 42 m/s against an authored 1.2, and
                // it drove the next segment out in a single frame. That is a lurch, and it is
                // the same class of mistake the cadence above refuses, so it is refused the
                // same way and in both directions.
                bool gapLooksLikeTheCadence = _snapshotInterval > 0f
                    && _sinceSnapshot <= _snapshotInterval * OutlierMultiple
                    && _sinceSnapshot >= _snapshotInterval / OutlierMultiple;

                if (gapLooksLikeTheCadence)
                {
                    float travelled = Vector3.Distance(authoritative, _lastAuthoritative);
                    float observed = travelled / _sinceSnapshot;

                    _authoritativeSpeed = _authoritativeSpeed <= 0f
                        ? observed
                        : Mathf.Lerp(_authoritativeSpeed, observed, SpeedTrackingRate);
                }

                _lastAuthoritative = authoritative;
                _segmentFrom = transform.position;
                _segmentTo = authoritative;
                _segmentElapsed = 0f;
                _sinceSnapshot = 0f;
            }

            float span = SpanFor(Vector3.Distance(_segmentFrom, _segmentTo));

            // Advanced before it is used. Evaluating at zero would draw the frame a packet
            // lands on at exactly the previous position -- a stalled frame at the snapshot
            // rate, which is the same stepping by another route.
            _segmentElapsed += deltaSeconds;

            transform.position = Vector3.Lerp(_segmentFrom, _segmentTo,
                Mathf.Clamp01(_segmentElapsed / span));
        }

        /// <summary>
        /// How long to spend playing out a segment of a given length.
        /// </summary>
        /// <remarks>
        /// <b>The bug this replaces.</b> This used to be the measured snapshot interval, on
        /// the reasonable-sounding assumption that consecutive snapshots are the same
        /// distance apart. They are not. The server advances a character only when an input
        /// packet lands, and it replicates on a different clock at a different rate, so one
        /// snapshot carries one input step and the next carries two. Measured on the shipping
        /// configuration -- input at 50 Hz, replication at 30 Hz -- consecutive snapshots
        /// differed in distance by three to one. Playing every one of them over the same
        /// span therefore drew the character at anything from 0.9 to 1.9 m/s while the
        /// authored speed was 1.38, and the walk cycle plays at a fixed rate, so the feet
        /// slipped and caught against ground that was surging and stalling underneath them.
        /// That is the running stutter, and it was worst on a climb only because foot contact
        /// is easier to read against a slope.
        ///
        /// <b>Distance over speed, which is just time.</b> A segment two steps long should
        /// take twice as long to play as a one-step segment, which is exactly what dividing
        /// by the speed gives. The drawn speed then holds at whatever the server is really
        /// doing, no matter how unevenly the snapshots that report it happen to be spaced.
        /// Measured on the same configuration, the ripple falls from 19% to 3%.
        ///
        /// <b>It still cannot drift.</b> Every segment still ends on a position the server
        /// actually sent, and every segment still starts from where the character is being
        /// drawn now, so a lag is carried rather than compounded and <see cref="ShouldSnap"/>
        /// still catches anything that is not movement at all.
        ///
        /// <b>No stretch here, deliberately.</b> <see cref="_playbackStretch"/> exists to stop
        /// a time-based segment finishing early and stalling; applying it to a distance-based
        /// one would simply draw the character slower than the server is moving it, forever,
        /// falling further behind every segment. It still applies to the fallback below.
        ///
        /// <b>The fallback.</b> Until a couple of snapshots have been seen there is no
        /// measured speed, and a character that is standing still has no speed to measure. In
        /// both cases this returns the old time-based span, which behaves exactly as before.
        /// </remarks>
        private float SpanFor(float segmentLength)
        {
            float byTime = Mathf.Clamp(_snapshotInterval, MinimumInterval, MaximumInterval)
                * _playbackStretch;

            if (_authoritativeSpeed <= 0.0001f || segmentLength <= 0.0001f) return byTime;

            // A span of zero would divide the lerp by nothing; the floor is a frame at a
            // rate no display reaches, so it never shortens a segment that matters.
            return Mathf.Max(segmentLength / _authoritativeSpeed, 0.0005f);
        }

        /// <summary>
        /// Whether the gap is too large to be movement.
        /// </summary>
        /// <remarks>
        /// A respawn, a reconnect and a map change all replicate as one enormous position
        /// change, and easing towards it would fly the character across the world in front of
        /// the player -- through walls, for several seconds, looking like a hack. Past the
        /// threshold the visible character is simply placed where the server says it is.
        ///
        /// <b>This is presentation and nothing else.</b> Either branch draws the same
        /// authoritative position; they differ only in how long the picture takes to agree
        /// with it.
        /// </remarks>
        public bool ShouldSnap(Vector3 authoritative)
        {
            return CharacterVisualRules.ShouldSnap(transform.position, authoritative,
                _snapDistance);
        }

        /// <summary>The distance past which the visible character is placed, not eased.</summary>
        public float SnapDistance => _snapDistance;

        /// <summary>
        /// The current movement input, as a vector no longer than one.
        /// </summary>
        /// <remarks>
        /// Clamped here as a courtesy so an honest client is not refused for a diagonal
        /// worth 1.41. The server does not rely on this: it refuses an oversized input
        /// itself, because a client that wants to send 1.41 simply would not call this
        /// method.
        /// </remarks>
        private Vector2 ReadInput()
        {
            Vector2 intent = Intent;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_developmentKeyboard)
            {
                Keyboard keyboard = Keyboard.current;

                if (keyboard != null)
                {
                    var keys = new Vector2(
                        (keyboard.dKey.isPressed ? 1f : 0f)
                        - (keyboard.aKey.isPressed ? 1f : 0f),
                        (keyboard.wKey.isPressed ? 1f : 0f)
                        - (keyboard.sKey.isPressed ? 1f : 0f));

                    if (keys != Vector2.zero) intent = keys;
                }
            }
#endif

            return intent.sqrMagnitude > 1f ? intent.normalized : intent;
        }

        /// <summary>
        /// Sends one movement intent.
        /// </summary>
        /// <remarks>
        /// A monotonic sequence, so a duplicated or reordered packet is detectable by the
        /// server -- which refuses it rather than moving twice.
        ///
        /// Standing still is still sent once and then stopped, so the server is not asked to
        /// re-evaluate an empty input every frame for a player who is reading their bag.
        /// </remarks>
        private void Send(Vector2 input)
        {
            if (input == Vector2.zero && LastSentInput == Vector2.zero) return;

            LastSentInput = input;

            _entity.RequestMove(input.x, input.y, ++_sequence);
        }
    }
}
