using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a party invitation stands.</summary>
    public enum PartyInviteState
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,

        /// <summary>Withdrawn, or the party stopped being joinable.</summary>
        Cancelled = 3,

        /// <summary>Its time ran out.</summary>
        Expired = 4
    }

    /// <summary>
    /// One outstanding invitation to a party.
    /// </summary>
    /// <remarks>
    /// <b>An entity, not a flag.</b> A boolean on a character could record that somebody was
    /// invited but not by whom, to which party, or whether a second invitation arrived while
    /// the first was open. Two parties inviting one player at once is ordinary, and with a
    /// flag the second overwrites the first and one party waits forever for an answer that
    /// went somewhere else.
    ///
    /// <b>It resolves once.</b> <see cref="State"/> leaves <see cref="PartyInviteState.Pending"/>
    /// exactly once; every later attempt is refused. That is what stops one invitation being
    /// accepted twice and adding a member twice.
    ///
    /// Flat because it has to persist: one row of a future <c>party_invite</c> table.
    /// </remarks>
    public sealed class PartyInvite : IPersistentState
    {
        private PartyInviteState _state = PartyInviteState.Pending;
        private Revision _revision;

        public PartyInvite(InstanceId inviteId, PartyId party, CharacterId from, CharacterId target,
            long createdTicks = 0L, long expiresTicks = 0L)
        {
            InviteId = inviteId;
            Party = party;
            From = from;
            Target = target;
            CreatedTicks = createdTicks;
            ExpiresTicks = expiresTicks;
            _revision = Revision.Initial;
        }

        public InstanceId InviteId { get; }

        public PartyId Party { get; }

        /// <summary>Who sent it. Kept for display and for audit, never for permission.</summary>
        public CharacterId From { get; }

        public CharacterId Target { get; }

        public long CreatedTicks { get; }

        /// <summary>When it lapses. Zero means it does not expire on its own.</summary>
        public long ExpiresTicks { get; }

        public PartyInviteState State => _state;

        public Revision Revision => _revision;

        public bool IsPending => _state == PartyInviteState.Pending;

        /// <summary>Whether it has lapsed as of a caller-supplied time.</summary>
        /// <remarks>Time is an argument for the reason every service here takes one: nothing
        /// in this assembly reads a clock.</remarks>
        public bool HasExpired(long nowTicks)
        {
            return ExpiresTicks > 0L && nowTicks >= ExpiresTicks;
        }

        /// <summary>
        /// Settles the invitation.
        /// </summary>
        /// <remarks>Refuses to move an already-settled invitation, so an accept that arrives
        /// twice adds one member. Assignment only: whether the accepter may actually join is
        /// <see cref="PartyService"/>'s decision.</remarks>
        public bool Resolve(PartyInviteState state)
        {
            if (state == PartyInviteState.Pending) return false;
            if (_state != PartyInviteState.Pending) return false;

            _state = state;
            _revision = _revision.Next();
            return true;
        }
    }

    /// <summary>
    /// One party, authoritatively.
    /// </summary>
    /// <remarks>
    /// <b>Identities, never characters.</b> Members are <see cref="CharacterId"/> values. A
    /// party holding character objects would keep them alive, would go stale the moment one
    /// levelled, and would make a membership list impossible to persist as rows.
    ///
    /// <b>Named by <see cref="PartyId"/>.</b> Not by its leader and not by its members, both
    /// of which change.
    ///
    /// <b>No duplicate members, structurally.</b> <see cref="TryAdd"/> refuses somebody
    /// already in the list, so the invariant holds even against a caller that reached past
    /// the service.
    ///
    /// <b>Never leaderless while it exists.</b> The leader is always a member; removing them
    /// is either a leadership transfer or a disband, and both are explicit.
    ///
    /// Flat because it has to persist: <c>party</c> plus <c>party_member</c> rows.
    /// </remarks>
    public sealed class PartyState : IPersistentState
    {
        private readonly List<CharacterId> _members = new List<CharacterId>();

        private CharacterId _leader;
        private PartyLootPolicy _lootPolicy;
        private Revision _revision;

        public PartyState(PartyId id, CharacterId leader,
            PartyLootPolicy lootPolicy = PartyLootPolicy.Personal, long createdTicks = 0L)
        {
            Id = id;
            _leader = leader;
            _lootPolicy = lootPolicy;
            CreatedTicks = createdTicks;
            _revision = Revision.Initial;

            if (leader.IsValid) _members.Add(leader);
        }

        public PartyId Id { get; }

        public long CreatedTicks { get; }

        public Revision Revision => _revision;

        /// <summary>Who may invite, kick, transfer and disband.</summary>
        public CharacterId Leader => _leader;

        /// <summary>Members in join order, leader first at creation.</summary>
        public IReadOnlyList<CharacterId> Members => _members;

        public int MemberCount => _members.Count;

        public PartyLootPolicy LootPolicy => _lootPolicy;

        /// <summary>Whether the party still exists. A disbanded party has no members.</summary>
        public bool IsActive => _members.Count > 0;

        public bool Contains(CharacterId character)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i] == character) return true;
            }

            return false;
        }

        public bool IsLeader(CharacterId character)
        {
            return character.IsValid && _leader == character;
        }

        /// <summary>
        /// Adds a member.
        /// </summary>
        /// <remarks>Refuses a duplicate and refuses an invalid identity. Capacity is
        /// <see cref="PartyService"/>'s decision, because the limit is authored content and a
        /// state should not read a registry.</remarks>
        public bool TryAdd(CharacterId character)
        {
            if (!character.IsValid || Contains(character)) return false;

            _members.Add(character);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Removes a member.
        /// </summary>
        /// <remarks>Refuses to remove the leader: that would leave the party leaderless while
        /// still existing. Leaving as leader is a transfer followed by a removal, or a
        /// disband, and <see cref="PartyService"/> decides which.</remarks>
        public bool TryRemove(CharacterId character)
        {
            if (!character.IsValid || character == _leader) return false;

            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i] != character) continue;

                _members.RemoveAt(i);
                _revision = _revision.Next();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Hands leadership to another member.
        /// </summary>
        /// <remarks>Atomic by construction: one assignment, one revision. There is no moment
        /// at which the party has two leaders or none.</remarks>
        public bool TryTransferLeadership(CharacterId character)
        {
            if (!character.IsValid || character == _leader) return false;
            if (!Contains(character)) return false;

            _leader = character;
            _revision = _revision.Next();
            return true;
        }

        public bool TrySetLootPolicy(PartyLootPolicy policy)
        {
            if (_lootPolicy == policy) return false;

            _lootPolicy = policy;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Empties the party.
        /// </summary>
        /// <remarks>The one place a party may become leaderless, and it stops existing in the
        /// same step -- <see cref="IsActive"/> is false afterwards, so nothing can act on the
        /// gap.</remarks>
        public bool Disband()
        {
            if (_members.Count == 0) return false;

            _members.Clear();
            _leader = CharacterId.None;
            _revision = _revision.Next();
            return true;
        }
    }
}
