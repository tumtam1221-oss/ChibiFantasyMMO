using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Client
{
    /// <summary>
    /// What the transport learned, offered to whoever answers the domain's questions.
    /// </summary>
    /// <remarks>
    /// <b>One direction only.</b> The controller already fetches these lists for the panels;
    /// this hands the same answers to the authority instead of fetching them a second time,
    /// and nothing flows back. An authority that did its own fetching would ask for the same
    /// server list twice per sign-in and could disagree with the list the player is looking
    /// at, which is the one thing a lobby must never do.
    ///
    /// Optional by design: an authority that has its own source of truth -- a server-side
    /// implementation, a test fixture -- simply does not implement this and is told nothing.
    /// </remarks>
    public interface ISessionCatalogueSink
    {
        /// <summary>The account the transport authenticated, with the status it reported.</summary>
        void ObserveAccount(in AuthenticatedAccount account);

        /// <summary>The servers the authority returned. Replaces what was there.</summary>
        void ObserveServers(IReadOnlyList<ServerInfo> servers);

        /// <summary>The channels of the chosen server.</summary>
        void ObserveChannels(IReadOnlyList<ChannelInfo> channels);

        /// <summary>This account's characters on the chosen server.</summary>
        void ObserveCharacters(IReadOnlyList<CharacterSelectEntry> characters);
    }

    /// <summary>
    /// The domain's view of the real account authority, backed by the real API.
    /// </summary>
    /// <remarks>
    /// <b>The last missing half of the production flow.</b>
    /// <see cref="SessionUiController"/> needs two seams: <see cref="IAccountApi"/> for the
    /// transport and <see cref="ISessionAuthority"/> for the questions the domain asks while
    /// validating a step. Only the first had a production implementation, so until now the
    /// second could only be a test fixture -- which is to say the real screens could not be
    /// pointed at the real backend at all.
    ///
    /// <b>It reports; it does not decide.</b> Every answer below is something the authority
    /// said: a server's status is the status the authority published, a character is valid
    /// because the authority listed it, and ownership is asked over the wire at the moment it
    /// matters rather than inferred from a list that arrived earlier. There is no rule here
    /// for an attacker to edit, because there is no rule here.
    ///
    /// <b>Refusing is the default.</b> Anything not in what the authority sent back is
    /// unknown, and unknown is not permission -- an unseen server, an unseen channel and an
    /// unseen character are all simply absent, which the flow service already refuses.
    ///
    /// <b>It holds no credential and no token.</b> The token belongs to the transport and
    /// stays there; this holds identifiers, statuses and populations, all of which the player
    /// is already looking at.
    /// </remarks>
    public sealed class RemoteSessionAuthority : ISessionAuthority, ISessionCatalogueSink
    {
        private readonly IAccountApi _api;

        private readonly Dictionary<ServerId, ServerInfo> _servers =
            new Dictionary<ServerId, ServerInfo>();

        private readonly Dictionary<ChannelId, ChannelInfo> _channels =
            new Dictionary<ChannelId, ChannelInfo>();

        private readonly Dictionary<CharacterId, CharacterSelectEntry> _characters =
            new Dictionary<CharacterId, CharacterSelectEntry>();

        private AuthenticatedAccount _account;

        public RemoteSessionAuthority(IAccountApi api)
        {
            _api = api;
        }

        // ---- what the transport last reported --------------------------------------------

        public void ObserveAccount(in AuthenticatedAccount account)
        {
            _account = account;
        }

        public void ObserveServers(IReadOnlyList<ServerInfo> servers)
        {
            _servers.Clear();

            if (servers == null) return;

            for (int i = 0; i < servers.Count; i++)
            {
                if (servers[i].Server.IsValid) _servers[servers[i].Server] = servers[i];
            }
        }

        public void ObserveChannels(IReadOnlyList<ChannelInfo> channels)
        {
            _channels.Clear();

            if (channels == null) return;

            for (int i = 0; i < channels.Count; i++)
            {
                if (channels[i].Channel.IsValid) _channels[channels[i].Channel] = channels[i];
            }
        }

        public void ObserveCharacters(IReadOnlyList<CharacterSelectEntry> characters)
        {
            _characters.Clear();

            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].Character.IsValid)
                {
                    _characters[characters[i].Character] = characters[i];
                }
            }
        }

        // ---- what the domain asks ---------------------------------------------------------

        /// <summary>
        /// The status the authority gave for the account it authenticated.
        /// </summary>
        /// <remarks>Any other account reads as <see cref="AccountStatus.Unknown"/>. A client
        /// knows the status of exactly one account -- its own -- and answering for a second
        /// one would mean inventing it.</remarks>
        public AccountStatus StatusOf(AccountId account)
        {
            return _account.Account.IsValid && _account.Account == account
                ? _account.Status
                : AccountStatus.Unknown;
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

        /// <summary>
        /// Asks the authority, over the wire, whether the account still owns the character.
        /// </summary>
        /// <remarks>
        /// Not a lookup in <see cref="_characters"/>, deliberately, even though that list is
        /// already scoped to this account by the authority's own SQL. A list is a snapshot;
        /// ownership has to be re-established at the moment it is acted on, and this is the
        /// one question where being a moment out of date lets somebody play a character they
        /// no longer have.
        ///
        /// A call that does not arrive is a no, not a yes. The authority re-checks ownership
        /// on its own side when the character is selected, so a client that answered
        /// optimistically here would gain nothing but a worse error message.
        /// </remarks>
        public bool OwnsCharacter(AccountId account, CharacterId character)
        {
            if (_api == null) return false;

            ApiResult<bool> owned = _api.OwnsCharacter(account, character);

            return owned.IsOk && owned.Value;
        }

        /// <summary>
        /// Whether the whole service is closed.
        /// </summary>
        /// <remarks>
        /// Always false, and that is a statement about the wire rather than about the world.
        /// The authority publishes maintenance per server and per channel -- statuses this
        /// class reports faithfully and the flow service already refuses on -- and it
        /// publishes no global flag. Deriving one here, from every server happening to be
        /// down, would be the client inventing an outage the authority never declared.
        /// </remarks>
        public bool IsUnderMaintenance()
        {
            return false;
        }
    }
}
