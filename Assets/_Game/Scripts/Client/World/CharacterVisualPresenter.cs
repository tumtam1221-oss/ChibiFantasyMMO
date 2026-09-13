using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
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

        /// <summary>
        /// The parameter the swing state is actually wired to.
        /// </summary>
        /// <remarks>
        /// <b>The trigger above is declared and never read.</b> Measured against the
        /// controller: the only transitions into and out of the attack state are on
        /// <c>AttackPhase</c> -- in while it is non-zero, back to locomotion when it returns
        /// to zero -- and nothing in the graph consumes <c>Attack</c>. So for as long as the
        /// presenter only pulled the trigger, the server accepted swings, monsters lost
        /// health, and the character on screen stood perfectly still. The same shape of gap
        /// as the one this remark's neighbour describes, one layer further down.
        ///
        /// The trigger is still pulled, because the prototype presenter reads it and a
        /// controller may yet wire it; the phase is what makes the swing visible today.
        /// </remarks>
        private static readonly int AttackPhaseHash = Animator.StringToHash("AttackPhase");

        // AttackPhase: 0 = locomotion, 1 = the punch, 2 = the guard held between punches.

        /// <summary>
        /// The attack state's own playback rate. Only that state reads it.
        /// </summary>
        /// <remarks>The controller multiplies the attack state's speed by this parameter and
        /// nothing else's: locomotion, idle and death keep their own speed of one however
        /// fast a character swings. Set from the replicated attack speed through the one
        /// conversion the server also uses, see <see cref="AttackSpeed"/>.</remarks>
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

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

        /// <summary>
        /// The model's skinned meshes and, for each, the index of its fist blend shape
        /// (-1 when the mesh has none).
        /// </summary>
        /// <remarks>The production rigs have no finger bones, so a clenched fist is a
        /// blend shape on the mesh rather than a pose in the clip. The presenter closes
        /// the hands for as long as a swing or the guard is being shown and opens them
        /// again when the fight is over, so the walk and idle clips never have to know.</remarks>
        private SkinnedMeshRenderer[] _skins = System.Array.Empty<SkinnedMeshRenderer>();
        private int[] _fistShape = System.Array.Empty<int>();
        private float _fistWeight;

        private Vector3 _lastPosition;

        /// <summary>The number the animator was last shown, which is the eased one.</summary>
        private float _shownSpeed;
        private bool _hasLastPosition;
        private int _builtGenderCode = int.MinValue;
        private bool _presentedDead;
        private float _facing;

        /// <summary>When the swing being shown should give way to the guard.</summary>
        private float _swingUntil = float.NegativeInfinity;

        /// <summary>When the swing being shown reaches its contact frame.</summary>
        private float _swingImpactAt = float.NegativeInfinity;
        private bool _swinging;

        /// <summary>
        /// When the guard held after a punch should drop back to locomotion.
        /// </summary>
        /// <remarks>
        /// A fighter does not drop their hands between punches. Every punch ends in the
        /// guard (phase 2, a held loop) and stays there for a beat, so the next accepted
        /// swing starts from the guard rather than from arms hanging at the sides -- which
        /// is what makes a run of auto-attacks read as one fight and not as a series of
        /// arm movements from idle. The guard drops when nothing has been accepted for a
        /// while, the moment the character starts walking (the guard state cannot walk),
        /// or on death.
        /// </remarks>
        private float _guardUntil = float.NegativeInfinity;
        private bool _guarding;

        /// <summary>
        /// How long the guard is held after the last punch, in seconds.
        /// </summary>
        /// <remarks>A fighter who has just dropped a monster stays in the stance for a
        /// couple of seconds -- the reference sheet's post-combat idle -- and then eases
        /// back to the ordinary idle. The guard still gives way the moment the character
        /// walks, the moment another swing is accepted (into the punch), and on death.</remarks>
        public const float GuardHoldSeconds = 2.0f;

        /// <summary>Above this blend of walking the guard gives way to locomotion.</summary>
        private const float GuardDropsAtSpeed01 = 0.1f;

        /// <summary>Name of the blend shape on the production meshes that curls the fingers into a fist.</summary>
        public const string FistBlendShape = "Fist";

        /// <summary>Seconds the hands take to close into fists, and to open again.</summary>
        /// <remarks>Shorter than the punch's wind-up, so the fists are closed before the
        /// first swing reaches its chamber; long enough that they do not pop.</remarks>
        public const float FistSeconds = 0.12f;

        /// <summary>How far the picture is pushed back by the last blow it took, in metres.</summary>
        private float _recoil;
        private int _lastHealth = int.MinValue;
        private Renderer[] _renderers;
        private GameObject _renderersOf;

        /// <summary>
        /// How far a blow shoves the picture, and how fast that settles.
        /// </summary>
        /// <remarks>Three centimetres, on the visual root only -- the network object that the
        /// server positions is never touched -- decaying over about a tenth of a second. Enough
        /// to read as being hit; nowhere near enough to read as being moved.</remarks>
        private const float RecoilMetres = 0.03f;
        private const float RecoilSettle = 22f;

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

            // A body on the ground does not swing. The server refuses the attack anyway; this
            // is only what stops a stale report from twitching a corpse.
            if (_presentedDead) return;

            // Turned to face what the server says was struck. The heading is replicated
            // state written just before this message, and is read again every frame of the
            // swing (see SettleSwing) in case it arrives a tick behind. The turn itself goes
            // through the same eased Face() every frame uses, so it reads as a turn and not
            // a snap.
            _facing = _entity.SwingFacing;

            // One accepted swing is one complete cycle: wind-up, contact, follow-through,
            // recovery. How fast the cycle plays comes from the character's replicated
            // attack speed through the same conversion the server paced the swing with, so
            // the cycle always fits inside the interval the server enforces and the next
            // accepted swing can only ever arrive after this one has finished. Locomotion
            // is untouched: the rate goes to the attack state's own parameter, never to
            // the animator as a whole.
            float interval = AttackSpeed.IntervalSeconds(_entity.AttackSpeed);

            PlaybackRate = AttackSpeed.PlaybackRate(interval,
                CombatFeedback.BasicAttackClipSeconds, CombatFeedback.MaxPlaybackRate);

            float cycle = CombatFeedback.BasicAttackClipSeconds / PlaybackRate;

            // A swing that somehow arrives while the last is still being drawn -- which the
            // server's pacing makes impossible for a basic attack, and a later skill might
            // not -- is never allowed to cut a punch off before its fist lands: until the
            // contact frame the picture keeps the punch it is throwing (the blow is still
            // counted, and its number still lands); after contact, in the recovery, the
            // next punch starts from the top. Either way it is counted so a test can see it.
            bool beforeImpact = _swinging && Time.time < _swingImpactAt;

            if (_swinging) SwingsRestarted++;
            if (beforeImpact) SwingsAbsorbed++;

            _swinging = true;
            _guarding = false;
            _swingUntil = Time.time + cycle;
            _swingImpactAt = Time.time + CombatFeedback.BasicAttackImpactSeconds / PlaybackRate;

            CombatFeedback feedback = CombatFeedback.Current;

            if (feedback != null)
            {
                // What a monster this swing is about to hit will hold its number for: the
                // clip's contact frame at the rate it is actually playing.
                feedback.NoteSwing(CombatFeedback.BasicAttackImpactSeconds / PlaybackRate,
                    Time.time);
                feedback.Play(CombatSound.PlayerSwing);
            }

            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            _animator.SetFloat(AttackSpeedHash, PlaybackRate);

            // Mid-punch and not yet at contact: the animator is left alone -- the punch it is
            // drawing lands first. The trigger and phase are already what they need to be.
            if (beforeImpact) return;

            _animator.SetTrigger(AttackHash);

            // From the guard (phase 2) or from a punch's recovery (phase 1, past contact),
            // the next punch starts from its first frame rather than blending in halfway.
            if (_animator.GetInteger(AttackPhaseHash) != 0)
            {
                AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);

                _animator.Play(current.fullPathHash, 0, 0f);
            }

            _animator.SetInteger(AttackPhaseHash, 1);
        }

        /// <summary>The rate the current or last swing was played at. For tests.</summary>
        public float PlaybackRate { get; private set; } = 1f;

        /// <summary>How many swings arrived while one was still being drawn. For tests.</summary>
        public int SwingsRestarted { get; private set; }

        /// <summary>
        /// How many of those arrived before the drawn punch had landed, and so were absorbed
        /// into it instead of restarting the animator. For tests.
        /// </summary>
        public int SwingsAbsorbed { get; private set; }

        /// <summary>Whether a swing is being shown right now. For tests.</summary>
        public bool IsSwinging => _swinging;

        /// <summary>How many blows this character has been seen to take. For tests.</summary>
        public int HitsShown { get; private set; }

        /// <summary>
        /// Ends the swing when its time is up, handing the animator back to locomotion.
        /// </summary>
        private void SettleSwing()
        {
            if (!_swinging) return;

            // the heading may land a tick after the swing did; keep pointing at the latest
            if (!_presentedDead) _facing = _entity.SwingFacing;

            if (Time.time < _swingUntil) return;

            _swinging = false;

            // Into the guard, and hold it: the clip ends on the guard pose, and phase 2 is
            // the looped guard state the controller blends to from there.
            _guarding = true;
            _guardUntil = Time.time + GuardHoldSeconds;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetInteger(AttackPhaseHash, 2);
            }
        }

        /// <summary>Whether the fighting guard is being held right now. For tests.</summary>
        public bool IsGuarding => _guarding;

        /// <summary>How closed the hands are, 0 (open) to 100 (fists). For tests.</summary>
        public float FistWeight => _fistWeight;

        /// <summary>
        /// The next fist blend-shape weight: eased toward closed while fighting, toward
        /// open otherwise, at a fixed rate so a fight that ends mid-close still reads.
        /// </summary>
        public static float NextFistWeight(float current, bool fighting, float deltaSeconds)
        {
            float target = fighting ? 100f : 0f;

            return Mathf.MoveTowards(current, target, 100f * Mathf.Max(0f, deltaSeconds) / FistSeconds);
        }

        /// <summary>
        /// Closes the hands into fists while a swing or the guard is shown, opens them after.
        /// </summary>
        private void SettleFists(float deltaSeconds)
        {
            bool fighting = (_swinging || _guarding) && !_presentedDead;
            float next = NextFistWeight(_fistWeight, fighting, deltaSeconds);

            if (Mathf.Approximately(next, _fistWeight)) return;

            _fistWeight = next;
            ApplyFists();
        }

        private void ApplyFists()
        {
            for (var i = 0; i < _skins.Length; i++)
            {
                if (_fistShape[i] < 0 || _skins[i] == null) continue;

                _skins[i].SetBlendShapeWeight(_fistShape[i], _fistWeight);
            }
        }

        /// <summary>
        /// Drops the guard when the fight has gone quiet, the character walks off, or dies.
        /// </summary>
        private void SettleGuard(float speed01)
        {
            if (!_guarding) return;

            if (Time.time < _guardUntil && speed01 <= GuardDropsAtSpeed01 && !_presentedDead) return;

            _guarding = false;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetInteger(AttackPhaseHash, 0);
            }
        }

        /// <summary>
        /// Draws a blow this character took, the moment the server's health says it did.
        /// </summary>
        /// <remarks>
        /// <b>No delay on this side.</b> The slime's swing is authored so the server applies
        /// its damage at the clip's own moment of contact -- the wind-up is the same 0.8 s the
        /// picture shows -- so by the time the health drops here the jump has already landed.
        /// The number, the flash and the shove are drawn at once.
        ///
        /// <b>Presentation only, and only presentation.</b> The health is read, never
        /// written; the shove moves the visual root and never the object the server owns.
        /// </remarks>
        private void PresentHitsTaken()
        {
            int health = _entity.Health;

            if (_lastHealth == int.MinValue)
            {
                _lastHealth = health;

                return;
            }

            if (health >= _lastHealth)
            {
                _lastHealth = health;

                return;
            }

            int amount = _lastHealth - health;

            _lastHealth = health;

            HitsShown++;
            _recoil = RecoilMetres;

            CombatFeedback feedback = CombatFeedback.Current;

            if (feedback == null || _model == null) return;

            float head = HeadHeight();
            Vector3 where = transform.position;

            feedback.ShowDamage(where + new Vector3(0f, head + 0.08f, 0f), amount,
                DamageNumberKind.Taken);
            feedback.ShowImpact(where + new Vector3(0f, head * 0.6f, 0f), ImpactKind.Soft);

            if (_renderersOf != _model)
            {
                _renderersOf = _model;
                _renderers = _model.GetComponentsInChildren<Renderer>(true);
            }

            feedback.Flash(_renderers, new Color(1.6f, 1.25f, 1.2f), CombatFeedback.FlashSeconds);
            feedback.Play(CombatSound.PlayerHurt);
        }

        /// <summary>How tall the model stands, from its renderers.</summary>
        private float HeadHeight()
        {
            if (_renderersOf != _model)
            {
                _renderersOf = _model;
                _renderers = _model.GetComponentsInChildren<Renderer>(true);
            }

            var top = 0f;

            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;

                float y = _renderers[i].bounds.max.y - transform.position.y;

                if (y > top) top = y;
            }

            return top > 0.2f ? top : 1.2f;
        }

        /// <summary>Lets the shove from a blow settle back to nothing.</summary>
        private void SettleRecoil(float deltaSeconds)
        {
            if (_visualRoot == null) return;

            if (_recoil <= 0.0005f)
            {
                if (_recoil != 0f)
                {
                    _recoil = 0f;
                    _visualRoot.localPosition = Vector3.zero;
                }

                return;
            }

            _recoil *= Mathf.Exp(-RecoilSettle * Mathf.Max(deltaSeconds, 0f));

            // straight back, along the way the picture is facing
            Vector3 back = Quaternion.Euler(0f, _facing, 0f) * Vector3.back;

            _visualRoot.localPosition = back * _recoil;
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

            PresentHitsTaken();
            PresentDeath();
            SettleSwing();
            SettleGuard(speed);
            SettleFists(deltaSeconds);
            SettleRecoil(deltaSeconds);

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
                _skins = System.Array.Empty<SkinnedMeshRenderer>();
                _fistShape = System.Array.Empty<int>();
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

            // Find the fist blend shape on each mesh once; a mesh without one is left alone.
            _skins = _model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _fistShape = new int[_skins.Length];

            for (var i = 0; i < _skins.Length; i++)
            {
                Mesh mesh = _skins[i].sharedMesh;
                _fistShape[i] = mesh == null ? -1 : mesh.GetBlendShapeIndex(FistBlendShape);
            }

            ApplyFists();

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
                _visualRoot.localPosition = Vector3.zero;
            }

            _shownSpeed = 0f;
            _recoil = 0f;

            // A swing in progress ends with the character, and a revived one starts clean:
            // the phase is cleared either way so the animator's graph has nothing pending
            // to fall back into.
            _swinging = false;
            _guarding = false;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetBool(DeadHash, dead);
                _animator.SetFloat(SpeedHash, 0f);
                _animator.SetInteger(AttackPhaseHash, 0);
                _animator.ResetTrigger(AttackHash);
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
