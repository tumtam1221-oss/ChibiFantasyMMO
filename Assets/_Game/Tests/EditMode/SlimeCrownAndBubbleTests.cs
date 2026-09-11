using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Shared knowledge of which vertices are the slime and which are the bubbles.
    /// </summary>
    /// <remarks>
    /// Found by following the triangles rather than by position or index, because that is the
    /// same question as "are the bubbles part of the body". A bubble welded to the body stops
    /// being its own island, so every measurement taken through here fails the moment one is.
    /// </remarks>
    internal static class SlimeParts
    {
        /// <summary>Connected pieces of a mesh, biggest first.</summary>
        /// <remarks>
        /// Joined by position before by triangle. An imported mesh splits a vertex wherever its
        /// UV or its normal changes, so the seam running up the back of the body and the two
        /// poles arrive as separate indices sitting on top of each other -- follow the triangles
        /// alone and this one body comes back as twenty-odd fragments.
        /// </remarks>
        internal static List<List<int>> Islands(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            Vector3[] positions = mesh.vertices;
            var owner = new int[mesh.vertexCount];

            for (var i = 0; i < owner.Length; i++) owner[i] = i;

            var atPosition = new Dictionary<Vector3Int, int>();

            for (var i = 0; i < positions.Length; i++)
            {
                var cell = new Vector3Int(
                    Mathf.RoundToInt(positions[i].x * 100000f),
                    Mathf.RoundToInt(positions[i].y * 100000f),
                    Mathf.RoundToInt(positions[i].z * 100000f));

                if (atPosition.TryGetValue(cell, out int first)) owner[i] = first;
                else atPosition[cell] = i;
            }

            System.Func<int, int> find = null;

            find = index =>
            {
                while (owner[index] != index)
                {
                    owner[index] = owner[owner[index]];
                    index = owner[index];
                }

                return index;
            };

            for (var t = 0; t < triangles.Length; t += 3)
            {
                int a = find(triangles[t]);
                int b = find(triangles[t + 1]);
                int c = find(triangles[t + 2]);

                owner[b] = a;
                owner[c] = a;
            }

            var groups = new Dictionary<int, List<int>>();

            for (var i = 0; i < owner.Length; i++)
            {
                int root = find(i);

                if (!groups.TryGetValue(root, out List<int> bucket))
                {
                    bucket = new List<int>();
                    groups[root] = bucket;
                }

                bucket.Add(i);
            }

            var islands = new List<List<int>>(groups.Values);

            islands.Sort((x, y) => y.Count.CompareTo(x.Count));

            return islands;
        }
    }

    /// <summary>
    /// The smooth crown, the bubbles that rise out of it, and the blink.
    /// </summary>
    /// <remarks>
    /// <b>What must never come back.</b> A curl, a horn, an antenna, or a solid nub stuck to
    /// the head. Earlier models had all of those and the brief has rejected each one by name;
    /// the body's top is a plain dome and anything above it is temporary.
    ///
    /// <b>What the bubbles are now.</b> Not ornaments parked over the head -- air forming inside
    /// the jelly, pushing up, breaking the surface, floating off and vanishing. That cycle runs
    /// for about three seconds, far longer than any clip, so it lives in
    /// <see cref="MonsterJellyPresentation"/> and these tests step that component rather than
    /// sampling an animation. The single property that matters most is the last one: a bubble
    /// has to reach exactly zero and go, or it is an ornament again.
    ///
    /// <b>Why so much of this is measured off baked vertices.</b> Bones can say anything; the
    /// skin is what the player sees. The two came apart once already -- a bubble bone keyed to
    /// zero scale on the exported frame wrote a degenerate bind matrix, and the bones moved
    /// perfectly over a mesh that never budged.
    /// </remarks>
    [TestFixture]
    public sealed class SlimeCrownAndBubbleTests
    {
        private const string Slime = "monster.training_slime";

        private const string Fbx =
            "Assets/_Game/Art/Monsters/TrainingSlime/Monster_TrainingSlime.fbx";

        private const string Folder = "Assets/_Game/Art/Monsters/TrainingSlime/";

        /// <summary>The top of the body, in metres above its feet.</summary>
        private const float Crown = 0.288f;

        private static GameObject Prefab()
        {
            var visuals = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(
                "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset");

            Assert.That(visuals, Is.Not.Null);

            GameObject prefab = visuals.PrefabFor(new DefinitionId(Slime));

            Assert.That(prefab, Is.Not.Null, Slime + " resolves to no model");

            return prefab;
        }

        private static AnimationClip Clip(string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var clip = asset as AnimationClip;

                if (clip != null && clip.name == name) return clip;
            }

            Assert.Fail(name + " is not in " + Fbx);

            return null;
        }

        /// <summary>One bubble, measured off the skin.</summary>
        private struct Blob
        {
            public Vector3 Centre;

            public float Height;

            public float Width;
        }

        /// <summary>Instantiates the model and steps its presentation by hand.</summary>
        private sealed class Live : System.IDisposable
        {
            private readonly GameObject _instance;
            private readonly SkinnedMeshRenderer _skin;
            private readonly Mesh _baked = new Mesh();
            private readonly List<int>[] _bubbleVertices;
            private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

            internal readonly MonsterJellyPresentation Jelly;
            internal readonly Transform Body;
            internal readonly int Bulge;

            internal int Bubbles
            {
                get { return _bubbleVertices.Length; }
            }

            internal Live(int seed)
            {
                _instance = (GameObject)Object.Instantiate(Prefab());
                _instance.transform.position = Vector3.zero;
                _instance.transform.rotation = Quaternion.identity;

                _skin = _instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                Assert.That(_skin, Is.Not.Null);

                Jelly = _instance.GetComponent<MonsterJellyPresentation>();

                Assert.That(Jelly, Is.Not.Null,
                    "the prefab has no MonsterJellyPresentation, so nothing drives the bubbles");

                Jelly.Seed(seed);

                foreach (Transform t in _instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Body") Body = t;
                }

                Bulge = -1;

                for (var i = 0; i < _skin.sharedMesh.blendShapeCount; i++)
                {
                    if (_skin.sharedMesh.GetBlendShapeName(i) == "Crown_Bulge") Bulge = i;
                }

                // group the vertices by the bone that owns them, which is how the mesh says
                // which blob is which
                var byBone = new Dictionary<int, List<int>>();
                BoneWeight[] weights = _skin.sharedMesh.boneWeights;

                for (var i = 0; i < weights.Length; i++)
                {
                    int bone = weights[i].boneIndex0;

                    if (!_skin.bones[bone].name.StartsWith("Bubble_")) continue;

                    if (!byBone.TryGetValue(bone, out List<int> bucket))
                    {
                        bucket = new List<int>();
                        byBone[bone] = bucket;
                    }

                    bucket.Add(i);
                }

                var keys = new List<int>(byBone.Keys);

                keys.Sort();

                _bubbleVertices = new List<int>[keys.Count];

                for (var i = 0; i < keys.Count; i++) _bubbleVertices[i] = byBone[keys[i]];
            }

            internal void Tick(float seconds)
            {
                Jelly.Tick(seconds);
            }

            internal Blob Bubble(int index)
            {
                _skin.BakeMesh(_baked, true);

                Vector3[] posed = _baked.vertices;
                Transform renderer = _skin.transform;
                Transform root = _instance.transform;

                float lo = float.MaxValue, hi = float.MinValue;
                float left = float.MaxValue, right = float.MinValue;
                var centre = Vector3.zero;

                foreach (int i in _bubbleVertices[index])
                {
                    Vector3 v = root.InverseTransformPoint(renderer.TransformPoint(posed[i]));

                    centre += v;
                    if (v.y < lo) lo = v.y;
                    if (v.y > hi) hi = v.y;
                    if (v.x < left) left = v.x;
                    if (v.x > right) right = v.x;
                }

                return new Blob
                {
                    Centre = centre / _bubbleVertices[index].Count,
                    Height = hi - lo,
                    Width = right - left,
                };
            }

            internal float BulgeWeight()
            {
                return Bulge < 0 ? 0f : _skin.GetBlendShapeWeight(Bulge);
            }

            /// <summary>Which of the three face sheets is on the renderer this frame.</summary>
            internal string Face()
            {
                _skin.GetPropertyBlock(_block);

                Texture texture = _block.GetTexture("_BaseMap");

                return texture == null ? "none" : texture.name;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(_baked);
                Object.DestroyImmediate(_instance);
            }
        }

        // ---- the shape -----------------------------------------------------------------

        [Test]
        public void TheRigIsTheBodyItsCrownAndOneBonePerBubble()
        {
            var instance = (GameObject)Object.Instantiate(Prefab());

            try
            {
                var names = new List<string>();

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    names.Add(t.name);
                }

                foreach (string expected in new[]
                    { "Root", "Body", "Crown_01", "Crown_02",
                      "Bubble_1", "Bubble_2", "Bubble_3" })
                {
                    Assert.That(names, Does.Contain(expected));
                }

                foreach (string name in names)
                {
                    foreach (string banned in new[] { "Curl", "Horn", "Antenna", "Nub", "Spike" })
                    {
                        Assert.That(name, Does.Not.Contain(banned), "leftover: " + name);
                    }
                }

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                Assert.That(skinned.bones.Length, Is.EqualTo(7),
                    "the rig has " + skinned.bones.Length + " bones");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void EveryBubbleBoneActuallyDeformsSomething()
        {
            // The failure this exists for: a bubble bone keyed to zero scale on the frame the
            // FBX was exported from wrote a bind matrix Unity imported as all zeros. The bones
            // moved beautifully and not one vertex followed, and nothing else in the suite
            // noticed, because everything else was reading the bones.
            var instance = (GameObject)Object.Instantiate(Prefab());

            try
            {
                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Matrix4x4[] binds = skinned.sharedMesh.bindposes;

                for (var i = 0; i < binds.Length; i++)
                {
                    Assert.That(binds[i].GetColumn(3).w, Is.EqualTo(1f).Within(1e-4f),
                        skinned.bones[i].name + " has a degenerate bind pose, so it deforms "
                        + "nothing whatever is done to it");
                    Assert.That(binds[i].determinant, Is.Not.EqualTo(0f).Within(1e-9f),
                        skinned.bones[i].name + "'s bind pose cannot be inverted");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ThereAreThreeBubblesAndAtRestTheyAreAllInsideTheBody()
        {
            using (var live = new Live(11))
            {
                Assert.That(live.Bubbles, Is.EqualTo(3),
                    "the mesh carries " + live.Bubbles + " bubbles");

                var instance = (GameObject)Object.Instantiate(Prefab());

                try
                {
                    var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                    Assert.That(SlimeParts.Islands(skinned.sharedMesh).Count, Is.EqualTo(4),
                        "the mesh should be the body and three separate bubbles");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }

                for (var i = 0; i < live.Bubbles; i++)
                {
                    Blob blob = live.Bubble(i);

                    Assert.That(blob.Centre.y + blob.Height * 0.5f, Is.LessThan(Crown),
                        "bubble " + (i + 1) + " is modelled poking out of the top of the body, "
                        + "so a slime with no presentation on it wears it as a lump");
                }
            }
        }

        [Test]
        public void TheTopOfTheBodyIsASmoothDomeWithNothingOnIt()
        {
            // A curl, a horn or a nub all show up the same way: somewhere above the shoulder the
            // body stops narrowing and gets wider again. A plain dome never does. Measured with
            // the bulge at rest, which is the silhouette the slime wears nearly all the time.
            var instance = (GameObject)Object.Instantiate(Prefab());

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                for (var i = 0; i < skinned.sharedMesh.blendShapeCount; i++)
                {
                    skinned.SetBlendShapeWeight(i, 0f);
                }

                Clip("Slime_Idle").SampleAnimation(instance, 0f);

                List<int> body = SlimeParts.Islands(skinned.sharedMesh)[0];
                var baked = new Mesh();

                try
                {
                    skinned.BakeMesh(baked, true);

                    Vector3[] raw = baked.vertices;
                    var verts = new Vector3[raw.Length];

                    for (var i = 0; i < raw.Length; i++)
                    {
                        verts[i] = instance.transform.InverseTransformPoint(
                            skinned.transform.TransformPoint(raw[i]));
                    }

                    float lo = float.MaxValue, hi = float.MinValue;

                    foreach (int i in body)
                    {
                        if (verts[i].y < lo) lo = verts[i].y;
                        if (verts[i].y > hi) hi = verts[i].y;
                    }

                    const int Slices = 14;
                    var widest = new float[Slices];

                    foreach (int i in body)
                    {
                        float t = (verts[i].y - lo) / Mathf.Max(1e-6f, hi - lo);

                        if (t < 0.5f) continue;

                        var slice = (int)((t - 0.5f) / 0.5f * (Slices - 1));
                        float r = Mathf.Sqrt(verts[i].x * verts[i].x + verts[i].z * verts[i].z);

                        if (r > widest[slice]) widest[slice] = r;
                    }

                    for (var s = 1; s < Slices; s++)
                    {
                        Assert.That(widest[s], Is.LessThanOrEqualTo(widest[s - 1] + 1e-4f),
                            "the body widens again at " + (50 + s * 50 / (Slices - 1))
                            + "% of its height, which is a nub growing out of the crown");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheBodyIsFullerAndRounderThanItWas()
        {
            // The complaint was flat and wide. Width over height is only half the story: what
            // made it read as a mound was that its contact patch was nearly as wide as the body,
            // so the sides never curved back in. Both are held here.
            var instance = (GameObject)Object.Instantiate(Prefab());

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                Clip("Slime_Idle").SampleAnimation(instance, 0f);

                List<int> body = SlimeParts.Islands(skinned.sharedMesh)[0];
                var baked = new Mesh();

                try
                {
                    skinned.BakeMesh(baked, true);

                    Vector3[] raw = baked.vertices;
                    float lo = float.MaxValue, hi = float.MinValue;
                    float left = float.MaxValue, right = float.MinValue;
                    var verts = new Vector3[raw.Length];

                    foreach (int i in body)
                    {
                        verts[i] = instance.transform.InverseTransformPoint(
                            skinned.transform.TransformPoint(raw[i]));

                        if (verts[i].y < lo) lo = verts[i].y;
                        if (verts[i].y > hi) hi = verts[i].y;
                        if (verts[i].x < left) left = verts[i].x;
                        if (verts[i].x > right) right = verts[i].x;
                    }

                    float tall = hi - lo;
                    float wide = right - left;
                    float widest = 0f;
                    float patch = 0f;

                    foreach (int i in body)
                    {
                        float r = Mathf.Sqrt(verts[i].x * verts[i].x + verts[i].z * verts[i].z);

                        if (r > widest) widest = r;
                        if (verts[i].y < lo + 0.003f && r > patch) patch = r;
                    }

                    Assert.That(wide / tall, Is.GreaterThan(1.12f).And.LessThan(1.34f),
                        "it is " + (wide / tall) + " times as wide as it is tall: under 1.12 is "
                        + "an egg, over 1.34 is the mound this was meant to stop being");
                    Assert.That(patch / widest, Is.LessThan(0.72f),
                        "the base is " + (patch / widest * 100f).ToString("0")
                        + "% of the widest radius, so the sides drop almost straight down "
                        + "instead of curving in");
                    Assert.That(patch / widest, Is.GreaterThan(0.35f),
                        "the base is too small to look like it is resting on the ground");
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---- the lifecycle -------------------------------------------------------------

        [Test]
        public void EveryBubbleFormsInsideRisesOutAndDisappears()
        {
            // The whole brief for the bubbles, in one pass: each one has to be nothing, then
            // something inside the body, then out above the crown, then nothing again.
            using (var live = new Live(11))
            {
                var lowest = new float[live.Bubbles];
                var highest = new float[live.Bubbles];
                var smallest = new float[live.Bubbles];
                var largest = new float[live.Bubbles];
                var bornInside = new bool[live.Bubbles];
                var rosePast = new bool[live.Bubbles];

                for (var i = 0; i < live.Bubbles; i++)
                {
                    lowest[i] = float.MaxValue;
                    smallest[i] = float.MaxValue;
                }

                // twelve seconds is more than two full cycles of the longest-lived bubble
                for (var f = 0; f < 360; f++)
                {
                    live.Tick(1f / 30f);

                    for (var i = 0; i < live.Bubbles; i++)
                    {
                        Blob blob = live.Bubble(i);

                        lowest[i] = Mathf.Min(lowest[i], blob.Centre.y);
                        highest[i] = Mathf.Max(highest[i], blob.Centre.y);
                        smallest[i] = Mathf.Min(smallest[i], blob.Height);
                        largest[i] = Mathf.Max(largest[i], blob.Height);

                        if (blob.Height > 0.004f && blob.Height < 0.022f
                            && blob.Centre.y < Crown) bornInside[i] = true;
                        if (blob.Height > 0.020f && blob.Centre.y > Crown + 0.02f)
                            rosePast[i] = true;
                    }
                }

                for (var i = 0; i < live.Bubbles; i++)
                {
                    Assert.That(smallest[i], Is.LessThan(0.0005f),
                        "bubble " + (i + 1) + " never shrinks below "
                        + (smallest[i] * 1000f).ToString("0.0") + " mm, so it never disappears "
                        + "-- it is a permanent speck over the slime's head");
                    Assert.That(largest[i], Is.GreaterThan(0.020f),
                        "bubble " + (i + 1) + " never grows past "
                        + (largest[i] * 1000f).ToString("0") + " mm, so nobody will see it");
                    Assert.That(bornInside[i], Is.True,
                        "bubble " + (i + 1) + " is never small and inside the body, so it does "
                        + "not form in there -- it just appears");
                    Assert.That(rosePast[i], Is.True,
                        "bubble " + (i + 1) + " never rises clear of the crown at a visible size");
                    Assert.That(highest[i], Is.GreaterThan(lowest[i] + 0.15f),
                        "bubble " + (i + 1) + " only travels "
                        + ((highest[i] - lowest[i]) * 1000f).ToString("0") + " mm");
                }
            }
        }

        [Test]
        public void TheBubblesTakeTurnsRatherThanAllArrivingAtOnce()
        {
            using (var live = new Live(11))
            {
                var atOnce = new int[live.Bubbles + 1];

                for (var f = 0; f < 900; f++)
                {
                    live.Tick(1f / 30f);

                    var visible = 0;

                    for (var i = 0; i < live.Bubbles; i++)
                    {
                        if (live.Bubble(i).Height > 0.008f) visible++;
                    }

                    atOnce[visible]++;
                }

                Assert.That(atOnce[live.Bubbles], Is.LessThan(900 * 0.25f),
                    "all " + live.Bubbles + " bubbles are out together for "
                    + (atOnce[live.Bubbles] / 9f).ToString("0") + "% of the time, which is a "
                    + "fountain rather than a slime breathing");
                Assert.That(atOnce[1] + atOnce[2], Is.GreaterThan(900 * 0.5f),
                    "the usual picture should be one or two bubbles");
            }
        }

        [Test]
        public void TheCrownSwellsAsABubbleLeavesAndSettlesBackToNothing()
        {
            using (var live = new Live(11))
            {
                Assert.That(live.Bulge, Is.GreaterThanOrEqualTo(0),
                    "the mesh has no Crown_Bulge shape, so the surface cannot react");

                var peak = 0f;
                var rested = 0;

                for (var f = 0; f < 360; f++)
                {
                    live.Tick(1f / 30f);

                    float weight = live.BulgeWeight();

                    peak = Mathf.Max(peak, weight);
                    if (weight < 0.5f) rested++;
                }

                Assert.That(peak, Is.GreaterThan(20f),
                    "the crown never swells more than " + peak + ", so nothing reacts");
                Assert.That(peak, Is.LessThan(85f),
                    "the crown swells to " + peak + ", which is a horn rather than a swell");
                Assert.That(rested, Is.GreaterThan(360 / 3),
                    "the crown is pushed up for all but " + rested + " frames in twelve "
                    + "seconds, which is a permanent bump");
            }
        }

        [Test]
        public void ABubbleThatIsOutLagsBehindTheBodyWhenItJumps()
        {
            // Item ten of the brief. Easiest to see by moving the body and nothing else: a
            // bubble that has left the slime should not arrive with it.
            using (var live = new Live(11))
            {
                // run on until one is well clear of the crown
                var index = -1;

                for (var f = 0; f < 360 && index < 0; f++)
                {
                    live.Tick(1f / 30f);

                    for (var i = 0; i < live.Bubbles; i++)
                    {
                        Blob blob = live.Bubble(i);

                        if (blob.Height > 0.030f && blob.Centre.y > Crown + 0.05f) index = i;
                    }
                }

                Assert.That(index, Is.GreaterThanOrEqualTo(0), "no bubble ever got clear");

                float before = live.Bubble(index).Centre.y;

                // the body leaps
                live.Body.position += Vector3.up * 0.20f;
                live.Tick(1f / 30f);

                float after = live.Bubble(index).Centre.y;

                Assert.That(after - before, Is.LessThan(0.20f * 0.6f),
                    "the bubble moved " + (after - before) + " m in the frame the body moved "
                    + "0.20 m, so it is following rigidly rather than trailing");
                Assert.That(after - before, Is.GreaterThan(0f),
                    "the bubble ignored the body completely");
            }
        }

        // ---- the face ------------------------------------------------------------------

        [Test]
        public void ThereIsOneFaceAndItsEyesAreSmallAndDark()
        {
            byte[] bytes = System.IO.File.ReadAllBytes(
                Folder + "Monster_TrainingSlime_albedo.png");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                Assert.That(texture.LoadImage(bytes), Is.True);

                Color32[] pixels = texture.GetPixels32();
                int width = texture.width, height = texture.height;

                var ink = 0;
                float leftMost = 1f, rightMost = 0f;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        Color32 p = pixels[y * width + x];

                        if (p.r > 70 || p.g > 70 || p.b > 130) continue;

                        ink++;

                        float u = (x + 0.5f) / width;

                        if (u < leftMost) leftMost = u;
                        if (u > rightMost) rightMost = u;
                    }
                }

                Assert.That(ink, Is.GreaterThan(200), "there is no face painted at all");

                // u = 0.5 is the front and the back is at both edges, so ink outside the middle
                // half is a second face on the back of its head
                Assert.That(leftMost, Is.GreaterThan(0.25f));
                Assert.That(rightMost, Is.LessThan(0.75f));
                Assert.That(ink / (float)(width * height), Is.LessThan(0.0065f),
                    "the face covers too much of the sheet; the eyes have grown back");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TheBlinkSheetsChangeTheEyesAndNothingElse()
        {
            // Three complete face sheets, swapped by the presentation. They have to differ
            // where the eyes are and match everywhere else, or a blink moves the mouth too.
            var open = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var shut = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var half = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                Assert.That(open.LoadImage(
                    System.IO.File.ReadAllBytes(Folder + "Monster_TrainingSlime_albedo.png")),
                    Is.True);
                Assert.That(shut.LoadImage(System.IO.File.ReadAllBytes(
                    Folder + "Monster_TrainingSlime_albedo_blink.png")), Is.True,
                    "there is no shut-eye sheet");
                Assert.That(half.LoadImage(System.IO.File.ReadAllBytes(
                    Folder + "Monster_TrainingSlime_albedo_blink_half.png")), Is.True,
                    "there is no half-shut sheet");

                Assert.That(shut.width, Is.EqualTo(open.width));
                Assert.That(half.width, Is.EqualTo(open.width));

                Color32[] a = open.GetPixels32();
                Color32[] b = shut.GetPixels32();
                int width = open.width, height = open.height;

                var changedInside = 0;
                var changedOutside = 0;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        int i = y * width + x;

                        if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g)
                            + Mathf.Abs(a[i].b - b[i].b) < 24) continue;

                        // the eyes live a little under half way up, in the middle of the sheet
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;

                        if (v > 0.36f && v < 0.62f && u > 0.30f && u < 0.70f) changedInside++;
                        else changedOutside++;
                    }
                }

                Assert.That(changedInside, Is.GreaterThan(400),
                    "the shut sheet barely differs from the open one");
                Assert.That(changedOutside, Is.LessThan(changedInside / 20),
                    "the blink changes " + changedOutside + " pixels away from the eyes, so it "
                    + "is moving the mouth or the cheeks too");
            }
            finally
            {
                Object.DestroyImmediate(open);
                Object.DestroyImmediate(shut);
                Object.DestroyImmediate(half);
            }
        }

        [Test]
        public void ItBlinksOccasionallyAndQuickly()
        {
            using (var live = new Live(11))
            {
                var starts = new List<float>();
                var lengths = new List<float>();
                var shutFor = 0;
                var wasOpen = true;
                var clock = 0f;

                for (var f = 0; f < 1800; f++)      // one minute
                {
                    live.Tick(1f / 30f);
                    clock += 1f / 30f;

                    bool open = live.Face().EndsWith("_albedo");

                    if (!open)
                    {
                        if (wasOpen) starts.Add(clock);
                        shutFor++;
                    }
                    else if (!wasOpen)
                    {
                        lengths.Add(shutFor / 30f);
                        shutFor = 0;
                    }

                    wasOpen = open;
                }

                Assert.That(starts.Count, Is.GreaterThan(9).And.LessThan(30),
                    "it blinked " + starts.Count + " times in a minute");

                foreach (float length in lengths)
                {
                    Assert.That(length, Is.GreaterThanOrEqualTo(0.10f).And.LessThanOrEqualTo(0.27f),
                        "a blink lasted " + length + " s");
                }

                var quick = 0;

                for (var i = 1; i < starts.Count; i++)
                {
                    float gap = starts[i] - starts[i - 1];

                    if (gap < 1f) { quick++; continue; }

                    Assert.That(gap, Is.GreaterThanOrEqualTo(2.4f).And.LessThanOrEqualTo(5.4f),
                        "there were " + gap + " s between blinks");
                }

                Assert.That(quick, Is.LessThan(starts.Count / 3),
                    quick + " of " + starts.Count + " blinks came straight after another one; "
                    + "a double blink is meant to be occasional");
            }
        }
    }
}
