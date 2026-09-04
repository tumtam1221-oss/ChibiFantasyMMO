using System.Collections.Generic;
using System.IO;
using ChibiFantasy.Contracts;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The boundaries Phase 16 draws, enforced rather than audited once.
    /// </summary>
    /// <remarks>
    /// Every assertion here started as a finding in a manual audit. A rule that lives only
    /// in a report is a rule that holds until the next person is in a hurry, so each one is
    /// a test now -- including the one that was actually being broken when the audit ran:
    /// the world server was constructing an HTTP transport itself.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldArchitectureTests
    {
        private const string Server = "Assets/_Game/Scripts/Server";
        private const string NetworkAssembly = "Assets/_Game/Scripts/Network";
        private const string Gameplay = "Assets/_Game/Scripts/Gameplay";
        private const string Ui = "Assets/_Game/Scripts/UI";
        private const string Client = "Assets/_Game/Scripts/Client";

        /// <summary>Code lines only, with comments and blank lines removed.</summary>
        private static IEnumerable<string> CodeLines(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();

                    if (line.Length == 0) continue;
                    if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*"))
                    {
                        continue;
                    }

                    yield return file + ": " + line;
                }
            }
        }

        private static void AssertAbsent(string directory, params string[] forbidden)
        {
            foreach (string line in CodeLines(directory))
            {
                foreach (string term in forbidden)
                {
                    Assert.That(line, Does.Not.Contain(term), line);
                }
            }
        }

        // ---- the server's dependencies ----------------------------------------------------

        [Test]
        public void The_server_names_no_transport()
        {
            // The finding that produced BackendAuthority: the bootstrap was constructing a
            // UnityWebRequestTransport, which made this assembly an HTTP caller and defeated
            // the point of the authority seam.
            AssertAbsent(Server, "UnityWebRequest", "HttpClient", "System.Net", "WebRequest");
        }

        [Test]
        public void The_server_names_no_database()
        {
            AssertAbsent(Server, "MySql", "PDO", "SELECT ", "INSERT INTO", "SQLSTATE", ".php");
        }

        [Test]
        public void The_network_assembly_names_neither_a_transport_nor_a_database()
        {
            AssertAbsent(NetworkAssembly, "UnityWebRequest", "HttpClient", "MySql", "PDO",
                "SELECT ", ".php");
        }

        [Test]
        public void Gameplay_remains_free_of_the_network_as_well_as_the_engine()
        {
            AssertAbsent(Gameplay, "UnityEngine", "FishNet", "UnityWebRequest", "System.Net");
        }

        // ---- secrets -------------------------------------------------------------------------

        [Test]
        public void No_credential_or_connection_detail_appears_anywhere_in_unity()
        {
            foreach (string directory in new[] { Server, NetworkAssembly, Gameplay, Ui, Client })
            {
                AssertAbsent(directory, "DB_PASSWORD", "DB_USERNAME", "ConnectionString",
                    "password=", "Password=");
            }
        }

        [Test]
        public void No_token_reaches_the_ui_or_the_view_layer()
        {
            // A token in view data is a token in a screenshot, a bug report and a log.
            foreach (string line in CodeLines(Ui))
            {
                Assert.That(line.ToLowerInvariant(), Does.Not.Contain("token"), line);
            }
        }

        [Test]
        public void Nothing_in_the_network_or_server_layer_logs_a_secret()
        {
            foreach (string directory in new[] { Server, NetworkAssembly })
            {
                foreach (string line in CodeLines(directory))
                {
                    if (!line.Contains("Debug.Log")) continue;

                    string lower = line.ToLowerInvariant();

                    Assert.That(lower, Does.Not.Contain("token"), line);
                    Assert.That(lower, Does.Not.Contain("password"), line);
                    Assert.That(lower, Does.Not.Contain("bearer"), line);
                }
            }
        }

        [Test]
        public void The_authenticator_contains_no_logging_call_at_all()
        {
            // It handles the join message, which carries a session token. The surest way not
            // to log a secret is to have nowhere that logs anything.
            foreach (string raw in File.ReadAllLines(Server + "/WorldAuthenticator.cs"))
            {
                string line = raw.Trim();

                if (line.StartsWith("//") || line.StartsWith("*")) continue;

                Assert.That(line, Does.Not.Contain("Debug.Log"), line);
            }
        }

        // ---- client authority ------------------------------------------------------------------

        [Test]
        public void A_join_message_has_nowhere_to_put_stats_a_position_or_an_owner()
        {
            // Rule 16.16 N: a forged spawn is not something the server has to detect,
            // because there is no field a client could send one in.
            System.Type message = typeof(WorldJoinRequestMessage);

            foreach (string absent in new[]
                     { "X", "Y", "Z", "Position", "Level", "Stats", "Health", "OwnerId",
                       "ClaimedOwnerId", "Experience" })
            {
                Assert.That(message.GetField(absent), Is.Null,
                    absent + " must not be something a client can send");
            }
        }

        [Test]
        public void A_join_claim_has_nowhere_to_put_an_owner()
        {
            Assert.That(typeof(WorldJoinClaim).GetProperty("ClaimedOwner"), Is.Null);
            Assert.That(typeof(WorldJoinClaim).GetProperty("Owner"), Is.Null,
                "ownership is projected from the account, never claimed");
        }

        [Test]
        public void An_admission_projects_ownership_rather_than_storing_a_second_copy()
        {
            WorldAdmission admission = WorldAdmission.Admitted(
                new ChibiFantasy.Core.SessionId("s"), new ChibiFantasy.Core.AccountId("acc"),
                new ChibiFantasy.Core.CharacterId("c"), new ChibiFantasy.Core.ServerId("srv"),
                new ChibiFantasy.Core.ChannelId("ch"), new ChibiFantasy.Core.DefinitionId("m"),
                default, default, SessionState.EnteringWorld);

            Assert.That(admission.Owner.Value, Is.EqualTo(admission.Account.Value));
        }

        // ---- no duplicate systems ------------------------------------------------------------------

        [Test]
        public void None_of_the_forbidden_parallel_systems_exist()
        {
            string[] forbidden =
            {
                "class LoginManager", "class AuthenticationManager", "class CharacterSelectState",
                "class InventoryNetworkState", "class TradeInventory", "class ShopInventory",
                "class GoldManager", "class SessionManager",
            };

            foreach (string directory in new[]
                     { Server, NetworkAssembly, Gameplay, Ui, Client,
                       "Assets/_Game/Scripts/Backend", "Assets/_Game/Scripts/Contracts" })
            {
                AssertAbsent(directory, forbidden);
            }
        }

        [Test]
        public void There_is_exactly_one_world_connection_registry_and_one_coordinator()
        {
            Assert.That(typeof(WorldConnectionRegistry).Assembly.GetName().Name,
                Is.EqualTo("ChibiFantasy.Network"));
            Assert.That(typeof(WorldEntryCoordinator).Assembly.GetName().Name,
                Is.EqualTo("ChibiFantasy.Server"));

            // The authority seam lives in Contracts so neither side owns it.
            Assert.That(typeof(IWorldSessionAuthority).Assembly.GetName().Name,
                Is.EqualTo("ChibiFantasy.Contracts"));
        }

        [Test]
        public void The_network_layer_holds_no_engine_object_in_a_message()
        {
            // Rule 16.13: contracts carry primitives and ids, never engine types.
            foreach (System.Type type in new[]
                     {
                         typeof(WorldJoinRequestMessage), typeof(WorldJoinResponseMessage),
                         typeof(WorldSpawnMessage), typeof(WorldLeaveMessage),
                     })
            {
                foreach (System.Reflection.FieldInfo field in type.GetFields())
                {
                    System.Type fieldType = field.FieldType;

                    Assert.That(fieldType.IsPrimitive || fieldType == typeof(string),
                        Is.True,
                        type.Name + "." + field.Name + " is " + fieldType.Name
                        + "; network messages carry primitives and strings only");
                }
            }
        }
    }
}
