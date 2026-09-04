using ChibiFantasy.Contracts;

namespace ChibiFantasy.Client.UI
{
    /// <summary>The screens a player passes through before the world.</summary>
    /// <remarks>
    /// One value per production scene. Named for what the player is doing rather than for
    /// the session state, because two states can legitimately share a screen -- choosing a
    /// character and having chosen one are both the character screen until the world loads.
    /// </remarks>
    public enum ClientScreen
    {
        Login = 0,
        ServerSelect = 1,
        ChannelSelect = 2,
        CharacterSelect = 3,
        World = 4
    }

    /// <summary>
    /// Decides which screen a session state belongs on.
    /// </summary>
    /// <remarks>
    /// <b>It holds no state, and that is the whole point.</b> Where the player is, is
    /// derived from the session the domain already owns. A coordinator with its own current
    /// screen would be a second state machine, and the two would disagree the first time a
    /// session expired while a menu was open -- the player would be looking at a character
    /// list they were no longer entitled to.
    ///
    /// <b>Backwards is a real answer.</b> An expired or revoked session maps to the login
    /// screen from anywhere, including from inside the world. That is not a special case
    /// bolted on; it falls out of asking the session rather than remembering a path.
    ///
    /// Pure and engine-free: no scene loading, no <c>MonoBehaviour</c>, no coroutine. What
    /// to show is a decision; showing it is somebody else's job, and separating them is what
    /// lets every rule below be an ordinary test.
    /// </remarks>
    public static class ClientFlowCoordinator
    {
        /// <summary>
        /// The screen a session belongs on.
        /// </summary>
        /// <remarks>
        /// Read forward: no session at all is the login screen; signed in but nothing chosen
        /// is the server list; and so on until the world. A session that has ended sends the
        /// player back to the start wherever they were.
        /// </remarks>
        public static ClientScreen ScreenFor(SessionState state)
        {
            switch (state)
            {
                case SessionState.Authenticated:
                    return ClientScreen.ServerSelect;

                case SessionState.ServerSelected:
                    return ClientScreen.ChannelSelect;

                case SessionState.ChannelSelected:
                case SessionState.CharacterSelected:
                    return ClientScreen.CharacterSelect;

                case SessionState.EnteringWorld:
                case SessionState.Active:
                    return ClientScreen.World;

                // Unauthenticated, Expired, Revoked and anything added later. A state this
                // does not recognise is not a reason to leave a player somewhere they may
                // not belong, so the answer is the screen that requires nothing.
                default:
                    return ClientScreen.Login;
            }
        }

        /// <summary>The scene that screen lives in.</summary>
        /// <remarks>Paths rather than build indices: an index is a number that silently
        /// means something different when somebody reorders the build list, and a wrong
        /// scene is a worse failure than a missing one.</remarks>
        public static string SceneFor(ClientScreen screen)
        {
            switch (screen)
            {
                case ClientScreen.ServerSelect: return ClientScenes.ServerSelect;
                case ClientScreen.ChannelSelect: return ClientScenes.ChannelSelect;
                case ClientScreen.CharacterSelect: return ClientScenes.CharacterSelect;
                case ClientScreen.World: return ClientScenes.World;
                default: return ClientScenes.Login;
            }
        }

        /// <summary>The scene a session belongs in.</summary>
        public static string SceneFor(SessionState state)
        {
            return SceneFor(ScreenFor(state));
        }
    }

    /// <summary>The production scenes, by name.</summary>
    /// <remarks>
    /// One place, so a rename is one edit and a test can assert every one of them is
    /// actually in the build. Names rather than paths at the call site, because
    /// <c>SceneManager</c> takes either and a name survives a folder move.
    /// </remarks>
    public static class ClientScenes
    {
        public const string Login = "Login";
        public const string ServerSelect = "ServerSelect";
        public const string ChannelSelect = "ChannelSelect";
        public const string CharacterSelect = "CharacterSelect";
        public const string World = "GameWorld";

        /// <summary>Where they live, for a build-settings check.</summary>
        public const string Folder = "Assets/_Game/Scenes/Client/";

        /// <summary>Every production scene, in the order a player meets them.</summary>
        public static string[] All => new[]
        {
            Login, ServerSelect, ChannelSelect, CharacterSelect, World,
        };

        /// <summary>The asset path of a scene by name.</summary>
        public static string PathOf(string scene)
        {
            return Folder + scene + ".unity";
        }
    }
}
