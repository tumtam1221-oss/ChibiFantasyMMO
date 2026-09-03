using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why an enhancement attempt was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, matching <see cref="ItemContainerRejection"/>,
    /// <see cref="EquipRejection"/> and <see cref="ItemUseRejection"/>. Every one is
    /// checked <em>before</em> anything is written or consumed.
    /// </remarks>
    public enum EnhancementRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No inventory, no registry, or no equipment was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such piece was found to enhance.</summary>
        InvalidEquipment = 2,

        /// <summary>The piece belongs to someone else.</summary>
        NotOwner = 3,

        /// <summary>The piece's definition could not be resolved.</summary>
        InvalidDefinition = 4,

        /// <summary>The definition is not authored as enhanceable.</summary>
        NotEnhanceable = 5,

        /// <summary>The definition names no enhancement track, or it could not be resolved.</summary>
        InvalidRule = 6,

        /// <summary>The piece is already at the strictest authored ceiling.</summary>
        AlreadyMaxLevel = 7,

        /// <summary>The track authors no step advancing from the current level.</summary>
        NoStepForLevel = 8,

        /// <summary>The required material is missing or there is not enough of it.</summary>
        MissingMaterial = 9,

        /// <summary>The currency cost cannot be paid.</summary>
        InsufficientCost = 10,

        /// <summary>The authored step is malformed -- a negative cost, an impossible chance.</summary>
        InvalidStep = 11,

        /// <summary>
        /// The step's failure behaviour cannot be carried out as authored.
        /// </summary>
        /// <remarks>A downgrade authored at level zero, for instance. Refused rather than
        /// improvised: silently clamping would apply a consequence content did not
        /// choose.</remarks>
        InvalidFailureBehavior = 12
    }

    /// <summary>What an enhancement attempt did to the piece.</summary>
    /// <remarks>Distinct from success and failure of the <em>attempt</em>: an attempt can be
    /// accepted and still have left the piece worse off, which is the point of authored
    /// failure behaviour.</remarks>
    public enum EnhancementOutcome
    {
        /// <summary>The attempt was refused. Nothing happened.</summary>
        Rejected = 0,

        /// <summary>The level went up by one.</summary>
        Upgraded = 1,

        /// <summary>The roll failed and the level held.</summary>
        FailedKept = 2,

        /// <summary>The roll failed and the level dropped by one.</summary>
        FailedDowngraded = 3,

        /// <summary>The roll failed and the level was reset to zero.</summary>
        FailedReset = 4,

        /// <summary>The roll failed and the piece was destroyed.</summary>
        FailedDestroyed = 5
    }

    /// <summary>
    /// What an enhancement attempt did.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="ItemContainerResult"/>, <see cref="EquipResult"/> and
    /// <see cref="ItemUseResult"/>: accepted or refused, a typed reason, and the figures
    /// that explain it.
    ///
    /// <see cref="IsAccepted"/> means the attempt ran, not that it went well. A destroyed
    /// sword is an accepted attempt with <see cref="EnhancementOutcome.FailedDestroyed"/>,
    /// because the materials were legitimately spent and the rule was legitimately applied.
    /// Conflating the two would leave a caller unable to tell "you cannot try that" from
    /// "you tried and it broke".
    /// </remarks>
    public readonly struct EnhancementResult
    {
        private EnhancementResult(bool accepted, EnhancementRejection reason,
            EnhancementOutcome outcome, InstanceId instanceId, int fromLevel, int toLevel,
            int materialsConsumed, int currencySpent, Revision revision)
        {
            IsAccepted = accepted;
            Reason = reason;
            Outcome = outcome;
            InstanceId = instanceId;
            FromLevel = fromLevel;
            ToLevel = toLevel;
            MaterialsConsumed = materialsConsumed;
            CurrencySpent = currencySpent;
            Revision = revision;
        }

        /// <summary>Whether the attempt ran at all. Not whether it succeeded.</summary>
        public bool IsAccepted { get; }

        public EnhancementRejection Reason { get; }

        public EnhancementOutcome Outcome { get; }

        /// <summary>The piece that was attempted.</summary>
        public InstanceId InstanceId { get; }

        public int FromLevel { get; }

        /// <summary>Where it ended up. Equal to <see cref="FromLevel"/> when the level held.</summary>
        public int ToLevel { get; }

        public int MaterialsConsumed { get; }

        public int CurrencySpent { get; }

        /// <summary>The piece's revision after the attempt, for stale-state checks.</summary>
        public Revision Revision { get; }

        /// <summary>Whether the level actually went up.</summary>
        public bool IsUpgrade => Outcome == EnhancementOutcome.Upgraded;

        /// <summary>Whether the piece no longer exists.</summary>
        public bool WasDestroyed => Outcome == EnhancementOutcome.FailedDestroyed;

        public static EnhancementResult Accepted(EnhancementOutcome outcome, InstanceId instanceId,
            int fromLevel, int toLevel, int materials, int currency, Revision revision)
        {
            return new EnhancementResult(true, EnhancementRejection.None, outcome, instanceId,
                fromLevel, toLevel, materials, currency, revision);
        }

        public static EnhancementResult Rejected(EnhancementRejection reason,
            InstanceId instanceId = default, int fromLevel = 0)
        {
            return new EnhancementResult(false, reason, EnhancementOutcome.Rejected, instanceId,
                fromLevel, fromLevel, 0, 0, default);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;

            return Outcome + " +" + FromLevel + " -> +" + ToLevel
                + " (materials " + MaterialsConsumed + ", currency " + CurrencySpent + ")";
        }
    }
}
