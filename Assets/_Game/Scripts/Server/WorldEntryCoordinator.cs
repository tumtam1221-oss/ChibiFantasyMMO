using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// What happened when a connection tried to join the world.
    /// </summary>
    /// <remarks>
    /// Carries the admission rather than repeating it, so there is one copy of the
    /// authority's answer and no chance of a caller reading a stale duplicate.
    /// </remarks>
    public readonly struct WorldJoinOutcome
    {
        private WorldJoinOutcome(bool accepted, SessionRejection reason, WorldAdmission admission,
            ConnectionOutcome connection, int displaced, WorldEntryState entryState)
        {
            IsAccepted = accepted;
            Reason = reason;
            Admission = admission;
            Connection = connection;
            DisplacedConnectionId = displaced;
            EntryState = entryState;
        }

        public bool IsAccepted { get; }

        public SessionRejection Reason { get; }

        /// <summary>The authority's answer. Meaningless unless accepted.</summary>
        public WorldAdmission Admission { get; }

        public ConnectionOutcome Connection { get; }

        /// <summary>An older connection that must now be disconnected, or -1.</summary>
        public int DisplacedConnectionId { get; }

        public WorldEntryState EntryState { get; }

        public static WorldJoinOutcome Accepted(in WorldAdmission admission,
            ConnectionOutcome connection, int displaced)
        {
            return new WorldJoinOutcome(true, SessionRejection.None, admission, connection,
                displaced, WorldEntryState.Connecting);
        }

        public static WorldJoinOutcome Refused(SessionRejection reason)
        {
            return new WorldJoinOutcome(false, reason, WorldAdmission.Refused(reason),
                ConnectionOutcome.Refused, -1, WorldEntryState.None);
        }

        public override string ToString()
        {
            return IsAccepted ? "joined: " + Admission : "refused: " + Reason;
        }
    }

    /// <summary>
    /// Everything that has to be true before a character exists in the world, and the
    /// order it becomes true in.
    /// </summary>
    /// <remarks>
    /// <b>Phase 14 ended at Authorised. This is the rest of the sentence.</b> The states
    /// were declared then and left unused --
    /// <see cref="WorldEntryState.Connecting"/> and <see cref="WorldEntryState.Ready"/> --
    /// so that the phase which actually connected something would not have to change a
    /// contract. It does not.
    ///
    /// <b>Pure, and that is what makes it testable.</b> No FishNet type, no
    /// <c>UnityEngine</c> type, no clock and no transport: a connection is an integer, the
    /// authority is an interface, and time arrives as an argument. Every rule in 16.5,
    /// 16.6, 16.9, 16.10, 16.11 and 16.16 is decided here, which means every one of them is
    /// exercised by an ordinary EditMode test rather than by standing up two processes and
    /// hoping.
    ///
    /// <b>Order matters, and it is not arbitrary.</b> The protocol is checked before the
    /// session, because a client that cannot be spoken to cannot be told anything useful
    /// about its session. The authority is asked before the registry is touched, because
    /// registering first would give an unauthorised connection a place in the world for as
    /// long as the check took. And the character is spawned only after the registry has
    /// accepted it, because two connections racing for one character must both be resolved
    /// before either exists.
    ///
    /// <b>It never spawns anything itself.</b> It says what should be spawned and where;
    /// creating objects is the FishNet layer's job. That separation is why this file has no
    /// engine reference and why the rule "a rejected connection must not spawn a character"
    /// is structural rather than remembered -- a refusal returns no spawn to act on.
    /// </remarks>
    public sealed class WorldEntryCoordinator
    {
        private readonly IWorldSessionAuthority _authority;
        private readonly WorldConnectionRegistry _registry;
        private readonly VersionRequirement _required;

        public WorldEntryCoordinator(IWorldSessionAuthority authority,
            WorldConnectionRegistry registry, VersionRequirement required = default)
        {
            _authority = authority;
            _registry = registry;
            _required = required;
        }

        public WorldConnectionRegistry Registry => _registry;

        /// <summary>
        /// Decides whether a connection may join, and records it if so.
        /// </summary>
        /// <remarks>
        /// <b>Nothing the client sent is read as an answer.</b> The token is resolved; the
        /// claims are compared. Rules 16.5 and 16.16 A through E are all the same rule seen
        /// from five angles, and this is where it is enforced -- inside
        /// <see cref="IWorldSessionAuthority.Admit"/> for the comparison, and here for the
        /// consequences.
        /// </remarks>
        public WorldJoinOutcome Join(int connectionId, in WorldJoinClaim claim)
        {
            if (_authority == null || _registry == null)
            {
                return WorldJoinOutcome.Refused(SessionRejection.MissingContext);
            }

            if (connectionId < 0 || !claim.HasToken)
            {
                return WorldJoinOutcome.Refused(SessionRejection.MissingContext);
            }

            // Version before anything else: an incompatible protocol cannot be reasoned
            // with, and telling such a client its session is fine would be misleading.
            VersionCompatibilityResult compatibility =
                VersionPolicy.Evaluate(claim.Versions, _required);

            if (!compatibility.IsPlayable)
            {
                return WorldJoinOutcome.Refused(SessionRejection.VersionMismatch);
            }

            WorldAdmission admission = _authority.Admit(claim);

            if (!admission.IsAdmitted)
            {
                return WorldJoinOutcome.Refused(admission.Reason);
            }

            if (!admission.HasCharacter)
            {
                return WorldJoinOutcome.Refused(SessionRejection.UnknownCharacter);
            }

            ConnectionOutcome outcome = _registry.Register(connectionId, admission,
                out int displaced);

            if (outcome == ConnectionOutcome.Refused)
            {
                // Another live session holds this character. Refusing is the only answer
                // that cannot produce two authoritative copies of one character.
                return WorldJoinOutcome.Refused(SessionRejection.AlreadyInWorld);
            }

            return WorldJoinOutcome.Accepted(admission, outcome, displaced);
        }

        /// <summary>
        /// Resolves where an admitted character stands.
        /// </summary>
        /// <remarks>
        /// <b>From authored definitions, never from the client and never from a literal.</b>
        /// The map comes from the character's own row and the spawn point from Phase 11's
        /// <see cref="TravelService.FindPlayerSpawn"/>, which is the same resolution the
        /// travel system uses -- so there is one answer to "where does a character appear on
        /// this map", not two that can drift.
        ///
        /// Returns null when the map is unknown or has no player spawn. A caller that
        /// cannot place a character must refuse the entry rather than invent an origin,
        /// which is why this cannot fall back to a default position.
        /// </remarks>
        public SpawnPointDefinition ResolveSpawn(in WorldAdmission admission,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints)
        {
            if (!admission.IsAdmitted || !admission.Map.IsValid) return null;

            return TravelService.FindPlayerSpawn(admission.Map, spawnPoints);
        }

        /// <summary>
        /// Records that the character reached the world: Connecting becomes Ready, and the
        /// authority's session becomes Active.
        /// </summary>
        /// <remarks>
        /// Two things move together and neither is assumed from the other. The registry
        /// learns the character is present; the authority learns the session is live. A
        /// server that told the authority and not the registry would have a session nobody
        /// could find, and one that told the registry only would leave the session stuck in
        /// EnteringWorld -- which is, correctly, what happens when this is never called.
        /// </remarks>
        public bool ConfirmArrival(int connectionId)
        {
            if (!_registry.CanAct(connectionId)) return false;

            if (!_registry.TryGet(connectionId, out WorldConnectionRegistry.Entry entry))
            {
                return false;
            }

            if (!_registry.MarkInWorld(connectionId)) return false;

            return _authority.ConfirmArrival(entry.Session);
        }

        /// <summary>
        /// Handles a connection going away.
        /// </summary>
        /// <remarks>
        /// <b>Idempotent, and it has to be.</b> A disconnect callback, a timeout and a
        /// shutdown can all fire for the same socket. The second call finds nothing
        /// registered and returns false having done nothing -- it does not release a session
        /// a reconnection has since started, which is the corruption rule 16.10 is about.
        ///
        /// <b>A displaced connection releases nothing.</b> When a session reconnects, the
        /// old socket's disconnect arrives afterwards; releasing the session then would
        /// throw away the session the new socket is using. So a stale connection is dropped
        /// quietly, and only a connection that still owns its session releases it.
        /// </remarks>
        public bool Leave(int connectionId)
        {
            bool stale = _registry.IsStale(connectionId);

            if (!_registry.Unregister(connectionId, out WorldConnectionRegistry.Entry entry))
            {
                return false;
            }

            if (stale)
            {
                // Its session belongs to the connection that replaced it. Ending it here
                // would disconnect the player who just successfully reconnected.
                return false;
            }

            return _authority.Release(entry.Session);
        }

        /// <summary>
        /// Releases every connection, for a server that is stopping.
        /// </summary>
        /// <remarks>Without this, every player on a server that shuts down is locked out of
        /// their own account until their session expires, and every character they were
        /// playing stays marked InWorld in a world that no longer exists.</remarks>
        public int ReleaseAll()
        {
            int released = 0;

            foreach (WorldConnectionRegistry.Entry entry in _registry.Clear())
            {
                if (_authority.Release(entry.Session)) released++;
            }

            return released;
        }

        /// <summary>Where a character is, as this server knows it.</summary>
        public WorldPresence PresenceOf(CharacterId character)
        {
            return _registry.PresenceOf(character);
        }

        /// <summary>Whether a connection is entitled to act on its character.</summary>
        /// <remarks>The check a message handler makes before applying anything. A stale
        /// socket fails it, which is rule 16.16 M.</remarks>
        public bool CanAct(int connectionId)
        {
            return _registry.CanAct(connectionId);
        }
    }
}
