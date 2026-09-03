using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// The account authority, described without naming a transport.
    /// </summary>
    /// <remarks>
    /// <b>This is the boundary the brief draws.</b> Everything above it -- the session state
    /// machine, the selection services, the UI -- knows only these six calls. Everything below
    /// it is somebody else's problem: Phase 15 will implement it over HTTP against PHP and
    /// MySQL, and nothing above will change.
    ///
    /// <b>No transport appears in this file.</b> No URL, no header, no status code, no
    /// <c>UnityWebRequest</c>, no connection string, no SQL. An interface that mentioned any
    /// of them would make every caller an HTTP caller.
    ///
    /// <b>No credential appears either.</b> <see cref="Authenticate"/> takes a
    /// <see cref="LoginRequest"/>, which carries no password by construction, and returns an
    /// <see cref="AuthenticatedAccount"/>. How a secret is collected, transmitted, hashed and
    /// compared is entirely below this line -- the domain is handed a conclusion and could not
    /// mishandle a secret if it wanted to, because it is never given one.
    ///
    /// <b>Synchronous on purpose.</b> The domain is engine-free and its tests are
    /// deterministic; making these return a task would push asynchrony into every service and
    /// every test for the benefit of an implementation that does not exist yet. A real
    /// implementation does its waiting on the client side of this line and calls in with what
    /// it got -- which is also what a dedicated server, already off the main thread, wants.
    /// </remarks>
    public interface IAccountApi
    {
        /// <summary>
        /// Verifies whoever is signing in and reports the account.
        /// </summary>
        /// <remarks>The credential never crosses this call. An implementation collects and
        /// verifies it on its own side and returns the account it established.</remarks>
        ApiResult<AuthenticatedAccount> Authenticate(LoginRequest request);

        /// <summary>The servers this account may see.</summary>
        /// <remarks>Filtering is the authority's, not the client's: a hidden server is simply
        /// absent, rather than present and greyed out.</remarks>
        ApiResult<IReadOnlyList<ServerInfo>> GetServers(AccountId account);

        /// <summary>The channels of one server.</summary>
        ApiResult<IReadOnlyList<ChannelInfo>> GetChannels(AccountId account, ServerId server);

        /// <summary>
        /// The characters this account owns on one server.
        /// </summary>
        /// <remarks>Scoped by account at the authority, so another account's characters are
        /// never returned rather than being returned and filtered. A filter above the boundary
        /// would already have leaked their existence.</remarks>
        ApiResult<IReadOnlyList<Data.CharacterSelectEntry>> GetCharacters(AccountId account,
            ServerId server);

        /// <summary>
        /// Confirms an account still owns a character, at the moment of asking.
        /// </summary>
        /// <remarks>Separate from <see cref="GetCharacters"/> because a list is a snapshot and
        /// ownership has to be re-established when it is acted on. This is the call a future
        /// server makes against its database rather than trusting a list it sent earlier.</remarks>
        ApiResult<bool> OwnsCharacter(AccountId account, CharacterId character);

        /// <summary>
        /// Records that a session has been handed to the world.
        /// </summary>
        /// <remarks>The persistence seam for the handoff: the authority is told, so a second
        /// login can find the session already in play. Phase 14 does not connect anything --
        /// this is the note that it was authorised.</remarks>
        ApiResult<bool> NotifyWorldEntry(AccountId account, SessionId session,
            CharacterId character, ServerId server, ChannelId channel);
    }
}
