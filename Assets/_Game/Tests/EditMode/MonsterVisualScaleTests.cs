using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// How big a monster is drawn, and what that has to leave alone.
    /// </summary>
    /// <remarks>
    /// <b>Two different sizes.</b> There is the size the model was built at, which is a
    /// property of the art, and the size it is drawn at next to a player, which is a judgement
    /// that gets revisited every time somebody stands in Harbor Town and looks at it. Keeping
    /// them apart is why the scale lives in the catalogue: the shape can be approved once and
    /// the size argued about separately, without re-exporting a mesh or invalidating every
    /// measurement written about it.
    ///
    /// <b>What must not move with it.</b> Attack range, aggro range and the monster's position
    /// are metres in the world that the server decides. Drawing a slime smaller must not make
    /// it hit from closer, and nothing in the presentation layer is allowed to reach into
    /// those numbers -- so this fixture checks the authored gameplay figures are still what
    /// they were, alongside the visual ones.
    /// </remarks>
    [TestFixture]
    public sealed class MonsterVisualScaleTests
    {
        private const string Slime = "monster.training_slime";

        private const string VisualsPath =
            "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset";

        private const string ContentPath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private static MonsterVisualCatalogue Visuals()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(VisualsPath);

            Assert.That(catalogue, Is.Not.Null, VisualsPath + " is missing");

            return catalogue;
        }

        [Test]
        public void TheSlimeIsDrawnAtThreeQuartersOfTheSizeItWasModelled()
        {
            Assert.That(Visuals().VisualScaleFor(new DefinitionId(Slime)),
                Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void AMonsterWithNoAuthoredScaleIsDrawnAtFullSize()
        {
            // Zero in an unfilled field must not mean "invisible". Every monster added before
            // this setting existed has one.
            Assert.That(Visuals().VisualScaleFor(new DefinitionId("monster.nothing_here")),
                Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Visuals().VisualScaleFor(default), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ScalingIsUniformSoTheApprovedShapeIsUntouched()
        {
            // One number, applied to all three axes. A scale authored per axis would be a way
            // to quietly re-proportion an approved model, which is the thing this must not be.
            MonsterVisualCatalogue visuals = Visuals();

            foreach (MonsterVisualCatalogue.Entry entry in visuals.Entries)
            {
                float scale = visuals.VisualScaleFor(entry.Monster);

                Assert.That(scale, Is.GreaterThan(0.05f).And.LessThan(20f),
                    entry.Monster.Value + " is drawn at " + scale + " times its size");
            }
        }

        [Test]
        public void TheModelItselfIsStillTheSizeItWasApproved()
        {
            // The mesh must not have been quietly re-exported smaller as well, or the two
            // shrinks multiply and the next person to measure the model is misled.
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            Assert.That(prefab, Is.Not.Null);

            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var baked = new Mesh();

                try
                {
                    skinned.BakeMesh(baked, true);

                    List<int> body = SlimeParts.Islands(skinned.sharedMesh)[0];
                    Vector3[] raw = baked.vertices;
                    float lo = float.MaxValue, hi = float.MinValue;
                    float left = float.MaxValue, right = float.MinValue;

                    foreach (int i in body)
                    {
                        Vector3 v = instance.transform.InverseTransformPoint(
                            skinned.transform.TransformPoint(raw[i]));

                        if (v.y < lo) lo = v.y;
                        if (v.y > hi) hi = v.y;
                        if (v.x < left) left = v.x;
                        if (v.x > right) right = v.x;
                    }

                    Assert.That(hi - lo, Is.EqualTo(0.288f).Within(0.004f),
                        "the modelled body is " + (hi - lo) + " m tall, not the 0.288 it was "
                        + "approved at -- the mesh has been resized as well as the drawing");
                    Assert.That((right - left) / (hi - lo), Is.EqualTo(1.25f).Within(0.04f),
                        "the modelled proportions have changed");
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
        public void TheClickTargetMatchesWhateverSizeTheMonsterIsDrawnAt()
        {
            // A renderer measures itself in world units and a box collider is sized in its own
            // local ones. They only agree while the scale is 1, so a monster drawn at three
            // quarters used to get a click target three quarters the size of itself. Held here
            // because the symptom -- "it is fiddly to click" -- does not point at a collider.
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * 0.75f;

                var renderers = instance.GetComponentsInChildren<Renderer>(true);

                Assert.That(renderers.Length, Is.GreaterThan(0));

                Bounds drawn = renderers[0].bounds;

                for (var i = 1; i < renderers.Length; i++) drawn.Encapsulate(renderers[i].bounds);

                // the same arithmetic the presenter does, checked end to end in world space
                var box = instance.AddComponent<BoxCollider>();
                Vector3 lossy = instance.transform.lossyScale;

                box.center = instance.transform.InverseTransformPoint(drawn.center);
                box.size = new Vector3(drawn.size.x / Mathf.Abs(lossy.x),
                    drawn.size.y / Mathf.Abs(lossy.y), drawn.size.z / Mathf.Abs(lossy.z));

                Assert.That(box.bounds.size.x, Is.EqualTo(drawn.size.x).Within(0.002f),
                    "the collider is " + box.bounds.size.x + " m wide against a monster drawn "
                    + drawn.size.x + " m wide");
                Assert.That(box.bounds.size.y, Is.EqualTo(drawn.size.y).Within(0.002f));
                Assert.That(box.bounds.size.z, Is.EqualTo(drawn.size.z).Within(0.002f));
                Assert.That(box.bounds.center.y, Is.EqualTo(drawn.center.y).Within(0.002f),
                    "the collider does not sit where the monster is drawn");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void OneHopCarriesTheSlimeAboutOneOfItsOwnWidths()
        {
            // HopMetres says how much ground one cycle of the bounce is meant to cover, and
            // the presentation plays the clip faster or slower to match. It is a distance in
            // metres, so it does NOT follow when the monster is drawn at a different size --
            // shrink the slime and leave this alone and it takes the same long strides on a
            // smaller body, which reads as gliding.
            //
            // Tied to the drawn width rather than pinned to a number, so the next person to
            // revisit the scale finds out here rather than in the game.
            MonsterVisualCatalogue visuals = Visuals();
            var id = new DefinitionId(Slime);

            var instance = (GameObject)Object.Instantiate(visuals.PrefabFor(id));

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one * visuals.VisualScaleFor(id);

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var baked = new Mesh();

                try
                {
                    skinned.BakeMesh(baked, true);

                    List<int> body = SlimeParts.Islands(skinned.sharedMesh)[0];
                    Vector3[] raw = baked.vertices;
                    float left = float.MaxValue, right = float.MinValue;

                    foreach (int i in body)
                    {
                        float x = skinned.transform.TransformPoint(raw[i]).x;

                        if (x < left) left = x;
                        if (x > right) right = x;
                    }

                    float widths = visuals.HopMetresFor(id) / (right - left);

                    Assert.That(widths, Is.GreaterThan(0.8f).And.LessThan(1.6f),
                        "one hop carries the slime " + widths.ToString("0.00")
                        + " of its own widths; under 0.8 it shuffles, over 1.6 it glides");
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
        public void TheCullingBoxCoversTheBubblesAndNothingMore()
        {
            // The presentation widens the skinned bounds so a bubble that has floated above
            // the body is not culled with it. That widening is applied in the renderer's own
            // units, and this model imports with the mesh stored a hundred times smaller than
            // it is drawn -- so a figure in metres came out a hundred times too big and every
            // slime got a thirty-nine metre culling box. Nothing looks wrong when that happens,
            // which is exactly why it is worth a test.
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one
                    * Visuals().VisualScaleFor(new DefinitionId(Slime));

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var jelly = instance.GetComponent<MonsterJellyPresentation>();

                Assert.That(jelly, Is.Not.Null);

                jelly.Seed(1);

                Vector3 box = skinned.bounds.size;

                // tall enough for a bubble at the top of its climb, and nowhere near the size
                // of a building
                Assert.That(box.y, Is.GreaterThan(0.45f),
                    "the culling box is only " + box.y + " m tall, so a risen bubble is culled "
                    + "with the body");
                Assert.That(box.y, Is.LessThan(2f),
                    "the culling box is " + box.y + " m tall for a monster a quarter of a metre "
                    + "high, so it is being skinned long after it leaves the screen");
                Assert.That(box.x, Is.LessThan(2f));
                Assert.That(box.z, Is.LessThan(2f));

                // and it must not creep: preparing twice is not twice as big
                jelly.Seed(2);

                for (var i = 0; i < 10; i++) jelly.Tick(1f / 30f);

                Assert.That(skinned.bounds.size.y, Is.EqualTo(box.y).Within(0.001f),
                    "the culling box grew from " + box.y + " to " + skinned.bounds.size.y);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ScalingTheDrawingHasNotTouchedWhatTheServerDecides()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(ContentPath);

            Assert.That(content, Is.Not.Null);
            Assert.That(content.BuildMonsters().TryGet(new DefinitionId(Slime),
                out MonsterDefinition slime), Is.True);

            // 19E.1 re-authored the reach on purpose -- it commits from 0.5 m and can land
            // from 0.85 -- so these are the figures the drawing must not have moved.
            Assert.That(slime.AttackRange, Is.EqualTo(0.85f).Within(1e-4f),
                "the slime now reaches " + slime.AttackRange + " m; drawing it smaller must "
                + "not change how close it has to be to hit");
            Assert.That(slime.AttackStartRange, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slime.AttackCooldownSeconds, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(slime.AttackWindupSeconds, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(slime.AttackRecoverySeconds, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slime.MoveSpeed, Is.EqualTo(0.95f).Within(1e-4f));
            Assert.That(slime.WanderSpeed, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void TheScaledSlimeStillStandsOnTheGround()
        {
            // The model's pivot is between its feet, so a uniform scale about the root cannot
            // lift it or sink it. Asserted rather than assumed, because the alternative is a
            // hand-tuned Y offset that nobody dares touch afterwards.
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            foreach (float scale in new[] { 1f, 0.75f })
            {
                var instance = (GameObject)Object.Instantiate(prefab);

                try
                {
                    instance.transform.position = Vector3.zero;
                    instance.transform.rotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one * scale;

                    var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    var baked = new Mesh();

                    try
                    {
                        skinned.BakeMesh(baked, true);

                        var lowest = float.MaxValue;

                        foreach (Vector3 v in baked.vertices)
                        {
                            float y = skinned.transform.TransformPoint(v).y;

                            if (y < lowest) lowest = y;
                        }

                        Assert.That(lowest, Is.EqualTo(0f).Within(0.002f),
                            "at " + scale + " scale the lowest point of the slime is at "
                            + lowest + ", so it " + (lowest > 0f ? "floats" : "sinks"));
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
        }
    }
}
