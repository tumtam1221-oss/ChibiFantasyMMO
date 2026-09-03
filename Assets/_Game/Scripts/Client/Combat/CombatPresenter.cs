using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.Combat
{
    /// <summary>
    /// Draws what combat already decided.
    /// </summary>
    /// <remarks>
    /// <b>Strictly downstream, and provably so.</b> It subscribes to
    /// <see cref="CombatActionDriver.Presentation"/> and does three things: sets Animator
    /// parameters, spawns an optional effect, plays an optional sound. It holds no combat
    /// state, calls no executor, and touches no health, mana, cooldown or skill state.
    /// Delete this component and every fight resolves identically -- that is the property
    /// the phase is built to guarantee, and the regression harness measures it rather than
    /// assuming it.
    ///
    /// <b>Nothing here is authoritative.</b> No animation event exists in this project and
    /// none is introduced. Damage is not applied from a clip, a frame, a particle or a
    /// sound; by the time an event arrives the numbers are already final and this only
    /// reads them.
    ///
    /// <b>Every reference is optional.</b> A missing Animator, a disabled one, a missing
    /// parameter, a null prefab and a null clip are all ordinary states, checked once and
    /// then skipped quietly. Optional art must not produce a console error every frame,
    /// and must never change what happened.
    ///
    /// <b>Existing Animator parameters are reused.</b> <c>Attack</c> and <c>AttackPhase</c>
    /// already exist from the prototype controller; <c>Cast</c>, <c>Hit</c> and
    /// <c>Dead</c> are added only because nothing expressed them. No parameter duplicates
    /// <see cref="CombatAction"/>, which remains the gameplay state; these mirror it.
    /// </remarks>
    [RequireComponent(typeof(CombatActionDriver))]
    public sealed class CombatPresenter : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private CombatPresentationConfig config;

        [Tooltip("Where effects and sounds are placed. Falls back to this transform.")]
        [SerializeField] private Transform presentationAnchor;

        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int AttackPhaseHash = Animator.StringToHash("AttackPhase");
        private static readonly int CastHash = Animator.StringToHash("Cast");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        private CombatActionDriver _driver;
        private CombatantBehaviour _self;
        private AudioSource _audio;
        private GameObject _activeCastVfx;

        private bool _checked;
        private bool _hasAttack, _hasPhase, _hasCast, _hasHit, _hasDead;

        // Counters exist so a test can prove one execution produces one presentation.
        public int StartedCount { get; private set; }
        public int ExecutedCount { get; private set; }
        public int HitCount { get; private set; }
        public int DeathCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int RejectedCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int VfxSpawnedCount { get; private set; }
        public int SfxPlayedCount { get; private set; }

        /// <summary>The last event drawn, of any kind. For inspection and tests.</summary>
        public CombatPresentationEvent LastEvent { get; private set; }

        /// <summary>
        /// The last strike specifically.
        /// </summary>
        /// <remarks>Kept apart from <see cref="LastEvent"/> because an action ends with a
        /// Completed event that carries no damage type, so "what was the last hit" and
        /// "what happened most recently" are different questions. A HUD asking about
        /// damage wants this one.</remarks>
        public CombatPresentationEvent LastHit { get; private set; }

        /// <summary>The last refusal specifically, for the same reason.</summary>
        public CombatPresentationEvent LastRejected { get; private set; }

        public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

        public void SetAnimator(Animator value) { animator = value; _checked = false; }
        public void SetConfig(CombatPresentationConfig value) { config = value; }

        /// <summary>Clears counters so a test can measure one scenario at a time.</summary>
        public void ResetCounters()
        {
            StartedCount = 0; ExecutedCount = 0; HitCount = 0; DeathCount = 0;
            CancelledCount = 0; RejectedCount = 0; CompletedCount = 0;
            VfxSpawnedCount = 0; SfxPlayedCount = 0;
        }

        private void Awake()
        {
            _driver = GetComponent<CombatActionDriver>();
            _self = GetComponent<CombatantBehaviour>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (presentationAnchor == null) presentationAnchor = transform;
            _audio = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (_driver != null) _driver.Presentation += OnPresentation;
        }

        private void OnDisable()
        {
            if (_driver != null) _driver.Presentation -= OnPresentation;
            StopCastVfx();
        }

        /// <summary>Checks once which Animator parameters exist, rather than assuming.</summary>
        private void EnsureParameters()
        {
            if (_checked) return;
            _checked = true;
            _hasAttack = _hasPhase = _hasCast = _hasHit = _hasDead = false;

            if (!HasAnimator) return;

            AnimatorControllerParameter[] parameters = animator.parameters;

            for (int i = 0; i < parameters.Length; i++)
            {
                int hash = parameters[i].nameHash;
                AnimatorControllerParameterType type = parameters[i].type;

                if (hash == AttackHash && type == AnimatorControllerParameterType.Trigger) _hasAttack = true;
                if (hash == CastHash && type == AnimatorControllerParameterType.Trigger) _hasCast = true;
                if (hash == HitHash && type == AnimatorControllerParameterType.Trigger) _hasHit = true;
                if (hash == AttackPhaseHash && type == AnimatorControllerParameterType.Int) _hasPhase = true;
                if (hash == DeadHash && type == AnimatorControllerParameterType.Bool) _hasDead = true;
            }
        }

        private bool AnimatorUsable
        {
            get
            {
                EnsureParameters();
                return HasAnimator && animator.isActiveAndEnabled;
            }
        }

        // ------------------------------------------------------------- the one entry point

        private void OnPresentation(CombatPresentationEvent e)
        {
            LastEvent = e;

            switch (e.Kind)
            {
                case CombatPresentationEventKind.Started:
                    StartedCount++;
                    OnStarted(e);
                    break;

                case CombatPresentationEventKind.Executed:
                    ExecutedCount++;
                    OnExecuted(e);
                    break;

                case CombatPresentationEventKind.Hit:
                    HitCount++;
                    LastHit = e;
                    OnHit(e);
                    break;

                case CombatPresentationEventKind.Death:
                    DeathCount++;
                    OnDeath(e);
                    break;

                case CombatPresentationEventKind.Cancelled:
                    CancelledCount++;
                    StopCastVfx();
                    SetPhase(0);
                    Play(config == null ? null : config.cancelSfx);
                    break;

                case CombatPresentationEventKind.Rejected:
                    // Deliberately quiet: a refusal is a UI concern and there is no combat
                    // UI in this phase. The count is here so a future HUD has a hook.
                    RejectedCount++;
                    LastRejected = e;
                    break;

                case CombatPresentationEventKind.Completed:
                    CompletedCount++;
                    StopCastVfx();
                    SetPhase(0);
                    break;
            }
        }

        private void OnStarted(CombatPresentationEvent e)
        {
            SetPhase(1);

            if (e.ActionType == CombatActionType.Skill && e.Skill.IsValid)
            {
                Trigger(CastHash, _hasCast);
                Play(config == null ? null : config.magicCastSfx);
                StartCastVfx();
            }
            else
            {
                Trigger(AttackHash, _hasAttack);
                Play(config == null ? null : config.attackSfx);
            }
        }

        private void OnExecuted(CombatPresentationEvent e)
        {
            StopCastVfx();
            SetPhase(2);

            // A skill whose wind-up already played a cast still swings on release.
            if (e.ActionType == CombatActionType.Skill) Trigger(AttackHash, _hasAttack);
        }

        private void OnHit(CombatPresentationEvent e)
        {
            // A hit describes the TARGET. A presenter riding the attacker must not flinch
            // on its own rig, or an attacker plays a hit reaction every time it lands one.
            if (IsAboutMe(e)) Trigger(HitHash, _hasHit);

            if (config == null) return;

            if (e.IsHeal)
            {
                Spawn(config.healVfx);
                Play(config.healSfx);
                return;
            }

            // The authored damage type chooses the impact. Reused from PHASE 07.4;
            // nothing is recomputed and no second enum exists.
            if (e.DamageType == DamageType.Magic)
            {
                Spawn(config.magicHitVfx);
                Play(config.magicHitSfx);
            }
            else
            {
                Spawn(config.physicalHitVfx);
            }
        }

        private void OnDeath(CombatPresentationEvent e)
        {
            // Death also describes the TARGET. Driving the local rig here would kill the
            // attacker every time it scored a kill, which is exactly what happened before
            // this guard existed.
            if (IsAboutMe(e) && AnimatorUsable && _hasDead) animator.SetBool(DeadHash, true);

            if (config == null) return;
            Spawn(config.deathVfx);
            Play(config.deathSfx);
        }

        /// <summary>
        /// Whether an event's target is the combatant this presenter rides.
        /// </summary>
        /// <remarks>
        /// Hit and death are reactions belonging to whoever was struck. Every other event
        /// -- started, executed, cancelled, completed -- belongs to the actor, and this
        /// presenter is the actor's, so only these two need asking.
        ///
        /// A combatant that is both is answered correctly by the same comparison.
        /// </remarks>
        private bool IsAboutMe(CombatPresentationEvent e)
        {
            if (_self == null) return false;
            return e.TargetId.IsValid && e.TargetId == _self.CombatantId;
        }

        // ------------------------------------------------------------- safe primitives

        private void Trigger(int hash, bool exists)
        {
            if (exists && AnimatorUsable) animator.SetTrigger(hash);
        }

        private void SetPhase(int phase)
        {
            EnsureParameters();
            if (_hasPhase && AnimatorUsable) animator.SetInteger(AttackPhaseHash, phase);
        }

        private void StartCastVfx()
        {
            if (config == null || config.castVfx == null) return;

            StopCastVfx();
            _activeCastVfx = Instantiate(config.castVfx, Anchor.position, Anchor.rotation, Anchor);
            VfxSpawnedCount++;
        }

        private void StopCastVfx()
        {
            if (_activeCastVfx == null) return;
            Destroy(_activeCastVfx);
            _activeCastVfx = null;
        }

        private void Spawn(GameObject prefab)
        {
            if (prefab == null) return;

            GameObject instance = Instantiate(prefab, Anchor.position, Anchor.rotation);
            VfxSpawnedCount++;

            float life = config == null ? 2f : config.effectLifetimeSeconds;
            if (life > 0f) Destroy(instance, life);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null) return;

            SfxPlayedCount++;

            // An AudioSource is optional too; without one there is simply no sound.
            if (_audio == null) return;
            _audio.PlayOneShot(clip, config == null ? 1f : config.sfxVolume);
        }

        private Transform Anchor => presentationAnchor != null ? presentationAnchor : transform;
    }
}
