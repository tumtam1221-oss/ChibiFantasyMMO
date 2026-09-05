using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Contracts
{
    /// <summary>
    /// A party as storage remembers it.
    /// </summary>
    /// <remarks>
    /// <b>Durable facts only.</b> Who leads, who belongs, in what order they joined, and
    /// which loot policy they chose. Nothing about a connection, a position, a health bar
    /// or an inventory: character persistence already owns all of that, and a party row
    /// that repeated any of it would be a second copy to disagree with the first.
    ///
    /// <b>Join order is not decoration.</b> Round-robin loot and successor selection both
    /// walk the member list in sequence, so the order a party is stored in is the order it
    /// must come back in -- otherwise a server restart silently changes whose turn it is.
    ///
    /// <b>And neither is the cursor.</b> Storing the member list in order but forgetting
    /// which of them is next means a restart quietly rewinds the rotation to the first
    /// member, who then gets a second turn they never earned. The order and the position
    /// in it are one fact, so they are stored together and read back together.
    /// </remarks>
    public readonly struct PersistedParty
    {
        public PersistedParty(PartyId party, CharacterId leader, PartyLootPolicy lootPolicy,
            IReadOnlyList<CharacterId> members, int revision, int cursor = 0)
        {
            Party = party;
            Leader = leader;
            LootPolicy = lootPolicy;
            Members = members ?? System.Array.Empty<CharacterId>();
            Revision = revision;
            Cursor = cursor;
        }

        public PartyId Party { get; }

        public CharacterId Leader { get; }

        public PartyLootPolicy LootPolicy { get; }

        /// <summary>Every member, in join order. The leader is among them.</summary>
        public IReadOnlyList<CharacterId> Members { get; }

        /// <summary>Storage's own version, for refusing a stale write.</summary>
        public int Revision { get; }

        /// <summary>
        /// Which member is next in the round-robin, as an index into <see cref="Members"/>.
        /// </summary>
        /// <remarks>An index rather than a count of drops so far: a running total would
        /// grow without bound and would have to be interpreted against a member count that
        /// may since have changed. <see cref="IsCursorValid"/> is what a reader checks
        /// before believing it.</remarks>
        public int Cursor { get; }

        /// <summary>Whether <see cref="Cursor"/> actually names one of these members.</summary>
        /// <remarks>False is corruption, not a value to repair: a cursor pointing past the
        /// end of the party names nobody's turn, and folding it back into range would hand
        /// the next drop to an arbitrary member while looking like it worked.</remarks>
        public bool IsCursorValid => Cursor >= 0 && Cursor < Members.Count;

        /// <summary>Whether this describes a party at all.</summary>
        public bool Exists => Party.IsValid && Members.Count > 0;

        public override string ToString()
        {
            return Exists
                ? "party " + Party + " of " + Members.Count + " (" + LootPolicy
                    + ", turn " + Cursor + ")"
                : "no party";
        }
    }

    /// <summary>Why a party could not be read or written.</summary>
    public enum PartyPersistenceFailure
    {
        None = 0,

        /// <summary>The backend could not be reached at all.</summary>
        Unreachable = 1,

        /// <summary>The caller is not a member of the party they tried to write.</summary>
        NotAMember = 2,

        /// <summary>Somebody else wrote first. Re-read and try again.</summary>
        StaleRevision = 3,

        /// <summary>A member already belongs to another party.</summary>
        AlreadyInAParty = 4,

        /// <summary>The party itself was refused: no leader, an unknown policy.</summary>
        InvalidParty = 5,

        /// <summary>
        /// Storage answered, and what it said cannot be believed.
        /// </summary>
        /// <remarks>A loot policy outside the authored enum, or a round-robin cursor that
        /// addresses no member. Separate from <see cref="InvalidParty"/> because that is a
        /// write this world was refused, whereas this is a row already on disk: nothing the
        /// caller can change makes it load, and an operator has to look at it.</remarks>
        Corrupt = 6
    }

    /// <summary>What reading or writing a party did.</summary>
    public readonly struct PartyPersistenceResult
    {
        private PartyPersistenceResult(bool ok, PartyPersistenceFailure failure,
            PersistedParty party, int revision, string detail)
        {
            IsOk = ok;
            Failure = failure;
            Party = party;
            Revision = revision;
            Detail = detail;
        }

        public bool IsOk { get; }

        public PartyPersistenceFailure Failure { get; }

        public PersistedParty Party { get; }

        public int Revision { get; }

        /// <summary>An operator's sentence. Never a credential, never a SQL fragment.</summary>
        public string Detail { get; }

        public static PartyPersistenceResult Loaded(PersistedParty party)
        {
            return new PartyPersistenceResult(true, PartyPersistenceFailure.None, party,
                party.Revision, null);
        }

        /// <summary>Read successfully, and this character is in no party.</summary>
        public static PartyPersistenceResult None()
        {
            return new PartyPersistenceResult(true, PartyPersistenceFailure.None, default,
                0, null);
        }

        public static PartyPersistenceResult Saved(int revision)
        {
            return new PartyPersistenceResult(true, PartyPersistenceFailure.None, default,
                revision, null);
        }

        public static PartyPersistenceResult Failed(PartyPersistenceFailure failure,
            string detail = null)
        {
            return new PartyPersistenceResult(false, failure, default, 0, detail);
        }

        public override string ToString()
        {
            return IsOk ? "ok " + Party : "failed: " + Failure + " " + Detail;
        }
    }

    /// <summary>
    /// Where a party is kept between sessions.
    /// </summary>
    /// <remarks>An interface for the same reason the character store is one: the world
    /// composes against it, so a test can stand a world up without HTTP and the shipped
    /// server can talk to PHP, with neither knowing about the other.</remarks>
    public interface IPartyStateStore
    {
        /// <summary>The party this character belongs to, if any.</summary>
        PartyPersistenceResult Load(SessionId session);

        /// <summary>
        /// Writes the party as it now stands.
        /// </summary>
        /// <remarks>The whole membership every time: a join, a leave and a kick are all
        /// "the party now looks like this", which is one path rather than three that could
        /// disagree. An empty member list ends the party.</remarks>
        PartyPersistenceResult Save(SessionId session, PersistedParty party);
    }
}
