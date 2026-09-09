using System.Collections.Generic;
using ChibiFantasy.Data;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Finds a walking route across a map's baked ground, around whatever is in the way.
    /// </summary>
    /// <remarks>
    /// <b>Client-side steering, not authority.</b> The server still moves a character one
    /// straight step at a time and refuses a step into a rock or a wall. What this adds is
    /// the route the client steers along so those refusals stop happening: a click behind a
    /// kiosk becomes a walk around the kiosk. Nothing here can put a character anywhere the
    /// server would not have allowed -- every waypoint is a cell the same
    /// <see cref="MapHeightField"/> the server samples calls walkable.
    ///
    /// <b>A* on the height field's grid.</b> Eight-connected, octile heuristic, with the
    /// walkable mask eroded by one cell so a route keeps a character's width from walls and
    /// never clips a corner. The result is string-pulled: consecutive waypoints that see
    /// each other across walkable cells are merged, so a straight corridor is one leg.
    ///
    /// <b>A goal you cannot stand on is moved, not refused.</b> Clicking a tree, a table or
    /// the water walks to the nearest walkable cell within a few metres, which is what a
    /// player means by such a click. Only a goal with no walkable ground anywhere near it,
    /// or one that is genuinely cut off, fails -- and the caller then falls back to the
    /// straight line it always used.
    /// </remarks>
    public static class GridPathfinder
    {
        /// <summary>
        /// How far a blocked goal is moved to find standable ground, in cells.
        /// </summary>
        /// <remarks>Twelve metres at half-metre cells. A click usually lands on a building,
        /// and a building is wider than a few cells: searching only its own width finds
        /// nothing standable, the route fails, and the character walks straight into the
        /// wall instead of up to the door.</remarks>
        public const int GoalSearchCells = 24;

        /// <summary>
        /// Upper bound on nodes expanded, so a hopeless search cannot stall a frame.
        /// </summary>
        /// <remarks>Generous, because a node now costs one array lookup rather than five
        /// height samples: a budget tight enough to matter would abandon ordinary journeys
        /// across the map and drop the player back to walking into walls.</remarks>
        public const int DefaultMaxExpansions = 120000;

        /// <summary>
        /// Whether a straight walk from here to there stays on standable ground.
        /// </summary>
        /// <remarks>The common case by far -- open street, open field -- and answering it
        /// costs a few dozen samples instead of a search. A caller that gets true should
        /// steer straight and plan nothing.</remarks>
        public static bool IsStraightWalkable(MapHeightField field, Vector2 from, Vector2 to)
        {
            if (field == null || !field.IsValid) return false;

            var grid = Grid.For(field);

            if (!grid.TryCell(from, out int sx, out int sz)) return false;
            if (!grid.TryCell(to, out int gx, out int gz)) return false;

            return grid.Standable(gx, gz)
                && grid.LineIsClear(new Vector2Int(sx, sz), new Vector2Int(gx, gz));
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] StepCost = { 1f, 1f, 1f, 1f, 1.41421f, 1.41421f, 1.41421f, 1.41421f };

        /// <summary>
        /// Plans a route from <paramref name="start"/> to <paramref name="goal"/> (world X/Z).
        /// </summary>
        /// <param name="field">The map's baked ground.</param>
        /// <param name="start">Where the character stands.</param>
        /// <param name="goal">Where the click landed.</param>
        /// <param name="waypoints">Filled with the intermediate corners to steer through,
        /// then the (possibly adjusted) goal as the last entry. Cleared first.</param>
        /// <param name="maxExpansions">Search budget.</param>
        /// <returns>False when there is no field, no standable ground near the goal, or no
        /// route within the budget. The caller should then steer straight, as before.</returns>
        public static bool TryFindPath(MapHeightField field, Vector2 start, Vector2 goal,
            List<Vector2> waypoints, int maxExpansions = DefaultMaxExpansions)
        {
            if (waypoints == null) return false;

            waypoints.Clear();

            if (field == null || !field.IsValid) return false;

            var grid = Grid.For(field);

            if (!grid.TryCell(start, out int sx, out int sz)) return false;
            if (!grid.TryCell(goal, out int gx, out int gz)) return false;

            // The character is where it is, even if the erosion would disallow that cell.
            if (!grid.Standable(gx, gz) && !grid.TryNearestStandable(gx, gz, GoalSearchCells, out gx, out gz))
            {
                return false;
            }

            if (sx == gx && sz == gz)
            {
                waypoints.Add(grid.Centre(gx, gz));
                return true;
            }

            var cells = new List<Vector2Int>();

            if (!grid.Search(sx, sz, gx, gz, maxExpansions, cells)) return false;

            grid.Simplify(cells, waypoints);

            return waypoints.Count > 0;
        }

        /// <summary>The height field seen as a grid of standable cells.</summary>
        /// <remarks>
        /// <b>Built once per map.</b> Asking the field whether a cell is standable costs five
        /// samples, and a search asks it tens of thousands of times; doing that on the click
        /// frame is what a player feels as a stutter. The mask is computed the first time a
        /// map is used and kept until a different field arrives, which turns every later
        /// question into one array lookup.
        /// </remarks>
        private sealed class Grid
        {
            private static MapHeightField _cachedField;
            private static Grid _cached;

            private readonly int _columns;
            private readonly int _rows;
            private readonly float _cell;
            private readonly float _originX;
            private readonly float _originZ;
            private readonly bool[] _walkable;
            private readonly bool[] _standable;

            /// <summary>The grid for a field, reusing the last one when it is the same map.</summary>
            public static Grid For(MapHeightField field)
            {
                if (_cached != null && ReferenceEquals(_cachedField, field)) return _cached;

                _cachedField = field;
                _cached = new Grid(field);

                return _cached;
            }

            private Grid(MapHeightField field)
            {
                _columns = field.Columns;
                _rows = field.Rows;
                _cell = field.CellSize;
                _originX = field.OriginX;
                _originZ = field.OriginZ;

                int total = _columns * _rows;
                _walkable = new bool[total];
                _standable = new bool[total];

                for (int z = 0; z < _rows; z++)
                for (int x = 0; x < _columns; x++)
                {
                    _walkable[z * _columns + x] =
                        field.TrySample(_originX + x * _cell, _originZ + z * _cell, out _);
                }

                // A body needs a cell of clearance on every side, so it never clips a corner
                // or scrapes along a wall the server would refuse it against.
                for (int z = 0; z < _rows; z++)
                for (int x = 0; x < _columns; x++)
                {
                    _standable[z * _columns + x] = Walkable(x, z) && Walkable(x + 1, z)
                        && Walkable(x - 1, z) && Walkable(x, z + 1) && Walkable(x, z - 1);
                }
            }

            public bool TryCell(Vector2 world, out int cx, out int cz)
            {
                cx = Mathf.RoundToInt((world.x - _originX) / _cell);
                cz = Mathf.RoundToInt((world.y - _originZ) / _cell);

                return cx >= 0 && cz >= 0 && cx < _columns && cz < _rows;
            }

            public Vector2 Centre(int cx, int cz)
            {
                return new Vector2(_originX + cx * _cell, _originZ + cz * _cell);
            }

            private bool Walkable(int cx, int cz)
            {
                if (cx < 0 || cz < 0 || cx >= _columns || cz >= _rows) return false;

                return _walkable[cz * _columns + cx];
            }

            /// <summary>Walkable with a cell of clearance on every side: room for a body.</summary>
            public bool Standable(int cx, int cz)
            {
                if (cx < 0 || cz < 0 || cx >= _columns || cz >= _rows) return false;

                return _standable[cz * _columns + cx];
            }

            /// <summary>Whether a straight line between two cells stays standable.</summary>
            public bool LineIsClear(Vector2Int a, Vector2Int b)
            {
                return LineClear(a, b);
            }

            /// <summary>
            /// The closest spot near a blocked goal that a character could actually stand on.
            /// </summary>
            /// <remarks>Open ground first. The gap between a building and its neighbour is
            /// often one cell wide and standable but reachable from nowhere -- aiming a route
            /// at such a sliver fails the whole search, and the player is told there is no way
            /// to a door they can plainly see. A cell with room around it is a place somebody
            /// can walk to; only if the search finds none does any standable cell do.</remarks>
            public bool TryNearestStandable(int cx, int cz, int radius, out int nx, out int nz)
            {
                if (TryNearestStandable(cx, cz, radius, 6, out nx, out nz)) return true;

                return TryNearestStandable(cx, cz, radius, 0, out nx, out nz);
            }

            private bool TryNearestStandable(int cx, int cz, int radius, int neighboursNeeded,
                out int nx, out int nz)
            {
                nx = cx;
                nz = cz;

                for (int r = 1; r <= radius; r++)
                {
                    int bestX = 0, bestZ = 0;
                    float best = float.MaxValue;

                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;   // ring only

                        int x = cx + dx, z = cz + dz;

                        if (!Standable(x, z)) continue;
                        if (neighboursNeeded > 0 && OpenNeighbours(x, z) < neighboursNeeded) continue;

                        float d = dx * dx + dz * dz;

                        if (d < best)
                        {
                            best = d;
                            bestX = x;
                            bestZ = z;
                        }
                    }

                    if (best < float.MaxValue)
                    {
                        nx = bestX;
                        nz = bestZ;
                        return true;
                    }
                }

                return false;
            }

            private int OpenNeighbours(int cx, int cz)
            {
                int open = 0;

                for (int k = 0; k < 8; k++)
                {
                    if (Standable(cx + Dx[k], cz + Dz[k])) open++;
                }

                return open;
            }

            public bool Search(int sx, int sz, int gx, int gz, int maxExpansions,
                List<Vector2Int> cells)
            {
                // Search only the neighbourhood of the journey. Without this, a goal that is
                // walled in sends the search across the whole map before admitting defeat --
                // tens of milliseconds the player feels as a stutter on the click. The
                // margin is wide enough to walk right around anything a town contains.
                const int Margin = 70;

                int minX = Mathf.Max(0, Mathf.Min(sx, gx) - Margin);
                int maxX = Mathf.Min(_columns - 1, Mathf.Max(sx, gx) + Margin);
                int minZ = Mathf.Max(0, Mathf.Min(sz, gz) - Margin);
                int maxZ = Mathf.Min(_rows - 1, Mathf.Max(sz, gz) + Margin);

                int total = _columns * _rows;
                var gScore = new float[total];
                var cameFrom = new int[total];
                var closed = new bool[total];

                for (int i = 0; i < total; i++)
                {
                    gScore[i] = float.MaxValue;
                    cameFrom[i] = -1;
                }

                var open = new BinaryHeap();

                int startIndex = sz * _columns + sx;
                int goalIndex = gz * _columns + gx;

                gScore[startIndex] = 0f;
                open.Push(startIndex, Heuristic(sx, sz, gx, gz));

                int expansions = 0;

                while (open.Count > 0 && expansions < maxExpansions)
                {
                    int current = open.Pop();

                    if (closed[current]) continue;

                    closed[current] = true;
                    expansions++;

                    if (current == goalIndex)
                    {
                        Reconstruct(cameFrom, current, cells);
                        return true;
                    }

                    int cx = current % _columns;
                    int cz = current / _columns;

                    for (int k = 0; k < 8; k++)
                    {
                        int nx = cx + Dx[k], nz = cz + Dz[k];

                        if (nx < minX || nz < minZ || nx > maxX || nz > maxZ) continue;

                        int next = nz * _columns + nx;

                        if (closed[next]) continue;

                        // The start may sit inside the eroded band (against a wall); every
                        // other cell on the route must have clearance.
                        if (next != goalIndex && !Standable(nx, nz)) continue;

                        // No corner cutting: a diagonal needs both orthogonal neighbours.
                        if (k >= 4 && (!Walkable(cx + Dx[k], cz) || !Walkable(cx, cz + Dz[k]))) continue;

                        float tentative = gScore[current] + StepCost[k];

                        if (tentative >= gScore[next]) continue;

                        gScore[next] = tentative;
                        cameFrom[next] = current;
                        open.Push(next, tentative + Heuristic(nx, nz, gx, gz));
                    }
                }

                return false;
            }

            private static float Heuristic(int x, int z, int gx, int gz)
            {
                int dx = Mathf.Abs(x - gx), dz = Mathf.Abs(z - gz);
                int diag = Mathf.Min(dx, dz);

                return (dx + dz) - diag * (2f - 1.41421f);
            }

            private void Reconstruct(int[] cameFrom, int end, List<Vector2Int> cells)
            {
                cells.Clear();

                for (int i = end; i >= 0; i = cameFrom[i])
                {
                    cells.Add(new Vector2Int(i % _columns, i / _columns));
                }

                cells.Reverse();
            }

            /// <summary>
            /// Merges runs of cells that see each other into single legs.
            /// </summary>
            /// <remarks>Greedy string pulling: from the current corner, keep the farthest cell
            /// reachable in a straight line over standable ground. The last cell is always
            /// kept, so the route still ends exactly at the goal.</remarks>
            public void Simplify(List<Vector2Int> cells, List<Vector2> waypoints)
            {
                waypoints.Clear();

                if (cells.Count == 0) return;

                int anchor = 0;

                while (anchor < cells.Count - 1)
                {
                    int farthest = anchor + 1;

                    for (int i = cells.Count - 1; i > anchor + 1; i--)
                    {
                        if (LineClear(cells[anchor], cells[i]))
                        {
                            farthest = i;
                            break;
                        }
                    }

                    waypoints.Add(Centre(cells[farthest].x, cells[farthest].y));
                    anchor = farthest;
                }

                if (waypoints.Count == 0) waypoints.Add(Centre(cells[cells.Count - 1].x, cells[cells.Count - 1].y));
            }

            private bool LineClear(Vector2Int a, Vector2Int b)
            {
                int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) * 2;

                for (int i = 1; i < steps; i++)
                {
                    float t = i / (float)steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                    int z = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));

                    if (!Standable(x, z)) return false;
                }

                return true;
            }
        }

        /// <summary>A minimal min-heap of (index, priority).</summary>
        private sealed class BinaryHeap
        {
            private readonly List<int> _items = new List<int>();
            private readonly List<float> _priorities = new List<float>();

            public int Count => _items.Count;

            public void Push(int item, float priority)
            {
                _items.Add(item);
                _priorities.Add(priority);

                int i = _items.Count - 1;

                while (i > 0)
                {
                    int parent = (i - 1) / 2;

                    if (_priorities[parent] <= _priorities[i]) break;

                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                int last = _items.Count - 1;

                _items[0] = _items[last];
                _priorities[0] = _priorities[last];
                _items.RemoveAt(last);
                _priorities.RemoveAt(last);

                int i = 0;

                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;

                    if (left < _items.Count && _priorities[left] < _priorities[smallest]) smallest = left;
                    if (right < _items.Count && _priorities[right] < _priorities[smallest]) smallest = right;

                    if (smallest == i) break;

                    Swap(i, smallest);
                    i = smallest;
                }

                return top;
            }

            private void Swap(int a, int b)
            {
                int item = _items[a];
                _items[a] = _items[b];
                _items[b] = item;

                float priority = _priorities[a];
                _priorities[a] = _priorities[b];
                _priorities[b] = priority;
            }
        }
    }
}
