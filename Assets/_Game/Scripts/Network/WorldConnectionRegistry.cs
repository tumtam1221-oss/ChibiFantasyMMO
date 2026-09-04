using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;

namespace ChibiFantasy.Network
{
    /// <summary>What the registry did with a connection.</summary>
    /// <remarks>
    /// Four outcomes rather than a boolean, because "already connected" is not a failure
    /// and "replaced somebody" is not an ordinary success. A caller has to be able to tell
    /// them apart: one of them means another socket needs closing.
    /// </remarks>
    public enum ConnectionOutcome
    {
        /// <summary>A new connection was registered.</summary>
        Registered = 0,

        /// <summary>This exact connection was already registered. Nothing changed.</summary>
        AlreadyRegistered = 1,

        /// <summary>An older connection for this session was displaced by this one.</summary>
        Replaced = 2,

        /// <summary>Refused: the session or character is held by a live connection.</summary>
        Refused = 3
    }

    /// <summary>
    /// Who is connected to this world server, and what that means for their character.
    /// </summary>
    /// <remarks>
    /// <b>Pure, on purpose.</b> No FishNet type appears here and no engine type either: a
    /// connection is an integer, because that is all FishNet's <c>ClientId</c> is and
    /// because the rules being enforced -- one character in one world, a stale socket
    /// cannot act, a disconnect observed twice does nothing the second time -- are rules
    /// about identity, not about sockets. Keeping them here means they can be tested
    /// exhaustively without standing up a transport, which is the difference between
    /// "we think reconnection is safe" and knowing it.
    ///
    /// <b>The duplicate-connection policy is replacement, and that is a decision.</b> When
    /// a session that is already connected connects again, the newer socket wins and the
    /// older is reported for disconnection. The alternative -- refusing the new one -- locks
    /// a player out of their own character every time their network drops without a clean
    /// close, which is the common case and not the rare one. Refusal is reserved for the
    /// genuinely dangerous collision: a <i>different</i> session trying to take a character
    /// somebody is holding.
    ///
    /// <b>Stale connections cannot act.</b> A displaced connection is not merely removed
    /// from the map; it is remembered as stale, so a message still in flight from it is
    /// refused rather than applied to a character its replacement now controls. Removing it
    /// silently would let the last packet of a dead socket move somebody's character.
    /// </remarks>
    public sealed class WorldConnectionRegistry
    {
        /// <summary>One connected session.</summary>
        public readonly struct Entry
        {
            public Entry(int connectionId, SessionId session, AccountId account,
                CharacterId character, ServerId server, ChannelId channel,
                WorldPresence presence)
            {
                ConnectionId = connectionId;
                Session = session;
                Account = account;
                Character = character;
                Server = server;
                Channel = channel;
                Presence = presence;
            }

            public int ConnectionId { get; }

            public SessionId Session { get; }

            public AccountId Account { get; }

            public CharacterId Character { get; }

            public ServerId Server { get; }

            public ChannelId Channel { get; }

            public WorldPresence Presence { get; }

            public Entry WithPresence(WorldPresence presence)
            {
                return new Entry(ConnectionId, Session, Account, Character, Server, Channel,
                    presence);
            }

            public bool IsValid => Session.IsValid;
        }

        private readonly Dictionary<int, Entry> _byConnection = new Dictionary<int, Entry>();
        private readonly Dictionary<string, int> _bySession = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _byCharacter = new Dictionary<string, int>();

        /// <summary>
        /// Connections that were displaced and must not be acted on again.
        /// </summary>
        /// <remarks>Kept rather than forgotten, so a message from a socket that lost its
        /// place is refused with a reason instead of silently treated as unknown -- which
        /// reads identically to a message from a client that never connected, and is a
        /// different bug.</remarks>
        private readonly HashSet<int> _stale = new HashSet<int>();

        public int Count => _byConnection.Count;

        /// <summary>Connections displaced by a newer one, awaiting disconnection.</summary>
        public IReadOnlyCollection<int> Stale => _stale;

        /// <summary>
        /// Registers an admitted connection.
        /// </summary>
        /// <param name="displaced">The older connection this replaced, or -1.</param>
        /// <remarks>
        /// Takes an <see cref="WorldAdmission"/> rather than loose identifiers, so nothing
        /// can be registered that the authority did not admit. There is no overload that
        /// accepts an account id, which is what makes "the client supplied its own identity"
        /// unrepresentable rather than merely refused.
        /// </remarks>
        public ConnectionOutcome Register(int connectionId, in WorldAdmission admission,
            out int displaced)
        {
            displaced = -1;

            if (!admission.IsAdmitted || connectionId < 0)
            {
                return ConnectionOutcome.Refused;
            }

            string session = admission.Session.Value;
            string character = admission.Character.Value;

            if (_byConnection.TryGetValue(connectionId, out Entry existing))
            {
                // The same socket claiming the same session twice: a retry, not a conflict.
                return existing.Session == admission.Session
                    ? ConnectionOutcome.AlreadyRegistered
                    : ConnectionOutcome.Refused;
            }

            // A different session holding this character is the dangerous collision, and
            // the only one refused outright. Two sessions cannot play one character.
            if (_byCharacter.TryGetValue(character, out int holder)
                && _byConnection.TryGetValue(holder, out Entry held)
                && held.Session != admission.Session)
            {
                return ConnectionOutcome.Refused;
            }

            ConnectionOutcome outcome = ConnectionOutcome.Registered;

            if (_bySession.TryGetValue(session, out int older) && older != connectionId)
            {
                // The same session reconnecting. The newer socket wins; the older is
                // remembered as stale so anything still in flight from it is refused.
                Forget(older, markStale: true);

                displaced = older;
                outcome = ConnectionOutcome.Replaced;
            }

            _byConnection[connectionId] = new Entry(connectionId, admission.Session,
                admission.Account, admission.Character, admission.Server, admission.Channel,
                WorldPresence.Connecting);

            _bySession[session] = connectionId;

            if (!string.IsNullOrEmpty(character)) _byCharacter[character] = connectionId;

            // A connection id can be reused by the transport after the old one is gone, so
            // registering it clears any stale mark it inherited.
            _stale.Remove(connectionId);

            return outcome;
        }

        /// <summary>Marks a registered connection as having arrived in the world.</summary>
        /// <remarks>Presence moves from Connecting to InWorld only here, so a connection
        /// that was admitted and then failed to load never reads as present.</remarks>
        public bool MarkInWorld(int connectionId)
        {
            if (!_byConnection.TryGetValue(connectionId, out Entry entry)) return false;

            if (entry.Presence == WorldPresence.InWorld) return false;

            _byConnection[connectionId] = entry.WithPresence(WorldPresence.InWorld);

            return true;
        }

        /// <summary>
        /// Removes a connection.
        /// </summary>
        /// <remarks>
        /// <b>Idempotent, which is the whole requirement of rule 16.10.</b> A disconnect can
        /// be observed more than once -- a callback, a timeout and a shutdown can all fire
        /// for the same socket -- and the second observation must do nothing. It returns
        /// false rather than throwing, so a caller that does not check is still correct.
        ///
        /// A stale connection removes cleanly too: it was already displaced, so there is
        /// nothing left to undo.
        /// </remarks>
        public bool Unregister(int connectionId, out Entry removed)
        {
            _stale.Remove(connectionId);

            if (!_byConnection.TryGetValue(connectionId, out removed))
            {
                removed = default;

                return false;
            }

            Forget(connectionId, markStale: false);

            return true;
        }

        /// <summary>Whether a connection may act -- registered, and not displaced.</summary>
        /// <remarks>The check that stops a stale socket controlling a character its
        /// replacement now owns.</remarks>
        public bool CanAct(int connectionId)
        {
            return !_stale.Contains(connectionId) && _byConnection.ContainsKey(connectionId);
        }

        public bool IsStale(int connectionId) => _stale.Contains(connectionId);

        public bool TryGet(int connectionId, out Entry entry)
        {
            return _byConnection.TryGetValue(connectionId, out entry);
        }

        public bool TryGetBySession(SessionId session, out Entry entry)
        {
            entry = default;

            return session.IsValid
                && _bySession.TryGetValue(session.Value, out int id)
                && _byConnection.TryGetValue(id, out entry);
        }

        /// <summary>
        /// Where a character is, as this server knows it.
        /// </summary>
        /// <remarks>Derived from an actual connection, never from a timer or a guess. A
        /// character nobody is connected as is Offline because there is no connection, not
        /// because something decided it looked idle.</remarks>
        public WorldPresence PresenceOf(CharacterId character)
        {
            if (!character.IsValid) return WorldPresence.Offline;

            if (!_byCharacter.TryGetValue(character.Value, out int id)) return WorldPresence.Offline;

            return _byConnection.TryGetValue(id, out Entry entry)
                ? entry.Presence
                : WorldPresence.Offline;
        }

        /// <summary>Every connection, for a shutdown that has to release them all.</summary>
        public IReadOnlyList<Entry> All()
        {
            var all = new List<Entry>(_byConnection.Count);

            foreach (KeyValuePair<int, Entry> pair in _byConnection) all.Add(pair.Value);

            return all;
        }

        /// <summary>Empties the registry, reporting what was in it.</summary>
        /// <remarks>A server shutting down has to release every session it was holding, or
        /// every player it had is locked out until their session expires. Returning the
        /// entries rather than releasing them here keeps this type free of the authority.</remarks>
        public IReadOnlyList<Entry> Clear()
        {
            IReadOnlyList<Entry> all = All();

            _byConnection.Clear();
            _bySession.Clear();
            _byCharacter.Clear();
            _stale.Clear();

            return all;
        }

        private void Forget(int connectionId, bool markStale)
        {
            if (!_byConnection.TryGetValue(connectionId, out Entry entry)) return;

            _byConnection.Remove(connectionId);

            // Only remove the reverse mappings if they still point at this connection. A
            // replacement has already claimed them, and clearing them would unregister the
            // live connection while removing the dead one.
            if (_bySession.TryGetValue(entry.Session.Value, out int session)
                && session == connectionId)
            {
                _bySession.Remove(entry.Session.Value);
            }

            if (!string.IsNullOrEmpty(entry.Character.Value)
                && _byCharacter.TryGetValue(entry.Character.Value, out int character)
                && character == connectionId)
            {
                _byCharacter.Remove(entry.Character.Value);
            }

            if (markStale) _stale.Add(connectionId);
        }
    }
}
