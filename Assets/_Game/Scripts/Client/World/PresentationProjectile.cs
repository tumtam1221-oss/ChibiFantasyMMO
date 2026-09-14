using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// A purely visual projectile — an arrow or a spell bolt travelling from an origin to an
    /// impact point.
    /// </summary>
    /// <remarks>
    /// <b>It cannot touch gameplay, by construction.</b> There is no collider, no trigger,
    /// no reference to a combatant, a monster, a health value, the combat pipeline, or the
    /// network. It moves a transform and, when it arrives, fires an optional impact effect
    /// and destroys itself. Damage, hit registration, EXP, drops and quests are the server's
    /// and are already resolved (immediately, on the accepted attack) before this ever
    /// spawns — this is the picture that catches up to a decision already made.
    ///
    /// <b>Aimed at a position, not a target.</b> It is launched toward a captured impact
    /// point. If the thing it was fired at dies or despawns mid-flight, the projectile still
    /// completes to the last known point and cleans up — it never chases a live object and
    /// so can never resolve "did it hit". A caller may update <see cref="Destination"/> for
    /// a light homing look, but nothing here reads whether the target is alive.
    ///
    /// <b>Always cleans up.</b> It despawns on arrival and, as a backstop, after
    /// <see cref="_maxLifetimeSeconds"/> regardless — a projectile whose destination is never
    /// reached (a caller that forgot to move it, a zero speed) still disappears.
    /// </remarks>
    public sealed class PresentationProjectile : MonoBehaviour
    {
        [SerializeField] private float _speed = 18f;
        [SerializeField] private float _arriveDistance = 0.15f;
        [SerializeField] private float _maxLifetimeSeconds = 4f;
        [SerializeField] private GameObject _impactEffectPrefab;

        private Vector3 _destination;
        private float _age;
        private bool _arrived;

        /// <summary>Where it is travelling to. May be updated for a homing look.</summary>
        public Vector3 Destination
        {
            get => _destination;
            set => _destination = value;
        }

        /// <summary>Whether it has reached its point (and is about to clean up).</summary>
        public bool HasArrived => _arrived;

        /// <summary>
        /// Launches a visual projectile. Presentation only — it carries no damage.
        /// </summary>
        /// <param name="prefab">The projectile visual.</param>
        /// <param name="origin">Where it starts (a bow/staff socket in production).</param>
        /// <param name="destination">The impact point captured at fire time.</param>
        /// <param name="speed">Metres per second; zero or less uses the prefab default.</param>
        public static PresentationProjectile Spawn(GameObject prefab, Vector3 origin,
            Vector3 destination, float speed = 0f)
        {
            if (prefab == null) return null;

            GameObject go = Instantiate(prefab, origin, Quaternion.identity);

            var projectile = go.GetComponent<PresentationProjectile>();

            if (projectile == null) projectile = go.AddComponent<PresentationProjectile>();

            projectile._destination = destination;

            if (speed > 0f) projectile._speed = speed;

            projectile.FaceTravel();

            return projectile;
        }

        private void Update()
        {
            _age += Time.deltaTime;

            if (_age >= _maxLifetimeSeconds)
            {
                Finish();

                return;
            }

            Vector3 to = _destination - transform.position;
            float distance = to.magnitude;

            if (distance <= _arriveDistance)
            {
                Finish();

                return;
            }

            FaceTravel();

            transform.position += to / distance * Mathf.Min(_speed * Time.deltaTime, distance);
        }

        private void FaceTravel()
        {
            Vector3 to = _destination - transform.position;

            if (to.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(to);
        }

        private void Finish()
        {
            if (_arrived) return;

            _arrived = true;

            if (_impactEffectPrefab != null)
            {
                Instantiate(_impactEffectPrefab, transform.position, Quaternion.identity);
            }

            Destroy(gameObject);
        }
    }
}
