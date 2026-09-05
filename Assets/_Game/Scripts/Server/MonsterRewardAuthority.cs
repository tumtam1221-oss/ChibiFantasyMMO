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
        ConcurrencyConflict = 10,

        /// <summary>
        /// A party's round-robin turn could not be written down, so nothing was paid.
        /// </summary>
        /// <remarks>Under RoundRobin the turn is the claim. Handing the pile over while
        /// the cursor is still at the previous turn would let a restart offer the same
        /// member the same turn again, so the defeat is held instead: the roll that was
        /// already made is kept, and a retry finishes it without rolling a second one.
        /// </remarks>
        PartyRotationNotCommitted = 11,

        /// <summary>
        /// The defeat's decision could not be written down, so none of it was handed over.
        /// </summary>
        /// <remarks>A defeat is resolved once: the drop tables are rolled, the rare chance
        /// is spent and the split is settled. Paying any of that out before the decision is
        /// durable means a world that stops loses it -- and a restart would resolve the same
        /// defeat again, giving a second go at a one in ten million item. So nothing is paid
        /// until the decision is safe.</remarks>
        RewardNotRecorded = 12
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
    public sealed class MonsterRewardAuthority : MonsterLootRegistry.ILootTakenObserver
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

        /// <summary>
        /// A defeat that is decided but not yet paid, because its party's turn would not
        /// commit.
        /// </summary>
        /// <remarks>
        /// <b>Everything expensive is already in here.</b> The defeat claim, the drop roll
        /// and the chosen claimant were all settled before the write was attempted, and
        /// none of them may happen twice -- a second roll would be a second chance at a one
        /// in ten million item. So the answers are held rather than recomputed, and a retry
        /// only re-attempts the part that failed.
        ///
        /// <b>The pile is built but not published.</b> It exists as an object and is in no
        /// map, so nothing in the world can see or take it until the turn behind it is
        /// durable.
        /// </remarks>
        private sealed class HeldDefeat
        {
            /// <summary>The monster's runtime instance: one life, one reward.</summary>
            public InstanceId Monster;

            public DefinitionId Definition;
            public DefinitionId Map;
            public CharacterId Killer;

            /// <summary>The pile this defeat produced, built and possibly not yet published.</summary>
            public LootObjectState Pile;

            /// <summary>What each eligible character is owed, and whether they have had it.</summary>
            public List<MonsterRewardGrant> Grants = new List<MonsterRewardGrant>();

            /// <summary>What fell, in pile order, and whether it has been taken.</summary>
            public List<MonsterRewardLootEntry> Entries =
                new List<MonsterRewardLootEntry>();

            public PartyId Party;
            public CharacterId Claimant;
            public int Cursor;
            public bool HasCursor;
            public bool CursorCommitted;
            public bool LootPublished;

            /// <summary>This decision's durable identity, once it has one.</summary>
            public string RewardId;

            public int Revision;

            /// <summary>Whether the decision is safely written down.</summary>
            public bool Recorded;

            /// <summary>Whether anything here is still owed to somebody.</summary>
            public bool IsOutstanding
            {
                get
                {
                    for (var i = 0; i < Grants.Count; i++)
                    {
                        if (!Grants[i].IsDelivered) return true;
                    }

                    for (var i = 0; i < Entries.Count; i++)
                    {
                        if (!Entries[i].IsClaimed) return true;
                    }

                    return false;
                }
            }
        }

        private readonly Dictionary<string, HeldDefeat> _held =
            new Dictionary<string, HeldDefeat>();

        /// <summary>How many world ticks a refused retry waits before trying again.</summary>
        /// <remarks>
        /// <b>Ticks, not seconds, and not a number from the caller.</b> Nothing public on
        /// this authority accepts a figure from anybody -- an amount or a connection id
        /// could arrive through such a parameter -- so the backoff counts the world's own
        /// calls rather than being handed a delta.
        ///
        /// The wait exists because the write that failed is an HTTP call, and retrying one
        /// of those on every frame would turn a backend hiccup into a stalled world. The
        /// first attempt is not made to wait at all: the backend may already be back.
        /// </remarks>
        private const int RetryTickInterval = 300;

        private int _ticksUntilRetry;

        /// <summary>Where a decided defeat is written down before it is paid.</summary>
        /// <remarks>Optional: a world composed without one behaves exactly as it did before
        /// there was an outbox, keeping its decisions in memory. That is what every test
        /// world that never asked for durability still gets.</remarks>
        private readonly IMonsterRewardOutbox _outbox;

        /// <summary>Whether this world has already asked storage what it still owes.</summary>
        private bool _recovered;

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
            float personalWindowSeconds = 0f,
            WorldPartyRegistry parties = null,
            float rewardRangeMetres = 0f,
            IMonsterRewardOutbox outbox = null)
        {
            _parties = parties;
            _rewardRangeMetres = rewardRangeMetres;
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
            _outbox = outbox;
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

            // A defeat already decided but never paid. Asked before the granted guard
            // because the monster is in _paid already: its claim was consumed, and this
            // is the same defeat being finished rather than a second one.
            if (_held.TryGetValue(monster.Value, out HeldDefeat waitingDefeat))
            {
                if (!TryResolveRecipient(killer, out LivingCharacter waiting)
                    || waiting.Character != waitingDefeat.Killer)
                {
                    return MonsterRewardResult.Refused(
                        MonsterRewardRejection.RewardAlreadyGranted);
                }

                return Settle(waiting, waitingDefeat);
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

            // Who this kill belongs to, decided now and not revisited.
            DefeatRewardContext context = ContextFor(recipient, living);

            // The pile is attributed through Phase 13's own policy. One claimant, recorded
            // immutably on the loot, so a party that disbands later cannot make it
            // unclaimable and a party somebody joins later cannot make it theirs.
            //
            // Built, not yet published: it is in no map until the turn behind it is safe.
            CharacterId claimant = ClaimantFor(context);

            // Each drop is given the identity it will have once somebody is carrying it,
            // chosen here rather than at pickup. That makes it an idempotency key: a
            // delivery repeated after a crash produces the same item instead of a second
            // one. The same InstanceId a pickup would have minted, only decided earlier.
            if (rolled != null)
            {
                for (var i = 0; i < rolled.Count; i++)
                {
                    LootResult roll = rolled[i];

                    rolled[i] = new LootResult(roll.Source, roll.Item, roll.Quantity,
                        roll.RarityOverride, InstanceId.New());
                }
            }

            var held = new HeldDefeat
            {
                Monster = monster,
                Definition = living.State.Definition.Id,
                Map = context.Map,
                Killer = context.Killer,
                Party = context.Party,
                Claimant = claimant,
                Pile = BuildLoot(defeat, living, claimant, rolled),
            };

            // The split is part of the decision, not something to work out again later: a
            // party that loses a member between the kill and the payment must not change
            // what the kill was worth to the members who were there.
            var shares = new List<PartyExperienceShare>();

            PartyExperiencePolicy.Share(defeat.ExperienceReward, context.Eligible, shares);

            for (var i = 0; i < shares.Count; i++)
            {
                held.Grants.Add(new MonsterRewardGrant(shares[i].Character,
                    shares[i].Experience));
            }

            // Nobody eligible is still a defeat that owes the killer nothing, and the
            // killer's own result has to come from somewhere.
            if (held.Grants.Count == 0)
            {
                held.Grants.Add(new MonsterRewardGrant(context.Killer, 0));
            }

            if (held.Pile != null && rolled != null)
            {
                for (var i = 0; i < rolled.Count; i++)
                {
                    held.Entries.Add(new MonsterRewardLootEntry(i, rolled[i].Item,
                        rolled[i].Quantity, rolled[i].RarityOverride, false, default,
                        rolled[i].Instance));
                }
            }

            // The turn this defeat will spend, worked out now and not mutated: it belongs
            // to the decision, so a recovered world commits the same one.
            if (held.Pile != null && RestsOnTheTurn(context))
            {
                held.Cursor = _parties.NextRotation(context.Party);
                held.HasCursor = true;
            }

            return Settle(recipient, held);
        }

        /// <summary>
        /// Commits the party's turn if the claim rests on one, then pays the defeat.
        /// </summary>
        /// <remarks>
        /// <b>The order is the whole point of this method.</b> Under RoundRobin the pile
        /// belongs to whoever the turn names, so the turn has to be durable before the pile
        /// is real. Publishing first and saving afterwards is what let a failed write leave
        /// a spent turn behind an unspent cursor, and a restart then gave the same member
        /// the same turn twice.
        ///
        /// <b>Personal and NeedGreed are not made to wait.</b> Neither hands the pile out by
        /// rotation -- Personal gives it to the killer whatever the cursor says -- so their
        /// claim does not rest on the turn, and coupling them to a write they do not depend
        /// on would only invent a way for a solo-style drop to fail.
        /// </remarks>
        private MonsterRewardResult Settle(LivingCharacter recipient, HeldDefeat held)
        {
            // 1. The decision, written down before any of it is handed over. Everything
            //    below can be attempted again; the roll above it cannot.
            if (!Record(held))
            {
                Hold(held, "its decision could not be written down");

                return MonsterRewardResult.Refused(
                    MonsterRewardRejection.RewardNotRecorded);
            }

            // 2. The party's turn, when this pile is handed out by one.
            if (held.Pile != null && !held.CursorCommitted && held.HasCursor
                && _parties != null)
            {
                PartyPersistenceResult committed =
                    _parties.TryCommitNextRotation(held.Party);

                if (!committed.IsOk)
                {
                    Hold(held, "its loot turn could not be written down ("
                        + committed.Failure + ")");

                    return MonsterRewardResult.Refused(
                        MonsterRewardRejection.PartyRotationNotCommitted);
                }

                held.CursorCommitted = true;

                Progress(held, cursorCommitted: true);
            }
            else if (held.Pile != null && !held.CursorCommitted && held.Party.IsValid
                && _parties != null)
            {
                // Personal and NeedGreed do not hand the pile out by rotation, so their
                // claim does not rest on the turn and is not made to wait on a write.
                _parties.AdvanceRotation(held.Party);

                held.CursorCommitted = true;
            }

            // 3. The pile becomes real. Only entries nobody has taken go back on the
            //    ground, which is what stops a restart respawning an item already carried.
            if (held.Pile != null && !held.LootPublished && HasUnclaimedLoot(held))
            {
                if (PublishLoot(held.Pile, held.Map) != null)
                {
                    held.LootPublished = true;

                    Progress(held, lootPublished: true);
                }
            }

            // 4. Experience, per recipient, skipping anybody already paid.
            MonsterRewardResult mine = Pay(held, recipient);

            // 5. And done, when there is nothing left that could still be lost.
            //
            // A world with no outbox has nothing to lose it to: its rewards were never
            // durable, so an unclaimed pile is just a pile and the defeat is finished.
            if (_outbox == null)
            {
                _held.Remove(held.Monster.Value);

                return mine;
            }

            if (!held.IsOutstanding)
            {
                _held.Remove(held.Monster.Value);

                Progress(held, complete: true);
            }
            else
            {
                _held[held.Monster.Value] = held;
            }

            return mine;
        }

        /// <summary>Whether anything in this pile is still on the ground rather than carried.</summary>
        private static bool HasUnclaimedLoot(HeldDefeat held)
        {
            for (var i = 0; i < held.Entries.Count; i++)
            {
                if (!held.Entries[i].IsClaimed) return true;
            }

            return held.Entries.Count == 0;
        }

        /// <summary>
        /// Writes the decision down, if this world was composed to keep them.
        /// </summary>
        /// <remarks>Recording the same defeat twice is not an error and must not mint a
        /// second reward: the backend keys on the defeat, so a world that saved and never
        /// heard the answer is handed back the reward it already wrote.</remarks>
        private bool Record(HeldDefeat held)
        {
            if (_outbox == null || held.Recorded) return true;

            if (string.IsNullOrEmpty(held.RewardId))
            {
                held.RewardId = InstanceId.New().Value;
            }

            if (!TryWorldSession(out SessionId session)) return false;

            MonsterRewardOutboxResult saved = _outbox.Record(session,
                new PersistedMonsterReward(held.RewardId, held.Monster, held.Definition,
                    held.Map, held.Killer,
                    held.Pile == null ? default : held.Pile.LootId,
                    (int)LootPolicy.OwnerOnly, held.Claimant,
                    held.Pile == null ? 0f : held.Pile.Position.X,
                    held.Pile == null ? 0f : held.Pile.Position.Y,
                    held.Pile == null ? 0f : held.Pile.Position.Z,
                    held.Party, held.Cursor, held.HasCursor,
                    held.Grants, held.Entries));

            if (!saved.IsOk) return false;

            held.RewardId = saved.RewardId;
            held.Revision = saved.Revision;
            held.Recorded = true;

            return true;
        }

        /// <summary>Records that part of a reward has landed.</summary>
        /// <remarks>Best effort by design: the side effect has already happened, and
        /// failing to write the bookkeeping must not undo it. What it costs is a repeat
        /// attempt later, which every step here is built to survive.</remarks>
        private void Progress(HeldDefeat held, bool? cursorCommitted = null,
            bool? lootPublished = null, bool complete = false,
            IReadOnlyList<CharacterId> paid = null,
            IReadOnlyList<MonsterRewardLootEntry> claimed = null)
        {
            if (_outbox == null || !held.Recorded) return;

            if (!TryWorldSession(out SessionId session)) return;

            MonsterRewardOutboxResult moved = _outbox.Progress(session, held.RewardId,
                held.Revision, paid, claimed, cursorCommitted, lootPublished, complete);

            if (moved.IsOk)
            {
                held.Revision = moved.Revision;

                return;
            }

            UnityEngine.Debug.LogWarning("[reward] could not record progress for "
                + held.Monster + ": " + moved.Failure);
        }

        /// <summary>Holds a decided defeat so a retry can finish it without deciding again.</summary>
        private void Hold(HeldDefeat held, string why)
        {
            _held[held.Monster.Value] = held;

            // Ready on the very next tick: the backend may already be back, and waiting out
            // the interval before the first attempt would hold a reward for no reason.
            _ticksUntilRetry = 0;

            UnityEngine.Debug.LogWarning("[reward] holding the reward for " + held.Monster
                + ": " + why);
        }

        /// <summary>
        /// Pays everyone this defeat still owes, and reports the killer's own share.
        /// </summary>
        /// <remarks>
        /// <b>Skipping whoever has already been paid is the whole point.</b> A reward that
        /// crashed between paying one member and recording it comes back with that member
        /// already marked, so recovery pays the rest and leaves them alone.
        ///
        /// The split itself is Phase 13's arithmetic and was done once, at the defeat. This
        /// only hands the decided amounts over.
        /// </remarks>
        private MonsterRewardResult Pay(HeldDefeat held, LivingCharacter killer)
        {
            MonsterRewardResult mine = default;

            var paid = new List<CharacterId>();
            var found = false;

            for (var i = 0; i < held.Grants.Count; i++)
            {
                MonsterRewardGrant grant = held.Grants[i];

                if (grant.IsDelivered)
                {
                    if (grant.Character == held.Killer && !found)
                    {
                        // Already paid on an earlier attempt. Reported as granted, because
                        // it was, rather than paid a second time.
                        mine = MonsterRewardResult.Granted(true,
                            MonsterRewardRejection.None, held.Monster, grant.Character,
                            0, 0, 0, 0, LootIdOf(held), LootCountOf(held));

                        found = true;
                    }

                    continue;
                }

                LivingCharacter recipient = null;

                if (grant.Character == held.Killer && killer != null
                    && killer.Character == held.Killer)
                {
                    recipient = killer;
                }
                else if (!_characters.TryGetByCharacter(grant.Character,
                    out recipient))
                {
                    // Not in this world right now. Their share stays owed, which is what
                    // lets somebody log in tomorrow and still be paid.
                    continue;
                }

                MonsterRewardResult result = Apply(held.Monster, recipient,
                    grant.Experience, held.Pile);

                if (!result.IsGranted) continue;

                held.Grants[i] = new MonsterRewardGrant(grant.Character, grant.Experience,
                    true);

                paid.Add(grant.Character);

                if (grant.Character == held.Killer && !found)
                {
                    mine = result;
                    found = true;
                }
            }

            if (paid.Count > 0) Progress(held, paid: paid);

            return found
                ? mine
                : MonsterRewardResult.Granted(true, MonsterRewardRejection.None,
                    held.Monster, held.Killer, 0, 0, 0, 0, LootIdOf(held),
                    LootCountOf(held));
        }

        private static InstanceId LootIdOf(HeldDefeat held)
        {
            return held.Pile == null ? default : held.Pile.LootId;
        }

        private static int LootCountOf(HeldDefeat held)
        {
            return held.Pile == null ? 0 : held.Pile.Count;
        }

        /// <summary>
        /// Picks up whatever this world still owed when it last stopped.
        /// </summary>
        /// <remarks>
        /// <b>Read once, when somebody arrives.</b> A pending reward is scoped to a server
        /// and a channel, and the backend works out which from the session -- so this needs
        /// a session, and the first character through the door provides one. Asking at world
        /// boot would mean asking with no session at all.
        ///
        /// <b>Nothing is decided here.</b> The roll, the split and the claimant were settled
        /// when the monster died and are read back exactly as they were stored. This
        /// rebuilds the same decision; it does not make a new one, which is the entire
        /// reason the row exists.
        ///
        /// <b>Already-taken loot does not come back.</b> An entry somebody carried off is
        /// marked in storage, so a recovered pile contains only what is still owed -- and a
        /// pile with nothing owed is not republished at all.
        /// </remarks>
        public int RecoverPending()
        {
            if (_outbox == null || _recovered) return 0;

            if (!TryWorldSession(out SessionId session)) return 0;

            _recovered = true;

            IReadOnlyList<PersistedMonsterReward> pending = _outbox.Pending(session);

            var restored = 0;

            for (var i = 0; i < pending.Count; i++)
            {
                PersistedMonsterReward reward = pending[i];

                if (!reward.Exists) continue;

                // Already being carried by this world -- a reward it wrote and has not
                // finished. Storage does not get to overwrite live progress.
                if (_held.ContainsKey(reward.Defeat.Value)) continue;

                HeldDefeat held = Rebuild(reward);

                if (held == null)
                {
                    // Refused rather than repaired. A row naming an item or a monster this
                    // build does not have is an operator's problem, and substituting
                    // something else would quietly hand out the wrong reward.
                    UnityEngine.Debug.LogWarning("[reward] cannot recover " + reward.RewardId
                        + ": it names content this world does not have");

                    continue;
                }

                // The defeat was claimed in the world that decided it, and that world is
                // gone. Claiming it again here is what stops this monster paying twice if
                // it somehow still exists.
                _paid.Add(reward.Defeat.Value);

                _held[reward.Defeat.Value] = held;

                restored++;
            }

            if (restored > 0)
            {
                _ticksUntilRetry = 0;

                UnityEngine.Debug.Log("[reward] recovered " + restored
                    + " unfinished monster reward(s)");
            }

            return restored;
        }

        /// <summary>
        /// Turns a stored decision back into the one this world was carrying.
        /// </summary>
        /// <remarks>Returns null when the row names content this build cannot resolve. The
        /// alternative -- dropping the entry and completing the reward -- would silently
        /// swallow a rare drop, and substituting another item would hand out something
        /// nobody rolled.</remarks>
        private HeldDefeat Rebuild(PersistedMonsterReward reward)
        {
            var held = new HeldDefeat
            {
                Monster = reward.Defeat,
                Definition = reward.Monster,
                Map = reward.Map,
                Killer = reward.Killer,
                Party = reward.Party,
                Claimant = reward.Claimant,
                Cursor = reward.Cursor,
                HasCursor = reward.HasCursor,
                CursorCommitted = reward.IsCursorCommitted,
                LootPublished = false,
                RewardId = reward.RewardId,
                Revision = reward.Revision,
                Recorded = true,
            };

            for (var i = 0; i < reward.Experience.Count; i++)
            {
                held.Grants.Add(reward.Experience[i]);
            }

            var outstanding = new List<LootResult>();

            for (var i = 0; i < reward.Entries.Count; i++)
            {
                MonsterRewardLootEntry entry = reward.Entries[i];

                held.Entries.Add(entry);

                if (entry.IsClaimed) continue;

                if (_items != null && !_items.Contains(entry.Item)) return null;

                // A stored drop with no decided identity cannot be delivered idempotently:
                // a retry would mint one and nothing could tell the two apart. Refused
                // rather than given one now, because the identity belongs to the decision
                // and this is not where decisions are made.
                if (!entry.Instance.IsValid) return null;

                outstanding.Add(new LootResult(reward.Defeat, entry.Item, entry.Quantity,
                    entry.Rarity, entry.Instance));
            }

            // Anything already in the claimant's bag is delivered, whatever the delivery
            // stamp says. This is the crash window this gate closes: the inventory
            // committed, the stamp did not, and without this the pile would go back on the
            // ground for a second pickup.
            Reconcile(held, outstanding);

            if (outstanding.Count > 0 && reward.Loot.IsValid)
            {
                // The same pile id it had before, so a world that publishes it again is
                // republishing one object rather than minting a second.
                held.Pile = new LootObjectState(reward.Loot, reward.Defeat,
                    new CombatPosition(reward.X, reward.Y, reward.Z),
                    outstanding, (LootPolicy)reward.LootPolicy, reward.Claimant,
                    _lootLifetimeSeconds, _personalWindowSeconds);
            }

            return held;
        }

        /// <summary>
        /// Marks as delivered anything the claimant is demonstrably already carrying.
        /// </summary>
        /// <remarks>
        /// <b>Durable ownership outranks the delivery stamp.</b> The item and the stamp
        /// travel through different aggregates and cannot be written in one transaction, so
        /// the ordering is chosen to fail safely: the bag is written first and the stamp
        /// second. A crash in between therefore looks like "owned but unclaimed", and the
        /// only correct reading of that is delivered.
        ///
        /// <b>Somebody else's copy is a conflict, not a delivery.</b> An identity found in
        /// the wrong inventory is reported and left alone: transferring it would move an
        /// item nobody asked to move, and re-publishing it would create a second one.
        ///
        /// Only characters this world can see are checked. One that is offline is not
        /// evidence of anything, and the pickup itself refuses a duplicate identity anyway.
        /// </remarks>
        private void Reconcile(HeldDefeat held, List<LootResult> outstanding)
        {
            if (_characters == null || outstanding.Count == 0) return;

            for (var i = held.Entries.Count - 1; i >= 0; i--)
            {
                MonsterRewardLootEntry entry = held.Entries[i];

                if (entry.IsClaimed || !entry.Instance.IsValid) continue;

                if (!TryFindOwner(entry.Instance, out CharacterId owner)) continue;

                if (owner != held.Claimant)
                {
                    UnityEngine.Debug.LogWarning("[reward] " + entry.Item
                        + " from " + held.Monster + " is held by " + owner
                        + " and not by the character it was decided for; leaving it alone");

                    continue;
                }

                held.Entries[i] = new MonsterRewardLootEntry(entry.Index, entry.Item,
                    entry.Quantity, entry.Rarity, true, owner, entry.Instance);

                for (var r = outstanding.Count - 1; r >= 0; r--)
                {
                    if (outstanding[r].Instance == entry.Instance) outstanding.RemoveAt(r);
                }

                Progress(held, claimed: new[] { held.Entries[i] });
            }
        }

        /// <summary>Who, of the characters in this world, is carrying that exact item.</summary>
        private bool TryFindOwner(InstanceId instance, out CharacterId owner)
        {
            owner = default;

            IReadOnlyList<LivingCharacter> here = _characters.All();

            for (var i = 0; i < here.Count; i++)
            {
                LivingCharacter living = here[i];

                if (living == null || living.Inventory == null) continue;

                if (living.Inventory.IndexOf(instance) < 0) continue;

                owner = living.Character;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Records that somebody carried an entry off a recovered or live pile.
        /// </summary>
        /// <remarks>
        /// <b>Written before the reward can be called finished.</b> This is the evidence
        /// that stops a restart putting an item back on the floor that is already in
        /// somebody's bag. The pickup itself has already happened and been saved with the
        /// character; this only records that it did.
        ///
        /// <b>Implemented explicitly, so it is not part of this authority's surface.</b>
        /// Only the pile registry it is handed to can call it. Nothing on this class takes
        /// a figure or an identity from a caller, and an observer callback is not the place
        /// to start.
        /// </remarks>
        void MonsterLootRegistry.ILootTakenObserver.NoteLootTaken(InstanceId loot,
            int index, CharacterId taker)
        {
            if (!loot.IsValid) return;

            foreach (KeyValuePair<string, HeldDefeat> pair in _held)
            {
                HeldDefeat held = pair.Value;

                if (held.Pile == null || held.Pile.LootId != loot) continue;

                for (var i = 0; i < held.Entries.Count; i++)
                {
                    MonsterRewardLootEntry entry = held.Entries[i];

                    if (entry.Index != index || entry.IsClaimed) continue;

                    held.Entries[i] = new MonsterRewardLootEntry(entry.Index, entry.Item,
                        entry.Quantity, entry.Rarity, true, taker);

                    Progress(held, claimed: new[] { held.Entries[i] });

                    if (!held.IsOutstanding)
                    {
                        _held.Remove(pair.Key);

                        Progress(held, complete: true);
                    }

                    return;
                }

                return;
            }
        }

        /// <summary>
        /// A session this world can speak to the backend as.
        /// </summary>
        /// <remarks>
        /// <b>Any character in this world will do.</b> A pending reward belongs to a server
        /// and a channel, and every session here names the same pair -- so the backend
        /// scopes the call correctly whoever it is made through.
        ///
        /// That is what closes the old limitation where a held reward needed the killer to
        /// be connected. The reward is the world's to finish, not one player's; if nobody
        /// at all is here there is no session and it simply waits, which is the honest
        /// answer rather than a lost drop.
        /// </remarks>
        private bool TryWorldSession(out SessionId session)
        {
            session = default;

            if (_characters == null) return false;

            IReadOnlyList<LivingCharacter> here = _characters.All();

            for (var i = 0; i < here.Count; i++)
            {
                if (here[i] == null || !here[i].Session.IsValid) continue;

                session = here[i].Session;

                return true;
            }

            return false;
        }

        /// <summary>Whether this defeat's pile is handed out by the party's rotation.</summary>
        private bool RestsOnTheTurn(in DefeatRewardContext context)
        {
            if (!context.Party.IsValid || _parties == null) return false;

            // A world that does not persist parties has no durable turn to contradict, so
            // there is nothing here to protect and a drop must not be withheld for it.
            if (!_parties.IsDurable) return false;

            if (!_parties.TryGetPartyOf(context.Killer, out PartyState party)) return false;

            return party.LootPolicy == PartyLootPolicy.RoundRobin;
        }

        /// <summary>Defeats decided but not yet paid, because a turn would not commit.</summary>
        public int HeldCount => _held.Count;

        /// <summary>
        /// Tries the defeats that are waiting on a party turn again.
        /// </summary>
        /// <remarks>
        /// <b>Driven by the world's own tick</b>, in the step that already runs monsters and
        /// the piles they left, so recovery needs no clock of its own. The corpse is not
        /// needed: the pile was built when the monster died and carries its own position,
        /// so a defeat outlives the body it came from and a slow backend cannot cost a
        /// party their drop just because the monster was tidied away first.
        ///
        /// Returns how many were finished, which is what a test and an operator both want to
        /// know.
        /// </remarks>
        public int RetryHeld()
        {
            if (_held.Count == 0) return 0;

            if (_ticksUntilRetry > 0)
            {
                _ticksUntilRetry--;

                return 0;
            }

            var finished = 0;

            foreach (string key in new List<string>(_held.Keys))
            {
                if (!_held.TryGetValue(key, out HeldDefeat held)) continue;

                // The killer is not required. Once the decision is durable it owes what it
                // owes whether or not the person who earned it is connected: the pile goes
                // back on the ground, the turn is committed, and each share is paid to
                // whichever member is here. What is still owed simply stays owed.
                _characters.TryGetByCharacter(held.Killer, out LivingCharacter killer);

                Settle(killer, held);

                // Finished means gone from the queue. A reward that is still waiting on a
                // pile nobody has picked up has not finished, however well the attempt went.
                if (!_held.ContainsKey(key)) finished++;
            }

            // Whatever is still waiting waits a while before the next attempt.
            if (_held.Count > 0) _ticksUntilRetry = RetryTickInterval;

            return finished;
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
        private LootObjectState BuildLoot(in MonsterDefeatResult defeat,
            LivingMonster monster, CharacterId claimant, List<LootResult> rolled)
        {
            if (!CanDrop || rolled == null || rolled.Count == 0) return null;

            return MonsterDefeatService.CreateLoot(defeat, rolled,
                monster.State.Position, LootPolicy.OwnerOnly,
                claimant, _lootLifetimeSeconds,
                _personalWindowSeconds);
        }

        /// <summary>
        /// Puts a built pile into the world, which is the moment it becomes real.
        /// </summary>
        /// <remarks>Separate from building it because this is the line a claim cannot be
        /// taken back across: before it the pile is an object nobody can see, and after it
        /// a player can be standing on it. Everything that must be true of the claim is
        /// made true before this is called.</remarks>
        private LootObjectState PublishLoot(LootObjectState pile, DefinitionId map)
        {
            if (pile == null) return null;

            return _loot.Add(pile, map) ? pile : null;
        }

        /// <summary>
        /// Finds the character behind a killer's instance id.
        /// </summary>
        /// <remarks>The identity projection this project already uses: a character's
        /// combatant id is its <see cref="CharacterId"/> as an <see cref="InstanceId"/>.
        /// A monster's id resolves to nothing here, which is what stops a monster killing a
        /// monster from paying anybody.</remarks>
        /// <summary>
        /// Which single member may take this pile, according to the party's own policy.
        /// </summary>
        /// <remarks>
        /// <b>Phase 13 decides; this only asks.</b> Personal attributes to the killer,
        /// exactly as a solo kill always has. RoundRobin hands it to whoever the rotation
        /// says. Nothing here re-implements either rule, and nothing here knows what the
        /// item is -- a Devil Fruit and a copper coin are attributed identically, because
        /// rarity is the drop table's business and not the party's.
        /// </remarks>
        private CharacterId ClaimantFor(in DefeatRewardContext context)
        {
            if (!context.Party.IsValid || _parties == null) return context.Killer;

            if (!_parties.TryGetPartyOf(context.Killer, out PartyState party))
            {
                return context.Killer;
            }

            var claimants = new List<CharacterId>();

            PartyLootPolicyService.EligibleClaimants(party, context.Killer, context.Rotation,
                claimants);

            for (var i = 0; i < claimants.Count; i++)
            {
                // The rotation can land on a member who was not part of this defeat -- out
                // of range, on another map, offline. Their turn passes to the next one who
                // was actually there rather than reserving loot nobody can reach.
                if (Contains(context.Eligible, claimants[i])) return claimants[i];
            }

            return context.Killer;
        }

        private static bool Contains(IReadOnlyList<CharacterId> members, CharacterId member)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] == member) return true;
            }

            return false;
        }

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
        /// <summary>The parties this world is running. Null in a world with none.</summary>
        private readonly WorldPartyRegistry _parties;

        /// <summary>How near a member must have been to share. Zero means anywhere on the map.</summary>
        private readonly float _rewardRangeMetres;

        /// <summary>
        /// Who this defeat may pay, worked out once at the moment it is claimed.
        /// </summary>
        /// <remarks>
        /// <b>Every filter is applied at defeat time and never again.</b> Same party, same
        /// map, close enough, still alive. A member who was across the map when the boss
        /// died does not become eligible by running over afterwards, and one who was there
        /// does not stop being eligible by leaving the party -- which is what makes a share
        /// something a player earns rather than something they can arrange later.
        ///
        /// <b>Solo is the same code path with a party of one.</b> There is no branch here
        /// for "not in a party": the eligible list is simply the killer, and everything
        /// downstream divides by one.
        /// </remarks>
        private DefeatRewardContext ContextFor(LivingCharacter killer, LivingMonster monster)
        {
            CharacterId killerId = killer.Domain.Identity.CharacterId;

            var eligible = new List<CharacterId>();

            PartyState party = null;

            if (_parties != null) _parties.TryGetPartyOf(killerId, out party);

            if (party == null || !party.IsActive)
            {
                eligible.Add(killerId);

                return new DefeatRewardContext(monster.Instance, killerId, PartyId.None,
                    eligible, monster.Map, 0);
            }

            IReadOnlyList<CharacterId> members = party.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (!SharesTheDefeat(members[i], killerId, monster)) continue;

                eligible.Add(members[i]);
            }

            // A party whose every member was out of range still pays the one who landed the
            // blow: a kill that rewarded nobody would be a bug wearing a rule's clothes.
            if (eligible.Count == 0) eligible.Add(killerId);

            return new DefeatRewardContext(monster.Instance, killerId, party.Id, eligible,
                monster.Map, _parties.RotationOf(party.Id));
        }

        /// <summary>Whether one member was actually part of this kill.</summary>
        private bool SharesTheDefeat(CharacterId member, CharacterId killer,
            LivingMonster monster)
        {
            // The killer is always in: they are standing on the corpse by definition.
            if (member == killer) return true;

            if (!_characters.TryGetByCharacter(member, out LivingCharacter living))
            {
                // Not in this world right now -- disconnected, or on another server.
                return false;
            }

            if (!IsEligible(living, monster)) return false;

            if (_rewardRangeMetres <= 0f) return true;

            CombatPosition from = living.Combatant.Position;

            return from.SqrDistanceTo(monster.State.Position)
                <= _rewardRangeMetres * _rewardRangeMetres;
        }

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
