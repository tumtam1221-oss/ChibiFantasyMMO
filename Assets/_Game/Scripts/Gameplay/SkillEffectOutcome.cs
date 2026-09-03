using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What became of one effect.</summary>
    /// <remarks>
    /// <see cref="Unsupported"/> exists so an effect the runtime cannot perform is
    /// reported rather than skipped. A status effect that quietly did nothing and reported
    /// success would be the worst kind of bug: the fight looks correct, the player is not
    /// stunned, and nothing anywhere says why. Silence is not an option this enum offers.
    ///
    /// <see cref="NoOp"/> is distinct from <see cref="Applied"/>: an effect that computed
    /// zero, or healed a target already at full, genuinely ran and genuinely changed
    /// nothing, which a combat log should be able to say.
    /// </remarks>
    public enum SkillEffectStatus
    {
        /// <summary>Never set on a returned outcome.</summary>
        None = 0,

        /// <summary>The effect ran and changed state.</summary>
        Applied = 1,

        /// <summary>The effect ran and correctly changed nothing.</summary>
        NoOp = 2,

        /// <summary>This runtime cannot perform this kind of effect yet.</summary>
        Unsupported = 3,

        /// <summary>The effect was well formed but could not act on this target.</summary>
        Failed = 4
    }

    /// <summary>
    /// The result of one effect within one skill use.
    /// </summary>
    /// <remarks>
    /// One of these per authored effect, in authored order, so a caller can see exactly
    /// what each did rather than a single aggregate that hides an unsupported effect
    /// behind a successful one.
    ///
    /// <see cref="Before"/> and <see cref="After"/> are the affected pool's values, which
    /// is what makes an overheal or a killing blow legible: the applied change can be
    /// smaller than <see cref="Amount"/>, and both numbers are worth having.
    /// </remarks>
    public readonly struct SkillEffectOutcome
    {
        private SkillEffectOutcome(SkillEffectKind kind, SkillEffectStatus status, int amount,
            int before, int after, string detail)
        {
            Kind = kind;
            Status = status;
            Amount = amount;
            Before = before;
            After = after;
            Detail = detail;
        }

        /// <summary>The authored kind this outcome came from.</summary>
        public SkillEffectKind Kind { get; }

        public SkillEffectStatus Status { get; }

        /// <summary>The computed amount, before clamping by the target's pool.</summary>
        public int Amount { get; }

        /// <summary>Affected pool before the effect. Zero where no pool was touched.</summary>
        public int Before { get; }

        /// <summary>Affected pool after the effect.</summary>
        public int After { get; }

        /// <summary>Why, for unsupported and failed outcomes. Null otherwise.</summary>
        public string Detail { get; }

        /// <summary>How much the pool actually moved, which may be less than <see cref="Amount"/>.</summary>
        public int Change => After - Before;

        public bool DidMutate => Status == SkillEffectStatus.Applied;

        public static SkillEffectOutcome Applied(SkillEffectKind kind, int amount, int before, int after)
        {
            return new SkillEffectOutcome(
                kind,
                before == after ? SkillEffectStatus.NoOp : SkillEffectStatus.Applied,
                amount, before, after, null);
        }

        public static SkillEffectOutcome NoOp(SkillEffectKind kind, int amount, int value)
        {
            return new SkillEffectOutcome(kind, SkillEffectStatus.NoOp, amount, value, value, null);
        }

        public static SkillEffectOutcome Unsupported(SkillEffectKind kind, string detail)
        {
            return new SkillEffectOutcome(kind, SkillEffectStatus.Unsupported, 0, 0, 0, detail);
        }

        public static SkillEffectOutcome Failed(SkillEffectKind kind, string detail)
        {
            return new SkillEffectOutcome(kind, SkillEffectStatus.Failed, 0, 0, 0, detail);
        }

        public override string ToString()
        {
            return Kind + ": " + Status
                + (Status == SkillEffectStatus.Applied || Status == SkillEffectStatus.NoOp
                    ? " amount=" + Amount + " (" + Before + " -> " + After + ")"
                    : " (" + Detail + ")");
        }
    }
}
