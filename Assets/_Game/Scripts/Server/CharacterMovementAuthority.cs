using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Runs a client's movement input against the authoritative world.
    /// </summary>
    /// <remarks>
    /// <b>The identity is the connection, exactly as combat does it.</b> A request arrives
    /// through a character's own network object, the object's owner names the connection,
    /// and the character is looked up from that. There is no character id in the message and
    /// nothing here reads one, so a client cannot move somebody else by editing a field that
    /// does not exist.
    ///
    /// <b>Every number in the step is the server's.</b> Speed comes from authored content,
    /// the map radius from the map, and the elapsed time from this class's own clock --
    /// advanced by <see cref="Tick"/> from the server loop, never from a client's
    /// <c>Time.deltaTime</c>. What the client supplies is two axes and a sequence.
    ///
    /// <b>Nothing new decides whether a move is legal.</b>
    /// <see cref="CharacterMovementSimulator"/> computes the destination and
    /// <c>MovementValidator</c> -- which already existed and is unchanged -- accepts or
    /// refuses it. A refusal leaves the character exactly where it was.
    ///
    /// <b>Not a second movement system.</b> The travel authority still owns changing maps
    /// and the monster runtime still owns monster movement; this owns one thing, which is a
    /// player walking inside the map they are already on.
    /// </remarks>
    public sealed class CharacterMovementAuthority : ICharacterMovementRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly CombatCommandAuthority.WorldConnectionRegistryAdapter _connections;
        private readonly IDefinitionRegistry<MapDefinition> _maps;
        private readonly float _metresPerSecond;
        private readonly float _toleranceFactor;
        private readonly long _maxStepMilliseconds;

        /// <summary>
        /// The server's own clock, in milliseconds.
        /// </summary>
        /// <remarks>Advanced by <see cref="Tick"/> rather than read from anywhere. That is
        /// what makes "the client cannot supply delta time" structural: there is no clock
        /// here a client can reach, and a test advances it by hand.</remarks>
        private long _nowMilliseconds;

        /// <param name="characters">The authoritative registry. The only source of a character.</param>
        /// <param name="canAct">
        /// Whether a connection is still entitled to act. The same narrowed question the
        /// combat authority takes, so a displaced socket cannot move the character its
        /// replacement now controls.
        /// </param>
        /// <param name="maps">Authored maps, read only for their movement radius.</param>
        /// <param name="metresPerSecond">
        /// Authored walking speed. Supplied once, from content -- a client has no field for
        /// it and this class holds no literal.
        /// </param>
        /// <param name="toleranceFactor">Jitter headroom for the existing distance check.</param>
        /// <param name="maxStepMilliseconds">
        /// The most time a single accepted request may account for. Without it, a client that
        /// waits a minute and then presses forward would be entitled to a minute of walking
        /// in one step.
        /// </param>
        public CharacterMovementAuthority(WorldCharacterRegistry characters,
            CombatCommandAuthority.WorldConnectionRegistryAdapter canAct,
            IDefinitionRegistry<MapDefinition> maps, float metresPerSecond,
            float toleranceFactor = 1.25f, long maxStepMilliseconds = 250L)
        {
            _characters = characters;
            _connections = canAct;
            _maps = maps;
            _metresPerSecond = metresPerSecond;
            _toleranceFactor = toleranceFactor;
            _maxStepMilliseconds = maxStepMilliseconds < 1L ? 1L : maxStepMilliseconds;
        }

        /// <summary>How many requests have been handled, accepted or not.</summary>
        public int Handled { get; private set; }

        /// <summary>What the last request produced. Diagnostics, never sent anywhere.</summary>
        public MovementResult LastResult { get; private set; }

        /// <summary>The server clock these steps are measured against.</summary>
        public long NowMilliseconds => _nowMilliseconds;

        /// <summary>
        /// Advances the server's movement clock.
        /// </summary>
        /// <remarks>Time arrives as an argument, matching every other service in this
        /// project. Called from the server's loop; nothing here reads a frame time.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            _nowMilliseconds += (long)(deltaSeconds * 1000f);
        }

        /// <summary>
        /// Moves the character belonging to a connection, if the world allows it.
        /// </summary>
        /// <remarks>
        /// The cheap identity checks run first, so a flood from a stale connection costs
        /// nothing and never reaches the simulation.
        /// </remarks>
        public void Submit(int connectionId, float inputX, float inputZ, long sequence)
        {
            Handled++;

            if (_characters == null)
            {
                LastResult = MovementResult.Rejected(MovementRejection.MissingContext,
                    default);

                return;
            }

            if (_connections != null && !_connections(connectionId))
            {
                LastResult = MovementResult.Rejected(MovementRejection.NotInWorld, default);

                return;
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter character))
            {
                // No character on this connection: a disconnected player, or one that never
                // entered the world.
                LastResult = MovementResult.Rejected(MovementRejection.MissingContext,
                    default);

                return;
            }

            MovementResult result = CharacterMovementSimulator.Advance(
                new CharacterMovementIntent(inputX, inputZ, sequence),
                character.Location,
                BudgetFor(character),
                character.LastMovementSequence,
                character.LastMovementTimestamp,
                _nowMilliseconds,
                character.Combatant == null || character.Combatant.IsAlive());

            LastResult = result;

            if (!result.IsAccepted) return;

            // One call, so the sequence cannot advance without the clock. It also copies the
            // new position onto the combatant, which is what makes combat range and monster
            // AI see where the player actually is.
            character.RecordMovement(sequence, _nowMilliseconds);
        }

        /// <summary>
        /// What this character is entitled to on the map they are standing on.
        /// </summary>
        /// <remarks>Built per request rather than cached, because a character can travel and
        /// a cached radius would be the previous map's. The speed is the server's authored
        /// figure; the bounds are the map's.</remarks>
        private MovementBudget BudgetFor(LivingCharacter character)
        {
            MapDefinition map = null;

            if (_maps != null && character.Location != null)
            {
                _maps.TryGet(character.Location.CurrentMap, out map);
            }

            return MovementValidator.BudgetFor(_metresPerSecond, map, _toleranceFactor,
                _maxStepMilliseconds);
        }
    }
}
