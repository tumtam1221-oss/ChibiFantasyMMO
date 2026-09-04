using System.IO;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// What a live end-to-end run needs in order to sign in, read from where the
    /// backend left it.
    /// </summary>
    /// <remarks>
    /// <b>This exists so that a credential never has to be written down.</b> The
    /// integration test runs in Unity; the account it signs in as is created by
    /// <c>backend/bin/integration-fixture.php</c> in another process entirely. Something
    /// has to carry the password between them, and the two alternatives were both worse:
    /// a constant in this file, or a constant in the PHP file. Either would be a known
    /// credential living in the repository for good.
    ///
    /// Instead the backend invents one per run and drops it in <c>backend/storage/</c>,
    /// which <c>.gitignore</c> refuses. This reads it back. If the file is absent the
    /// answer is "not available" -- never a default, never a guess, and never a fallback
    /// password.
    ///
    /// <b>Nothing here reaches production.</b> It reads a development path, it is used
    /// only by tests and an editor harness, and it holds a value only for as long as the
    /// object lives.
    /// </remarks>
    public sealed class IntegrationFixture
    {
        /// <summary>Where the backend writes the handoff, relative to the project root.</summary>
        public const string RelativePath = "backend/storage/integration-fixture.json";

        private IntegrationFixture(bool available, JsonReader json, string reason)
        {
            IsAvailable = available;
            Reason = reason;

            LoginIdentifier = json.String("login_identifier");
            Password = json.String("password");
            AccountId = json.String("account_id");
            ServerId = json.String("server_id");
            ChannelId = json.String("channel_id");
            CharacterId = json.String("character_id");
            MapId = json.String("map_id");
            Database = json.String("database");
        }

        /// <summary>Whether a fixture was found and could be read.</summary>
        public bool IsAvailable { get; }

        /// <summary>Why it was not, for a test to report when it skips.</summary>
        public string Reason { get; }

        public string LoginIdentifier { get; }

        /// <summary>The password the backend generated for this run. Never logged.</summary>
        public string Password { get; }

        public string AccountId { get; }

        public string ServerId { get; }

        public string ChannelId { get; }

        public string CharacterId { get; }

        public string MapId { get; }

        /// <summary>The database it seeded, so a test can assert it is not the live one.</summary>
        public string Database { get; }

        /// <summary>
        /// Loads the handoff, or explains why there is none.
        /// </summary>
        /// <remarks>Total: a missing file, an unreadable one and a malformed one all
        /// produce an unavailable fixture rather than an exception, because the caller is
        /// a test that must skip cleanly rather than fail for the wrong reason.</remarks>
        public static IntegrationFixture Load(string projectRoot = null)
        {
            string path = string.IsNullOrEmpty(projectRoot)
                ? RelativePath
                : Path.Combine(projectRoot, RelativePath);

            try
            {
                if (!File.Exists(path))
                {
                    return Unavailable("no fixture at " + path
                        + " -- run: php backend/bin/integration-fixture.php");
                }

                string text = File.ReadAllText(path);
                var json = JsonReader.Parse(text);

                if (string.IsNullOrEmpty(json.String("login_identifier")))
                {
                    return Unavailable("fixture at " + path + " has no login identifier");
                }

                return new IntegrationFixture(true, json, null);
            }
            catch (IOException e)
            {
                return Unavailable(e.Message);
            }
        }

        private static IntegrationFixture Unavailable(string reason)
        {
            return new IntegrationFixture(false, JsonReader.Parse(string.Empty), reason);
        }

        /// <summary>Never prints the password.</summary>
        public override string ToString()
        {
            return IsAvailable
                ? "fixture " + LoginIdentifier + " in " + Database
                : "no fixture: " + Reason;
        }
    }
}
