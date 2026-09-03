using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why an attack was refused.</summary>
    /// <remarks>
    /// Deliberately a superset of <see cref="TargetRejection"/> rather than a reuse of it.
    /// Targeting answers "may I aim at this", an attack additionally answers "may I do it
    /// from here, right now"; folding range and readiness into the targeting enum would
    /// give the targeting evaluator reasons it cannot produce.
    /// <see cref="FromTarget"/> maps the shared values so the two never drift.
    /// </remarks>
    public enum AttackRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No attacker was supplied.</summary>
        NoAttacker = 1,

        /// <summary>No target was supplied.</summary>
        NoTarget = 2,

        /// <summary>The attacker is not alive.</summary>
        AttackerDead = 3,

        /// <summary>The target is not alive.</summary>
        TargetDead = 4,

        /// <summary>At least one side has no team, so no relationship exists.</summary>
        UndefinedRelationship = 5,

        /// <summary>The relationship is real but a basic attack does not accept it.</summary>
        RelationshipNotPermitted = 6,

        /// <summary>The target is further away than the attack reaches.</summary>
        OutOfRange = 7,

        /// <summary>A position was not a real number, so range could not be judged.</summary>
        InvalidPosition = 8,

        /// <summary>The attacker is mid-swing or recovering.</summary>
        NotReady = 9
    }

    /// <summary>
    /// What happened when an attack was executed.
    /// </summary>
    /// <remarks>
    /// <b>Three outcomes, not two.</b> A rejected request, an accepted one that resolved,
    /// and -- the case worth naming -- an accepted one that resolved for zero. The last is
    /// a hit: <see cref="IsHit"/> is true and <see cref="Damage"/> is zero, which is how a
    /// caller tells "your armour held" from "you cannot attack that".
    ///
    /// <b>Health before and after are both recorded.</b> Not because a caller cannot
    /// subtract, but because the applied change can differ from the computed
    /// <see cref="Damage"/> when the target had less health left than the blow was worth.
    /// A combat log that showed the computed figure would routinely overstate the killing
    /// hit.
    ///
    /// <b>Identities, not references.</b> The result outlives the call and may later cross
    /// a network boundary or land in a log; holding live <see cref="ICombatant"/> pointers
    /// would keep entities alive and let a reader mutate a fight through its own history.
    ///
    /// No UI, no VFX, no packet, no log sink. This is the return value; presentation
    /// decides what to do with it.
    /// </remarks>
    public readonly struct AttackResult
    {
        private AttackResult(bool isHit, AttackRejection reason, InstanceId attackerId,
            InstanceId targetId, int damage, int healthBefore, int healthAfter, bool targetDied)
        {
            IsHit = isHit;
            Reason = reason;
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
            TargetHealthBefore = healthBefore;
            TargetHealthAfter = healthAfter;
            TargetDied = targetDied;
        }

        /// <summary>True when the attack was accepted and resolved, even for zero damage.</summary>
        public bool IsHit { get; }

        /// <summary><see cref="AttackRejection.None"/> when hit.</summary>
        public AttackRejection Reason { get; }

        public InstanceId AttackerId { get; }

        public InstanceId TargetId { get; }

        /// <summary>Damage the formula produced. Zero on rejection.</summary>
        public int Damage { get; }

        /// <summary>Target health immediately before application. Zero on rejection.</summary>
        public int TargetHealthBefore { get; }

        /// <summary>Target health immediately after application. Zero on rejection.</summary>
        public int TargetHealthAfter { get; }

        /// <summary>
        /// Whether this blow took the target from alive to not alive.
        /// </summary>
        /// <remarks>A transition, not a status. Attacking an already-dead target is
        /// rejected, so this is never true merely because the target is at zero.</remarks>
        public bool TargetDied { get; }

        /// <summary>How much health actually left the target, which may be less than <see cref="Damage"/>.</summary>
        public int HealthLost => TargetHealthBefore - TargetHealthAfter;

        public static AttackResult Hit(InstanceId attackerId, InstanceId targetId,
            int damage, int healthBefore, int healthAfter)
        {
            return new AttackResult(true, AttackRejection.None, attackerId, targetId,
                damage, healthBefore, healthAfter, healthBefore > 0 && healthAfter <= 0);
        }

        public static AttackResult Rejected(AttackRejection reason,
            InstanceId attackerId, InstanceId targetId)
        {
            return new AttackResult(false, reason, attackerId, targetId, 0, 0, 0, false);
        }

        /// <summary>Maps a targeting refusal onto the attack vocabulary.</summary>
        /// <remarks>Explicit rather than a cast, so the two enums may diverge in future
        /// without silently mapping onto each other's numbers.</remarks>
        public static AttackRejection FromTarget(TargetRejection reason)
        {
            switch (reason)
            {
                case TargetRejection.NoAttacker: return AttackRejection.NoAttacker;
                case TargetRejection.NoTarget: return AttackRejection.NoTarget;
                case TargetRejection.AttackerDead: return AttackRejection.AttackerDead;
                case TargetRejection.TargetDead: return AttackRejection.TargetDead;
                case TargetRejection.UndefinedRelationship: return AttackRejection.UndefinedRelationship;
                case TargetRejection.RelationshipNotPermitted: return AttackRejection.RelationshipNotPermitted;
                default: return AttackRejection.None;
            }
        }

        public override string ToString()
        {
            return IsHit
                ? "hit for " + Damage + " (" + TargetHealthBefore + " -> " + TargetHealthAfter
                  + (TargetDied ? ", died)" : ")")
                : "rejected: " + Reason;
        }
    }
}
