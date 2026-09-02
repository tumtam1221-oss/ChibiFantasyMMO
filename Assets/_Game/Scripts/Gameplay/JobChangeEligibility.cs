namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a job change was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, because every one of these needs a different
    /// message to a player and a different response from a server. Closed technical
    /// category: each value is a distinct branch a caller must handle.
    /// </remarks>
    public enum JobChangeRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>The target job has no definition.</summary>
        UnknownJob = 1,

        /// <summary>The character's class has no definition.</summary>
        UnknownClass = 2,

        /// <summary>The character's current job has no definition.</summary>
        UnknownCurrentJob = 3,

        /// <summary>The target is not among the jobs the current step leads to.</summary>
        NotOffered = 4,

        /// <summary>The target belongs to a different class tree.</summary>
        WrongBaseClass = 5,

        /// <summary>The target expects a different job to come before it.</summary>
        PrerequisiteNotMet = 6,

        /// <summary>The character has not reached the target's required level.</summary>
        LevelTooLow = 7,

        /// <summary>The character already holds the target job.</summary>
        AlreadyHeld = 8
    }

    /// <summary>
    /// The answer to whether one job change may happen.
    /// </summary>
    /// <remarks>
    /// Carries the required level whether or not it was met, so a caller can show the
    /// player what they are working toward rather than only that they failed.
    /// </remarks>
    public readonly struct JobChangeEligibility
    {
        private JobChangeEligibility(bool allowed, JobChangeRejection reason, int requiredLevel)
        {
            IsAllowed = allowed;
            Reason = reason;
            RequiredLevel = requiredLevel;
        }

        public bool IsAllowed { get; }

        /// <summary><see cref="JobChangeRejection.None"/> when allowed.</summary>
        public JobChangeRejection Reason { get; }

        /// <summary>Level the target demands. Zero when the target could not be resolved.</summary>
        public int RequiredLevel { get; }

        public static JobChangeEligibility Allowed(int requiredLevel)
        {
            return new JobChangeEligibility(true, JobChangeRejection.None, requiredLevel);
        }

        public static JobChangeEligibility Rejected(JobChangeRejection reason, int requiredLevel)
        {
            return new JobChangeEligibility(false, reason, requiredLevel);
        }

        public override string ToString()
        {
            return IsAllowed
                ? "allowed (requires level " + RequiredLevel + ")"
                : Reason + " (requires level " + RequiredLevel + ")";
        }
    }
}
