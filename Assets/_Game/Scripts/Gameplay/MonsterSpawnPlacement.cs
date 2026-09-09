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
        /// <summary>
        /// Whether a map admits monsters at all.
        /// </summary>
        /// <remarks>
        /// <b>The town rule, in one place.</b> A town is where players stand about, trade and
        /// take quests; a monster inside one is a bug, not content. This answers that question
        /// from authored map data alone -- no monster id is named anywhere, so marking a new
        /// map safe is a content edit rather than a code change.
        ///
        /// <b>It reuses the flags that already exist.</b> <see cref="MapDefinition.IsTown"/>
        /// and <see cref="MapDefinition.IsSafeZone"/> were already authored and already drive
        /// PvP policy and warp destinations; this is the same vocabulary answering a third
        /// question rather than a second parallel map-rule system.
        ///
        /// <b>An unknown map is allowed.</b> Null means the caller could not resolve the map,
        /// and refusing every unresolved map would silently empty a world whose registry was
        /// wired late. Content validation is what catches a map that does not exist.
        /// </remarks>
        public static bool AllowsMonsters(MapDefinition map)
        {
            if (map == null) return true;

            // Safe as a whole: closed, full stop.
            if (map.IsSafeZone) return false;

            // A town with no authored zones is the old kind of town -- the whole map is
            // the town. A town that authors zones is a walled town in open country: the
            // safety stops at the zones and the map stays open beyond them.
            if (map.IsTown && map.SafeZones.Length == 0) return false;

            return true;
        }

        /// <summary>
        /// Whether a monster may stand at a particular spot on a map.
        /// </summary>
        /// <remarks>
        /// <b>The zone rule, in one place.</b> Everything the server refuses about monsters
        /// and safe ground -- a nest, a configured row, a step, a target -- asks this, so a
        /// spot the spawn validator accepts can never be walked into by a monster for a
        /// different reason, and no second copy of the geometry can drift.
        /// </remarks>
        public static bool AllowsMonstersAt(MapDefinition map, float x, float z)
        {
            if (!AllowsMonsters(map)) return false;

            return map == null || !map.IsInsideSafeZone(x, z);
        }

        /// <summary>
        /// Whether a player standing at a spot may be chosen as a monster's target.
        /// </summary>
        /// <remarks>Standing inside a safe zone means unreachable and untargetable at once:
        /// a monster stopped at the wall must not keep swinging at somebody just inside it.</remarks>
        public static bool MayBeTargeted(MapDefinition map, float x, float z)
        {
            return map == null || !map.IsInsideSafeZone(x, z);
        }

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
