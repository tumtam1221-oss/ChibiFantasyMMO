using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// A request that one combatant wants to strike another.
    /// </summary>
    /// <remarks>
    /// <b>A wish, not an event.</b> Constructing one evaluates nothing, validates nothing
    /// and changes nothing; it is the input to
    /// <see cref="BasicAttackExecutor.Execute"/> and has no behaviour of its own. That
    /// separation is what lets the same request be validated on a client for responsiveness
    /// and re-validated on a server for authority, without the act of describing it having
    /// already applied damage somewhere.
    ///
    /// <b>No weapon type.</b> There is no Sword, Staff, Bow or Mace here and no enum that
    /// would need one adding. <see cref="AttackDefinition"/> is an optional
    /// <see cref="DefinitionId"/> naming authored content, which is how the rest of the
    /// project already refers to content, and it is <see cref="DefinitionId.None"/> for a
    /// plain unarmed swing. Combat never interprets it in this phase.
    ///
    /// <b><see cref="Sequence"/> is the caller's, not ours.</b> It exists so a future
    /// server can discard a replayed or out-of-order request. Nothing in this phase reads
    /// it, and nothing here mints one, because a number minted client-side would be worth
    /// exactly nothing to an authority that does not trust the client.
    /// </remarks>
    public readonly struct AttackIntent
    {
        public AttackIntent(ICombatant attacker, ICombatant target,
            DefinitionId attackDefinition = default, int sequence = 0)
        {
            Attacker = attacker;
            Target = target;
            AttackDefinition = attackDefinition;
            Sequence = sequence;
        }

        /// <summary>Who wants to attack. May be null; validation reports that rather than throwing.</summary>
        public ICombatant Attacker { get; }

        /// <summary>Who they want to attack. May be null; validation reports that rather than throwing.</summary>
        public ICombatant Target { get; }

        /// <summary>
        /// Optional authored content behind the swing.
        /// </summary>
        /// <remarks><see cref="DefinitionId.None"/> means a generic basic attack, which is
        /// a complete and legal request rather than a missing one.</remarks>
        public DefinitionId AttackDefinition { get; }

        /// <summary>Caller-supplied ordering number. Unused by combat in this phase.</summary>
        public int Sequence { get; }

        /// <summary>
        /// Whether both participants are present.
        /// </summary>
        /// <remarks>A shallow structural check only. It says nothing about whether the
        /// attack is legal; that is <see cref="TargetEvaluator"/>'s job, and this exists so
        /// a caller can cheaply discard a malformed request before building rules.</remarks>
        public bool IsStructurallyComplete => Attacker != null && Target != null;

        public override string ToString()
        {
            string attacker = Attacker == null ? "<none>" : Attacker.CombatantId.ToString();
            string target = Target == null ? "<none>" : Target.CombatantId.ToString();
            return attacker + " -> " + target
                + (AttackDefinition.IsValid ? " [" + AttackDefinition + "]" : " [basic]");
        }
    }
}
