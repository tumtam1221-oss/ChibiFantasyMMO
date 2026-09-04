using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// What a combat command did, as the server decided it.
    /// </summary>
    /// <remarks>
    /// <b>Presentation-safe and immutable.</b> Ids, numbers and outcomes -- no combatant, no
    /// runtime state, no persistence detail, nothing a client could hold a reference to and
    /// write through. Every field is a value the server computed, so a client showing a
    /// damage number is showing the server's number rather than its own guess.
    /// </remarks>
    public readonly struct ServerCombatResult
    {
        private ServerCombatResult(bool accepted, CombatCommandRejection rejection,
            AttackRejection attackRejection, SkillUseRejection skillRejection,
            InstanceId attacker, InstanceId target, int damage, int healthBefore,
            int healthAfter, bool defeated, long experience, int lootCount,
            InstanceId lootPile)
        {
            IsAccepted = accepted;
            Rejection = rejection;
            AttackRejection = attackRejection;
            SkillRejection = skillRejection;
            Attacker = attacker;
            Target = target;
            Damage = damage;
            TargetHealthBefore = healthBefore;
            TargetHealthAfter = healthAfter;
            TargetDefeated = defeated;
            ExperienceGranted = experience;
            LootCount = lootCount;
            LootPile = lootPile;
        }

        /// <summary>Whether the command was carried out. A miss is still not an acceptance.</summary>
        public bool IsAccepted { get; }

        /// <summary>Why the command never reached combat at all.</summary>
        public CombatCommandRejection Rejection { get; }

        /// <summary>Why the swing itself was refused: out of range, not ready, wrong target.</summary>
        public AttackRejection AttackRejection { get; }

        /// <summary>Why the skill was refused.</summary>
        public SkillUseRejection SkillRejection { get; }

        public InstanceId Attacker { get; }

        public InstanceId Target { get; }

        public int Damage { get; }

        public int TargetHealthBefore { get; }

        public int TargetHealthAfter { get; }

        public bool TargetDefeated { get; }

        /// <summary>Experience the defeat paid, through the existing reward authority.</summary>
        public long ExperienceGranted { get; }

        /// <summary>Entries the defeat dropped. The pile is in the world, not in a bag.</summary>
        public int LootCount { get; }

        public InstanceId LootPile { get; }

        public static ServerCombatResult Refused(CombatCommandRejection rejection)
        {
            return new ServerCombatResult(false, rejection, Gameplay.AttackRejection.None,
                SkillUseRejection.None, default, default, 0, 0, 0, false, 0, 0, default);
        }

        public static ServerCombatResult AttackRefused(AttackResult attack)
        {
            return new ServerCombatResult(false, CombatCommandRejection.None, attack.Reason,
                SkillUseRejection.None, attack.AttackerId, attack.TargetId, 0, 0, 0, false,
                0, 0, default);
        }

        public static ServerCombatResult SkillRefused(in SkillExecutionResult skill)
        {
            return new ServerCombatResult(false, CombatCommandRejection.None,
                Gameplay.AttackRejection.None, skill.Reason, skill.CasterId, skill.TargetId,
                0, 0, 0, false, 0, 0, default);
        }

        public static ServerCombatResult Landed(InstanceId attacker, InstanceId target,
            int damage, int before, int after, bool defeated,
            in MonsterRewardResult reward)
        {
            return new ServerCombatResult(true, CombatCommandRejection.None,
                Gameplay.AttackRejection.None, SkillUseRejection.None, attacker, target,
                damage, before, after, defeated, reward.ExperienceGranted, reward.LootCount,
                reward.LootPile);
        }

        public override string ToString()
        {
            if (!IsAccepted)
            {
                return "refused: " + (Rejection != CombatCommandRejection.None
                    ? Rejection.ToString()
                    : SkillRejection != SkillUseRejection.None
                        ? SkillRejection.ToString()
                        : AttackRejection.ToString());
            }

            return Attacker + " hit " + Target + " for " + Damage
                + " (" + TargetHealthBefore + " -> " + TargetHealthAfter + ")"
                + (TargetDefeated ? ", defeated" : string.Empty);
        }
    }

    /// <summary>
    /// The production path from a client's combat command to a dead monster.
    /// </summary>
    /// <remarks>
    /// <b>This is the wiring that was missing, and only the wiring.</b> Every part it calls
    /// already existed and is unchanged: identity, replay and map are
    /// <see cref="CombatCommandAuthority"/>'s; eligibility, range, the damage formula and
    /// the health write are <see cref="BasicAttackExecutor"/>'s; skills are
    /// <see cref="SkillExecutor"/>'s; the defeat claim, experience and loot are
    /// <see cref="MonsterRewardAuthority"/>'s; retirement and respawn are
    /// <see cref="MonsterWorldRuntime"/>'s. What did not exist was anything that called them
    /// in order, which is why no monster had ever actually died from a player's attack.
    ///
    /// <b>One death, one claim, one payout.</b> A lethal blow calls the reward authority
    /// exactly once, and that single call produces the experience and the loot together --
    /// there is no separate experience claim and loot claim to disagree.
    ///
    /// <b>The client names a target and a sequence.</b> Not damage, not a hit, not a death,
    /// not a reward. Those are all computed here from state the server holds, so a forged
    /// command can at most ask for something it is not allowed to have.
    ///
    /// <b>Timing is real, not assumed.</b> Each character gets the existing
    /// <see cref="AttackStateMachine"/> and <see cref="SkillCooldownState"/>, advanced by
    /// <see cref="Tick"/> from the server's own loop. A client cannot swing faster by asking
    /// faster.
    /// </remarks>
    public sealed class ServerCombatPipeline
    {
        private readonly CombatCommandAuthority _commands;
        private readonly MonsterWorldRuntime _monsters;
        private readonly MonsterRewardAuthority _rewards;
        private readonly BasicAttackRules _basicAttack;
        private readonly AttackTiming _timing;
        private readonly IDefinitionRegistry<SkillDefinition> _skills;
        private readonly SkillExecutionRules _skillRules;

        /// <summary>Authored status effects, or null on a world with no status content.</summary>
        /// <remarks>Passed through to the skill context, where it answers two questions the
        /// pipeline itself never asks: whether the caster is silenced, and what an effect a
        /// skill applies actually is.</remarks>
        private readonly IDefinitionRegistry<StatusEffectDefinition> _statusEffects;

        /// <summary>Per-character swing timing, so asking twice does not hit twice.</summary>
        private readonly Dictionary<string, AttackStateMachine> _attacks =
            new Dictionary<string, AttackStateMachine>();

        /// <summary>Per-character skill cooldowns, the Phase 06 state.</summary>
        private readonly Dictionary<string, SkillCooldownState> _cooldowns =
            new Dictionary<string, SkillCooldownState>();

        /// <param name="commands">Identity, replay and map. The command boundary.</param>
        /// <param name="monsters">The authoritative monster runtime.</param>
        /// <param name="rewards">
        /// The 17.14/17.15 authority. Optional: a world composed without one still fights,
        /// it simply pays nothing, which is honest for a server with no progression content.
        /// </param>
        /// <param name="basicAttack">
        /// Reach, the damage floor and which stats count. Content, supplied once, never
        /// invented here -- a second copy of these numbers is how two damage formulas
        /// start disagreeing.
        /// </param>
        /// <param name="timing">How long a swing and its recovery take.</param>
        /// <param name="skills">Authored skills. Null means this server executes none.</param>
        /// <param name="skillRules">Which stat defends, and the damage floor.</param>
        public ServerCombatPipeline(CombatCommandAuthority commands,
            MonsterWorldRuntime monsters, MonsterRewardAuthority rewards,
            in BasicAttackRules basicAttack, AttackTiming timing = default,
            IDefinitionRegistry<SkillDefinition> skills = null,
            SkillExecutionRules skillRules = default,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects = null)
        {
            _commands = commands;
            _monsters = monsters;
            _rewards = rewards;
            _basicAttack = basicAttack;
            _timing = timing;
            _skills = skills;
            _skillRules = skillRules;
            _statusEffects = statusEffects;
        }

        /// <summary>How many characters this pipeline is tracking timing for.</summary>
        public int TrackedCombatants => _attacks.Count;

        /// <summary>
        /// Runs one combat command to completion.
        /// </summary>
        /// <remarks>
        /// The order is the whole gate. Identity, replay and map first, because they are
        /// cheap and because a stale connection must cost nothing. Then the domain: the
        /// existing executor decides eligibility, range and damage and performs the single
        /// health write. Only then does death matter, and only then is the reward authority
        /// asked -- once.
        ///
        /// A command that the domain refused does not consume the sequence, so a player
        /// whose swing was out of range can step closer and try again with the same one.
        /// </remarks>
        public ServerCombatResult Execute(int connectionId, in CombatCommand command)
        {
            if (_commands == null)
            {
                return ServerCombatResult.Refused(CombatCommandRejection.NoCharacter);
            }

            CombatCommandResolution resolution = _commands.Resolve(connectionId, command);

            if (!resolution.IsResolved)
            {
                return ServerCombatResult.Refused(resolution.Reason);
            }

            return command.IsSkill
                ? ExecuteSkill(resolution, command)
                : ExecuteBasicAttack(resolution, command);
        }

        /// <summary>
        /// Advances every tracked character's swing timer and cooldowns.
        /// </summary>
        /// <remarks>Time arrives as an argument, matching every other service in this
        /// project. Nothing here reads a clock, which is what makes a cooldown reproducible
        /// in a test.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            foreach (KeyValuePair<string, AttackStateMachine> pair in _attacks)
            {
                pair.Value.Advance(deltaSeconds);
            }

            foreach (KeyValuePair<string, SkillCooldownState> pair in _cooldowns)
            {
                pair.Value.Advance(deltaSeconds);
            }
        }

        /// <summary>Forgets a character's timing, for a disconnect or a world reset.</summary>
        public bool Forget(CharacterId character)
        {
            if (!character.IsValid) return false;

            _cooldowns.Remove(character.Value);

            return _attacks.Remove(character.Value);
        }

        // ---- basic attack -------------------------------------------------------------

        private ServerCombatResult ExecuteBasicAttack(in CombatCommandResolution resolution,
            in CombatCommand command)
        {
            AttackStateMachine timing = TimingFor(resolution.Attacker);

            // Asked before the swing so a refusal costs nothing, and begun only once the
            // swing is actually going to happen.
            bool ready = timing.CanAttack;

            AttackIntent intent = CombatCommandAuthority.ToAttackIntent(resolution, command,
                command.Skill);

            AttackResult attack = BasicAttackExecutor.Execute(intent, _basicAttack, ready);

            if (!attack.IsHit) return ServerCombatResult.AttackRefused(attack);

            timing.TryBeginAttack();

            CombatCommandAuthority.Commit(resolution.Attacker, command);

            return AfterDamage(resolution, attack.TargetId, attack.Damage,
                attack.TargetHealthBefore, attack.TargetHealthAfter, attack.TargetDied);
        }

        // ---- skills ---------------------------------------------------------------------

        private ServerCombatResult ExecuteSkill(in CombatCommandResolution resolution,
            in CombatCommand command)
        {
            if (_skills == null)
            {
                // A server with no skill content executes none, rather than inventing one.
                return ServerCombatResult.Refused(CombatCommandRejection.Malformed);
            }

            SkillCooldownState cooldowns = CooldownsFor(resolution.Attacker);

            // The caster's own status list and the authored effects, so the validator can
            // ask whether they are silenced and the executor can resolve what a skill
            // applies. Neither rule is restated here -- both live where they already lived.
            var context = new SkillUseContext(_skills, resolution.Attacker.Skills,
                resolution.Attacker.Domain.Progression.Level, cooldowns, _statusEffects,
                resolution.Attacker.Status);

            SkillUseRequest request = CombatCommandAuthority.ToSkillRequest(resolution,
                command);

            SkillExecutionResult skill = SkillExecutor.Execute(request, context, _skillRules);

            if (!skill.IsExecuted) return ServerCombatResult.SkillRefused(skill);

            CombatCommandAuthority.Commit(resolution.Attacker, command);

            // A skill that healed or buffed did no damage and killed nothing; the same
            // path still reports it, because the caller asked what the command did.
            return AfterDamage(resolution, skill.TargetId, -skill.TargetHealthChange,
                skill.TargetHealthBefore, skill.TargetHealthAfter, skill.TargetDied);
        }

        // ---- what happens after health changed --------------------------------------------

        /// <summary>
        /// Death, reward and retaliation, in that order.
        /// </summary>
        /// <remarks>
        /// <b>The reward authority is called once, and only for a death.</b> It owns the
        /// defeat claim, so experience and loot come out of the same one -- this method
        /// deliberately does not ask for them separately.
        ///
        /// <b>Retaliation is only for a survivor.</b> Telling a dead monster it was attacked
        /// would be meaningless, and the AI would refuse anyway. A monster that lived is
        /// notified and decides for itself: passive ignores it, defensive fights back,
        /// aggressive was already coming. Nothing here forces aggression.
        ///
        /// <b>Retirement is not done here.</b> The monster is dead and its defeat is
        /// claimed; <see cref="MonsterWorldRuntime.Tick"/> retires it and its existing
        /// configuration schedules the respawn. Removing it here would be a second
        /// lifecycle competing with the one that already works.
        /// </remarks>
        private ServerCombatResult AfterDamage(in CombatCommandResolution resolution,
            InstanceId target, int damage, int before, int after, bool died)
        {
            InstanceId attacker = resolution.Attacker.CombatantId;

            if (!died)
            {
                if (damage > 0) _monsters?.NotifyAttacked(target, attacker);

                return ServerCombatResult.Landed(attacker, target, damage, before, after,
                    false, default);
            }

            MonsterRewardResult reward = _rewards == null
                ? default
                : _rewards.Grant(target, attacker);

            return ServerCombatResult.Landed(attacker, target, damage, before, after, true,
                reward);
        }

        private AttackStateMachine TimingFor(LivingCharacter character)
        {
            string key = character.Character.Value;

            if (_attacks.TryGetValue(key, out AttackStateMachine existing)) return existing;

            var created = new AttackStateMachine(_timing);

            _attacks[key] = created;

            return created;
        }

        private SkillCooldownState CooldownsFor(LivingCharacter character)
        {
            string key = character.Character.Value;

            if (_cooldowns.TryGetValue(key, out SkillCooldownState existing)) return existing;

            var created = new SkillCooldownState();

            _cooldowns[key] = created;

            return created;
        }
    }
}
