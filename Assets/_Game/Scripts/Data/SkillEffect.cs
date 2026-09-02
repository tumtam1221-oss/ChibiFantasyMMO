using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What kind of thing a skill effect does.</summary>
    /// <remarks>
    /// Describes, never executes. Each value names a distinct branch a future combat system
    /// will have to implement explicitly, which is what makes an enum right here.
    ///
    /// Buff, debuff, silence, stun and slow are all <see cref="ApplyStatusEffect"/> pointing
    /// at a <see cref="StatusEffectDefinition"/>; the difference between them is authored on
    /// the status effect rather than the skill. Damage and heal over time are the same:
    /// a status effect that ticks, not a separate kind here.
    ///
    /// Shield, knockback, teleport, summon, cleanse and dispel are absent because nothing
    /// yet knows their shape. Adding one later is a new value plus a new factory; no
    /// existing effect data changes, which is what keeps authored content patch-safe.
    /// </remarks>
    public enum SkillEffectKind
    {
        None = 0,
        Damage = 1,
        Heal = 2,
        ApplyStatusEffect = 3,
        ModifyStat = 4,
        ModifyResource = 5
    }

    /// <summary>
    /// One thing a skill does, described as data.
    /// </summary>
    /// <remarks>
    /// <b>The boundary this type exists to draw.</b> A skill can state what it does and
    /// nothing here can do it. No damage is computed, no status applied, no resource spent,
    /// no target chosen. A future server-authoritative combat system reads this; it is not
    /// implemented, and this type deliberately gives it nothing to lean on.
    ///
    /// <b>Amounts are flat plus scaling, not a formula language.</b> An effect is a counted
    /// base amount plus a sum of stat terms, each a rational multiple of one stat. That is
    /// the same shape <see cref="DerivedStatFormulaDefinition"/> already uses, so no second
    /// formula system was invented and no expression needs evaluating. Integers and
    /// rationals throughout means the same effect describes the same operation on every
    /// machine, which is what a server and a client must agree on.
    ///
    /// <b>Every part is an existing type.</b> Scaling is <see cref="StatTerm"/>, stat
    /// changes are <see cref="StatModifier"/>, elements are <see cref="ElementType"/>,
    /// resources are <see cref="SkillResourceType"/>. Nothing here duplicates a concept the
    /// project already had.
    ///
    /// <b>Kind decides which fields matter.</b> Rather than a constructor taking everything,
    /// each kind has a factory that asks only for what it needs, so an effect cannot be
    /// built in a shape its kind does not support. Fields a kind does not use stay at their
    /// defaults and are ignored; validation states which are required.
    ///
    /// Nothing presentational appears here. Icons, animations and sounds live on the skill,
    /// not on what the skill does to someone.
    /// </remarks>
    [Serializable]
    public struct SkillEffect
    {
        [SerializeField] private SkillEffectKind _kind;
        [SerializeField] private DefinitionId _reference;
        [SerializeField] private ElementType _element;
        [SerializeField] private SkillResourceType _resource;
        [SerializeField] private int _flatAmount;
        [SerializeField] private StatTerm[] _scaling;
        [SerializeField] private StatModifier _statModifier;

        public SkillEffectKind Kind => _kind;

        /// <summary>
        /// The definition this effect acts through.
        /// </summary>
        /// <remarks>A <see cref="StatusEffectDefinition"/> for
        /// <see cref="SkillEffectKind.ApplyStatusEffect"/>. Unused by every other kind,
        /// which name what they act on through their own fields.</remarks>
        public DefinitionId Reference => _reference;

        /// <summary>Element of a damage effect. Neutral where it does not apply.</summary>
        public ElementType Element => _element;

        /// <summary>Which pool a heal or resource change touches.</summary>
        public SkillResourceType Resource => _resource;

        /// <summary>
        /// Counted base amount, before scaling.
        /// </summary>
        /// <remarks>An integer because damage, healing and resource changes are counted
        /// rather than measured, and a player compares them.</remarks>
        public int FlatAmount => _flatAmount;

        /// <summary>
        /// Contributions from the caster's stats, summed on top of the flat amount.
        /// </summary>
        /// <remarks>May be empty for an effect that does not scale. How the caster's stats
        /// are obtained is the future combat system's problem, not this contract's.</remarks>
        public StatTerm[] Scaling => _scaling;

        /// <summary>The change a stat-modifying effect makes.</summary>
        public StatModifier StatModifier => _statModifier;

        /// <summary>Damage of an element, from a flat amount and optional stat scaling.</summary>
        public static SkillEffect Damage(int flatAmount, ElementType element, StatTerm[] scaling = null)
        {
            return new SkillEffect
            {
                _kind = SkillEffectKind.Damage,
                _element = element,
                _flatAmount = flatAmount,
                _scaling = scaling ?? new StatTerm[0]
            };
        }

        /// <summary>Restoration of a resource, from a flat amount and optional stat scaling.</summary>
        public static SkillEffect Heal(int flatAmount, SkillResourceType resource,
            StatTerm[] scaling = null)
        {
            return new SkillEffect
            {
                _kind = SkillEffectKind.Heal,
                _resource = resource,
                _flatAmount = flatAmount,
                _scaling = scaling ?? new StatTerm[0]
            };
        }

        /// <summary>Application of an authored status effect.</summary>
        /// <remarks>Duration, stacking and what the status actually does are all authored on
        /// the <see cref="StatusEffectDefinition"/>, so a skill never restates them.</remarks>
        public static SkillEffect ApplyStatusEffect(DefinitionId statusEffect)
        {
            return new SkillEffect
            {
                _kind = SkillEffectKind.ApplyStatusEffect,
                _reference = statusEffect,
                _scaling = new StatTerm[0]
            };
        }

        /// <summary>A direct change to one stat.</summary>
        public static SkillEffect ModifyStat(StatModifier modifier)
        {
            return new SkillEffect
            {
                _kind = SkillEffectKind.ModifyStat,
                _statModifier = modifier,
                _scaling = new StatTerm[0]
            };
        }

        /// <summary>A change to a resource pool, positive or negative.</summary>
        public static SkillEffect ModifyResource(SkillResourceType resource, int amount,
            StatTerm[] scaling = null)
        {
            return new SkillEffect
            {
                _kind = SkillEffectKind.ModifyResource,
                _resource = resource,
                _flatAmount = amount,
                _scaling = scaling ?? new StatTerm[0]
            };
        }
    }
}
