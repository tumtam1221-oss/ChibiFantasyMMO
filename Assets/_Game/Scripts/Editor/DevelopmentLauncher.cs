using System;
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

            string key = WorldServerKey();

            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[dev] no " + WorldKeyVariable + " available, so this world "
                    + "will not remember what day it is across a restart. Generate one: "
                    + "ChibiFantasy > World > Generate world server key.");
            }

            var start = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe),

                // Not ShellExecute, because the key has to go into the child's environment
                // and ShellExecute cannot carry one. A console-subsystem build still gets
                // its own window this way, so closing that window still stops the server.
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            // Passed through the environment rather than on the command line: a command
            // line is readable by anything that can list processes, and this is a
            // credential. It is forwarded here, never invented -- backend/.env decides it.
            if (!string.IsNullOrEmpty(key))
            {
                start.EnvironmentVariables[WorldKeyVariable] = key;
            }

            Process.Start(start);

            Debug.Log("[dev] world server starting on port 7770"
                + (string.IsNullOrEmpty(key) ? " WITHOUT a world key" : " with the world key")
                + ". It runs in its own window; close that window to stop it.");
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
        /// <summary>The environment variable the world server reads its key from.</summary>
        private const string WorldKeyVariable = "CHIBI_WORLD_SERVER_KEY";

        /// <summary>What the API calls the same value.</summary>
        private const string BackendKeyVariable = "WORLD_SERVER_KEY";

        /// <summary>
        /// Reads the deployment key out of backend/.env.
        /// </summary>
        /// <remarks>
        /// <b>Read, not decided.</b> This launcher is not a second source of truth for
        /// anything, and a key invented here would be a key the API does not have -- which
        /// fails silently, because the only symptom is a world that quietly stops
        /// remembering the date.
        ///
        /// <b>Never logged.</b> The value is returned and handed to a child process, and no
        /// line in this file prints it.
        /// </remarks>
        private static string WorldServerKey()
        {
            // The process environment wins, exactly as the backend's own Env does, so an
            // operator can override without editing a file.
            string fromEnvironment = Environment.GetEnvironmentVariable(WorldKeyVariable);

            if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

            string path = Path.Combine(ProjectRoot(), "backend", ".env");

            if (!File.Exists(path)) return null;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == '#') continue;

                int split = line.IndexOf('=');

                if (split <= 0) continue;

                if (line.Substring(0, split).Trim() != BackendKeyVariable) continue;

                return line.Substring(split + 1).Trim().Trim('"');
            }

            return null;
        }

        /// <summary>
        /// Creates the deployment key the world server proves itself with.
        /// </summary>
        /// <remarks>Runs the backend's own command rather than generating one here, so there
        /// is exactly one thing in this project that decides what a key is and where it is
        /// written. Refuses to replace an existing key -- rotating one means restarting the
        /// API and every world server together.</remarks>
        [MenuItem("ChibiFantasy/World/Generate world server key", priority = 200)]
        public static void GenerateWorldServerKey()
        {
            string root = ProjectRoot();

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c php bin/world-key.php --generate && pause",
                WorkingDirectory = Path.Combine(root, "backend"),
                UseShellExecute = true,
            };

            Process.Start(start);

            Debug.Log("[dev] generating a world server key in its own window. It is written "
                + "to backend/.env, which is gitignored, and is never printed here. Restart "
                + "the API afterwards so it picks the new key up.");
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
