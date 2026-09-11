using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Client.World;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A click behind an obstacle becomes a route around it, over the same ground the server
    /// samples -- and a click on something you cannot stand on walks to the nearest spot you can.
    /// </summary>
    public sealed class GridPathfinderTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>A 40 x 40 m field at one-metre cells; <paramref name="blocked"/> marks cells unwalkable.</summary>
        private MapHeightField Field(System.Func<int, int, bool> blocked)
        {
            const int n = 41;
            var field = ScriptableObject.CreateInstance<MapHeightField>();
            var heights = new float[n * n];
            var walkable = new byte[n * n];

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                walkable[z * n + x] = blocked != null && blocked(x, z) ? (byte)0 : (byte)1;
            }

            field.Initialize(-20f, -20f, 1f, n, n, heights, walkable);
            _created.Add(field);
            return field;
        }

        private static bool Blocked(MapHeightField field, Vector2 p)
        {
            return !field.TrySample(p.x, p.y, out _);
        }

        [Test]
        public void Open_ground_is_one_straight_leg()
        {
            MapHeightField field = Field(null);
            var path = new List<Vector2>();

            Assert.That(GridPathfinder.TryFindPath(field, new Vector2(-10f, 0f), new Vector2(10f, 0f), path), Is.True);
            Assert.That(path.Count, Is.EqualTo(1), "nothing in the way: the goal is the only waypoint");
            Assert.That(path[0], Is.EqualTo(new Vector2(10f, 0f)));
        }

        [Test]
        public void A_wall_with_a_gap_is_walked_through_the_gap()
        {
            // A wall along x = 0 from z = -20 to z = 12, open above z = 12.
            MapHeightField field = Field((x, z) => x == 20 && z < 33);
            var path = new List<Vector2>();

            Assert.That(GridPathfinder.TryFindPath(field, new Vector2(-10f, 0f), new Vector2(10f, 0f), path), Is.True);
            Assert.That(path.Count, Is.GreaterThan(1), "it must turn at least once");

            // Every leg stays on walkable ground, sampled finely.
            Vector2 previous = new Vector2(-10f, 0f);

            foreach (Vector2 corner in path)
            {
                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    Vector2 p = Vector2.Lerp(previous, corner, t);
                    Assert.That(Blocked(field, p), Is.False, "leg crosses the wall at " + p);
                }

                previous = corner;
            }

            Assert.That(path[path.Count - 1], Is.EqualTo(new Vector2(10f, 0f)));
            Assert.That(previous.y, Is.EqualTo(0f), "and it arrives exactly where the click landed");
        }

        [Test]
        public void A_goal_on_an_obstacle_walks_to_the_nearest_standable_ground()
        {
            // A 3 x 3 rock centred at (5, 5).
            MapHeightField field = Field((x, z) => Mathf.Abs(x - 25) <= 1 && Mathf.Abs(z - 25) <= 1);
            var path = new List<Vector2>();

            Assert.That(GridPathfinder.TryFindPath(field, new Vector2(-5f, 5f), new Vector2(5f, 5f), path), Is.True);

            Vector2 end = path[path.Count - 1];

            Assert.That(Blocked(field, end), Is.False, "the adjusted goal is standable");
            Assert.That(Vector2.Distance(end, new Vector2(5f, 5f)), Is.LessThan(4f),
                "and close to where the player pointed");
        }

        [Test]
        public void A_sealed_goal_has_no_route()
        {
            // A ring of rock around (10, 10), three cells thick so no erosion sneaks through.
            MapHeightField field = Field((x, z) =>
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(30f, 30f));
                return d >= 5f && d <= 8f;
            });
            var path = new List<Vector2>();

            Assert.That(GridPathfinder.TryFindPath(field, new Vector2(-10f, -10f), new Vector2(10f, 10f), path), Is.False);
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void No_field_means_no_route_and_the_caller_steers_straight()
        {
            var path = new List<Vector2> { new Vector2(1f, 1f) };

            Assert.That(GridPathfinder.TryFindPath(null, Vector2.zero, Vector2.one, path), Is.False);
            Assert.That(path, Is.Empty, "a failed plan leaves nothing stale behind");
        }
    }
}
