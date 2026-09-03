using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The combat figures a skill needs that the skill schema does not author.
    /// </summary>
    /// <remarks>
    /// A skill authors how much it hits for; it does not author what resists it. The
    /// defending stat is a combat concern, named by id exactly as
    /// <see cref="BasicAttackRules"/> names it, so no stat name appears in skill code.
    ///
    /// Deliberately not <see cref="BasicAttackRules"/> itself: that type also carries an
    /// attack-power stat, a reach and a relationship mask, and for a skill all three come
    /// from the effect and the definition instead. Reusing it would mean populating three
    /// fields with values that are never read.
    /// </remarks>
    public readonly struct SkillExecutionRules
    {
        public SkillExecutionRules(DefinitionId defenseStat, int minimumDamage)
            : this(defenseStat, DefinitionId.None, minimumDamage)
        {
        }

        public SkillExecutionRules(DefinitionId defenseStat, DefinitionId magicDefenseStat,
            int minimumDamage)
        {
            DefenseStat = defenseStat;
            MagicDefenseStat = magicDefenseStat;
            MinimumDamage = minimumDamage < 0 ? 0 : minimumDamage;
        }

        /// <summary>Id of the stat that resists <see cref="DamageType.Physical"/> damage.</summary>
        public DefinitionId DefenseStat { get; }

        /// <summary>
        /// Id of the stat that resists <see cref="DamageType.Magic"/> damage.
        /// </summary>
        /// <remarks>A second id rather than a second formula. Which stat defends is
        /// content; how damage is computed is not, and there is only one of those.</remarks>
        public DefinitionId MagicDefenseStat { get; }

        /// <summary>Floor applied after subtraction. Never negative.</summary>
        public int MinimumDamage { get; }

        /// <summary>No defending stat and no floor: damage lands as authored.</summary>
        public static SkillExecutionRules None => default;

        /// <summary>
        /// The defence stat that answers a damage type.
        /// </summary>
        /// <remarks>The single place the physical/magic choice is made. Having it here
        /// rather than inline in the executor is what makes it testable on its own and
        /// what stops the two ever being crossed.
        /// <see cref="DamageType.None"/> resolves as physical; see that type for why.</remarks>
        public DefinitionId DefenseStatFor(DamageType damageType)
        {
            return damageType == DamageType.Magic ? MagicDefenseStat : DefenseStat;
        }
    }

    /// <summary>
    /// Runs one skill use: validate, pay, apply each effect, report.
    /// </summary>
    /// <remarks>
    /// <b>The only writer in the skill path</b>, exactly as
    /// <see cref="BasicAttackExecutor"/> is for basic attacks. Every other 07.3 type
    /// answers a question.
    ///
    /// <b>Transaction policy: validate everything, then execute.</b>
    /// <see cref="SkillUseValidator"/> runs to completion first, so a rejected use never
    /// reaches a single mutation and no rollback is ever needed. Once past validation the
    /// effects run in authored order and are not rolled back if a later one turns out to
    /// be unsupported, because <see cref="CharacterResourceState"/> offers no transaction
    /// and building compensating writes would be a larger and less honest mechanism than
    /// simply reporting what each effect did. Every effect reports its own outcome, so a
    /// partially-supported skill is visible rather than hidden. This is stated here
    /// because it is a real design limit, not an oversight.
    ///
    /// <b>Cost is paid once, after validation and before any effect.</b> Validation has
    /// already confirmed the caster can afford it, so the deduction cannot fail and cannot
    /// leave a half-paid use.
    ///
    /// <b>Damage reuses <see cref="BasicDamageFormula"/>.</b> No skill damage formula
    /// exists: the effect's computed amount is composed into the existing calculation as
    /// its attack power. One formula, one place to rebalance, one thing for a server to
    /// reproduce.
    ///
    /// <b>Unsupported is never silent.</b> Status effects and stat modifiers have no
    /// runtime yet and are reported as such. Nothing pretends they applied.
    /// </remarks>
    public static class SkillExecutor
    {
        private static readonly SkillEffectOutcome[] NoEffects = new SkillEffectOutcome[0];

        public static SkillExecutionResult Execute(in SkillUseRequest request,
            in SkillUseContext context, in SkillExecutionRules rules)
        {
            ICombatant caster = request.Caster;

            var casterId = caster == null ? default : caster.CombatantId;
            var requestedTargetId = request.Target == null ? default : request.Target.CombatantId;

            SkillUseEligibility eligibility = SkillUseValidator.Evaluate(request, context);

            if (!eligibility.IsAllowed)
            {
                return SkillExecutionResult.Rejected(
                    eligibility.Reason, request.Skill, casterId, requestedTargetId);
            }

            ICombatant target = eligibility.ResolvedTarget;
            SkillDefinition definition = eligibility.Skill;
            SkillLevelEntry level = eligibility.Level;

            // --- pay, once, now that nothing can refuse the use ---------------------
            int spent = 0;

            if (eligibility.ResourceCost > 0)
            {
                var pool = caster as ICombatantResourcePool;

                if (pool != null
                    && pool.TryApplyResourceDelta(definition.ResourceType, -eligibility.ResourceCost))
                {
                    spent = eligibility.ResourceCost;
                }
            }

            // --- cooldown begins on use, not on completion --------------------------
            if (context.Cooldowns != null)
            {
                context.Cooldowns.Begin(request.Skill, level.CooldownSeconds);
            }

            int healthBefore = target.CurrentHealth;

            SkillEffect[] effects = level.Effects;
            SkillEffectOutcome[] outcomes = effects == null || effects.Length == 0
                ? NoEffects
                : new SkillEffectOutcome[effects.Length];

            for (int i = 0; i < outcomes.Length; i++)
            {
                outcomes[i] = ApplyEffect(effects[i], caster, target, rules);
            }

            int healthAfter = target.CurrentHealth;

            return SkillExecutionResult.Executed(request.Skill, eligibility.Rank,
                casterId, target.CombatantId, spent, healthBefore, healthAfter, outcomes);
        }

        /// <summary>Dispatches one authored effect onto the existing state.</summary>
        private static SkillEffectOutcome ApplyEffect(in SkillEffect effect, ICombatant caster,
            ICombatant target, in SkillExecutionRules rules)
        {
            switch (effect.Kind)
            {
                case SkillEffectKind.Damage:
                    return ApplyDamage(effect, caster, target, rules);

                case SkillEffectKind.Heal:
                    return ApplyHeal(effect, caster, target);

                case SkillEffectKind.ModifyResource:
                    return ApplyResource(effect, caster, target);

                case SkillEffectKind.ApplyStatusEffect:
                    // The definition exists; no runtime that could hold an active status does.
                    return SkillEffectOutcome.Unsupported(effect.Kind,
                        "No status-effect runtime exists yet; nothing was applied.");

                case SkillEffectKind.ModifyStat:
                    // StatModifier is consumed by DerivedStatsCalculator at calculation
                    // time. Nothing yet holds a live set of modifiers for it to read.
                    return SkillEffectOutcome.Unsupported(effect.Kind,
                        "No runtime stat-modifier container exists yet; nothing was applied.");

                case SkillEffectKind.None:
                default:
                    return SkillEffectOutcome.Unsupported(effect.Kind,
                        "Effect kind has no runtime implementation.");
            }
        }

        private static SkillEffectOutcome ApplyDamage(in SkillEffect effect, ICombatant caster,
            ICombatant target, in SkillExecutionRules rules)
        {
            // Power is flat plus authored scaling. Which stat that scaling reads is what
            // makes a skill physical or magical on the attacking side; the damage type
            // decides only which defence answers it.
            int power = SkillAmountCalculator.CalculateMagnitude(effect, caster);

            DefinitionId defenseStat = rules.DefenseStatFor(effect.DamageType);
            int defense = defenseStat.IsValid
                && target.TryGetCombatStat(defenseStat, out int value) ? value : 0;

            // One formula for basic attacks and for both damage types. Physical and magic
            // differ in which stats they read, never in how the subtraction is done.
            int damage = BasicDamageFormula.Calculate(power, defense, rules.MinimumDamage);

            int before = target.CurrentHealth;
            target.ApplyHealthDelta(-(long)damage);
            int after = target.CurrentHealth;

            // The authored type is carried into the outcome so presentation can tell a
            // blow from a spell. Nothing about the calculation changes.
            return SkillEffectOutcome.Applied(effect.Kind, damage, before, after, effect.DamageType);
        }

        private static SkillEffectOutcome ApplyHeal(in SkillEffect effect, ICombatant caster,
            ICombatant target)
        {
            int amount = SkillAmountCalculator.CalculateMagnitude(effect, caster);

            // Health is on the base combat contract; every other pool is optional.
            if (effect.Resource == SkillResourceType.Health || effect.Resource == SkillResourceType.None)
            {
                int before = target.CurrentHealth;
                target.ApplyHealthDelta(amount);
                return SkillEffectOutcome.Applied(effect.Kind, amount, before, target.CurrentHealth);
            }

            return ApplyToPool(effect.Kind, target, effect.Resource, amount);
        }

        private static SkillEffectOutcome ApplyResource(in SkillEffect effect, ICombatant caster,
            ICombatant target)
        {
            // Signed: a resource effect may drain as well as restore.
            int amount = SkillAmountCalculator.Calculate(effect, caster);

            if (effect.Resource == SkillResourceType.Health)
            {
                int before = target.CurrentHealth;
                target.ApplyHealthDelta(amount);
                return SkillEffectOutcome.Applied(effect.Kind, amount, before, target.CurrentHealth);
            }

            if (effect.Resource == SkillResourceType.None)
            {
                return SkillEffectOutcome.Failed(effect.Kind,
                    "Effect names no resource pool to modify.");
            }

            return ApplyToPool(effect.Kind, target, effect.Resource, amount);
        }

        /// <summary>Applies to an optional pool, reporting honestly when the target has none.</summary>
        private static SkillEffectOutcome ApplyToPool(SkillEffectKind kind, ICombatant target,
            SkillResourceType resource, int amount)
        {
            var pool = target as ICombatantResourcePool;

            if (pool == null || !pool.TryGetResource(resource, out int before, out _))
            {
                return SkillEffectOutcome.Unsupported(kind,
                    "Target has no " + resource + " pool.");
            }

            pool.TryApplyResourceDelta(resource, amount);
            pool.TryGetResource(resource, out int after, out _);

            return SkillEffectOutcome.Applied(kind, amount, before, after);
        }
    }
}
