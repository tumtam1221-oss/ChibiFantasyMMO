using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What a client is pressing, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>Intent, not a destination.</b> A client says "I am holding forward-left"; it does
    /// not say where it has arrived. That is the difference between this and
    /// <see cref="MovementRequest"/>, which carries a claimed position and exists for the
    /// model where a client moves itself and the server audits it. With intent there is
    /// nothing to audit: the server does the moving, so a client cannot claim a position it
    /// could not have reached because it never claims a position at all.
    ///
    /// Two axes and a sequence. No speed, no delta, no map, no character -- every one of
    /// those is the server's, and a field for any of them would be a field to forge.
    /// </remarks>
    public readonly struct CharacterMovementIntent
    {
        public CharacterMovementIntent(float x, float z, long sequence)
        {
            X = x;
            Z = z;
            Sequence = sequence;
        }

        /// <summary>Sideways input, nominally in -1..1.</summary>
        public float X { get; }

        /// <summary>Forward input, nominally in -1..1.</summary>
        public float Z { get; }

        /// <summary>Monotonic per connection, so a replayed packet is detectable.</summary>
        public long Sequence { get; }

        /// <summary>Whether the player is asking to move at all.</summary>
        public bool IsMoving => X != 0f || Z != 0f;

        public override string ToString()
        {
            return "input (" + X + ", " + Z + ") #" + Sequence;
        }
    }

    /// <summary>
    /// Turns a client's input into an authoritative step.
    /// </summary>
    /// <remarks>
    /// <b>It computes the destination, then submits it to the rules that already exist.</b>
    /// <see cref="MovementValidator"/> holds every movement rule this project has -- the
    /// sequence, the map, the finiteness, the distance budget, the authored radius, the
    /// clamped elapsed time -- and it is not duplicated here. The one thing this adds is
    /// where the destination comes from: the server multiplies input by its own speed and
    /// its own clock, and then asks the validator whether the result stands.
    ///
    /// That arrangement is deliberate. Reusing the validator by handing it a
    /// <i>server-computed</i> position means the distance check can never be the only thing
    /// standing between a player and a teleport, because there is no client position to
    /// begin with -- and it means both movement models are held to one set of rules rather
    /// than drifting apart.
    ///
    /// <b>Pure, and engine-free.</b> No transform, no clock, no input system. Time and
    /// speed arrive as arguments, which is what makes a hostile client reproducible in an
    /// ordinary test.
    /// </remarks>
    public static class CharacterMovementSimulator
    {
        /// <summary>
        /// How far past a unit vector an input may stray before it is refused.
        /// </summary>
        /// <remarks>
        /// An analogue stick and a normalised keyboard vector both land near one, and float
        /// arithmetic on the way through a serialiser can leave a value a hair above it.
        /// Refusing those would refuse honest players; accepting 1.4 would let a client
        /// walk diagonally at forty percent extra speed, which is the classic version of
        /// this cheat.
        /// </remarks>
        public const float MaximumInputMagnitude = 1.01f;

        /// <summary>
        /// Advances a character by one accepted step, or refuses to.
        /// </summary>
        /// <param name="intent">What the client is pressing.</param>
        /// <param name="location">The authoritative location. Mutated only on success.</param>
        /// <param name="budget">Speed, tolerance and map bounds. The server's, never the client's.</param>
        /// <param name="lastSequence">The highest sequence already applied.</param>
        /// <param name="lastTimestampMilliseconds">When that one was applied.</param>
        /// <param name="serverTimestampMilliseconds">
        /// Now, by the server's own clock. The elapsed time is the gap between this and the
        /// previous accepted move -- so a client that floods requests inside one tick finds
        /// that no time has passed and moves once, not a hundred times.
        /// </param>
        /// <param name="isAlive">Whether the character may move at all.</param>
        public static MovementResult Advance(in CharacterMovementIntent intent,
            CharacterLocationState location, in MovementBudget budget, long lastSequence,
            long lastTimestampMilliseconds, long serverTimestampMilliseconds,
            bool isAlive = true)
        {
            if (location == null || !budget.IsUsable)
            {
                return MovementResult.Rejected(MovementRejection.MissingContext, default);
            }

            CombatPosition authoritative = location.Position;

            // Checked before anything else, because NaN compares false against every bound
            // and would otherwise reach the arithmetic below and poison the position.
            if (!IsFinite(intent.X) || !IsFinite(intent.Z))
            {
                return MovementResult.Rejected(MovementRejection.NotFinite, authoritative);
            }

            float magnitudeSquared = (intent.X * intent.X) + (intent.Z * intent.Z);

            if (magnitudeSquared > MaximumInputMagnitude * MaximumInputMagnitude)
            {
                // An input longer than a unit vector is a request to move faster than the
                // authored speed. Refused rather than clamped: a clamp would silently
                // reward the attempt with maximum speed.
                return MovementResult.Rejected(MovementRejection.TooFar, authoritative);
            }

            long elapsed = serverTimestampMilliseconds - lastTimestampMilliseconds;

            if (elapsed <= 0L)
            {
                return MovementResult.Rejected(MovementRejection.NoElapsedTime, authoritative);
            }

            // Clamped before it is multiplied by speed. A client that waits a minute and
            // then presses forward gets one step, not a minute of walking in one frame.
            if (elapsed > budget.MaxElapsedMilliseconds) elapsed = budget.MaxElapsedMilliseconds;

            float seconds = elapsed / 1000f;
            float distance = budget.MetresPerSecond * seconds;

            CombatPosition destination = new CombatPosition(
                authoritative.X + (intent.X * distance),
                authoritative.Y,
                authoritative.Z + (intent.Z * distance));

            // The map is the character's own, read from the location. A client has no field
            // to put one in and could not change it if it had.
            var request = new MovementRequest(location.CharacterId, location.CurrentMap,
                destination, intent.Sequence, serverTimestampMilliseconds);

            // Every remaining rule -- sequence, map, bounds, distance, aliveness -- is the
            // existing validator's, unchanged and not restated.
            return MovementValidator.Validate(request, location, budget, lastSequence,
                lastTimestampMilliseconds, isAlive);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
