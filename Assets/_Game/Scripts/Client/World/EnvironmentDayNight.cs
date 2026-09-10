using ChibiFantasy.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Draws the world's time of day: the sun's angle, its colour, the ambient light, and how
    /// much of the night grade is showing.
    /// </summary>
    /// <remarks>
    /// <b>It renders a clock it does not own.</b> The server decides what time it is and says
    /// so once, when a character arrives (<c>WorldSpawnMessage.TimeOfDay</c>). This runs the
    /// same arithmetic locally from that seed, so the sky advances smoothly at frame rate
    /// without a number being sent every tick. Two clients seeded from the same server stay
    /// in step because they are both counting real seconds at the same authored rate.
    ///
    /// <b>Nothing here decides when night is.</b> The phase boundaries and the day/night
    /// ramp live in <see cref="WorldClock"/>, which the server uses too. If this file had its
    /// own idea of dusk, the light and the gameplay that reads the phase would drift apart.
    ///
    /// <b>The grade is two volumes, not one profile being edited.</b> Writing new values into
    /// a shared <see cref="VolumeProfile"/> every frame would edit the project asset in the
    /// editor and leave the last frame's night baked into it on exit -- which is exactly how a
    /// "why is my scene dark in the morning" bug is born. Instead the day volume and the night
    /// volume both exist and their weights cross-fade, so the asset on disk is never touched.
    /// </remarks>
    [ExecuteAlways]
    public sealed class EnvironmentDayNight : MonoBehaviour
    {
        [Header("Driven")]
        [Tooltip("The scene's directional light. Its authored yaw is kept; only the pitch sweeps.")]
        [SerializeField] private Light _sun;

        [Tooltip("Weight goes to one by day. Leave empty to skip the day grade.")]
        [SerializeField] private Volume _dayVolume;

        [Tooltip("Weight goes to one by night. Leave empty to skip the night grade.")]
        [SerializeField] private Volume _nightVolume;

        [Header("Sun")]
        [SerializeField] private float _sunIntensityDay = 0.90f;
        [SerializeField] private float _sunIntensityNight = 0.12f;
        [SerializeField] private Color _sunColourDay = new Color(1.00f, 0.97f, 0.90f);
        [SerializeField] private Color _sunColourHorizon = new Color(1.00f, 0.78f, 0.55f);
        [SerializeField] private Color _sunColourNight = new Color(0.55f, 0.68f, 1.00f);

        [Header("Ambient")]
        [Tooltip("Ambient by day. The night figure is kept generous on purpose - see the remarks.")]
        [SerializeField] private float _ambientDay = 1.40f;

        [Tooltip("Ambient by night. Night is meant to read as night and still be playable.")]
        [SerializeField] private float _ambientNight = 0.85f;

        [Header("Overcast")]
        [Tooltip("What fraction of its brightness the sun keeps under a fully overcast sky.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastSunScale = 0.55f;

        [Tooltip("The sun is tinted towards this when the cloud comes over. Grey, not blue.")]
        [SerializeField] private Color _overcastSunTint = new Color(0.82f, 0.85f, 0.90f);

        [Tooltip("Ambient under cloud. Barely down: cloud flattens light, it does not remove it.")]
        [Range(0f, 2f)]
        [SerializeField] private float _overcastAmbientScale = 0.88f;

        [Tooltip("What the shadows keep under cloud. Soft shadows are most of the overcast read.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastShadowScale = 0.45f;

        [Tooltip("How long the cloud takes to roll in or off, in seconds.")]
        [SerializeField] private float _overcastFadeSeconds = 4f;

        [Header("Overcast sky")]
        [Tooltip("The colour of the cloud itself. A cool light grey, not a blue.")]
        [SerializeField] private Color _overcastSkyTint = new Color(0.86f, 0.89f, 0.94f);

        [Tooltip("How far the greyed sky is lifted towards white. Overcast is pale, not dark.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastSkyLift = 0.60f;

        [Tooltip("Distance fog goes this colour under cloud, so the hills grey out with the sky.")]
        [SerializeField] private Color _overcastFogColour = new Color(0.63f, 0.67f, 0.72f);

        [Tooltip("How much cloud cover the skybox draws when fully overcast.")]
        [Range(0f, 1f)]
        [SerializeField] private float _overcastCloudCoverage = 0.35f;

        [Header("Clock")]
        [Tooltip("Used until the server says otherwise, so the scene looks right in the editor.")]
        [SerializeField] private float _editorTimeOfDay = 0.35f;

        [Tooltip("Hold the world at Preview Time and ignore the server. For looking at the art.")]
        [SerializeField] private bool _freezeForPreview;

        [Tooltip("The moment to hold when Freeze For Preview is on. 0 is midnight, 0.5 is noon.")]
        [Range(0f, 1f)]
        [SerializeField] private float _previewTimeOfDay = 0.0f;

        [Header("Re-sync")]
        [Tooltip("Drift smaller than this, as a fraction of a day, is left alone. 0.0002 of "
            + "an hour-long day is about a second.")]
        [SerializeField] private float _driftDeadband = 0.0002f;

        [Tooltip("Drift larger than this, as a fraction of a day, is snapped rather than "
            + "eased -- a machine that slept is not drift, it is a different hour.")]
        [SerializeField] private float _driftSnapThreshold = 0.02f;

        [Tooltip("The most a correction may bend time, as a share of real time. 0.5 means the "
            + "clock runs between half speed and one and a half, never backwards.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float _driftCatchUpFraction = 0.5f;

        /// <summary>Below this much world time still owed, the correction is finished.</summary>
        private const double SettledSeconds = 0.001;

        private WorldClock _clock;
        private bool _seeded;
        private float _drift;
        private WorldClientBootstrap _world;
        private float _overcast;
        private float _overcastTarget;
        private float _authoredShadowStrength = 1f;
        private bool _readAuthoredShadow;
        private Material _skyAuthored;
        private Material _skyRuntime;
        private Color _fogAuthored;
        private bool _skySwapped;

        /// <summary>
        /// Where the sky is now, 0 and 1 being midnight.
        /// </summary>
        /// <remarks>
        /// <b>Preview wins over the server on purpose.</b> Judging how the town looks at
        /// midnight otherwise means waiting for midnight, or rebuilding the server with a
        /// different start time. This holds the sky still for whoever is looking at it and
        /// changes nothing anyone else sees -- it is a local rendering override, not a clock:
        /// the world's real time carries on underneath and is picked back up the moment the
        /// switch goes off.
        /// </remarks>
        public float TimeOfDay
        {
            get
            {
                if (_freezeForPreview) return _previewTimeOfDay;

                return _clock != null ? _clock.TimeOfDay : _editorTimeOfDay;
            }
        }

        /// <summary>Which part of the day is showing.</summary>
        public WorldTimePhase Phase => WorldClock.PhaseAt(TimeOfDay);

        /// <summary>Whether the server has told this client what time it is.</summary>
        public bool IsSeeded => _seeded;

        /// <summary>How much cloud is over the sun right now, 0 clear and 1 fully overcast.</summary>
        public float Overcast => _overcast;

        /// <summary>
        /// Asks for cloud, or for it to clear.
        /// </summary>
        /// <remarks>
        /// <b>Weather dims the sun; it does not own the sun.</b> The time of day still decides
        /// how bright and what colour the light is, and this scales that result. Letting the
        /// weather write an absolute brightness instead would make a rainy midnight as bright
        /// as a rainy noon.
        ///
        /// <b>It rolls in rather than snapping.</b> A sky that loses half its light on one
        /// frame reads as a bug, so the change is eased over
        /// <see cref="_overcastFadeSeconds"/> -- about as long as the first drops take to
        /// reach the ground.
        /// </remarks>
        public void SetOvercast(float amount)
        {
            _overcastTarget = Mathf.Clamp01(amount);

            // Nothing is ticking outside play mode, so there would be no fade to watch.
            if (!Application.isPlaying)
            {
                _overcast = _overcastTarget;

                Apply(TimeOfDay);
            }
        }

        /// <summary>
        /// Takes the world's time from the server.
        /// </summary>
        /// <remarks>Called from the spawn broadcast. Safe to call again -- a re-seed on a
        /// second arrival simply corrects any drift rather than restarting the day.</remarks>
        public void Seed(float timeOfDay, float secondsPerDay)
        {
            double perDay = secondsPerDay > 0f ? secondsPerDay : WorldClock.DefaultSecondsPerDay;

            _clock = new WorldClock(perDay, timeOfDay);
            _seeded = true;

            Apply(_clock.TimeOfDay);
        }

        /// <summary>How far this client last found itself from the world, in real seconds.</summary>
        /// <remarks>Reported rather than only corrected, so "is the sky drifting?" is a
        /// question with an answer instead of an argument.</remarks>
        public float DriftSeconds { get; private set; }

        /// <summary>
        /// The world repeated what time it is. Close the gap.
        /// </summary>
        /// <remarks>
        /// <b>Eased, not snapped, when the gap is small.</b> A correction applied outright
        /// moves the sun, and a sun that hops a little every minute is more noticeable than
        /// the drift it is fixing. Anything under
        /// <see cref="_driftSnapThreshold"/> is paid off gradually instead.
        ///
        /// <b>Snapped when it is large.</b> A laptop that was closed for an hour has not
        /// drifted, it is simply in the wrong day; easing that would spend hours of wrong sky
        /// being polite about it.
        ///
        /// <b>A changed day length is always a re-seed.</b> If an operator has changed how
        /// long a day is, the client's rate is wrong and no amount of nudging its position
        /// fixes that.
        /// </remarks>
        public void Resync(float timeOfDay, float secondsPerDay)
        {
            double perDay = secondsPerDay > 0f ? secondsPerDay : WorldClock.DefaultSecondsPerDay;

            if (_clock == null || System.Math.Abs(_clock.SecondsPerDay - perDay) > 0.001)
            {
                Seed(timeOfDay, secondsPerDay);

                return;
            }

            float difference = WorldClock.ShortestDifference(_clock.TimeOfDay, timeOfDay);

            DriftSeconds = difference * (float)perDay;

            if (Mathf.Abs(difference) < _driftDeadband)
            {
                _drift = 0f;

                return;
            }

            if (Mathf.Abs(difference) >= _driftSnapThreshold)
            {
                // Shifted rather than set, so a snap across midnight rolls the day over
                // instead of wrapping the hour and leaving the date behind.
                _clock.Shift(difference * perDay);
                _drift = 0f;

                Apply(TimeOfDay);

                return;
            }

            _drift = difference;
        }

        private void OnEnable()
        {
            if (_sun == null) _sun = FindSun();

            if (_sun != null && !_readAuthoredShadow)
            {
                // Read once and kept, because Apply writes this field every frame: reading it
                // back later would read our own damped value and walk the shadows to nothing.
                _authoredShadowStrength = _sun.shadowStrength;
                _readAuthoredShadow = true;
            }

            CaptureSky();
            Listen();
            Apply(TimeOfDay);
        }

        private void OnDisable()
        {
            if (_world != null)
            {
                _world.OnSpawnReceived -= OnSpawn;
                _world.OnTimeReceived -= OnTime;
            }

            ReleaseSky();

            _world = null;
        }

        /// <summary>
        /// Takes a throwaway copy of the skybox so the cloud can be painted on it.
        /// </summary>
        /// <remarks>
        /// <b>The authored material is never written to.</b> The town's sky is a licensed
        /// ToonScapes asset; tinting it directly would edit a file on disk that this project
        /// does not own and would leave the last frame's weather baked into it. The original
        /// stays the source every frame is read from, and only the copy is changed.
        ///
        /// <b>Play mode only.</b> <see cref="RenderSettings.skybox"/> is saved with the scene,
        /// so swapping in a copy that deliberately does not persist would risk saving a broken
        /// reference if anyone pressed save while it was in. Out of play mode the cloud still
        /// dims the sun, the shadows and the ambient -- it just leaves the sky itself alone.
        /// </remarks>
        private void CaptureSky()
        {
            if (!Application.isPlaying || _skySwapped) return;

            _skyAuthored = RenderSettings.skybox;

            if (_skyAuthored == null) return;

            _skyRuntime = new Material(_skyAuthored) { hideFlags = HideFlags.DontSave };
            _fogAuthored = RenderSettings.fogColor;
            RenderSettings.skybox = _skyRuntime;
            _skySwapped = true;
        }

        /// <summary>Puts the authored sky back exactly as it was.</summary>
        private void ReleaseSky()
        {
            if (!_skySwapped) return;

            RenderSettings.skybox = _skyAuthored;
            RenderSettings.fogColor = _fogAuthored;

            if (_skyRuntime != null) DestroyImmediate(_skyRuntime);

            _skyRuntime = null;
            _skySwapped = false;
        }

        /// <summary>
        /// Finds the world connection and takes the time from it.
        /// </summary>
        /// <remarks>
        /// <b>The component pulls rather than being pushed.</b> This lives in the environment
        /// scene, which is loaded additively at some point around the spawn broadcast; whether
        /// the message or the scene arrives first is not something either end controls. So it
        /// reads whatever has already arrived (<see cref="WorldClientBootstrap.LastSpawn"/>)
        /// and subscribes for whatever has not, and both orderings end up seeded.
        ///
        /// Doing nothing outside play mode keeps the editor showing the authored
        /// <see cref="_editorTimeOfDay"/> instead of hunting for a connection that is not there.
        /// </remarks>
        private void Listen()
        {
            if (!Application.isPlaying) return;

            _world = FindFirstObjectByType<WorldClientBootstrap>(FindObjectsInactive.Exclude);

            if (_world == null) return;

            _world.OnSpawnReceived -= OnSpawn;
            _world.OnSpawnReceived += OnSpawn;

            _world.OnTimeReceived -= OnTime;
            _world.OnTimeReceived += OnTime;

            // Whatever already arrived before this scene finished loading.
            if (_world.LastSpawn.SecondsPerDay > 0f)
            {
                Seed(_world.LastSpawn.TimeOfDay, _world.LastSpawn.SecondsPerDay);
            }
        }

        private void OnSpawn(ChibiFantasy.Network.WorldSpawnMessage message)
        {
            Seed(message.TimeOfDay, message.SecondsPerDay);
        }

        /// <summary>The world repeated the hour; close whatever gap has opened up.</summary>
        private void OnTime(ChibiFantasy.Network.WorldTimeMessage message)
            => Resync(message.TimeOfDay, message.SecondsPerDay);

        private void Update()
        {
            if (Application.isPlaying && _clock != null)
            {
                _clock.Advance(Time.deltaTime);

                PayOffDrift(Time.deltaTime);
            }

            if (Application.isPlaying && !Mathf.Approximately(_overcast, _overcastTarget))
            {
                float step = _overcastFadeSeconds > 0.01f
                    ? Time.deltaTime / _overcastFadeSeconds
                    : 1f;

                _overcast = Mathf.MoveTowards(_overcast, _overcastTarget, step);
            }

            Apply(TimeOfDay);
        }

        /// <summary>
        /// Closes a small gap with the world a little at a time.
        /// </summary>
        /// <remarks>
        /// <b>The clock is bent, not moved.</b> The step is capped at a share of the frame's
        /// own elapsed time by <see cref="WorldClock.CatchUpStep"/>, so a client that is ahead
        /// of the world is slowed down rather than wound back. Subtracting the error outright
        /// -- which this did first -- ran the clock at seventeen times reverse for a few
        /// seconds, and a sun that goes backwards is far more noticeable than a sun that is
        /// half a minute out.
        /// </remarks>
        private void PayOffDrift(float deltaSeconds)
        {
            if (_drift == 0f || _clock == null) return;

            double perDay = _clock.SecondsPerDay;
            double owed = _drift * perDay;
            double step = WorldClock.CatchUpStep(owed, deltaSeconds, _driftCatchUpFraction);

            if (step == 0.0) return;

            _clock.Shift(step);

            _drift -= (float)(step / perDay);

            // Settled once the remainder is under a millisecond of world time. Measured in
            // seconds rather than in fractions of a day because a fraction that reads as
            // "small" depends entirely on how long a day is.
            if (System.Math.Abs(_drift) * perDay < SettledSeconds) _drift = 0f;
        }

        /// <summary>Puts the whole look at one moment in the day.</summary>
        private void Apply(float timeOfDay)
        {
            float night = WorldClock.NightBlendAt(timeOfDay);

            if (_sun != null)
            {
                Vector3 euler = _sun.transform.eulerAngles;
                _sun.transform.rotation = Quaternion.Euler(SunPitchAt(timeOfDay), euler.y, euler.z);

                // The hour decides the light; the cloud only takes some of it away.
                float intensity = Mathf.Lerp(_sunIntensityDay, _sunIntensityNight, night);
                Color colour = SunColourAt(timeOfDay, _sunColourDay, _sunColourHorizon,
                    _sunColourNight);

                _sun.intensity = intensity * Mathf.Lerp(1f, _overcastSunScale, _overcast);
                _sun.color = Color.Lerp(colour, colour * _overcastSunTint, _overcast);
                _sun.shadowStrength = _authoredShadowStrength
                    * Mathf.Lerp(1f, _overcastShadowScale, _overcast);
            }

            RenderSettings.ambientIntensity = Mathf.Lerp(_ambientDay, _ambientNight, night)
                * Mathf.Lerp(1f, _overcastAmbientScale, _overcast);

            if (_dayVolume != null) _dayVolume.weight = 1f - night;
            if (_nightVolume != null) _nightVolume.weight = night;

            ApplySky();
        }

        /// <summary>
        /// Greys the sky itself out, so rain does not fall out of a clear blue day.
        /// </summary>
        /// <remarks>
        /// Every value is read from the authored material and written to the copy, so the
        /// result never compounds: at overcast zero the copy is byte-for-byte the original
        /// again, however many times this has run.
        /// </remarks>
        private void ApplySky()
        {
            if (!_skySwapped || _skyRuntime == null || _skyAuthored == null) return;

            RenderSettings.fogColor = Color.Lerp(_fogAuthored, _overcastFogColour, _overcast);

            GreyOut("_ZenithColorDay");
            GreyOut("_EquatorColorDay");
            GreyOut("_NadirColorDay");
            GreyOut("_ZenithColorTwilight");
            GreyOut("_EquatorColorTwilight");
            GreyOut("_NadirColorTwilight");
            GreyOut("_ZenithColorNight");
            GreyOut("_EquatorColorNight");
            GreyOut("_NadirColorNight");

            // the sun and moon discs go behind the cloud
            Fade("_SunIntensity", 0f);
            Fade("_SunRimIntensity", 0f);
            Fade("_MoonIntensity", 0f);

            // and the cloud layers themselves close over
            Fade("_BackgroundCoverageStrength", _overcastCloudCoverage);
            Fade("_MidgroundCoverageStrength", _overcastCloudCoverage);
        }

        /// <summary>
        /// Pulls one sky colour towards overcast grey.
        /// </summary>
        /// <remarks>
        /// <b>Desaturated, not darkened.</b> Simply scaling the authored colour down keeps all
        /// of its saturation, so the town's vivid blue zenith became a deep navy and the sky
        /// read as nightfall rather than as cloud. Taking the colour's brightness and throwing
        /// its hue away is what actually makes grey; lifting the result towards white is what
        /// keeps an overcast afternoon pale instead of gloomy.
        /// </remarks>
        private void GreyOut(string property)
        {
            if (!_skyAuthored.HasProperty(property)) return;

            Color authored = _skyAuthored.GetColor(property);

            float lit = Mathf.Lerp(authored.grayscale, 1f, _overcastSkyLift);
            var clouded = new Color(
                lit * _overcastSkyTint.r,
                lit * _overcastSkyTint.g,
                lit * _overcastSkyTint.b,
                authored.a);

            _skyRuntime.SetColor(property, Color.Lerp(authored, clouded, _overcast));
        }

        /// <summary>Moves one sky number towards what it should be under full cloud.</summary>
        private void Fade(string property, float overcastValue)
        {
            if (!_skyAuthored.HasProperty(property)) return;

            _skyRuntime.SetFloat(property,
                Mathf.Lerp(_skyAuthored.GetFloat(property), overcastValue, _overcast));
        }

        /// <summary>The only directional light in the scene, when one was not wired by hand.</summary>
        private static Light FindSun()
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) return light;
            }

            return null;
        }

        /// <summary>
        /// How high the sun stands, in degrees of pitch.
        /// </summary>
        /// <remarks>
        /// A sine through the day: highest at noon, below the horizon at midnight. The peak is
        /// deliberately short of straight overhead -- a sun at ninety degrees casts no visible
        /// shadow at all, and the shadows are most of what makes the town read as solid.
        ///
        /// Static and public so a test can pin the arc without a scene, a light or a camera.
        /// </remarks>
        public static float SunPitchAt(float timeOfDay)
        {
            const float middle = 12f;     // the pitch at dawn and dusk
            const float swing = 53f;      // so noon lands at 65 and midnight at -41

            return middle + (swing * Mathf.Sin(2f * Mathf.PI * (timeOfDay - 0.25f)));
        }

        /// <summary>
        /// The sun's colour through the day: warm low down, plain at noon, cool at night.
        /// </summary>
        /// <remarks>
        /// Driven off the sun's own height rather than off the clock directly, so the warm
        /// light and the low sun cannot disagree. The horizon tint is what makes dawn and dusk
        /// read as dawn and dusk rather than as the lights being turned down.
        /// </remarks>
        public static Color SunColourAt(float timeOfDay, Color day, Color horizon, Color night)
        {
            float pitch = SunPitchAt(timeOfDay);

            if (pitch <= 0f)
            {
                // below the horizon: fully the night colour once it is properly down
                return Color.Lerp(horizon, night, Mathf.Clamp01(-pitch / 20f));
            }

            // above it: warm near the horizon, plain daylight once it has climbed
            return Color.Lerp(horizon, day, Mathf.Clamp01(pitch / 35f));
        }
    }
}
