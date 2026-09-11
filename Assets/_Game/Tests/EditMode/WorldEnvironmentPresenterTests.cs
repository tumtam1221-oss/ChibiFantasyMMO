using UnityEngine;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The rules that keep the additive environment following the server's map without
    /// racing itself, reloading itself, or acting before the owner exists.
    /// </summary>
    /// <remarks>Every case is a call to the pure <see cref="WorldEnvironmentPresenter.Decide"/>
    /// so the readiness and idempotency contract is pinned without loading a Unity scene --
    /// the scene load itself is exercised by the manual build gate.</remarks>
    public sealed class WorldEnvironmentPresenterTests
    {
        private static readonly DefinitionId Town = new DefinitionId("map.harbor_town");
        private static readonly DefinitionId Outskirts = new DefinitionId("map.harbor_outskirts");

        [Test]
        public void The_first_authoritative_map_is_loaded()
        {
            // Owner just spawned: a valid map, nothing loaded, nothing in flight.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Town, loadedMap: DefinitionId.None,
                requestedMap: DefinitionId.None, isLoading: false, coroutineRunning: false),
                Is.True);
        }

        [Test]
        public void Nothing_loads_before_the_owner_has_a_map()
        {
            // The owner has not spawned, or its map has not replicated yet.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: DefinitionId.None, loadedMap: DefinitionId.None,
                requestedMap: DefinitionId.None, isLoading: false, coroutineRunning: false),
                Is.False, "an invalid map is not ready, not a destination");
        }

        [Test]
        public void The_same_map_is_not_reloaded()
        {
            // A SyncVar reports the same map every frame; once it is loaded, it stays put.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Town, loadedMap: Town,
                requestedMap: Town, isLoading: false, coroutineRunning: false),
                Is.False);
        }

        [Test]
        public void A_map_already_requested_is_not_requested_again()
        {
            // Asked for, not yet reflected in LoadedMap: idempotent against a repeated read.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Town, loadedMap: DefinitionId.None,
                requestedMap: Town, isLoading: false, coroutineRunning: false),
                Is.False);
        }

        [Test]
        public void No_load_starts_while_one_is_in_flight()
        {
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Outskirts, loadedMap: Town,
                requestedMap: Town, isLoading: true, coroutineRunning: false),
                Is.False, "the loader says it is busy");

            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Outskirts, loadedMap: Town,
                requestedMap: Town, isLoading: false, coroutineRunning: true),
                Is.False, "a coroutine is still running");
        }

        [Test]
        public void A_new_authoritative_map_swaps_the_environment()
        {
            // A completed server travel, or a reconnect onto another map: the destination
            // differs from what is loaded and from what was last asked, so it is brought up.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Outskirts, loadedMap: Town,
                requestedMap: Town, isLoading: false, coroutineRunning: false),
                Is.True);
        }

        [Test]
        public void A_reconnect_onto_the_same_map_does_not_duplicate_it()
        {
            // The owner object was replaced but lands on the same map it left: already
            // loaded, so nothing loads a second environment beside the first.
            Assert.That(WorldEnvironmentPresenter.Decide(
                authoritativeMap: Town, loadedMap: Town,
                requestedMap: Town, isLoading: false, coroutineRunning: false),
                Is.False);
        }

        // ---- the fallback floor steps aside for a real environment ------------------------

        [Test]
        public void The_fallback_ground_shows_only_while_no_environment_is_loaded()
        {
            Assert.That(WorldEnvironmentPresenter.ShowFallbackGround(DefinitionId.None), Is.True,
                "with no environment, the placeholder is the only thing a click can land on");
            Assert.That(WorldEnvironmentPresenter.ShowFallbackGround(Town), Is.False,
                "a loaded environment has its own ground; the 700 m plane would cover its water");
        }

        [Test]
        public void Handing_over_the_fallback_ground_before_any_load_leaves_it_visible()
        {
            var host = new UnityEngine.GameObject("presenter host");
            var ground = new UnityEngine.GameObject("fallback ground");

            try
            {
                var presenter = host.AddComponent<WorldEnvironmentPresenter>();

                presenter.UseFallbackGround(ground);

                Assert.That(presenter.FallbackGround, Is.SameAs(ground));
                Assert.That(ground.activeSelf, Is.True,
                    "nothing is loaded yet, so the floor must stay");

                presenter.UseFallbackGround(null);

                Assert.That(ground.activeSelf, Is.True, "letting go of it must not hide it");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void GameWorld_authors_the_fallback_ground_under_the_name_the_bootstrap_looks_for()
        {
            // The bootstrap finds the floor by name because it is plain scene content with no
            // script. This keeps the two from drifting apart without loading the scene.
            string scene = System.IO.File.ReadAllText("Assets/_Game/Scenes/Client/GameWorld.unity");

            Assert.That(scene, Does.Contain("m_Name: " + ChibiFantasy.Client.ClientApplicationBootstrap.FallbackGroundName + "\n")
                .Or.Contain("m_Name: " + ChibiFantasy.Client.ClientApplicationBootstrap.FallbackGroundName + "\r\n"),
                "GameWorld must still contain an object named '"
                + ChibiFantasy.Client.ClientApplicationBootstrap.FallbackGroundName + "'");
        }
    
        // ---- nothing is shown before the world says what hour it is ------------------------

        /// <summary>
        /// Builds a presenter with a loader and an authoritative map, ready to load.
        /// </summary>
        private static WorldEnvironmentPresenter Ready(out GameObject host,
            System.Func<bool> hourIsKnown)
        {
            host = new GameObject("presenter");

            var loader = host.AddComponent<MapSceneLoader>();
            var presenter = host.AddComponent<WorldEnvironmentPresenter>();

            presenter.Compose(loader, null, null, () => new DefinitionId("map.harbor_town"));
            presenter.UseHourKnown(hourIsKnown);

            return presenter;
        }

        [Test]
        public void With_no_question_supplied_the_hour_counts_as_known()
        {
            // A scene or a test with no connection behaves exactly as it always did.
            WorldEnvironmentPresenter presenter = Ready(out GameObject host, null);

            try
            {
                Assert.That(presenter.HourIsKnown, Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void The_environment_is_not_requested_before_the_hour_is_known()
        {
            // The bug this pins: the environment is authored in daylight, so bringing it up
            // before the world has said what hour it is shows a bright afternoon and then
            // corrects it. Arriving faster never fixed that -- the wrong picture was already
            // on screen. So it does not come up at all until the hour is in.
            var known = false;

            WorldEnvironmentPresenter presenter = Ready(out GameObject host, () => known);

            try
            {
                Assert.That(presenter.HourIsKnown, Is.False);
                Assert.That(presenter.RequestedMap.IsValid, Is.False,
                    "a map was requested while the hour was still unknown");

                known = true;

                Assert.That(presenter.HourIsKnown, Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
