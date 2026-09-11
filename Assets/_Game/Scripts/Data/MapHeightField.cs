using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// The ground of one map, as a grid of heights baked from its environment scene.
    /// </summary>
    /// <remarks>
    /// <b>Authored data, not physics.</b> The server never loads an environment scene and
    /// never raycasts. An editor bake walks the scene once, samples where the floor is on a
    /// regular grid, and stores the result here; the map references it and movement reads it
    /// through <see cref="IGroundHeight"/>. Change the scene, re-bake, and the server agrees
    /// with the client again -- there is no second copy of the terrain to keep in step.
    ///
    /// <b>Walkable is part of the answer.</b> A cell with no floor under it -- water, the
    /// hole under a bridge, the void past the terrain edge -- is marked unwalkable, and a
    /// sample touching one refuses. That is deliberately conservative: it is better to stop
    /// half a metre short of the water than to let the server put a character in it.
    ///
    /// <b>Pure sampling.</b> <see cref="Sample"/> is static over plain arrays so the arithmetic
    /// is testable without an asset, and the asset is nothing more than those arrays.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/World/Map Height Field", fileName = "hf_map")]
    public sealed class MapHeightField : ScriptableObject, IGroundHeight
    {
        [SerializeField] private float _originX;
        [SerializeField] private float _originZ;
        [SerializeField] private float _cellSize = 1f;
        [SerializeField] private int _columns;
        [SerializeField] private int _rows;
        [SerializeField] private float[] _heights = new float[0];
        [SerializeField] private byte[] _walkable = new byte[0];

        /// <summary>World X of the first column.</summary>
        public float OriginX => _originX;

        /// <summary>World Z of the first row.</summary>
        public float OriginZ => _originZ;

        /// <summary>Metres between grid samples.</summary>
        public float CellSize => _cellSize;

        /// <summary>Samples along X.</summary>
        public int Columns => _columns;

        /// <summary>Samples along Z.</summary>
        public int Rows => _rows;

        /// <summary>Whether the arrays describe a usable grid.</summary>
        public bool IsValid => _columns >= 2 && _rows >= 2 && _cellSize > 0f
            && _heights != null && _heights.Length == _columns * _rows
            && _walkable != null && _walkable.Length == _columns * _rows;

        /// <summary>
        /// Fills the field. The bake calls this; nothing at runtime does.
        /// </summary>
        public void Initialize(float originX, float originZ, float cellSize, int columns,
            int rows, float[] heights, byte[] walkable)
        {
            _originX = originX;
            _originZ = originZ;
            _cellSize = cellSize;
            _columns = columns;
            _rows = rows;
            _heights = heights;
            _walkable = walkable;
        }

        /// <inheritdoc />
        public bool TrySample(float x, float z, out float height)
        {
            if (!IsValid)
            {
                height = 0f;
                return false;
            }

            return Sample(_originX, _originZ, _cellSize, _columns, _rows, _heights, _walkable,
                x, z, out height);
        }

        /// <summary>
        /// Bilinear height at a point, refusing anything off the grid or touching an
        /// unwalkable sample.
        /// </summary>
        /// <remarks>Index layout is row-major: <c>row * columns + column</c>, row along Z.</remarks>
        public static bool Sample(float originX, float originZ, float cellSize, int columns,
            int rows, float[] heights, byte[] walkable, float x, float z, out float height)
        {
            height = 0f;

            if (cellSize <= 0f || columns < 2 || rows < 2) return false;
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z))
            {
                return false;
            }

            float u = (x - originX) / cellSize;
            float v = (z - originZ) / cellSize;

            if (u < 0f || v < 0f || u > columns - 1 || v > rows - 1) return false;

            int i0 = (int)u;
            int j0 = (int)v;
            if (i0 >= columns - 1) i0 = columns - 2;
            if (j0 >= rows - 1) j0 = rows - 2;
            int i1 = i0 + 1;
            int j1 = j0 + 1;

            int a = j0 * columns + i0;
            int b = j0 * columns + i1;
            int c = j1 * columns + i0;
            int d = j1 * columns + i1;

            // One bad corner is enough to refuse: the step would otherwise interpolate a
            // height partly made of "there is no floor here".
            if (walkable[a] == 0 || walkable[b] == 0 || walkable[c] == 0 || walkable[d] == 0)
            {
                return false;
            }

            float fu = u - i0;
            float fv = v - j0;

            float bottom = heights[a] + (heights[b] - heights[a]) * fu;
            float top = heights[c] + (heights[d] - heights[c]) * fu;

            height = bottom + (top - bottom) * fv;
            return true;
        }
    }
}
