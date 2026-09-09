using System.Collections;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Client.World
{
    /// <summary>Why a scene could not be brought up.</summary>
    /// <remarks>Presentation failures only. Whether the journey was <em>allowed</em> was
    /// settled by <c>TravelService</c> long before anything here ran.</remarks>
    public enum MapLoadFailure
    {
        None = 0,

        /// <summary>The travel result was a refusal, so there is nothing to load.</summary>
        TravelRejected = 1,

        /// <summary>No registry was supplied.</summary>
        MissingContext = 2,

        /// <summary>The destination map could not be resolved.</summary>
        UnknownMap = 3,

        /// <summary>The map references no scene, so there is nothing to bring up.</summary>
        NoScene = 4,

        /// <summary>Unity could not load the scene the map names.</summary>
        SceneLoadFailed = 5,

        /// <summary>The destination spawn could not be resolved.</summary>
        UnknownSpawn = 6,

        /// <summary>Another load is already running.</summary>
        AlreadyLoading = 7
    }

    /// <summary>
    /// Brings up the scene a completed journey landed in.
    /// </summary>
    /// <remarks>
    /// <b>It obeys; it never decides.</b> Whether a portal was usable, whether the player
    /// was close enough and whether the destination was allowed were all settled by
    /// <c>TravelService</c>. This receives an accepted <see cref="TravelResult"/> and does
    /// the two things gameplay cannot: resolve <see cref="MapDefinition.Scene"/> to a Unity
    /// scene, and put the player at the authored spawn.
    ///
    /// <b>The scene name lives here and nowhere else.</b> Gameplay knows a
    /// <see cref="DefinitionId"/> and an <see cref="AssetRef"/>; the string that names a
    /// file is resolved at this boundary, which is what keeps a filename out of every rule
    /// above it.
    ///
    /// <b>It never guesses.</b> A missing scene, a missing spawn or a failed load is an
    /// explicit failure. There is no fallback map and no fallback position -- silently
    /// loading the wrong place, or dropping the player at the origin, is worse than
    /// stopping and saying so.
    ///
    /// <b>One load at a time.</b> A second request while one is running is refused rather
    /// than queued, so two portals touched in one frame cannot race.
    /// </remarks>
    public sealed class MapSceneLoader : MonoBehaviour
    {
        [Tooltip("Where a player is placed when the destination spawn has no facing.")]
        [SerializeField] private Transform playerRoot;

        private IDefinitionRegistry<MapDefinition> _maps;
        private IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;

        /// <summary>Whether a load is in flight.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>The map currently brought up. Invalid before the first load.</summary>
        public DefinitionId LoadedMap { get; private set; }

        /// <summary>
        /// The environment scene currently loaded additively on top of GameWorld, or null.
        /// </summary>
        /// <remarks>Exactly one map environment is ever additive at a time: the next load
        /// unloads this before bringing up its own, so no old ground, lighting or props are
        /// left behind. GameWorld itself is never in here and is never unloaded.</remarks>
        private string _activeEnvScene;

        /// <summary>The environment scene loaded additively, for tests and diagnostics.</summary>
        public string ActiveEnvironmentScene => _activeEnvScene;

        /// <summary>Why the last attempt failed, or <see cref="MapLoadFailure.None"/>.</summary>
        public MapLoadFailure LastFailure { get; private set; }

        /// <summary>Raised once a destination is up and the player has been placed.</summary>
        public event System.Action<DefinitionId> Arrived;

        /// <summary>Raised when a load could not be completed.</summary>
        public event System.Action<MapLoadFailure> Failed;

        /// <summary>Points the loader at the content it resolves through.</summary>
        public void Bind(IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints, Transform player = null)
        {
            _maps = maps;
            _spawnPoints = spawnPoints;
            if (player != null) playerRoot = player;
        }

        /// <summary>
        /// Checks a journey can be presented, without loading anything.
        /// </summary>
        /// <remarks>Exposed so the decision and the presentation can be tested apart: this
        /// is every check the loader makes, and it touches no scene.</remarks>
        public MapLoadFailure Validate(in TravelResult travel)
        {
            if (!travel.IsAccepted) return MapLoadFailure.TravelRejected;
            if (_maps == null || _spawnPoints == null) return MapLoadFailure.MissingContext;
            if (IsLoading) return MapLoadFailure.AlreadyLoading;

            MapDefinition map;
            if (!_maps.TryGet(travel.DestinationMap, out map) || map == null)
                return MapLoadFailure.UnknownMap;

            if (!map.Scene.IsValid) return MapLoadFailure.NoScene;

            SpawnPointDefinition spawn;
            if (!_spawnPoints.TryGet(travel.DestinationSpawn, out spawn) || spawn == null)
                return MapLoadFailure.UnknownSpawn;

            return MapLoadFailure.None;
        }

        /// <summary>
        /// Resolves the scene a journey lands in.
        /// </summary>
        /// <remarks>The one place a <see cref="DefinitionId"/> becomes a scene name. Null
        /// when it cannot be resolved, which the caller must treat as a failure rather than
        /// substituting anything.</remarks>
        public string ResolveScene(DefinitionId map)
        {
            if (_maps == null || !map.IsValid) return null;

            MapDefinition definition;
            if (!_maps.TryGet(map, out definition) || definition == null) return null;

            return definition.Scene.IsValid ? definition.Scene.Address : null;
        }

        /// <summary>
        /// Loads the destination and places the player.
        /// </summary>
        /// <remarks>A coroutine because scene loading is asynchronous; the decision it acts
        /// on was made synchronously and long since.</remarks>
        public IEnumerator LoadAsync(TravelResult travel)
        {
            LastFailure = Validate(travel);

            if (LastFailure != MapLoadFailure.None)
            {
                Report(LastFailure);
                yield break;
            }

            string scene = ResolveScene(travel.DestinationMap);

            if (string.IsNullOrEmpty(scene))
            {
                LastFailure = MapLoadFailure.NoScene;
                Report(LastFailure);
                yield break;
            }

            IsLoading = true;

            // Additive, not Single: GameWorld carries the HUD, camera, network and this
            // loader, and must stay loaded. The old environment is unloaded first so that at
            // most one map environment -- its ground, lighting and props -- is ever live.
            if (!string.IsNullOrEmpty(_activeEnvScene) && _activeEnvScene != scene)
            {
                Scene previous = SceneManager.GetSceneByName(_activeEnvScene);

                if (previous.IsValid() && previous.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(previous);

                    while (unload != null && !unload.isDone) yield return null;
                }

                _activeEnvScene = null;
            }

            AsyncOperation operation = null;

            if (_activeEnvScene != scene)
            {
                try
                {
                    operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
                }
                catch (System.Exception)
                {
                    // A scene missing from the build settings throws rather than returning null.
                    operation = null;
                }

                if (operation == null)
                {
                    IsLoading = false;
                    LastFailure = MapLoadFailure.SceneLoadFailed;
                    Report(LastFailure);
                    yield break;
                }

                while (!operation.isDone) yield return null;

                _activeEnvScene = scene;

                // The environment owns the lighting and skybox for where the player stands,
                // so it becomes the active scene; GameWorld remains loaded underneath.
                Scene loaded = SceneManager.GetSceneByName(scene);

                if (loaded.IsValid() && loaded.isLoaded) SceneManager.SetActiveScene(loaded);
            }

            IsLoading = false;

            // Placement is the second half of arriving, and it uses the authored point the
            // travel result named. Nothing here invents a position.
            if (!PlacePlayer(travel.DestinationSpawn))
            {
                LastFailure = MapLoadFailure.UnknownSpawn;
                Report(LastFailure);
                yield break;
            }

            LoadedMap = travel.DestinationMap;

            var handler = Arrived;
            if (handler != null) handler(LoadedMap);
        }

        /// <summary>
        /// Brings up the environment for the map the player is already standing in.
        /// </summary>
        /// <remarks>
        /// The first half of arriving, for the arrival that has no journey: entering the
        /// world. The server has already decided where the character is, so this loads the
        /// environment additively and does not move anyone -- placement is the server's, and
        /// re-homing the player to a spawn here would fight it. Travel between maps goes
        /// through <see cref="LoadAsync"/>, which does place, because the destination is new.
        /// </remarks>
        public IEnumerator EnterAsync(DefinitionId map)
        {
            if (_maps == null) { LastFailure = MapLoadFailure.MissingContext; Report(LastFailure); yield break; }
            if (IsLoading) { LastFailure = MapLoadFailure.AlreadyLoading; Report(LastFailure); yield break; }

            string scene = ResolveScene(map);

            if (string.IsNullOrEmpty(scene))
            {
                LastFailure = MapLoadFailure.NoScene;
                Report(LastFailure);
                yield break;
            }

            if (_activeEnvScene == scene) { LoadedMap = map; yield break; }

            IsLoading = true;

            if (!string.IsNullOrEmpty(_activeEnvScene))
            {
                Scene previous = SceneManager.GetSceneByName(_activeEnvScene);

                if (previous.IsValid() && previous.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(previous);

                    while (unload != null && !unload.isDone) yield return null;
                }

                _activeEnvScene = null;
            }

            AsyncOperation operation = null;

            try { operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive); }
            catch (System.Exception) { operation = null; }

            if (operation == null)
            {
                IsLoading = false;
                LastFailure = MapLoadFailure.SceneLoadFailed;
                Report(LastFailure);
                yield break;
            }

            while (!operation.isDone) yield return null;

            _activeEnvScene = scene;

            Scene loaded = SceneManager.GetSceneByName(scene);
            if (loaded.IsValid() && loaded.isLoaded) SceneManager.SetActiveScene(loaded);

            IsLoading = false;
            LoadedMap = map;
            LastFailure = MapLoadFailure.None;

            var handler = Arrived;
            if (handler != null) handler(LoadedMap);
        }

        /// <summary>
        /// Puts the player at an authored spawn.
        /// </summary>
        /// <remarks>False when the spawn cannot be resolved, so the caller fails rather than
        /// leaving the player wherever the scene happened to put them.</remarks>
        public bool PlacePlayer(DefinitionId spawnPoint)
        {
            if (_spawnPoints == null) return false;

            SpawnPointDefinition spawn;
            if (!_spawnPoints.TryGet(spawnPoint, out spawn) || spawn == null) return false;

            if (playerRoot == null) return true;   // nothing to place, but the spawn resolved

            playerRoot.position = new Vector3(spawn.X, spawn.Y, spawn.Z);
            playerRoot.rotation = Quaternion.Euler(0f, spawn.FacingDegrees, 0f);
            return true;
        }

        private void Report(MapLoadFailure failure)
        {
            var handler = Failed;
            if (handler != null) handler(failure);
        }

        /// <summary>
        /// Takes the environment down when the loader goes.
        /// </summary>
        /// <remarks>The loader lives on the world connection, so leaving the world destroys
        /// it; without this the additive environment it brought up would be left loaded on
        /// top of GameWorld with nothing owning it, and the next entry would load a second
        /// one beside it. GameWorld is never the scene unloaded here -- it is not the active
        /// environment -- and the count guard refuses to unload the last scene, which Unity
        /// forbids and which only happens on the way out anyway.</remarks>
        private void OnDestroy()
        {
            if (string.IsNullOrEmpty(_activeEnvScene)) return;

            Scene env = SceneManager.GetSceneByName(_activeEnvScene);

            if (env.IsValid() && env.isLoaded && SceneManager.sceneCount > 1)
            {
                SceneManager.UnloadSceneAsync(env);
            }

            _activeEnvScene = null;
        }
    }
}
