using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>What one pass of monster swings did.</summary>
    public readonly struct MonsterAttackResult
    {
        internal MonsterAttackResult(int attempted, int landed, int damage, int defeated)
        {
            Attempted = attempted;
            Landed = landed;
            Damage = damage;
            Defeated = defeated;
        }

        /// <summary>How many monsters decided to swing.</summary>
        public int Attempted { get; }

        /// <summary>How many of those actually hit something.</summary>
        /// <remarks>A swing can be refused after the decision -- the target stepped out of
        /// reach, died to somebody else, or left the map between the AI tick and this. That
        /// is ordinary, and counting the two separately is what makes "monsters decide but
        /// never connect" visible rather than silent.</remarks>
        public int Landed { get; }

        /// <summary>Total damage dealt this pass.</summary>
        public int Damage { get; }

        /// <summary>How many targets the pass reduced to nothing.</summary>
        public int Defeated { get; }

        public override string ToString()
        {
            return Landed + "/" + Attempted + " swings for " + Damage
                + (Defeated > 0 ? " (" + Defeated + " defeated)" : string.Empty);
        }
    }

    /// <summary>
    /// Turns a monster's decision to swing into damage, through the combat rules everybody
    /// else uses.
    /// </summary>
    /// <remarks>
    /// <b>The defect this closes.</b> <see cref="MonsterAiController"/> has raised
    /// <c>WantsToAttack</c> since Phase 10 and <see cref="MonsterWorldRuntime.Tick"/> has
    /// reported those intents in <see cref="MonsterTickResult.Attacking"/> ever since -- and
    /// nothing read the list. <c>WorldSimulation</c> called <c>Tick</c> and discarded the
    /// result, so every monster in this project decided to attack, correctly, on schedule,
    /// and no player ever lost a point of health to one. The AI was complete; the wire was
    /// missing.
    ///
    /// <b>It resolves damage; it decides nothing.</b> Whether a monster wants to swing is
    /// the AI's, its reach and cooldown are content, the arithmetic is
    /// <see cref="BasicAttackExecutor"/>'s and the relationship rules are
    /// <see cref="CombatTeams"/>'. This is the join, and a join is all it is -- which is why
    /// a monster hitting a player takes exactly the damage path a player hitting a monster
    /// takes, rather than a second one that will drift.
    ///
    /// <b>There is no command here, and that is deliberate.</b> Every other attack in this
    /// project begins with something a client asked for, and
    /// <see cref="CombatCommandAuthority"/> exists to disbelieve it. A monster asks nobody.
    /// Routing this through the command boundary would mean fabricating a client request on
    /// the server -- the fake-command shape this project avoids -- so it goes straight to
    /// the executor, with no connection id in the signature and no method a client can reach.
    ///
    /// <b>Reach is the monster's own.</b> The rules are rebuilt per swing from the authored
    /// <see cref="MonsterDefinition.AttackRange"/>, so a boss with a longer reach gets it
    /// from content rather than from a constant here. The stats the damage is read from are
    /// supplied once, because which authored stat means "attack power" is content and the
    /// composer is what knows its id.
    /// </remarks>
    public sealed class MonsterAttackAuthority
    {
        private readonly MonsterWorldRuntime _monsters;
        private readonly DefinitionId _attackPowerStat;
        private readonly DefinitionId _defenceStat;
        private readonly int _minimumDamage;

        /// <param name="monsters">The world the swinging monsters live in.</param>
        /// <param name="attackPowerStat">Which authored stat is attack power.</param>
        /// <param name="defenceStat">Which authored stat is defence.</param>
        /// <param name="minimumDamage">
        /// The floor a landed hit does. The same figure the player's own basic attack uses,
        /// so a heavily armoured target does not become invulnerable to one side only.
        /// </param>
        public MonsterAttackAuthority(MonsterWorldRuntime monsters,
            DefinitionId attackPowerStat, DefinitionId defenceStat, int minimumDamage)
        {
            _monsters = monsters;
            _attackPowerStat = attackPowerStat;
            _defenceStat = defenceStat;
            _minimumDamage = minimumDamage < 0 ? 0 : minimumDamage;
        }

        /// <summary>How many swings have ever landed. For diagnostics and for tests.</summary>
        public int TotalLanded { get; private set; }

        /// <summary>
        /// Runs every swing the tick decided on.
        /// </summary>
        /// <remarks>Called immediately after <see cref="MonsterWorldRuntime.Tick"/> and
        /// before anything is published, so a player sees the health they have after the
        /// monsters have hit them rather than the health they had before.</remarks>
        public MonsterAttackResult Resolve(in MonsterTickResult tick)
        {
            var attempted = 0;
            var landed = 0;
            var damage = 0;
            var defeated = 0;

            if (_monsters == null) return default;

            for (var i = 0; i < tick.Attacking.Count; i++)
            {
                attempted++;

                if (!TryResolveOne(tick.Attacking[i], out int dealt, out bool died)) continue;

                landed++;
                damage += dealt;

                if (died) defeated++;
            }

            TotalLanded += landed;

            return new MonsterAttackResult(attempted, landed, damage, defeated);
        }

        private bool TryResolveOne(InstanceId monster, out int damage, out bool died)
        {
            damage = 0;
            died = false;

            if (!_monsters.TryGetMonster(monster, out LivingMonster living)) return false;

            // The intent was raised a moment ago; between then and now it can have died to
            // somebody else's swing.
            if (!living.IsAlive || living.State.Definition == null) return false;

            InstanceId targetId = living.State.TargetId;

            if (!targetId.IsValid) return false;

            if (!_monsters.TryResolve(targetId, out ICombatant target)) return false;

            if (!target.IsAlive()) return false;

            float reach = living.State.Definition.AttackRange;

            if (reach <= 0f) return false;

            // Rebuilt per swing from the monster's own authored reach. The executor checks
            // the distance again, so a target that stepped back after the AI decided is
            // refused here rather than hit from across the clearing.
            BasicAttackRules rules = BasicAttackRules.Melee(_attackPowerStat, _defenceStat,
                _minimumDamage, reach);

            var intent = new AttackIntent(living.Combatant, target);

            AttackResult attack = BasicAttackExecutor.Execute(intent, rules);

            if (!attack.IsHit) return false;

            damage = attack.Damage;
            died = attack.TargetDied;

            return true;
        }
    }
}
