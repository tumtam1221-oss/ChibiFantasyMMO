using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>What one character is owed for one defeat, and whether they have had it.</summary>
    public readonly struct MonsterRewardGrant
    {
        public MonsterRewardGrant(CharacterId character, int experience,
            bool delivered = false)
        {
            Character = character;
            Experience = experience;
            IsDelivered = delivered;
        }

        public CharacterId Character { get; }

        public int Experience { get; }

        /// <summary>Whether this share has already been paid, across any number of restarts.</summary>
        public bool IsDelivered { get; }

        public override string ToString()
        {
            return Character + " " + Experience + (IsDelivered ? " (paid)" : " (owed)");
        }
    }

    /// <summary>
    /// One thing a defeat's drop tables produced.
    /// </summary>
    /// <remarks>
    /// <b>No item instance id.</b> Instances are minted when an item enters an inventory,
    /// not when it lands on the ground, so at this point there is nothing allocated to
    /// record. What identifies this drop through a restart is the reward's loot id together
    /// with <see cref="Index"/>, and <see cref="IsClaimed"/> is the evidence that stops a
    /// recovered world putting an item somebody already took back on the floor.
    /// </remarks>
    public readonly struct MonsterRewardLootEntry
    {
        public MonsterRewardLootEntry(int index, DefinitionId item, int quantity,
            DefinitionId rarity = default, bool claimed = false,
            CharacterId claimedBy = default)
        {
            Index = index;
            Item = item;
            Quantity = quantity;
            Rarity = rarity;
            IsClaimed = claimed;
            ClaimedBy = claimedBy;
        }

        /// <summary>Its place in the pile. A pickup names a slot, so the order is load-bearing.</summary>
        public int Index { get; }

        public DefinitionId Item { get; }

        public int Quantity { get; }

        /// <summary>The rarity the drop table overrode, or none for "as authored".</summary>
        public DefinitionId Rarity { get; }

        public bool IsClaimed { get; }

        public CharacterId ClaimedBy { get; }

        public bool IsValid => Item.IsValid && Quantity > 0;

        public override string ToString()
        {
            return Item + " x" + Quantity + (IsClaimed ? " (taken)" : string.Empty);
        }
    }

    /// <summary>
    /// A monster defeat's decision, as storage remembers it.
    /// </summary>
    /// <remarks>
    /// <b>Decided facts only, and never recomputed.</b> A defeat is resolved exactly once:
    /// the drop tables are rolled, the rare chance is spent, the experience is split and, in
    /// a party, the claimant is chosen. Every one of those is in here because none of them
    /// may happen a second time -- least of all a one in ten million roll, which a restart
    /// would otherwise hand out another chance at.
    ///
    /// <b>Not a monster, and not a world.</b> No AI state, no health, no damage history, no
    /// connection, no scene object and no random generator. Content is named by authored
    /// <see cref="DefinitionId"/> and never copied.
    ///
    /// <b>The claimant is history, not a lookup.</b> Who this pile belonged to was settled
    /// when the monster died. A party that has since disbanded, changed policy or lost that
    /// member does not get to rewrite it, so it is stored rather than re-derived.
    /// </remarks>
    public readonly struct PersistedMonsterReward
    {
        public PersistedMonsterReward(string rewardId, InstanceId defeat,
            DefinitionId monster, DefinitionId map, CharacterId killer,
            InstanceId loot, int lootPolicy, CharacterId claimant,
            float x, float y, float z,
            PartyId party, int cursor, bool hasCursor,
            IReadOnlyList<MonsterRewardGrant> experience,
            IReadOnlyList<MonsterRewardLootEntry> entries,
            bool cursorCommitted = false, bool lootPublished = false,
            bool complete = false, int revision = 0)
        {
            RewardId = rewardId;
            Defeat = defeat;
            Monster = monster;
            Map = map;
            Killer = killer;
            Loot = loot;
            LootPolicy = lootPolicy;
            Claimant = claimant;
            X = x;
            Y = y;
            Z = z;
            Party = party;
            Cursor = cursor;
            HasCursor = hasCursor;
            Experience = experience ?? System.Array.Empty<MonsterRewardGrant>();
            Entries = entries ?? System.Array.Empty<MonsterRewardLootEntry>();
            IsCursorCommitted = cursorCommitted;
            IsLootPublished = lootPublished;
            IsComplete = complete;
            Revision = revision;
        }

        /// <summary>This reward's own id. Stable for as long as the reward exists.</summary>
        public string RewardId { get; }

        /// <summary>
        /// The defeat this pays for: one monster's runtime life.
        /// </summary>
        /// <remarks>Unique in storage, which is what makes recording a decision idempotent
        /// -- a world that saved and never heard the answer gets the reward it already
        /// wrote. A respawned monster is a new instance and so a new defeat, which is why
        /// this is not keyed by the monster's definition.</remarks>
        public InstanceId Defeat { get; }

        public DefinitionId Monster { get; }

        public DefinitionId Map { get; }

        public CharacterId Killer { get; }

        /// <summary>The pile this defeat produced. Invalid when it dropped nothing.</summary>
        public InstanceId Loot { get; }

        /// <summary>Phase 12's LootPolicy as a number, because a durable record does not
        /// depend on a gameplay enum this assembly cannot see.</summary>
        public int LootPolicy { get; }

        /// <summary>The single character the pile was attributed to, frozen at the defeat.</summary>
        public CharacterId Claimant { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public PartyId Party { get; }

        /// <summary>The rotation position this defeat must land on. Only meaningful with
        /// <see cref="HasCursor"/>.</summary>
        public int Cursor { get; }

        /// <summary>
        /// Whether this defeat owes a party rotation anything at all.
        /// </summary>
        /// <remarks>Separate from <see cref="Cursor"/> because zero is a real position --
        /// the first member's turn -- and a solo, Personal or NeedGreed defeat has no turn
        /// rather than turn zero.</remarks>
        public bool HasCursor { get; }

        public IReadOnlyList<MonsterRewardGrant> Experience { get; }

        public IReadOnlyList<MonsterRewardLootEntry> Entries { get; }

        public bool IsCursorCommitted { get; }

        public bool IsLootPublished { get; }

        public bool IsComplete { get; }

        public int Revision { get; }

        public bool Exists => !string.IsNullOrEmpty(RewardId) && Defeat.IsValid;

        /// <summary>Whether anything this defeat decided is still owed to somebody.</summary>
        public bool HasUndelivered
        {
            get
            {
                for (var i = 0; i < Experience.Count; i++)
                {
                    if (!Experience[i].IsDelivered) return true;
                }

                for (var i = 0; i < Entries.Count; i++)
                {
                    if (!Entries[i].IsClaimed) return true;
                }

                return false;
            }
        }

        public override string ToString()
        {
            return Exists
                ? "reward " + RewardId + " for " + Defeat + " (" + Experience.Count
                    + " paid, " + Entries.Count + " items)"
                : "no reward";
        }
    }

    /// <summary>Why a reward could not be written down or read back.</summary>
    public enum MonsterRewardOutboxFailure
    {
        None = 0,

        /// <summary>The backend could not be reached at all.</summary>
        Unreachable = 1,

        /// <summary>The reward itself was refused: no defeat, an item nobody authored.</summary>
        InvalidReward = 2,

        /// <summary>Somebody else moved this reward on first. Re-read before trying again.</summary>
        StaleRevision = 3,

        /// <summary>Everything this reward owed has already been handed over.</summary>
        AlreadyComplete = 4,

        /// <summary>No such reward, or it belongs to another world.</summary>
        UnknownReward = 5
    }

    /// <summary>What recording or advancing a reward did.</summary>
    public readonly struct MonsterRewardOutboxResult
    {
        private MonsterRewardOutboxResult(bool ok, MonsterRewardOutboxFailure failure,
            string rewardId, int revision, bool existing, string detail)
        {
            IsOk = ok;
            Failure = failure;
            RewardId = rewardId;
            Revision = revision;
            WasAlreadyRecorded = existing;
            Detail = detail;
        }

        public bool IsOk { get; }

        public MonsterRewardOutboxFailure Failure { get; }

        public string RewardId { get; }

        public int Revision { get; }

        /// <summary>Storage already had this defeat, so nothing new was decided.</summary>
        public bool WasAlreadyRecorded { get; }

        /// <summary>An operator's sentence. Never a credential, never a SQL fragment.</summary>
        public string Detail { get; }

        public static MonsterRewardOutboxResult Recorded(string rewardId, int revision,
            bool existing)
        {
            return new MonsterRewardOutboxResult(true, MonsterRewardOutboxFailure.None,
                rewardId, revision, existing, null);
        }

        public static MonsterRewardOutboxResult Failed(MonsterRewardOutboxFailure failure,
            string detail = null)
        {
            return new MonsterRewardOutboxResult(false, failure, null, 0, false, detail);
        }

        public override string ToString()
        {
            return IsOk ? "ok " + RewardId : "failed: " + Failure + " " + Detail;
        }
    }

    /// <summary>
    /// Where a decided defeat is kept until it has been paid.
    /// </summary>
    /// <remarks>An interface for the same reason the character and party stores are: the
    /// world composes against it, so a test can stand a world up without HTTP and the
    /// shipped server can talk to PHP, with neither knowing about the other.</remarks>
    public interface IMonsterRewardOutbox
    {
        /// <summary>
        /// Writes a decided defeat down, before any of it is handed over.
        /// </summary>
        /// <remarks>Recording the same defeat twice is not an error and must not produce a
        /// second reward: a world that saved and never heard the answer has to be able to
        /// ask again.</remarks>
        MonsterRewardOutboxResult Record(SessionId session, PersistedMonsterReward reward);

        /// <summary>Everything this world still owes, oldest first.</summary>
        IReadOnlyList<PersistedMonsterReward> Pending(SessionId session);

        /// <summary>
        /// Records that part of a reward has landed, and optionally that all of it has.
        /// </summary>
        /// <remarks>One call rather than one per side effect, because they all have to be
        /// checked against the same revision -- otherwise two recovering worlds could each
        /// believe they were the one who delivered.</remarks>
        MonsterRewardOutboxResult Progress(SessionId session, string rewardId, int revision,
            IReadOnlyList<CharacterId> experienceDelivered,
            IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
            bool? cursorCommitted, bool? lootPublished, bool complete);
    }
}
