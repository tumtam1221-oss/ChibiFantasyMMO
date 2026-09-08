using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What the locomotion clips actually do to a body at runtime.
    /// </summary>
    /// <remarks>
    /// <b>Why runtime and not the editor sampling API.</b> <c>AnimationMode.SampleAnimationClip</c>
    /// reports a body drifting half a metre across the walk loop, and a real
    /// <c>Animator</c> does not necessarily agree with it, because the two treat a humanoid's
    /// <c>RootT</c> channel differently. Only one of them is what a player sees. These tests
    /// drive a real Animator with the real controller and measure the rig, so the answer
    /// comes from the thing that actually renders.
    ///
    /// <b>What a wrong answer looks like on screen.</b> If the body translates within the
    /// clip while the transform does not, the character slides forward and then snaps back
    /// every time the loop wraps -- which reads as a stride that ends by dragging the feet
    /// together rather than as continuous walking.
    /// </remarks>
    [TestFixture]
    internal sealed class LocomotionClipRuntimeTests
    {
        private const string MaleModel =
            "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy.fbx";

        private const string Controller =
            "Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller";

        private GameObject _subject;

        [TearDown]
        public void TearDown()
        {
            if (_subject != null) Object.Destroy(_subject);
        }

        private Animator Spawn()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MaleModel);

            _subject = Object.Instantiate(prefab);
            _subject.transform.position = Vector3.zero;
            _subject.transform.rotation = Quaternion.identity;

            Animator animator = _subject.GetComponentInChildren<Animator>();

            animator.applyRootMotion = false;
            animator.runtimeAnimatorController =
                UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();

            return animator;
        }

        /// <summary>Samples the rig for a while and reports where the body sat each frame.</summary>
        private static IEnumerator Trace(Animator animator, float speed, float seconds,
            List<float> hipsForward, List<float> transformForward)
        {
            animator.SetFloat("Speed", speed);

            var elapsed = 0f;

            while (elapsed < seconds)
            {
                yield return null;

                elapsed += Time.deltaTime;

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform root = animator.transform;

                hipsForward.Add(root.InverseTransformPoint(hips.position).z);
                transformForward.Add(root.position.z);
            }
        }

        [UnityTest]
        public IEnumerator TheWalkClipDoesNotTranslateTheBodyAwayFromItsTransform()
        {
            Animator animator = Spawn();

            var hips = new List<float>();
            var moved = new List<float>();

            // Comfortably more than three loops of a 1.07s clip.
            yield return Trace(animator, 1f, 4f, hips, moved);

            Assert.That(hips, Is.Not.Empty, "the animator never ticked");

            var min = float.MaxValue;
            var max = float.MinValue;

            foreach (float h in hips)
            {
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }

            // A walk cycle rocks the hips a few centimetres. Anything approaching the stride
            // length is the clip's forward travel leaking into the pose.
            Assert.That(max - min, Is.LessThan(0.20f),
                "the body wanders " + (max - min).ToString("F3") + "m forward within the "
                + "walk clip while the transform stays put, so it slides out and snaps back "
                + "once per loop -- which is the stride appearing to end with the feet "
                + "dragged together");
        }

        [UnityTest]
        public IEnumerator TheWalkClipNeverMovesTheTransformItself()
        {
            Animator animator = Spawn();

            var hips = new List<float>();
            var moved = new List<float>();

            yield return Trace(animator, 1f, 3f, hips, moved);

            foreach (float z in moved)
            {
                Assert.That(z, Is.EqualTo(0f).Within(0.0001f),
                    "root motion moved the character: animation deciding position is the "
                    + "client deciding position");
            }
        }

        [UnityTest]
        public IEnumerator TheBodyReturnsToWhereItStartedEachLoopRatherThanSnappingBack()
        {
            Animator animator = Spawn();

            var hips = new List<float>();
            var moved = new List<float>();

            yield return Trace(animator, 1f, 4f, hips, moved);

            // The largest single-frame change in body position. A loop that snaps shows up
            // here as one frame far larger than its neighbours.
            var worst = 0f;

            for (var i = 1; i < hips.Count; i++)
            {
                worst = Mathf.Max(worst, Mathf.Abs(hips[i] - hips[i - 1]));
            }

            Assert.That(worst, Is.LessThan(0.05f),
                "one frame moved the body " + worst.ToString("F3") + "m, which is a loop "
                + "wrapping rather than a body walking");
        }

        [UnityTest]
        public IEnumerator StandingStillHoldsTheBodyStill()
        {
            Animator animator = Spawn();

            var hips = new List<float>();
            var moved = new List<float>();

            yield return Trace(animator, 0f, 3f, hips, moved);

            var min = float.MaxValue;
            var max = float.MinValue;

            foreach (float h in hips)
            {
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }

            Assert.That(max - min, Is.LessThan(0.05f),
                "a breathing idle must not travel: " + (max - min).ToString("F3") + "m");
        }
    }
}
