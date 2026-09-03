using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a status effect did not land.</summary>
    public enum StatusApplyRejection
    {
        None = 0,

        /// <summary>No state or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The effect could not be resolved.</summary>
        UnknownEffect = 2,

        /// <summary>The character refuses this effect, or its whole category.</summary>
        Immune = 3,

        /// <summary>Already present, and the authored rule is to ignore a re-application.</summary>
        AlreadyPresent = 4
    }

    /// <summary>What applying a status effect did.</summary>
    public readonly struct StatusApplyResult
    {
        private StatusApplyResult(bool accepted, StatusApplyRejection reason, DefinitionId effect,
            float duration, int stacks)
        {
            IsAccepted = accepted;
            Reason = reason;
            Effect = effect;
            DurationSeconds = duration;
            Stacks = stacks;
        }

        public bool IsAccepted { get; }

        public StatusApplyRejection Reason { get; }

        public DefinitionId Effect { get; }

        /// <summary>What was actually applied, after the authored override was resolved.</summary>
        public float DurationSeconds { get; }

        public int Stacks { get; }

        public static StatusApplyResult Accepted(DefinitionId effect, float duration, int stacks)
        {
            return new StatusApplyResult(true, StatusApplyRejection.None, effect, duration, stacks);
        }

        public static StatusApplyResult Rejected(StatusApplyRejection reason,
            DefinitionId effect = default)
        {
            return new StatusApplyResult(false, reason, effect, 0f, 0);
        }

        public override string ToString()
        {
            return IsAccepted ? Effect + " applied" : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Decides whether a status effect may be applied, and applies it.
    /// </summary>
    /// <remarks>
    /// <b>The rules live here; the list lives in the state.</b>
    /// <see cref="StatusEffectRuntimeState"/> holds what is applied and knows how to stack;
    /// this decides whether anything should be. Splitting them is what lets a server make
    /// the decision and hand a client the outcome to record.
    ///
    /// <b>Immunity is checked before anything is written.</b> A refused effect leaves the
    /// state byte-for-byte as it was, which is the same validate-then-mutate contract every
    /// service in this assembly keeps.
    ///
    /// <b>It knows no effect.</b> No <see cref="DefinitionId"/> is compared to a literal.
    /// Whether an effect silences, buffs or burns is <see cref="StatusEffectDefinition"/>'s
    /// business, and immunity is expressed against an authored category rather than a list
    /// of names.
    /// </remarks>
    public static class StatusEffectService
    {
        /// <summary>
        /// Applies an authored effect to a character.
        /// </summary>
        /// <param name="state">Who is receiving it.</param>
        /// <param name="effectId">Reference to a <see cref="StatusEffectDefinition"/>.</param>
        /// <param name="source">What is granting it, so it can later be taken back exactly.</param>
        /// <param name="effects">Registry the effect is resolved through.</param>
        /// <param name="durationOverride">Seconds. Zero defers to the effect's own duration.</param>
        /// <param name="stacks">How many stacks this application is worth.</param>
        public static StatusApplyResult TryApply(StatusEffectRuntimeState state,
            DefinitionId effectId, DefinitionId source,
            IDefinitionRegistry<StatusEffectDefinition> effects,
            float durationOverride = 0f, int stacks = 1)
        {
            if (state == null || effects == null)
                return StatusApplyResult.Rejected(StatusApplyRejection.MissingContext, effectId);

            StatusEffectDefinition definition;
            if (!effectId.IsValid || !effects.TryGet(effectId, out definition) || definition == null)
                return StatusApplyResult.Rejected(StatusApplyRejection.UnknownEffect, effectId);

            // The whole point of an immunity: it is asked before the state is touched.
            if (state.IsImmuneTo(effectId, definition.Category))
                return StatusApplyResult.Rejected(StatusApplyRejection.Immune, effectId);

            // An authored override wins; zero defers to the effect's own duration, matching
            // the convention ItemUseService already uses for buff items.
            float duration = durationOverride > 0f
                ? durationOverride
                : definition.DurationSeconds;

            var active = new ActiveStatusEffect(effectId, source, duration, stacks);

            if (!state.Apply(active, definition.StackBehavior, definition.MaxStacks))
                return StatusApplyResult.Rejected(StatusApplyRejection.AlreadyPresent, effectId);

            return StatusApplyResult.Accepted(effectId, duration, stacks);
        }

        /// <summary>
        /// Whether an effect would be refused, without applying it.
        /// </summary>
        /// <remarks>What a tooltip uses, so the UI asks the same question the service will
        /// answer rather than reading the immunity list itself.</remarks>
        public static bool WouldBeRefused(StatusEffectRuntimeState state, DefinitionId effectId,
            IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            if (state == null || effects == null) return true;

            StatusEffectDefinition definition;
            if (!effects.TryGet(effectId, out definition) || definition == null) return true;

            return state.IsImmuneTo(effectId, definition.Category);
        }
    }
}
