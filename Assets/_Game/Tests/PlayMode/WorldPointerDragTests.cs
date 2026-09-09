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
    /// Holding the button steers; it does not replay clicks.
    /// </summary>
    /// <remarks>
    /// <b>Why this is worth a test of its own.</b> A held button is the one gesture that can
    /// change its mind about what it meant halfway through. The cursor sweeps across the
    /// world on its way to where the player is pointing, and anything it passes over -- a
    /// monster, a loot pile, an NPC -- would, if the hold were treated as a stream of
    /// clicks, silently replace a walk with an attack or a pickup. So the rule is that a
    /// drag only ever moves a ground destination, and that rule is what these check.
    ///
    /// <b>Real camera, real colliders, no network.</b> <see cref="WorldPointerInput.ClickAt"/>
    /// and <see cref="WorldPointerInput.DragTo"/> take a screen point and ask physics, which
    /// is all that is needed here: the networked half of the component is not what the drag
    /// rule lives in, and standing a server up to reach it would test the server.
    /// </remarks>
    public sealed class WorldPointerDragTests
    {
        private GameObject _ground;
        private GameObject _cameraObject;
        private GameObject _inputObject;
        private Camera _camera;
        private WorldPointerInput _pointer;

        /// <summary>
        /// Where this test's own world is built.
        /// </summary>
        /// <remarks>
        /// A kilometre up, and deliberately. Other suites in this assembly load the shipped
        /// world, and terrain left standing under the origin would answer a ray that is
        /// supposed to find nothing -- which passes when this class runs alone and fails
        /// when it runs after them. Nothing in the project is built up here.
        /// </remarks>
        private static readonly Vector3 Elsewhere = new Vector3(0f, 3000f, 0f);

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.transform.position = Elsewhere;
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);

            _cameraObject = new GameObject("drag test camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.transform.position = Elsewhere + new Vector3(0f, 14f, -14f);
            _camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            _inputObject = new GameObject("drag test pointer");
            _pointer = _inputObject.AddComponent<WorldPointerInput>();
            _pointer.Compose(null, _camera, null, null);

            // The floor was moved after it was created, and physics does not necessarily
            // know that until it is told. Without this the ray is cast against a collider
            // still sitting at the origin and finds nothing where the picture shows ground.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_inputObject);
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_ground);
        }

        /// <summary>Two points on screen that are far enough apart to be different places.</summary>
        private static Vector2 Left => new Vector2(Screen.width * 0.35f, Screen.height * 0.5f);

        private static Vector2 Right => new Vector2(Screen.width * 0.65f, Screen.height * 0.5f);

        [UnityTest]
        public IEnumerator AHeldButtonMovesTheDestinationItIsDraggedTo()
        {
            yield return null;

            Assert.That(_pointer.ClickAt(Left), Is.EqualTo(WorldPointerInput.Intent.Ground),
                "the test camera is not looking at the test ground");

            Vector3 first = _pointer.Destination;

            Assert.That(_pointer.DragTo(Right), Is.True, "the drag found no ground");

            Assert.That(_pointer.Destination.x, Is.Not.EqualTo(first.x).Within(0.1f),
                "the destination did not follow the pointer");
            Assert.That(_pointer.Current, Is.EqualTo(WorldPointerInput.Intent.Ground));
            Assert.That(_pointer.DragUpdates, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ADragTakesUpTheWalkAgainAfterArrival()
        {
            yield return null;

            _pointer.ClickAt(Left);
            _pointer.Stop();

            Assert.That(_pointer.Current, Is.EqualTo(WorldPointerInput.Intent.None),
                "arrival did not clear the intent");

            Assert.That(_pointer.DragTo(Right), Is.True,
                "a hold that outlived its destination stopped steering");
            Assert.That(_pointer.Current, Is.EqualTo(WorldPointerInput.Intent.Ground));
        }

        [UnityTest]
        public IEnumerator ADragOverNothingLeavesTheDestinationAlone()
        {
            yield return null;

            _pointer.ClickAt(Left);

            Vector3 before = _pointer.Destination;

            // Nothing under the cursor at all. Taking the ground away is a surer way to say
            // that than aiming at the horizon, where a large enough floor is still hit.
            _ground.SetActive(false);
            Physics.SyncTransforms();

            Assert.That(_pointer.DragTo(Right), Is.False,
                "a drag off the world moved the destination");

            Assert.That(_pointer.Destination, Is.EqualTo(before));
            Assert.That(_pointer.DragUpdates, Is.EqualTo(0));
        }
    }
}

#endif
