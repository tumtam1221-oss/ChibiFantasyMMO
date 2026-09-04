using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ChibiFantasy.Editor
{
    /// <summary>
    /// Builds the two things this project ships: a client, and a dedicated server.
    /// </summary>
    /// <remarks>
    /// <b>They are different programs and they start in different places.</b> A player's
    /// build opens <see cref="Login"/> and walks a person through logging in; a server
    /// opens the world and listens. Unity has exactly one "scene 0" per build, so the only
    /// honest way to have both is to build them from different scene lists -- which is why
    /// this exists at all.
    ///
    /// <b>The scene list is passed in, never left behind.</b> Every build states its own
    /// scenes in <see cref="BuildPlayerOptions.scenes"/>, and nothing here writes
    /// <see cref="EditorBuildSettings"/>. A build utility that reordered the project's
    /// shared list would leave whoever built last deciding what the next person's Play
    /// button opens, and would make "which scene does the server start in" a question about
    /// build order rather than about this file.
    ///
    /// <b>The server list is one scene, deliberately.</b> A dedicated server has no login,
    /// no character select and no map scenes to show anybody; including them would ship a
    /// client's worth of scenes to a machine that will never draw a frame. If the server
    /// ever genuinely needs a second scene, it is added here and to the test that pins this.
    ///
    /// <b>No argument is required to boot correctly.</b> Entry is decided at build time by
    /// which scene is first, so a dedicated server started with no arguments at all opens
    /// the world. That leaves the command line free for the things a deployment will
    /// actually want to vary -- port, server id, environment -- none of which are
    /// implemented here, and none of which entry now depends on.
    ///
    /// <b>Callable without the editor UI.</b> Each entry point is a static method so CI can
    /// reach it with <c>-executeMethod</c>; the menu items are the same code.
    /// </remarks>
    public static class GameBuilder
    {
        /// <summary>The scene a player's build opens.</summary>
        public const string ClientEntryScene = "Assets/_Game/Scenes/Client/Login.unity";

        /// <summary>The scene a dedicated server opens.</summary>
        public const string ServerEntryScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string ClientFolder = "Assets/_Game/Scenes/Client/";

        /// <summary>
        /// Every scene a player's build needs, in the order they are reached.
        /// </summary>
        /// <remarks>Login first, because the first entry is where the process starts. The
        /// rest are listed because <see cref="UnityEngine.SceneManagement.SceneManager"/>
        /// can only load a scene that was built in.</remarks>
        public static string[] ClientScenes => new[]
        {
            ClientEntryScene,
            ClientFolder + "ServerSelect.unity",
            ClientFolder + "ChannelSelect.unity",
            ClientFolder + "CharacterSelect.unity",
            ClientFolder + "GameWorld.unity",
        };

        /// <summary>Every scene a dedicated server needs. Just the world.</summary>
        public static string[] ServerScenes => new[] { ServerEntryScene };

        // ---- menu ----------------------------------------------------------------------------

        [MenuItem("ChibiFantasy/Build/Windows Client")]
        public static void BuildWindowsClient()
        {
            Report(BuildClient(BuildTarget.StandaloneWindows64,
                "Builds/WindowsClient/ChibiFantasy.exe"));
        }

        [MenuItem("ChibiFantasy/Build/Windows Dedicated Server")]
        public static void BuildWindowsServer()
        {
            Report(BuildDedicatedServer(BuildTarget.StandaloneWindows64,
                "Builds/WindowsServer/ChibiFantasyServer.exe"));
        }

        [MenuItem("ChibiFantasy/Build/Linux Dedicated Server")]
        public static void BuildLinuxServer()
        {
            Report(BuildDedicatedServer(BuildTarget.StandaloneLinux64,
                "Builds/LinuxServer/ChibiFantasyServer.x86_64"));
        }

        // ---- the builds themselves ----------------------------------------------------------------

        /// <summary>
        /// Exactly how a player's client is built. Starting at the login screen.
        /// </summary>
        /// <remarks>The options are returned rather than only used, so that what a build
        /// would do can be asserted without running one -- a build takes minutes, and
        /// "which scene is first" is the kind of thing that should fail in seconds.</remarks>
        public static BuildPlayerOptions ClientOptions(BuildTarget target, string outputPath)
        {
            return new BuildPlayerOptions
            {
                scenes = ClientScenes,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                subtarget = (int)StandaloneBuildSubtarget.Player,
                locationPathName = outputPath,
                options = BuildOptions.None,
            };
        }

        /// <summary>Builds a player's client, starting at the login screen.</summary>
        public static BuildReport BuildClient(BuildTarget target, string outputPath)
        {
            return Build(ClientOptions(target, outputPath));
        }

        /// <summary>
        /// Builds a dedicated server, starting in the world.
        /// </summary>
        /// <remarks>The subtarget is what makes it a server rather than a headless client:
        /// it is what defines <c>UNITY_SERVER</c> and strips the graphics the machine will
        /// not use. Deciding server-ness from <c>Application.isBatchMode</c> instead would
        /// be wrong, because a client can be run in batch mode too.</remarks>
        public static BuildPlayerOptions ServerOptions(BuildTarget target, string outputPath)
        {
            return new BuildPlayerOptions
            {
                scenes = ServerScenes,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                subtarget = (int)StandaloneBuildSubtarget.Server,
                locationPathName = outputPath,
                options = BuildOptions.None,
            };
        }

        public static BuildReport BuildDedicatedServer(BuildTarget target, string outputPath)
        {
            return Build(ServerOptions(target, outputPath));
        }

        private static BuildReport Build(BuildPlayerOptions options)
        {
            string directory = Path.GetDirectoryName(options.locationPathName);

            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            return BuildPipeline.BuildPlayer(options);
        }

        /// <summary>Says what happened, and fails a batch build rather than exiting zero.</summary>
        private static void Report(BuildReport report)
        {
            string summary = report.summary.result + " " + report.summary.platform
                + " -> " + report.summary.outputPath
                + " (" + report.summary.totalSize / (1024 * 1024) + " MB, "
                + report.summary.totalErrors + " errors)";

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[build] " + summary);

                return;
            }

            Debug.LogError("[build] " + summary);

            // A build machine that reported success for a failed build would publish it.
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
