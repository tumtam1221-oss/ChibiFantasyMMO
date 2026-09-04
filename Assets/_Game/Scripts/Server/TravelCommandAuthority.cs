using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>Why a travel request never reached the travel rules.</summary>
    /// <remarks>
    /// Resolution failures only. Level requirements, portal distance, required items and
    /// destination validity are <see cref="TravelRejection"/>'s, decided by Phase 11's
    /// <see cref="TravelService"/> and deliberately not restated here.
    /// </remarks>
    public enum TravelCommandRejection
    {
        None = 0,

        /// <summary>The connection has no character in this world.</summary>
        NoCharacter = 1,

        /// <summary>The connection has been displaced and may no longer act.</summary>
        StaleConnection = 2,

        /// <summary>The message named a character this connection does not control.</summary>
        NotYourCharacter = 3,

        /// <summary>Neither a portal nor a destination was named.</summary>
        Malformed = 4,

        /// <summary>The character is dead and cannot travel.</summary>
        NotAlive = 5,

        /// <summary>An older or replayed request.</summary>
        OutOfOrder = 6
    }

    /// <summary>
    /// A client asking to travel.
    /// </summary>
    /// <remarks>
    /// <b>There is no position here, and that is the point.</b> A client names a portal it
    /// wants to walk through or a town it wants to warp to. It cannot name coordinates,
    /// cannot name a map directly without a portal or a warp that reaches it, and cannot ask
    /// to arrive at a particular spot — the destination spawn is resolved from authored
    /// content. That is rule 17.9's "no client teleport authority", enforced by the message
    /// having nowhere to express one.
    /// </remarks>
    public readonly struct TravelCommand
    {
        public TravelCommand(CharacterId claimedCharacter, DefinitionId portal,
            DefinitionId destinationMap, DefinitionId destinationSpawn, long sequence)
        {
            ClaimedCharacter = claimedCharacter;
            Portal = portal;
            DestinationMap = destinationMap;
            DestinationSpawn = destinationSpawn;
            Sequence = sequence;
        }

        /// <summary>Who the client thinks it is. Compared, never believed.</summary>
        public CharacterId ClaimedCharacter { get; }

        /// <summary>The portal to walk through, if this is a portal traversal.</summary>
        public DefinitionId Portal { get; }

        /// <summary>The map to warp to, if this is a warp.</summary>
        public DefinitionId DestinationMap { get; }

        /// <summary>The authored spawn to warp to, if this is a warp.</summary>
        public DefinitionId DestinationSpawn { get; }

        public long Sequence { get; }

        public bool IsPortal => Portal.IsValid;

        public bool IsWarp => !Portal.IsValid && DestinationMap.IsValid
            && DestinationSpawn.IsValid;

        public override string ToString()
        {
            return IsPortal ? "portal " + Portal : "warp " + DestinationSpawn;
        }
    }

    /// <summary>What a travel request produced.</summary>
    public readonly struct TravelCommandResult
    {
        private TravelCommandResult(bool accepted, TravelCommandRejection rejection,
            TravelResult travel, DefinitionId map, DefinitionId spawn)
        {
            IsAccepted = accepted;
            Rejection = rejection;
            Travel = travel;
            Map = map;
            Spawn = spawn;
        }

        public bool IsAccepted { get; }

        /// <summary>Why it never reached the rules. None when it did.</summary>
        public TravelCommandRejection Rejection { get; }

        /// <summary>What Phase 11 decided, when it was asked.</summary>
        public TravelResult Travel { get; }

        /// <summary>Where the character now is. Authoritative.</summary>
        public DefinitionId Map { get; }

        public DefinitionId Spawn { get; }

        public static TravelCommandResult Travelled(in TravelResult travel, DefinitionId map,
            DefinitionId spawn)
        {
            return new TravelCommandResult(true, TravelCommandRejection.None, travel, map, spawn);
        }

        /// <summary>The rules were asked and said no.</summary>
        public static TravelCommandResult RefusedByRules(in TravelResult travel,
            DefinitionId map, DefinitionId spawn)
        {
            return new TravelCommandResult(false, TravelCommandRejection.None, travel, map, spawn);
        }

        /// <summary>It never reached the rules.</summary>
        public static TravelCommandResult Refused(TravelCommandRejection rejection,
            DefinitionId map = default, DefinitionId spawn = default)
        {
            return new TravelCommandResult(false, rejection, default, map, spawn);
        }

        public override string ToString()
        {
            if (IsAccepted) return "travelled to " + Map;

            return Rejection == TravelCommandRejection.None
                ? "refused: " + Travel.Reason
                : "refused: " + Rejection;
        }
    }

    /// <summary>
    /// Turns a client's travel request into a journey the existing rules decide.
    /// </summary>
    /// <remarks>
    /// <b>It resolves; Phase 11 decides.</b> Source map, destination map, spawn validity,
    /// level requirement, required item, portal distance and town/field/boss restrictions
    /// are all <see cref="TravelService"/>'s, with twelve typed rejection reasons already.
    /// Restating any of them here would create a second set of travel rules that disagrees
    /// with the first the moment either is authored differently.
    ///
    /// <b>A refused journey moves nobody.</b> <see cref="TravelService"/> updates the
    /// location only on success, so a refusal leaves the character exactly where it was and
    /// the result reports that — a client cannot travel by being ignored.
    ///
    /// <b>A successful journey marks the character dirty.</b> Where somebody is standing is
    /// worth persisting; that it happened through a portal is not, so nothing is written
    /// here and the next lifecycle save carries it.
    /// </remarks>
    public sealed class TravelCommandAuthority
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly CanActPredicate _canAct;

        /// <summary>Whether a connection is still entitled to act.</summary>
        public delegate bool CanActPredicate(int connectionId);

        public TravelCommandAuthority(WorldCharacterRegistry characters, CanActPredicate canAct)
        {
            _characters = characters;
            _canAct = canAct;
        }

        /// <summary>
        /// Validates a travel request and, if the rules agree, moves the character.
        /// </summary>
        /// <param name="connectionId">Who asked.</param>
        /// <param name="command">What they asked for.</param>
        /// <param name="context">Phase 11's registries, inventory and level.</param>
        /// <param name="isAlive">Whether the character may travel at all.</param>
        public TravelCommandResult Execute(int connectionId, in TravelCommand command,
            in TravelService.Context context, bool isAlive = true)
        {
            if (_characters == null)
            {
                return TravelCommandResult.Refused(TravelCommandRejection.NoCharacter);
            }

            if (_canAct != null && !_canAct(connectionId))
            {
                return TravelCommandResult.Refused(TravelCommandRejection.StaleConnection);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter living))
            {
                return TravelCommandResult.Refused(TravelCommandRejection.NoCharacter);
            }

            DefinitionId currentMap = living.Location.CurrentMap;
            DefinitionId currentSpawn = living.Location.CurrentSpawnPoint;

            if (command.Sequence <= living.LastTravelSequence)
            {
                return TravelCommandResult.Refused(TravelCommandRejection.OutOfOrder,
                    currentMap, currentSpawn);
            }

            if (command.ClaimedCharacter.IsValid
                && command.ClaimedCharacter != living.Character)
            {
                return TravelCommandResult.Refused(TravelCommandRejection.NotYourCharacter,
                    currentMap, currentSpawn);
            }

            if (!isAlive)
            {
                return TravelCommandResult.Refused(TravelCommandRejection.NotAlive,
                    currentMap, currentSpawn);
            }

            if (!command.IsPortal && !command.IsWarp)
            {
                return TravelCommandResult.Refused(TravelCommandRejection.Malformed,
                    currentMap, currentSpawn);
            }

            // From here it is Phase 11's decision, and its own location update.
            TravelResult travel = command.IsPortal
                ? TravelService.TryTraversePortal(living.Location, command.Portal, context)
                // requireTown: true, and it is not optional. A client-initiated warp is a
                // town warp; without this a client could name any authored spawn id and
                // arrive in a field or a boss area, which is precisely the restriction
                // Phase 11 built the flag for.
                : TravelService.TryTravelToSpawn(living.Location, command.DestinationMap,
                    command.DestinationSpawn, context, requireTown: true);

            living.LastTravelSequence = command.Sequence;

            if (!travel.IsAccepted)
            {
                // The location is untouched, so reporting it tells a client the truth.
                return TravelCommandResult.RefusedByRules(travel, currentMap, currentSpawn);
            }

            // Where somebody is standing is worth persisting. Nothing is written here --
            // the next lifecycle save carries it.
            living.MarkDirty();

            // Arriving somewhere new invalidates the movement stream.
            living.ResetMovementStream();

            return TravelCommandResult.Travelled(travel, living.Location.CurrentMap,
                living.Location.CurrentSpawnPoint);
        }
    }
}
