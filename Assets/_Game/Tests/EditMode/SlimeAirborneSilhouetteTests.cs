using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The slime has two silhouettes, and which one it is wearing says whether it is on the
    /// ground.
    /// </summary>
    /// <remarks>
    /// <b>Standing:</b> fat and wide, the underside flattened against the floor and spreading
    /// outward. <b>Airborne:</b> the lower body draws in, the flat bottom rounds off, and it
    /// becomes a compact plump drop with daylight underneath. Pausing the game at the apex
    /// should be enough to tell the two apart without looking at anything else.
    ///
    /// <b>Why this is a blend shape and not a bone.</b> Bones scale profiles; they cannot
    /// reshape them. Every attempt to pull the underside in with a "belly" bone produced a
    /// smaller flat bottom with a hard corner -- a plug, or a stepped plinth -- because the
    /// corner where the flat underside meets the wall is in the mesh, and scaling a corner
    /// leaves a corner. The rounded underside is a second lathed profile, and the clips blend
    /// between them.
    ///
    /// <b>What these tests are really guarding.</b> Two curves that have to agree: how high
    /// the body is off the ground, and how round its underside is. Hand-keyed against each
    /// other they drifted apart immediately -- the first pass left the slime hanging four
    /// centimetres up on a flat bottom through its whole landing and rebound. The animation
    /// now derives the blend from the hop, and <see cref="TheUndersideIsNeverFlatWhileItIsOffTheGround"/>
    /// is what stops anyone unpicking that.
    /// </remarks>
    [TestFixture]
    public sealed class SlimeAirborneSilhouetteTests
    {
        private const string Slime = "monster.training_slime";

        private const string Fbx =
            "Assets/_Game/Art/Monsters/TrainingSlime/Monster_TrainingSlime.fbx";

        /// <summary>Daylight under the slime past which a flat bottom is a bug, in metres.</summary>
        /// <remarks>
        /// Raised from 20 mm when the body was reshaped fuller and rounder. A slime whose
        /// underside is one centimetre clear has not left the ground so much as started to;
        /// the blend follows its height on purpose, so the first frame or two of a hop are
        /// meant to be only slightly rounded. Thirty millimetres is about a tenth of its
        /// height and a good third of the way to its apex -- past that it is plainly in the air.
        /// </remarks>
        private const float ClearlyAirborne = 0.030f;

        /// <summary>
        /// How round the underside must be, as a share of the roundest it can be.
        /// </summary>
        /// <remarks>
        /// A fraction, not a measurement in millimetres. The absolute figure has gone stale
        /// twice now -- once when the body shrank and again when it was made rounder standing
        /// up, which narrowed the gap between its two undersides without making the change any
        /// less visible. Measured against the model's own fully-blended underside, the test
        /// keeps meaning the same thing whatever size or shape the slime is next.
        /// </remarks>
        private const float RoundEnough = 0.40f;

        /// <summary>The same, at the top of a hop, where there is no excuse at all.</summary>
        private const float RoundAtTheApex = 0.60f;

        /// <summary>
        /// How far from its standing proportions towards its airborne ones the body must get
        /// at the top of a hop.
        /// </summary>
        /// <remarks>
        /// A share of the journey rather than a share of the standing figure, for the reason
        /// the last threshold here was wrong: the body was reshaped rounder standing up, which
        /// left less distance between the two silhouettes without making the change any less
        /// readable. Demanding a fixed percentage off the standing ratio quietly demands more
        /// and more as the resting shape gets rounder, until one day it asks for a change the
        /// two authored profiles cannot produce.
        /// </remarks>
        private const float WayToAirborne = 0.65f;

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

        /// <summary>
        /// One posed frame, measured. Everything the fixture asserts is one of these numbers.
        /// </summary>
        private struct Silhouette
        {
            /// <summary>Daylight between the lowest point of the model and the floor.</summary>
            public float Gap;

            public float Width;

            public float Height;

            /// <summary>
            /// How far the rim of the contact patch has risen above its middle. Zero is a
            /// flat bottom; a rounded belly lifts the rim and leaves the middle as the low
            /// point.
            /// </summary>
            public float Dome;

            /// <summary>The Airborne blend shape's weight, 0-100.</summary>
            public float Blend;

            public float Forward;
        }

        /// <summary>
        /// Poses the model and reads its shape off the deformed vertices.
        /// </summary>
        private sealed class Probe : System.IDisposable
        {
            private readonly GameObject _instance;
            private readonly SkinnedMeshRenderer _skinned;
            private readonly Transform _bodyBone;
            private readonly Mesh _baked = new Mesh();
            private readonly List<int> _patchRim = new List<int>();
            private readonly List<int> _patchMiddle = new List<int>();

            /// <summary>
            /// The body, without the bubbles floating over it.
            /// </summary>
            /// <remarks>
            /// Every reading here is about the body's silhouette, and the two bubbles hang
            /// above the crown -- measuring the whole mesh would fold their height into the
            /// body's and quietly ruin every wide-versus-tall figure in this fixture.
            /// </remarks>
            private readonly List<int> _body;

            public Probe()
            {
                _instance = (GameObject)Object.Instantiate(Prefab());
                _instance.transform.position = Vector3.zero;
                _instance.transform.rotation = Quaternion.identity;

                _skinned = _instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                foreach (Transform t in _instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Body") _bodyBone = t;
                }

                Assert.That(_skinned, Is.Not.Null);
                Assert.That(_bodyBone, Is.Not.Null, "the rig has no Body bone");

                _body = SlimeParts.Islands(_skinned.sharedMesh)[0];

                // Which vertices make up the contact patch, decided once from the standing
                // pose and then followed by index. There is no way to ask a deformed vertex
                // where it came from, and the patch is the only part of the body whose
                // curvature answers the question being asked.
                Clip("Slime_Idle").SampleAnimation(_instance, 0f);

                Vector3[] standing = Posed();
                var floor = float.MaxValue;

                foreach (int i in _body)
                {
                    if (standing[i].y < floor) floor = standing[i].y;
                }

                var patch = new List<int>();
                var reach = 0f;

                foreach (int i in _body)
                {
                    if (standing[i].y > floor + 0.002f) continue;

                    patch.Add(i);

                    float r = Mathf.Sqrt(standing[i].x * standing[i].x
                        + standing[i].z * standing[i].z);

                    if (r > reach) reach = r;
                }

                Assert.That(patch.Count, Is.GreaterThan(50),
                    "the underside is only " + patch.Count + " vertices, too few to tell a "
                    + "flat bottom from a rounded one");

                foreach (int i in patch)
                {
                    float r = Mathf.Sqrt(standing[i].x * standing[i].x
                        + standing[i].z * standing[i].z);

                    if (r > reach * 0.90f) _patchRim.Add(i);
                    else if (r < reach * 0.20f) _patchMiddle.Add(i);
                }

                Assert.That(_patchRim, Is.Not.Empty);
                Assert.That(_patchMiddle, Is.Not.Empty);
            }

            /// <summary>
            /// The deformed vertices, in the model's own space -- y up, z forward.
            /// </summary>
            /// <remarks>
            /// The conversion is not optional. A baked mesh comes back in the renderer's
            /// space, and the renderer sits under the rotation the FBX import puts there, so
            /// the raw vertices have the slime lying on its side: every height reads as zero
            /// and the body comes out exactly as wide as it is tall.
            /// </remarks>
            private Vector3[] Posed()
            {
                _skinned.BakeMesh(_baked, true);

                Vector3[] vertices = _baked.vertices;
                Transform renderer = _skinned.transform;
                Transform root = _instance.transform;

                for (var i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = root.InverseTransformPoint(
                        renderer.TransformPoint(vertices[i]));
                }

                return vertices;
            }

            public Silhouette At(AnimationClip clip, int frame, int frames)
            {
                clip.SampleAnimation(_instance, clip.length * frame / (frames - 1f));

                Vector3[] posed = Posed();

                float lowest = float.MaxValue, highest = float.MinValue;
                float left = float.MaxValue, right = float.MinValue;

                foreach (int i in _body)
                {
                    Vector3 v = posed[i];

                    if (v.y < lowest) lowest = v.y;
                    if (v.y > highest) highest = v.y;
                    if (v.x < left) left = v.x;
                    if (v.x > right) right = v.x;
                }

                var rim = 0f;

                foreach (int i in _patchRim) rim += posed[i].y;
                rim /= _patchRim.Count;

                var middle = 0f;

                foreach (int i in _patchMiddle) middle += posed[i].y;
                middle /= _patchMiddle.Count;

                return new Silhouette
                {
                    Gap = lowest,
                    Width = right - left,
                    Height = highest - lowest,
                    Dome = rim - middle,
                    Blend = _skinned.GetBlendShapeWeight(0),
                    Forward = _instance.transform.InverseTransformPoint(_bodyBone.position).z,
                };
            }

            /// <summary>
            /// The roundest this model's underside gets: the airborne shape at full weight,
            /// standing still. Everything else is judged as a share of it.
            /// </summary>
            public float FullyRoundedDome()
            {
                AnimationClip idle = Clip("Slime_Idle");

                idle.SampleAnimation(_instance, 0f);
                _skinned.SetBlendShapeWeight(0, 100f);

                Vector3[] posed = Posed();
                var rim = 0f;
                var middle = 0f;

                foreach (int i in _patchRim) rim += posed[i].y;
                foreach (int i in _patchMiddle) middle += posed[i].y;

                _skinned.SetBlendShapeWeight(0, 0f);

                return rim / _patchRim.Count - middle / _patchMiddle.Count;
            }

            /// <summary>Wide-over-tall with the airborne shape fully on, standing still.</summary>
            public float FullyRoundedRatio()
            {
                AnimationClip idle = Clip("Slime_Idle");

                idle.SampleAnimation(_instance, 0f);
                _skinned.SetBlendShapeWeight(0, 100f);

                Vector3[] posed = Posed();
                float lo = float.MaxValue, hi = float.MinValue;
                float left = float.MaxValue, right = float.MinValue;

                foreach (int i in _body)
                {
                    Vector3 v = posed[i];

                    if (v.y < lo) lo = v.y;
                    if (v.y > hi) hi = v.y;
                    if (v.x < left) left = v.x;
                    if (v.x > right) right = v.x;
                }

                _skinned.SetBlendShapeWeight(0, 0f);

                return (right - left) / Mathf.Max(1e-6f, hi - lo);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(_baked);
                Object.DestroyImmediate(_instance);
            }
        }

        private static int FrameCount(AnimationClip clip)
        {
            return Mathf.RoundToInt(clip.length * 30f) + 1;
        }

        [Test]
        public void TheMeshCarriesTheAirborneShapeAndTheCrownSwellAndNothingElse()
        {
            // Two, and only these two. A third would mean somebody solved a shape problem by
            // adding another blend rather than by changing the model, and blend shapes are the
            // one thing on this mesh that costs memory per copy.
            var instance = (GameObject)Object.Instantiate(Prefab());

            try
            {
                Mesh mesh = instance.GetComponentInChildren<SkinnedMeshRenderer>(true)
                    .sharedMesh;

                var names = new List<string>();

                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    names.Add(mesh.GetBlendShapeName(i));
                }

                Assert.That(names, Is.EquivalentTo(new[] { "Airborne", "Crown_Bulge" }),
                    "the model carries " + string.Join(", ", names));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void StandingOnTheGroundItIsWideAndItsUndersideIsFlat()
        {
            using (var probe = new Probe())
            {
                AnimationClip idle = Clip("Slime_Idle");
                Silhouette rest = probe.At(idle, 0, FrameCount(idle));

                Assert.That(rest.Blend, Is.EqualTo(0f).Within(0.01f),
                    "the idle is standing still and should be wearing the standing shape");
                // 1.25 now, not the sheet's 1.4-1.6: the body was deliberately made fuller and
                // less wide after the sheet's proportions read as flat in the game camera.
                Assert.That(rest.Width / rest.Height, Is.GreaterThan(1.15f),
                    "standing it is only " + (rest.Width / rest.Height) + " times as wide "
                    + "as it is tall, which is an egg rather than a slime");
                Assert.That(rest.Dome, Is.LessThan(0.004f),
                    "the underside is already " + rest.Dome + " m domed while it is sitting "
                    + "on the floor, so it is not flattened against anything");
            }
        }

        [Test]
        public void TheUndersideIsNeverFlatWhileItIsOffTheGround()
        {
            // The whole point. "Flat-bottomed object being carried through the air" is the
            // failure this exists to catch, and it is a failure of two curves agreeing
            // rather than of either one on its own -- so it can only be caught by posing the
            // model and looking at the shape of its underside.
            foreach (string name in new[] { "Slime_Move", "Slime_Attack" })
            {
                AnimationClip clip = Clip(name);

                using (var probe = new Probe())
                {
                    float full = probe.FullyRoundedDome();
                    int frames = FrameCount(clip);

                    for (var i = 0; i < frames; i++)
                    {
                        Silhouette s = probe.At(clip, i, frames);

                        if (s.Gap <= ClearlyAirborne) continue;

                        Assert.That(s.Dome, Is.GreaterThan(full * RoundEnough),
                            name + " frame " + i + ": the slime is " + s.Gap.ToString("0.000")
                            + " m off the ground with an underside only "
                            + (s.Dome / full * 100f).ToString("0") + "% as rounded as it can "
                            + "be, which reads as a flat-bottomed object floating");
                    }
                }
            }
        }

        [Test]
        public void AtTheApexItIsARoundDropWithDaylightUnderIt()
        {
            foreach (string name in new[] { "Slime_Move", "Slime_Attack" })
            {
                AnimationClip clip = Clip(name);

                using (var probe = new Probe())
                {
                    float full = probe.FullyRoundedDome();
                    AnimationClip idle = Clip("Slime_Idle");
                    Silhouette standing = probe.At(idle, 0, FrameCount(idle));

                    int frames = FrameCount(clip);

                    var apex = new Silhouette();

                    for (var i = 0; i < frames; i++)
                    {
                        Silhouette s = probe.At(clip, i, frames);

                        if (s.Gap > apex.Gap) apex = s;
                    }

                    Assert.That(apex.Gap, Is.GreaterThan(0.05f),
                        name + " never gets more than " + apex.Gap + " m off the ground");
                    Assert.That(apex.Dome, Is.GreaterThan(full * RoundAtTheApex),
                        name + " reaches its apex with an underside only "
                        + (apex.Dome / full * 100f).ToString("0") + "% as rounded as it can be");

                    // How far along the journey between the two authored silhouettes it got.
                    // The hop is deliberately allowed to travel less of it than the attack, so
                    // a threshold that suits one has to be loose enough for the other.
                    float squat = apex.Width / apex.Height;
                    float floored = standing.Width / standing.Height;
                    float airborne = probe.FullyRoundedRatio();
                    float travelled = (floored - squat) / Mathf.Max(1e-6f, floored - airborne);

                    Assert.That(travelled, Is.GreaterThan(WayToAirborne),
                        name + " at its apex is " + squat.ToString("0.000")
                        + " times as wide as it is tall, only " + (travelled * 100f).ToString("0")
                        + "% of the way from its standing " + floored.ToString("0.000")
                        + " to its airborne " + airborne.ToString("0.000"));
                }
            }
        }

        [Test]
        public void TheAttackIsPushedFurtherThanTheHop()
        {
            // The brief asks for the same idea in both and more of it in the attack. Equal
            // would mean the attack had not been given its own treatment.
            var reach = new Dictionary<string, Silhouette>();

            foreach (string name in new[] { "Slime_Move", "Slime_Attack" })
            {
                AnimationClip clip = Clip(name);

                using (var probe = new Probe())
                {
                    int frames = FrameCount(clip);
                    var best = new Silhouette();

                    for (var i = 0; i < frames; i++)
                    {
                        Silhouette s = probe.At(clip, i, frames);

                        if (s.Blend > best.Blend) best = s;
                    }

                    reach[name] = best;
                }
            }

            Assert.That(reach["Slime_Attack"].Blend,
                Is.GreaterThan(reach["Slime_Move"].Blend + 15f),
                "the attack only reaches " + reach["Slime_Attack"].Blend + " against the "
                + "hop's " + reach["Slime_Move"].Blend + ", which is not exaggerated further");
            Assert.That(reach["Slime_Attack"].Gap,
                Is.GreaterThan(reach["Slime_Move"].Gap * 2f),
                "the attack barely leaves the ground further than the hop does");
        }

        [Test]
        public void TheGroundedClipsNeverLeaveTheGroundShape()
        {
            // Idle, hit and death all happen with the slime sitting on the floor. A blend
            // creeping into any of them would round the underside of a slime that is plainly
            // resting on the ground.
            foreach (string name in new[] { "Slime_Idle", "Slime_Hit", "Slime_Death" })
            {
                AnimationClip clip = Clip(name);

                using (var probe = new Probe())
                {
                    int frames = FrameCount(clip);

                    for (var i = 0; i < frames; i++)
                    {
                        Assert.That(probe.At(clip, i, frames).Blend, Is.LessThan(0.5f),
                            name + " frame " + i + " has lifted the airborne shape");
                    }
                }
            }
        }

        [Test]
        public void TheAttackJumpIsReadableAndLeavesNothingBehind()
        {
            // The numbers the brief asked to be measured rather than assumed.
            AnimationClip clip = Clip("Slime_Attack");

            Assert.That(clip.length, Is.GreaterThanOrEqualTo(1.10f).And.LessThanOrEqualTo(1.35f),
                "the attack runs " + clip.length + " s");

            using (var probe = new Probe())
            {
                int frames = FrameCount(clip);

                var apexGap = 0f;
                var travel = 0f;

                Silhouette opening = probe.At(clip, 0, frames);

                for (var i = 0; i < frames; i++)
                {
                    Silhouette s = probe.At(clip, i, frames);

                    if (s.Gap > apexGap) apexGap = s.Gap;
                    if (s.Forward > travel) travel = s.Forward;
                }

                Silhouette closing = probe.At(clip, frames - 1, frames);

                Assert.That(apexGap, Is.GreaterThanOrEqualTo(0.20f).And.LessThanOrEqualTo(0.30f),
                    "the jump reaches " + apexGap + " m, outside the 0.20-0.30 m asked for");
                Assert.That(travel, Is.GreaterThan(0.40f),
                    "it only travels " + travel + " m forward, which will not read as a lunge");

                // No root motion, and nothing left behind: whatever the visual does, the
                // slime has to finish standing exactly where the server still thinks it is.
                Assert.That(closing.Forward, Is.EqualTo(opening.Forward).Within(0.002f),
                    "the attack ends " + (closing.Forward - opening.Forward)
                    + " m forward of where it began, so repeating it walks the model away "
                    + "from its own position");
                Assert.That(closing.Gap, Is.EqualTo(opening.Gap).Within(0.002f));
                Assert.That(closing.Blend, Is.EqualTo(0f).Within(0.5f),
                    "the attack leaves the airborne shape partly lifted, so the slime stands "
                    + "there afterwards with a rounded underside");
            }
        }
    }
}
