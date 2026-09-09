using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Makes the planting move.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists at all.</b> Every vegetation material in the town already asks for
    /// wind: <c>_EnableWind</c> is on and <c>_ENABLEWIND_ON</c> is a valid keyword on all of
    /// them. What was missing is the wind itself. The shader reads five <em>global</em>
    /// shader variables, and an unset global is zero, so the trees and grass were being
    /// swayed by a wind of strength zero.
    ///
    /// <b>Why not the one in the package.</b> ToonScapes ships a component that sets these,
    /// but it opens with an unguarded <c>using UnityEditor</c> and sits in no assembly
    /// definition, so putting it in a scene that ships would tie a player build to an editor
    /// namespace. This sets the same five globals and the same noise texture, from a project
    /// assembly, and leaves the licensed source untouched.
    ///
    /// <b>Globals, so one of these is enough.</b> These are not per-material or per-renderer
    /// values; one component drives every vegetation shader in the loaded scenes. Putting a
    /// second one in another scene would simply mean the last one to run wins, which is why
    /// this belongs on the environment scene and not on anything spawned.
    ///
    /// <b>It writes only when something changed.</b> A global shader set is cheap but not
    /// free, and nothing here animates: the shader does its own movement from
    /// <c>_Time</c>. Left alone this costs one comparison a frame.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EnvironmentWind : MonoBehaviour
    {
        private static readonly int DirectionId =
            Shader.PropertyToID("ToonScapesGlobalWindDirection");

        private static readonly int StrengthId =
            Shader.PropertyToID("ToonScapesGlobalWindStrength");

        private static readonly int ScaleId = Shader.PropertyToID("ToonScapesGlobalWindScale");
        private static readonly int SpeedId = Shader.PropertyToID("ToonScapesGlobalWindSpeed");

        private static readonly int JitterId =
            Shader.PropertyToID("ToonScapesGlobalWindJitter");

        private static readonly int NoiseId =
            Shader.PropertyToID("ToonScapesGlobalNoiseTexture");

        [Tooltip("Which way the wind blows. Only the horizontal part is used by the shader.")]
        [SerializeField] private Vector3 _direction = new Vector3(1f, 0f, 0.35f);

        [Tooltip("How far a branch is pushed. The shader multiplies this by 3.5, so small "
            + "numbers are the useful range for a town rather than a storm.")]
        [Range(0f, 20f)]
        [SerializeField] private float _strength = 0.35f;

        [Tooltip("How large the gusts are across the world. Lower is broader.")]
        [Range(0f, 20f)]
        [SerializeField] private float _scale = 1.1f;

        [Tooltip("How quickly the sway travels.")]
        [Range(0f, 20f)]
        [SerializeField] private float _speed = 0.35f;

        [Tooltip("The small, fast flutter of individual leaves and blades.")]
        [Range(0f, 20f)]
        [SerializeField] private float _jitter = 0.5f;

        [Tooltip("The noise the gusts are shaped from. Without it the sway is uniform.")]
        [SerializeField] private Texture _noise;

        private bool _written;
        private Vector3 _lastDirection;
        private float _lastStrength;
        private float _lastScale;
        private float _lastSpeed;
        private float _lastJitter;
        private Texture _lastNoise;

        /// <summary>How many times the globals have actually been written. For tests.</summary>
        public int Writes { get; private set; }

        private void OnEnable()
        {
            _written = false;

            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void OnValidate()
        {
            _written = false;
        }

        /// <summary>Pushes the current settings into the shader globals, if they moved.</summary>
        public void Apply()
        {
            if (_written
                && _direction == _lastDirection
                && _strength == _lastStrength
                && _scale == _lastScale
                && _speed == _lastSpeed
                && _jitter == _lastJitter
                && _noise == _lastNoise)
            {
                return;
            }

            Shader.SetGlobalVector(DirectionId, _direction);
            Shader.SetGlobalFloat(StrengthId, _strength);
            Shader.SetGlobalFloat(ScaleId, _scale);
            Shader.SetGlobalFloat(SpeedId, _speed);
            Shader.SetGlobalFloat(JitterId, _jitter);

            // A null texture would clear whatever a previous scene set, which is worse than
            // leaving it: the sway would go uniform rather than gusty.
            if (_noise != null) Shader.SetGlobalTexture(NoiseId, _noise);

            _lastDirection = _direction;
            _lastStrength = _strength;
            _lastScale = _scale;
            _lastSpeed = _speed;
            _lastJitter = _jitter;
            _lastNoise = _noise;
            _written = true;

            Writes++;
        }
    }
}
