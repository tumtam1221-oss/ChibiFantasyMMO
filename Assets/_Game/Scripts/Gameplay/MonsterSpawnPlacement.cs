using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Where a monster spawn point sits in the world.
    /// </summary>
    /// <remarks>
    /// <b>The seam between authored places and the spawner.</b> A
    /// <see cref="SpawnPointDefinition"/> of type <see cref="SpawnType.Monster"/> is content:
    /// a map, a position and a facing. A <see cref="MonsterSpawnPoint"/> is what the spawner
    /// runs on. This turns one into the other, so a monster spawn is authored in exactly the
    /// same place a player spawn is, and no coordinate is written in code.
    ///
    /// <b>One map rule, not two.</b> <see cref="IsMapAllowed"/> is the single answer to
    /// "may this monster stand here". <c>MonsterSpawnService</c> asks it at spawn time and
    /// <see cref="Validate"/> asks it in the content pass, so a spawn the validator accepts
    /// cannot be refused at runtime for a different reason -- and no second copy of the rule
    /// can drift.
    ///
    /// <b>It creates nothing.</b> Building a placement spawns no monster and touches no
    /// runtime state; <c>MonsterSpawnService.TrySpawn</c> remains the only thing that puts a
    /// monster in the world.
    /// </remarks>
    public static class MonsterSpawnPlacement
    {
        /// <summary>
        /// Builds a spawn point from an authored place.
        /// </summary>
        /// <param name="spawn">A <see cref="SpawnType.Monster"/> point. The map and position
        /// come from here and nowhere else.</param>
        /// <param name="monster">Which monster comes from it.</param>
        /// <param name="maxAlive">How many may live from this point at once.</param>
        /// <param name="respawnDelaySeconds">Override, or zero to use the monster's own.</param>
        /// <param name="radius">How far around the point one may appear.</param>
        /// <returns>An invalid point when the spawn is not a usable monster placement, so a
        /// caller cannot accidentally place a monster on a player's arrival marker.</returns>
        public static MonsterSpawnPoint FromSpawnPoint(SpawnPointDefinition spawn,
            DefinitionId monster, int maxAlive = 1, float respawnDelaySeconds = 0f,
            float radius = 0f)
        {
            if (spawn == null || !spawn.IsValid) return default;
            if (spawn.SpawnType != SpawnType.Monster) return default;
            if (!monster.IsValid) return default;

            return new MonsterSpawnPoint(monster,
                new CombatPosition(spawn.X, spawn.Y, spawn.Z),
                radius, maxAlive, respawnDelaySeconds, spawn.Map);
        }

        /// <summary>
        /// Whether a monster's authored restrictions permit a map.
        /// </summary>
        /// <remarks>An empty restriction list means unrestricted, and a point that names no
        /// map cannot be judged, so both are allowed. Anything stricter would refuse content
        /// that predates maps carrying an id at all.</remarks>
        public static bool IsMapAllowed(MonsterDefinition definition, DefinitionId map)
        {
            if (definition == null) return false;

            DefinitionId[] allowed = definition.AllowedMaps;

            if (allowed.Length == 0 || !map.IsValid) return true;

            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == map) return true;
            }

            return false;
        }

        /// <summary>
        /// Checks authored monster spawns against the maps they claim to be on.
        /// </summary>
        /// <remarks>
        /// A content-pass check, not a runtime one: <c>MonsterSpawnService</c> already
        /// refuses a spawn it cannot justify. This exists so the refusal turns up against
        /// the row that caused it rather than as an empty field a player reports.
        ///
        /// A town holding a hostile spawn is a warning rather than an error, because event
        /// content legitimately does it; a monster restricted away from the map it is placed
        /// on is an error, because it would simply never appear.
        /// </remarks>
        public static void Validate(IReadOnlyList<MonsterSpawnPoint> points,
            IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<MonsterDefinition> monsters, ValidationReport report)
        {
            if (points == null || report == null) return;

            for (int i = 0; i < points.Count; i++)
            {
                MonsterSpawnPoint point = points[i];

                if (!point.Monster.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, default(DefinitionId),
                        "A monster spawn point names no monster.");
                    continue;
                }

                MonsterDefinition monster = null;

                if (monsters != null
                    && (!monsters.TryGet(point.Monster, out monster) || monster == null))
                {
                    report.AddError(ValidationCode.MissingReference, point.Monster,
                        "The monster spawn point references a monster that does not resolve.");
                    continue;
                }

                if (!point.Map.IsValid)
                {
                    // Placeable, but nothing can check where it is; travel and validation
                    // both need the map to say anything at all.
                    report.AddWarning(ValidationCode.InvalidConfiguration, point.Monster,
                        "The monster spawn point belongs to no map.");
                    continue;
                }

                MapDefinition map = null;

                if (maps != null && (!maps.TryGet(point.Map, out map) || map == null))
                {
                    report.AddError(ValidationCode.MissingReference, point.Monster,
                        "The monster spawn point is on map '" + point.Map
                        + "', which does not resolve to any definition.");
                    continue;
                }

                if (monster != null && !IsMapAllowed(monster, point.Map))
                {
                    report.AddError(ValidationCode.InvalidConfiguration, point.Monster,
                        "The monster is not authored for map '" + point.Map
                        + "', so the point would never spawn anything.");
                }

                if (map != null && map.IsTown)
                {
                    report.AddWarning(ValidationCode.InvalidConfiguration, point.Monster,
                        "A monster spawn point stands in town '" + map.Id + "'.");
                }
            }
        }

        /// <summary>
        /// Checks that authored monster spawn markers sit on maps that exist.
        /// </summary>
        /// <remarks>The registry-wide counterpart to <see cref="Validate"/>: it reads the
        /// <see cref="SpawnType.Monster"/> markers themselves rather than the spawn points
        /// built from them, so a marker nothing has wired up yet is still checked.</remarks>
        public static void ValidateMonsterSpawnMarkers(
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints,
            IDefinitionRegistry<MapDefinition> maps, ValidationReport report)
        {
            if (spawnPoints == null || report == null) return;

            IReadOnlyList<SpawnPointDefinition> all = spawnPoints.All;

            for (int i = 0; i < all.Count; i++)
            {
                SpawnPointDefinition spawn = all[i];
                if (spawn == null || spawn.SpawnType != SpawnType.Monster) continue;

                if (!spawn.Map.IsValid)
                {
                    report.AddError(ValidationCode.InvalidConfiguration, spawn.Id,
                        "The monster spawn marker belongs to no map.");
                    continue;
                }

                MapDefinition map;
                if (maps == null) continue;

                if (!maps.TryGet(spawn.Map, out map) || map == null)
                {
                    report.AddError(ValidationCode.MissingReference, spawn.Id,
                        "The monster spawn marker is on map '" + spawn.Map
                        + "', which does not resolve to any definition.");
                    continue;
                }

                if (map.IsTown)
                {
                    report.AddWarning(ValidationCode.InvalidConfiguration, spawn.Id,
                        "A monster spawn marker stands in town '" + map.Id + "'.");
                }
            }
        }
    }
}
