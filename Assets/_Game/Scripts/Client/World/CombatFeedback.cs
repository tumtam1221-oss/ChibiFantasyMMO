using System.Collections.Generic;
using ChibiFantasy.Client.Combat;
using TMPro;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>Which side of a blow a damage number is telling the player about.</summary>
    public enum DamageNumberKind
    {
        /// <summary>Damage something in the world took.</summary>
        Dealt = 0,

        /// <summary>Damage the player's own character took.</summary>
        Taken = 1
    }

    /// <summary>What kind of contact an impact effect is standing in for.</summary>
    public enum ImpactKind
    {
        /// <summary>A weapon landing on something: bright, sharp, brief.</summary>
        Physical = 0,

        /// <summary>Something soft landing on the player: dimmer, rounder.</summary>
        Soft = 1
    }

    /// <summary>The sounds combat can ask for. Each is a hook; none is required to exist.</summary>
    public enum CombatSound
    {
        PlayerSwing = 0,
        PlayerHitMonster = 1,
        MonsterAttack = 2,
        PlayerHurt = 3,
        MonsterDeath = 4
    }

    /// <summary>
    /// The small things that make a blow readable: a number, a spark, a flash, a sound.
    /// </summary>
    /// <remarks>
    /// <b>Strictly downstream.</b> Everything here is asked for by a presenter that has
    /// already read an authoritative change -- a replicated health value dropping, a swing
    /// the server accepted -- and nothing here writes to anything gameplay reads. Delete this
    /// component and every fight resolves identically; the fight is merely harder to see.
    ///
    /// <b>One of these per world, ticked once.</b> Every number and every spark lives in a
    /// pool owned here and is advanced from a single <see cref="LateUpdate"/>. There is no
    /// component on a damage number, no Update on a spark, and nothing is instantiated after
    /// the pools are built. This is an MMO: a camp of slimes under three players is a hit
    /// every few frames, and a GameObject per hit that outlives its half second is how a
    /// fight turns into a hierarchy full of corpses.
    ///
    /// <b>Why it is reachable through <see cref="Current"/>.</b> The character presenter sits
    /// on a networked prefab and is instantiated by the network layer, so nothing composes it
    /// by hand; the monster presenter is composed by the bootstrap. Both need the same pools.
    /// The bootstrap sets <see cref="Current"/> when it composes this, tests set it to their
    /// own, and a presenter that finds it null draws nothing and is not wrong to.
    ///
    /// <b>What it reuses.</b> The hook asset is the prototype's
    /// <see cref="CombatPresentationConfig"/> -- one prefab and one clip per category -- so
    /// there is one place art plugs in, not two. The world-space text is built the way the
    /// nameplate builds its own. The flash goes through a <see cref="MaterialPropertyBlock"/>,
    /// which is how the slime's blink already talks to its material, and touches no material
    /// asset and instances none.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CombatFeedback : MonoBehaviour
    {
        /// <summary>
        /// How far into the basic attack clip, at normal speed, the fist reaches the target.
        /// </summary>
        /// <remarks>Measured off the imported clip, not typed. The clip is authored from the
        /// punch reference sheet's eight poses: guard (frame 0), wind-up early (3), wind-up
        /// max with the fist chambered at the hip and the torso turned away (6), punch start
        /// with the hips driving first (9), IMPACT (12), follow-through (14), retract along a
        /// high inside path (18), back to guard (23). Frame 12 is contact, and this is it in
        /// seconds. The server applies the damage the instant it accepts the swing, so the
        /// presentation of the hit is held back by this -- divided by the rate the clip is
        /// playing at, see <see cref="NoteSwing"/> -- so the number appears when the fist
        /// arrives and not while the body is still loading.
        /// </remarks>
        public const float BasicAttackImpactSeconds = 21f / 30f;   // TRIAL: Mixamo Cross Punch (source frames 14-40, arm locked out at source f34 = clip f20, +1 frame for the blend-in lag measured in play)

        /// <summary>
        /// How long one complete attack cycle takes at normal speed: sway back and wind-up,
        /// weight transfer and punch, impact, retract, and the sway back to guard.
        /// </summary>
        /// <remarks>The whole clip, because the clip was authored to be the whole cycle:
        /// it starts and ends on the guard pose, so nothing is cut and nothing is blended
        /// away. At the default attack speed the cycle fits inside the one-second interval
        /// with a beat in the guard to spare; faster characters play it faster, see
        /// <see cref="MaxPlaybackRate"/>.</remarks>
        public const float BasicAttackClipSeconds = 26f / 30f;     // TRIAL: Cross Punch, source frames 14-40 (fits the 0.5 s interval at ASPD 200 at the 1.75x cap)

        /// <summary>
        /// The fastest the attack clip is ever played.
        /// </summary>
        /// <remarks>At this rate contact still lands on its own frame and the wind-up still
        /// reads; past it the cycle is a vibration. The attack-speed ceiling the content
        /// carries (two swings a second) is exactly the rate at which one cycle at this
        /// speed fills the interval, so nothing the server allows ever needs more than
        /// this, and a faster character in a later phase gets a shorter clip rather than a
        /// faster one.</remarks>
        public const float MaxPlaybackRate = 1.75f;

        /// <summary>How long a flash holds, in seconds. Brief enough to read as a flinch.</summary>
        public const float FlashSeconds = 0.11f;

        private const int NumberPoolSize = 24;
        private const int ImpactPoolSize = 8;
        private const float NumberLifetime = 0.85f;
        private const float NumberRise = 0.30f;
        private const float NumberFontSize = 1.15f;

        /// <summary>
        /// Records that a swing has just been drawn, and when its contact will be.
        /// </summary>
        /// <remarks>
        /// <b>Why a monster asks the feedback rather than the attacker.</b> A monster's
        /// picture learns it was hit from its replicated health, which names no attacker;
        /// the delay it should hold that blow for depends on how fast the attacker's clip is
        /// playing. The swing and the health drop arrive in the same server tick, so the
        /// most recent swing drawn on this client is the one that caused the drop it is
        /// about to see. Two attackers on one target within the same tick share the later
        /// one's timing -- a few frames on a pile-on, and the number is still the server's.
        /// </remarks>
        /// <param name="impactSeconds">Seconds from now until the clip makes contact.</param>
        /// <param name="now">The caller's clock, so a test needs no engine time.</param>
        public void NoteSwing(float impactSeconds, float now)
        {
            _lastSwingImpact = impactSeconds < 0f ? 0f : impactSeconds;
            _lastSwingAt = now;
        }

        /// <summary>
        /// How long a blow observed now should be held before it is drawn.
        /// </summary>
        /// <remarks>The last swing's contact delay while that swing is fresh; otherwise the
        /// clip's own contact at normal speed, which is what a blow with no swing on this
        /// client to explain it (a skill, a swing drawn before the feedback existed) is
        /// held for.</remarks>
        public float ImpactDelayAt(float now)
        {
            return now - _lastSwingAt <= SwingFreshSeconds
                ? _lastSwingImpact
                : BasicAttackImpactSeconds;
        }

        /// <summary>How long after a swing is drawn a health drop is taken to be its blow.</summary>
        private const float SwingFreshSeconds = 0.3f;

        private float _lastSwingImpact = BasicAttackImpactSeconds;
        private float _lastSwingAt = float.NegativeInfinity;

        /// <summary>The feedback the presenters in this world draw through. May be null.</summary>
        public static CombatFeedback Current { get; private set; }

        [SerializeField] private CombatPresentationConfig _config;

        private struct Number
        {
            public TextMeshPro Label;
            public Transform Transform;
            public Vector3 Origin;
            public float Age;
            public bool Live;
            public Color Colour;
        }

        private struct Impact
        {
            public ParticleSystem System;
            public float Age;
            public bool Live;
        }

        private struct Flashing
        {
            public Renderer Renderer;
            public Color Rest;
            public float Age;
            public float Seconds;
        }

        private Number[] _numbers;
        private Impact[] _impacts;
        private readonly List<Flashing> _flashes = new List<Flashing>(16);
        private MaterialPropertyBlock _block;
        private AudioSource _audio;
        private Camera _camera;
        private Material _numberMaterial;
        private Material _sparkMaterial;
        private int _nextNumber;
        private int _nextImpact;
        private int _side = 1;
        private bool _built;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Numbers shown so far. For tests.</summary>
        public int NumbersShown { get; private set; }

        /// <summary>Impacts played so far. For tests.</summary>
        public int ImpactsPlayed { get; private set; }

        /// <summary>Flashes started so far. For tests.</summary>
        public int FlashesStarted { get; private set; }

        /// <summary>Sounds asked for so far, whether or not a clip existed. For tests.</summary>
        public int SoundsRequested { get; private set; }

        /// <summary>Sounds actually played, which needs a clip. For tests.</summary>
        public int SoundsPlayed { get; private set; }

        /// <summary>Numbers on screen right now. For tests.</summary>
        public int LiveNumbers
        {
            get
            {
                if (_numbers == null) return 0;

                var live = 0;

                for (var i = 0; i < _numbers.Length; i++) if (_numbers[i].Live) live++;

                return live;
            }
        }

        /// <summary>Renderers mid-flash right now. For tests.</summary>
        public int LiveFlashes => _flashes.Count;

        /// <summary>Supplies the art hooks and makes this the world's feedback.</summary>
        public void Compose(CombatPresentationConfig config)
        {
            _config = config;
            Current = this;
        }

        private void OnEnable()
        {
            if (Current == null) Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;

            if (_numberMaterial != null) Destroy(_numberMaterial);
            if (_sparkMaterial != null) Destroy(_sparkMaterial);
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        // ---- damage numbers -----------------------------------------------------------

        /// <summary>
        /// Puts a number in the world at a point and lets it drift up and fade.
        /// </summary>
        /// <remarks>
        /// Alternates left and right of the point it is given, so two blows in quick
        /// succession do not print on top of each other, and so a monster's numbers climb
        /// past its health bar to one side of it rather than through it.
        /// </remarks>
        public void ShowDamage(Vector3 worldPosition, int amount, DamageNumberKind kind)
        {
            if (amount <= 0) return;

            Build();

            ref Number number = ref _numbers[_nextNumber];

            _nextNumber = (_nextNumber + 1) % _numbers.Length;
            _side = -_side;

            number.Live = true;
            number.Age = 0f;
            number.Origin = worldPosition + new Vector3(0.10f * _side, 0f, 0f);
            number.Colour = kind == DamageNumberKind.Taken
                ? new Color(1.00f, 0.42f, 0.36f)
                : new Color(1.00f, 0.95f, 0.70f);
            number.Label.text = amount.ToString();
            number.Label.color = number.Colour;
            number.Transform.position = number.Origin;
            number.Transform.localScale = Vector3.one * 1.3f;
            number.Label.gameObject.SetActive(true);

            NumbersShown++;
        }

        // ---- impact sparks ------------------------------------------------------------

        /// <summary>Plays one short burst at a point.</summary>
        public void ShowImpact(Vector3 worldPosition, ImpactKind kind)
        {
            Build();

            ref Impact impact = ref _impacts[_nextImpact];

            _nextImpact = (_nextImpact + 1) % _impacts.Length;

            impact.Live = true;
            impact.Age = 0f;
            impact.System.transform.position = worldPosition;

            ParticleSystem.MainModule main = impact.System.main;

            main.startColor = kind == ImpactKind.Soft
                ? new Color(0.85f, 0.92f, 1.0f, 0.85f)
                : new Color(1.0f, 0.98f, 0.80f, 1.0f);
            main.startSize = kind == ImpactKind.Soft ? 0.09f : 0.07f;

            impact.System.gameObject.SetActive(true);
            impact.System.Play(true);

            ImpactsPlayed++;
        }

        // ---- flash --------------------------------------------------------------------

        /// <summary>
        /// Brightens a set of renderers for a moment, through a property block.
        /// </summary>
        /// <remarks>
        /// The rest colour is read from the shared material and written back at the end, so
        /// nothing about the material asset changes and no instance of it is ever made. A
        /// renderer already flashing is restarted rather than stacked, so a flurry of hits
        /// cannot leave one permanently lit.
        /// </remarks>
        public void Flash(Renderer[] renderers, Color colour, float seconds)
        {
            if (renderers == null) return;

            Build();

            for (var i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null) continue;

                var index = -1;

                for (var f = 0; f < _flashes.Count; f++)
                {
                    if (_flashes[f].Renderer == renderer) { index = f; break; }
                }

                if (index < 0)
                {
                    Material shared = renderer.sharedMaterial;
                    Color rest = shared != null && shared.HasProperty(BaseColorId)
                        ? shared.GetColor(BaseColorId)
                        : Color.white;

                    _flashes.Add(new Flashing
                        { Renderer = renderer, Rest = rest, Age = 0f, Seconds = seconds });
                    index = _flashes.Count - 1;
                }
                else
                {
                    Flashing again = _flashes[index];

                    again.Age = 0f;
                    again.Seconds = seconds;
                    _flashes[index] = again;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, colour);
                renderer.SetPropertyBlock(_block);
            }

            FlashesStarted++;
        }

        // ---- sound --------------------------------------------------------------------

        /// <summary>Plays the clip authored for a sound, if one is.</summary>
        /// <remarks>A hook with nothing in it is silent, not an error. No placeholder is
        /// substituted: this project has no combat audio yet, and a wrong sound teaches the
        /// ear the wrong thing faster than no sound does.</remarks>
        public void Play(CombatSound sound)
        {
            SoundsRequested++;

            AudioClip clip = ClipFor(sound);

            if (clip == null) return;

            Build();

            _audio.PlayOneShot(clip, _config != null ? _config.sfxVolume : 0.8f);

            SoundsPlayed++;
        }

        private AudioClip ClipFor(CombatSound sound)
        {
            if (_config == null) return null;

            switch (sound)
            {
                case CombatSound.PlayerSwing: return _config.attackSfx;
                case CombatSound.PlayerHitMonster: return _config.hitSfx;
                case CombatSound.MonsterAttack: return _config.monsterAttackSfx;
                case CombatSound.PlayerHurt: return _config.hurtSfx;
                case CombatSound.MonsterDeath: return _config.deathSfx;
                default: return null;
            }
        }

        // ---- the tick -----------------------------------------------------------------

        /// <summary>Advances every live number, spark and flash. Public for tests.</summary>
        public void Tick(float deltaTime)
        {
            if (!_built) return;

            if (_camera == null) _camera = Camera.main;

            for (var i = 0; i < _numbers.Length; i++)
            {
                ref Number number = ref _numbers[i];

                if (!number.Live) continue;

                number.Age += deltaTime;

                float t = number.Age / NumberLifetime;

                if (t >= 1f)
                {
                    number.Live = false;
                    number.Label.gameObject.SetActive(false);
                    continue;
                }

                // up quickly then slowing, a pop that settles in the first tenth, and gone
                // over the last two fifths
                float rise = 1f - (1f - t) * (1f - t);
                float pop = t < 0.12f ? 1.3f - 0.3f * (t / 0.12f) : 1f;
                float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;

                number.Transform.position = number.Origin + Vector3.up * (NumberRise * rise);
                number.Transform.localScale = Vector3.one * pop;

                Color colour = number.Colour;
                colour.a = alpha;
                number.Label.color = colour;

                if (_camera != null)
                {
                    Vector3 toCamera = _camera.transform.position - number.Transform.position;

                    toCamera.y = 0f;

                    if (toCamera.sqrMagnitude > 0.0001f)
                    {
                        number.Transform.rotation =
                            Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                    }
                }
            }

            for (var i = 0; i < _impacts.Length; i++)
            {
                ref Impact impact = ref _impacts[i];

                if (!impact.Live) continue;

                impact.Age += deltaTime;

                if (impact.Age < 0.6f) continue;

                impact.Live = false;
                impact.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                impact.System.gameObject.SetActive(false);
            }

            for (var i = _flashes.Count - 1; i >= 0; i--)
            {
                Flashing flash = _flashes[i];

                flash.Age += deltaTime;

                if (flash.Age < flash.Seconds)
                {
                    _flashes[i] = flash;
                    continue;
                }

                if (flash.Renderer != null)
                {
                    flash.Renderer.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, flash.Rest);
                    flash.Renderer.SetPropertyBlock(_block);
                }

                _flashes.RemoveAt(i);
            }
        }

        // ---- the pools ----------------------------------------------------------------

        private void Build()
        {
            if (_built) return;

            _built = true;
            _block = new MaterialPropertyBlock();

            _audio = GetComponent<AudioSource>();

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f;
            }

            _numbers = new Number[NumberPoolSize];

            for (var i = 0; i < _numbers.Length; i++)
            {
                var host = new GameObject("DamageNumber");

                host.transform.SetParent(transform, false);

                var label = host.AddComponent<TextMeshPro>();

                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = NumberFontSize;
                label.fontStyle = FontStyles.Bold;
                label.rectTransform.sizeDelta = new Vector2(2f, 0.6f);
                label.text = string.Empty;

                // The outline is on ONE material every label shares, never set on the label.
                // TextMeshPro's outlineWidth setter instances a material per component the
                // moment it is touched -- twenty-four labels was twenty-four materials, and
                // an error in the console for each. One outlined copy of the font's own
                // material, made once and shared, is the whole of the cost.
                if (_numberMaterial == null && label.font != null && label.font.material != null)
                {
                    _numberMaterial = new Material(label.font.material)
                        { name = "DamageNumber (runtime)" };
                    _numberMaterial.EnableKeyword("OUTLINE_ON");
                    _numberMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
                    _numberMaterial.SetColor(ShaderUtilities.ID_OutlineColor,
                        new Color32(30, 24, 40, 255));
                }

                if (_numberMaterial != null) label.fontSharedMaterial = _numberMaterial;

                host.SetActive(false);

                _numbers[i] = new Number { Label = label, Transform = host.transform };
            }

            _impacts = new Impact[ImpactPoolSize];

            GameObject authored = _config != null ? _config.physicalHitVfx : null;

            for (var i = 0; i < _impacts.Length; i++)
            {
                ParticleSystem system = authored != null
                    ? Instantiate(authored, transform).GetComponentInChildren<ParticleSystem>(true)
                    : BuildSpark();

                if (system == null) system = BuildSpark();

                // An authored prefab may well play on awake; it is stopped and cleared before
                // it is pooled, so nothing is ever configured while it runs and its first
                // burst is the first hit rather than the moment the world loaded.
                if (system.isPlaying)
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                system.gameObject.SetActive(false);

                _impacts[i] = new Impact { System = system };
            }
        }

        /// <summary>
        /// A small burst of bright specks, built in code.
        /// </summary>
        /// <remarks>
        /// This is first-combat feedback, not final art, and no impact effect has been
        /// authored yet. Rather than ship nothing, or a coloured cube, the fallback is a real
        /// particle burst: a dozen additive sparks thrown outward and gone in a quarter of a
        /// second. When an artist supplies a prefab through the config it is used instead,
        /// pooled the same way.
        /// </remarks>
        private ParticleSystem BuildSpark()
        {
            var host = new GameObject("HitSpark");

            host.transform.SetParent(transform, false);

            // Inactive before the component exists. A ParticleSystem added to a live object
            // starts playing on the spot (play-on-awake is its default), and setting the
            // duration of a playing system is refused with an error on every pool slot
            // built -- eight errors per world, the "Setting the duration while system is
            // still playing" the manual test kept seeing. Configured asleep, played on the
            // first hit, and the duration is never touched again.
            host.SetActive(false);

            var system = host.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;

            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSize = 0.07f;
            main.startColor = new Color(1.0f, 0.98f, 0.80f, 1.0f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;

            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            ParticleSystem.ShapeModule shape = system.shape;

            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;

            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;

            colour.enabled = true;

            var gradient = new Gradient();

            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.85f, 0.55f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var renderer = host.GetComponent<ParticleSystemRenderer>();

            if (_sparkMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

                if (shader == null) shader = Shader.Find("Sprites/Default");

                _sparkMaterial = new Material(shader) { name = "HitSpark (runtime)" };

                if (_sparkMaterial.HasProperty("_Surface")) _sparkMaterial.SetFloat("_Surface", 1f);
                if (_sparkMaterial.HasProperty("_Blend")) _sparkMaterial.SetFloat("_Blend", 1f);
                if (_sparkMaterial.HasProperty("_BaseColor"))
                {
                    _sparkMaterial.SetColor("_BaseColor", Color.white);
                }

                _sparkMaterial.renderQueue = 3000;
            }

            renderer.sharedMaterial = _sparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return system;
        }
    }
}
