using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides when a monster with nothing to do takes a walk, and where to.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> A camp of monsters standing perfectly still on the same spot
    /// forever reads as a world that has not been switched on. They have had a
    /// <see cref="MonsterAiState.Wander"/> state since Phase 10 and nothing ever entered it,
    /// because the state machine had no opinion about idling and no geometry to idle within.
    ///
    /// <b>It is not AI, and the split is deliberate.</b>
    /// <see cref="MonsterAiController"/> owns every decision that involves another creature:
    /// noticing, chasing, striking, leashing, giving up. This owns one question nobody else
    /// is asking -- what does a monster do when the answer to all of those is "nothing" --
    /// and it can only ever act through <see cref="MonsterAiController.BeginWander"/>, which
    /// refuses while anything more important is happening. A stroll therefore cannot
    /// interrupt a fight, delay a chase, or keep a corpse moving, and none of that is a rule
    /// written here.
    ///
    /// <b>Its own area, not the map.</b> The disc is the nest a monster came from, so a camp
    /// stays a camp: slimes mill about the clearing they were authored into rather than
    /// dispersing across the countryside. A nest authored with no radius produces monsters
    /// that never stroll, which is the right reading of "they stand exactly here".
    ///
    /// <b>Deterministic, engine-free, clockless.</b> The same seed and the same tick lengths
    /// always produce the same walk. Randomness is a small xorshift over a stable hash of the
    /// monster's instance id rather than <c>UnityEngine.Random</c> or a shared
    /// <c>System.Random</c>: this assembly may not touch the engine, and a shared generator
    /// would make one monster's stroll depend on how many others ticked first, which is
    /// exactly the kind of thing that cannot be reproduced in a test.
    ///
    /// <b>Blocked destinations resolve themselves.</b> The movement step refuses a step into
    /// a safe zone, off the map, or onto ground nothing can stand on, so a monster that
    /// picked an unreachable spot would otherwise lean against the obstacle forever. The
    /// give-up timer is what makes that a pause rather than a wedge.
    /// </remarks>
    public sealed class MonsterWanderPlan
    {
        /// <summary>Shortest pause between strolls.</summary>
        public const float MinimumRestSeconds = 2f;

        /// <summary>How much longer than that a pause may be.</summary>
        /// <remarks>The range matters more than either end: a camp whose monsters all rest
        /// for exactly the same time steps off together, which reads as a chorus line rather
        /// than as animals.</remarks>
        public const float RestSpreadSeconds = 5f;

        /// <summary>How long a stroll may take before the spot is written off.</summary>
        public const float GiveUpSeconds = 8f;

        /// <summary>How close to the chosen spot counts as arrived.</summary>
        /// <remarks>Measured flat. The ground moves a walking monster up and down and it
        /// would never close the vertical gap, so height is not part of "did I get there".
        /// </remarks>
        public const float ArrivalDistance = 0.25f;

        /// <summary>The shortest walk worth taking.</summary>
        /// <remarks>Without a floor the disc keeps handing out spots a step away, and the
        /// monster twitches on the spot instead of strolling.</remarks>
        public const float MinimumWalk = 0.6f;

        private readonly CombatPosition _home;
        private readonly float _radius;

        private uint _random;
        private float _restRemaining;
        private float _walkElapsed;

        /// <param name="seed">
        /// The monster's identity. Two monsters from one nest must not walk in step, and
        /// their identities are the only thing that reliably differs between them.
        /// </param>
        /// <param name="home">The centre of the area to stay inside -- the nest, not the spawn.</param>
        /// <param name="radius">How far from it to stray. Zero means never stroll.</param>
        public MonsterWanderPlan(InstanceId seed, CombatPosition home, float radius)
        {
            _home = home;
            _radius = radius > 0f ? radius : 0f;
            _random = Seed(seed);

            // Staggered from the first tick rather than after the first stroll. Otherwise a
            // nest that populates in one frame sends every monster off at the same instant.
            _restRemaining = NextRest();
        }

        /// <summary>Whether this plan can ever send a monster anywhere.</summary>
        public bool CanWander => _radius > 0f;

        /// <summary>Seconds left before the next stroll. For tests and for an operator.</summary>
        public float RestRemaining => _restRemaining;

        /// <summary>
        /// Advances the stroll by one server tick.
        /// </summary>
        /// <remarks>
        /// Called after the AI has settled its state and before movement runs, so a stroll
        /// begun this tick is walked this tick and the monster never stands still for a frame
        /// holding a fresh destination.
        /// </remarks>
        public void Tick(float deltaSeconds, MonsterAiController ai)
        {
            if (ai == null || !CanWander) return;

            if (deltaSeconds < 0f) deltaSeconds = 0f;

            MonsterRuntimeState monster = ai.Monster;

            // Busy, or dead. Anything the controller settled on that is not standing about
            // outranks a stroll, and coming back from it starts the pause over -- a monster
            // that has just fought its way home should not immediately wander off.
            if (ai.State != MonsterAiState.Idle && ai.State != MonsterAiState.Wander)
            {
                _walkElapsed = 0f;
                _restRemaining = NextRest();

                return;
            }

            if (ai.State == MonsterAiState.Wander)
            {
                _walkElapsed += deltaSeconds;

                bool arrived = !monster.HasWanderDestination
                    || FlatDistance(monster.Position, monster.WanderDestination) <= ArrivalDistance;

                // Given up: the spot is behind a town wall, off the map, or over water, and
                // the movement step has been refusing it every tick.
                if (arrived || _walkElapsed >= GiveUpSeconds)
                {
                    ai.StopWandering();

                    _walkElapsed = 0f;
                    _restRemaining = NextRest();
                }

                return;
            }

            _restRemaining -= deltaSeconds;

            if (_restRemaining > 0f) return;

            if (ai.BeginWander(Choose(monster.Position)))
            {
                _walkElapsed = 0f;

                return;
            }

            // Refused -- it became busy between the controller's tick and this one. Wait
            // out another pause rather than hammering the controller every tick.
            _restRemaining = NextRest();
        }

        /// <summary>
        /// Picks somewhere inside the nest to walk to.
        /// </summary>
        /// <remarks>
        /// Uniform over the disc -- the square root is what stops the spots clustering in
        /// the middle, which would have a camp slowly collapse onto its own centre.
        ///
        /// The height is the monster's own, not the nest's. The ground puts a walking
        /// monster at the right height every step anyway, and a destination hanging in the
        /// air above a slope would add a vertical component to every step for nothing.
        /// </remarks>
        private CombatPosition Choose(CombatPosition from)
        {
            float angle = NextFloat() * 6.2831853f;
            float distance = _radius * Sqrt(NextFloat());

            float x = _home.X + Cos(angle) * distance;
            float z = _home.Z + Sin(angle) * distance;

            var chosen = new CombatPosition(x, from.Y, z);

            if (FlatDistance(from, chosen) >= MinimumWalk) return chosen;

            // Too close to be worth walking. Cross the nest instead of shuffling: the far
            // side of the disc from where it stands is always a real walk and always inside
            // the area.
            float dx = from.X - _home.X;
            float dz = from.Z - _home.Z;
            float length = Sqrt(dx * dx + dz * dz);

            if (length <= 0.0001f)
            {
                // Standing exactly on the centre, so there is no opposite side to pick. Any
                // direction is as good as any other; the angle already drawn is one.
                return new CombatPosition(_home.X + Cos(angle) * _radius, from.Y,
                    _home.Z + Sin(angle) * _radius);
            }

            return new CombatPosition(_home.X - dx / length * _radius, from.Y,
                _home.Z - dz / length * _radius);
        }

        private float NextRest()
        {
            return MinimumRestSeconds + NextFloat() * RestSpreadSeconds;
        }

        /// <summary>A number in [0, 1). Xorshift32: small, fast, and reproducible.</summary>
        private float NextFloat()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;

            return (_random >> 8) * (1f / 16777216f);
        }

        /// <summary>
        /// A stable number from a monster's identity.
        /// </summary>
        /// <remarks>FNV-1a rather than <c>string.GetHashCode</c>, which is deliberately
        /// randomised per process on modern .NET: a walk that differed between two runs of
        /// the same server could not be reproduced in a test, which is the whole point of
        /// seeding from the identity in the first place.</remarks>
        private static uint Seed(InstanceId instance)
        {
            string value = instance.Value;

            uint hash = 2166136261u;

            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
            }

            // Xorshift is stuck at zero forever, and an empty or unlucky id must not produce
            // a monster that never moves again.
            return hash == 0u ? 2463534242u : hash;
        }

        private static float FlatDistance(in CombatPosition a, in CombatPosition b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;

            return Sqrt(dx * dx + dz * dz);
        }

        // System.Math, not the engine's. This assembly draws no reference to UnityEngine.
        private static float Sqrt(float value) => value <= 0f ? 0f : (float)System.Math.Sqrt(value);

        private static float Cos(float radians) => (float)System.Math.Cos(radians);

        private static float Sin(float radians) => (float)System.Math.Sin(radians);
    }
}
