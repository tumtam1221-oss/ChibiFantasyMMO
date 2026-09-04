using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What must not reach a player's machine.
    /// </summary>
    /// <remarks>
    /// Both findings here were named in the Phase 17 brief, and both were real: a type that
    /// holds a password compiled into every build, and a prototype harness that authors a
    /// character from a literal. Neither was exploitable on its own; both were one careless
    /// build away from being embarrassing.
    ///
    /// These are source-level assertions rather than build-level ones because a test that
    /// only fails after a player build is a test nobody runs.
    /// </remarks>
    [TestFixture]
    internal sealed class ProductionSafetyTests
    {
        private const string Scripts = "Assets/_Game/Scripts";

        private static IEnumerable<string> CodeLines(string file)
        {
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();

                if (line.Length == 0) continue;
                if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*"))
                {
                    continue;
                }

                yield return line;
            }
        }

        // ---- the two findings the brief named -------------------------------------------

        [Test]
        public void The_integration_fixture_is_compiled_out_of_player_builds()
        {
            string file = Scripts + "/Backend/IntegrationFixture.cs";
            string text = File.ReadAllText(file);

            Assert.That(text, Does.Contain("#if UNITY_EDITOR"),
                "it reads a development path and holds a password; the surest way for code "
                + "not to run in production is for it not to be there");
            Assert.That(text.TrimEnd(), Does.EndWith("#endif"));
        }

        [Test]
        public void The_prototype_harness_refuses_to_run_outside_the_editor()
        {
            string file = Scripts + "/Client/Prototype/ProtoInventoryHarness.cs";
            string text = File.ReadAllText(file);

            // It authors an owner and a character from literals, which is correct for a
            // prototype scene and wrong for anything a player runs.
            Assert.That(text, Does.Contain("Application.isEditor"),
                "the harness must disable itself in a build");
        }

        [Test]
        public void Only_prototype_code_builds_a_character_identity_from_a_literal()
        {
            // A production character's identity comes from the account database. Anywhere
            // else constructing one from a constant is either prototype code or a bug.
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(Scripts, "*.cs",
                SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Prototype/")) continue;

                foreach (string line in CodeLines(file))
                {
                    if (line.Contains("new CharacterId(\"")
                        || line.Contains("new AccountId(\"")
                        || line.Contains("new OwnerId(\""))
                    {
                        offenders.Add(Path.GetFileName(file) + ": " + line);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "identity is issued by the account database, never written down");
        }

        // ---- the standing rules ----------------------------------------------------------

        [Test]
        public void No_credential_is_written_down_anywhere_in_unity()
        {
            foreach (string file in Directory.GetFiles(Scripts, "*.cs",
                SearchOption.AllDirectories))
            {
                foreach (string line in CodeLines(file))
                {
                    Assert.That(line, Does.Not.Contain("DB_PASSWORD"), file);
                    Assert.That(line, Does.Not.Contain("DB_USERNAME"), file);
                    Assert.That(line, Does.Not.Contain("ConnectionString"), file);
                    Assert.That(line.ToLowerInvariant(), Does.Not.Contain("password=\""), file);
                }
            }
        }

        [Test]
        public void No_sql_reaches_unity()
        {
            foreach (string file in Directory.GetFiles(Scripts, "*.cs",
                SearchOption.AllDirectories))
            {
                foreach (string line in CodeLines(file))
                {
                    Assert.That(line, Does.Not.Contain("SELECT "), file);
                    Assert.That(line, Does.Not.Contain("INSERT INTO"), file);
                    Assert.That(line, Does.Not.Contain("UPDATE "), file);
                    Assert.That(line, Does.Not.Contain("SQLSTATE"), file);
                }
            }
        }

        [Test]
        public void Nothing_logs_a_secret()
        {
            foreach (string file in Directory.GetFiles(Scripts, "*.cs",
                SearchOption.AllDirectories))
            {
                foreach (string line in CodeLines(file))
                {
                    if (!line.Contains("Debug.Log")) continue;

                    string lower = line.ToLowerInvariant();

                    Assert.That(lower, Does.Not.Contain("token"), file + ": " + line);
                    Assert.That(lower, Does.Not.Contain("password"), file + ": " + line);
                    Assert.That(lower, Does.Not.Contain("bearer"), file + ": " + line);
                }
            }
        }

        [Test]
        public void Gameplay_holds_no_engine_transport_or_database_dependency()
        {
            foreach (string file in Directory.GetFiles(Scripts + "/Gameplay", "*.cs",
                SearchOption.AllDirectories))
            {
                foreach (string line in CodeLines(file))
                {
                    Assert.That(line, Does.Not.Contain("UnityEngine"), file);
                    Assert.That(line, Does.Not.Contain("FishNet"), file);
                    Assert.That(line, Does.Not.Contain("UnityWebRequest"), file);
                    Assert.That(line, Does.Not.Contain("System.Net"), file);
                }
            }
        }
    }
}
