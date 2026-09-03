namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Runs one basic attack: validate, calculate, apply, report.
    /// </summary>
    /// <remarks>
    /// <b>The only place damage is applied.</b> Every other combat type in this phase
    /// answers a question; this one is the single writer, which is what makes "did anything
    /// mutate" a question with one place to look.
    ///
    /// <b>Nothing is written until everything has been checked.</b> Validation runs to
    /// completion before <see cref="ICombatant.ApplyHealthDelta"/> is called even once, so
    /// a rejected attack cannot leave a half-applied fight behind. There is no partial
    /// path to unwind because there is no partial path.
    ///
    /// <b>It holds no numbers and no state.</b> Reach, the damage floor and which stats
    /// count all arrive in <see cref="BasicAttackRules"/>; readiness arrives already
    /// decided. A static class with no fields cannot accumulate per-attacker state that a
    /// second combatant would then inherit.
    ///
    /// <b>Health is somebody else's.</b> The applied delta goes through the combatant, which
    /// routes it to the existing resource state. Clamping at zero, the revision bump and
    /// the rule that a change of nothing is not a change are all inherited rather than
    /// restated here.
    ///
    /// No animation is consulted and none is waited for. A swing that resolves is resolved
    /// whether or not anything is on screen.
    /// </remarks>
    public static class BasicAttackExecutor
    {
        /// <summary>
        /// Executes an attack that the caller has already decided is timed correctly.
        /// </summary>
        /// <remarks>
        /// Readiness is a parameter rather than something read from the attacker, because
        /// attack timing lives in <see cref="AttackStateMachine"/> and belongs to whatever
        /// drives the entity. Passing it in keeps the executor free of a clock.
        /// </remarks>
        public static AttackResult Execute(in AttackIntent intent, in BasicAttackRules rules,
            bool attackerIsReady = true)
        {
            ICombatant attacker = intent.Attacker;
            ICombatant target = intent.Target;

            // Identities are captured up front so a rejection can still say who was involved.
            var attackerId = attacker == null ? default : attacker.CombatantId;
            var targetId = target == null ? default : target.CombatantId;

            TargetEligibility eligibility =
                TargetEvaluator.Evaluate(attacker, target, rules.PermittedTargets);

            if (!eligibility.IsAllowed)
            {
                return AttackResult.Rejected(
                    AttackResult.FromTarget(eligibility.Reason), attackerId, targetId);
            }

            if (!attackerIsReady)
            {
                return AttackResult.Rejected(AttackRejection.NotReady, attackerId, targetId);
            }

            CombatPosition attackerPosition = attacker.Position;
            CombatPosition targetPosition = target.Position;

            if (!attackerPosition.IsFinite || !targetPosition.IsFinite)
            {
                return AttackResult.Rejected(
                    AttackRejection.InvalidPosition, attackerId, targetId);
            }

            // Squared throughout: no square root, and exactly-at-range compares equal
            // instead of landing either side of a rounded distance.
            float sqrDistance = attackerPosition.SqrDistanceTo(targetPosition);

            if (sqrDistance > rules.RangeSquared)
            {
                return AttackResult.Rejected(AttackRejection.OutOfRange, attackerId, targetId);
            }

            int attackPower = BasicAttackRules.ReadStat(attacker, rules.AttackPowerStat);
            int defense = BasicAttackRules.ReadStat(target, rules.DefenseStat);

            int damage = BasicDamageFormula.Calculate(attackPower, defense, rules.MinimumDamage);

            int healthBefore = target.CurrentHealth;
            target.ApplyHealthDelta(-(long)damage);
            int healthAfter = target.CurrentHealth;

            return AttackResult.Hit(attackerId, targetId, damage, healthBefore, healthAfter);
        }
    }
}
