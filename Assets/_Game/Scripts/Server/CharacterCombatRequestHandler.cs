using ChibiFantasy.Core;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Turns a client's network request into a run of the server's combat pipeline.
    /// </summary>
    /// <remarks>
    /// <b>The join, and only the join.</b> The message arrived through
    /// <c>CharacterNetworkEntity</c>; the fight is <see cref="ServerCombatPipeline"/>'s,
    /// which is 18.1's and unchanged. This is what stands between them, and it adds no rule
    /// of its own -- every refusal below this line is one the pipeline already had.
    ///
    /// <b>It builds the command; the client does not.</b> A <c>CombatCommand</c> has a field
    /// for the attacker a client thinks it is, and this deliberately leaves it empty: the
    /// connection is the identity, the pipeline resolves the character from it, and a value
    /// there could only ever be compared and rejected. Passing nothing is stronger than
    /// passing something that has to be checked.
    ///
    /// <b>Replication is driven after the fight, not by it.</b> A result the client cannot
    /// see is a result that did not happen as far as the player is concerned, so a caller
    /// supplies what to do afterwards -- normally synchronising both replication services,
    /// which is exactly what the server's own tick does.
    /// </remarks>
    public sealed class CharacterCombatRequestHandler : ICharacterCombatRequestSink
    {
        private readonly ServerCombatPipeline _pipeline;
        private readonly System.Action _afterCombat;

        /// <param name="pipeline">18.1's pipeline. The only thing that resolves a fight.</param>
        /// <param name="afterCombat">
        /// Run once a request has been handled, accepted or not. Normally a synchronise of
        /// the character and monster replication services, so what the server decided
        /// reaches the client that asked.
        /// </param>
        public CharacterCombatRequestHandler(ServerCombatPipeline pipeline,
            System.Action afterCombat = null)
        {
            _pipeline = pipeline;
            _afterCombat = afterCombat;
        }

        /// <summary>How many requests have been handled. For diagnostics and tests.</summary>
        public int Handled { get; private set; }

        /// <summary>
        /// What the last request produced.
        /// </summary>
        /// <remarks>Not sent anywhere. A client learns what happened from replicated state,
        /// which cannot be forged; this exists so a server operator and a test can see the
        /// outcome without one being invented for them.</remarks>
        public ServerCombatResult LastResult { get; private set; }

        public void Submit(int connectionId, InstanceId target, DefinitionId skill, int rank,
            long sequence)
        {
            Handled++;

            if (_pipeline == null)
            {
                LastResult = ServerCombatResult.Refused(CombatCommandRejection.NoCharacter);

                return;
            }

            // ClaimedAttacker is left empty on purpose. See the type remarks.
            LastResult = _pipeline.Execute(connectionId,
                new CombatCommand(default, target, skill, rank, sequence));

            _afterCombat?.Invoke();
        }
    }
}
