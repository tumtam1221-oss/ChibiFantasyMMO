using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// An empty world must never be a silent world.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> A manual test signed in for real, was admitted by
    /// PHP for real, loaded GameWorld -- and saw sky, ground and nothing else. The client had
    /// tried to reach the world server, failed, and said nothing: <c>ComposeWorld</c> threw
    /// away the result of <c>Connect</c>, and <c>OnConnectionState</c> returned early on
    /// every state that was not <c>Started</c>. A dead socket and a world that has not
    /// spawned anything yet looked identical from inside the game.
    ///
    /// <b>Why the existing suites could not catch it.</b> Every PlayMode test that reaches
    /// the world starts its own server first, so the connection always succeeds; there was no
    /// test in which connecting fails. These are source guards over the two places that
    /// dropped the information, because the thing to protect is that the failure is
    /// *reported* -- and a test that called the reporting method itself would prove only
    /// that the method exists, which is what the gate asks not to write.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldConnectionReportingTests
    {
        private const string Bootstrap =
            "Assets/_Game/Scripts/Client/WorldClientBootstrap.cs";

        private const string Root =
            "Assets/_Game/Scripts/Client/ClientApplicationBootstrap.cs";

        [Test]
        public void AFailedConnectionIsRecordedRatherThanDropped()
        {
            string source = System.IO.File.ReadAllText(Bootstrap);

            Assert.That(source, Does.Not.Contain(
                "if (args.ConnectionState != LocalConnectionState.Started) return;"),
                "every state but Started was dropped, which is how a dead socket became an "
                + "empty world with nothing said");

            Assert.That(source, Does.Contain("public bool ConnectionFailed"),
                "nothing can tell 'not connected' apart from 'nothing spawned yet'");

            Assert.That(source, Does.Contain("LocalConnectionState.Stopped"),
                "the stopped state is what a failed connect arrives as");
        }

        [Test]
        public void TheCompositionRootReadsTheResultOfConnecting()
        {
            string source = System.IO.File.ReadAllText(Root);

            Assert.That(source, Does.Not.Contain("            World.Connect();"),
                "the result of connecting was discarded by the production composition root");

            Assert.That(source, Does.Contain("if (!World.Connect())"),
                "the composition root must act on a connection it could not start");
        }

        /// <summary>
        /// The world is composed once per load, and the presentation with it.
        /// </summary>
        /// <remarks>Guards the other half of the reported symptom: a world that is composed
        /// twice has two of everything, including two owned characters. The root composes on
        /// scene load and the presentation inside that same call, so the two cannot drift
        /// apart.</remarks>
        [Test]
        public void TheWorldIsComposedFromTheSceneLoadAndComposesItsPresentation()
        {
            string source = System.IO.File.ReadAllText(Root);

            Assert.That(source,
                Does.Contain("if (scene.name == ClientScenes.World) ComposeWorld();"),
                "the world is no longer composed when the world scene loads");

            Assert.That(source, Does.Contain("ComposePresentation();"),
                "the presentation is not composed with the world, so an admitted player "
                + "would have no character, camera or HUD");

            int composes = Occurrences(source, "ComposePresentation();");

            Assert.That(composes, Is.EqualTo(1),
                "composing the presentation from more than one place is how a reconnect ends "
                + "up with two of everything; found " + composes);
        }

        /// <summary>Rebinding a screen or a scene must not stack subscriptions.</summary>
        [Test]
        public void EveryRebindUnsubscribesBeforeItSubscribes()
        {
            foreach (string path in new[]
                     {
                         Root,
                         "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs",
                     })
            {
                string source = System.IO.File.ReadAllText(path);

                int added = Occurrences(source, " += ");
                int removed = Occurrences(source, " -= ");

                Assert.That(removed, Is.GreaterThanOrEqualTo(added),
                    path + " adds " + added + " handlers but removes only " + removed
                    + "; a rebind would leave a duplicate behind");
            }
        }

        private static int Occurrences(string source, string needle)
        {
            var count = 0;
            int at = source.IndexOf(needle, System.StringComparison.Ordinal);

            while (at >= 0)
            {
                count++;
                at = source.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal);
            }

            return count;
        }
    }
}
