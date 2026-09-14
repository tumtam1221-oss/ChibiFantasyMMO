using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// How one basic attack or skill is <em>shown</em> — the animation to play and when the
    /// blow reads as landing. Presentation only.
    /// </summary>
    /// <remarks>
    /// <b>Never gameplay.</b> Nothing here decides damage, range, hit, cost, cooldown or
    /// attack permission. Those are the server's, resolved before any of this plays. This
    /// carries only the animator trigger, the clip/impact timing the visuals use, and
    /// optional cast/projectile presentation. A client that edited these would change how a
    /// swing looks, never what it does.
    ///
    /// <b>Zero means "use the shipped default".</b> A clip or impact time of zero defers to
    /// the approved <c>CombatFeedback</c> constants, so the unarmed cross punch reads exactly
    /// as it did in Phase 19E whether or not a definition is assigned.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Presentation/Combat Presentation",
        fileName = "CombatPresentation")]
    public sealed class CombatPresentationDefinition : ScriptableObject
    {
        [SerializeField] private DefinitionId _id;

        [Tooltip("Animator trigger this presentation fires. The approved unarmed punch uses "
            + "\"Attack\"; a cast uses \"Cast\".")]
        [SerializeField] private string _animatorTrigger = "Attack";

        [Tooltip("Whole clip length in seconds. Zero uses CombatFeedback.BasicAttackClipSeconds.")]
        [SerializeField] private float _clipSeconds;

        [Tooltip("When the blow reads as landing, in seconds from the swing start. Zero uses "
            + "CombatFeedback.BasicAttackImpactSeconds.")]
        [SerializeField] private float _impactSeconds;

        [Tooltip("A cast rather than a strike: fires the Cast trigger path. Presentation only.")]
        [SerializeField] private bool _isCast;

        [Tooltip("Optional visual projectile spawned at the release. Presentation only — "
            + "carries no damage.")]
        [SerializeField] private GameObject _projectilePrefab;

        [SerializeField] private float _projectileSpeed = 18f;

        public DefinitionId Id => _id;

        public string AnimatorTrigger => string.IsNullOrEmpty(_animatorTrigger)
            ? "Attack"
            : _animatorTrigger;

        public float ClipSeconds => _clipSeconds;

        public float ImpactSeconds => _impactSeconds;

        public bool IsCast => _isCast;

        public GameObject ProjectilePrefab => _projectilePrefab;

        public float ProjectileSpeed => _projectileSpeed <= 0f ? 18f : _projectileSpeed;
    }
}
