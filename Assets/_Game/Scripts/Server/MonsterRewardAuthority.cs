using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>Why a defeat produced no reward.</summary>
    /// <remarks>Named individually because the answers are operationally different: a
    /// duplicate grant is correct behaviour, a failed persist needs a retry, and an unknown
    /// recipient means an identity did not resolve.</remarks>
    public enum MonsterRewardRejection
    {
        None = 0,

        /// <summary>No progression curve, no registry, or no runtime.</summary>
        MissingContext = 1,

        /// <summary>No such monster on this server.</summary>
        UnknownMonster = 2,

        /// <summary>The monster is still standing. A reward would be minted from nothing.</summary>
        InvalidDefeat = 3,

        /// <summary>Its defeat was claimed elsewhere, so this call is not the one that killed it.</summary>
        MonsterAlreadyDefeated = 4,

        /// <summary>This monster has already paid out. The correct answer to a retry.</summary>
        RewardAlreadyGranted = 5,

        /// <summary>The killer does not resolve to a character on this server.</summary>
        UnknownRecipient = 6,

        /// <summary>The recipient exists but may not be credited.</summary>
        RecipientNotEligible = 7,

        /// <summary>The authored reward is not a number experience can be granted from.</summary>
        InvalidExperienceReward = 8,

        /// <summary>The experience was granted but could not be written back yet.</summary>
        PersistenceFailed = 9,

        /// <summary>Somebody else wrote the character first. The save must be retried.</summary>
        ConcurrencyConflict = 10
    }

    /// <summary>
    /// What one monster's defeat paid, and to whom.
    /// </summary>
    /// <remarks>
    /// <b><see cref="IsGranted"/> and <see cref="IsPersisted"/> are separate on purpose.</b>
    /// Experience is authoritative the moment it is applied to the character in memory; the
    /// database catching up is a second fact. Collapsing them would force a caller to choose
    /// between reporting a reward that has not been saved and losing one that has been
    /// earned.
    /// </remarks>
    public readonly struct MonsterRewardResult
    {
        private MonsterRewardResult(bool granted, bool persisted,
            MonsterRewardRejection reason, InstanceId monster, CharacterId recipient,
            long experience, int levelBefore, int levelAfter, long experienceAfter,
            InstanceId lootPile = default, int lootCount = 0)
        {
            LootPile = lootPile;
            LootCount = lootCount;
            IsGranted = granted;
            IsPersisted = persisted;
            Reason = reason;
            Monster = monster;
            Recipient = recipient;
            ExperienceGranted = experience;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            ExperienceAfter = experienceAfter;
        }

        public bool IsGranted { get; }

        /// <summary>Whether the character was written back. False is not a lost reward.</summary>
        public bool IsPersisted { get; }

        public MonsterRewardRejection Reason { get; }

        public InstanceId Monster { get; }

        public CharacterId Recipient { get; }

        /// <summary>Experience actually applied. Never negative, never invented.</summary>
        public long ExperienceGranted { get; }

        public int LevelBefore { get; }

        public int LevelAfter { get; }

        /// <summary>Progress within the level after the grant, in Phase 05 terms.</summary>
        public long ExperienceAfter { get; }

        public bool LevelledUp => LevelAfter > LevelBefore;

        /// <summary>
        /// The pile this kill left in the world, if anything fell.
        /// </summary>
        /// <remarks>
        /// <b>A pile, not an item.</b> Nothing has entered anybody's bag: what dropped is
        /// lying on the ground, owned by the killer, and becomes an item only when somebody
        /// picks it up. That is why experience and loot can share one defeat claim without
        /// experience ever handing out an item.
        /// </remarks>
        public InstanceId LootPile { get; }

        /// <summary>How many entries the roll produced. Zero is the common answer.</summary>
        public int LootCount { get; }

        public bool HasLoot => LootPile.IsValid && LootCount > 0;

        public static MonsterRewardResult Refused(MonsterRewardRejection reason)
        {
            return new MonsterRewardResult(false, false, reason, default, default,
                0, 0, 0, 0);
        }

        public static MonsterRewardResult Granted(bool persisted,
            MonsterRewardRejection reason, InstanceId monster, CharacterId recipient,
            long experience, int levelBefore, int levelAfter, long experienceAfter,
            InstanceId lootPile = default, int lootCount = 0)
        {
            return new MonsterRewardResult(true, persisted, reason, monster, recipient,
                experience, levelBefore, levelAfter, experienceAfter, lootPile, lootCount);
        }

        public override string ToString()
        {
            if (!IsGranted) return "no reward: " + Reason;

            return Recipient + " gained " + ExperienceGranted + " exp, level "
                + LevelBefore + " -> " + LevelAfter
                + (HasLoot ? ", dropped " + LootCount : string.Empty)
                + (IsPersisted ? ", saved" : ", not yet saved: " + Reason);
        }
    }

    /// <summary>
    /// Turns an authoritative monster defeat into experience on a real character.
    /// </summary>
    /// <remarks>
    /// <b>Why this is a new type rather than an existing one.</b> Three candidates were
    /// inspected and none can safely own it. <c>MonsterDefeatService</c> lives in the
    /// engine-free gameplay assembly and deliberately "resolves; does not grant" -- it
    /// cannot see <see cref="WorldCharacterRegistry"/>, which is in this assembly, and
    /// reversing that reference would drag the world server into gameplay.
    /// <see cref="MonsterWorldRuntime"/> owns monsters; giving it the power to mutate and
    /// persist character progression would make the monster runtime a writer of player
    /// state. <c>CombatCommandAuthority</c> resolves a command into an attacker and a
    /// target and never touches a monster's death. So the responsibility is composed here,
    /// from parts that all already exist, and nothing is reimplemented: the claim is Phase
    /// 10's, the levelling is Phase 05's, the save is 17.3's.
    ///
    /// <b>Exactly-once is Phase 10's guard, not a new one.</b>
    /// <c>MonsterRuntimeState.TryClaimDefeat</c> already permits one claim per monster life,
    /// keyed by the authoritative instance id the server minted. This asks it exactly once,
    /// through <see cref="MonsterDefeatService.Resolve"/>, so experience, loot and quest
    /// credit can never disagree about whether a kill happened. A second call finds the
    /// defeat already claimed and is answered <see cref="MonsterRewardRejection.RewardAlreadyGranted"/>.
    /// <c>TransactionLedger</c> was inspected and not reused: an
    /// <c>EconomyTransaction</c> carries currency and item entries and has no place to put
    /// experience, so recording a grant there would mean inventing a fake currency movement.
    ///
    /// <b>The client is nowhere in this file.</b> No connection id, no message, no request
    /// object, no amount from anywhere but <see cref="MonsterDefinition.ExperienceReward"/>.
    /// A player cannot say a monster died, cannot say what it was worth, cannot nominate who
    /// is paid, and cannot ask twice.
    ///
    /// <b>A failed save is not a lost reward.</b> Experience is applied to the authoritative
    /// character first and the character is left dirty, so the existing save lifecycle
    /// retries it. The result says plainly that it is not persisted rather than reporting a
    /// success the database never saw.
    /// </remarks>
    public sealed class MonsterRewardAuthority
    {
        private readonly MonsterWorldRuntime _monsters;
        private readonly WorldCharacterRegistry _characters;
        private readonly CharacterProgressionDefinition _progression;
        private readonly MonsterLootRegistry _loot;
        private readonly IDefinitionRegistry<ItemDefinition> _items;
        private readonly IDefinitionRegistry<DropTableDefinition> _dropTables;
        private readonly IRandomResultSource _rolls;
        private readonly IRandomRangeSource _quantities;
        private readonly float _lootLifetimeSeconds;
        private readonly float _personalWindowSeconds;

        /// <summary>
        /// Monsters that have already paid out, by instance id.
        /// </summary>
        /// <remarks>
        /// A second line of defence rather than the guard itself -- the guard is the defeat
        /// claim. It exists so a repeat can be answered with
        /// <see cref="MonsterRewardRejection.RewardAlreadyGranted"/>, which tells a caller it
        /// is a retry, instead of the bare "already defeated" that a consumed claim reports.
        /// </remarks>
        private readonly HashSet<string> _paid = new HashSet<string>();

        /// <param name="monsters">The authoritative monster runtime.</param>
        /// <param name="characters">Where players are, and the only way to a character.</param>
        /// <param name="progression">
        /// The authored level curve. Supplied rather than looked up, because which curve a
        /// server levels against is content -- the same reason
        /// <see cref="CharacterProgressionState"/> takes one per call.
        /// </param>
        /// <param name="loot">Where dropped piles are put. Null means this server drops nothing.</param>
        /// <param name="items">Authored items. A drop of content that does not exist is skipped.</param>
        /// <param name="dropTables">Authored drop tables. What a monster may drop is content.</param>
        /// <param name="rolls">
        /// Whether an entry lands. <b>Supply one in production.</b> The seam defaults to
        /// <c>AlwaysSucceeds</c>, which would drop every entry on every table on every kill;
        /// <see cref="SystemRandomSource"/> is the real generator.
        /// </param>
        /// <param name="quantities">How many of an entry. Same seam, same warning.</param>
        /// <param name="lootLifetimeSeconds">Seconds a pile lasts. Zero means forever.</param>
        /// <param name="personalWindowSeconds">
        /// Seconds the killer's claim holds before anyone may take it. Zero means it never
        /// lapses, which is what <c>OwnerOnly</c> loot wants.
        /// </param>
        public MonsterRewardAuthority(MonsterWorldRuntime monsters,
            WorldCharacterRegistry characters, CharacterProgressionDefinition progression,
            MonsterLootRegistry loot = null,
            IDefinitionRegistry<ItemDefinition> items = null,
            IDefinitionRegistry<DropTableDefinition> dropTables = null,
            IRandomResultSource rolls = null,
            IRandomRangeSource quantities = null,
            float lootLifetimeSeconds = 0f,
            float personalWindowSeconds = 0f)
        {
            _monsters = monsters;
            _characters = characters;
            _progression = progression;
            _loot = loot;
            _items = items;
            _dropTables = dropTables;
            _rolls = rolls;
            _quantities = quantities;
            _lootLifetimeSeconds = lootLifetimeSeconds;
            _personalWindowSeconds = personalWindowSeconds;
        }

        /// <summary>Whether this server is composed to produce loot at all.</summary>
        private bool CanDrop => _loot != null && _items != null && _dropTables != null;

        /// <summary>How many defeats have paid out. For diagnostics and tests.</summary>
        public int GrantedCount => _paid.Count;

        /// <summary>Whether this monster's defeat has already been rewarded.</summary>
        public bool HasGranted(InstanceId monster)
        {
            return monster.IsValid && _paid.Contains(monster.Value);
        }

        /// <summary>
        /// Grants a defeated monster's experience to the character that killed it.
        /// </summary>
        /// <remarks>
        /// The order is the whole safety argument. Everything that can refuse is asked
        /// before anything is claimed, so a rejected call leaves the monster claimable and
        /// the character untouched. The claim comes next and is the point of no return.
        /// Only then is experience applied, and only then is a save attempted -- so a
        /// database that is down cannot prevent a kill from counting, and cannot make it
        /// count twice.
        /// </remarks>
        /// <param name="monster">The monster the server's own combat killed.</param>
        /// <param name="killer">
        /// Who killed it, as the server resolved it. An instance id the server minted, never
        /// a claim carried in a network message.
        /// </param>
        public MonsterRewardResult Grant(InstanceId monster, InstanceId killer)
        {
            if (_monsters == null || _characters == null || _progression == null)
            {
                return MonsterRewardResult.Refused(MonsterRewardRejection.MissingContext);
            }

            if (!_monsters.TryGetMonster(monster, out LivingMonster living))
            {
                return MonsterRewardResult.Refused(MonsterRewardRejection.UnknownMonster);
            }

            if (living.IsAlive)
            {
                // Still standing. Nothing about a living monster is owed to anybody.
                return MonsterRewardResult.Refused(MonsterRewardRejection.InvalidDefeat);
            }

            if (HasGranted(monster))
            {
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.RewardAlreadyGranted);
            }

            if (living.State.IsDefeatClaimed)
            {
                // Claimed by something else -- loot, a quest, an earlier reward pass. This
                // call did not kill it, so it pays nothing.
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.MonsterAlreadyDefeated);
            }

            if (!TryResolveRecipient(killer, out LivingCharacter recipient))
            {
                return MonsterRewardResult.Refused(MonsterRewardRejection.UnknownRecipient);
            }

            if (!IsEligible(recipient, living))
            {
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.RecipientNotEligible);
            }

            MonsterDefinition definition = living.State.Definition;

            if (definition == null || definition.ExperienceReward < 0)
            {
                // Content validation already refuses a negative reward, so reaching here
                // means a definition arrived some other way. Refusing beats granting a
                // number nobody authored.
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.InvalidExperienceReward);
            }

            if (!recipient.Domain.Progression.CanAdd(definition.ExperienceReward,
                _progression))
            {
                // A gain the progression system would throw on -- an out-of-curve level or
                // an experience total that would overflow. Refused rather than attempted,
                // so the claim is not consumed by a grant that cannot be applied.
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.InvalidExperienceReward);
            }

            // The point of no return: Phase 10's single guard, asked once.
            //
            // Experience and loot come out of the same claim deliberately. Two authorities
            // each calling Resolve would race for one claim and whichever lost would pay
            // nothing -- a kill that gave experience but no drops, or the reverse, depending
            // on call order. One claimant, both payouts.
            List<LootResult> rolled = CanDrop ? new List<LootResult>() : null;

            MonsterDefeatResult defeat = MonsterDefeatService.Resolve(living.State, killer,
                DropContext(recipient), rolled);

            if (!defeat.IsClaimed)
            {
                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.MonsterAlreadyDefeated);
            }

            _paid.Add(monster.Value);

            return Apply(monster, recipient, defeat.ExperienceReward,
                DropLoot(defeat, living, recipient, rolled));
        }

        /// <summary>
        /// Applies the experience and writes the character back.
        /// </summary>
        /// <remarks>
        /// A zero reward is a legitimate authored value -- a training dummy owes nobody
        /// anything -- so it grants successfully, changes nothing and saves nothing. Calling
        /// <c>AddExperience(0)</c> would advance the character's revision and dirty it for a
        /// write with no content.
        /// </remarks>
        private MonsterRewardResult Apply(InstanceId monster, LivingCharacter recipient,
            int experience, LootObjectState pile)
        {
            InstanceId lootId = pile == null ? default : pile.LootId;
            int lootCount = pile == null ? 0 : pile.Count;

            CharacterProgressionState progression = recipient.Domain.Progression;

            int levelBefore = progression.Level;

            if (experience == 0)
            {
                return MonsterRewardResult.Granted(true, MonsterRewardRejection.None,
                    monster, recipient.Domain.Identity.CharacterId, 0, levelBefore,
                    levelBefore, progression.Experience, lootId, lootCount);
            }

            // Phase 05 does the levelling: multiple levels in one call, remainder preserved,
            // experience banked at the cap. There is no second formula here.
            progression.AddExperience(experience, _progression);

            recipient.MarkDirty();

            CharacterPersistenceResult saved = _characters.Save(recipient);

            MonsterRewardRejection reason = saved.IsOk
                ? MonsterRewardRejection.None
                : saved.Failure == CharacterPersistenceFailure.StaleRevision
                    ? MonsterRewardRejection.ConcurrencyConflict
                    : MonsterRewardRejection.PersistenceFailed;

            return MonsterRewardResult.Granted(saved.IsOk, reason, monster,
                recipient.Domain.Identity.CharacterId, experience, levelBefore,
                progression.Level, progression.Experience, lootId, lootCount);
        }

        /// <summary>
        /// The context a drop roll runs in.
        /// </summary>
        /// <remarks>
        /// The killer's level comes from the authoritative character, never from a message,
        /// so a level-banded entry cannot be unlocked by claiming to be level sixty. The
        /// monster's rank is not set here at all: <c>DropResolver</c> reads it off the
        /// monster that actually died, which is what makes a World Boss-only entry
        /// unobtainable from a rat.
        /// </remarks>
        private DropResolver.Context DropContext(LivingCharacter recipient)
        {
            if (!CanDrop) return default;

            return new DropResolver.Context(_items, _dropTables, _rolls, _quantities,
                recipient.Domain.Progression.Level);
        }

        /// <summary>
        /// Puts what fell on the ground, owned by the killer.
        /// </summary>
        /// <remarks>
        /// <b>OwnerOnly, and no courtesy window by default.</b> The killer is the only
        /// eligible taker under the current single-killer model, and the policy enum already
        /// carries the party case for when a party system exists -- so nothing here needs to
        /// change to support one.
        ///
        /// Returns null when nothing dropped, which is the common answer: an empty pile in
        /// the world is a pickup request that can only ever be refused.
        /// </remarks>
        private LootObjectState DropLoot(in MonsterDefeatResult defeat, LivingMonster monster,
            LivingCharacter recipient, List<LootResult> rolled)
        {
            if (!CanDrop || rolled == null || rolled.Count == 0) return null;

            LootObjectState pile = MonsterDefeatService.CreateLoot(defeat, rolled,
                monster.State.Position, LootPolicy.OwnerOnly,
                recipient.Domain.Identity.CharacterId, _lootLifetimeSeconds,
                _personalWindowSeconds);

            if (pile == null) return null;

            return _loot.Add(pile, monster.Map) ? pile : null;
        }

        /// <summary>
        /// Finds the character behind a killer's instance id.
        /// </summary>
        /// <remarks>The identity projection this project already uses: a character's
        /// combatant id is its <see cref="CharacterId"/> as an <see cref="InstanceId"/>.
        /// A monster's id resolves to nothing here, which is what stops a monster killing a
        /// monster from paying anybody.</remarks>
        private bool TryResolveRecipient(InstanceId killer, out LivingCharacter recipient)
        {
            recipient = null;

            if (!killer.IsValid || string.IsNullOrEmpty(killer.Value)) return false;

            return _characters.TryGetByCharacter(new CharacterId(killer.Value), out recipient);
        }

        /// <summary>
        /// Whether a resolved character may be credited.
        /// </summary>
        /// <remarks>
        /// Alive, and standing where the kill happened. The map check is deliberate: credit
        /// belongs to the world the monster died in, and a character the registry places on
        /// another map is not in a position to have landed the blow. A monster with no map
        /// is not checked, matching every other map rule in this project, where an unset map
        /// means unrestricted rather than forbidden.
        /// </remarks>
        private static bool IsEligible(LivingCharacter recipient, LivingMonster monster)
        {
            if (recipient.Domain == null || recipient.Combatant == null) return false;

            if (!recipient.Combatant.IsAlive()) return false;

            if (!monster.Map.IsValid) return true;

            return recipient.Location != null
                && recipient.Location.CurrentMap == monster.Map;
        }
    }
}
