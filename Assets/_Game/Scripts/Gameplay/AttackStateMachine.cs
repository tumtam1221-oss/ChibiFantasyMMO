namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a combatant is in the rhythm of a swing.</summary>
    public enum AttackPhase
    {
        /// <summary>Not attacking. A new attack may begin.</summary>
        Idle = 0,

        /// <summary>Mid-swing. The blow has already been applied.</summary>
        Attacking = 1,

        /// <summary>After the swing, before the next may begin.</summary>
        Recovery = 2
    }

    /// <summary>How long a swing and its recovery take.</summary>
    /// <remarks>
    /// Values, not constants in code, so timing is tunable without a recompile. Negatives
    /// are folded to zero at construction rather than rejected: a zero-length phase is a
    /// meaningful configuration (an instant swing) and the alternative is throwing during
    /// a fight because somebody typed a minus sign.
    ///
    /// This is not a cooldown. A cooldown is per-ability, survives leaving combat and will
    /// belong with the skill runtime state from Phase 06; this is only the window in which
    /// one entity cannot swing again.
    /// </remarks>
    public readonly struct AttackTiming
    {
        public AttackTiming(float attackDuration, float recoveryDuration)
        {
            AttackDuration = attackDuration > 0f ? attackDuration : 0f;
            RecoveryDuration = recoveryDuration > 0f ? recoveryDuration : 0f;
        }

        public float AttackDuration { get; }

        public float RecoveryDuration { get; }

        public float TotalDuration => AttackDuration + RecoveryDuration;

        /// <summary>A swing that occupies no time at all. Legal, and useful in tests.</summary>
        public static AttackTiming Instant => default;
    }

    /// <summary>
    /// Stops a combatant swinging faster than its timing allows.
    /// </summary>
    /// <remarks>
    /// <b>The caller supplies the time.</b> <see cref="Advance"/> takes a delta rather than
    /// reading <c>UnityEngine.Time</c>, which keeps the Gameplay assembly engine-free and
    /// makes the machine testable at exact durations instead of at whatever the frame rate
    /// happened to be. The same deltas always produce the same phases.
    ///
    /// <b>It cannot stick.</b> Remaining time only ever decreases, a non-positive remainder
    /// always advances the phase, and the loop in <see cref="Advance"/> drains a delta
    /// larger than both phases in one call rather than leaving a backlog. A frame spike is
    /// therefore a jump to <see cref="AttackPhase.Idle"/>, never a combatant frozen
    /// mid-swing. Non-finite and negative deltas are ignored, so a corrupt frame time
    /// cannot poison the state either.
    ///
    /// <b>Damage is not here.</b> The machine says only whether a swing may start; the blow
    /// is applied by <see cref="BasicAttackExecutor"/> at that instant. Nothing waits for
    /// an animation, so combat remains correct with no Animator present at all.
    /// </remarks>
    public sealed class AttackStateMachine
    {
        /// <summary>
        /// How close to zero counts as elapsed.
        /// </summary>
        /// <remarks>
        /// Without this, a phase ends only on exact float equality, which summed deltas
        /// never reach: advancing 0.2, 0.2, 0.5 and 0.1 through a 0.4/0.6 swing leaves
        /// roughly 2e-8 seconds outstanding, and a hundred 0.01 steps drift further than
        /// one 1.0 step. The visible symptom is a phase change landing a frame later at
        /// one frame rate than another, which is precisely the frame-rate dependence this
        /// machine exists to avoid.
        ///
        /// Ten microseconds is far below anything a player or an animation can resolve --
        /// about 0.06% of a 60Hz frame -- and comfortably above the accumulated error of
        /// any sane attack duration.
        /// </remarks>
        private const float SettleEpsilon = 1e-5f;

        private AttackTiming _timing;
        private float _remaining;

        public AttackStateMachine(AttackTiming timing)
        {
            _timing = timing;
            Phase = AttackPhase.Idle;
            _remaining = 0f;
        }

        public AttackPhase Phase { get; private set; }

        /// <summary>Seconds left in the current phase. Zero while idle.</summary>
        public float Remaining => _remaining;

        public AttackTiming Timing => _timing;

        /// <summary>Whether a new swing may begin right now.</summary>
        public bool CanAttack => Phase == AttackPhase.Idle;

        /// <summary>Replaces the timing. Takes effect from the next swing, not retroactively.</summary>
        public void SetTiming(AttackTiming timing)
        {
            _timing = timing;
        }

        /// <summary>
        /// Begins a swing if the machine is idle.
        /// </summary>
        /// <returns>False when already attacking or recovering, which is the whole point:
        /// a caller that ignores the result cannot spam through it, because the phase is
        /// unchanged and the executor is never reached.</returns>
        public bool TryBeginAttack()
        {
            if (Phase != AttackPhase.Idle)
            {
                return false;
            }

            Phase = AttackPhase.Attacking;
            _remaining = _timing.AttackDuration;

            // A zero-length swing must not need a frame to leave; settle immediately so a
            // caller reading the phase in the same tick sees the truth.
            Settle();
            return true;
        }

        /// <summary>
        /// Moves time forward.
        /// </summary>
        /// <remarks>Ignores non-finite or non-positive deltas rather than trusting them.
        /// A negative delta would wind a swing backwards and a NaN would make every
        /// comparison false, leaving the phase stuck forever.</remarks>
        public void Advance(float deltaSeconds)
        {
            if (Phase == AttackPhase.Idle)
            {
                return;
            }

            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            _remaining -= deltaSeconds;
            Settle();
        }

        /// <summary>Forces the machine back to idle. For despawn, death and state resets.</summary>
        public void Reset()
        {
            Phase = AttackPhase.Idle;
            _remaining = 0f;
        }

        /// <summary>
        /// Consumes any exhausted phases.
        /// </summary>
        /// <remarks>A loop rather than one step, so a delta longer than the whole sequence
        /// lands on idle in a single call instead of needing one call per phase.</remarks>
        private void Settle()
        {
            while (_remaining <= SettleEpsilon && Phase != AttackPhase.Idle)
            {
                float carry = _remaining;

                if (Phase == AttackPhase.Attacking)
                {
                    Phase = AttackPhase.Recovery;
                    _remaining = _timing.RecoveryDuration + carry;
                }
                else
                {
                    Phase = AttackPhase.Idle;
                    _remaining = 0f;
                }
            }

            if (Phase == AttackPhase.Idle)
            {
                _remaining = 0f;
            }
        }

        public override string ToString()
        {
            return Phase + (Phase == AttackPhase.Idle
                ? string.Empty
                : " (" + _remaining.ToString("0.###") + "s left)");
        }
    }
}
