using System.IO;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Editor
{
    /// <summary>
    /// Bakes an environment scene's floor into a <see cref="MapHeightField"/>.
    /// </summary>
    /// <remarks>
    /// <b>What counts as floor.</b> The same things a click may land on: the terrain, and
    /// a walkable deck (a collider whose object name ends in <c>_Deck</c>). Everything else a
    /// ray meets -- a roof, a rock, a fence -- is not floor, so the cell under it is
    /// unwalkable and the server refuses to walk there. Water is a hole in the terrain, so
    /// the ray passes through and the cell is unwalkable for the same reason.
    ///
    /// <b>Run it again after every terrain change.</b> The asset is a snapshot; it does not
    /// track the scene. <see cref="Bake"/> is public and engine-only so an editor script can
    /// call it right after sculpting, and the menu item does the same for a person.
    /// </remarks>
    public static class MapHeightFieldBaker
    {
        /// <summary>Metres between samples. Half a metre resolves a path kerb.</summary>
        public const float DefaultCellSize = 0.5f;

        private const float RayStart = 250f;
        private const float RayLength = 600f;

        [MenuItem("ChibiFantasy/World/Bake Map Height Field (active scene terrain)")]
        public static void BakeActiveSceneMenu()
        {
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();

            if (terrain == null)
            {
                Debug.LogError("No terrain in the active scene; nothing to bake.");
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            string path = "Assets/_Game/Data/Production/Maps/HeightFields/hf_" + sceneName + ".asset";

            MapHeightField field = BakeTerrainScene(terrain, path, DefaultCellSize);

            Debug.Log("Baked " + field.Columns + "x" + field.Rows + " height field to " + path);

            // The field has just moved, so every spawn standing on it is now at the wrong
            // height until this runs. Doing it here rather than leaving it to a second menu
            // item is the only way the two cannot drift apart.
            SnapSpawnsToGroundMenu();
        }

        /// <summary>
        /// Puts every authored spawn on the ground its map actually has.
        /// </summary>
        /// <remarks>
        /// <b>A spawn's height is derived, not authored.</b> X and Z are a design decision --
        /// this corner of the plaza, that end of the bridge -- but Y is simply wherever the
        /// terrain is underneath, and a person typing it into an inspector will get it wrong
        /// by whatever the terrain was sculpted to since. Every one of them currently reads
        /// zero, which is right only for a map whose ground happens to be at sea level.
        ///
        /// <b>Why the float was visible.</b> A character arrives at the spawn's coordinates
        /// exactly, and nothing lowers them afterwards: the server only derives height when
        /// it takes a step. So a spawn authored a quarter of a metre above its terrain left
        /// the player hanging there until they moved, at which point they dropped -- which is
        /// precisely the report this fixes.
        ///
        /// Content, not runtime: it writes the assets, so the built server and the client
        /// both simply read a spawn that is already correct.
        /// </remarks>
        [MenuItem("ChibiFantasy/World/Snap Spawn Points To Ground")]
        public static void SnapSpawnsToGroundMenu()
        {
            int moved = SnapSpawnsToGround();

            Debug.Log(moved == 0
                ? "Spawn points: every one already sits on its map's ground."
                : "Spawn points: moved " + moved + " onto the ground.");
        }

        /// <summary>Snaps every spawn onto its map's baked field. Returns how many moved.</summary>
        /// <remarks>
        /// A spawn whose map has no baked field is left alone rather than dropped to zero:
        /// a flat map is a map whose authored height is already the truth, and guessing at
        /// one would silently move content that was right.
        /// </remarks>
        public static int SnapSpawnsToGround()
        {
            var maps = new System.Collections.Generic.Dictionary<DefinitionId, MapDefinition>();

            foreach (string mapGuid in AssetDatabase.FindAssets("t:" + nameof(MapDefinition)))
            {
                var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(
                    AssetDatabase.GUIDToAssetPath(mapGuid));

                if (map != null && map.Id.IsValid) maps[map.Id] = map;
            }

            var moved = 0;

            foreach (string spawnGuid in AssetDatabase.FindAssets(
                "t:" + nameof(SpawnPointDefinition)))
            {
                string spawnPath = AssetDatabase.GUIDToAssetPath(spawnGuid);
                var spawn = AssetDatabase.LoadAssetAtPath<SpawnPointDefinition>(spawnPath);

                if (spawn == null || !spawn.IsValid) continue;

                MapDefinition map;

                if (!maps.TryGetValue(spawn.Map, out map) || map.HeightField == null) continue;

                float ground;

                if (!map.HeightField.TrySample(spawn.X, spawn.Z, out ground))
                {
                    // Standing where the map says nobody can stand. Refusing to move it is
                    // the honest answer: the coordinates are wrong, and quietly putting the
                    // spawn on the nearest walkable cell would hide that from whoever
                    // authored it.
                    Debug.LogWarning("Spawn " + spawn.Id + " at (" + spawn.X + ", " + spawn.Z
                        + ") is not on walkable ground on " + map.Id + "; left where it is.");

                    continue;
                }

                if (Mathf.Abs(spawn.Y - ground) < 0.0005f) continue;

                var serialized = new SerializedObject(spawn);
                SerializedProperty y = serialized.FindProperty("_y");

                Debug.Log("Spawn " + spawn.Id + ": y " + y.floatValue.ToString("F3") + " -> "
                    + ground.ToString("F3"));

                y.floatValue = ground;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(spawn);
                moved++;
            }

            if (moved > 0) AssetDatabase.SaveAssets();

            return moved;
        }

        /// <summary>
        /// Bakes the area a terrain covers into an asset at <paramref name="assetPath"/>,
        /// creating or overwriting it.
        /// </summary>
        public static MapHeightField BakeTerrainScene(Terrain terrain, string assetPath,
            float cellSize)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            MapHeightField field = AssetDatabase.LoadAssetAtPath<MapHeightField>(assetPath);
            bool fresh = field == null;

            if (fresh) field = ScriptableObject.CreateInstance<MapHeightField>();

            Bake(field, origin.x, origin.z, size.x, size.z, cellSize);

            if (fresh)
            {
                string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');

                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'),
                        Path.GetFileName(folder));
                }

                AssetDatabase.CreateAsset(field, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(field);
            }

            AssetDatabase.SaveAssets();

            return field;
        }

        /// <summary>
        /// Samples the loaded physics scene over a rectangle and writes it into the field.
        /// </summary>
        public static void Bake(MapHeightField field, float originX, float originZ,
            float sizeX, float sizeZ, float cellSize)
        {
            int columns = Mathf.FloorToInt(sizeX / cellSize) + 1;
            int rows = Mathf.FloorToInt(sizeZ / cellSize) + 1;

            var heights = new float[columns * rows];
            var walkable = new byte[columns * rows];

            for (int j = 0; j < rows; j++)
            {
                float z = originZ + j * cellSize;

                for (int i = 0; i < columns; i++)
                {
                    float x = originX + i * cellSize;
                    int index = j * columns + i;

                    if (TrySampleColumn(x, z, out float floorY))
                    {
                        heights[index] = floorY;
                        walkable[index] = 1;
                    }
                }
            }

            field.Initialize(originX, originZ, cellSize, columns, rows, heights, walkable);
        }

        /// <summary>
        /// How much clear space a character needs above the floor to stand there.
        /// </summary>
        /// <remarks>The playable characters are about one metre tall. Anything solid inside
        /// this band -- a rock, a wall, a table, a fence, a tree trunk -- makes the cell
        /// unwalkable; a roof or a branch above it does not, so a gazebo can still be
        /// walked into.</remarks>
        public const float StandingHeight = 1.2f;

        /// <summary>
        /// Terrain lower than this is under the water and not walkable.
        /// </summary>
        /// <remarks>The water surface sits at y = -0.25 and every bank reaches -0.35 exactly
        /// at the shoreline, so -0.3 stops a character with its feet at the water's edge.
        /// Decks are exempt: a pier or a bridge is walkable at any height.</remarks>
        public const float SubmergedBelowY = -0.3f;

        /// <summary>
        /// Finds the floor under a point and whether a character could stand on it.
        /// </summary>
        /// <remarks>Looks at every collider the vertical ray meets, not just the first: the
        /// highest floor is the ground; any non-floor solid within <see cref="StandingHeight"/>
        /// above it is an obstacle. This is what turns a scene's prop colliders into the
        /// server's "you cannot walk through that rock".</remarks>
        public static bool TrySampleColumn(float x, float z, out float floorY)
        {
            floorY = 0f;

            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, RayStart, z), Vector3.down,
                RayLength);

            bool found = false;
            Vector3 floorNormal = Vector3.up;

            for (int k = 0; k < hits.Length; k++)
            {
                if (!IsFloor(hits[k].collider)) continue;

                if (!found || hits[k].point.y > floorY)
                {
                    floorY = hits[k].point.y;
                    floorNormal = hits[k].normal;
                    found = true;
                }
            }

            if (!found) return false;

            bool terrainFloor = IsTerrainAt(hits, floorY);

            if (floorNormal.y < MinimumWalkableNormalY && terrainFloor) return false;

            // The river and sea beds are real terrain now (so the water can be seen
            // through), but a floor under the water surface is not somewhere to walk.
            if (terrainFloor && floorY < SubmergedBelowY) return false;

            for (int k = 0; k < hits.Length; k++)
            {
                if (IsFloor(hits[k].collider)) continue;

                float above = hits[k].point.y - floorY;

                if (above > 0.05f && above < StandingHeight) return false;
            }

            return true;
        }

        private static bool IsTerrainAt(RaycastHit[] hits, float y)
        {
            for (int k = 0; k < hits.Length; k++)
            {
                if (hits[k].collider is TerrainCollider && Mathf.Abs(hits[k].point.y - y) < 0.001f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Steepest ground a character may stand on, as the cosine of the slope angle.
        /// </summary>
        /// <remarks>About 38 degrees. A shrine ramp with stairs is ~20; a hillside cut is
        /// 45 or more. Without this rule the server would happily walk a player straight up
        /// a cliff face, because it has no physics to say otherwise.</remarks>
        public const float MinimumWalkableNormalY = 0.78f;

        /// <summary>Decks are flat by construction; terrain must not be too steep.</summary>
        public static bool IsGentleEnough(RaycastHit hit)
        {
            if (!(hit.collider is TerrainCollider)) return true;

            return hit.normal.y >= MinimumWalkableNormalY;
        }

        /// <summary>
        /// The same rule as the client's click: terrain, or a walkable deck -- a collider on
        /// or under an object whose name carries <c>_Deck</c> (a bridge, a pier).
        /// </summary>
        public static bool IsFloor(Collider collider)
        {
            if (collider == null) return false;
            if (collider is TerrainCollider) return true;

            for (Transform t = collider.transform; t != null; t = t.parent)
            {
                if (t.name.Contains("_Deck")) return true;
            }

            return false;
        }
    }
}
