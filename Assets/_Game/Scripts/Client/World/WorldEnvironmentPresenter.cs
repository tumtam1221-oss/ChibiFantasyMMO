using System.Collections;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Keeps the additive environment scene matching the map the server has the player on.
    /// </summary>
    /// <remarks>
    /// <b>It follows authority; it never decides.</b> The map it presents is whatever the
    /// server replicated onto the owned character -- <c>CharacterNetworkEntity.Map</c>, read
    /// through a delegate so this component never depends on the network assembly. Nothing
    /// here chooses a destination, so there is no way for the client to present a map the
    /// server did not put it on. Travel authority lives on the server; this is its shadow.
    ///
    /// <b>Readiness, not delays.</b> Entering the world is asynchronous: the scene loads, the
    /// network connects, the owned object spawns and its map replicates, each on its own
    /// clock. This waits on the real state -- an owned map that is <see cref="DefinitionId"/>
    /// valid -- rather than on a timer, and does nothing until then. That is why the read is
    /// a delegate returning <see cref="DefinitionId.None"/> before the owner exists.
    ///
    /// <b>Exactly one environment, brought up once.</b> <see cref="Decide"/> refuses to load
    /// a map already loaded, already requested, or while a load is in flight, so a map that
    /// replicates the same value every frame -- which a <c>SyncVar</c> does -- is loaded once.
    /// A different authoritative map (a completed server travel, or a reconnect onto another
    /// map) swaps the environment through <see cref="MapSceneLoader.EnterAsync"/>, which
    /// unloads the old one first. GameWorld itself is never unloaded.
    ///
    /// <b>It never moves the player.</b> Placement is the server's; the owned character's
    /// position arrives as replicated state. So this calls <see cref="MapSceneLoader.EnterAsync"/>,
    /// which loads the environment and places nobody, and never <c>LoadAsync</c>, which would
    /// re-home the player to a scene marker and fight the server for their position.
    ///
    /// <b>The fallback ground yields to a real environment.</b> GameWorld carries a flat
    /// "World Ground" so a click has somewhere to land when no map scene exists. It is a
    /// 700 m plane just under y=0, so with an environment loaded it covers every carved
    /// bay and river, and shows as a flat grey-green sea. <see cref="ShowFallbackGround"/>
    /// keeps it up only while no environment is loaded; the moment one is, it is hidden --
    /// renderer and collider together, so a click on water lands on nothing rather than on
    /// the plane beneath it. When the environment goes away it comes back. Nothing here
    /// deletes it and no scene file changes.
    /// </remarks>
    public sealed class WorldEnvironmentPresenter : MonoBehaviour
    {
        private MapSceneLoader _loader;
        private System.Func<DefinitionId> _authoritativeMap;
        private GameObject _fallbackGround;

        private DefinitionId _requested;
        private Coroutine _running;

        /// <summary>The single loader this drives. Null before composition.</summary>
        public MapSceneLoader Loader => _loader;

        /// <summary>The map last handed to the loader. For tests and diagnostics.</summary>
        public DefinitionId RequestedMap => _requested;

        /// <summary>The fallback floor this shows and hides, or null when there is none.</summary>
        public GameObject FallbackGround => _fallbackGround;

        /// <summary>
        /// Hands over the flat fallback floor so it can step aside for a real environment.
        /// </summary>
        /// <param name="fallbackGround">GameWorld's placeholder ground. Null is allowed and
        /// means there is nothing to hide.</param>
        public void UseFallbackGround(GameObject fallbackGround)
        {
            _fallbackGround = fallbackGround;
            ApplyFallbackGround();
        }

        /// <summary>
        /// Whether the fallback floor should be visible and clickable right now.
        /// </summary>
        /// <remarks>Pure: only while no environment is loaded. A load in flight still shows
        /// it, so the world never has a moment with no floor at all; the frame the
        /// environment is up, it goes.</remarks>
        public static bool ShowFallbackGround(DefinitionId loadedMap)
        {
            return !loadedMap.IsValid;
        }

        /// <summary>
        /// Points the presenter at its loader and the authority it follows.
        /// </summary>
        /// <param name="loader">The one environment loader in the world.</param>
        /// <param name="maps">Content the loader resolves a map to a scene through.</param>
        /// <param name="spawnPoints">Content the loader resolves spawns through.</param>
        /// <param name="authoritativeMap">Reads the map the server has the owner on, or
        /// <see cref="DefinitionId.None"/> while there is no owner yet.</param>
        public void Compose(MapSceneLoader loader,
            IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints,
            System.Func<DefinitionId> authoritativeMap)
        {
            _loader = loader;
            _authoritativeMap = authoritativeMap;

            if (_loader != null) _loader.Bind(maps, spawnPoints);
        }

        /// <summary>
        /// Whether the authoritative map should now be brought up.
        /// </summary>
        /// <remarks>Pure, so the readiness and idempotency rules can be tested without a
        /// scene. Every false is a real reason to wait: not ready, in flight, already there,
        /// or already asked.</remarks>
        public static bool Decide(DefinitionId authoritativeMap, DefinitionId loadedMap,
            DefinitionId requestedMap, bool isLoading, bool coroutineRunning)
        {
            // The owner has not spawned, or its map has not replicated yet.
            if (!authoritativeMap.IsValid) return false;

            // A load is running; a second would race it.
            if (isLoading || coroutineRunning) return false;

            // Already standing in it. A SyncVar that reports the same map every frame lands
            // here after the first load, which is what keeps the load from repeating.
            if (authoritativeMap == loadedMap) return false;

            // Asked for and not yet reflected in LoadedMap (a load that resolved to the same
            // scene, or one still settling). Not asked again.
            if (authoritativeMap == requestedMap) return false;

            return true;
        }

        private void Update()
        {
            if (_loader == null || _authoritativeMap == null) return;

            ApplyFallbackGround();

            // A finished coroutine leaves its handle behind; clear it before deciding so a
            // new authoritative map is not blocked by the last load's ghost.
            if (_running != null && !_loader.IsLoading) _running = null;

            DefinitionId map = _authoritativeMap();

            if (!Decide(map, _loader.LoadedMap, _requested, _loader.IsLoading, _running != null))
            {
                return;
            }

            _requested = map;
            _running = StartCoroutine(Enter(map));
        }

        private IEnumerator Enter(DefinitionId map)
        {
            yield return _loader.EnterAsync(map);

            // A failed load did not reach LoadedMap; clearing the request lets the next frame
            // try again rather than latching on a map that never came up.
            if (_loader.LoadedMap != map) _requested = DefinitionId.None;

            _running = null;
        }

        private void ApplyFallbackGround()
        {
            if (_fallbackGround == null) return;

            bool show = ShowFallbackGround(_loader != null ? _loader.LoadedMap : DefinitionId.None);

            if (_fallbackGround.activeSelf != show) _fallbackGround.SetActive(show);
        }

        private void OnDestroy()
        {
            // Leaving the world takes the environment with it; the floor must not stay hidden.
            if (_fallbackGround != null && !_fallbackGround.activeSelf) _fallbackGround.SetActive(true);
        }
    }
}
