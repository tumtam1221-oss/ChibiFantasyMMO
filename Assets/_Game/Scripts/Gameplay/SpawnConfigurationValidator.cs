using ChibiFantasy.Contracts;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides whether a configured nest or AI override may reach the authoritative runtime.
    /// </summary>
    /// <remarks>
    /// <b>This is the only door.</b> Configuration arrives from a database an operator
    /// edits by hand, so it is the least trustworthy input the server takes -- more so than
    /// a client's, because a client is expected to lie and a spreadsheet is expected to be
    /// right. Everything that reaches <c>MonsterWorldRuntime</c> passes here first, and
    /// anything refused is named rather than corrected: an operator who typed 30 into a nest
    /// that holds 10 meant something, and silently spawning 10 hides the mistake until
    /// somebody notices the map is wrong.
    ///
    /// <b>Content is checked against the registries, not against a copy.</b> Whether
    /// <c>monster.orc</c> exists is a question only the loaded content can answer, which is
    /// why the database holds no monster table to go stale.
    ///
    /// <b>Pure and engine-free.</b> No clock, no transport, no registry of its own -- the
    /// registries arrive as arguments, so every rule below is exercised by an ordinary test.
    /// </remarks>
    public static class SpawnConfigurationValidator
    {
        /// <summary>
        /// Whether a configured nest can become a live spawn point.
        /// </summary>
        /// <param name="configuration">The row, as the database described it.</param>
        /// <param name="maps">Authored maps. A nest on an unknown map can never be reached.</param>
        /// <param name="monsters">Authored monsters. A nest for an unknown one spawns nothing.</param>
        public static SpawnConfigurationVerdict Validate(
            in MonsterSpawnConfiguration configuration,
            IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<MonsterDefinition> monsters)
        {
            if (maps == null || monsters == null)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.MissingContext);
            }

            if (!configuration.Map.IsValid || !configuration.Monster.IsValid)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.MissingContext);
            }

            if (!maps.TryGet(configuration.Map, out MapDefinition map) || map == null)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.UnknownMap);
            }

            if (!monsters.TryGet(configuration.Monster, out MonsterDefinition monster)
                || monster == null)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.UnknownMonster);
            }

            if (configuration.MaxAlive <= 0)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.MaxAliveNotPositive);
            }

            if (configuration.InitialCount < 0)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.InitialCountNegative);
            }

            if (configuration.InitialCount > configuration.MaxAlive)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.InitialCountExceedsMaxAlive);
            }

            if (configuration.Radius < 0f || !IsFinite(configuration.Radius))
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.RadiusNegative);
            }

            if (configuration.RespawnSeconds < 0f || !IsFinite(configuration.RespawnSeconds))
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.RespawnNegative);
            }

            // Checked last among the numbers, but checked: a NaN coordinate compares false
            // against every bound and would put a nest nowhere, silently.
            if (!IsFinite(configuration.X) || !IsFinite(configuration.Y)
                || !IsFinite(configuration.Z))
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.PositionNotFinite);
            }

            return SpawnConfigurationVerdict.Accepted;
        }

        /// <summary>
        /// Whether an AI override may be applied.
        /// </summary>
        /// <remarks>
        /// An override for a monster this server does not have is refused rather than
        /// ignored: it means the database and the content build disagree, which an operator
        /// needs to know about before players find out.
        /// </remarks>
        public static SpawnConfigurationVerdict Validate(
            in MonsterAiConfiguration configuration,
            IDefinitionRegistry<MonsterDefinition> monsters)
        {
            if (monsters == null || !configuration.Monster.IsValid)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.MissingContext);
            }

            if (!monsters.TryGet(configuration.Monster, out MonsterDefinition monster)
                || monster == null)
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.UnknownMonster);
            }

            if (configuration.HasAggression && !IsKnownAggression(configuration.Aggression))
            {
                // A behaviour nobody implemented would fall through to whatever the
                // switch's default happens to be, which is a balance decision made by
                // accident.
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.UnknownAggression);
            }

            if (!IsUsable(configuration.HasDetectionRange, configuration.DetectionRange)
                || !IsUsable(configuration.HasChaseRange, configuration.ChaseRange)
                || !IsUsable(configuration.HasAttackRange, configuration.AttackRange)
                || !IsUsable(configuration.HasAttackCooldown, configuration.AttackCooldown)
                || !IsUsable(configuration.HasMoveSpeed, configuration.MoveSpeed))
            {
                return SpawnConfigurationVerdict.Refused(
                    SpawnConfigurationRejection.AiValueInvalid);
            }

            return SpawnConfigurationVerdict.Accepted;
        }

        /// <summary>Whether an int names a behaviour the AI actually implements.</summary>
        /// <remarks>Bounded by the existing <see cref="MonsterAggressionType"/> rather than
        /// by a literal range, so adding a mode to that enum extends this automatically and
        /// a mode that does not exist is refused.</remarks>
        public static bool IsKnownAggression(int aggression)
        {
            return System.Enum.IsDefined(typeof(MonsterAggressionType), aggression);
        }

        private static bool IsUsable(bool present, float value)
        {
            return !present || (value >= 0f && IsFinite(value));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
