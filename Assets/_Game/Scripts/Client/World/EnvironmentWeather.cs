using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Draws the weather the server says the world is having.
    /// </summary>
    /// <remarks>
    /// <b>It decides nothing.</b> Whether it is raining is a fact about the world that arrives
    /// from the server, exactly like the time of day. This turns that fact into particles.
    /// A client that made its own weather would put two players in the same field under
    /// different skies.
    ///
    /// <b>The emitters are built in code.</b> Rain and snow here are a box of falling quads;
    /// authoring that as a prefab would put four more assets in the project whose only content
    /// is numbers that are already written below, and would need re-authoring in every
    /// environment scene. Built at runtime, any scene that has this component has weather.
    ///
    /// <b>They ride the camera.</b> An emitter fixed to the world would only rain on the part
    /// of the map it was placed over. This keeps the box above whoever is looking, so the rain
    /// is always where the player is and the particle count stays small.
    /// </remarks>
    [ExecuteAlways]
    public sealed class EnvironmentWeather : MonoBehaviour
    {
        [Header("Preview")]
        [Tooltip("Ignore the server and show this weather. For looking at the art.")]
        [SerializeField] private bool _freezeForPreview;

        [Tooltip("The weather to show while Freeze For Preview is on.")]
        [SerializeField] private WorldWeather _previewWeather = WorldWeather.Clear;

        [Header("Rain")]
        [SerializeField] private int _rainParticles = 650;
        [SerializeField] private float _rainSpeed = 14f;
        [SerializeField] private Color _rainColour = new Color(0.80f, 0.88f, 0.98f, 0.42f);

        [Header("Snow")]
        [SerializeField] private int _snowParticles = 320;
        [SerializeField] private float _snowSpeed = 1.6f;
        [SerializeField] private Color _snowColour = new Color(1f, 1f, 1f, 0.85f);

        [Header("Cloud")]
        [Tooltip("How overcast the sky goes while it rains. Drives the sun, not the particles.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastRain = 1f;

        [Tooltip("How overcast the sky goes while it snows.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastSnow = 0.8f;

        [Tooltip("How long the sky darkens before the first drop falls, in seconds.")]
        [SerializeField] private float _leadSeconds = 8f;

        [Header("Volume")]
        [Tooltip("How high above the camera the box sits, and how wide it is.")]
        [SerializeField] private float _height = 9f;
        [SerializeField] private float _width = 12f;

        [Tooltip("How far along the camera's own heading the box is pushed, as a fraction of "
            + "its width. Zero would drop half the rain behind the player.")]
        [SerializeField] private float _lead = 0.4f;

        private WorldClientBootstrap _world;
        private EnvironmentDayNight _dayNight;
        private ParticleSystem _rain;
        private ParticleSystem _snow;
        private Transform _follow;
        private WorldWeather _showing = WorldWeather.Clear;
        private WorldWeather _falling = WorldWeather.Clear;
        private float _dropsIn;
        private bool _waitingForCloud;

        /// <summary>What the world says the weather is.</summary>
        public WorldWeather Showing => _showing;

        /// <summary>What is actually coming down, which lags <see cref="Showing"/> while the cloud gathers.</summary>
        public WorldWeather Falling => _falling;

        /// <summary>Seconds until the first drop, or zero when nothing is waiting on the sky.</summary>
        public float SecondsUntilFalling => _waitingForCloud ? Mathf.Max(0f, _dropsIn) : 0f;

        /// <summary>Whether the preview switch is holding the sky.</summary>
        public bool IsFrozen => _freezeForPreview;

        private void OnEnable()
        {
            if (_dayNight == null) _dayNight = GetComponent<EnvironmentDayNight>();

            Build();
            Listen();

            // Whatever the sky is doing when this wakes up, it is already doing it.
            Show(_freezeForPreview ? _previewWeather : Current(), immediate: true);
        }

        private void OnDisable()
        {
            if (_world != null)
            {
                _world.OnWeatherReceived -= OnWeather;
                _world.OnSpawnReceived -= OnSpawn;
            }

            // Otherwise switching the weather off would leave the cloud it had asked for
            // sitting over the town with nothing left to take it away.
            if (_dayNight != null) _dayNight.SetOvercast(0f);

            _world = null;
        }

        private void Update()
        {
            Follow();

            WorldWeather wanted = _freezeForPreview ? _previewWeather : Current();

            // A preview flip is somebody looking at the art; make them wait eight seconds for
            // it and they will think the switch is broken.
            if (wanted != _showing) Show(wanted, immediate: _freezeForPreview);

            if (!_waitingForCloud) return;

            _dropsIn -= Application.isPlaying ? Time.deltaTime : 0f;

            if (_dropsIn > 0f) return;

            SetFalling(_showing);
            _waitingForCloud = false;
        }

        /// <summary>The weather the server last mentioned, or clear before it has said anything.</summary>
        private WorldWeather Current()
        {
            if (_world == null) return WorldWeather.Clear;

            return (WorldWeather)_world.LastWeather;
        }

        /// <summary>
        /// Finds the world connection and takes the weather from it.
        /// </summary>
        /// <remarks>Pulls as well as subscribes, for the same reason
        /// <see cref="EnvironmentDayNight"/> does: this scene streams in around the arrival
        /// message and neither order is guaranteed.</remarks>
        private void Listen()
        {
            if (!Application.isPlaying) return;

            _world = FindFirstObjectByType<WorldClientBootstrap>(FindObjectsInactive.Exclude);

            if (_world == null) return;

            _world.OnWeatherReceived -= OnWeather;
            _world.OnWeatherReceived += OnWeather;

            _world.OnSpawnReceived -= OnSpawn;
            _world.OnSpawnReceived += OnSpawn;

            // Already spawned before this component woke up: catch up without a build-up.
            if (_world.LastWeather != (int)_showing) Show((WorldWeather)_world.LastWeather, true);
        }

        /// <summary>
        /// The weather turned while this client was watching, so let the cloud arrive first.
        /// </summary>
        private void OnWeather(int weather) => Show((WorldWeather)weather, immediate: false);

        /// <summary>
        /// Arriving in the world. Whatever it is doing here, it has been doing for a while.
        /// </summary>
        /// <remarks>Walking into a storm that is already going and being made to wait eight
        /// seconds in the dry for it to start would look like the rain was broken, not like
        /// weather approaching.</remarks>
        private void OnSpawn(ChibiFantasy.Network.WorldSpawnMessage message)
            => Show((WorldWeather)message.Weather, immediate: true);

        /// <summary>Keeps the emitters over whoever is looking.</summary>
        private void Follow()
        {
            if (_follow == null)
            {
                Camera camera = Camera.main;

                if (camera == null) return;

                _follow = camera.transform;
            }

            // Ahead of the lens, not on top of it. A box centred on the camera spends half
            // its particles behind the player's head, where they cost the same to simulate
            // and are never seen -- which is why the first rain looked like drizzle.
            Vector3 heading = _follow.forward;
            heading.y = 0f;
            heading = heading.sqrMagnitude < 0.0001f ? Vector3.forward : heading.normalized;

            Vector3 above = _follow.position
                + (Vector3.up * _height)
                + (heading * (_width * _lead));

            Place(_rain, above);
            Place(_snow, above);
        }

        /// <summary>
        /// Puts one emitter over the camera, facing straight down.
        /// </summary>
        /// <remarks>
        /// <b>The rotation is reset, not left alone.</b> This component lives on the same
        /// object as the scene's directional light, so an emitter parented to it inherits the
        /// sun's rotation -- and the sun turns through the day. That tilted the whole box, so
        /// the rain fell at whatever angle the sun happened to be at and swung round as the
        /// morning went on. Forcing world identity here keeps rain falling down, which is the
        /// one thing rain has to do.
        /// </remarks>
        private static void Place(ParticleSystem system, Vector3 position)
        {
            if (system == null) return;

            system.transform.SetPositionAndRotation(position, Quaternion.identity);
        }

        /// <summary>Turns on the one that should be falling and turns the others off.</summary>
        /// <remarks>
        /// <b>The cloud is part of the weather, not a separate setting.</b> Rain with the sun
        /// still blazing reads as a particle effect placed over a sunny day rather than as
        /// weather, so the same call that starts the drops also asks the sky to close over.
        /// The dimming itself belongs to <see cref="EnvironmentDayNight"/>, which owns the
        /// light -- this only says how much cloud, never how bright the world is.
        /// </remarks>
        private void Show(WorldWeather weather, bool immediate)
        {
            _showing = weather;

            // The cloud always moves at once. It is the thing that announces the change.
            if (_dayNight != null) _dayNight.SetOvercast(OvercastFor(weather));

            bool starting = weather != WorldWeather.Clear;

            if (!starting || immediate || _leadSeconds <= 0f)
            {
                SetFalling(weather);
                _waitingForCloud = false;

                return;
            }

            // Stopping is not delayed -- only starting is -- so anything already falling
            // stops now and the new sky waits for its cloud.
            SetFalling(WorldWeather.Clear);
            _dropsIn = _leadSeconds;
            _waitingForCloud = true;
        }

        /// <summary>Starts or stops the emitters. What is coming down, as opposed to what the sky is.</summary>
        private void SetFalling(WorldWeather weather)
        {
            _falling = weather;

            SetRunning(_rain, weather == WorldWeather.Rain);
            SetRunning(_snow, weather == WorldWeather.Snow);
        }

        /// <summary>How much cloud a given sky brings with it.</summary>
        private float OvercastFor(WorldWeather weather)
        {
            if (weather == WorldWeather.Rain) return _overcastRain;
            if (weather == WorldWeather.Snow) return _overcastSnow;

            return 0f;
        }

        /// <summary>
        /// Stops emitting without clearing what is already falling.
        /// </summary>
        /// <remarks><see cref="ParticleSystemStopBehavior.StopEmitting"/> rather than
        /// StopEmittingAndClear so that rain which has already left the cloud lands instead of
        /// vanishing in mid-air the instant the sky clears.</remarks>
        private static void SetRunning(ParticleSystem system, bool running)
        {
            if (system == null) return;

            if (running)
            {
                if (!system.isPlaying) system.Play();
            }
            else if (system.isPlaying)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Build()
        {
            if (_rain == null) _rain = BuildFall("Rain", _rainParticles, _rainSpeed,
                _rainColour, 0.035f, 1.2f, 0f, streak: true);

            if (_snow == null) _snow = BuildFall("Snow", _snowParticles, _snowSpeed,
                _snowColour, 0.06f, 7f, 0.35f, streak: false);
        }

        /// <summary>
        /// One box of falling quads.
        /// </summary>
        /// <param name="size">How big one particle is, in metres. Small: these are drops.</param>
        /// <param name="lifetime">Long enough to reach the ground from the top of the box.</param>
        /// <param name="drift">Sideways wander. Rain falls straight; snow does not.</param>
        /// <param name="streak">Rain is stretched along its motion; snow stays round.</param>
        private ParticleSystem BuildFall(string label, int count, float speed, Color colour,
            float size, float lifetime, float drift, bool streak)
        {
            var go = new GameObject("Weather " + label);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.identity;   // see Place: the parent is the sun
            go.hideFlags = HideFlags.DontSave;

            var system = go.AddComponent<ParticleSystem>();
            system.Stop();

            ParticleSystem.MainModule main = system.main;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = 1f;
            main.startColor = colour;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.playOnAwake = false;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = count / lifetime;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;

            // The rotation is what makes particles fall: a box emits along its local +Z, and
            // turning it a quarter turn about X points that at the ground. The catch is that
            // the scale is read in the box's own space, so the flat axis has to be the local
            // Z that just became vertical -- writing the flat axis as Y instead stands the
            // cloud on its edge as a wall, which is how "rain" first came out as a handful of
            // drops at one depth.
            shape.scale = new Vector3(_width, _width, 0.1f);
            shape.rotation = new Vector3(90f, 0f, 0f);

            if (drift > 0f)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = drift;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.4f;
            }

            main.startSize = size;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = streak
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            renderer.velocityScale = 0f;
            renderer.lengthScale = streak ? 5f : 1f;
            renderer.material = FallMaterial(colour, streak);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 0f;

            return system;
        }

        /// <summary>
        /// A transparent unlit material for the drops, with a soft texture.
        /// </summary>
        /// <remarks>
        /// <b>The texture is the point.</b> An untextured quad renders as a solid rectangle,
        /// which is what "rain" looked like the first time: white slabs across the sky. A soft
        /// alpha falloff is what turns a quad into a drop.
        ///
        /// <b>Transparent has to be asked for.</b> URP's particle shader defaults to opaque,
        /// so the surface mode, the blend and the render queue are all set explicitly rather
        /// than assumed from the fact that the colour has an alpha.
        ///
        /// Built here rather than referenced so the component carries no asset dependency,
        /// and marked DontSave so it never lands in the project as a stray asset.
        /// </remarks>
        private static Material FallMaterial(Color colour, bool streak)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };

            // transparent, alpha blended, drawn after the world
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Texture2D texture = streak ? StreakTexture() : DotTexture();

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);

            return material;
        }

        /// <summary>A round soft blob, for a snowflake.</summary>
        private static Texture2D DotTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = ((x + 0.5f) / size) - 0.5f;
                    float dy = ((y + 0.5f) / size) - 0.5f;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy)) * 2f;
                    float a = Mathf.Clamp01(1f - (d * d));

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply();

            return texture;
        }

        /// <summary>A vertical streak that fades at both ends, for a raindrop.</summary>
        private static Texture2D StreakTexture()
        {
            const int w = 8;
            const int h = 32;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            for (int y = 0; y < h; y++)
            {
                float alongV = Mathf.Sin(Mathf.PI * ((y + 0.5f) / h));   // 0 at the ends

                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(((x + 0.5f) / w) - 0.5f) * 2f;
                    float acrossV = Mathf.Clamp01(1f - (dx * dx));

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alongV * acrossV));
                }
            }

            texture.Apply();

            return texture;
        }
    }
}
