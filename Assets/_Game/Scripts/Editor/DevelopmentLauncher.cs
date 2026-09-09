using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ChibiFantasy.Editor
{
    /// <summary>
    /// Starts the three things a person needs running to play the game locally.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> Playing this project by hand means a database, an account API
    /// and a world server, each with its own command line. Retyping them is how a session
    /// ends up pointed at the wrong database or the wrong port, and how "it does not work"
    /// turns out to mean "the API was not running". These menu items run the same commands
    /// the documentation gives, from the paths the project already configures.
    ///
    /// <b>It starts processes; it configures nothing.</b> The API address, the world port and
    /// the database all come from files that already decide them -- <c>backend/.env</c>, the
    /// client bootstrap's serialized fields, the server scene. Nothing here is a second
    /// source of truth for any of them, and no credential is written down.
    ///
    /// <b>Editor only.</b> This assembly is not part of any build.
    /// </remarks>
    public static class DevelopmentLauncher
    {
        private const string LoginScene = "Assets/_Game/Scenes/Client/Login.unity";

        private const string DevelopmentServer =
            "Builds/WindowsServerDev/ChibiFantasyServer.exe";

        /// <summary>The database the development API serves. The integration fixture's.</summary>
        private const string DevelopmentDatabase = "chibifantasy_integration";

        private const string ApiHost = "127.0.0.1:8099";

        [MenuItem("ChibiFantasy/Play/1. Start PHP API", priority = 100)]
        public static void StartApi()
        {
            string root = ProjectRoot();

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // Quoted, because cmd's `set X=value && ...` keeps the space before `&&`
                // in the value; the API would then look for a database named
                // "chibifantasy_integration " and answer 503 to everything.
                Arguments = "/k set \"DB_DATABASE=" + DevelopmentDatabase
                    + "\" && php -S " + ApiHost + " -t public",
                WorkingDirectory = Path.Combine(root, "backend"),
                UseShellExecute = true,
            };

            Process.Start(start);

            Debug.Log("[dev] account API starting on http://" + ApiHost
                + " against " + DevelopmentDatabase
                + ". It runs in its own window; close that window to stop it.");
        }

        /// <summary>
        /// Seeds the development account and characters the client logs in with.
        /// </summary>
        /// <remarks>The existing integration fixture, run as itself. It writes its
        /// credential to <c>backend/storage/integration-fixture.json</c>, which is
        /// gitignored -- that file is where a developer reads their development sign-in
        /// from, and nothing here prints it into the console.</remarks>
        [MenuItem("ChibiFantasy/Play/0. Seed development account", priority = 90)]
        public static void SeedDevelopmentAccount()
        {
            string root = ProjectRoot();

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c php bin/integration-fixture.php && pause",
                WorkingDirectory = Path.Combine(root, "backend"),
                UseShellExecute = true,
            };

            Process.Start(start);

            Debug.Log("[dev] seeding the development account. Its sign-in credential is "
                + "written to backend/storage/integration-fixture.json, which is gitignored "
                + "and is the only place it exists.");
        }

        [MenuItem("ChibiFantasy/Play/2. Start Development Dedicated Server", priority = 110)]
        public static void StartDevelopmentServer()
        {
            string root = ProjectRoot();
            string exe = Path.Combine(root, DevelopmentServer);

            if (!File.Exists(exe))
            {
                Debug.LogError("[dev] no development server at " + DevelopmentServer
                    + ". Build one first: ChibiFantasy > Build > Windows Development "
                    + "Dedicated Server.");

                return;
            }

            var start = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = true,
            };

            Process.Start(start);

            Debug.Log("[dev] world server starting on port 7770. It runs in its own window; "
                + "close that window to stop it.");
        }

        [MenuItem("ChibiFantasy/Play/3. Play Development Client", priority = 120)]
        public static void PlayDevelopmentClient()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[dev] already playing.");

                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(LoginScene, OpenSceneMode.Single);

            EditorApplication.isPlaying = true;
        }

        /// <summary>Where the repository is, from where Unity says the project is.</summary>
        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
