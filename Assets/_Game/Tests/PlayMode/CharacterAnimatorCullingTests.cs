#if UNITY_EDITOR

using System.Collections;
using ChibiFantasy.Client.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A running character keeps running when the camera is not looking straight at it.
    /// </summary>
    /// <remarks>
    /// <b>The defect this pins.</b> Both production rigs import with
    /// <c>AnimatorCullingMode.CullUpdateTransforms</c>, which stops writing bone transforms
    /// as soon as the renderer's bounds leave the frustum. Position is replicated and keeps
    /// advancing, so the character slides along with its legs stopped and then snaps back
    /// into stride -- reported as "the legs stutter while running", and reported against
    /// running up a hill and up the bridge in particular, because the camera's position is
    /// smoothed and its collision snaps inward as the rising ground behind it intrudes, so a
    /// climbing character leads the camera and drifts to the edge of the frame.
    ///
    /// <b>Measured, not argued.</b> Off screen with the imported setting the foot bone
    /// travels exactly zero; with <see cref="CharacterVisualPresenter.PrepareAnimator"/> it
    /// keeps moving. Both halves are asserted, so this fails if the rig's import default ever
    /// stops being the dangerous one (in which case the fix is redundant and can go) and it
    /// fails if the fix is removed.
    /// </remarks>
    public sealed class CharacterAnimatorCullingTests
    {
        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/CharacterVisualCatalogue.asset";

        /// <summary>Far enough to the side that nothing of the rig is in frame.</summary>
        private static readonly Vector3 OffScreen = new Vector3(60f, 0f, 0f);

        private CharacterVisualCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<CharacterVisualCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "the visual catalogue has moved");

            return catalogue;
        }

        [UnityTest]
        public IEnumerator TheImportedRigsStillCullTheirBonesSoTheFixIsStillNeeded()
        {
            yield return null;

            CharacterVisualCatalogue catalogue = Catalogue();

            foreach (GameObject model in new[] { catalogue.Male, catalogue.Female })
            {
                GameObject subject = Object.Instantiate(model);

                try
                {
                    var animator = subject.GetComponentInChildren<Animator>();

                    Assert.That(animator, Is.Not.Null, model.name + " has no animator");
                    Assert.That(animator.cullingMode,
                        Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms),
                        model.name + ": the import default changed. If it is now "
                        + "AlwaysAnimate the presenter's PrepareAnimator is redundant; if it "
                        + "is something else, check that it still animates off screen.");
                }
                finally
                {
                    Object.DestroyImmediate(subject);
                }
            }
        }

        [UnityTest]
        public IEnumerator APreparedCharacterKeepsAnimatingWhileItIsOffScreen()
        {
            yield return null;

            CharacterVisualCatalogue catalogue = Catalogue();

            var cameraObject = new GameObject("culling test camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, -4f);
            camera.transform.rotation = Quaternion.identity;

            GameObject subject = Object.Instantiate(catalogue.Male);
            subject.transform.position = Vector3.zero;

            try
            {
                var animator = subject.GetComponentInChildren<Animator>();

                // Exactly what the presenter does, called rather than copied.
                CharacterVisualPresenter.PrepareAnimator(animator);

                animator.runtimeAnimatorController = catalogue.LocomotionFor(1);
                animator.SetFloat("Speed", 1f);

                Assert.That(animator.cullingMode,
                    Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
                Assert.That(animator.applyRootMotion, Is.False,
                    "root motion would let the animation write the character's position");

                Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

                Assert.That(foot, Is.Not.Null, "the rig no longer maps a left foot");

                // Let the blend reach the run before anything is measured.
                for (var i = 0; i < 40; i++) yield return null;

                subject.transform.position = OffScreen;

                for (var i = 0; i < 10; i++) yield return null;

                float travelled = 0f;
                Vector3 previous = foot.position - subject.transform.position;

                for (var i = 0; i < 60; i++)
                {
                    yield return null;

                    Vector3 local = foot.position - subject.transform.position;
                    travelled += (local - previous).magnitude;
                    previous = local;
                }

                Assert.That(travelled, Is.GreaterThan(0.01f),
                    "the foot bone travelled " + travelled.ToString("F4")
                    + " m over 60 frames off screen -- the pose is frozen, which is the "
                    + "running stutter this exists to prevent");
            }
            finally
            {
                Object.DestroyImmediate(subject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}

#endif
