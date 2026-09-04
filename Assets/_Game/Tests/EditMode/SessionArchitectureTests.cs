using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The boundaries this phase exists to draw, checked by reading the code.
    /// </summary>
    /// <remarks>
    /// The rules here are the ones a future phase is most likely to break by accident: a
    /// password reaching the domain, an HTTP call appearing under Gameplay, a second session
    /// state machine, a hard-coded server id. Each is asserted against the source rather than
    /// trusted to a comment.
    /// </remarks>
    [TestFixture]
    internal sealed class SessionArchitectureTests : SessionTestBase
    {
        private static readonly string[] SessionDomainFiles =
        {
            "Assets/_Game/Scripts/Gameplay/AccountSessionState.cs",
            "Assets/_Game/Scripts/Gameplay/SessionFlowService.cs",
            "Assets/_Game/Scripts/Gameplay/SessionDirectory.cs"
        };

        // ---- credentials and secrets ---------------------------------------------------

        [Test]
        public void No_credential_appears_anywhere_in_the_domain()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Gameplay",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    string lower = code.ToLowerInvariant();

                    Assert.That(lower.Contains("password"), Is.False, file);
                    Assert.That(lower.Contains("passwordhash"), Is.False, file);
                    Assert.That(lower.Contains("bcrypt"), Is.False, file);
                    Assert.That(lower.Contains("sha256"), Is.False, file);
                    Assert.That(lower.Contains("jwt"), Is.False, file);
                    Assert.That(lower.Contains("apikey"), Is.False, file);
                    Assert.That(lower.Contains("connectionstring"), Is.False, file);
                }
            }
        }

        [Test]
        public void No_secret_appears_anywhere_in_the_scripts()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    string lower = code.ToLowerInvariant();

                    Assert.That(lower.Contains("signingkey"), Is.False, file);
                    Assert.That(lower.Contains("privatekey"), Is.False, file);
                    Assert.That(lower.Contains("client_secret"), Is.False, file);
                    Assert.That(lower.Contains("db_password"), Is.False, file);
                }
            }
        }

        [Test]
        public void The_session_token_is_opaque_and_decodes_nothing()
        {
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Contracts/SessionContracts.cs"))
            {
                Assert.That(code, Does.Not.Contain("Convert.FromBase64"));
                Assert.That(code, Does.Not.Contain("Convert.ToBase64"));
                Assert.That(code, Does.Not.Contain("Encoding."));
            }

            // It carries a string and nothing else, and never prints it.
            System.Reflection.PropertyInfo[] properties = typeof(SessionToken).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(AccountId)),
                    property.Name + " would make a token decodable into an identity");
            }

            Assert.That(new SessionToken("super-secret-value").ToString(),
                Is.EqualTo("<token>"), "a token must not appear in a log");
        }

        [Test]
        public void No_service_takes_a_token_as_authorisation()
        {
            // Authorisation is decided by looking a session up, never by inspecting a bearer
            // value. Nothing in the flow accepts one.
            foreach (string file in SessionDomainFiles)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("SessionToken"), file);
                }
            }
        }

        // ---- transport separation ------------------------------------------------------

        [Test]
        public void No_transport_appears_in_the_domain()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Gameplay",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("UnityWebRequest"), file);
                    Assert.That(code, Does.Not.Contain("HttpClient"), file);
                    Assert.That(code, Does.Not.Contain("System.Net"), file);
                    Assert.That(code, Does.Not.Contain("http://"), file);
                    Assert.That(code, Does.Not.Contain("https://"), file);
                    Assert.That(code, Does.Not.Contain("localhost"), file);
                    Assert.That(code, Does.Not.Contain("SELECT "), file);
                    Assert.That(code, Does.Not.Contain("MySql"), file);
                    Assert.That(code, Does.Not.Contain(".php"), file);
                }
            }
        }

        /// <summary>
        /// The one file in Backend that is allowed to name a concrete transport.
        /// </summary>
        /// <remarks>
        /// Phase 15 asserted that <i>nothing</i> in Backend named a transport, which was true
        /// while Backend held only the seam. Phase 16 adds the implementation, and the
        /// property actually worth protecting was never "no file mentions UnityWebRequest" --
        /// it was "the seam does not, so nothing above it becomes an HTTP caller."
        ///
        /// So the exception is named rather than the rule relaxed, and the test below checks
        /// that this list still has exactly one entry. A second transport, or a stray
        /// <c>http://</c> in <c>HttpAccountApi</c>, still fails.
        /// </remarks>
        private static readonly string[] TransportImplementations =
        {
            // Opens the connection.
            "UnityWebRequestTransport.cs",

            // Names it, so that nothing outside Backend has to. It exists because an audit
            // found the world server constructing a transport itself, which made the Server
            // assembly an HTTP caller and defeated the authority seam.
            "BackendAuthority.cs",
        };

        /// <summary>Files that may actually open a connection. Still exactly one.</summary>
        private static readonly string[] TransportSenders = { "UnityWebRequestTransport.cs" };

        [Test]
        public void No_transport_appears_in_the_backend_seam_either()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Backend",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                if (System.Array.IndexOf(TransportImplementations,
                    System.IO.Path.GetFileName(file)) >= 0)
                {
                    continue;
                }

                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("UnityWebRequest"), file);
                    Assert.That(code, Does.Not.Contain("HttpClient"), file);
                    Assert.That(code, Does.Not.Contain("http://"), file);
                    Assert.That(code, Does.Not.Contain("https://"), file);
                    Assert.That(code, Does.Not.Contain("SELECT "), file);
                }
            }
        }

        [Test]
        public void Exactly_one_file_implements_a_transport()
        {
            // Naming a transport and opening one are different privileges. Two files may
            // name it; exactly one may send. Either list growing must be a deliberate
            // decision somebody makes by editing this test, not something that slips in.
            Assert.That(TransportImplementations.Length, Is.EqualTo(2));
            Assert.That(TransportSenders.Length, Is.EqualTo(1));

            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Backend",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var senders = new System.Collections.Generic.List<string>();

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    if (code.Contains("SendWebRequest") || code.Contains("new HttpClient"))
                    {
                        senders.Add(System.IO.Path.GetFileName(file));
                        break;
                    }
                }
            }

            Assert.That(senders, Is.EquivalentTo(TransportSenders),
                "only the named transport may open a connection");
        }

        [Test]
        public void The_transport_implementation_still_writes_nothing_to_a_log()
        {
            // It handles the bearer token and the login body. A Debug.Log anywhere in it is
            // one accident away from a password in a player's log file.
            string file = "Assets/_Game/Scripts/Backend/UnityWebRequestTransport.cs";

            foreach (string code in CodeLines(file))
            {
                Assert.That(code, Does.Not.Contain("Debug.Log"), file);
                Assert.That(code, Does.Not.Contain("Console.Write"), file);
            }
        }

        [Test]
        public void Gameplay_remains_engine_free()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Gameplay",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("UnityEngine"), file);
                }
            }
        }

        [Test]
        public void The_domain_holds_no_scene_or_network_object()
        {
            System.Type[] types =
            {
                typeof(AccountSessionState), typeof(SessionDirectory)
            };

            foreach (System.Type type in types)
            {
                foreach (System.Reflection.PropertyInfo property in type.GetProperties())
                {
                    string name = property.PropertyType.Name;

                    Assert.That(name, Is.Not.EqualTo("GameObject"), type.Name + "." + property.Name);
                    Assert.That(name, Is.Not.EqualTo("Scene"), type.Name + "." + property.Name);
                    Assert.That(name, Is.Not.EqualTo("NetworkObject"), type.Name + "." + property.Name);
                    Assert.That(name, Is.Not.EqualTo("NetworkConnection"), type.Name + "." + property.Name);
                }
            }
        }

        [Test]
        public void No_scene_name_appears_in_the_session_flow()
        {
            foreach (string file in SessionDomainFiles)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain(".unity"), file);
                    Assert.That(code, Does.Not.Contain("SceneManager"), file);
                    Assert.That(code, Does.Not.Contain("LoadScene"), file);
                }
            }
        }

        [Test]
        public void There_is_still_one_scene_loader()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int loaders = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class MapSceneLoader")) loaders++;

                Assert.That(source, Does.Not.Contain("class LoginSceneLoader"), file);
                Assert.That(source, Does.Not.Contain("class ServerSelectSceneLoader"), file);
                Assert.That(source, Does.Not.Contain("class CharacterSelectSceneLoader"), file);
            }

            Assert.That(loaders, Is.EqualTo(1), "Phase 11's loader is reused, not duplicated");
        }

        // ---- no duplicate systems ------------------------------------------------------

        [Test]
        public void There_is_one_session_state_machine()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int sessions = 0;
            int flows = 0;
            int directories = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class AccountSessionState")) sessions++;
                if (source.Contains("class SessionFlowService")) flows++;
                if (source.Contains("class SessionDirectory")) directories++;

                Assert.That(source, Does.Not.Contain("class LoginState"), file);
                Assert.That(source, Does.Not.Contain("class LoginManager"), file);
                Assert.That(source, Does.Not.Contain("class AuthenticationManager"), file);
                Assert.That(source, Does.Not.Contain("class ServerSelectState"), file);
                Assert.That(source, Does.Not.Contain("class ChannelSelectState"), file);
                Assert.That(source, Does.Not.Contain("class CharacterSelectState"), file);
            }

            Assert.That(sessions, Is.EqualTo(1));
            Assert.That(flows, Is.EqualTo(1));
            Assert.That(directories, Is.EqualTo(1));
        }

        [Test]
        public void The_legal_transitions_are_stated_in_exactly_one_place()
        {
            int declarations = 0;

            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                if (source.Contains("bool CanTransitionTo")) declarations++;
            }

            Assert.That(declarations, Is.EqualTo(1),
                "a second copy of the machine would eventually disagree");
        }

        [Test]
        public void There_is_one_character_ownership_authority()
        {
            // Both seams ask the same question; neither derives an answer of its own.
            Assert.That(typeof(ISessionAuthority).GetMethod("OwnsCharacter"), Is.Not.Null);
            Assert.That(typeof(IAccountApi).GetMethod("OwnsCharacter"), Is.Not.Null);

            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/SessionFlowService.cs"))
            {
                // Ownership is asked, never computed from a list the client was given.
                Assert.That(code, Does.Not.Contain("_ownership"));
                Assert.That(code, Does.Not.Contain(".Owner =="));
            }
        }

        [Test]
        public void Ownership_reuses_the_existing_owner_identity()
        {
            var account = new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active);

            Assert.That(account.ToOwnerId(), Is.EqualTo(new OwnerId(AccountA.Value)),
                "an account projects onto the Phase 08 ownership identity rather than "
                + "introducing a second one");

            AccountSessionState session = SignIn(AccountA);

            Assert.That(session.Owner, Is.EqualTo(new OwnerId(AccountA.Value)));
        }

        // ---- hard-coded identities -----------------------------------------------------

        [Test]
        public void No_identity_is_written_into_any_session_file()
        {
            var files = new List<string>(SessionDomainFiles);
            files.Add("Assets/_Game/Scripts/Contracts/SessionContracts.cs");
            files.Add("Assets/_Game/Scripts/Contracts/AccountContracts.cs");
            files.Add("Assets/_Game/Scripts/Contracts/DirectoryContracts.cs");
            files.Add("Assets/_Game/Scripts/Backend/IAccountApi.cs");

            for (int i = 0; i < files.Count; i++)
            {
                foreach (string code in CodeLines(files[i]))
                {
                    Assert.That(code, Does.Not.Contain("\"server:"), files[i]);
                    Assert.That(code, Does.Not.Contain("\"channel:"), files[i]);
                    Assert.That(code, Does.Not.Contain("\"account:"), files[i]);
                    Assert.That(code, Does.Not.Contain("\"char:"), files[i]);
                    Assert.That(code, Does.Not.Contain("\"map."), files[i]);
                    Assert.That(code, Does.Not.Contain("ServerId(\""), files[i]);
                    Assert.That(code, Does.Not.Contain("ChannelId(\""), files[i]);
                }
            }
        }

        [Test]
        public void The_character_slot_limit_is_authored()
        {
            SessionConfiguration three = AddConfiguration(characterSlots: 3);

            Assert.That(three.MaxCharacterSlots, Is.EqualTo(3));
            Assert.That(SessionConfiguration.Default.MaxCharacterSlots,
                Is.EqualTo(SessionConfiguration.DefaultCharacterSlots));

            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/SessionFlowService.cs"))
            {
                Assert.That(code, Does.Not.Contain("== 3"));
                Assert.That(code, Does.Not.Contain(">= 3"));
            }
        }

        [Test]
        public void No_service_reads_an_ambient_clock()
        {
            foreach (string file in SessionDomainFiles)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("DateTime.Now"), file);
                    Assert.That(code, Does.Not.Contain("DateTime.UtcNow"), file);
                    Assert.That(code, Does.Not.Contain("Time.time"), file);
                    Assert.That(code, Does.Not.Contain("Stopwatch"), file);
                }
            }
        }

        // ---- ui boundary ---------------------------------------------------------------

        [Test]
        public void The_ui_assembly_holds_no_session_state()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/UI", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("ChibiFantasy.Gameplay"), file);
                    Assert.That(code, Does.Not.Contain("ChibiFantasy.Backend"), file);
                    Assert.That(code, Does.Not.Contain("AccountSessionState"), file);
                    Assert.That(code, Does.Not.Contain("SessionDirectory"), file);
                    Assert.That(code, Does.Not.Contain("IAccountApi"), file);
                }
            }
        }

        [Test]
        public void The_ui_holds_no_credential_field()
        {
            System.Type[] viewData =
            {
                typeof(ChibiFantasy.UI.LoginViewData),
                typeof(ChibiFantasy.UI.SessionFlowViewData),
                typeof(ChibiFantasy.UI.ServerRowViewData),
                typeof(ChibiFantasy.UI.ChannelRowViewData),
                typeof(ChibiFantasy.UI.CharacterRowViewData)
            };

            foreach (System.Type type in viewData)
            {
                foreach (System.Reflection.PropertyInfo property in type.GetProperties())
                {
                    string lower = property.Name.ToLowerInvariant();

                    Assert.That(lower.Contains("password"), Is.False,
                        type.Name + "." + property.Name);
                    Assert.That(lower.Contains("token"), Is.False,
                        type.Name + "." + property.Name);
                    Assert.That(lower.Contains("secret"), Is.False,
                        type.Name + "." + property.Name);
                }
            }
        }

        [Test]
        public void Session_commands_live_only_in_the_session_controller()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.EndsWith("SessionUiController.cs")) continue;

                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("SessionFlowService.TryLogin"), normalized);
                Assert.That(source, Does.Not.Contain("SessionFlowService.TryEnterWorld"),
                    normalized);
                Assert.That(source, Does.Not.Contain("SessionFlowService.TrySelectCharacter"),
                    normalized);
            }
        }

        [Test]
        public void A_view_row_carries_no_gameplay_object()
        {
            System.Type[] rows =
            {
                typeof(ChibiFantasy.UI.ServerRowViewData),
                typeof(ChibiFantasy.UI.ChannelRowViewData),
                typeof(ChibiFantasy.UI.CharacterRowViewData)
            };

            foreach (System.Type row in rows)
            {
                foreach (System.Reflection.PropertyInfo property in row.GetProperties())
                {
                    Assert.That(property.PropertyType.IsValueType
                        || property.PropertyType == typeof(string), Is.True,
                        row.Name + "." + property.Name + " is a reference a view could mutate");
                }
            }
        }

        // ---- population and presence honesty -------------------------------------------

        [Test]
        public void An_unknown_population_is_reported_as_unknown()
        {
            PopulationReading unknown = PopulationReading.Unknown(200);

            Assert.That(unknown.IsKnown, Is.False);
            Assert.That(unknown.IsFull, Is.False,
                "absent information is not a barrier");
        }

        [Test]
        public void No_population_figure_is_invented()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');
                if (!normalized.Contains("Session") && !normalized.Contains("Directory")) continue;

                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("Random"), normalized);
                }
            }
        }

        [Test]
        public void Character_presence_is_unknown_until_a_server_reports_it()
        {
            var characters = new List<CharacterSelectEntry>
            {
                NewCharacter(CharacterA1, "Ayla", CharacterGender.Female, 25)
            };

            PopulationReading presence = Client.UI.SessionAdapter.PresenceOf(characters,
                CharacterA1);

            Assert.That(presence.IsKnown, Is.False,
                "reporting Offline would be a fabrication a player acts on");
        }

        // ---- phase 13 regression -------------------------------------------------------

        [Test]
        public void The_economy_boundary_is_untouched()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string normalized = file.Replace('\\', '/');

                if (normalized.EndsWith("EconomyService.cs")) continue;
                if (normalized.EndsWith("CharacterWalletState.cs")) continue;

                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("TryApplyDelta"), normalized);
                }
            }
        }

        [Test]
        public void The_ownership_transfer_seam_is_untouched()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            int seams = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                if (source.Contains("class ItemOwnershipTransfer")) seams++;
            }

            Assert.That(seams, Is.EqualTo(1));
        }

        [Test]
        public void No_authentication_behaviour_depends_on_game_content()
        {
            foreach (string file in SessionDomainFiles)
            {
                foreach (string code in CodeLines(file))
                {
                    Assert.That(code, Does.Not.Contain("DevilFruit"), file);
                    Assert.That(code, Does.Not.Contain("CardDefinition"), file);
                    Assert.That(code, Does.Not.Contain("PetDefinition"), file);
                    Assert.That(code, Does.Not.Contain("ItemDefinition"), file);
                }
            }
        }
    }
}
