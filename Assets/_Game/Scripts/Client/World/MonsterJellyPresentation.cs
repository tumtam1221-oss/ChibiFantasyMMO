using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// The parts of a slime that are alive but are not animation: bubbles rising out of it,
    /// the swell one makes on its way out, and blinking.
    /// </summary>
    /// <remarks>
    /// <b>Why any of this is code at all.</b> A bubble's life -- forming inside, pushing up,
    /// emerging, floating, shrinking, gone -- runs for about three seconds. The clips it would
    /// have to live in are between four tenths of a second and one and four fifths, and they
    /// loop. Baked into the hop, a bubble would be born and die once per hop, forever, in time
    /// with the bouncing; baked into the idle it would stop dead the moment the slime moved.
    /// The lifecycle has to outlive the clip, so it cannot be a clip.
    ///
    /// <b>What it costs.</b> Per slime per frame: three springs of three floats, three
    /// transform writes, one blend-shape write, and a texture swap only on the four frames of
    /// a blink. No allocation after Awake, no physics, no per-vertex work, nothing replicated.
    /// This is cosmetic and client-only; the server neither knows nor cares whether a slime is
    /// blowing bubbles.
    ///
    /// <b>Why it is on the prefab rather than added at spawn.</b> It needs three face textures,
    /// and a component added by code cannot carry serialized references. Sitting on the prefab
    /// it also means the presenter did not have to learn anything new.
    ///
    /// <b>Why the clips still key the bubble bones to zero scale.</b> So that a model with this
    /// component missing or disabled shows no bubbles, rather than three balls parked inside
    /// its head. The Animator writes them small during its update; this runs afterwards, in
    /// LateUpdate, and overrides.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MonsterJellyPresentation : MonoBehaviour
    {
        /// <summary>A bubble's whole cycle, including the part where there is no bubble.</summary>
        private const float Lifetime = 5.6f;

        /// <summary>
        /// How much of that cycle a bubble actually exists for. The rest is the slime not
        /// blowing one.
        /// </summary>
        /// <remarks>
        /// This fraction is what keeps three bubbles from reading as a fountain. At 1.0 every
        /// bubble is always somewhere in its life and there are two or three on screen at all
        /// times; at 0.55, with the three of them out of phase, the usual picture is one and
        /// the occasional overlap is what makes it feel unplanned.
        /// </remarks>
        private const float Alive = 0.55f;

        /// <summary>Where in its life a bubble has reached the surface and broken through.</summary>
        private const float Emerges = 0.36f;

        /// <summary>How far above the crown a bubble climbs before it is gone, in metres.</summary>
        private const float RiseHeight = 0.26f;

        private const float BlinkShortest = 2.6f;
        private const float BlinkLongest = 5.0f;

        /// <summary>Frames of the blink, at the clip rate the rest of the slime runs at.</summary>
        private const float BlinkStep = 1f / 30f;

        [Header("Face")]
        [SerializeField] private Texture _eyesOpen;
        [SerializeField] private Texture _eyesHalf;
        [SerializeField] private Texture _eyesShut;

        [Header("Body")]
        [Tooltip("Height of the top of the body above its feet, in metres.")]
        [SerializeField] private float _crownHeight = 0.288f;

        private SkinnedMeshRenderer _skin;
        private Transform _body;
        private Transform[] _bubbles;
        private Vector3[] _birth;
        private float[] _phase;
        private float[] _lifetime;
        private float[] _size;
        private Vector3[] _follow;
        private Vector3[] _followVelocity;
        private int _bulge = -1;
        private MaterialPropertyBlock _block;
        private int _baseMapId;

        private float _clock;
        private float _nextBlink;
        private float _blinkAt = float.NegativeInfinity;
        private int _blinksLeft;
        private int _lastFace = -1;
        private uint _random;
        private bool _ready;

        /// <summary>Set before the first tick to make a slime's timing reproducible.</summary>
        public void Seed(int seed)
        {
            Prepare();

            _random = (uint)(seed * 2654435761u) | 1u;

            if (_bubbles == null) return;

            for (var i = 0; i < _bubbles.Length; i++)
            {
                _phase[i] = Next();
            }

            _nextBlink = BlinkShortest + Next() * (BlinkLongest - BlinkShortest);
        }

        private float Next()
        {
            // xorshift32: no allocation, no UnityEngine.Random global state to disturb, and
            // the same slime behaves the same way twice.
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;

            return (_random & 0xFFFFFF) / (float)0x1000000;
        }

        private void Awake()
        {
            Prepare();
        }

        /// <summary>
        /// Finds everything this drives, once.
        /// </summary>
        /// <remarks>
        /// Called from Awake and again from the first Seed or Tick, because Awake is not
        /// guaranteed to have run: a prefab instantiated by an edit-mode test never gets one,
        /// and the whole component silently did nothing there -- bubbles frozen at full size,
        /// no blink, no swell, and every test of it passing vacuously until they were written
        /// strictly enough to notice.
        /// </remarks>
        private void Prepare()
        {
            if (_ready) return;

            _ready = true;

            _skin = GetComponentInChildren<SkinnedMeshRenderer>(true);

            if (_skin == null)
            {
                enabled = false;
                return;
            }

            var found = new System.Collections.Generic.List<Transform>();

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Body") _body = t;
                if (t.name.StartsWith("Bubble_")) found.Add(t);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            _bubbles = found.ToArray();
            _birth = new Vector3[_bubbles.Length];
            _phase = new float[_bubbles.Length];
            _lifetime = new float[_bubbles.Length];
            _size = new float[_bubbles.Length];
            _follow = new Vector3[_bubbles.Length];
            _followVelocity = new Vector3[_bubbles.Length];

            for (var i = 0; i < _bubbles.Length; i++)
            {
                // where the model says this bubble is born, in the model's own space
                _birth[i] = transform.InverseTransformPoint(_bubbles[i].position);

                // no two the same: a little difference in how long they last and how big they
                // get is most of what stops three bubbles reading as one effect
                _lifetime[i] = Lifetime * (0.86f + 0.14f * i);
                _size[i] = 1f;
            }

            Mesh mesh = _skin.sharedMesh;

            if (mesh != null)
            {
                _bulge = IndexOfShape(mesh, "Crown_Bulge");

                // The bubbles climb well above the bind pose, and skinned bounds are baked at
                // import. Left alone, a slime whose body is off the bottom of the screen takes
                // its risen bubbles with it -- they vanish a frame early, together, which reads
                // as a bug rather than as popping.
                //
                // The conversion is not decoration. This model imports with the mesh stored a
                // hundred times smaller than it is drawn and the renderer scaled back up by
                // the same hundred, so local bounds are in hundredths of a metre. Expanding
                // them by a figure in metres gave every slime a culling box thirty-nine metres
                // across, which nothing looks wrong about until you wonder why distant slimes
                // are still being skinned.
                float perMetre = _skin.transform
                    .InverseTransformVector(transform.TransformVector(Vector3.up)).magnitude;

                Bounds local = _skin.localBounds;

                local.Expand(RiseHeight * 2f * perMetre);
                _skin.localBounds = local;
            }

            _block = new MaterialPropertyBlock();
            _baseMapId = Shader.PropertyToID("_BaseMap");

            // Out of sight before anything is drawn. The clips deliberately do not key these
            // bones -- a bone keyed to zero scale on the exported frame writes a degenerate
            // bind matrix and Unity then imports a bone that deforms nothing -- so hiding them
            // on the first frame is this component's job.
            for (var i = 0; i < _bubbles.Length; i++) _bubbles[i].localScale = Vector3.zero;

            if (_random == 0u) Seed(GetInstanceID());
        }

        private static int IndexOfShape(Mesh mesh, string name)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i) == name) return i;
            }

            return -1;
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances everything by <paramref name="deltaTime"/>. Public so the behaviour can be
        /// stepped and measured without entering play mode.
        /// </summary>
        public void Tick(float deltaTime)
        {
            Prepare();

            if (_skin == null || _bubbles == null) return;

            _clock += deltaTime;

            // How far the body has moved from where it rests. Everything the bubbles do about
            // the hop and the attack comes from this one number.
            Vector3 bodyOffset = Vector3.zero;

            if (_body != null)
            {
                // the Body bone rests at the model's own origin, so its position in model
                // space IS how far the slime has moved from standing
                bodyOffset = transform.InverseTransformPoint(_body.position);
            }

            var swell = 0f;

            for (var i = 0; i < _bubbles.Length; i++)
            {
                float life = _lifetime[i];
                float cycle = Mathf.Repeat(_clock + _phase[i] * life, life) / life;
                bool exists = cycle < Alive;
                float age = exists ? cycle / Alive : 1f;

                Vector3 local = Shape(i, age, out float scale, out Vector3 wobble);

                if (!exists)
                {
                    scale = 0f;
                    local = _birth[i];
                    _follow[i] = bodyOffset;
                    _followVelocity[i] = Vector3.zero;
                }

                // Inside the jelly a bubble is carried by the body exactly; once it is out it
                // is on its own and has to be caught up with. The spring is what makes it hang
                // back when the slime jumps out from under it and overshoot at the top.
                float outside = Mathf.Clamp01((age - Emerges) / 0.10f);
                float omega = 13f - 2f * i;
                float zeta = 0.26f;

                Vector3 accel = (bodyOffset - _follow[i]) * (omega * omega)
                    - _followVelocity[i] * (2f * zeta * omega);

                _followVelocity[i] += accel * deltaTime;
                _follow[i] += _followVelocity[i] * deltaTime;

                Vector3 carried = Vector3.Lerp(bodyOffset, _follow[i], outside);

                _bubbles[i].position = transform.TransformPoint(local + carried);
                SetScale(_bubbles[i], scale, wobble);

                // the surface reacting just before one breaks through
                float push = exists ? 1f - Mathf.Abs(age - Emerges) / 0.13f : 0f;

                if (push > swell) swell = push;
            }

            if (_bulge >= 0)
            {
                // capped well below full. The shape key at 100 is a soft peak; at 70 it is a
                // swell, and a swell is what a bubble under the surface makes.
                _skin.SetBlendShapeWeight(_bulge, Mathf.Clamp01(swell) * 70f);
            }

            Blink();
        }

        /// <summary>
        /// Where a bubble is and how big, at <paramref name="age"/> through its life.
        /// </summary>
        /// <remarks>
        /// Form, push, emerge, rise, shrink, gone -- and nothing left behind. The scale curve
        /// ends at exactly zero, which is the difference between a bubble that disappears and a
        /// permanent speck hanging over the slime's head.
        /// </remarks>
        private Vector3 Shape(int index, float age, out float scale, out Vector3 wobble)
        {
            float drift = Mathf.Sin((age + index * 0.37f) * Mathf.PI * 2.4f + index)
                * 0.018f * Mathf.Clamp01((age - Emerges) * 3f);

            // height: creeps up through the body, then climbs away above the crown
            float inside = Mathf.SmoothStep(_birth[index].y, _crownHeight * 0.92f,
                Mathf.Clamp01(age / Emerges));
            float above = _crownHeight + RiseHeight
                * Mathf.Pow(Mathf.Clamp01((age - Emerges) / (1f - Emerges)), 0.72f);
            float height = age < Emerges ? inside : Mathf.Max(inside, above);

            // size: nearly nothing while it is forming, biggest as it clears the surface, then
            // steadily smaller until it is gone
            float grow = Mathf.SmoothStep(0.10f, 1f, Mathf.Clamp01(age / (Emerges * 0.92f)));
            float fade = 1f - Mathf.Clamp01((age - Emerges) / (1f - Emerges));

            scale = grow * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fade * 1.25f));
            scale *= 0.92f + 0.08f * index;

            // a very small wobble, different for each, so they are soft rather than hard
            float beat = _clock * (5.2f + index * 1.3f) + index * 2f;
            float amount = 0.055f * Mathf.Clamp01((age - Emerges) * 4f);

            wobble = new Vector3(1f - amount * Mathf.Sin(beat) * 0.5f,
                1f + amount * Mathf.Sin(beat),
                1f - amount * Mathf.Sin(beat) * 0.5f);

            return new Vector3(_birth[index].x * (1f - Mathf.Clamp01(age / Emerges)) + drift,
                height,
                _birth[index].z * (1f - Mathf.Clamp01(age / Emerges)));
        }

        /// <summary>
        /// Scales a bubble bone, stretching it along whichever of its own axes points up.
        /// </summary>
        /// <remarks>
        /// Probed rather than assumed. The importer leaves the armature under a rotation, so
        /// which local axis is "up" is a property of the exported file and not something worth
        /// being confidently wrong about.
        /// </remarks>
        private void SetScale(Transform bone, float scale, Vector3 wobble)
        {
            Vector3 up = bone.InverseTransformDirection(transform.up);

            float ax = Mathf.Abs(up.x), ay = Mathf.Abs(up.y), az = Mathf.Abs(up.z);
            var along = ay >= ax && ay >= az ? 1 : (ax >= az ? 0 : 2);

            var result = new Vector3(wobble.x, wobble.x, wobble.x) * scale;

            result[along] = wobble.y * scale;

            bone.localScale = result;
        }

        private void Blink()
        {
            if (_eyesOpen == null || _eyesShut == null) return;

            if (_clock >= _nextBlink && _clock > _blinkAt + 8f * BlinkStep)
            {
                _blinkAt = _clock;

                // A second blink straight after the first, now and again. The follow-up has
                // to book a LONG next gap, not another short one: booking another short gap
                // makes the pair into a run of three, which is a twitch rather than a blink.
                if (_blinksLeft > 0)
                {
                    _blinksLeft--;
                    _nextBlink = _clock + BlinkShortest
                        + Next() * (BlinkLongest - BlinkShortest);
                }
                else
                {
                    bool twice = Next() < 0.18f;

                    _blinksLeft = twice ? 1 : 0;
                    _nextBlink = _clock + (twice ? 7f * BlinkStep
                        : BlinkShortest + Next() * (BlinkLongest - BlinkShortest));
                }
            }

            float since = _clock - _blinkAt;
            var frame = (int)(since / BlinkStep);

            // open, half, shut, shut, half, open: six frames, a fifth of a second
            int face = frame == 1 || frame == 4 ? 1 : (frame == 2 || frame == 3 ? 2 : 0);

            if (face == _lastFace) return;

            _lastFace = face;

            Texture wanted = _eyesShut;

            if (face == 0) wanted = _eyesOpen;
            else if (face == 1 && _eyesHalf != null) wanted = _eyesHalf;

            _skin.GetPropertyBlock(_block);
            _block.SetTexture(_baseMapId, wanted);
            _skin.SetPropertyBlock(_block);
        }
    }
}
