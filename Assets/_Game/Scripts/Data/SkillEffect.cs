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
    /// Buff, debuff, silence, stun and slow are all
    /// <see cref="ApplyStatusEffect"/> pointing at a
    /// <see cref="StatusEffectDefinition"/>; they are not separate kinds, because the
    /// difference between them is authored on the status effect rather than on the skill.
    ///
    /// Summon and projectile are absent on purpose: those are delivery and spawning
    /// mechanisms rather than effects, and inventing their shape before a combat system
    /// exists would be a guess.
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
    /// This is the boundary the step exists to draw: a skill can say what it does, and
    /// nothing here can do it. No damage is computed, no status applied, no resource spent.
    ///
    /// <see cref="Reference"/> means different things per kind and is unused by some: a
    /// status effect for <see cref="SkillEffectKind.ApplyStatusEffect"/>, a stat for
    /// <see cref="SkillEffectKind.ModifyStat"/>, and nothing for plain damage. Validation
    /// enforces which kinds require it.
    /// </remarks>
    [Serializable]
    public struct SkillEffect
    {
        [SerializeField] private SkillEffectKind _kind;
        [SerializeField] private DefinitionId _reference;
        [SerializeField] private float _magnitude;

        public SkillEffect(SkillEffectKind kind, DefinitionId reference, float magnitude)
        {
            _kind = kind;
            _reference = reference;
            _magnitude = magnitude;
        }

        public SkillEffectKind Kind => _kind;

        /// <summary>The definition this effect acts through, where the kind needs one.</summary>
        public DefinitionId Reference => _reference;

        /// <summary>Authored size of the effect. What it measures is the kind's business.</summary>
        public float Magnitude => _magnitude;
    }
}
