namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a monster did not move this tick.</summary>
    /// <remarks>
    /// Most of these are ordinary, not faults. A monster standing in <see cref="Idle"/> is
    /// working correctly. They are named separately because "did not move" covers a monster
    /// that is dead, one that has arrived, one that has nowhere to go and one that would
    /// leave the map — and a server operator watching a stuck monster needs to know which.
    /// </remarks>
    public enum MonsterMoveRejection
    {
        None = 0,

        /// <summary>No monster, or no definition to read a speed from.</summary>
        MissingContext = 1,

        /// <summary>Dead. A corpse does not walk.</summary>
        NotAlive = 2,

        /// <summary>No time passed, so no distance can have been covered.</summary>
        NoElapsedTime = 3,

        /// <summary>This behaviour state does not move. Idle, Detect, Attack, Wander.</summary>
        StateDoesNotMove = 4,

        /// <summary>Chasing, but there is nowhere to chase to.</summary>
        NoDestination = 5,

        /// <summary>Already at the destination, within the arrival tolerance.</summary>
        AlreadyThere = 6,

        /// <summary>The step would leave the authored bounds of the map.</summary>
        OutOfBounds = 7,

        /// <summary>The monster is authored with no speed, so it cannot move at all.</summary>
        NoSpeed = 8
    }

    /// <summary>What one movement step did.</summary>
    public readonly struct MonsterMoveResult
    {
        private MonsterMoveResult(bool moved, MonsterMoveRejection reason,
            CombatPosition position, float distance)
        {
            Moved = moved;
            Reason = reason;
            Position = position;
            Distance = distance;
        }

        public bool Moved { get; }

        /// <summary>Why not. <see cref="MonsterMoveRejection.None"/> when it did.</summary>
        public MonsterMoveRejection Reason { get; }

        /// <summary>
        /// Where the monster is now.
        /// </summary>
        /// <remarks>On a refusal this is where it already was, so the value is always the
        /// authoritative position and a caller can report it without checking.</remarks>
        public CombatPosition Position { get; }

        public float Distance { get; }

        public static MonsterMoveResult Stepped(CombatPosition position, float distance)
        {
            return new MonsterMoveResult(true, MonsterMoveRejection.None, position, distance);
        }

        public static MonsterMoveResult Refused(MonsterMoveRejection reason,
            CombatPosition current)
        {
            return new MonsterMoveResult(false, reason, current, 0f);
        }

        public override string ToString()
        {
            return Moved ? "moved " + Distance + " to " + Position : "still: " + Reason;
        }
    }

    /// <summary>
    /// Moves a monster, on the server, one tick at a time.
    /// </summary>
    /// <remarks>
    /// <b>This is not a command boundary, and that distinction is the design.</b>
    /// <see cref="MovementValidator"/> exists because a player <i>claims</i> a position and
    /// the server has to disbelieve it: it takes a request with a sequence number and a
    /// timestamp, and its whole job is refusing a lie. A monster claims nothing. There is no
    /// request, no client, and nothing to disbelieve — so routing monster movement through
    /// that seam would mean fabricating a client request server-side, which is precisely the
    /// fake-command shape this project avoids. Monster movement is server-internal state
    /// advancement, exactly like the AI tick that precedes it.
    ///
    /// <b>It decides nothing about behaviour.</b> Which state a monster is in, what it is
    /// targeting and whether it has been leashed are all
    /// <see cref="MonsterAiController"/>'s, decided before this runs. This answers one
    /// question: given that state, where is the monster a moment later. Splitting them is
    /// what keeps 17.11 from becoming a second AI.
    ///
    /// <b>Deterministic and engine-free.</b> No clock, no randomness, no physics, no
    /// <c>NavMesh</c>, no <c>Transform</c>. Time arrives as an argument and the same inputs
    /// always produce the same output, which is what makes a chase reproducible in a test.
    ///
    /// <b>Straight-line movement, and that is a documented limitation.</b> A monster walks
    /// directly toward its destination and will walk into a wall, because this project has
    /// no navigation system and inventing one here would be a far larger change than the
    /// sub-phase calls for. Pathfinding belongs to a later phase; when it arrives it
    /// replaces the destination this steps toward, not this stepping.
    /// </remarks>
    public static class MonsterMovement
    {
        /// <summary>
        /// How close counts as arrived.
        /// </summary>
        /// <remarks>Without a tolerance a monster oscillates around its destination forever,
        /// stepping past it and back every tick, because floating point will never land
        /// exactly.</remarks>
        public const float ArrivalEpsilon = 0.01f;

        /// <summary>
        /// Advances one monster by one tick.
        /// </summary>
        /// <param name="monster">The authoritative state. Written only on success.</param>
        /// <param name="state">What the AI decided this tick.</param>
        /// <param name="target">
        /// Where the monster's target is, or null when there is none the server could
        /// resolve. Supplied by the caller rather than looked up here, so this stays free of
        /// resolution and map-scoping concerns — a cross-map or unknown target simply
        /// arrives as null.
        /// </param>
        /// <param name="deltaSeconds">Server tick length.</param>
        /// <param name="maxRadius">
        /// The authored extent of the map, or zero for unbounded. The same
        /// <c>MapDefinition.MovementRadius</c> a player is held to, so a monster cannot walk
        /// somewhere a player could not follow.
        /// </param>
        public static MonsterMoveResult Step(MonsterRuntimeState monster, MonsterAiState state,
            CombatPosition? target, float deltaSeconds, float maxRadius = 0f)
        {
            if (monster == null || monster.Definition == null)
            {
                return MonsterMoveResult.Refused(MonsterMoveRejection.MissingContext, default);
            }

            CombatPosition current = monster.Position;

            // A corpse does not walk. Checked before anything else, because a dead monster
            // in any state is still dead.
            if (!monster.IsAlive)
            {
                return MonsterMoveResult.Refused(MonsterMoveRejection.NotAlive, current);
            }

            if (deltaSeconds <= 0f)
            {
                return MonsterMoveResult.Refused(MonsterMoveRejection.NoElapsedTime, current);
            }

            if (!TryDestinationFor(monster, state, target, out CombatPosition destination,
                out MonsterMoveRejection rejection))
            {
                return MonsterMoveResult.Refused(rejection, current);
            }

            float speed = monster.Definition.MoveSpeed;

            if (speed <= 0f)
            {
                // An authored speed of zero is a stationary monster -- a turret, a plant.
                // Reported rather than silently doing nothing, so a mis-authored one is
                // visible.
                return MonsterMoveResult.Refused(MonsterMoveRejection.NoSpeed, current);
            }

            float dx = destination.X - current.X;
            float dy = destination.Y - current.Y;
            float dz = destination.Z - current.Z;

            float sqrRemaining = dx * dx + dy * dy + dz * dz;

            if (sqrRemaining <= ArrivalEpsilon * ArrivalEpsilon)
            {
                return MonsterMoveResult.Refused(MonsterMoveRejection.AlreadyThere, current);
            }

            float remaining = Sqrt(sqrRemaining);
            float budget = speed * deltaSeconds;

            // Never overshoot. A long tick must not throw a monster past its target and
            // leave it oscillating.
            float distance = budget < remaining ? budget : remaining;

            float scale = distance / remaining;

            var next = new CombatPosition(
                current.X + dx * scale,
                current.Y + dy * scale,
                current.Z + dz * scale);

            if (!next.IsFinite)
            {
                // Cannot arise from server state, but a position that is not a number would
                // corrupt every distance check downstream, so it is refused rather than
                // stored.
                return MonsterMoveResult.Refused(MonsterMoveRejection.NoDestination, current);
            }

            if (maxRadius > 0f && !WithinRadius(next, maxRadius))
            {
                // Refused rather than clamped: a monster that stops at the edge is
                // comprehensible, and one slid along an invisible wall is not. The leash
                // will send it home shortly anyway.
                return MonsterMoveResult.Refused(MonsterMoveRejection.OutOfBounds, current);
            }

            // The only line that changes anything, and unreachable from every refusal above.
            monster.Position = next;

            return MonsterMoveResult.Stepped(next, distance);
        }

        /// <summary>
        /// Where a monster in this state is trying to get to.
        /// </summary>
        /// <remarks>
        /// The single place that maps a behaviour state onto a destination. Two states move:
        /// <see cref="MonsterAiState.Chase"/> toward its target, and
        /// <see cref="MonsterAiState.Return"/> toward the spawn it came from. Everything else
        /// stands still, including <see cref="MonsterAiState.Attack"/> — a monster already in
        /// reach has no reason to keep walking into its target.
        ///
        /// <see cref="MonsterAiState.Wander"/> is listed as stationary deliberately. The
        /// state exists in Phase 10's enum but the controller never enters it, so any wander
        /// behaviour written here would be dead code that looked implemented.
        /// </remarks>
        private static bool TryDestinationFor(MonsterRuntimeState monster, MonsterAiState state,
            CombatPosition? target, out CombatPosition destination,
            out MonsterMoveRejection rejection)
        {
            destination = default;
            rejection = MonsterMoveRejection.None;

            switch (state)
            {
                case MonsterAiState.Chase:
                    if (target == null || !target.Value.IsFinite)
                    {
                        // Chasing nothing. The AI will drop to Return next tick; this tick
                        // it simply does not move.
                        rejection = MonsterMoveRejection.NoDestination;

                        return false;
                    }

                    destination = target.Value;

                    return true;

                case MonsterAiState.Return:
                    destination = monster.SpawnPosition;

                    return true;

                default:
                    rejection = MonsterMoveRejection.StateDoesNotMove;

                    return false;
            }
        }

        /// <summary>Horizontal bound only, matching the rule players are held to.</summary>
        private static bool WithinRadius(in CombatPosition position, float radius)
        {
            float x = position.X;
            float z = position.Z;

            return x * x + z * z <= radius * radius;
        }

        /// <summary>Square root without the engine's maths.</summary>
        /// <remarks>This assembly is engine-free; <c>System.Math</c> is not the engine.</remarks>
        private static float Sqrt(float value)
        {
            return value <= 0f ? 0f : (float)System.Math.Sqrt(value);
        }
    }
}
