using System;
using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What a monster is currently doing.</summary>
    /// <remarks>Closed technical category: each value is a distinct branch with distinct
    /// transitions, and adding one is a code change regardless. Which state a monster is in
    /// is decided from authored ranges, not from its identity.</remarks>
    public enum MonsterAiState
    {
        /// <summary>Standing at home, looking for something.</summary>
        Idle = 0,

        /// <summary>Moving around near home, still looking.</summary>
        Wander = 1,

        /// <summary>Something was noticed. A moment of reaction before pursuit.</summary>
        Detect = 2,

        /// <summary>Closing on a target.</summary>
        Chase = 3,

        /// <summary>In range and striking.</summary>
        Attack = 4,

        /// <summary>Going home, having lost or given up on a target.</summary>
        Return = 5,

        /// <summary>Not standing. Nothing further happens.</summary>
        Dead = 6
    }

    /// <summary>
    /// Drives one monster's behaviour.
    /// </summary>
    /// <remarks>
    /// <b>One controller for every monster.</b> The transitions below are the same for a
    /// rat and for a world boss; what differs is the authored detection range, attack
    /// range, cooldown and leash, all read from the <see cref="MonsterDefinition"/>. A
    /// controller per monster type would put content in code.
    ///
    /// <b>It decides; it does not act.</b> Reaching <see cref="MonsterAiState.Attack"/>
    /// raises <see cref="WantsToAttack"/>, and the caller runs that through the existing
    /// combat runtime. Nothing here touches a health value, applies damage or mutates a
    /// character -- so the AI cannot become a second combat path.
    ///
    /// <b>Caller-supplied time.</b> No clock is read, matching
    /// <c>SkillCooldownState</c> and <c>AttackStateMachine</c>. That keeps the assembly
    /// engine-free and makes a five-second chase reproducible in a test.
    ///
    /// <b>Targeting is deliberately small.</b> Nearest eligible enemy in range, dropped
    /// when it dies, leaves, or the leash runs out. There is no threat table: party hate,
    /// taunts and boss aggro are later systems, and <see cref="SelectTarget"/> is the one
    /// method they will replace.
    ///
    /// <b>An attack is a cycle, not a condition.</b> Reaching
    /// <see cref="MonsterAiState.Attack"/> starts an anticipation, the swing lands when that
    /// elapses, and a recovery holds the monster still afterwards before the authored
    /// cooldown even begins to matter. All three figures are content. The alternative --
    /// raise the intent whenever the target is in reach and the cooldown has expired -- is
    /// what this replaced, and it spaced the damage correctly while reading as a twitch.
    ///
    /// <b>Strolling is offered to it, not decided by it.</b>
    /// <see cref="MonsterAiState.Wander"/> exists because a world of monsters standing
    /// perfectly still reads as a world that is switched off. Where and when to stroll is
    /// <see cref="MonsterWanderPlan"/>'s, which has the nest's geometry and its own timers;
    /// this owns only the ordering -- <see cref="BeginWander"/> is refused outright by
    /// anything more important, so a stroll can never interrupt a fight.
    /// </remarks>
    public sealed class MonsterAiController
    {
        private readonly MonsterRuntimeState _monster;
        private float _attackCooldownRemaining;
        private float _windupRemaining;
        private float _recoveryRemaining;
        private bool _windingUp;
        private float _stateElapsed;

        /// <summary>How long the reaction pause between noticing and pursuing lasts.</summary>
        /// <remarks>A constant rather than authored content: it exists so a monster does not
        /// snap instantly from idle to attacking, which reads as a bug. Content that wants
        /// to tune reaction time would author it, and this becomes the default.</remarks>
        public const float DetectDurationSeconds = 0.3f;

        public MonsterAiController(MonsterRuntimeState monster)
        {
            _monster = monster ?? throw new ArgumentNullException(nameof(monster));
            State = MonsterAiState.Idle;
        }

        public MonsterRuntimeState Monster => _monster;

        public MonsterAiState State { get; private set; }

        /// <summary>
        /// True on the tick the monster wants to strike.
        /// </summary>
        /// <remarks>An intention, not an attack. The caller turns it into a
        /// <see cref="CombatAction"/> through the existing runner, which is what keeps
        /// combat rules in one place.</remarks>
        public bool WantsToAttack { get; private set; }

        /// <summary>Seconds until it may strike again.</summary>
        public float AttackCooldownRemaining => _attackCooldownRemaining;

        /// <summary>Seconds of anticipation left before the swing lands.</summary>
        public float WindupRemaining => _windupRemaining;

        /// <summary>Seconds it is still committed to the swing it just threw.</summary>
        public float RecoveryRemaining => _recoveryRemaining;

        /// <summary>
        /// True on the tick it commits to a swing -- the moment the anticipation starts.
        /// </summary>
        /// <remarks>
        /// <b>Why this and not <see cref="WantsToAttack"/>.</b> The presentation has to start
        /// the attack animation at the anticipation, because the anticipation is the part of
        /// it a player is supposed to see coming. <c>WantsToAttack</c> is raised when the
        /// damage lands, which is the end of the swing -- animating from there shows the
        /// recoil first and the wind-up afterwards, in the wrong order.
        ///
        /// A monster with no authored anticipation commits and strikes on the same tick, so
        /// both are raised together and nothing changes for it.
        ///
        /// <b>It is not permission to hurt anybody.</b> Damage is still resolved from
        /// <c>WantsToAttack</c> alone, one authored windup later, and is still range-checked
        /// again at that point. A committed swing whose target stepped away animates as a
        /// lunge at nothing, which is what missing looks like.
        /// </remarks>
        public bool BeganSwing { get; private set; }

        /// <summary>Whether it is mid-swing: winding up, or recovering from one.</summary>
        /// <remarks>A monster in this state does nothing else. It is what turns "in range"
        /// from a condition that fires every time it is true into a cycle with a beginning
        /// and an end.</remarks>
        public bool IsCommittedToASwing => _windupRemaining > 0f || _recoveryRemaining > 0f;

        /// <summary>How long it has been in the current state.</summary>
        public float StateElapsed => _stateElapsed;

        /// <summary>
        /// Advances one tick.
        /// </summary>
        /// <param name="deltaSeconds">Elapsed time, supplied by the caller.</param>
        /// <param name="candidates">
        /// Everything that could be a target. The caller decides what is worth offering;
        /// this filters by team, life and range.
        /// </param>
        public void Tick(float deltaSeconds, IReadOnlyList<ICombatant> candidates)
        {
            WantsToAttack = false;
            BeganSwing = false;

            if (deltaSeconds < 0f) deltaSeconds = 0f;

            if (_attackCooldownRemaining > 0f)
            {
                _attackCooldownRemaining -= deltaSeconds;
                if (_attackCooldownRemaining < 0f) _attackCooldownRemaining = 0f;
            }

            if (_recoveryRemaining > 0f)
            {
                _recoveryRemaining -= deltaSeconds;
                if (_recoveryRemaining < 0f) _recoveryRemaining = 0f;
            }

            // Death outranks everything, including a target it was mid-swing on.
            if (!_monster.IsAlive)
            {
                Enter(MonsterAiState.Dead, deltaSeconds);
                _monster.ClearTarget();
                return;
            }

            if (State == MonsterAiState.Dead)
            {
                // It came back. Respawn put it home; behaviour starts over.
                Enter(MonsterAiState.Idle, deltaSeconds);
            }

            _stateElapsed += deltaSeconds;

            MonsterDefinition definition = _monster.Definition;
            ICombatant target = ResolveTarget(candidates);

            // Leash first: a monster dragged too far from home gives up whatever it is
            // doing, so a player cannot walk one across a map.
            if (IsLeashed(definition))
            {
                _monster.ClearTarget();
                Enter(MonsterAiState.Return, deltaSeconds);
                return;
            }

            if (target == null)
            {
                _monster.ClearTarget();

                if (State == MonsterAiState.Chase || State == MonsterAiState.Attack
                    || State == MonsterAiState.Detect)
                {
                    // It had something and lost it.
                    Enter(MonsterAiState.Return, deltaSeconds);
                    return;
                }

                if (State == MonsterAiState.Return && IsHome())
                {
                    Enter(MonsterAiState.Idle, deltaSeconds);
                    return;
                }

                // Return keeps walking home, and Wander keeps strolling. Everything else
                // with nothing to fight stands still.
                //
                // Wander is left alone here deliberately: this method owns "is there
                // anything to fight", and a monster minding its own business is not an
                // answer to that question. Stomping it back to Idle every tick is what
                // used to make the state unreachable.
                if (State != MonsterAiState.Return && State != MonsterAiState.Wander)
                {
                    Enter(MonsterAiState.Idle, deltaSeconds);
                }

                return;
            }

            _monster.SetTarget(target.CombatantId);

            float sqrDistance = _monster.Position.SqrDistanceTo(target.Position);
            float attackRange = definition.AttackRange;

            bool inReach = attackRange > 0f && sqrDistance <= attackRange * attackRange;

            // A swing already begun is seen through even if the target steps out of reach.
            //
            // Two reasons, and the second is the one that bites. The first is the fight
            // reading correctly: stepping back during the anticipation should make the
            // monster miss, not make it silently cancel and stand there. The second is that
            // without this, a player standing near the edge of reach cancels and re-arms the
            // anticipation every time they drift a few centimetres, and the monster never
            // reaches the end of a windup at all -- which gets far more likely the smaller
            // the authored reach is, and it has just been made much smaller.
            //
            // The damage is unaffected: it is resolved separately and revalidates the range
            // at the moment of impact, so a swing seen through at somebody who left lands on
            // nobody.
            if (inReach || IsCommittedToASwing)
            {
                Enter(MonsterAiState.Attack, deltaSeconds);

                // One swing is a cycle, not a condition that keeps being true:
                //
                //   arrive in reach -> wind up -> strike -> recover -> wait out the
                //   cooldown -> wind up again
                //
                // Before this, reaching for a target raised the intent the moment the
                // cooldown expired, with nothing before it and nothing after it. The
                // damage was correctly spaced and the fight still read as a monster
                // twitching at you, because there was no anticipation to see coming and no
                // beat afterwards to register that it had finished.
                if (_recoveryRemaining > 0f) return;

                if (_attackCooldownRemaining > 0f) return;

                if (_windupRemaining > 0f)
                {
                    // The tick the anticipation actually starts running, which is after any
                    // recovery and cooldown have cleared -- not the tick it was armed.
                    if (!_windingUp)
                    {
                        _windingUp = true;
                        BeganSwing = true;
                    }

                    _windupRemaining -= deltaSeconds;

                    if (_windupRemaining > 0f) return;

                    _windupRemaining = 0f;
                }

                // A monster authored with no anticipation commits and strikes together.
                if (!_windingUp) BeganSwing = true;

                _windingUp = false;

                WantsToAttack = true;

                float cooldown = definition.AttackCooldownSeconds;
                _attackCooldownRemaining = cooldown > 0f ? cooldown : 0f;
                _recoveryRemaining = definition.AttackRecoverySeconds;

                // Armed for the next one, so the cycle repeats rather than the first swing
                // being the only one with anticipation.
                _windupRemaining = definition.AttackWindupSeconds;

                return;
            }

            // Noticed but not yet committed: a brief pause so the turn reads as a reaction.
            if (State == MonsterAiState.Idle || State == MonsterAiState.Wander
                || State == MonsterAiState.Return)
            {
                Enter(MonsterAiState.Detect, deltaSeconds);
                return;
            }

            if (State == MonsterAiState.Detect && _stateElapsed < DetectDurationSeconds) return;

            Enter(MonsterAiState.Chase, deltaSeconds);
        }

        /// <summary>Sends it home without a target. What a reset or a wipe calls.</summary>
        public void ForceReturn()
        {
            _monster.ClearTarget();
            WantsToAttack = false;
            Enter(MonsterAiState.Return, 0f);
        }

        /// <summary>
        /// Sends it strolling to a spot.
        /// </summary>
        /// <remarks>
        /// <b>Only from standing still.</b> A monster that is chasing, striking, walking home
        /// or dead has a reason to be doing it, and an idle stroll is the lowest-priority
        /// thing a monster can be doing. Refusing here rather than letting the caller check
        /// keeps that ordering in one place -- <see cref="MonsterWanderPlan"/> asks, this
        /// decides.
        ///
        /// Returns whether it took. A caller that wants to know may look; one that does not
        /// can call it every tick and be ignored.
        /// </remarks>
        public bool BeginWander(CombatPosition destination)
        {
            if (State != MonsterAiState.Idle && State != MonsterAiState.Wander) return false;

            if (!_monster.IsAlive || !destination.IsFinite) return false;

            _monster.SetWanderDestination(destination);
            Enter(MonsterAiState.Wander, 0f);

            return true;
        }

        /// <summary>Stops a stroll and stands still. What arriving and giving up call.</summary>
        public void StopWandering()
        {
            if (State != MonsterAiState.Wander) return;

            Enter(MonsterAiState.Idle, 0f);
        }

        /// <summary>
        /// Picks something to fight.
        /// </summary>
        /// <remarks>
        /// The nearest living enemy inside the authored detection range. Deliberately the
        /// whole targeting policy: a threat table, taunts, party hate and boss aggro are
        /// later systems, and this is the single method they replace.
        ///
        /// A passive or defensive monster never picks anything, so it only fights back once
        /// something else sets its target -- which is what the retaliation seam on the world
        /// runtime does when one of them is attacked.
        /// </remarks>
        public ICombatant SelectTarget(IReadOnlyList<ICombatant> candidates)
        {
            if (candidates == null) return null;

            MonsterDefinition definition = _monster.Definition;

            // Passive and Defensive never pick a target from proximity. The difference
            // between them is what happens when they are hit, which is not this method's
            // question: retaliation arrives through MonsterRuntimeState.SetTarget, and
            // ResolveTarget honours an existing target whatever the aggression type.
            //
            // Aggressive and AssistOnly continue to acquire on sight. AssistOnly's real
            // semantics -- joining a fight a neighbour is already in -- are not implemented
            // anywhere in this project, and inventing them here would be a balance decision
            // made by accident, so its existing behaviour is preserved unchanged.
            if (definition.AggressionType == MonsterAggressionType.Passive
                || definition.AggressionType == MonsterAggressionType.Defensive)
            {
                return null;
            }

            float range = definition.DetectionRange;
            if (range <= 0f) return null;

            float sqrRange = range * range;
            ICombatant best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                ICombatant candidate = candidates[i];
                if (!candidate.IsAlive()) continue;
                if (candidate.CombatantId == _monster.InstanceId) continue;

                // Reuses the existing relationship rule rather than comparing teams here,
                // so "who is an enemy" has one definition in the project.
                if (CombatTeams.Relate(Combatant(), candidate) != CombatRelationship.Hostile) continue;

                float sqr = _monster.Position.SqrDistanceTo(candidate.Position);
                if (sqr > sqrRange || sqr >= bestSqr) continue;

                best = candidate;
                bestSqr = sqr;
            }

            return best;
        }

        /// <summary>
        /// Keeps the current target if it is still worth having, otherwise picks a new one.
        /// </summary>
        /// <remarks>Holding on matters: re-picking the nearest every tick would make a
        /// monster swap targets whenever two players crossed, which reads as broken.</remarks>
        private ICombatant ResolveTarget(IReadOnlyList<ICombatant> candidates)
        {
            if (candidates == null) return null;

            if (_monster.HasTarget)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].CombatantId != _monster.TargetId) continue;
                    return IsStillValid(candidates[i]) ? candidates[i] : null;
                }

                // The target is not even on the list any more: it left, or logged out.
                return null;
            }

            return SelectTarget(candidates);
        }

        /// <summary>
        /// Whether a target is worth keeping.
        /// </summary>
        /// <remarks>Kept inside the leash rather than the detection range, so a monster does
        /// not forget someone who stepped one pace back. Giving up is the leash's job.</remarks>
        private bool IsStillValid(ICombatant target)
        {
            if (!target.IsAlive()) return false;

            MonsterDefinition definition = _monster.Definition;
            float leash = definition.LeashRange;

            if (leash <= 0f) return true;

            return target.Position.SqrDistanceTo(_monster.SpawnPosition) <= leash * leash * 4f;
        }

        private bool IsLeashed(MonsterDefinition definition)
        {
            float leash = definition.LeashRange;
            if (leash <= 0f) return false;

            return _monster.SqrDistanceFromSpawn > leash * leash;
        }

        /// <summary>Close enough to home to count as arrived.</summary>
        private bool IsHome()
        {
            const float Tolerance = 0.01f;
            return _monster.SqrDistanceFromSpawn <= Tolerance * Tolerance;
        }

        private void Enter(MonsterAiState state, float deltaSeconds)
        {
            if (State == state) return;

            // Arriving in reach arms the anticipation; leaving disarms it, so a monster that
            // was interrupted mid-windup has to wind up again rather than striking the
            // instant it catches its target a second time.
            if (state == MonsterAiState.Attack)
            {
                _windupRemaining = _monster.Definition == null
                    ? 0f
                    : _monster.Definition.AttackWindupSeconds;
                _windingUp = false;
            }
            else if (State == MonsterAiState.Attack)
            {
                _windupRemaining = 0f;
                _recoveryRemaining = 0f;
                _windingUp = false;
            }

            // Leaving a stroll forgets where it was strolling to. Left behind, a stale
            // destination would send the monster back to it the moment it went idle again,
            // which reads as a monster that will not stay where a fight left it.
            if (State == MonsterAiState.Wander) _monster.ClearWanderDestination();

            State = state;

            // The tick that entered a state has already elapsed against the previous one.
            _stateElapsed = deltaSeconds;
        }

        private ICombatant Combatant()
        {
            return _cached ?? (_cached = new MonsterCombatant(_monster));
        }

        private MonsterCombatant _cached;
    }
}
