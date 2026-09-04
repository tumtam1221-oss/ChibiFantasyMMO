using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>Why a combat command was refused before it ever reached the rules.</summary>
    /// <remarks>
    /// <b>These are resolution failures, not combat outcomes.</b> "Out of range" and "on
    /// cooldown" already exist as <see cref="SkillUseRejection"/> values and are the domain's
    /// to decide. What is named here is the layer above: a connection that has no character,
    /// a message naming an attacker it does not control, a target that does not exist. Those
    /// cannot reach the rules, because the rules take live objects and there is nothing to
    /// hand them.
    /// </remarks>
    public enum CombatCommandRejection
    {
        None = 0,

        /// <summary>The connection has no character in this world.</summary>
        NoCharacter = 1,

        /// <summary>The connection has been displaced and may no longer act.</summary>
        StaleConnection = 2,

        /// <summary>The message named an attacker this connection does not control.</summary>
        NotYourCharacter = 3,

        /// <summary>No such target on this server.</summary>
        UnknownTarget = 4,

        /// <summary>The target is on a different map.</summary>
        DifferentMap = 5,

        /// <summary>The attacker is dead.</summary>
        AttackerDead = 6,

        /// <summary>An older or replayed command.</summary>
        OutOfOrder = 7,

        /// <summary>The command named no skill or no target where one is required.</summary>
        Malformed = 8
    }

    /// <summary>
    /// A client asking to attack or to use a skill.
    /// </summary>
    /// <remarks>
    /// <b>Look at what is absent.</b> No damage, no resulting health, no cooldown state, no
    /// hit flag, no critical flag. A client cannot send any of them because there is nowhere
    /// to put them — which is why "forged damage" is not something the server has to detect.
    /// It sends who it wants to hit and with what; everything that follows is computed.
    ///
    /// <see cref="ClaimedAttacker"/> is compared against the connection's own character and
    /// never read as a value. Editing it produces a refusal, not a different attacker.
    /// </remarks>
    public readonly struct CombatCommand
    {
        public CombatCommand(CharacterId claimedAttacker, InstanceId target, DefinitionId skill,
            int rank, long sequence)
        {
            ClaimedAttacker = claimedAttacker;
            Target = target;
            Skill = skill;
            Rank = rank;
            Sequence = sequence;
        }

        /// <summary>Who the client thinks it is. Compared, never believed.</summary>
        public CharacterId ClaimedAttacker { get; }

        /// <summary>Who it wants to hit. Resolved against the server's own combatants.</summary>
        public InstanceId Target { get; }

        /// <summary>The skill, or invalid for a basic attack.</summary>
        public DefinitionId Skill { get; }

        public int Rank { get; }

        /// <summary>Monotonic per connection, so a replayed command is refused.</summary>
        public long Sequence { get; }

        public bool IsSkill => Skill.IsValid;

        public override string ToString()
        {
            return IsSkill ? "skill " + Skill + " on " + Target : "attack " + Target;
        }
    }

    /// <summary>What resolving a combat command produced.</summary>
    public readonly struct CombatCommandResolution
    {
        private CombatCommandResolution(bool ok, CombatCommandRejection reason,
            LivingCharacter attacker, ICombatant attackerCombatant, ICombatant target)
        {
            IsResolved = ok;
            Reason = reason;
            Attacker = attacker;
            AttackerCombatant = attackerCombatant;
            Target = target;
        }

        public bool IsResolved { get; }

        public CombatCommandRejection Reason { get; }

        /// <summary>The connection's own character. Never the one the message named.</summary>
        public LivingCharacter Attacker { get; }

        public ICombatant AttackerCombatant { get; }

        public ICombatant Target { get; }

        public static CombatCommandResolution Resolved(LivingCharacter attacker,
            ICombatant attackerCombatant, ICombatant target)
        {
            return new CombatCommandResolution(true, CombatCommandRejection.None, attacker,
                attackerCombatant, target);
        }

        public static CombatCommandResolution Refused(CombatCommandRejection reason)
        {
            return new CombatCommandResolution(false, reason, null, null, null);
        }

        public override string ToString()
        {
            return IsResolved ? "resolved" : "refused: " + Reason;
        }
    }

    /// <summary>
    /// Supplies the combatants a world server is authoritative for.
    /// </summary>
    /// <remarks>
    /// An interface because what is targetable differs by phase: players now, monsters when
    /// 17.10 lands, and neither of them belongs to this file. What matters here is that a
    /// target is <i>looked up</i> rather than sent — a client that names something the server
    /// does not hold gets a refusal, not a target.
    /// </remarks>
    public interface ICombatantResolver
    {
        /// <summary>The combatant behind an instance id, if this server has one.</summary>
        bool TryResolve(InstanceId instance, out ICombatant combatant);

        /// <summary>Which map a combatant is on, so a cross-map attack can be refused.</summary>
        bool TryGetMap(InstanceId instance, out DefinitionId map);
    }

    /// <summary>
    /// Turns a client's combat request into something the existing combat rules can run.
    /// </summary>
    /// <remarks>
    /// <b>It resolves; it does not decide combat.</b> Range, cooldown, resource cost,
    /// relationship, learned skills, level and damage are all decided by
    /// <see cref="SkillUseValidator"/>, <see cref="SkillExecutor"/> and
    /// <see cref="BasicAttackExecutor"/>, which Phase 07 already built and this deliberately
    /// does not reimplement. Duplicating any of them would create a second set of combat
    /// rules that disagrees with the first the moment either is tuned.
    ///
    /// <b>What it does own is identity.</b> The attacker is the connection's own character,
    /// looked up, never taken from the message. The target is looked up against the
    /// combatants this server holds. Between them that is the whole of "the client cannot
    /// forge a target or an attacker" — not because those forgeries are detected, but
    /// because the values are never read from the client in the first place.
    ///
    /// <b>Pure.</b> No engine type, no transport, no clock. Every rule below is exercised by
    /// an ordinary test.
    /// </remarks>
    public sealed class CombatCommandAuthority
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly WorldConnectionRegistryAdapter _connections;
        private readonly ICombatantResolver _combatants;

        /// <summary>
        /// Whether a connection is still entitled to act.
        /// </summary>
        /// <remarks>Narrowed to one question rather than taking the whole connection
        /// registry, so this file cannot reach for anything else and a test can answer it
        /// with a lambda.</remarks>
        public delegate bool WorldConnectionRegistryAdapter(int connectionId);

        public CombatCommandAuthority(WorldCharacterRegistry characters,
            WorldConnectionRegistryAdapter canAct, ICombatantResolver combatants)
        {
            _characters = characters;
            _connections = canAct;
            _combatants = combatants;
        }

        /// <summary>
        /// Resolves a command into an attacker and a target, or refuses it.
        /// </summary>
        /// <remarks>
        /// Order is deliberate: the cheap identity checks run before any lookup, so a flood
        /// of commands from a stale connection costs nothing, and a replayed command is
        /// refused before anything is resolved.
        /// </remarks>
        public CombatCommandResolution Resolve(int connectionId, in CombatCommand command)
        {
            if (_characters == null || _combatants == null)
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.NoCharacter);
            }

            // A displaced socket must not act on a character its replacement now controls.
            if (_connections != null && !_connections(connectionId))
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.StaleConnection);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter attacker))
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.NoCharacter);
            }

            // Replay, before anything is looked up.
            if (command.Sequence <= attacker.LastCombatSequence)
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.OutOfOrder);
            }

            // The message names an attacker. It is compared against the connection's own
            // character and never used as one -- editing it cannot change who attacks.
            if (command.ClaimedAttacker.IsValid && command.ClaimedAttacker != attacker.Character)
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.NotYourCharacter);
            }

            if (!command.Target.IsValid)
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.Malformed);
            }

            if (!_combatants.TryResolve(attacker.CombatantId, out ICombatant attackerCombatant))
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.NoCharacter);
            }

            if (!attackerCombatant.IsAlive())
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.AttackerDead);
            }

            if (!_combatants.TryResolve(command.Target, out ICombatant target))
            {
                // Named something this server does not hold. A refusal, not a target.
                return CombatCommandResolution.Refused(CombatCommandRejection.UnknownTarget);
            }

            // A target on another map is not merely far away -- range alone would let a
            // player hit through a loading screen if two maps overlap in coordinates.
            if (_combatants.TryGetMap(command.Target, out DefinitionId targetMap)
                && targetMap.IsValid
                && !attacker.Location.IsOn(targetMap))
            {
                return CombatCommandResolution.Refused(CombatCommandRejection.DifferentMap);
            }

            return CombatCommandResolution.Resolved(attacker, attackerCombatant, target);
        }

        /// <summary>
        /// Records that a command was accepted, so the next one must be newer.
        /// </summary>
        /// <remarks>Separate from <see cref="Resolve"/> because a resolution that the
        /// domain then refuses -- out of range, on cooldown -- must not consume the
        /// sequence. A player whose skill was refused for being on cooldown has to be able
        /// to press it again.</remarks>
        public static void Commit(LivingCharacter attacker, in CombatCommand command)
        {
            if (attacker == null) return;

            attacker.LastCombatSequence = command.Sequence;
        }

        /// <summary>
        /// Builds the request the existing validator and executor take.
        /// </summary>
        /// <remarks>The rank is clamped to at least one rather than trusted: a rank of zero
        /// or a negative would reach <see cref="SkillUseValidator"/> as a value it has no
        /// meaning for, and the validator's own rank check is about what the character has
        /// learned rather than about arithmetic.</remarks>
        public static SkillUseRequest ToSkillRequest(in CombatCommandResolution resolution,
            in CombatCommand command)
        {
            return new SkillUseRequest(resolution.AttackerCombatant, command.Skill,
                resolution.Target, command.Rank < 1 ? 1 : command.Rank);
        }

        /// <summary>Builds the basic-attack intent the existing executor takes.</summary>
        public static AttackIntent ToAttackIntent(in CombatCommandResolution resolution,
            in CombatCommand command, DefinitionId attackDefinition)
        {
            return new AttackIntent(resolution.AttackerCombatant, resolution.Target,
                attackDefinition, (int)command.Sequence);
        }
    }
}
