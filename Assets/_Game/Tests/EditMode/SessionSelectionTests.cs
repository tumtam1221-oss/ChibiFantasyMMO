using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Server, channel and character selection, and the enter-world handoff.
    /// </summary>
    /// <remarks>
    /// The property under test throughout is that the authority is asked afresh at every step
    /// and again at the end. A server that closed, a channel that filled, an account that lost
    /// a character between choosing and entering must all be caught -- so the same checks run
    /// twice on purpose, and several tests change the world in between to prove it.
    /// </remarks>
    [TestFixture]
    internal sealed class SessionSelectionTests : SessionTestBase
    {
        // ---- server --------------------------------------------------------------------

        [Test]
        public void An_open_server_can_be_selected()
        {
            AccountSessionState session = SignIn(AccountA);

            SessionResult result = SessionFlowService.TrySelectServer(Command(session), Server1,
                Flow());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Server, Is.EqualTo(Server1));
            Assert.That(result.State, Is.EqualTo(SessionState.ServerSelected));
            Assert.That(result.ChannelDiscoveryRequired, Is.True,
                "the client is told what comes next rather than deciding the flow");
        }

        [Test]
        public void An_unknown_server_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session),
                new ServerId("server:gone"), Flow()).Reason,
                Is.EqualTo(SessionRejection.UnknownServer));
        }

        [Test]
        public void A_disabled_server_is_refused()
        {
            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Online,
                PopulationReading.Known(1, 1000), enabled: false));

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.ServerUnavailable));
        }

        [Test]
        public void A_server_in_maintenance_is_refused()
        {
            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Maintenance,
                PopulationReading.Known(0, 1000)));

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.ServerMaintenance));
        }

        [Test]
        public void A_full_server_is_refused()
        {
            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Online,
                PopulationReading.Known(1000, 1000)));

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.ServerFull));
        }

        [Test]
        public void A_server_with_an_unknown_population_is_not_treated_as_full()
        {
            AccountSessionState session = SignIn(AccountA);

            // Server 2 reports no figure at all.
            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server2, Flow())
                .IsAccepted, Is.True,
                "absent information must not be read as a barrier");
        }

        [Test]
        public void A_busy_server_is_still_selectable()
        {
            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Busy,
                PopulationReading.Known(900, 1000)));

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .IsAccepted, Is.True);
        }

        [Test]
        public void A_server_demanding_a_newer_client_is_refused()
        {
            var strict = new VersionRequirement(new VersionNumber(9, 0, 0),
                new VersionNumber(9, 0, 0), new VersionNumber(3));

            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Online,
                PopulationReading.Unknown(), versions: strict));

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.VersionMismatch),
                "servers may be on different builds during a rollout");
        }

        [Test]
        public void A_hidden_server_is_absent_from_the_list_and_still_refused()
        {
            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Hidden,
                PopulationReading.Unknown()));

            var listed = Authority.GetServers(AccountA).Value;

            bool present = false;
            for (int i = 0; i < listed.Count; i++)
            {
                if (listed[i].Server == Server1) present = true;
            }

            Assert.That(present, Is.False, "hidden means absent, not greyed out");

            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.ServerUnavailable),
                "and asking for it directly still fails");
        }

        // ---- channel -------------------------------------------------------------------

        [Test]
        public void A_channel_of_the_selected_server_can_be_selected()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            SessionResult result = SessionFlowService.TrySelectChannel(Command(session),
                Channel1A, Flow());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Channel, Is.EqualTo(Channel1A));
            Assert.That(result.State, Is.EqualTo(SessionState.ChannelSelected));
        }

        [Test]
        public void A_channel_of_another_server_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            SessionResult result = SessionFlowService.TrySelectChannel(Command(session),
                Channel1B, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.ChannelServerMismatch),
                "a channel number alone is a label, not an identity");
            Assert.That(session.Channel.IsValid, Is.False);
        }

        [Test]
        public void An_unknown_channel_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Assert.That(SessionFlowService.TrySelectChannel(Command(session),
                new ChannelId("channel:gone"), Flow()).Reason,
                Is.EqualTo(SessionRejection.UnknownChannel));
        }

        [Test]
        public void A_channel_in_maintenance_is_refused()
        {
            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Maintenance,
                PopulationReading.Known(0, 200)));

            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Assert.That(SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow())
                .Reason, Is.EqualTo(SessionRejection.ChannelMaintenance));
        }

        [Test]
        public void A_full_channel_is_refused()
        {
            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Online,
                PopulationReading.Known(200, 200)));

            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Assert.That(SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow())
                .Reason, Is.EqualTo(SessionRejection.ChannelFull));
        }

        [Test]
        public void A_disabled_channel_is_refused()
        {
            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Online,
                PopulationReading.Unknown(), enabled: false));

            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Assert.That(SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow())
                .Reason, Is.EqualTo(SessionRejection.ChannelUnavailable));
        }

        // ---- PK ------------------------------------------------------------------------

        [Test]
        public void Pk_is_read_from_the_authority_and_not_derived()
        {
            ChannelInfo quiet;
            ChannelInfo rough;

            Authority.TryGetChannel(Channel1A, out quiet);
            Authority.TryGetChannel(Channel2A, out rough);

            Assert.That(quiet.PkEnabled, Is.False);
            Assert.That(rough.PkEnabled, Is.True,
                "two channels of one server differ, so nothing can be deriving it");
        }

        [Test]
        public void Re_authoring_pk_changes_the_answer_with_no_code_change()
        {
            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Online,
                PopulationReading.Unknown(), pkEnabled: true));

            ChannelInfo updated;
            Authority.TryGetChannel(Channel1A, out updated);

            Assert.That(updated.PkEnabled, Is.True);
        }

        [Test]
        public void A_client_cannot_change_pk_by_selecting_a_channel()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel2A, Flow());

            ChannelInfo after;
            Authority.TryGetChannel(Channel2A, out after);

            Assert.That(after.PkEnabled, Is.True, "selection does not write to the channel");

            // Structural: the session records which channel, and holds no PK flag of its own.
            foreach (System.Reflection.PropertyInfo property in
                typeof(AccountSessionState).GetProperties())
            {
                Assert.That(property.Name.ToLowerInvariant().Contains("pk"), Is.False,
                    property.Name + " would be a client-side copy of a server rule");
            }
        }

        [Test]
        public void No_channel_is_named_in_the_flow_service()
        {
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Gameplay/SessionFlowService.cs"))
            {
                Assert.That(code, Does.Not.Contain("\"channel:"));
                Assert.That(code, Does.Not.Contain("\"server:"));
                Assert.That(code, Does.Not.Contain("PkEnabled"),
                    "PK is the server's rule; the flow service has no business reading it");
            }
        }

        // ---- character -----------------------------------------------------------------

        [Test]
        public void An_owned_character_can_be_selected()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            SessionResult result = SessionFlowService.TrySelectCharacter(Command(session),
                CharacterA1, Flow());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Character, Is.EqualTo(CharacterA1));
            Assert.That(result.State, Is.EqualTo(SessionState.CharacterSelected));
        }

        [Test]
        public void Another_accounts_character_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            SessionResult result = SessionFlowService.TrySelectCharacter(Command(session),
                CharacterB1, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.CharacterNotOwned));
            Assert.That(session.Character.IsValid, Is.False);
        }

        [Test]
        public void A_foreign_character_and_a_missing_one_are_indistinguishable()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            SessionRejection foreign = SessionFlowService.TrySelectCharacter(Command(session),
                CharacterB1, Flow()).Reason;

            SessionRejection missing = SessionFlowService.TrySelectCharacter(Command(session),
                new CharacterId("char:nobody"), Flow()).Reason;

            Assert.That(foreign, Is.EqualTo(SessionRejection.CharacterNotOwned));
            Assert.That(missing, Is.EqualTo(SessionRejection.CharacterNotOwned),
                "a different answer would confirm that somebody else's character is real");
        }

        [Test]
        public void An_unavailable_character_is_refused()
        {
            Authority.SetCharacterAvailability(CharacterA1, CharacterAvailability.Locked);

            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            Assert.That(SessionFlowService.TrySelectCharacter(Command(session), CharacterA1,
                Flow()).Reason, Is.EqualTo(SessionRejection.CharacterUnavailable));
        }

        [Test]
        public void A_character_already_in_the_world_is_refused()
        {
            Authority.SetCharacterAvailability(CharacterA1, CharacterAvailability.InWorld);

            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            Assert.That(SessionFlowService.TrySelectCharacter(Command(session), CharacterA1,
                Flow()).Reason, Is.EqualTo(SessionRejection.CharacterUnavailable));
        }

        [Test]
        public void An_account_only_ever_sees_its_own_characters()
        {
            var listed = Authority.GetCharacters(AccountA, Server1).Value;

            Assert.That(listed.Count, Is.EqualTo(2));

            for (int i = 0; i < listed.Count; i++)
            {
                Assert.That(listed[i].Character, Is.Not.EqualTo(CharacterB1),
                    "filtering happens at the authority, not after the data has been sent");
            }
        }

        [Test]
        public void A_character_row_carries_no_character_state()
        {
            CharacterSelectEntry entry;
            Authority.TryGetCharacter(CharacterA1, out entry);

            System.Reflection.PropertyInfo[] properties =
                typeof(CharacterSelectEntry).GetProperties();

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(CharacterState)),
                    property.Name);
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("ItemContainerState"),
                    property.Name);
                Assert.That(property.PropertyType.Name, Is.Not.EqualTo("CharacterStatsState"),
                    property.Name);
                Assert.That(property.PropertyType.Name,
                    Is.Not.EqualTo("CharacterEquipmentState"), property.Name);
            }

            Assert.That(entry.Map, Is.EqualTo(new DefinitionId(TownMap)),
                "location is a map reference, reusing Phase 11 rather than a second model");
        }

        // ---- enter world ---------------------------------------------------------------

        [Test]
        public void A_complete_session_is_authorised_for_the_world()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            EnterWorldResult result = SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.EntryState, Is.EqualTo(WorldEntryState.Authorised),
                "authorised, not connected: nothing here connects anything");
            Assert.That(result.Character, Is.EqualTo(CharacterA1));
            Assert.That(result.Server, Is.EqualTo(Server1));
            Assert.That(result.Channel, Is.EqualTo(Channel1A));
            Assert.That(result.Map, Is.EqualTo(new DefinitionId(TownMap)));
            Assert.That(session.State, Is.EqualTo(SessionState.EnteringWorld));
        }

        [Test]
        public void A_spoofed_account_in_the_request_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var forged = new EnterWorldRequest(RequestId.New(), session.SessionId, AccountB,
                session.Character, session.Server, session.Channel, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.SessionInvalid));
            Assert.That(session.State, Is.EqualTo(SessionState.CharacterSelected));
        }

        [Test]
        public void A_spoofed_character_in_the_request_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var forged = new EnterWorldRequest(RequestId.New(), session.SessionId,
                session.Account, CharacterB1, session.Server, session.Channel, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.CharacterNotOwned));
        }

        [Test]
        public void A_spoofed_server_in_the_request_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var forged = new EnterWorldRequest(RequestId.New(), session.SessionId,
                session.Account, session.Character, Server2, session.Channel, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.UnknownServer));
        }

        [Test]
        public void A_spoofed_channel_in_the_request_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var forged = new EnterWorldRequest(RequestId.New(), session.SessionId,
                session.Account, session.Character, session.Server, Channel1B, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.ChannelServerMismatch));
        }

        [Test]
        public void A_spoofed_session_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var forged = new EnterWorldRequest(RequestId.New(), SessionId.New(),
                session.Account, session.Character, session.Server, session.Channel,
                CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.SessionInvalid));
        }

        [Test]
        public void An_expired_session_cannot_enter_the_world()
        {
            Configuration = AddConfiguration(lifetimeSeconds: 100);

            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1, 0);

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow(500)).Reason,
                Is.EqualTo(SessionRejection.SessionExpired));
            Assert.That(session.State, Is.EqualTo(SessionState.CharacterSelected));
        }

        [Test]
        public void A_revoked_session_cannot_enter_the_world()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Sessions.Revoke(session.SessionId);

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).Reason,
                Is.EqualTo(SessionRejection.SessionRevoked));
        }

        [Test]
        public void A_stale_client_cannot_enter_the_world()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            var old = new VersionSet(new VersionNumber(0, 1, 0), new VersionNumber(3),
                new VersionNumber(1, 5, 0));

            var request = new EnterWorldRequest(RequestId.New(), session.SessionId,
                session.Account, session.Character, session.Server, session.Channel, old);

            Assert.That(SessionFlowService.TryEnterWorld(request, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.VersionMismatch));
        }

        [Test]
        public void Maintenance_declared_after_selection_still_blocks_entry()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Authority.Maintenance = true;

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).Reason,
                Is.EqualTo(SessionRejection.ServerMaintenance));
        }

        [Test]
        public void A_server_that_closed_after_selection_blocks_entry()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Maintenance,
                PopulationReading.Unknown()));

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).Reason,
                Is.EqualTo(SessionRejection.ServerMaintenance),
                "everything is re-checked, because the world moves while a player chooses");
        }

        [Test]
        public void A_channel_that_filled_after_selection_blocks_entry()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Online,
                PopulationReading.Known(200, 200)));

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).Reason, Is.EqualTo(SessionRejection.ChannelFull));
        }

        [Test]
        public void A_character_that_became_unavailable_after_selection_blocks_entry()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Authority.SetCharacterAvailability(CharacterA1, CharacterAvailability.Locked);

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).Reason,
                Is.EqualTo(SessionRejection.CharacterUnavailable));
        }

        [Test]
        public void A_rejected_entry_changes_no_session_state()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Revision before = session.Revision;
            Authority.Maintenance = true;

            SessionFlowService.TryEnterWorld(EnterRequest(session), CurrentRequirement, Flow());

            Assert.That(session.State, Is.EqualTo(SessionState.CharacterSelected));
            Assert.That(session.Revision, Is.EqualTo(before));
        }

        [Test]
        public void The_same_entry_request_twice_enters_once()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            RequestId request = RequestId.New();

            EnterWorldResult first = SessionFlowService.TryEnterWorld(
                EnterRequest(session, request), CurrentRequirement, Flow());

            EnterWorldResult second = SessionFlowService.TryEnterWorld(
                EnterRequest(session, request), CurrentRequirement, Flow());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.Character, Is.EqualTo(first.Character));
            Assert.That(session.State, Is.EqualTo(SessionState.EnteringWorld),
                "the state moved once");
        }

        [Test]
        public void Entry_reports_the_character_revision_it_was_authorised_against()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            CharacterSelectEntry entry;
            Authority.TryGetCharacter(CharacterA1, out entry);

            EnterWorldResult result = SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow());

            Assert.That(result.CharacterRevision, Is.EqualTo(entry.Revision));
            Assert.That(result.SessionRevision, Is.EqualTo(session.Revision));
        }

        [Test]
        public void Too_many_entry_attempts_are_rate_limited()
        {
            Configuration = AddConfiguration(maxEnterWorldAttempts: 1, windowSeconds: 60);

            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1, 5);

            // The first attempt fails for an unrelated reason, so the session stays put.
            Authority.Maintenance = true;
            SessionFlowService.TryEnterWorld(EnterRequest(session), CurrentRequirement, Flow(5));

            EnterWorldResult second = SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow(6));

            Assert.That(second.Reason, Is.EqualTo(SessionRejection.RateLimited));
        }

        // ---- multi-account safety ------------------------------------------------------

        [Test]
        public void One_sessions_selections_do_not_leak_into_another()
        {
            AccountSessionState a = SignInAndSelect(AccountA, Server1, Channel1A, CharacterA1);
            AccountSessionState b = SignIn(AccountB);

            Assert.That(b.Server.IsValid, Is.False);
            Assert.That(b.Channel.IsValid, Is.False);
            Assert.That(b.Character.IsValid, Is.False);
            Assert.That(a.Character, Is.EqualTo(CharacterA1));
        }

        [Test]
        public void One_account_cannot_command_anothers_session()
        {
            AccountSessionState a = SignIn(AccountA);

            // Account B quotes account A's session identity.
            var forged = new SessionCommand(RequestId.New(), a.SessionId, AccountB);

            Assert.That(SessionFlowService.TrySelectServer(forged, Server1, Flow()).Reason,
                Is.EqualTo(SessionRejection.SessionInvalid));
            Assert.That(a.State, Is.EqualTo(SessionState.Authenticated));
        }

        [Test]
        public void Account_b_cannot_enter_the_world_as_account_a_s_character()
        {
            SignInAndSelect(AccountA, Server1, Channel1A, CharacterA1);

            AccountSessionState b = SignIn(AccountB);
            SessionFlowService.TrySelectServer(Command(b), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(b), Channel1A, Flow());

            // Selecting somebody else's character is refused outright.
            Assert.That(SessionFlowService.TrySelectCharacter(Command(b), CharacterA1, Flow())
                .Reason, Is.EqualTo(SessionRejection.CharacterNotOwned));

            // And a session that never reached a character cannot enter the world at all.
            var premature = new EnterWorldRequest(RequestId.New(), b.SessionId, AccountB,
                CharacterA1, Server1, Channel1A, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(premature, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.InvalidTransition));

            // With a character of its own legitimately chosen, the ownership check is what
            // catches a forged request naming somebody else's.
            SessionFlowService.TrySelectCharacter(Command(b), CharacterB1, Flow());

            var forged = new EnterWorldRequest(RequestId.New(), b.SessionId, AccountB,
                CharacterA1, Server1, Channel1A, CurrentVersions);

            Assert.That(SessionFlowService.TryEnterWorld(forged, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(SessionRejection.CharacterNotOwned));
            Assert.That(b.Character, Is.EqualTo(CharacterB1), "nothing was changed by trying");
        }

        [Test]
        public void An_account_disabled_mid_session_can_do_nothing_further()
        {
            AccountSessionState session = SignIn(AccountA);

            Authority.SetStatus(AccountA, AccountStatus.Banned);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.AccountUnavailable));
        }
    }
}
