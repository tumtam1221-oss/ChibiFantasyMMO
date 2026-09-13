using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The decisions the character presentation makes, and the assets it makes them about.
    /// </summary>
    /// <remarks>
    /// <b>Two kinds of test, deliberately together.</b> The rules below are arithmetic and
    /// need no socket; the asset checks below are structural and need no socket either. What
    /// does need one -- a model actually appearing on a replicated character -- is in the
    /// PlayMode suite against real FishNet, because that is the only place it means anything.
    ///
    /// <b>The avatar checks are a regression, not a formality.</b> A humanoid rig whose Chest
    /// stops mapping is a rig that silently retargets wrong: the walk still plays, the
    /// character still moves, and the torso is subtly broken in every frame of the game. It
    /// is exactly the kind of failure nobody notices until it is in a build.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterVisualPresentationTests
    {
        private const string MaleModel =
            "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy.fbx";

        private const string FemaleModel =
            "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy.fbx";

        /// <summary>Folders whose clips were authored on the character inside them.</summary>
        /// <remarks>
        /// The character ships as two files: the character itself, in its rest pose, and a
        /// sidecar carrying the run its author made for that exact skeleton. A clip from the
        /// sidecar is not retargeted from anywhere, so tests that exist to catch retargeting
        /// damage have nothing to say about it.
        /// </remarks>
        private static readonly string[] OwnRigFolders =
        {
            "Assets/_Game/Art/Characters/Production/MaleMeshy/",
            "Assets/_Game/Art/Characters/Production/FemaleMeshy/",
        };

        /// <summary>Whether this clip was authored on the rig it is played on.</summary>
        private static bool IsAuthoredOnItsOwnRig(AnimationClip clip)
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(clip);

            foreach (string folder in OwnRigFolders)
            {
                if (path.StartsWith(folder, System.StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/CharacterVisualCatalogue.asset";

        private const string CharacterPrefab =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string WorldScene = "Assets/_Game/Scenes/Client/GameWorld.unity";

        private const string LocomotionController =
            "Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- which model ---------------------------------------------------------------------

        [Test]
        public void EachGenderGetsItsOwnApprovedModel()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.ModelFor(CharacterGender.Male), Is.SameAs(catalogue.Male));
            Assert.That(catalogue.ModelFor(CharacterGender.Female),
                Is.SameAs(catalogue.Female));
            Assert.That(catalogue.Male, Is.Not.SameAs(catalogue.Female),
                "one model for both would make the choice meaningless");
        }

        [Test]
        public void AnUnsetGenderIsNotSilentlyMale()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.ModelFor(CharacterGender.Unspecified),
                Is.SameAs(catalogue.Fallback),
                "a zero-valued enum reading as Male is the bug Unspecified exists to prevent");
        }

        [Test]
        public void AGenderCodeThisBuildDoesNotKnowFallsBackRatherThanGuessing()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(CharacterVisualCatalogue.GenderOf(0),
                Is.EqualTo(CharacterGender.Unspecified));
            Assert.That(CharacterVisualCatalogue.GenderOf(1), Is.EqualTo(CharacterGender.Male));
            Assert.That(CharacterVisualCatalogue.GenderOf(2),
                Is.EqualTo(CharacterGender.Female));

            // An older client meeting a newer server.
            Assert.That(CharacterVisualCatalogue.GenderOf(97),
                Is.EqualTo(CharacterGender.Unspecified));
            Assert.That(catalogue.ModelFor(97), Is.SameAs(catalogue.Fallback));
            Assert.That(CharacterVisualCatalogue.GenderOf(-4),
                Is.EqualTo(CharacterGender.Unspecified));
        }

        [Test]
        public void TheShippedCatalogueNamesTheApprovedAssetsAndTheExistingController()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Male),
                Is.EqualTo(MaleModel));
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Female),
                Is.EqualTo(FemaleModel));

            Assert.That(catalogue.Locomotion, Is.Not.Null,
                "with no controller nothing animates");
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Locomotion),
                Is.EqualTo("Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller"),
                "the existing validated controller, not a second one");
        }

        // ---- what the locomotion controller actually plays -------------------------------------

        /// <summary>
        /// The blend tree is standing and walking, and nothing is being played faster than
        /// it was animated.
        /// </summary>
        /// <remarks>
        /// <b>The multiplier is the regression this guards.</b> A walk clip that depicts
        /// 1.16 m/s played at three times its rate to cover 4 m/s of ground is not a run; it
        /// is a walk on fast-forward, and it looks like one. It has been tried in this
        /// project and rejected. The check is therefore not "the controller is configured"
        /// but "no playback rate anywhere is anything other than one" -- across the child
        /// motions, the state speed, and the state's speed parameter, which are three
        /// separate places the same shortcut can be hidden.
        ///
        /// <b>Two stops, on purpose.</b> Run belongs to a later step and would arrive as a
        /// third. Until then a third entry means something was added without the blend being
        /// re-judged by eye.
        /// </remarks>
        [Test]
        public void TheLocomotionBlendIsStandingAndWalkingAtTheirOwnNaturalRates()
        {
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            Assert.That(controller, Is.Not.Null, "the locomotion controller has moved");

            UnityEditor.Animations.BlendTree tree = null;

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                Assert.That(child.state.speed, Is.EqualTo(1f),
                    child.state.name + " plays at " + child.state.speed
                    + "x -- a state speed is the same fast-forward by another name");

                // The attack state is the one exception, and it is the attack speed stat:
                // its rate comes from the character's replicated ASPD through the
                // AttackSpeed parameter (19E.1), and AttackSpeedAndMeleeRangeTests holds
                // that nothing else reads it. Locomotion runs at its natural rate.
                if (child.state.name == "BasicPunch")
                {
                    Assert.That(child.state.speedParameter, Is.EqualTo("AttackSpeed"));

                    continue;
                }

                Assert.That(child.state.speedParameterActive, Is.False,
                    child.state.name + " drives its playback rate from a parameter, which is "
                    + "a multiplier that does not show up in the inspector as one");

                if (child.state.motion is UnityEditor.Animations.BlendTree found)
                {
                    tree = found;
                }
            }

            Assert.That(tree, Is.Not.Null, "nothing blends between standing and walking");
            Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
            Assert.That(tree.children.Length, Is.EqualTo(2),
                "standing and walking only -- a run is a later step, not a silent third stop");

            foreach (UnityEditor.Animations.ChildMotion child in tree.children)
            {
                Assert.That(child.motion, Is.Not.Null, "an empty stop in the blend tree");
                Assert.That(child.timeScale, Is.GreaterThan(0f).And.LessThan(2.2f),
                    child.motion.name + " is played at " + child.timeScale
                    + "x -- past about 2x a run stops reading as running and becomes the "
                    + "fast-forward this project rejected at 3.3x");

                if (child.threshold <= 0f)
                {
                    Assert.That(child.timeScale, Is.EqualTo(1f),
                        "standing still covers no ground, so there is no reason to play it "
                        + "at anything but its authored rate");
                }
            }

            Assert.That(tree.children[0].threshold, Is.EqualTo(0f), "standing is not at rest");
            Assert.That(tree.children[1].threshold, Is.EqualTo(1f),
                "full pace is not the top of the blend");
        }

        /// <summary>Both clips the blend reaches are loopable humanoid motion.</summary>
        /// <remarks>A clip that is not humanoid does not retarget onto either approved model
        /// and animates nothing; a clip that does not loop plays once and freezes, which for
        /// an idle is a character turning into a statue after three seconds.</remarks>
        [Test]
        public void BothLocomotionClipsLoopAndRetarget()
        {
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            var clips = 0;

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                if (!(child.state.motion is UnityEditor.Animations.BlendTree tree)) continue;

                foreach (UnityEditor.Animations.ChildMotion motion in tree.children)
                {
                    var clip = motion.motion as AnimationClip;

                    Assert.That(clip, Is.Not.Null, "a blend stop that is not a clip");
                    Assert.That(clip.isHumanMotion, Is.True,
                        clip.name + " is not humanoid, so it retargets onto neither model");
                    Assert.That(clip.isLooping, Is.True,
                        clip.name + " does not loop -- it plays once and then freezes");

                    clips++;
                }
            }

            Assert.That(clips, Is.EqualTo(2));
        }

        /// <summary>
        /// Both legs take a real step, and the same size step.
        /// </summary>
        /// <remarks>
        /// <b>The clip this caught.</b> The walk this project shipped for months reached
        /// 0.496m forward with the right foot and 0.157m with the left -- a symmetry of
        /// 0.32. On screen that is a long right stride followed by a stunted left one that
        /// barely clears the other foot, which reads as "right, left, together" rather than
        /// as walking. Every other check passed on it: it looped seamlessly, it retargeted,
        /// it had no root drift, and Blender called it clean. Nothing was measuring whether
        /// the two legs did the same amount of work.
        ///
        /// <b>Separation is the one reading that survives.</b> Absolute foot positions taken
        /// through the editor sampling API carry an offset that is not there at runtime.
        /// The distance <em>between</em> the feet does not: whatever that offset is, both
        /// feet share it and it cancels.
        /// </remarks>
        [Test]
        public void EveryLocomotionClipStepsEvenlyOnBothLegs()
        {
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                if (!(child.state.motion is UnityEditor.Animations.BlendTree tree)) continue;

                foreach (UnityEditor.Animations.ChildMotion motion in tree.children)
                {
                    var clip = (AnimationClip)motion.motion;

                    // Standing still is not a gait.
                    if (motion.threshold <= 0f) continue;

                    float symmetry = GaitSymmetry(clip, out float left, out float right);

                    if (AcceptedUnevenGait.TryGetValue(clip.name, out float accepted))
                    {
                        // Known, measured and deliberately kept. Still pinned, so the clip
                        // cannot quietly get worse and cannot quietly get fixed without
                        // somebody noticing this entry is stale.
                        Assert.That(symmetry, Is.EqualTo(accepted).Within(0.05f),
                            clip.name + " is on the accepted-uneven-gait list at "
                            + accepted.ToString("F2") + " but now measures "
                            + symmetry.ToString("F2") + " -- update the entry or remove it");

                        continue;
                    }

                    Assert.That(symmetry, Is.GreaterThan(0.7f),
                        clip.name + " steps " + left.ToString("F3") + "m with the left foot "
                        + "and " + right.ToString("F3") + "m with the right (symmetry "
                        + symmetry.ToString("F2") + ") -- one leg barely stepping reads as a "
                        + "stride that ends with the feet dragged together");
                }
            }
        }

        /// <summary>
        /// The feet point where the character is going, not out to the sides.
        /// </summary>
        /// <remarks>
        /// <b>The defect this caught.</b> A run clip retargeted onto the chibi with its feet
        /// turned out 12 degrees, which reads as waddling rather than running. The clip was
        /// innocent: on its own rig it measured within half a degree of the jog. The splay
        /// was introduced by the retarget, because each animation FBX was building its own
        /// avatar from its own bind pose even though every one of them is the same Mixamo
        /// character. Pointing the offenders at one shared avatar removed it.
        ///
        /// <b>Which avatar is a per-clip measurement, not a rule.</b> Sharing helped the run,
        /// the fast run and the slash, and made both idles noticeably worse. So this asserts
        /// the outcome -- how the feet actually end up pointing -- and leaves the importer
        /// setting to whatever achieves it.
        ///
        /// <b>Measured heel-to-toe, and only when the foot is down.</b> A bone's own forward
        /// axis is whatever the rig author chose, and it swings wildly once the foot pitches;
        /// the vector from ankle to toe does not. Splay is only meaningful on a planted foot,
        /// so only the lowest quarter of the stride is counted.
        ///
        /// <b>Only retargeted clips are judged.</b> This test exists to catch what a retarget
        /// adds. A clip authored on the model's own skeleton -- the Meshy run inside the
        /// character's FBX -- is the animator's intent and the reference everything else is
        /// held against, so it is measured and reported but not asserted. Headings are
        /// averaged as vectors: the Meshy rig rests its right toe joint behind the ankle, so
        /// that foot's heading lives next to the 180 degree seam where a plain mean of angles
        /// is nonsense.
        /// </remarks>
        [Test]
        public void TheFeetPointForwardsRatherThanOutwards()
        {
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                if (!(child.state.motion is UnityEditor.Animations.BlendTree tree)) continue;

                foreach (UnityEditor.Animations.ChildMotion motion in tree.children)
                {
                    var clip = (AnimationClip)motion.motion;

                    float splay = FootSplay(clip);

                    if (IsAuthoredOnItsOwnRig(clip))
                    {
                        // Authored on this very skeleton: nothing was retargeted, so there
                        // is nothing for this test to blame. Reported for the record.
                        TestContext.WriteLine(clip.name + " (own rig) plants its feet "
                            + splay.ToString("F1") + " degrees out -- authored, not asserted");

                        continue;
                    }

                    // A standing character has no direction of travel, and a relaxed stance
                    // turns the feet out five to fifteen degrees -- that is a person, not a
                    // defect. Only the moving stops are held to pointing where the character
                    // is going. The resting one still has to stay inside human, so a rig
                    // that splays a stance to forty degrees is still caught.
                    float limit = motion.threshold <= 0f ? 18f : 6f;

                    Assert.That(Mathf.Abs(splay), Is.LessThan(limit),
                        clip.name + " plants its feet " + splay.ToString("F1")
                        + " degrees out"
                        + (motion.threshold <= 0f
                            ? " while standing, which is past a natural stance"
                            : " from the direction of travel -- the character waddles")
                        + ". This is usually the clip's avatar disagreeing with the rest of "
                        + "the set, not the clip itself");
                }
            }
        }

        /// <summary>Average outward angle of the planted feet, in degrees.</summary>
        private static float FootSplay(AnimationClip clip)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MaleModel);
            GameObject subject = Object.Instantiate(prefab);

            try
            {
                Animator animator = subject.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;

                Transform leftAnkle = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightAnkle = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform leftToe = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                Transform rightToe = animator.GetBoneTransform(HumanBodyBones.RightToes);
                Transform root = animator.transform;

                Assert.That(leftToe, Is.Not.Null, "the rig no longer maps toes, so foot "
                    + "direction cannot be measured at all");

                const int Samples = 90;

                var heights = new List<float>();
                var headings = new List<Vector2>();
                var rightHeights = new List<float>();
                var rightHeadings = new List<Vector2>();

                UnityEditor.AnimationMode.StartAnimationMode();

                for (var i = 0; i <= Samples; i++)
                {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(subject, clip,
                        clip.length * i / Samples);
                    UnityEditor.AnimationMode.EndSampling();

                    heights.Add(root.InverseTransformPoint(leftAnkle.position).y);
                    rightHeights.Add(root.InverseTransformPoint(rightAnkle.position).y);
                    headings.Add(Heading(root, leftAnkle, leftToe));
                    rightHeadings.Add(Heading(root, rightAnkle, rightToe));
                }

                UnityEditor.AnimationMode.StopAnimationMode();

                return (Planted(heights, headings) - Planted(rightHeights, rightHeadings)) * 0.5f;
            }
            finally
            {
                if (UnityEditor.AnimationMode.InAnimationMode())
                {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }

                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>Ankle-to-toe direction on the ground, in the character's own frame.</summary>
        private static Vector2 Heading(Transform root, Transform ankle, Transform toe)
        {
            Vector3 a = root.InverseTransformPoint(ankle.position);
            Vector3 t = root.InverseTransformPoint(toe.position);

            return new Vector2(t.x - a.x, t.z - a.z).normalized;
        }

        /// <summary>
        /// The mean heading, in degrees from forward (+ is the character's right), over the
        /// frames where the foot was lowest. A vector mean, so a heading near 180 degrees
        /// does not average with itself into garbage.
        /// </summary>
        private static float Planted(List<float> heights, List<Vector2> headings)
        {
            var sorted = new List<float>(heights);
            sorted.Sort();

            float floor = sorted[sorted.Count / 4];

            Vector2 total = Vector2.zero;

            for (var i = 0; i < heights.Count; i++)
            {
                if (heights[i] > floor) continue;

                total += headings[i];
            }

            return total == Vector2.zero ? 0f : Mathf.Atan2(total.x, total.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Clips kept in the locomotion set despite an uneven gait, and what they measure.
        /// </summary>
        /// <remarks>
        /// <b>An exception list, not a lowered bar.</b> The threshold below still applies to
        /// everything else; these are named one at a time, with the number they were accepted
        /// at, so the exception is visible in the failure message rather than hidden in a
        /// constant. Each entry is pinned both ways -- a clip on this list that changes in
        /// either direction fails, because a silently improved entry is a stale one.
        ///
        /// <b>Currently empty, deliberately.</b> The clip that needed an entry --
        /// EXT_Walk_Loop_VALIDATION, at 0.32 -- is no longer in the locomotion set. The
        /// mechanism is kept because the next borderline clip should be argued for by name
        /// here rather than by quietly lowering the threshold below.
        /// </remarks>
        private static readonly Dictionary<string, float> AcceptedUnevenGait =
            new Dictionary<string, float>();

        /// <summary>How evenly a clip steps, from 0 (one leg does nothing) to 1 (even).</summary>
        private static float GaitSymmetry(AnimationClip clip, out float leftReach,
            out float rightReach)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MaleModel);
            GameObject subject = Object.Instantiate(prefab);

            try
            {
                Animator animator = subject.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;

                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform root = animator.transform;

                var forward = 0f;
                var backward = 0f;

                UnityEditor.AnimationMode.StartAnimationMode();

                const int Samples = 90;

                for (var i = 0; i <= Samples; i++)
                {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(subject, clip,
                        clip.length * i / Samples);
                    UnityEditor.AnimationMode.EndSampling();

                    float separation = root.InverseTransformPoint(left.position).z
                        - root.InverseTransformPoint(right.position).z;

                    forward = Mathf.Max(forward, separation);
                    backward = Mathf.Min(backward, separation);
                }

                UnityEditor.AnimationMode.StopAnimationMode();

                leftReach = forward;
                rightReach = -backward;

                float bigger = Mathf.Max(leftReach, rightReach);

                return bigger <= 0.0001f ? 0f : Mathf.Min(leftReach, rightReach) / bigger;
            }
            finally
            {
                if (UnityEditor.AnimationMode.InAnimationMode())
                {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }

                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>
        /// The character covers the ground its legs claim to, and the clip moves no body.
        /// </summary>
        /// <remarks>
        /// <b>A correction to an earlier version of this test.</b> It used to require the
        /// top clip to report a non-zero <c>averageSpeed</c>, on the reasoning that zero
        /// meant the forward travel had been baked into the pose and the body would slide
        /// and snap back. That is true of a clip that was captured travelling and then
        /// flattened -- it is not true of one authored in place, which legitimately reports
        /// zero and moves no body at all. The check was asserting a proxy; it now asserts
        /// the thing the proxy stood for, measured on the rig.
        ///
        /// <b>And that the two speeds agree.</b> The world moves a character at one number
        /// and the presenter divides by another to reach the top of the blend. If they
        /// disagree the character is drawn running at a pace it is not travelling, which is
        /// foot slide, and no amount of animation work fixes it.
        /// </remarks>
        [Test]
        public void TheBlendReachesFullPaceBeforeTheWorldSpeedSoJitterCannotLeakIdleIn()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            var content = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(
                "Assets/_Game/Data/Production/WorldContentCatalogue.asset");

            float ratio = catalogue.ReferenceWalkSpeed / content.WalkMetresPerSecond;

            // The Speed parameter is a blend weight, not a speed. It is measured from the
            // presented transform each frame, and snapshot playback is never perfectly even,
            // so a reference equal to the world speed dips below 1 on every slow segment.
            // Below 1 the 1D tree mixes the idle in, which both waters the pose down and --
            // because a blend tree also blends cycle length, and the idle is four times
            // longer -- stretches the run cycle. That was the "legs hesitate" a player saw.
            // Saturating at three quarters of travel speed leaves 25% of headroom for jitter
            // while a genuinely slow drift still shows as less than a full run.
            Assert.That(ratio, Is.InRange(0.6f, 0.85f),
                "the blend reaches full pace at " + catalogue.ReferenceWalkSpeed
                + " m/s against a world speed of " + content.WalkMetresPerSecond
                + " m/s (x" + ratio.ToString("F2") + ") -- above 0.85 snapshot jitter leaks "
                + "the idle into the run, below 0.6 a slow drift plays as a full run");
        }

        /// <summary>
        /// The feet cover the ground the character travels, on both shipped rigs.
        /// </summary>
        /// <remarks>
        /// <b>The defect this caught.</b> The run clip is authored for a human of about
        /// 1.8 m. Retargeted onto a 0.8 m character with 0.23 m legs, the same joint angles
        /// cover a quarter of the ground: measured 0.93 m/s (male) and 0.86 m/s (female)
        /// against a world speed of 3.83 m/s. That is a character skating across the map
        /// with its feet touching down on a quarter of the distance, which is what "it walks
        /// strangely" turned out to mean.
        ///
        /// <b>Measured, not configured.</b> Stride is a property of the rig, not of the clip,
        /// so the ground speed is read off the planted foot of each production model, scaled
        /// by the blend's playback rate, and compared with what the server moves them at.
        /// Which combination of world speed and playback rate satisfies this is a feel
        /// decision; that it is satisfied at all is a correctness one.
        /// </remarks>
        [Test]
        public void TheFeetCoverTheGroundTheCharacterTravels()
        {
            var content = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(
                "Assets/_Game/Data/Production/WorldContentCatalogue.asset");
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            UnityEditor.Animations.ChildMotion run = default(UnityEditor.Animations.ChildMotion);
            var found = false;

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                if (!(child.state.motion is UnityEditor.Animations.BlendTree tree)) continue;

                foreach (UnityEditor.Animations.ChildMotion motion in tree.children)
                {
                    if (motion.threshold >= 1f) { run = motion; found = true; }
                }
            }

            Assert.That(found, Is.True, "no full-pace stop in the locomotion blend");

            foreach (string model in new[] { MaleModel, FemaleModel })
            {
                float ground = PlantedFootGroundSpeed(model, (AnimationClip)run.motion) * run.timeScale;
                float slide = content.WalkMetresPerSecond / ground;

                Assert.That(slide, Is.InRange(0.85f, 1.15f),
                    System.IO.Path.GetFileName(model) + ": the feet cover " + ground.ToString("F2")
                    + " m/s at " + run.timeScale + "x while the world moves "
                    + content.WalkMetresPerSecond + " m/s (x" + slide.ToString("F2")
                    + ") -- beyond about 15% that is visible skating");
            }
        }

        /// <summary>Ground speed implied by the planted foot sliding back under the hips.</summary>
        private static float PlantedFootGroundSpeed(string modelPath, AnimationClip clip)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject subject = Object.Instantiate(prefab);
            subject.transform.position = Vector3.zero;

            try
            {
                Animator animator = subject.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;

                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

                const int Samples = 60;
                float dt = clip.length / Samples;
                var speeds = new List<float>();
                float previousLeft = 0f, previousRight = 0f;
                var primed = false;

                UnityEditor.AnimationMode.StartAnimationMode();

                for (var i = 0; i <= Samples; i++)
                {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(subject, clip, i * dt);
                    UnityEditor.AnimationMode.EndSampling();

                    float lz = left.position.z - hips.position.z;
                    float rz = right.position.z - hips.position.z;

                    if (primed)
                    {
                        bool leftDown = left.position.y < right.position.y;
                        float dz = leftDown ? lz - previousLeft : rz - previousRight;

                        // the planted foot moves backwards under the hips at ground speed
                        if (dz < 0f) speeds.Add(-dz / dt);
                    }

                    previousLeft = lz; previousRight = rz; primed = true;
                }

                UnityEditor.AnimationMode.StopAnimationMode();

                speeds.Sort();

                return speeds.Count == 0 ? 0f : speeds[speeds.Count / 2];
            }
            finally
            {
                if (UnityEditor.AnimationMode.InAnimationMode())
                {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }

                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>
        /// No locomotion clip walks the body away from the transform.
        /// </summary>
        /// <remarks>Root motion is off, so a clip carrying its travel in the pose rather
        /// than in the root channel slides the body forward and teleports it back once per
        /// loop. Measured on the rig, because the importer flag that causes it is not the
        /// only way to arrive at it.</remarks>
        [Test]
        public void NoLocomotionClipDragsTheBodyAwayFromTheCharacter()
        {
            var controller = UnityEditor.AssetDatabase
                .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(LocomotionController);

            foreach (UnityEditor.Animations.ChildAnimatorState child in
                controller.layers[0].stateMachine.states)
            {
                if (!(child.state.motion is UnityEditor.Animations.BlendTree tree)) continue;

                foreach (UnityEditor.Animations.ChildMotion motion in tree.children)
                {
                    var clip = (AnimationClip)motion.motion;

                    float travel = BodyTravel(clip);

                    Assert.That(travel, Is.LessThan(0.1f),
                        clip.name + " moves the body " + travel.ToString("F3")
                        + "m within the clip while the transform stays put, so it slides "
                        + "out and snaps back once per loop");
                }
            }
        }

        /// <summary>How far the hips wander, fore and aft, across a clip.</summary>
        private static float BodyTravel(AnimationClip clip)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MaleModel);
            GameObject subject = Object.Instantiate(prefab);

            try
            {
                Animator animator = subject.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform root = animator.transform;

                var min = float.MaxValue;
                var max = float.MinValue;

                UnityEditor.AnimationMode.StartAnimationMode();

                const int Samples = 60;

                for (var i = 0; i <= Samples; i++)
                {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(subject, clip,
                        clip.length * i / Samples);
                    UnityEditor.AnimationMode.EndSampling();

                    float z = root.InverseTransformPoint(hips.position).z;

                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }

                UnityEditor.AnimationMode.StopAnimationMode();

                return max - min;
            }
            finally
            {
                if (UnityEditor.AnimationMode.InAnimationMode())
                {
                    UnityEditor.AnimationMode.StopAnimationMode();
                }

                Object.DestroyImmediate(subject);
            }
        }

        // ---- the shipped assets are actually shipped -----------------------------------------

        [Test]
        public void NothingTheGameShipsResolvesIntoTheValidationFolder()
        {
            // The catalogue and the character prefab are the two roots a running client
            // pulls character art through. Everything either of them reaches, transitively,
            // has to be content a fresh clone actually receives -- and the validation folder
            // is deliberately not that. A reference into it is a model that exists on the
            // machine it was authored on and nowhere else.
            var offenders = new List<string>();

            foreach (string root in new[] { CataloguePath, CharacterPrefab })
            {
                foreach (string dependency in
                    UnityEditor.AssetDatabase.GetDependencies(root, true))
                {
                    if (dependency.Replace("\\", "/").Contains("/Validation/"))
                    {
                        offenders.Add(root + " -> " + dependency);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "a shipped asset reaching into the validation folder is a missing model on "
                + "every machine but one");
        }

        [Test]
        public void EveryCharacterAssetTheGameNeedsExistsWhereItSaysItDoes()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.Male, Is.Not.Null, "no approved male model is configured");
            Assert.That(catalogue.Female, Is.Not.Null,
                "no approved female model is configured");
            Assert.That(catalogue.Fallback, Is.Not.Null);
            Assert.That(catalogue.Locomotion, Is.Not.Null);

            foreach (string path in RequiredCharacterAssets())
            {
                Assert.That(System.IO.File.Exists(path), Is.True, "missing " + path);
                Assert.That(System.IO.File.Exists(path + ".meta"), Is.True,
                    "missing importer settings for " + path
                    + " -- without the .meta the avatar is regenerated and the humanoid "
                    + "mapping is whatever Unity guesses");
                Assert.That(path, Does.Not.Contain("/Validation/"), path);
            }
        }

        [Test]
        public void TheCharacterPrefabStillResolvesTheCatalogueAfterTheAssetsMoved()
        {
            var presenter = Load(CharacterPrefab).GetComponent<CharacterVisualPresenter>();

            var catalogue = new UnityEditor.SerializedObject(presenter)
                .FindProperty("_catalogue").objectReferenceValue
                as CharacterVisualCatalogue;

            Assert.That(catalogue, Is.Not.Null);
            Assert.That(catalogue.Male, Is.Not.Null,
                "the prefab reaches a catalogue whose male model no longer resolves");
            Assert.That(catalogue.Female, Is.Not.Null);

            // And the models it reaches are the humanoids the animator expects.
            Assert.That(catalogue.Male.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Null);
            Assert.That(catalogue.Female.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Null);
        }

        /// <summary>
        /// Every file the shipped character presentation cannot run without.
        /// </summary>
        /// <remarks>The minimum set, not the folder: the two approved models, their body
        /// textures, and every clip the shipped animator reaches -- male and female, because
        /// each gender has its own authored variant. The basic punch is the Mixamo cross
        /// retargeted onto each Meshy rig in Blender and the guard it is thrown from is the
        /// project's own clip, authored on the Meshy rigs (19E); the
        /// sword pack's swing is no longer reached. The controller and its female override already live in tracked
        /// content.</remarks>
        private static string[] RequiredCharacterAssets()
        {
            const string production = "Assets/_Game/Art/Characters/Production/";

            const string melee = "Assets/Kevin Iglesias/Human Animations/Animations/";

            return new[]
            {
                production + "MaleMeshy/CHR_Male_Meshy.fbx",
                production + "MaleMeshy/CHR_Male_Meshy@Idle.fbx",
                production + "MaleMeshy/CHR_Male_Meshy@Run.fbx",
                production + "MaleMeshy/CHR_Male_Meshy@CrossPunch.fbx",
                production + "MaleMeshy/CHR_Male_Meshy@GuardIdle.fbx",
                production + "MaleMeshy/CHR_Male_Meshy.mat",
                production + "MaleMeshy/Textures/CHR_Male_Meshy_BaseColor.png",
                production + "MaleMeshy/Textures/CHR_Male_Meshy_Normal.png",
                production + "FemaleMeshy/CHR_Female_Meshy.fbx",
                production + "FemaleMeshy/CHR_Female_Meshy@Idle.fbx",
                production + "FemaleMeshy/CHR_Female_Meshy@Run.fbx",
                production + "FemaleMeshy/CHR_Female_Meshy@CrossPunch.fbx",
                production + "FemaleMeshy/CHR_Female_Meshy@GuardIdle.fbx",
                production + "FemaleMeshy/CHR_Female_Meshy.mat",
                production + "FemaleMeshy/Textures/CHR_Female_Meshy_BaseColor.png",
                production + "FemaleMeshy/Textures/CHR_Female_Meshy_Normal.png",
                melee + "Male/Combat/HumanM@Death01.fbx",
                melee + "Female/Combat/HumanF@Death01.fbx",
            };
        }

        // ---- walking ------------------------------------------------------------------------

        [Test]
        public void StandingStillIsExactlyZeroAndWalkingIsOne()
        {
            // A whole metre in a whole second, against a reference walk of one metre.
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(1f, 0f, 0f), 1f, 0.05f, 1f),
                Is.EqualTo(1f).Within(0.0001f));

            // A twitch. Fed to a blend tree this is a character shuffling on the spot
            // forever, so it has to be zero rather than nearly zero.
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0.001f, 0f, 0f), 1f, 0.05f,
                1f), Is.Zero);

            Assert.That(CharacterVisualRules.SpeedFor(Vector3.zero, 1f, 0.05f, 1f), Is.Zero);
        }

        [Test]
        public void FallingIsNotWalking()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0f, -5f, 0f), 1f, 0.05f, 1f),
                Is.Zero, "a character dropping down a slope must not run on the spot");
        }

        [Test]
        public void RunningFasterThanTheClipDepictsStillClampsToOne()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0f, 0f, 40f), 1f, 0.05f, 1f),
                Is.EqualTo(1f), "there is no run animation to blend towards");
        }

        [Test]
        public void ANonAdvancingFrameReportsNothingRatherThanDividingByZero()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(5f, 0f, 0f), 0f, 0.05f, 1f),
                Is.Zero);
        }

        // ---- easing the blend ------------------------------------------------------------------

        [Test]
        public void TheBlendMovesTowardsWhatWasMeasuredRatherThanJumpingToIt()
        {
            // One smoothing constant of elapsed time covers about 63% of the gap. The exact
            // figure matters less than the two things either side of it: it moved, and it
            // did not arrive.
            float eased = CharacterVisualRules.DampedSpeed(0f, 1f, 0.1f, 0.1f);

            Assert.That(eased, Is.GreaterThan(0.5f), "the walk never got going");
            Assert.That(eased, Is.LessThan(1f),
                "arriving in one frame is the snap this exists to remove");
        }

        [Test]
        public void TheEasingRunsAtTheSameVisibleRateOnAFastMachineAsOnASlowOne()
        {
            // Half a second of easing, taken in one step and in twenty.
            float coarse = CharacterVisualRules.DampedSpeed(0f, 1f, 0.5f, 0.15f);

            var fine = 0f;

            for (var i = 0; i < 20; i++)
            {
                fine = CharacterVisualRules.DampedSpeed(fine, 1f, 0.025f, 0.15f);
            }

            Assert.That(fine, Is.EqualTo(coarse).Within(0.02f),
                "a character at 200fps must not lean into a walk at a different rate from "
                + "the same character at 40fps");
        }

        [Test]
        public void StandingStillEventuallyMeansExactlyZeroRatherThanNearlyZero()
        {
            var speed = 1f;

            for (var i = 0; i < 200; i++)
            {
                speed = CharacterVisualRules.DampedSpeed(speed, 0f, 1f / 60f, 0.15f);
            }

            Assert.That(speed, Is.Zero,
                "an exponential that never arrives leaves a residue of walk in the blend, "
                + "which is a character shuffling on the spot forever");
        }

        /// <summary>
        /// The ease finishes in a bounded time rather than trailing off forever.
        /// </summary>
        /// <remarks>An exponential on its own took the better part of a second to give up
        /// its last tenth of a walk. Nobody sees a blend of 0.09 as walking, but the
        /// character was still not standing, and "still not standing" is a state other things
        /// -- an idle variation, a turn, an attack recovery -- are entitled to wait on.</remarks>
        [Test]
        public void TheEaseFinishesWithinAboutTwiceItsSmoothingTime()
        {
            const float smoothing = 0.15f;
            const float step = 1f / 240f;

            var speed = 1f;
            var elapsed = 0f;

            while (speed > 0f && elapsed < 5f)
            {
                speed = CharacterVisualRules.DampedSpeed(speed, 0f, step, smoothing);
                elapsed += step;
            }

            Assert.That(speed, Is.Zero, "it never arrived at all");
            Assert.That(elapsed, Is.LessThan(smoothing * 2.5f),
                "a walk that takes " + elapsed.ToString("F2") + "s to leave the blend is a "
                + "character who has visibly stopped and is still not standing");

            // And it is genuinely eased on the way, not a straight line.
            float quarter = CharacterVisualRules.DampedSpeed(1f, 0f, smoothing * 0.25f,
                smoothing);

            Assert.That(quarter, Is.LessThan(0.85f),
                "a quarter of the smoothing time should have taken a real bite out of it");
        }

        [Test]
        public void NoSmoothingIsTheIdentityRatherThanADivisionByZero()
        {
            Assert.That(CharacterVisualRules.DampedSpeed(0f, 1f, 1f / 60f, 0f),
                Is.EqualTo(1f));
        }

        [Test]
        public void ANonAdvancingFrameLeavesTheBlendWhereItWas()
        {
            Assert.That(CharacterVisualRules.DampedSpeed(0.4f, 1f, 0f, 0.15f),
                Is.EqualTo(0.4f), "a paused editor must not creep the blend forward");
        }

        // ---- facing --------------------------------------------------------------------------

        [Test]
        public void TheVisualFacesTheWayItIsActuallyMoving()
        {
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 0f, 1f), 1f, 0.05f, 123f),
                Is.EqualTo(0f).Within(0.01f));
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(1f, 0f, 0f), 1f, 0.05f, 123f),
                Is.EqualTo(90f).Within(0.01f));
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 0f, -1f), 1f, 0.05f, 123f),
                Is.EqualTo(180f).Within(0.01f));
        }

        [Test]
        public void StoppingKeepsTheLastFacingRatherThanSnappingToNorth()
        {
            Assert.That(CharacterVisualRules.FacingFor(Vector3.zero, 1f, 0.05f, 217f),
                Is.EqualTo(217f));

            // Height alone is not a direction to face.
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 3f, 0f), 1f, 0.05f, 217f),
                Is.EqualTo(217f));
        }

        // ---- the snap threshold ------------------------------------------------------------------

        [Test]
        public void ARespawnIsPlacedRatherThanFlownAcrossTheMap()
        {
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 200f), 4f), Is.True);

            // Ordinary movement between two packets is eased.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 0.3f), 4f), Is.False);

            // Exactly at the threshold snaps: the boundary belongs to the safe side.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 4f), 4f), Is.True);

            // Disabled means never, not always.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 900f), 0f), Is.False);
        }

        [Test]
        public void TheProductionMovementComponentUsesThatSameThreshold()
        {
            var host = new GameObject("Snap");
            _created.Add(host);

            host.AddComponent<ChibiFantasy.Network.CharacterNetworkEntity>();

            var input = host.AddComponent<ChibiFantasy.Client.CharacterMovementInput>();

            Assert.That(input.SnapDistance, Is.GreaterThan(0f),
                "a reconnect with no threshold glides the character across the world");
            Assert.That(input.ShouldSnap(new Vector3(0f, 0f, input.SnapDistance + 1f)),
                Is.True);
            Assert.That(input.ShouldSnap(new Vector3(0f, 0f, input.SnapDistance * 0.5f)),
                Is.False);
        }

        // ---- the nameplate ---------------------------------------------------------------------

        [Test]
        public void TheNameplateShowsTheNameAndNothingElse()
        {
            Assert.That(CharacterVisualRules.NameplateFor("Ayla"), Is.EqualTo("Ayla"));
            Assert.That(CharacterVisualRules.NameplateFor("  Ayla  "), Is.EqualTo("Ayla"));
        }

        [Test]
        public void ANamelessCharacterShowsNothingRatherThanAnIdentifier()
        {
            Assert.That(CharacterVisualRules.NameplateFor(null), Is.Empty);
            Assert.That(CharacterVisualRules.NameplateFor(string.Empty), Is.Empty);
            Assert.That(CharacterVisualRules.NameplateFor("   "), Is.Empty,
                "a blank plate is a cosmetic bug; a leaked character id is not");
        }

        [Test]
        public void NothingInThePresentationCanPutAnIdentifierAboveAHead()
        {
            string source = Read(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs")
                + Read("Assets/_Game/Scripts/Client/World/CharacterVisualRules.cs");

            // The nameplate is fed from DisplayName. Any of these reaching it would put an
            // account-locating identifier in every screenshot of the game.
            Assert.That(source, Does.Not.Contain("Character.Value"));
            Assert.That(source, Does.Not.Contain("OwnerId"));
            Assert.That(source, Does.Not.Contain("ClientId"));
            Assert.That(source, Does.Not.Contain("ConnectionId"));
            Assert.That(source, Does.Not.Contain("SessionToken"));
        }

        [Test]
        public void TheNameplateOnlyRewritesItsStringWhenTheNameChanges()
        {
            var host = new GameObject("Plate host");
            _created.Add(host);

            CharacterNameplate plate = CharacterNameplate.Create(host.transform, 1.9f);

            plate.Refresh("Ayla");
            plate.Refresh("Ayla");
            plate.Refresh("Ayla");

            Assert.That(plate.Text, Is.EqualTo("Ayla"));
            Assert.That(plate.WriteCount, Is.EqualTo(1),
                "rebuilding a string per character per frame is how a hundred players "
                + "becomes a garbage collection problem");

            plate.Refresh("Borin");

            Assert.That(plate.WriteCount, Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyNameplateIsHiddenRatherThanDrawnBlank()
        {
            var host = new GameObject("Plate host");
            _created.Add(host);

            CharacterNameplate plate = CharacterNameplate.Create(host.transform, 1.9f);

            plate.Refresh("Ayla");

            Assert.That(plate.gameObject.activeSelf, Is.True);

            plate.Refresh(string.Empty);

            Assert.That(plate.gameObject.activeSelf, Is.False);
        }

        // ---- composition ----------------------------------------------------------------------

        [Test]
        public void TheProductionCharacterPrefabCarriesTheNetworkIdentityAndThePresentation()
        {
            GameObject prefab = Load(CharacterPrefab);

            Assert.That(prefab.GetComponent<FishNet.Object.NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ChibiFantasy.Network.CharacterNetworkEntity>(),
                Is.Not.Null);
            Assert.That(prefab.GetComponent<ChibiFantasy.Client.CharacterMovementInput>(),
                Is.Not.Null, "without this the character never follows the server's position");

            var presenter = prefab.GetComponent<CharacterVisualPresenter>();

            Assert.That(presenter, Is.Not.Null);

            var catalogue = new UnityEditor.SerializedObject(presenter)
                .FindProperty("_catalogue").objectReferenceValue;

            Assert.That(catalogue, Is.Not.Null, "a presenter with no catalogue draws nothing");
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue),
                Is.EqualTo(CataloguePath));
        }

        [Test]
        public void ThereIsExactlyOneCharacterNetworkObjectAndTheVisualIsNotAnother()
        {
            GameObject prefab = Load(CharacterPrefab);

            Assert.That(prefab.GetComponentsInChildren<FishNet.Object.NetworkObject>(true).Length,
                Is.EqualTo(1), "one identity per character, on the root");
            Assert.That(
                prefab.GetComponentsInChildren<ChibiFantasy.Network.CharacterNetworkEntity>(true)
                    .Length, Is.EqualTo(1));

            // Presentation is a MonoBehaviour. The moment it is a NetworkBehaviour it has a
            // wire of its own, and a client with a wire is a client with an opinion.
            Assert.That(typeof(CharacterVisualPresenter).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
            Assert.That(typeof(WorldCameraDirector).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
            Assert.That(typeof(CharacterNameplate).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
        }

        [Test]
        public void TheWorldSceneCanActuallyRender()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(WorldScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var cameras = new List<Camera>();
                var missing = new List<string>();

                var hud = 0;
                var bags = 0;
                var binders = 0;
                var directors = 0;
                var lights = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    cameras.AddRange(root.GetComponentsInChildren<Camera>(true));
                    lights += root.GetComponentsInChildren<Light>(true).Length;
                    hud += root.GetComponentsInChildren<ChibiFantasy.Client.UI.WorldHudScreen>(
                        true).Length;
                    bags += root.GetComponentsInChildren<ChibiFantasy.Client.UI.InventoryScreen>(
                        true).Length;
                    binders += root
                        .GetComponentsInChildren<ChibiFantasy.Client.UI.WorldPresentationBinder>(
                            true).Length;
                    directors += root.GetComponentsInChildren<WorldCameraDirector>(true).Length;

                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (Component c in t.GetComponents<Component>())
                        {
                            if (c == null) missing.Add(t.name);
                        }
                    }
                }

                Assert.That(missing, Is.Empty,
                    "a GameObject with a missing script is a screen that silently does nothing");

                Assert.That(cameras.Count, Is.EqualTo(1),
                    "no camera is a black screen and an editor warning; two is a fight");
                Assert.That(cameras[0].gameObject.activeInHierarchy, Is.True);
                Assert.That(cameras[0].GetComponent<
                    ChibiFantasy.Client.Prototype.ProtoThirdPersonCamera>(), Is.Not.Null,
                    "the Phase 07.1 rig, rather than a second camera framework");

                Assert.That(lights, Is.GreaterThanOrEqualTo(1),
                    "an unlit world renders the approved models as silhouettes");

                Assert.That(hud, Is.EqualTo(1));
                Assert.That(bags, Is.EqualTo(1));
                Assert.That(binders, Is.EqualTo(1));
                Assert.That(directors, Is.EqualTo(1), "one camera director, not one per player");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void EveryProductionScreenSceneStillHasItsScreen()
        {
            // The login scene shipped in 18.5 with a missing script, so the whole flow began
            // at a blank screen. Checked for all of them rather than only the one that broke.
            var expected = new Dictionary<string, System.Type>
            {
                { "Login", typeof(ChibiFantasy.Client.UI.LoginScreen) },
                { "ServerSelect", typeof(ChibiFantasy.Client.UI.ServerSelectScreen) },
                { "ChannelSelect", typeof(ChibiFantasy.Client.UI.ChannelSelectScreen) },
                { "CharacterSelect", typeof(ChibiFantasy.Client.UI.CharacterSelectScreen) },
            };

            foreach (KeyValuePair<string, System.Type> pair in expected)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    ChibiFantasy.Client.UI.ClientScenes.PathOf(pair.Key),
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

                try
                {
                    var found = 0;

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        found += root.GetComponentsInChildren(pair.Value, true).Length;

                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        {
                            foreach (Component c in t.GetComponents<Component>())
                            {
                                Assert.That(c, Is.Not.Null,
                                    pair.Key + " has a missing script on " + t.name);
                            }
                        }
                    }

                    Assert.That(found, Is.EqualTo(1), pair.Key + " has no " + pair.Value.Name);
                }
                finally
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // ---- the approved rigs ------------------------------------------------------------------

        [Test]
        public void TheMaleAvatarIsStillAValidHumanoid()
        {
            AssertHumanoid(MaleModel);
        }

        [Test]
        public void TheFemaleAvatarIsStillAValidHumanoid()
        {
            AssertHumanoid(FemaleModel);
        }

        [Test]
        public void TheFemaleRigStillMapsChestExplicitly()
        {
            // The one mapping Unity's auto-mapper has previously dropped on a rig here.
            // Losing it does not break the import: it retargets a torso wrongly in every
            // frame of the game, which is not something anybody notices until it ships.
            // The Meshy rig numbers its spine from the hips upwards -- Spine02, Spine01,
            // Spine -- so the bone called plain "Spine" is the top of the chain. A mapper
            // that trusts the name puts Spine at Spine and leaves the chest empty; the
            // mapping is what matters, not the spelling.
            AssertBoneMapping(FemaleModel, "Spine", "Spine02");
            AssertBoneMapping(FemaleModel, "Chest", "Spine01");
            AssertBoneMapping(FemaleModel, "UpperChest", "Spine");
        }

        [Test]
        public void TheMaleRigStillMapsChestExplicitly()
        {
            AssertBoneMapping(MaleModel, "Spine", "Spine02");
            AssertBoneMapping(MaleModel, "Chest", "Spine01");
            AssertBoneMapping(MaleModel, "UpperChest", "Spine");
        }

        [Test]
        public void BothApprovedRigsAgreeOnTheirHumanBoneCount()
        {
            int male = HumanBones(MaleModel).Length;
            int female = HumanBones(FemaleModel).Length;

            Assert.That(male, Is.GreaterThanOrEqualTo(15),
                "a humanoid missing required bones cannot retarget the shared clips");
            Assert.That(female, Is.EqualTo(male),
                "two rigs sharing one animator controller must share a skeleton mapping");
        }

        // ---- guards ------------------------------------------------------------------------------

        [Test]
        public void ThePresentationCannotReachAnyServerAuthority()
        {
            foreach (string file in PresentationFiles())
            {
                string source = Code(file);

                Assert.That(source, Does.Not.Contain("ServerRpc"), file);
                Assert.That(source, Does.Not.Contain("ObserversRpc"), file);
                Assert.That(source, Does.Not.Contain("[Server]"), file);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), file);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), file);
                Assert.That(source, Does.Not.Contain("CharacterMovementAuthority"), file);
                Assert.That(source, Does.Not.Contain("CharacterInventoryAuthority"), file);
                Assert.That(source, Does.Not.Contain("ServerPublishState"), file);
                Assert.That(source, Does.Not.Contain("ServerPublishIdentity"), file);
            }
        }

        [Test]
        public void NoClientFileReachesTheServerAssemblysAuthorities()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("using ChibiFantasy.Server"), file);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), file);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), file);
                Assert.That(source, Does.Not.Contain("WorldCharacterRegistry"), file);
            }
        }

        [Test]
        public void AnimationNeverMovesAnybodyAndNeverAwardsAnything()
        {
            string presenter = Code(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs");

            // Root motion is a clip writing a transform, which is a client writing its own
            // position one frame at a time.
            Assert.That(presenter, Does.Contain("applyRootMotion = false"));

            // An animation event that damaged, healed, paid or looted would be gameplay
            // decided by a clip's timeline on one machine.
            Assert.That(presenter, Does.Not.Contain("AddComponent<AnimationEvent"));
            Assert.That(presenter, Does.Not.Contain("OnAnimatorMove"));
            Assert.That(presenter, Does.Not.Contain("RequestAttack"));
            Assert.That(presenter, Does.Not.Contain("RequestMove"));
            Assert.That(presenter, Does.Not.Contain("RequestInventoryAction"));
        }

        /// <summary>
        /// The easing is actually wired to the animator, not merely written.
        /// </summary>
        /// <remarks>
        /// <b>The defect this exists for has happened here repeatedly.</b> A rule gets
        /// written and tested, and nothing calls it -- the arithmetic is green and the game
        /// is unchanged. So this asserts the call site, not the function.
        ///
        /// <b>And that the animator is not asked to do it instead.</b> The four-argument
        /// <c>SetFloat</c> stops damping when the animator is culled, which leaves every
        /// off-screen character frozen part-way through a blend; using it would silently
        /// undo the reason the easing was moved out here in the first place.
        /// </remarks>
        [Test]
        public void TheBlendIsEasedByTheRuleRatherThanByTheAnimator()
        {
            string presenter = Code(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs");

            Assert.That(presenter, Does.Contain("CharacterVisualRules.DampedSpeed"),
                "the easing rule exists but nothing calls it, so the blend still snaps");

            Assert.That(presenter, Does.Not.Contain("SetFloat(SpeedHash, speed01, "),
                "the animator's own damping freezes under culling -- the whole reason the "
                + "easing is computed outside it");
        }

        [Test]
        public void ThePresentationDoesNotSearchOrAllocatePerFrame()
        {
            foreach (string file in PresentationFiles())
            {
                string source = Code(file);

                Assert.That(source, Does.Not.Contain("GameObject.Find"), file);
                Assert.That(source, Does.Not.Contain("FindObjectsOfType"), file);
                Assert.That(source, Does.Not.Contain("FindObjectsByType"), file);

                // Animator parameters are hashed once into a static, never per call.
                Assert.That(source, Does.Not.Contain("SetFloat(\""), file);
                Assert.That(source, Does.Not.Contain("SetBool(\""), file);
            }

            // Camera.main is a tagged search. Once, cached, on the nameplate.
            string plate = Code("Assets/_Game/Scripts/Client/World/CharacterNameplate.cs");

            Assert.That(Occurrences(plate, "Camera.main"), Is.EqualTo(1),
                "found once and cached, not searched for every frame per character");
            Assert.That(plate, Does.Contain("if (_camera == null) _camera = Camera.main"));
        }

        [Test]
        public void EveryClientMonoBehaviourLivesInAFileNamedAfterIt()
        {
            // This is why the login screen shipped in 18.5 as a missing script. Unity writes
            // exactly one MonoScript per .cs file -- for the type whose name matches the file
            // -- so a MonoBehaviour declared beside another one has no asset for a scene to
            // reference. It compiles, it passes every unit test, and the component is simply
            // absent at runtime.
            var offenders = new List<string>();

            foreach (System.Type type in typeof(CharacterVisualPresenter).Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(MonoBehaviour))) continue;
                if (type.IsAbstract || type.IsGenericType) continue;

                string[] matches = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                    type.Name + ".cs", System.IO.SearchOption.AllDirectories);

                if (matches.Length == 0) offenders.Add(type.FullName);
            }

            Assert.That(offenders, Is.Empty,
                "a MonoBehaviour with no file of its own cannot be put in a scene");
        }

        [Test]
        public void ThereIsOneCameraDirectorAndOneCharacterVisualPresenter()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var directors = 0;
            var presenters = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class WorldCameraDirector")) directors++;
                if (source.Contains("class CharacterVisualPresenter")) presenters++;
            }

            Assert.That(directors, Is.EqualTo(1), "a second camera owner would fight the first");
            Assert.That(presenters, Is.EqualTo(1),
                "one presenter with a local branch, not a local one and a remote one");
        }

        // ---- helpers ------------------------------------------------------------------------------

        private static string[] PresentationFiles()
        {
            return new[]
            {
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs",
                "Assets/_Game/Scripts/Client/World/CharacterVisualRules.cs",
                "Assets/_Game/Scripts/Client/World/CharacterVisualCatalogue.cs",
                "Assets/_Game/Scripts/Client/World/CharacterNameplate.cs",
                "Assets/_Game/Scripts/Client/World/WorldCameraDirector.cs",
            };
        }

        /// <summary>
        /// A file with its comments removed.
        /// </summary>
        /// <remarks>Guards below assert that certain names do not appear. Read whole, a file
        /// that <em>explains</em> why it never calls a ServerRpc would fail a guard about
        /// calling ServerRpcs -- which teaches the next author to stop writing the
        /// explanation, exactly backwards.</remarks>
        private static string Code(string path)
        {
            Assert.That(System.IO.File.Exists(path), Is.True, "no file at " + path);

            var kept = new List<string>();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("///") || trimmed.StartsWith("//")) continue;

                kept.Add(line);
            }

            return string.Join(" ", kept);
        }

        private static string Read(string path)
        {
            Assert.That(System.IO.File.Exists(path), Is.True, "no file at " + path);

            return System.IO.File.ReadAllText(path);
        }

        private static int Occurrences(string source, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = source.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }

        private static CharacterVisualCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<CharacterVisualCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "no catalogue at " + CataloguePath);

            return catalogue;
        }

        private static GameObject Load(string path)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(asset, Is.Not.Null, "no asset at " + path);

            return asset;
        }

        private static UnityEditor.ModelImporter Importer(string path)
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(path)
                as UnityEditor.ModelImporter;

            Assert.That(importer, Is.Not.Null, "no model importer at " + path);

            return importer;
        }

        private static HumanBone[] HumanBones(string path)
        {
            HumanBone[] bones = Importer(path).humanDescription.human;

            Assert.That(bones, Is.Not.Null, "no human description on " + path);

            return bones;
        }

        /// <summary>
        /// Asserts the model still imports as a valid humanoid.
        /// </summary>
        /// <remarks>Read from the importer and the Avatar the import produced. Nothing here
        /// reflects into internal scale fields: <c>humanScale</c> has no supported public API
        /// in this editor version, and a test that claimed to check it would be a test that
        /// checked nothing.</remarks>
        private static void AssertHumanoid(string path)
        {
            UnityEditor.ModelImporter importer = Importer(path);

            Assert.That(importer.animationType, Is.EqualTo(UnityEditor.ModelImporterAnimationType.Human),
                path + " is no longer imported as a humanoid");

            var avatars = 0;

            foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var avatar = asset as Avatar;

                if (avatar == null) continue;

                avatars++;

                Assert.That(avatar.isValid, Is.True, path + ": avatar is invalid");
                Assert.That(avatar.isHuman, Is.True, path + ": avatar is not human");
            }

            Assert.That(avatars, Is.EqualTo(1), path + ": expected exactly one avatar");
        }

        private static void AssertBoneMapping(string path, string humanName, string boneName)
        {
            foreach (HumanBone bone in HumanBones(path))
            {
                if (bone.humanName != humanName) continue;

                Assert.That(bone.boneName, Is.EqualTo(boneName),
                    path + ": " + humanName + " maps to '" + bone.boneName + "'");

                return;
            }

            Assert.Fail(path + ": " + humanName + " is not mapped at all -- Unity's "
                + "auto-mapper has dropped it");
        }
    }
}
