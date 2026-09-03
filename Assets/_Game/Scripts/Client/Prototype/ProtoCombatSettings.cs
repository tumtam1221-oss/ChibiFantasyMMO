using UnityEngine;

namespace ChibiFantasy.Client.Prototype
{
    /// <summary>
    /// PROTOTYPE combat tuning for PHASE 07.2.
    /// </summary>
    /// <remarks>
    /// Every number a basic attack uses lives here rather than in code, and the stat ids
    /// are strings for the same reason the rest of the project names content by id: combat
    /// must not know that a stat is called STR.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Prototype/Combat Settings",
                     fileName = "ProtoCombatSettings")]
    public sealed class ProtoCombatSettings : ScriptableObject
    {
        [Header("Stats (content ids) - PROTOTYPE")]
        [Tooltip("Id of the stat read from the attacker. No stat name appears in combat code.")]
        public string attackPowerStatId = "stat.str";

        [Tooltip("Id of the stat read from the target.")]
        public string defenseStatId = "stat.vit";

        [Header("Damage - PROTOTYPE")]
        [Tooltip("Floor applied after attack minus defence.")]
        public int minimumDamage = 1;

        [Header("Reach - PROTOTYPE")]
        [Tooltip("Maximum basic attack distance in world units.")]
        public float attackRange = 1.4f;

        [Header("Timing - PROTOTYPE")]
        public float attackDuration = 0.40f;

        public float recoveryDuration = 0.35f;
    }
}
