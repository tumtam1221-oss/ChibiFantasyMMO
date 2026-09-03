using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What a card effect applies to.</summary>
    /// <remarks>
    /// Closed technical category. Each value names a different question combat has to ask,
    /// and adding one is a code change by design -- a value nothing consumes would be
    /// authored content that silently does nothing, which is worse than a missing feature
    /// because a player pays for it.
    /// </remarks>
    public enum CardEffectKind
    {
        None = 0,

        /// <summary>Extra damage dealt to monsters of an authored rank.</summary>
        DamageVersusRank = 1,

        /// <summary>Damage reduced from monsters of an authored rank.</summary>
        DefenseVersusRank = 2,

        /// <summary>Extra damage dealt to monsters of an authored element.</summary>
        DamageVersusElement = 3,

        /// <summary>Damage reduced from monsters of an authored element.</summary>
        DefenseVersusElement = 4
    }

    /// <summary>
    /// One conditional effect a card contributes.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not a <see cref="StatModifier"/>.</b> A card that grants flat strength
    /// authors a <see cref="StatModifier"/> and reaches
    /// <c>DerivedStatsCalculator</c> through the existing modifier path, unchanged. "Twenty
    /// percent more damage to undead" is a different shape: it depends on who is being hit,
    /// which no character stat can express and no derived-stat total can hold. Forcing it
    /// into a modifier would mean either inventing a fake stat per monster rank or computing
    /// combat numbers outside the calculator.
    ///
    /// <b>A typed seam, honestly incomplete.</b> These are authored, validated, resolved and
    /// reported. <em>No combat formula consumes them in this phase</em>, and nothing pretends
    /// otherwise -- <c>BasicDamageFormula</c> is untouched. The alternative was to fake the
    /// behaviour or to reopen Phase 07 combat, and a typed placeholder that a later phase
    /// reads is better than either.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>card_effect</c> table
    /// is a card id, an ordinal, a kind, a rank, an element, a magnitude and a modifier kind.
    /// Nothing here names a monster: a card applies to a whole authored class of them.
    /// </remarks>
    [Serializable]
    public struct CardEffect
    {
        [SerializeField] private CardEffectKind _kind;

        [Tooltip("Monster rank a versus-rank effect applies to.")]
        [SerializeField] private MonsterRank _rank;

        [Tooltip("Monster element a versus-element effect applies to.")]
        [SerializeField] private ElementType _element;

        [Tooltip("Magnitude. Read as points or as a fraction, per Scaling.")]
        [SerializeField] private float _value;

        [Tooltip("Whether Value is a flat amount or a fraction.")]
        [SerializeField] private StatModifierKind _scaling;

        public CardEffect(CardEffectKind kind, float value,
            StatModifierKind scaling = StatModifierKind.Percent,
            MonsterRank rank = MonsterRank.Normal, ElementType element = ElementType.Neutral)
        {
            _kind = kind;
            _rank = rank;
            _element = element;
            _value = value;
            _scaling = scaling;
        }

        public CardEffectKind Kind => _kind;

        /// <summary>Meaningful only for the versus-rank kinds.</summary>
        public MonsterRank Rank => _rank;

        /// <summary>Meaningful only for the versus-element kinds.</summary>
        public ElementType Element => _element;

        public float Value => _value;

        public StatModifierKind Scaling => _scaling;

        public bool IsValid
        {
            get
            {
                if (_kind == CardEffectKind.None) return false;
                if (float.IsNaN(_value) || float.IsInfinity(_value)) return false;
                return true;
            }
        }

        /// <summary>Whether this effect applies against a given monster's authored class.</summary>
        /// <remarks>The one place the condition is evaluated, so a future combat formula asks
        /// rather than re-deriving it.</remarks>
        public bool AppliesTo(MonsterRank rank, ElementType element)
        {
            switch (_kind)
            {
                case CardEffectKind.DamageVersusRank:
                case CardEffectKind.DefenseVersusRank:
                    return _rank == rank;

                case CardEffectKind.DamageVersusElement:
                case CardEffectKind.DefenseVersusElement:
                    return _element == element;

                default:
                    return false;
            }
        }

        public override string ToString()
        {
            switch (_kind)
            {
                case CardEffectKind.DamageVersusRank:
                case CardEffectKind.DefenseVersusRank:
                    return _kind + " " + _rank + " " + _value;
                case CardEffectKind.DamageVersusElement:
                case CardEffectKind.DefenseVersusElement:
                    return _kind + " " + _element + " " + _value;
                default:
                    return _kind.ToString();
            }
        }
    }
}
