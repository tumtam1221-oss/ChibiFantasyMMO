using System;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Network;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// The door: no connection may say anything to this server until the account API has
    /// said who it is.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately thin.</b> Every decision belongs to
    /// <see cref="WorldEntryCoordinator"/>, which is pure and exhaustively tested. This
    /// class does three things -- receive a broadcast, ask the coordinator, report the
    /// answer to FishNet -- because a rule implemented inside a MonoBehaviour is a rule that
    /// can only be checked by standing up a transport.
    ///
    /// <b>The one broadcast it accepts before authentication.</b> FishNet's
    /// <c>requireAuthentication: false</c> is what lets a join request through, and it is
    /// used exactly once. Everything else on this server requires authentication, so an
    /// unauthenticated connection can send precisely one kind of message and nothing else
    /// reaches any handler.
    ///
    /// <b>A refusal is answered and then dropped.</b> The client is told why -- with the
    /// same <see cref="SessionRejection"/> vocabulary it saw at character select -- and then
    /// the connection is failed. Failing silently would leave a player staring at a
    /// progress bar with no idea that their session had expired.
    ///
    /// <b>Nothing here is logged.</b> The join request contains a session token. There is no
    /// logging call in this file, which is the only reliable way to ensure one is never
    /// written to a server log.
    /// </remarks>
    public sealed class WorldAuthenticator : Authenticator
    {
        /// <summary>
        /// FishNet's contract: the server subscribes to this and acts on the result.
        /// </summary>
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        private WorldEntryCoordinator _coordinator;

        /// <summary>Raised on the server after a connection is admitted, before it spawns.</summary>
        /// <remarks>Carries the coordinator's outcome so the bootstrap can act on the
        /// authority's identities rather than re-deriving them from a message.</remarks>
        public event Action<NetworkConnection, WorldJoinOutcome> OnAdmitted;

        /// <summary>
        /// Supplies the decision-maker.
        /// </summary>
        /// <remarks>Injected rather than constructed here, because building it would mean
        /// this MonoBehaviour knowing how to reach the account API -- which is exactly the
        /// dependency rule 16.14 forbids the server from having.</remarks>
        public void UseCoordinator(WorldEntryCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);

            networkManager.ServerManager.RegisterBroadcast<WorldJoinRequestMessage>(
                OnJoinRequest, requireAuthentication: false);
        }

        /// <summary>
        /// A connection asking to be let in.
        /// </summary>
        /// <remarks>
        /// The claim is built from the message and handed on unexamined. Nothing here reads
        /// an account or a character out of it and acts on it -- the coordinator compares
        /// them against the authority and this method never learns what they were.
        /// </remarks>
        private void OnJoinRequest(NetworkConnection connection, WorldJoinRequestMessage message,
            Channel channel)
        {
            if (_coordinator == null)
            {
                // A server that cannot ask the authority must not guess. Refusing every
                // connection is the correct behaviour for a misconfigured server, and a
                // loud one -- nobody gets in until it is wired properly.
                Reject(connection, SessionRejection.MissingContext);

                return;
            }

            var claim = new WorldJoinClaim(
                new SessionToken(message.Token),
                new VersionSet(
                    Parse(message.ClientVersion),
                    Parse(message.ProtocolVersion),
                    Parse(message.ContentVersion)),
                new AccountId(message.ClaimedAccountId),
                new CharacterId(message.ClaimedCharacterId),
                new ServerId(message.ClaimedServerId),
                new ChannelId(message.ClaimedChannelId));

            WorldJoinOutcome outcome = _coordinator.Join(connection.ClientId, claim);

            if (!outcome.IsAccepted)
            {
                Reject(connection, outcome.Reason);

                return;
            }

            // A displaced connection is disconnected before the new one is confirmed, so
            // there is never a moment when two sockets both believe they own the character.
            if (outcome.DisplacedConnectionId >= 0
                && NetworkManager.ServerManager.Clients.TryGetValue(
                    outcome.DisplacedConnectionId, out NetworkConnection older))
            {
                older.Disconnect(immediately: false);
            }

            NetworkManager.ServerManager.Broadcast(connection, new WorldJoinResponseMessage
            {
                Admitted = true,
                Rejection = (int)SessionRejection.None,
                SessionId = outcome.Admission.Session.Value,
                AccountId = outcome.Admission.Account.Value,
                CharacterId = outcome.Admission.Character.Value,
                ServerId = outcome.Admission.Server.Value,
                ChannelId = outcome.Admission.Channel.Value,
                EntryState = (int)WorldEntryState.Connecting,
            }, requireAuthenticated: false);

            OnAdmitted?.Invoke(connection, outcome);
            OnAuthenticationResult?.Invoke(connection, true);
        }

        private void Reject(NetworkConnection connection, SessionRejection reason)
        {
            NetworkManager.ServerManager.Broadcast(connection, new WorldJoinResponseMessage
            {
                Admitted = false,
                Rejection = (int)reason,
                EntryState = (int)WorldEntryState.None,
            }, requireAuthenticated: false);

            // False fails the connection, and FishNet disconnects it. No character is
            // spawned because nothing downstream ever runs -- rule 16.5's last line is
            // structural, not remembered.
            OnAuthenticationResult?.Invoke(connection, false);
        }

        /// <summary>
        /// Reads a dotted version, or zero.
        /// </summary>
        /// <remarks>An unparseable version becomes 0.0.0, which fails any requirement with
        /// a floor. That is the right default: a client whose version cannot be read is not
        /// given the benefit of the doubt.</remarks>
        private static VersionNumber Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return default;

            string[] parts = value.Split('.');

            int major = Part(parts, 0);
            int minor = Part(parts, 1);
            int patch = Part(parts, 2);

            return new VersionNumber(major, minor, patch);
        }

        private static int Part(string[] parts, int index)
        {
            return index < parts.Length
                && int.TryParse(parts[index], out int value)
                ? value
                : 0;
        }
    }
}
