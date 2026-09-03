using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why socketing a stone was refused.</summary>
    /// <remarks>A reason rather than a bare false, matching the rest of the project's
    /// vocabulary. Every one is checked before anything is written or consumed.</remarks>
    public enum EnchantRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No inventory, no registry, or no equipment was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such piece was found.</summary>
        InvalidEquipment = 2,

        /// <summary>The piece belongs to someone else.</summary>
        NotOwner = 3,

        /// <summary>The piece's definition could not be resolved, or it is not equipment.</summary>
        InvalidDefinition = 4,

        /// <summary>No stone was found in the slot given.</summary>
        InvalidStone = 5,

        /// <summary>The item found is not authored as a status stone.</summary>
        NotAStone = 6,

        /// <summary>The stone does not fit this category, subtype, slot or tier.</summary>
        NotCompatible = 7,

        /// <summary>Every socket is full.</summary>
        NoCapacity = 8,

        /// <summary>The piece already holds as many of this stone as the stone allows.</summary>
        DuplicateNotAllowed = 9,

        /// <summary>The piece does not meet a requirement the stone authors.</summary>
        RequirementNotMet = 10,

        /// <summary>The stone's configuration is malformed.</summary>
        InvalidRule = 11
    }

    /// <summary>What a socketing attempt did.</summary>
    public enum EnchantOutcome
    {
        /// <summary>The attempt was refused. Nothing happened.</summary>
        Rejected = 0,

        /// <summary>The stone went in.</summary>
        Socketed = 1,

        /// <summary>The roll failed and the stone survived.</summary>
        FailedStoneKept = 2,

        /// <summary>The roll failed and the stone was consumed.</summary>
        FailedStoneLost = 3,

        /// <summary>The roll failed, the stone was consumed and the sockets were cleared.</summary>
        FailedSocketsCleared = 4
    }

    /// <summary>
    /// What socketing a stone did.
    /// </summary>
    /// <remarks>
    /// Shaped after <see cref="EnhancementResult"/>. <see cref="IsAccepted"/> means the
    /// attempt ran, not that the stone went in: a failed roll that ate the stone is an
    /// accepted attempt, because the cost was legitimately paid and the authored rule
    /// legitimately applied.
    /// </remarks>
    public readonly struct EnchantResult
    {
        private EnchantResult(bool accepted, EnchantRejection reason, EnchantOutcome outcome,
            InstanceId equipment, DefinitionId stone, int socketIndex, int stonesConsumed,
            int enchantCount, Revision revision)
        {
            IsAccepted = accepted;
            Reason = reason;
            Outcome = outcome;
            EquipmentInstanceId = equipment;
            Stone = stone;
            SocketIndex = socketIndex;
            StonesConsumed = stonesConsumed;
            EnchantCount = enchantCount;
            Revision = revision;
        }

        /// <summary>Whether the attempt ran. Not whether the stone went in.</summary>
        public bool IsAccepted { get; }

        public EnchantRejection Reason { get; }

        public EnchantOutcome Outcome { get; }

        /// <summary>The piece that was worked on.</summary>
        public InstanceId EquipmentInstanceId { get; }

        /// <summary>The stone that was attempted.</summary>
        public DefinitionId Stone { get; }

        /// <summary>Where it landed. Minus one when it did not.</summary>
        public int SocketIndex { get; }

        public int StonesConsumed { get; }

        /// <summary>How many stones the piece holds afterwards.</summary>
        public int EnchantCount { get; }

        /// <summary>The piece's revision after the attempt, for stale-state checks.</summary>
        public Revision Revision { get; }

        public bool WasSocketed => Outcome == EnchantOutcome.Socketed;

        public static EnchantResult Accepted(EnchantOutcome outcome, InstanceId equipment,
            DefinitionId stone, int socketIndex, int consumed, int enchantCount, Revision revision)
        {
            return new EnchantResult(true, EnchantRejection.None, outcome, equipment, stone,
                socketIndex, consumed, enchantCount, revision);
        }

        public static EnchantResult Rejected(EnchantRejection reason,
            InstanceId equipment = default, DefinitionId stone = default)
        {
            return new EnchantResult(false, reason, EnchantOutcome.Rejected, equipment, stone,
                -1, 0, 0, default);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;

            return Outcome + " " + Stone + " socket " + SocketIndex
                + " (stones " + StonesConsumed + ", now " + EnchantCount + ")";
        }
    }
}
