using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A deterministic stand-in for the account authority.
    /// </summary>
    /// <remarks>
    /// <b>Test infrastructure, and unmistakably so.</b> It lives in the test assembly, it is
    /// internal, and it does nothing a transport does: no HTTP, no sockets, no localhost, no
    /// URL, no retry, no serialisation. It is a dictionary that answers questions, which is
    /// all a test needs and nothing a production implementation could be confused with.
    ///
    /// <b>It verifies no credential, because it is never given one.</b>
    /// <see cref="Authenticate"/> receives a <see cref="LoginRequest"/>, which has no room for
    /// a password, and returns whichever account the fixture told it to. That is exactly the
    /// shape a real implementation has from the domain's side -- the verification happens
    /// below the seam, wherever the seam is implemented.
    ///
    /// It implements both seams at once: <see cref="IAccountApi"/> for the client's transport
    /// and <see cref="ISessionAuthority"/> for the domain's questions. Production keeps them
    /// apart; a fixture has no reason to.
    /// </remarks>
    internal sealed class FakeAccountAuthority : IAccountApi, ISessionAuthority
    {
        private readonly Dictionary<AccountId, AuthenticatedAccount> _accounts =
            new Dictionary<AccountId, AuthenticatedAccount>();

        private readonly Dictionary<ServerId, ServerInfo> _servers =
            new Dictionary<ServerId, ServerInfo>();

        private readonly Dictionary<ChannelId, ChannelInfo> _channels =
            new Dictionary<ChannelId, ChannelInfo>();

        private readonly Dictionary<CharacterId, CharacterSelectEntry> _characters =
            new Dictionary<CharacterId, CharacterSelectEntry>();

        private readonly Dictionary<CharacterId, AccountId> _ownership =
            new Dictionary<CharacterId, AccountId>();

        /// <summary>Which account the next authentication resolves to.</summary>
        public AccountId NextAuthenticated { get; set; }

        /// <summary>Set to fail the next authentication at the transport layer.</summary>
        public ApiErrorKind AuthenticateFailsWith { get; set; }

        public bool Maintenance { get; set; }

        /// <summary>How many times world entry was reported. Proves the seam is called once.</summary>
        public int WorldEntryNotifications { get; private set; }

        public void AddAccount(AuthenticatedAccount account)
        {
            _accounts[account.Account] = account;
        }

        public void SetStatus(AccountId account, AccountStatus status)
        {
            AuthenticatedAccount existing;
            if (!_accounts.TryGetValue(account, out existing)) return;

            _accounts[account] = new AuthenticatedAccount(account, existing.DisplayName, status);
        }

        public void AddServer(ServerInfo server) => _servers[server.Server] = server;

        public void AddChannel(ChannelInfo channel) => _channels[channel.Channel] = channel;

        public void AddCharacter(CharacterSelectEntry entry, AccountId owner)
        {
            _characters[entry.Character] = entry;
            _ownership[entry.Character] = owner;
        }

        public void SetCharacterAvailability(CharacterId character,
            CharacterAvailability availability)
        {
            CharacterSelectEntry existing;
            if (!_characters.TryGetValue(character, out existing)) return;

            _characters[character] = new CharacterSelectEntry(existing.Character, existing.Name,
                existing.Gender, existing.Level, existing.Class, existing.Job, existing.Map,
                existing.Appearance, availability, existing.LastPlayedTicks, existing.Revision);
        }

        // ---- IAccountApi ---------------------------------------------------------------

        public ApiResult<AuthenticatedAccount> Authenticate(LoginRequest request)
        {
            if (AuthenticateFailsWith != ApiErrorKind.None)
            {
                return ApiResult<AuthenticatedAccount>.Failed(AuthenticateFailsWith);
            }

            AuthenticatedAccount account;
            if (!_accounts.TryGetValue(NextAuthenticated, out account))
            {
                return ApiResult<AuthenticatedAccount>.Ok(default);
            }

            return ApiResult<AuthenticatedAccount>.Ok(account);
        }

        public ApiResult<IReadOnlyList<ServerInfo>> GetServers(AccountId account)
        {
            var visible = new List<ServerInfo>();

            foreach (KeyValuePair<ServerId, ServerInfo> pair in _servers)
            {
                // A hidden server is absent, never present and greyed out.
                if (pair.Value.Status == ServerStatus.Hidden) continue;
                visible.Add(pair.Value);
            }

            return ApiResult<IReadOnlyList<ServerInfo>>.Ok(visible);
        }

        public ApiResult<IReadOnlyList<ChannelInfo>> GetChannels(AccountId account,
            ServerId server)
        {
            var owned = new List<ChannelInfo>();

            foreach (KeyValuePair<ChannelId, ChannelInfo> pair in _channels)
            {
                if (pair.Value.Server != server) continue;
                owned.Add(pair.Value);
            }

            return ApiResult<IReadOnlyList<ChannelInfo>>.Ok(owned);
        }

        public ApiResult<IReadOnlyList<CharacterSelectEntry>> GetCharacters(AccountId account,
            ServerId server)
        {
            var owned = new List<CharacterSelectEntry>();

            foreach (KeyValuePair<CharacterId, AccountId> pair in _ownership)
            {
                // Scoped at the authority: another account's characters are never returned.
                if (pair.Value != account) continue;

                CharacterSelectEntry entry;
                if (_characters.TryGetValue(pair.Key, out entry)) owned.Add(entry);
            }

            return ApiResult<IReadOnlyList<CharacterSelectEntry>>.Ok(owned);
        }

        public ApiResult<bool> OwnsCharacter(AccountId account, CharacterId character)
        {
            return ApiResult<bool>.Ok(OwnsCharacterCore(account, character));
        }

        public ApiResult<bool> NotifyWorldEntry(AccountId account, SessionId session,
            CharacterId character, ServerId server, ChannelId channel)
        {
            WorldEntryNotifications++;
            return ApiResult<bool>.Ok(true);
        }

        // ---- ISessionAuthority ---------------------------------------------------------

        public AccountStatus StatusOf(AccountId account)
        {
            AuthenticatedAccount known;
            return _accounts.TryGetValue(account, out known) ? known.Status : AccountStatus.Unknown;
        }

        public bool TryGetServer(ServerId server, out ServerInfo info)
        {
            return _servers.TryGetValue(server, out info);
        }

        public bool TryGetChannel(ChannelId channel, out ChannelInfo info)
        {
            return _channels.TryGetValue(channel, out info);
        }

        public bool TryGetCharacter(CharacterId character, out CharacterSelectEntry entry)
        {
            return _characters.TryGetValue(character, out entry);
        }

        bool ISessionAuthority.OwnsCharacter(AccountId account, CharacterId character)
        {
            return OwnsCharacterCore(account, character);
        }

        public bool IsUnderMaintenance() => Maintenance;

        private bool OwnsCharacterCore(AccountId account, CharacterId character)
        {
            AccountId owner;
            return _ownership.TryGetValue(character, out owner) && owner == account;
        }
    }

    /// <summary>
    /// Accounts, servers, channels and characters for the session tests.
    /// </summary>
    /// <remarks>
    /// <b>Two accounts, two servers, three channels, three characters.</b> Deliberately more
    /// than any one test needs, because most of what this phase promises is about telling them
    /// apart: account B's character must be invisible to account A, and channel 1 of server B
    /// must be unreachable from server A. A single-account fixture could not fail those.
    ///
    /// Every limit is authored on a <see cref="SessionConfiguration"/>, exactly as an operator
    /// would set it, so a test asserting against a hard-coded number would be testing the
    /// wrong thing.
    /// </remarks>
    internal abstract class SessionTestBase
    {
        protected FakeAccountAuthority Authority;
        protected SessionDirectory Sessions;
        protected SessionConfiguration Configuration;

        private List<Object> _created;

        protected AccountId AccountA;
        protected AccountId AccountB;

        protected ServerId Server1;
        protected ServerId Server2;

        protected ChannelId Channel1A;   // on server 1, PK off
        protected ChannelId Channel2A;   // on server 1, PK on
        protected ChannelId Channel1B;   // on server 2

        protected CharacterId CharacterA1;
        protected CharacterId CharacterA2;
        protected CharacterId CharacterB1;

        protected const string TownMap = "map.town";

        /// <summary>The versions the fixture's client reports.</summary>
        protected VersionSet CurrentVersions;

        /// <summary>What the fixture's authority demands.</summary>
        protected VersionRequirement CurrentRequirement;

        [SetUp]
        public void SetUpSessionFixtures()
        {
            _created = new List<Object>();

            Authority = new FakeAccountAuthority();
            Sessions = new SessionDirectory();
            Configuration = AddConfiguration();

            AccountA = new AccountId("account:a");
            AccountB = new AccountId("account:b");

            Authority.AddAccount(new AuthenticatedAccount(AccountA, "Player A",
                AccountStatus.Active));
            Authority.AddAccount(new AuthenticatedAccount(AccountB, "Player B",
                AccountStatus.Active));

            CurrentVersions = new VersionSet(new VersionNumber(1, 2, 0),
                new VersionNumber(3), new VersionNumber(1, 5, 0));

            CurrentRequirement = new VersionRequirement(new VersionNumber(1, 0, 0),
                new VersionNumber(1, 2, 0), new VersionNumber(3), new VersionNumber(1, 0, 0),
                new VersionNumber(1, 5, 0));

            Server1 = new ServerId("server:1");
            Server2 = new ServerId("server:2");

            Authority.AddServer(NewServer(Server1, "Aurora", ServerStatus.Online,
                PopulationReading.Known(120, 1000)));

            Authority.AddServer(NewServer(Server2, "Borealis", ServerStatus.Online,
                PopulationReading.Unknown(1000)));

            Channel1A = new ChannelId("channel:1a");
            Channel2A = new ChannelId("channel:2a");
            Channel1B = new ChannelId("channel:1b");

            Authority.AddChannel(NewChannel(Channel1A, Server1, ChannelStatus.Online,
                PopulationReading.Known(40, 200)));

            Authority.AddChannel(NewChannel(Channel2A, Server1, ChannelStatus.Online,
                PopulationReading.Known(10, 200), pkEnabled: true));

            Authority.AddChannel(NewChannel(Channel1B, Server2, ChannelStatus.Online,
                PopulationReading.Unknown(200)));

            CharacterA1 = new CharacterId("char:a1");
            CharacterA2 = new CharacterId("char:a2");
            CharacterB1 = new CharacterId("char:b1");

            Authority.AddCharacter(NewCharacter(CharacterA1, "Ayla", CharacterGender.Female, 25),
                AccountA);

            Authority.AddCharacter(NewCharacter(CharacterA2, "Aren", CharacterGender.Male, 3),
                AccountA);

            Authority.AddCharacter(NewCharacter(CharacterB1, "Bryn", CharacterGender.Female, 40),
                AccountB);
        }

        [TearDown]
        public void TearDownSessionFixtures()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        // ---- authoring -----------------------------------------------------------------

        protected SessionConfiguration AddConfiguration(int characterSlots = 5,
            int lifetimeSeconds = 0, int maxLoginAttempts = 0, int windowSeconds = 60,
            int maxEnterWorldAttempts = 0, bool allowConcurrent = false)
        {
            var definition = ScriptableObject.CreateInstance<SessionConfiguration>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"config.session\"},"
                + "\"_maxCharacterSlots\":" + characterSlots
                + ",\"_sessionLifetimeSeconds\":" + lifetimeSeconds
                + ",\"_maxLoginAttempts\":" + maxLoginAttempts
                + ",\"_loginAttemptWindowSeconds\":" + windowSeconds
                + ",\"_maxEnterWorldAttempts\":" + maxEnterWorldAttempts
                + ",\"_allowConcurrentSessions\":" + (allowConcurrent ? "true" : "false")
                + "}", definition);

            _created.Add(definition);
            return definition;
        }

        protected ServerInfo NewServer(ServerId id, string region, ServerStatus status,
            PopulationReading population, bool enabled = true,
            VersionRequirement? versions = null)
        {
            return new ServerInfo(id, new LocalizationKey(id.Value + ".name"), region, status,
                population, versions ?? CurrentRequirement, enabled);
        }

        protected ChannelInfo NewChannel(ChannelId id, ServerId server, ChannelStatus status,
            PopulationReading population, bool pkEnabled = false, bool enabled = true)
        {
            return new ChannelInfo(id, server, new LocalizationKey(id.Value + ".name"), status,
                population, pkEnabled, enabled);
        }

        protected CharacterSelectEntry NewCharacter(CharacterId id, string name,
            CharacterGender gender, int level,
            CharacterAvailability availability = CharacterAvailability.Playable)
        {
            return new CharacterSelectEntry(id, name, gender, level,
                new DefinitionId("class.novice"), default, new DefinitionId(TownMap), default,
                availability);
        }

        // ---- convenience ---------------------------------------------------------------

        protected SessionFlowService.Context Flow(long ticks = 0L)
        {
            return new SessionFlowService.Context(Authority, Sessions, Configuration, ticks);
        }

        protected LoginRequest NewLogin()
        {
            return new LoginRequest(RequestId.New(), CurrentVersions);
        }

        /// <summary>Signs an account in through the service and returns its session.</summary>
        protected AccountSessionState SignIn(AccountId account, long ticks = 0L)
        {
            Authority.NextAuthenticated = account;

            var request = new LoginRequest(RequestId.New(), CurrentVersions);

            LoginResult result = SessionFlowService.TryLogin(
                new AuthenticatedAccount(account, "Player", AccountStatus.Active), request,
                CurrentRequirement, Flow(ticks));

            AccountSessionState session;
            Sessions.TryGet(result.Session, out session);
            return session;
        }

        /// <summary>Drives a session as far as a chosen character, through the service.</summary>
        protected AccountSessionState SignInAndSelect(AccountId account, ServerId server,
            ChannelId channel, CharacterId character, long ticks = 0L)
        {
            AccountSessionState session = SignIn(account, ticks);

            SessionFlowService.TrySelectServer(Command(session), server, Flow(ticks));
            SessionFlowService.TrySelectChannel(Command(session), channel, Flow(ticks));
            SessionFlowService.TrySelectCharacter(Command(session), character, Flow(ticks));

            return session;
        }

        protected SessionCommand Command(AccountSessionState session,
            Revision? expectedRevision = null)
        {
            return new SessionCommand(RequestId.New(), session.SessionId, session.Account,
                expectedRevision);
        }

        protected EnterWorldRequest EnterRequest(AccountSessionState session,
            RequestId? request = null)
        {
            return new EnterWorldRequest(request ?? RequestId.New(), session.SessionId,
                session.Account, session.Character, session.Server, session.Channel,
                CurrentVersions);
        }

        /// <summary>A file's lines with the comments removed.</summary>
        /// <remarks>Prose may name a forbidden word while explaining why the code avoids it;
        /// asserting over raw text would check the documentation instead of the code.</remarks>
        internal static IEnumerable<string> CodeLines(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;

                yield return code;
            }
        }
    }
}
