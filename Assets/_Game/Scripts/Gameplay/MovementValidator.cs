using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a movement was refused.</summary>
    /// <remarks>
    /// Typed and specific, because these are not all the same event. A non-finite position
    /// is a broken client; an impossible delta is a speed hack; a wrong map is a client that
    /// has fallen behind a travel it did not notice. An operator watching for cheating needs
    /// to tell them apart, and a client that wants to resynchronise needs to know which one
    /// it was.
    /// </remarks>
    public enum MovementRejection
    {
        None = 0,

        /// <summary>No character, no map, or nothing to validate against.</summary>
        MissingContext = 1,

        /// <summary>The character is not somewhere movement makes sense.</summary>
        NotInWorld = 2,

        /// <summary>Dead characters do not walk.</summary>
        NotAlive = 3,

        /// <summary>
        /// The position contained NaN or an infinity.
        /// </summary>
        /// <remarks>Its own reason rather than folded into "too far", because a NaN
        /// compares false against every bound and would silently pass a distance check
        /// written the obvious way. This is checked first for exactly that reason.</remarks>
        NotFinite = 4,

        /// <summary>The client claims to be on a map it is not on.</summary>
        WrongMap = 5,

        /// <summary>Further than the elapsed time allows.</summary>
        TooFar = 6,

        /// <summary>Outside the authored bounds of the map.</summary>
        OutOfBounds = 7,

        /// <summary>The update is older than one already applied.</summary>
        OutOfOrder = 8,

        /// <summary>No time passed, so no movement can have happened.</summary>
        NoElapsedTime = 9
    }

    /// <summary>
    /// One client's claim about where it now is.
    /// </summary>
    /// <remarks>
    /// <b>A request, not a fact.</b> The name matters: a client sends where it believes it
    /// has moved to, and the server decides whether that is possible. Nothing in this type
    /// is applied anywhere until <see cref="MovementValidator"/> has agreed.
    ///
    /// <see cref="SequenceNumber"/> makes replay and reordering detectable. Without it a
    /// captured packet replayed later would be indistinguishable from a fresh one.
    /// </remarks>
    public readonly struct MovementRequest
    {
        public MovementRequest(CharacterId character, DefinitionId map, CombatPosition position,
            long sequenceNumber, long timestampMilliseconds)
        {
            Character = character;
            Map = map;
            Position = position;
            SequenceNumber = sequenceNumber;
            TimestampMilliseconds = timestampMilliseconds;
        }

        public CharacterId Character { get; }

        /// <summary>The map the client believes it is on. Compared, never believed.</summary>
        public DefinitionId Map { get; }

        public CombatPosition Position { get; }

        /// <summary>Monotonic per connection. An old one is refused rather than applied.</summary>
        public long SequenceNumber { get; }

        /// <summary>When the client says it moved. Bounded by the server's own clock.</summary>
        public long TimestampMilliseconds { get; }

        public override string ToString()
        {
            return Character + " -> " + Position + " on " + Map;
        }
    }

    /// <summary>What the server decided about a movement.</summary>
    public readonly struct MovementResult
    {
        private MovementResult(bool accepted, MovementRejection reason, CombatPosition position,
            float travelled)
        {
            IsAccepted = accepted;
            Reason = reason;
            Position = position;
            DistanceTravelled = travelled;
        }

        public bool IsAccepted { get; }

        public MovementRejection Reason { get; }

        /// <summary>
        /// Where the character actually is now.
        /// </summary>
        /// <remarks>On a refusal this is the position the character had before, not the one
        /// requested -- so a client that is corrected is told the truth rather than left to
        /// guess, and a rejection is always safe to apply.</remarks>
        public CombatPosition Position { get; }

        public float DistanceTravelled { get; }

        public static MovementResult Accepted(CombatPosition position, float travelled)
        {
            return new MovementResult(true, MovementRejection.None, position, travelled);
        }

        public static MovementResult Rejected(MovementRejection reason, CombatPosition authoritative)
        {
            return new MovementResult(false, reason, authoritative, 0f);
        }

        public override string ToString()
        {
            return IsAccepted ? "moved to " + Position : "refused: " + Reason;
        }
    }

    /// <summary>
    /// What a character is allowed to do, per map and per character.
    /// </summary>
    /// <remarks>
    /// <b>Data, not constants.</b> Every bound arrives here from authored content or from
    /// the character's own derived stats; nothing in the validator compares against a
    /// literal. A test authors two different budgets and watches the same validator reach
    /// different answers, which is the property that makes speed a balance decision rather
    /// than a code change.
    ///
    /// <see cref="ToleranceFactor"/> exists because a perfectly-tuned bound refuses honest
    /// players. Network jitter, frame timing and floating point all conspire to make a legal
    /// move look fractionally too long, and a server that disconnected everyone whose packet
    /// arrived late would be correct and unplayable.
    /// </remarks>
    public readonly struct MovementBudget
    {
        public MovementBudget(float metresPerSecond, float toleranceFactor = 1.25f,
            float maxRadius = 0f, long maxElapsedMilliseconds = 2000L)
        {
            MetresPerSecond = metresPerSecond < 0f ? 0f : metresPerSecond;
            ToleranceFactor = toleranceFactor < 1f ? 1f : toleranceFactor;
            MaxRadius = maxRadius < 0f ? 0f : maxRadius;
            MaxElapsedMilliseconds = maxElapsedMilliseconds < 1L ? 1L : maxElapsedMilliseconds;
        }

        /// <summary>Top speed, from the character's derived stats.</summary>
        public float MetresPerSecond { get; }

        /// <summary>Headroom for jitter. Never below one.</summary>
        public float ToleranceFactor { get; }

        /// <summary>
        /// How far from the origin the map extends. Zero means unbounded.
        /// </summary>
        /// <remarks>Zero rather than a sentinel, because an unauthored map genuinely has no
        /// bound and refusing every move on it would be worse than allowing them. Authoring
        /// a radius is what turns the check on.</remarks>
        public float MaxRadius { get; }

        /// <summary>
        /// The largest gap a single update may account for.
        /// </summary>
        /// <remarks>The check that stops the oldest trick in the book: claiming an enormous
        /// elapsed time so that an enormous distance becomes "legal". Time is capped before
        /// it is multiplied by speed.</remarks>
        public long MaxElapsedMilliseconds { get; }

        public bool IsUsable => MetresPerSecond > 0f;

        public static MovementBudget None => default;
    }

    /// <summary>
    /// Decides whether a claimed movement could have happened.
    /// </summary>
    /// <remarks>
    /// <b>The client asks; this answers.</b> Rule 11 of the phase brief says a client is
    /// never authoritative for position, and this is where that is enforced. A refused
    /// request leaves the character exactly where it was and reports that position back, so
    /// a client cannot move by being ignored.
    ///
    /// <b>Pure, and deliberately so.</b> No engine type, no clock, no transport. Time
    /// arrives as an argument. That is what lets every rule below -- including the ones that
    /// only matter under attack -- be exercised by an ordinary test rather than by trying to
    /// reproduce a hostile client.
    ///
    /// <b>Order of checks is deliberate.</b> Finiteness comes before distance, because NaN
    /// compares false against every bound and would slip through a distance check written
    /// the obvious way. Sequence comes before anything expensive, because a replayed packet
    /// should cost nothing. And elapsed time is clamped before it is multiplied by speed,
    /// because otherwise claiming a long gap buys an arbitrary distance.
    /// </remarks>
    public static class MovementValidator
    {
        /// <summary>
        /// Validates a claimed move and, if it holds, records it.
        /// </summary>
        /// <param name="request">What the client claims.</param>
        /// <param name="location">The character's authoritative location. Mutated only on success.</param>
        /// <param name="budget">Speed, tolerance and bounds, from stats and content.</param>
        /// <param name="lastSequence">The highest sequence already applied.</param>
        /// <param name="lastTimestampMilliseconds">When that one was applied.</param>
        /// <param name="isAlive">Whether the character may move at all.</param>
        public static MovementResult Validate(in MovementRequest request,
            CharacterLocationState location, in MovementBudget budget, long lastSequence,
            long lastTimestampMilliseconds, bool isAlive = true)
        {
            if (location == null || !budget.IsUsable)
            {
                return MovementResult.Rejected(MovementRejection.MissingContext, default);
            }

            CombatPosition authoritative = location.Position;

            if (!location.HasArrived)
            {
                return MovementResult.Rejected(MovementRejection.NotInWorld, authoritative);
            }

            if (request.Character != location.CharacterId)
            {
                // A client moving somebody else's character. There is no path by which
                // this should arrive, which is exactly why it is checked.
                return MovementResult.Rejected(MovementRejection.MissingContext, authoritative);
            }

            if (!isAlive)
            {
                return MovementResult.Rejected(MovementRejection.NotAlive, authoritative);
            }

            // Before anything expensive: a replayed or reordered packet costs nothing.
            if (request.SequenceNumber <= lastSequence)
            {
                return MovementResult.Rejected(MovementRejection.OutOfOrder, authoritative);
            }

            // Before any comparison: NaN is false against every bound, so a distance check
            // written the obvious way would accept it.
            if (!request.Position.IsFinite)
            {
                return MovementResult.Rejected(MovementRejection.NotFinite, authoritative);
            }

            if (!request.Map.IsValid || !location.IsOn(request.Map))
            {
                return MovementResult.Rejected(MovementRejection.WrongMap, authoritative);
            }

            long elapsed = request.TimestampMilliseconds - lastTimestampMilliseconds;

            if (elapsed <= 0L)
            {
                // No time passed, so no distance can have been covered. A client that
                // sends two positions with one timestamp is trying to teleport.
                return MovementResult.Rejected(MovementRejection.NoElapsedTime, authoritative);
            }

            // Clamped before it is multiplied. Claiming a long gap must not buy distance.
            if (elapsed > budget.MaxElapsedMilliseconds) elapsed = budget.MaxElapsedMilliseconds;

            float allowed = budget.MetresPerSecond * (elapsed / 1000f) * budget.ToleranceFactor;
            float squaredDistance = authoritative.SqrDistanceTo(request.Position);

            // Compared squared, so no square root runs on the hot path.
            if (squaredDistance > allowed * allowed)
            {
                return MovementResult.Rejected(MovementRejection.TooFar, authoritative);
            }

            if (budget.MaxRadius > 0f && !WithinRadius(request.Position, budget.MaxRadius))
            {
                return MovementResult.Rejected(MovementRejection.OutOfBounds, authoritative);
            }

            // Everything passed. This is the only line in the file that changes anything,
            // and it is unreachable from any refusal above.
            location.Position = request.Position;

            return MovementResult.Accepted(request.Position, Sqrt(squaredDistance));
        }

        /// <summary>
        /// Whether a position is inside a map's authored radius.
        /// </summary>
        /// <remarks>Horizontal only. Height is not bounded because a map's floor and
        /// ceiling are geometry rather than a number anybody authors, and a vertical bound
        /// invented here would refuse a legitimate jump or a staircase.</remarks>
        private static bool WithinRadius(in CombatPosition position, float radius)
        {
            float x = position.X;
            float z = position.Z;

            return x * x + z * z <= radius * radius;
        }

        /// <summary>Square root, without pulling in the engine's maths.</summary>
        /// <remarks>This assembly is engine-free; <c>System.Math</c> is not the engine.</remarks>
        private static float Sqrt(float value)
        {
            return value <= 0f ? 0f : (float)System.Math.Sqrt(value);
        }

        /// <summary>
        /// The budget a character is entitled to.
        /// </summary>
        /// <remarks>
        /// Derived from the character's own stats through the existing
        /// <see cref="DerivedStatsResult"/> rather than from a table of literals, so a speed
        /// buff is a stat change and nothing here needs editing. The map supplies the
        /// bounds; the character supplies the speed.
        /// </remarks>
        public static MovementBudget BudgetFor(float metresPerSecond, MapDefinition map,
            float toleranceFactor = 1.25f, long maxElapsedMilliseconds = 2000L)
        {
            float radius = map == null ? 0f : map.MovementRadius;

            return new MovementBudget(metresPerSecond, toleranceFactor, radius,
                maxElapsedMilliseconds);
        }
    }
}
