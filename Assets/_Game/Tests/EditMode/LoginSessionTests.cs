using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Login, sessions, the state machine and version compatibility.
    /// </summary>
    /// <remarks>
    /// The property under test throughout is that a session is a sequence, not a set of flags:
    /// every stage is reachable only from its predecessor, terminal states accept nothing, and
    /// a refused command leaves the session byte-identical.
    /// </remarks>
    [TestFixture]
    internal sealed class LoginSessionTests : SessionTestBase
    {
        // ---- login ---------------------------------------------------------------------

        [Test]
        public void A_valid_authentication_issues_a_session()
        {
            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Session.IsValid, Is.True);
            Assert.That(result.Account, Is.EqualTo(AccountA));

            AccountSessionState session;
            Assert.That(Sessions.TryGet(result.Session, out session), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
            Assert.That(session.Revision, Is.EqualTo(Revision.Initial));
        }

        [Test]
        public void An_unknown_account_is_refused_without_saying_which_part_was_wrong()
        {
            LoginResult result = SessionFlowService.TryLogin(default, NewLogin(),
                CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.InvalidCredentials),
                "a distinct reason would tell an attacker which accounts exist");
            Assert.That(Sessions.SessionCount, Is.EqualTo(0));
        }

        [Test]
        public void A_disabled_account_is_refused()
        {
            Authority.SetStatus(AccountA, AccountStatus.Disabled);

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Disabled),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.AccountDisabled));
            Assert.That(Sessions.SessionCount, Is.EqualTo(0));
        }

        [Test]
        public void A_banned_account_is_refused()
        {
            Authority.SetStatus(AccountA, AccountStatus.Banned);

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Banned),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.AccountBanned));
        }

        [Test]
        public void A_suspended_account_is_refused_distinctly_from_a_ban()
        {
            Authority.SetStatus(AccountA, AccountStatus.Suspended);

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Suspended),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.AccountSuspended));
        }

        [Test]
        public void The_authority_decides_the_account_status_not_the_caller()
        {
            // The caller claims Active; the authority says Banned. The authority wins.
            Authority.SetStatus(AccountA, AccountStatus.Banned);

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.AccountBanned));
        }

        [Test]
        public void Maintenance_refuses_a_login()
        {
            Authority.Maintenance = true;

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.Maintenance));
        }

        [Test]
        public void A_second_session_for_one_account_is_refused_by_default()
        {
            SignIn(AccountA);

            LoginResult second = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(second.Reason, Is.EqualTo(LoginRejection.SessionAlreadyActive));
            Assert.That(Sessions.SessionCount, Is.EqualTo(1));
        }

        [Test]
        public void Concurrent_sessions_are_allowed_when_content_says_so()
        {
            Configuration = AddConfiguration(allowConcurrent: true);

            SignIn(AccountA);

            LoginResult second = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                NewLogin(), CurrentRequirement, Flow());

            Assert.That(second.IsAccepted, Is.True, "the policy is authored, not written");
        }

        [Test]
        public void Two_different_accounts_may_both_be_signed_in()
        {
            Assert.That(SignIn(AccountA), Is.Not.Null);
            Assert.That(SignIn(AccountB), Is.Not.Null);
            Assert.That(Sessions.SessionCount, Is.EqualTo(2));
        }

        [Test]
        public void The_same_login_request_twice_issues_one_session()
        {
            var request = new LoginRequest(RequestId.New(), CurrentVersions);
            var account = new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active);

            LoginResult first = SessionFlowService.TryLogin(account, request, CurrentRequirement,
                Flow());

            LoginResult second = SessionFlowService.TryLogin(account, request, CurrentRequirement,
                Flow());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True, "a retry succeeds; it does not fail");
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.Session, Is.EqualTo(first.Session));
            Assert.That(Sessions.SessionCount, Is.EqualTo(1));
        }

        [Test]
        public void A_refused_login_is_re_evaluated_rather_than_cached()
        {
            Authority.Maintenance = true;

            var request = new LoginRequest(RequestId.New(), CurrentVersions);
            var account = new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active);

            Assert.That(SessionFlowService.TryLogin(account, request, CurrentRequirement, Flow())
                .Reason, Is.EqualTo(LoginRejection.Maintenance));

            Authority.Maintenance = false;

            Assert.That(SessionFlowService.TryLogin(account, request, CurrentRequirement, Flow())
                .IsAccepted, Is.True,
                "a rejection wrote nothing, so re-sending must be re-judged");
        }

        [Test]
        public void Too_many_attempts_are_rate_limited()
        {
            Configuration = AddConfiguration(maxLoginAttempts: 2, windowSeconds: 60);

            var account = new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active);

            SessionFlowService.TryLogin(account, NewLogin(), CurrentRequirement, Flow(10));
            SessionFlowService.TryLogin(account, NewLogin(), CurrentRequirement, Flow(11));

            LoginResult third = SessionFlowService.TryLogin(account, NewLogin(),
                CurrentRequirement, Flow(12));

            Assert.That(third.Reason, Is.EqualTo(LoginRejection.RateLimited));
        }

        [Test]
        public void Attempts_outside_the_window_do_not_count()
        {
            Configuration = AddConfiguration(maxLoginAttempts: 2, windowSeconds: 60);

            var account = new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active);

            SessionFlowService.TryLogin(account, NewLogin(), CurrentRequirement, Flow(0));
            SessionFlowService.TryLogin(account, NewLogin(), CurrentRequirement, Flow(1));

            // Well past the window, on caller-supplied time.
            LoginResult later = SessionFlowService.TryLogin(account, NewLogin(),
                CurrentRequirement, Flow(5000));

            Assert.That(later.Reason, Is.Not.EqualTo(LoginRejection.RateLimited));
        }

        [Test]
        public void No_credential_can_reach_the_domain()
        {
            // Structural: neither the request nor the account has anywhere to put a secret.
            System.Reflection.PropertyInfo[] onRequest = typeof(LoginRequest).GetProperties();
            System.Reflection.PropertyInfo[] onAccount =
                typeof(AuthenticatedAccount).GetProperties();

            foreach (System.Reflection.PropertyInfo property in onRequest)
            {
                AssertNotCredential(property.Name, "LoginRequest");
            }

            foreach (System.Reflection.PropertyInfo property in onAccount)
            {
                AssertNotCredential(property.Name, "AuthenticatedAccount");
            }

            foreach (System.Reflection.PropertyInfo property in
                typeof(AccountSessionState).GetProperties())
            {
                AssertNotCredential(property.Name, "AccountSessionState");
            }
        }

        private static void AssertNotCredential(string name, string owner)
        {
            string lower = name.ToLowerInvariant();

            Assert.That(lower.Contains("password"), Is.False, owner + "." + name);
            Assert.That(lower.Contains("secret"), Is.False, owner + "." + name);
            Assert.That(lower.Contains("hash"), Is.False, owner + "." + name);
            Assert.That(lower.Contains("salt"), Is.False, owner + "." + name);
        }

        // ---- version compatibility -----------------------------------------------------

        [Test]
        public void Matching_versions_are_compatible()
        {
            VersionCompatibilityResult result = VersionPolicy.Evaluate(CurrentVersions,
                CurrentRequirement);

            Assert.That(result.Compatibility, Is.EqualTo(VersionCompatibility.Compatible));
            Assert.That(result.IsPlayable, Is.True);
        }

        [Test]
        public void A_client_below_the_floor_needs_a_patch()
        {
            var old = new VersionSet(new VersionNumber(0, 9, 0), new VersionNumber(3),
                new VersionNumber(1, 5, 0));

            VersionCompatibilityResult result = VersionPolicy.Evaluate(old, CurrentRequirement);

            Assert.That(result.Compatibility, Is.EqualTo(VersionCompatibility.RequiredUpdate));
            Assert.That(result.Kind, Is.EqualTo(VersionKind.Client));
            Assert.That(result.Expected, Is.EqualTo(new VersionNumber(1, 0, 0)),
                "a launcher is told what to fetch");
            Assert.That(result.IsPlayable, Is.False);
        }

        [Test]
        public void A_client_behind_the_latest_but_above_the_floor_is_offered_an_update()
        {
            var slightlyOld = new VersionSet(new VersionNumber(1, 1, 0), new VersionNumber(3),
                new VersionNumber(1, 5, 0));

            VersionCompatibilityResult result = VersionPolicy.Evaluate(slightlyOld,
                CurrentRequirement);

            Assert.That(result.Compatibility, Is.EqualTo(VersionCompatibility.OptionalUpdate));
            Assert.That(result.IsPlayable, Is.True, "an optional update does not block play");
        }

        [Test]
        public void A_protocol_mismatch_is_incompatible_rather_than_patchable()
        {
            var wrongProtocol = new VersionSet(new VersionNumber(1, 2, 0),
                new VersionNumber(2), new VersionNumber(1, 5, 0));

            VersionCompatibilityResult result = VersionPolicy.Evaluate(wrongProtocol,
                CurrentRequirement);

            Assert.That(result.Compatibility, Is.EqualTo(VersionCompatibility.Incompatible));
            Assert.That(result.Kind, Is.EqualTo(VersionKind.Protocol));
        }

        [Test]
        public void A_newer_protocol_is_also_incompatible()
        {
            var ahead = new VersionSet(new VersionNumber(1, 2, 0), new VersionNumber(4),
                new VersionNumber(1, 5, 0));

            Assert.That(VersionPolicy.Evaluate(ahead, CurrentRequirement).Compatibility,
                Is.EqualTo(VersionCompatibility.Incompatible),
                "protocol is an exact contract, not a floor");
        }

        [Test]
        public void Mandatory_content_below_the_floor_needs_a_patch()
        {
            var staleContent = new VersionSet(new VersionNumber(1, 2, 0), new VersionNumber(3),
                new VersionNumber(0, 9, 0));

            Assert.That(VersionPolicy.Evaluate(staleContent, CurrentRequirement).Compatibility,
                Is.EqualTo(VersionCompatibility.RequiredUpdate));
        }

        [Test]
        public void Advisory_content_below_the_floor_is_only_a_nudge()
        {
            var advisory = new VersionRequirement(new VersionNumber(1, 0, 0),
                new VersionNumber(1, 2, 0), new VersionNumber(3), new VersionNumber(1, 0, 0),
                new VersionNumber(1, 5, 0), contentIsAdvisory: true);

            var staleContent = new VersionSet(new VersionNumber(1, 2, 0), new VersionNumber(3),
                new VersionNumber(0, 9, 0));

            VersionCompatibilityResult result = VersionPolicy.Evaluate(staleContent, advisory);

            Assert.That(result.Compatibility, Is.EqualTo(VersionCompatibility.OptionalUpdate),
                "whether stale content blocks play is authored, not written");
            Assert.That(result.IsPlayable, Is.True);
        }

        [Test]
        public void A_stale_client_cannot_log_in()
        {
            var old = new VersionSet(new VersionNumber(0, 9, 0), new VersionNumber(3),
                new VersionNumber(1, 5, 0));

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                new LoginRequest(RequestId.New(), old), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.ClientVersionMismatch));
            Assert.That(result.Compatibility.Expected, Is.EqualTo(new VersionNumber(1, 0, 0)));
        }

        [Test]
        public void A_protocol_mismatch_reports_its_own_reason()
        {
            var wrongProtocol = new VersionSet(new VersionNumber(1, 2, 0), new VersionNumber(2),
                new VersionNumber(1, 5, 0));

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(AccountA, "Player A", AccountStatus.Active),
                new LoginRequest(RequestId.New(), wrongProtocol), CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(LoginRejection.ProtocolVersionMismatch));
        }

        [Test]
        public void No_version_number_is_written_into_the_policy()
        {
            foreach (string code in CodeLines(
                "Assets/_Game/Scripts/Contracts/VersionContracts.cs"))
            {
                if (!code.Contains("VersionNumber(")) continue;

                // Constructing one from parameters is fine; constructing a literal is not.
                Assert.That(code, Does.Not.Contain("new VersionNumber(1"), code);
                Assert.That(code, Does.Not.Contain("new VersionNumber(2"), code);
            }
        }

        // ---- state machine -------------------------------------------------------------

        [Test]
        public void A_new_session_is_authenticated_and_nothing_more()
        {
            AccountSessionState session = SignIn(AccountA);

            Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
            Assert.That(session.Server.IsValid, Is.False);
            Assert.That(session.Channel.IsValid, Is.False);
            Assert.That(session.Character.IsValid, Is.False);
        }

        [Test]
        public void Selecting_a_channel_before_a_server_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);

            SessionResult result = SessionFlowService.TrySelectChannel(Command(session),
                Channel1A, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.InvalidTransition));
            Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
        }

        [Test]
        public void Selecting_a_character_before_a_channel_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            SessionResult result = SessionFlowService.TrySelectCharacter(Command(session),
                CharacterA1, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.InvalidTransition));
            Assert.That(session.State, Is.EqualTo(SessionState.ServerSelected));
        }

        [Test]
        public void Entering_the_world_before_choosing_a_character_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());

            EnterWorldResult result = SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.InvalidTransition));
            Assert.That(session.State, Is.EqualTo(SessionState.ChannelSelected));
        }

        [Test]
        public void The_full_sequence_reaches_the_world()
        {
            AccountSessionState session = SignIn(AccountA);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .IsAccepted, Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.ServerSelected));

            Assert.That(SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow())
                .IsAccepted, Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.ChannelSelected));

            Assert.That(SessionFlowService.TrySelectCharacter(Command(session), CharacterA1,
                Flow()).IsAccepted, Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.CharacterSelected));

            Assert.That(SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow()).IsAccepted, Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.EnteringWorld));
        }

        [Test]
        public void Choosing_a_different_server_clears_what_was_beneath_it()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            SessionFlowService.TrySelectServer(Command(session), Server2, Flow());

            Assert.That(session.Server, Is.EqualTo(Server2));
            Assert.That(session.Channel.IsValid, Is.False,
                "a channel of the old server is meaningless under the new one");
            Assert.That(session.Character.IsValid, Is.False);
            Assert.That(session.State, Is.EqualTo(SessionState.ServerSelected));
        }

        [Test]
        public void An_expired_session_refuses_every_command()
        {
            Configuration = AddConfiguration(lifetimeSeconds: 100);

            AccountSessionState session = SignIn(AccountA, 0);

            SessionResult result = SessionFlowService.TrySelectServer(Command(session), Server1,
                Flow(500));

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.SessionExpired));
            Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
        }

        [Test]
        public void A_session_within_its_lifetime_still_works()
        {
            Configuration = AddConfiguration(lifetimeSeconds: 100);

            AccountSessionState session = SignIn(AccountA, 0);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow(50))
                .IsAccepted, Is.True);
        }

        [Test]
        public void A_revoked_session_refuses_every_command()
        {
            AccountSessionState session = SignIn(AccountA);

            Sessions.Revoke(session.SessionId);

            Assert.That(SessionFlowService.TrySelectServer(Command(session), Server1, Flow())
                .Reason, Is.EqualTo(SessionRejection.SessionRevoked));
            Assert.That(session.State, Is.EqualTo(SessionState.Revoked));
        }

        [Test]
        public void A_terminal_session_can_never_come_back()
        {
            AccountSessionState session = SignIn(AccountA);
            session.Revoke();

            Assert.That(session.TrySelectServer(Server1), Is.False);
            Assert.That(session.TryBeginWorldEntry(), Is.False);
            Assert.That(session.Expire(), Is.False, "revoked does not become expired");
            Assert.That(session.State, Is.EqualTo(SessionState.Revoked));
        }

        [Test]
        public void Every_transition_out_of_a_terminal_state_is_refused()
        {
            SessionState[] all =
            {
                SessionState.Unauthenticated, SessionState.Authenticated,
                SessionState.ServerSelected, SessionState.ChannelSelected,
                SessionState.CharacterSelected, SessionState.EnteringWorld,
                SessionState.Active, SessionState.Expired, SessionState.Revoked
            };

            foreach (SessionState to in all)
            {
                Assert.That(AccountSessionState.CanTransitionTo(SessionState.Expired, to),
                    Is.False, "expired -> " + to);

                Assert.That(AccountSessionState.CanTransitionTo(SessionState.Revoked, to),
                    Is.False, "revoked -> " + to);
            }
        }

        [Test]
        public void The_authority_may_end_a_session_from_any_live_state()
        {
            SessionState[] live =
            {
                SessionState.Authenticated, SessionState.ServerSelected,
                SessionState.ChannelSelected, SessionState.CharacterSelected,
                SessionState.EnteringWorld, SessionState.Active
            };

            foreach (SessionState from in live)
            {
                Assert.That(AccountSessionState.CanTransitionTo(from, SessionState.Revoked),
                    Is.True, from + " -> revoked");

                Assert.That(AccountSessionState.CanTransitionTo(from, SessionState.Expired),
                    Is.True, from + " -> expired");
            }
        }

        [Test]
        public void Entering_the_world_twice_is_refused()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            SessionFlowService.TryEnterWorld(EnterRequest(session), CurrentRequirement, Flow());

            EnterWorldResult again = SessionFlowService.TryEnterWorld(EnterRequest(session),
                CurrentRequirement, Flow());

            Assert.That(again.Reason, Is.EqualTo(SessionRejection.AlreadyInWorld));
        }

        // ---- revision ------------------------------------------------------------------

        [Test]
        public void Each_accepted_selection_advances_the_revision_exactly_once()
        {
            AccountSessionState session = SignIn(AccountA);

            Revision start = session.Revision;

            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());
            Assert.That(session.Revision.Value, Is.EqualTo(start.Value + 1));

            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());
            Assert.That(session.Revision.Value, Is.EqualTo(start.Value + 2));

            SessionFlowService.TrySelectCharacter(Command(session), CharacterA1, Flow());
            Assert.That(session.Revision.Value, Is.EqualTo(start.Value + 3));
        }

        [Test]
        public void A_refused_command_advances_no_revision()
        {
            AccountSessionState session = SignIn(AccountA);
            Revision before = session.Revision;

            SessionFlowService.TrySelectChannel(Command(session), Channel1A, Flow());
            SessionFlowService.TrySelectCharacter(Command(session), CharacterA1, Flow());
            SessionFlowService.TrySelectServer(Command(session), new ServerId("server:gone"),
                Flow());

            Assert.That(session.Revision, Is.EqualTo(before));
        }

        [Test]
        public void Reading_a_list_advances_no_revision()
        {
            AccountSessionState session = SignInAndSelect(AccountA, Server1, Channel1A,
                CharacterA1);

            Revision before = session.Revision;

            var into = new System.Collections.Generic.List<ServerInfo>();
            SessionFlowService.SelectableServers(
                Authority.GetServers(AccountA).Value, session, Flow(), into);

            SessionFlowService.CanSelectServer(session, default, Flow());

            Assert.That(session.Revision, Is.EqualTo(before), "a query must not mutate");
        }

        [Test]
        public void Selecting_the_same_server_again_is_not_a_mutation()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Revision after = session.Revision;

            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            Assert.That(session.Revision, Is.EqualTo(after));
        }

        [Test]
        public void A_stale_revision_is_refused()
        {
            AccountSessionState session = SignIn(AccountA);
            Revision stale = session.Revision;

            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            SessionResult result = SessionFlowService.TrySelectChannel(
                Command(session, stale), Channel1A, Flow());

            Assert.That(result.Reason, Is.EqualTo(SessionRejection.StaleRevision));
        }

        [Test]
        public void A_current_revision_is_accepted()
        {
            AccountSessionState session = SignIn(AccountA);
            SessionFlowService.TrySelectServer(Command(session), Server1, Flow());

            SessionResult result = SessionFlowService.TrySelectChannel(
                Command(session, session.Revision), Channel1A, Flow());

            Assert.That(result.IsAccepted, Is.True);
        }

        [Test]
        public void The_same_selection_request_twice_selects_once()
        {
            AccountSessionState session = SignIn(AccountA);

            var command = new SessionCommand(RequestId.New(), session.SessionId,
                session.Account);

            SessionResult first = SessionFlowService.TrySelectServer(command, Server1, Flow());
            SessionResult second = SessionFlowService.TrySelectServer(command, Server1, Flow());

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsReplay, Is.True);
            Assert.That(second.Revision, Is.EqualTo(first.Revision));
            Assert.That(session.Revision, Is.EqualTo(first.Revision), "applied once");
        }
    }
}
