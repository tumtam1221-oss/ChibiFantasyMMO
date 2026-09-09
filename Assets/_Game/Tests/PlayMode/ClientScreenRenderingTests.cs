#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Whether a person pressing Play can actually see and use the login screen.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> The login scene had no camera. Its canvas is built
    /// at runtime and is a screen-space overlay, which reads like it needs nothing to draw
    /// it -- but a scene with no camera gives the render pipeline nothing to render, and
    /// pressing Play showed "Display 1 -- No cameras rendering" over an invisible form.
    /// Every screen test passed throughout, because they all drive the screens in code.
    ///
    /// <b>This one asks the runtime question.</b> The scene is loaded in Play Mode, as a
    /// player loads it, and what is asserted is that something is rendering and that the
    /// three things a person must click are there, on, and the right size.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientScreenRenderingTests
    {
        private const string LoginScene = "Assets/_Game/Scenes/Client/Login.unity";

        private Scene _scene;
        private Scene _previouslyActive;

        /// <summary>
        /// Puts the editor back exactly as this fixture found it.
        /// </summary>
        /// <remarks>
        /// <b>Both halves matter, and both were learned the hard way.</b> Loading the login
        /// scene builds the client root, which moves itself out of that scene and survives
        /// every later load -- so unloading the scene does not remove it, and it would go on
        /// binding screens for the rest of the run. And the active scene has to be handed
        /// back before this one is unloaded, or every fixture afterwards instantiates into a
        /// scene that no longer exists.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ClientApplicationBootstrap root = ClientApplicationBootstrap.Current;

            if (root != null) Object.DestroyImmediate(root.gameObject);

            if (_previouslyActive.IsValid() && _previouslyActive.isLoaded)
            {
                SceneManager.SetActiveScene(_previouslyActive);
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheLoginSceneRendersAndItsFormCanBeUsed()
        {
            yield return LoadLogin();

            // 1. Something is drawing to the display a player is looking at.
            var rendering = 0;

            foreach (Camera camera in Object.FindObjectsByType<Camera>(
                FindObjectsSortMode.None))
            {
                if (!camera.enabled) continue;
                if (!camera.gameObject.activeInHierarchy) continue;
                if (camera.targetDisplay != 0) continue;
                if (camera.targetTexture != null) continue;

                rendering++;
            }

            Assert.That(rendering, Is.EqualTo(1),
                "a player pressing Play sees \"No cameras rendering\" unless exactly one "
                + "camera is drawing display 1; found " + rendering);

            // 2. The screen built itself, and its canvas is on.
            var login = Object.FindAnyObjectByType<LoginScreen>(FindObjectsInactive.Include);

            Assert.That(login, Is.Not.Null, "the login scene has no login screen");

            Canvas canvas = login.GetComponentInChildren<Canvas>(true);

            Assert.That(canvas, Is.Not.Null, "the login screen built no canvas");
            Assert.That(canvas.isActiveAndEnabled, Is.True, "the login canvas is switched off");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));

            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                "without a raycaster nothing on this screen can be clicked");

            // 3. There is an event system, or no click reaches anything.
            Assert.That(Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include), Is.Not.Null,
                "no event system: the form would be visible and dead");

            // 4. The three things a person must use are present, on, and big enough to hit.
            TMP_InputField[] fields = login.GetComponentsInChildren<TMP_InputField>(true);

            Assert.That(fields.Length, Is.GreaterThanOrEqualTo(2),
                "expected a username and a password field, found " + fields.Length);

            foreach (TMP_InputField field in fields)
            {
                Assert.That(field.isActiveAndEnabled, Is.True,
                    field.name + " is not active, so it cannot be typed into");

                Assert.That(field.interactable, Is.True, field.name + " is not interactable");

                Rect rect = field.GetComponent<RectTransform>().rect;

                Assert.That(rect.width, Is.GreaterThan(40f), field.name + " is too small to click");
                Assert.That(rect.height, Is.GreaterThan(10f), field.name + " is too small to click");
            }

            Button[] buttons = login.GetComponentsInChildren<Button>(true);

            Assert.That(buttons.Length, Is.GreaterThanOrEqualTo(1), "no button to sign in with");

            var usable = 0;

            foreach (Button button in buttons)
            {
                if (button.isActiveAndEnabled && button.interactable) usable++;
            }

            Assert.That(usable, Is.GreaterThanOrEqualTo(1),
                "the sign-in button exists but nothing on this screen can be pressed");
        }

        /// <summary>
        /// Clicking a field puts a caret in that field.
        /// </summary>
        /// <remarks>
        /// <b>Two defects, both invisible to every other test.</b> A field composed in code
        /// gets its text component after <c>AddComponent</c> has already run TMP's
        /// <c>OnEnable</c>, and TMP builds its caret there and only when a text component is
        /// already present -- so the field took focus, accepted typing, and never showed a
        /// cursor. And fitting a sprite inflates the rect around the artwork, which is what
        /// UGUI hit-tests: the two fields' rects overlapped far enough that a click in the
        /// middle of the login field landed on the password one.
        ///
        /// <b>Neither is reachable by driving the screen in code.</b> <c>Fill</c> and
        /// <c>Submit</c> never ask where a rect is or whether a caret exists, which is why
        /// this goes through a real raycast on a real loaded scene.
        /// </remarks>
        [UnityTest]
        public IEnumerator ClickingAFieldPutsACaretInThatField()
        {
            yield return LoadLogin();

            var login = Object.FindAnyObjectByType<LoginScreen>(FindObjectsInactive.Include);

            Assert.That(login, Is.Not.Null, "the login scene has no login screen");

            TMP_InputField[] fields = login.GetComponentsInChildren<TMP_InputField>(true);

            Assert.That(fields.Length, Is.GreaterThanOrEqualTo(2),
                "expected a login and a password field, found " + fields.Length);

            EventSystem events = EventSystem.current;

            Assert.That(events, Is.Not.Null, "no event system: nothing can be clicked");

            Canvas canvas = login.GetComponentInChildren<Canvas>(true);
            Camera viewer = canvas.worldCamera;

            foreach (TMP_InputField field in fields)
            {
                Assert.That(field.textViewport, Is.Not.Null, field.name + " has no viewport");

                Assert.That(field.textViewport.Find("Caret"), Is.Not.Null,
                    field.name + " has no caret object, so a player who clicks it sees no "
                    + "cursor however well it takes focus");

                // Where the artwork is actually drawn, which is not the whole rect.
                RectTransform drawn = field.GetComponent<RectTransform>();
                Transform hitArea = drawn.Find("Hit Area");

                if (hitArea != null) drawn = (RectTransform)hitArea;

                var data = new PointerEventData(events)
                {
                    position = RectTransformUtility.WorldToScreenPoint(
                        viewer, drawn.TransformPoint(drawn.rect.center)),
                    button = PointerEventData.InputButton.Left,
                };

                var hits = new List<RaycastResult>();
                events.RaycastAll(data, hits);

                Assert.That(hits.Count, Is.GreaterThan(0),
                    "nothing at all was hit in the middle of " + field.name);

                GameObject top = hits[0].gameObject;

                Assert.That(top.GetComponentInParent<TMP_InputField>(), Is.SameAs(field),
                    "a click in the middle of " + field.name + " landed on '" + top.name
                    + "' instead -- the fields' hit areas overlap");

                ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerClickHandler);

                // TMP activates on the update after the click, so one frame is not enough.
                yield return null;
                yield return null;

                Assert.That(field.isFocused, Is.True,
                    "clicking " + field.name + " did not give it focus");
            }
        }

        private IEnumerator LoadLogin()
        {
            _previouslyActive = SceneManager.GetActiveScene();

            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                LoginScene, new LoadSceneParameters(LoadSceneMode.Additive));

            for (var i = 0; i < 400 && !_scene.isLoaded; i++) yield return null;

            Assert.That(_scene.isLoaded, Is.True, "the login scene would not load");

            SceneManager.SetActiveScene(_scene);

            // A few frames, so Awake and the first Update have built the screen.
            for (var i = 0; i < 10; i++) yield return null;
        }
    }
}

#endif
