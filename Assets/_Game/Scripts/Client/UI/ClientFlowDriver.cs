using ChibiFantasy.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Loads the scene the session says the player belongs in.
    /// </summary>
    /// <remarks>
    /// <b>It remembers nothing about where the player is.</b> The session already knows, and
    /// a driver with its own idea of the current screen would be a second state machine --
    /// the two would disagree the first time a session expired while a menu was open, and
    /// the player would be looking at a list they were no longer entitled to. So every
    /// decision is <see cref="ClientFlowCoordinator"/> asking the session, and the only state
    /// here is which scene is actually loaded, which is Unity's fact rather than the game's.
    ///
    /// <b>It loads; it does not authorise.</b> Nothing here can advance a session. A screen
    /// asks the controller, the controller asks the authority, the session changes, and this
    /// notices -- in that order, never skipped. There is no path from a button to a scene
    /// that does not go through the server first.
    /// </remarks>
    public sealed class ClientFlowDriver : MonoBehaviour
    {
        private SessionUiController _session;
        private string _loaded;

        /// <summary>The screen the session currently implies.</summary>
        public ClientScreen CurrentScreen { get; private set; } = ClientScreen.Login;

        /// <summary>The scene this driver last asked for.</summary>
        public string CurrentScene => _loaded;

        /// <summary>How many scene loads it has requested. For diagnostics and tests.</summary>
        public int LoadCount { get; private set; }

        /// <summary>
        /// Set false to decide the screen without touching Unity's scene manager.
        /// </summary>
        /// <remarks>What lets the mapping be tested for real -- a test can drive a whole
        /// session flow and assert every screen it passed through, without five scenes
        /// loading and unloading around it.</remarks>
        public bool LoadScenes { get; set; } = true;

        /// <summary>Raised after the driver settles on a screen.</summary>
        public event System.Action<ClientScreen> ScreenChanged;

        public void Bind(SessionUiController session)
        {
            _session = session;

            Evaluate();
        }

        /// <summary>
        /// Asks the session where the player belongs and goes there if it has changed.
        /// </summary>
        /// <remarks>Public so a screen can call it the moment it knows something changed,
        /// rather than everybody waiting for the next frame.</remarks>
        public void Evaluate()
        {
            SessionState state = _session == null
                ? SessionState.Unauthenticated
                : _session.Flow.State;

            ClientScreen screen = ClientFlowCoordinator.ScreenFor(state);

            if (screen == CurrentScreen && _loaded != null) return;

            CurrentScreen = screen;

            string scene = ClientFlowCoordinator.SceneFor(screen);

            if (scene == _loaded) return;

            _loaded = scene;
            LoadCount++;

            if (LoadScenes) SceneManager.LoadScene(scene);

            ScreenChanged?.Invoke(screen);
        }

        private void Update()
        {
            // The session changes when the server answers, which can be on any frame. This
            // is a comparison of one enum, not a rebuild of anything.
            if (_session != null && _session.RefreshIfChanged()) Evaluate();
        }
    }
}
