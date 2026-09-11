using ChibiFantasy.Data;
using ChibiFantasy.Network;
using TMPro;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Turns a replicated character into something a player can see.
    /// </summary>
    /// <remarks>
    /// <b>One presenter, two branches.</b> The local player and everybody else are the same
    /// character with the same model, the same animator and the same authoritative position;
    /// the only differences are whether a camera follows them and whether their own nameplate
    /// is drawn. Two classes would have meant two places to fix a walk cycle, and they would
    /// have drifted the first time one was edited.
    ///
    /// <b>It observes; it decides nothing.</b> Every value below is read from
    /// <see cref="CharacterNetworkEntity"/>, which is server-write-only. This class has no
    /// <c>ServerRpc</c>, does not implement <c>NetworkBehaviour</c>, and cannot reach combat,
    /// movement, inventory or reward authority -- there is no field, no interface and no
    /// method by which it could. Animation is downstream of position; position is never
    /// downstream of animation.
    ///
    /// <b>Speed is measured, not asked for.</b> The server replicates where the character is,
    /// not how fast it is going, so walking is inferred from how far the visible transform
    /// actually moved. That is why the walk animation is right for a remote player nobody is
    /// sending input for: the position moved, so the legs move.
    ///
    /// <b>The model is built once per identity.</b> A snapshot arrives many times a second
    /// and rebuilding a rig on each one would be the single most expensive mistake available
    /// here, so a model is instanced when the replicated gender first arrives and then only
    /// if it actually changes.
    ///
    /// <b>Root motion is off, and that is a safety property.</b> A clip that moved the
    /// transform would be animation deciding position, which is the client deciding position.
    /// </remarks>
    [RequireComponent(typeof(CharacterNetworkEntity))]
    public sealed class CharacterVisualPresenter : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        /// <summary>
        /// The trigger the locomotion controller already has for a swing.
        /// </summary>
        /// <remarks>
        /// <b>The link that was missing.</b> The server has been publishing accepted attacks
        /// for a long time: its replication step raises
        /// <see cref="CharacterNetworkEntity.AttackPerformed"/> on every client that can see
        /// the character, and nothing anywhere subscribed to it. Every part of the chain
        /// existed except the last one, so a player swung, a monster lost health, and the
        /// character on screen carried on standing there. Exactly the same shape of gap as
        /// the monster attack intents nobody read.
        ///
        /// (The server type that publishes it is deliberately not named here. A test scans
        /// every client file for the names of server classes, and a comment counts -- the
        /// rule is that the client does not know those types exist, not merely that it does
        /// not call them.)
        ///
        /// Nothing about the rig, the controller or the locomotion changes to fix it: the
        /// <c>Attack</c> trigger and the state it drives were already authored.
        /// </remarks>
        private static readonly int AttackHash = Animator.StringToHash("Attack");

        [Tooltip("Which approved model to use, and how to animate it.")]
        [SerializeField] private CharacterVisualCatalogue _catalogue;

        [Tooltip("Degrees per second the visual turns towards the way it is moving.")]
        [SerializeField] private float _turnSpeed = 720f;

        [Tooltip("Degrees the visual falls over when the server says they are down.")]
        [SerializeField] private float _deathTilt = 80f;

        [Tooltip("Seconds the blend takes to lean into a walk. This and the one below are "
            + "the only knobs between standing and walking -- the two clips share one blend "
            + "tree, so there is no state transition to time.")]
        [SerializeField] private float _speedRiseSeconds = 0.08f;

        [Tooltip("Seconds the blend takes to settle back to standing. Longer than the rise, "
            + "so stopping reads as a character slowing rather than a clip being switched "
            + "off, but short enough that the feet do not keep walking on the spot.")]
        [SerializeField] private float _speedFallSeconds = 0.15f;

        private CharacterNetworkEntity _entity;

        private Transform _visualRoot;
        private GameObject _model;
        private Animator _animator;
        private CharacterNameplate _nameplate;

        private Vector3 _lastPosition;

        /// <summary>The number the animator was last shown, which is the eased one.</summary>
        private float _shownSpeed;
        private bool _hasLastPosition;
        private int _builtGenderCode = int.MinValue;
        private bool _presentedDead;
        private float _facing;

        /// <summary>The gender the model was built for.</summary>
        public CharacterGender Gender => CharacterVisualCatalogue.GenderOf(
            _entity == null ? 0 : _entity.GenderCode);

        /// <summary>The transform the model hangs from. Presentation-only.</summary>
        public Transform VisualRoot => _visualRoot;

        /// <summary>The instanced model, or null if the catalogue had none.</summary>
        public GameObject Model => _model;

        public Animator Animator => _animator;

        public bool HasVisual => _model != null;

        /// <summary>How many times a model has been instanced. One per identity, not per frame.</summary>
        public int BuildCount { get; private set; }

        /// <summary>The last normalised speed handed to the animator, 0..1.</summary>
        /// <summary>
        /// How much walk is in the blend, from standing at zero to full pace at one.
        /// </summary>
        /// <remarks>This is the eased number actually handed to the animator, not the raw
        /// per-frame measurement, because the eased one is what a player sees and therefore
        /// the one worth asserting on.</remarks>
        public float Speed01 { get; private set; }

        /// <summary>The measurement before easing. For diagnosis and for tests.</summary>
        public float MeasuredSpeed01 { get; private set; }

        /// <summary>Whether the presentation is currently showing them as down.</summary>
        public bool IsPresentedDead => _presentedDead;

        /// <summary>The direction the visual faces, in degrees. Presentation-only.</summary>
        public float Facing => _facing;

        /// <summary>What the nameplate currently reads.</summary>
        public string NameplateText => _nameplate == null ? string.Empty : _nameplate.Text;

        public CharacterNameplate Nameplate => _nameplate;

        /// <summary>Supplies the catalogue, for composition that is not the prefab.</summary>
        public void UseCatalogue(CharacterVisualCatalogue catalogue)
        {
            _catalogue = catalogue;

            // A catalogue arriving after the first model was chosen must be allowed to
            // choose again, or a test that composes in that order would silently prove
            // nothing.
            _builtGenderCode = int.MinValue;
        }

        private void Awake()
        {
            _entity = GetComponent<CharacterNetworkEntity>();

            _visualRoot = new GameObject("VisualRoot").transform;
            _visualRoot.SetParent(transform, false);

            if (_entity != null) _entity.AttackPerformed += OnAttackPerformed;
        }

        /// <summary>How many swings this character has been seen to throw. For tests.</summary>
        public int AttacksShown { get; private set; }

        /// <summary>
        /// Draws the swing the server has already resolved.
        /// </summary>
        /// <remarks>
        /// <b>A report, never a cause.</b> By the time this runs the server has validated the
        /// range, spent the cooldown, rolled the damage and written the health. Nothing here
        /// is consulted about any of that, and an animation event is never allowed to be the
        /// thing that deals damage -- the trigger only decides what the character looks like
        /// while it happens.
        /// </remarks>
        private void OnAttackPerformed()
        {
            AttacksShown++;

            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            _animator.SetTrigger(AttackHash);
        }

        /// <summary>
        /// Brings the picture up to date.
        /// </summary>
        /// <remarks>Public so a test can step it without waiting on frames, which is the
        /// only way an assertion about animation state can be made deterministically.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (_entity == null) return;

            EnsureModel();

            float speed = MeasureSpeed(deltaSeconds);

            PresentDeath();

            if (_presentedDead)
            {
                // Down: no walking, no turning. The position still follows the server,
                // because the server is still the one saying where the body is. The ease is
                // dropped rather than run down, so a corpse is standing still on the frame
                // it falls over rather than a fifth of a second later.
                MeasuredSpeed01 = 0f;
                _shownSpeed = 0f;
                Speed01 = 0f;
                Apply(0f);

                return;
            }

            MeasuredSpeed01 = speed;

            _shownSpeed = CharacterVisualRules.DampedSpeed(_shownSpeed, speed, deltaSeconds,
                speed > _shownSpeed ? _speedRiseSeconds : _speedFallSeconds);

            Speed01 = _shownSpeed;

            Apply(Speed01);

            Face(deltaSeconds);

            if (_nameplate != null) _nameplate.Refresh(Describe());
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        // ---- the model -------------------------------------------------------------------

        /// <summary>
        /// Instances the approved model, once, when the server has said which.
        /// </summary>
        /// <remarks>Gender arrives with the rest of the identity a frame or two after the
        /// object spawns, so this is checked rather than done in <c>Awake</c>. Once built it
        /// is left alone: the comparison below is against the code the model was built for,
        /// not against whether a model exists.</remarks>
        private void EnsureModel()
        {
            if (_catalogue == null) return;

            // The server runs this same prefab. A headless server rigging a character mesh
            // it will never draw is pure cost, once per player, forever.
            if (!_entity.IsClientStarted) return;

            int code = _entity.GenderCode;

            if (code == _builtGenderCode) return;

            _builtGenderCode = code;

            GameObject prefab = _catalogue.ModelFor(code);

            if (_model != null)
            {
                DestroyVisual(_model);
                _model = null;
                _animator = null;
            }

            if (prefab == null)
            {
                // No approved model configured for this gender. Drawing nothing is the
                // honest answer; drawing the other gender is not.
                return;
            }

            _model = Instantiate(prefab, _visualRoot);

            // Lifted by the rig's sole depth so the feet rest on the ground rather than in
            // it. The object's own position is untouched: this moves the picture, not the
            // character.
            _model.transform.localPosition = new Vector3(0f, _catalogue.GroundOffsetFor(code), 0f);
            _model.transform.localRotation = Quaternion.identity;

            // The imported models carry a degenerate skinned-mesh bounding box (millimetres,
            // measured 0.004 x 0.009 x 0.002 on both production rigs). Left alone, the
            // renderer is judged off-screen the moment that box leaves the frustum and the
            // animator stops updating -- the character freezes mid-stride or vanishes while
            // its position keeps moving, which reads as stutter. Recomputing the bounds from
            // the bones every frame costs a little and makes culling honest.
            foreach (SkinnedMeshRenderer skinned in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.updateWhenOffscreen = true;
            }

            BuildCount++;

            BindAnimator(code);
            BuildNameplate();
        }

        /// <summary>
        /// Puts an animator into the state a networked character needs, whoever built it.
        /// </summary>
        /// <remarks>
        /// <b>Animation never moves anybody.</b> The locomotion clips are the in-place
        /// variants and root motion is off, so the authored forward travel is discarded
        /// rather than applied: a clip that moved the transform would be the client writing
        /// its own position, one frame at a time.
        ///
        /// <b>A character's legs must not depend on a bounding box.</b> Both production rigs
        /// import with <c>CullUpdateTransforms</c>, which stops writing bone transforms the
        /// moment the renderer is judged off-screen. The pose then freezes while the position
        /// carries on, and resumes with a jump when the bounds come back -- measured as a
        /// foot bone travelling exactly 0.0000 m over sixty frames off screen, against
        /// 0.0733 m once this is applied.
        ///
        /// The box that decides it is barely half the character (0.29 x 0.82 x 0.63 m on a
        /// 1 m rig), so it leaves the frustum while the character is still plainly in shot.
        /// That is why the report was about running UP things: the camera position is
        /// smoothed and its collision snaps inward the instant the rising ground behind
        /// intrudes, so a climbing character leads the camera and drifts to the edge of the
        /// frame -- and settles again once the climb ends, which is why it cleared up on its
        /// own.
        ///
        /// Cost is bones, not meshes. The expensive part of animating an unseen character is
        /// re-skinning it, and that is the renderer's business, not this.
        ///
        /// <b>Public and static so a test drives exactly what the game runs</b>, rather than
        /// a second copy of the rule that can quietly drift from it.
        /// </remarks>
        public static void PrepareAnimator(Animator animator)
        {
            if (animator == null) return;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void BindAnimator(int genderCode)
        {
            _animator = _model.GetComponentInChildren<Animator>();

            if (_animator == null) return;

            PrepareAnimator(_animator);

            RuntimeAnimatorController controller = _catalogue.LocomotionFor(genderCode);

            if (controller != null)
            {
                _animator.runtimeAnimatorController = controller;
            }
        }

        private void BuildNameplate()
        {
            if (_nameplate != null) return;

            bool mine = _entity.IsOwner;

            if (mine && !_catalogue.ShowOwnNameplate) return;

            _nameplate = CharacterNameplate.Create(_visualRoot, _catalogue.NameplateHeight);
            _nameplate.Refresh(Describe());
        }

        // ---- movement, as the picture sees it ---------------------------------------------

        /// <summary>How far the visible transform moved, per second.</summary>
        /// <remarks>Horizontal only. A character stepping off a ledge is falling, not
        /// walking, and a vertical component would put them into a walk cycle in mid-air.</remarks>
        private float MeasureSpeed(float deltaSeconds)
        {
            Vector3 position = transform.position;

            if (!_hasLastPosition || deltaSeconds <= 0f || _catalogue == null)
            {
                _lastPosition = position;
                _hasLastPosition = true;

                return 0f;
            }

            Vector3 delta = position - _lastPosition;

            _lastPosition = position;

            _facing = CharacterVisualRules.FacingFor(delta, deltaSeconds,
                _catalogue.MoveThreshold, _facing);

            return CharacterVisualRules.SpeedFor(delta, deltaSeconds,
                _catalogue.MoveThreshold, _catalogue.ReferenceWalkSpeed);
        }

        private void Apply(float speed01)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            _animator.SetFloat(SpeedHash, speed01);
            _animator.SetBool(DeadHash, _presentedDead);
        }

        private void Face(float deltaSeconds)
        {
            if (_visualRoot == null) return;

            Quaternion target = Quaternion.Euler(0f, _facing, 0f);

            _visualRoot.localRotation = _turnSpeed <= 0f
                ? target
                : Quaternion.RotateTowards(_visualRoot.localRotation, target,
                    _turnSpeed * Mathf.Max(deltaSeconds, 0f));
        }

        // ---- death ------------------------------------------------------------------------

        /// <summary>
        /// Shows what the server already decided.
        /// </summary>
        /// <remarks>
        /// <b>Read-only, in every direction.</b> Aliveness is derived from replicated health,
        /// so this cannot disagree with the server and cannot cause a death. Nothing here
        /// grants a reward, respawns anybody, resets health or despawns an object -- a client
        /// that could do any of those from a health value reaching zero would be a client
        /// that could kill a character by lying about one.
        ///
        /// <b>No death clip exists yet.</b> So the presentation is the animator's own
        /// <c>Dead</c> flag plus a tilt of the visual root -- the child, never the network
        /// object's transform, which the server still owns and still positions.
        /// </remarks>
        private void PresentDeath()
        {
            bool dead = !_entity.IsAlive;

            if (dead == _presentedDead) return;

            _presentedDead = dead;

            if (_visualRoot != null)
            {
                _visualRoot.localRotation = dead
                    ? Quaternion.Euler(_deathTilt, _facing, 0f)
                    : Quaternion.Euler(0f, _facing, 0f);
            }

            _shownSpeed = 0f;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetBool(DeadHash, dead);
                _animator.SetFloat(SpeedHash, 0f);
            }
        }

        // ---- the nameplate's words ----------------------------------------------------------

        /// <summary>What may appear above this character's head.</summary>
        /// <remarks>The rule itself lives in <see cref="CharacterVisualRules"/>, where it can
        /// be tested without spawning anything.</remarks>
        private string Describe()
        {
            return CharacterVisualRules.NameplateFor(
                _entity == null ? string.Empty : _entity.DisplayName);
        }

        private void OnDestroy()
        {
            if (_entity != null) _entity.AttackPerformed -= OnAttackPerformed;

            if (_model != null) DestroyVisual(_model);
        }

        /// <summary>Destroys a presentation object in whichever mode this is running.</summary>
        private static void DestroyVisual(GameObject visual)
        {
            if (visual == null) return;

            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }
    }
}
