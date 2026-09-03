using UnityEngine;

namespace ChibiFantasy.Client.Combat
{
    /// <summary>
    /// Which effect and sound answer which combat event.
    /// </summary>
    /// <remarks>
    /// <b>Hooks, not a library.</b> One prefab and one clip per category, and the
    /// categories are the ones combat can actually distinguish today. This is deliberately
    /// not a VFX system: no pooling, no timelines, no layering, no per-skill overrides.
    /// When skills need their own effects, the natural next step is an <c>AssetRef</c> on
    /// the skill definition resolved through this same seam, which is why the presenter
    /// looks its effects up rather than hard-coding them.
    ///
    /// <b>Every field is optional.</b> A null prefab or clip means that category simply
    /// does not present, and the presenter checks before using anything. Missing art must
    /// never be a crash and must never be a gameplay difference.
    ///
    /// Lives in the client assembly, because a prefab and an AudioClip are engine types
    /// and the combat rules must not see either.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Combat/Presentation Config",
                     fileName = "CombatPresentationConfig")]
    public sealed class CombatPresentationConfig : ScriptableObject
    {
        [Header("VFX (optional - missing is safe)")]
        [Tooltip("Spawned on the target when a physical blow lands.")]
        public GameObject physicalHitVfx;

        [Tooltip("Spawned on the target when a spell lands.")]
        public GameObject magicHitVfx;

        [Tooltip("Spawned on the target when healing lands.")]
        public GameObject healVfx;

        [Tooltip("Spawned on the caster while a skill with a cast time is winding up.")]
        public GameObject castVfx;

        [Tooltip("Spawned on a combatant that just died.")]
        public GameObject deathVfx;

        [Header("SFX (optional - missing is safe)")]
        public AudioClip attackSfx;
        public AudioClip magicCastSfx;
        public AudioClip magicHitSfx;
        public AudioClip healSfx;
        public AudioClip deathSfx;
        public AudioClip cancelSfx;

        [Header("Lifetime")]
        [Tooltip("Seconds before a spawned effect is destroyed. Keeps the scene from filling up.")]
        public float effectLifetimeSeconds = 2f;

        [Range(0f, 1f)]
        public float sfxVolume = 0.8f;
    }
}
