using ChibiFantasy.Client.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The mouse wheel, which is a camera defect and not a locomotion one.
    /// </summary>
    /// <remarks>
    /// Kept through the 18.18B1-G2 baseline restore because it is unrelated to the movement
    /// stack that was rolled back: the setting was metres per raw wheel unit, sized for the
    /// legacy input manager's 120-per-notch wheel, while the Input System reports about one.
    /// A notch moved the camera four millimetres. Measured on a live rig sitting at 3.016
    /// metres after several notches against a 3.0 default.
    /// </remarks>
    [TestFixture]
    internal sealed class CameraZoomTests
    {
        [Test]
        public void OneWheelNotchIsOneNotchOnEitherBackend()
        {
            Assert.That(ProtoThirdPersonCamera.NotchesFrom(1f), Is.EqualTo(1f).Within(0.001f),
                "the Input System reports about one per notch");

            Assert.That(ProtoThirdPersonCamera.NotchesFrom(120f), Is.EqualTo(1f).Within(0.001f),
                "a backend counting in WHEEL_DELTA still means one notch");

            Assert.That(ProtoThirdPersonCamera.NotchesFrom(-240f), Is.EqualTo(-2f).Within(0.001f));

            // A trackpad scrolls continuously and must not be rounded into steps.
            Assert.That(ProtoThirdPersonCamera.NotchesFrom(0.25f),
                Is.EqualTo(0.25f).Within(0.001f));

            Assert.That(ProtoThirdPersonCamera.NotchesFrom(0f), Is.EqualTo(0f));
        }

        [Test]
        public void TheWheelMovesTheCameraWithinItsLimits()
        {
            var settings = ScriptableObject.CreateInstance<ProtoCameraSettings>();

            try
            {
                Assert.That(settings.zoomMetresPerNotch, Is.GreaterThan(0.05f),
                    "a notch worth less than five centimetres is a camera that does not zoom");

                float range = settings.maxDistance - settings.minDistance;

                Assert.That(range, Is.GreaterThan(0f), "there is nowhere to zoom to");

                float notches = range / settings.zoomMetresPerNotch;

                Assert.That(notches, Is.LessThan(40f),
                    "crossing the range takes " + notches + " notches; the defect was that "
                    + "it took about twelve hundred");

                Assert.That(settings.minDistance, Is.GreaterThan(0f),
                    "a distance of zero puts the camera inside the character");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
