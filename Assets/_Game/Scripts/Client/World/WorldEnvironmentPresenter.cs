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
        private System.Func<bool> _hourIsKnown;
        private float _waitedForHour;

        /// <summary>How long the environment will wait to be told the hour before opening anyway.</summary>
        private const float HourPatienceSeconds = 3f;
        private GameObject _fallbackGround;
        private GameObject _fallbackLight;

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

        /// <summary>The stand-in sun this shows and hides, or null when there is none.</summary>
        public GameObject FallbackLight => _fallbackLight;

        /// <summary>Whether the world has told this client what hour it is.</summary>
        /// <remarks>True when nobody supplied the question, so a test or a scene with no
        /// connection behaves as it always did.</remarks>
        public bool HourIsKnown => _hourIsKnown == null || _hourIsKnown();

        /// <summary>
        /// Supplies the "do we know what time it is" question this waits on.
        /// </summary>
        /// <remarks>
        /// <b>Why the environment waits for the clock.</b> The environment scene is authored
        /// in daylight -- something has to be saved in it -- and its sky only becomes the
        /// world's sky once the hour arrives. Bringing it up before then shows the player a
        /// bright afternoon and then corrects it, which is the whole of the "why is it bright
        /// and then suddenly night" problem, and no amount of arriving-faster removes it:
        /// whatever the gap is, the wrong picture was already on screen.
        ///
        /// <b>The same rule this class already lives by.</b> It refuses to present a map
        /// until the server says which map. This is the second half of the same sentence:
        /// and until the world says what hour. Readiness, not delays -- so there is no timer
        /// here and nothing to tune.
        /// </remarks>
        public void UseHourKnown(System.Func<bool> hourIsKnown)
        {
            _hourIsKnown = hourIsKnown;
        }

        /// <summary>
        /// Hands over GameWorld's own sun so it can step aside for a real environment.
        /// </summary>
        /// <remarks>
        /// <b>The floor was given this treatment and the light was not.</b> GameWorld carries
        /// a directional light so that a world with no environment yet is not pitch black.
        /// It was left switched on afterwards, which meant every environment was lit by its
        /// own sun and by a second one at full strength that nothing could reach: the day and
        /// night cycle would dim its sun to a quarter and the scene would barely change,
        /// and arriving in the world looked like bright daylight until the environment
        /// finished loading and then abruptly changed.
        ///
        /// <b>Same rule as the floor, deliberately.</b> Both are stand-ins for something the
        /// environment brings, both are wanted only while it is absent, and both come back
        /// when it goes. One rule, one place.
        /// </remarks>
        public void UseFallbackLight(GameObject fallbackLight)
        {
            _fallbackLight = fallbackLight;
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

            // Waited for, but never indefinitely. The hour arrives with the admission, so
            // this is a frame or two in practice -- and if it somehow does not arrive, a
            // world with the wrong sky for a moment beats a player standing on an empty
            // plain forever, which is what a hard gate here actually produced.
            if (!HourIsKnown && _waitedForHour < HourPatienceSeconds)
            {
                _waitedForHour += Time.deltaTime;

                return;
            }

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
            bool show = ShowFallbackGround(_loader != null ? _loader.LoadedMap : DefinitionId.None);

            if (_fallbackGround != null && _fallbackGround.activeSelf != show)
            {
                _fallbackGround.SetActive(show);
            }

            if (_fallbackLight != null && _fallbackLight.activeSelf != show)
            {
                _fallbackLight.SetActive(show);
            }
        }

        private void OnDestroy()
        {
            // Leaving the world takes the environment with it; neither stand-in may stay
            // hidden, or the next world with no environment is a black screen with no floor.
            if (_fallbackGround != null && !_fallbackGround.activeSelf) _fallbackGround.SetActive(true);

            if (_fallbackLight != null && !_fallbackLight.activeSelf) _fallbackLight.SetActive(true);
        }
    }
}
