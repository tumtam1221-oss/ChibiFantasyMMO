using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One character's membership of one guild.
    /// </summary>
    /// <remarks>
    /// Identity and rank, nothing more. Holding a character object here would keep it alive,
    /// go stale the moment anything about them changed, and make the membership list
    /// impossible to persist as rows.
    ///
    /// Flat because it has to persist: one row of a future <c>guild_member</c> table is a
    /// guild, a character, a rank and a join time.
    /// </remarks>
    public readonly struct GuildMember
    {
        public GuildMember(CharacterId character, DefinitionId rank, long joinedTicks)
        {
            Character = character;
            Rank = rank;
            JoinedTicks = joinedTicks;
        }

        public CharacterId Character { get; }

        /// <summary>Reference to a <see cref="Data.GuildRankDefinition"/>.</summary>
        public DefinitionId Rank { get; }

        public long JoinedTicks { get; }

        public bool IsValid => Character.IsValid;

        public GuildMember WithRank(DefinitionId rank)
        {
            return new GuildMember(Character, rank, JoinedTicks);
        }

        public override string ToString()
        {
            return Character + " (" + Rank + ")";
        }
    }

    /// <summary>Where a guild invitation stands.</summary>
    public enum GuildInviteState
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Cancelled = 3,
        Expired = 4
    }

    /// <summary>
    /// One outstanding invitation to a guild.
    /// </summary>
    /// <remarks>An entity for the reason <see cref="PartyInvite"/> is one: a boolean cannot
    /// represent two guilds inviting one character at once, and overwriting the first leaves
    /// a guild waiting for an answer that went elsewhere.</remarks>
    public sealed class GuildInvite : IPersistentState
    {
        private GuildInviteState _state = GuildInviteState.Pending;
        private Revision _revision;

        public GuildInvite(InstanceId inviteId, GuildId guild, CharacterId from, CharacterId target,
            long createdTicks = 0L, long expiresTicks = 0L)
        {
            InviteId = inviteId;
            Guild = guild;
            From = from;
            Target = target;
            CreatedTicks = createdTicks;
            ExpiresTicks = expiresTicks;
            _revision = Revision.Initial;
        }

        public InstanceId InviteId { get; }

        public GuildId Guild { get; }

        public CharacterId From { get; }

        public CharacterId Target { get; }

        public long CreatedTicks { get; }

        public long ExpiresTicks { get; }

        public GuildInviteState State => _state;

        public Revision Revision => _revision;

        public bool IsPending => _state == GuildInviteState.Pending;

        public bool HasExpired(long nowTicks)
        {
            return ExpiresTicks > 0L && nowTicks >= ExpiresTicks;
        }

        /// <summary>Settles the invitation. Refuses to move one already settled.</summary>
        public bool Resolve(GuildInviteState state)
        {
            if (state == GuildInviteState.Pending) return false;
            if (_state != GuildInviteState.Pending) return false;

            _state = state;
            _revision = _revision.Next();
            return true;
        }
    }

    /// <summary>
    /// One guild, authoritatively.
    /// </summary>
    /// <remarks>
    /// <b>Named by <see cref="GuildId"/>, displayed by <see cref="Name"/>.</b> Nothing keys
    /// on the name, so renaming a guild orphans no member, no rank and no audit record.
    ///
    /// <b>No duplicate members, structurally.</b> <see cref="TryAdd"/> refuses a character
    /// already in the list, so the invariant survives a caller that reached past the service.
    ///
    /// <b>Never leaderless while it exists.</b> The leader is always a member; the only way
    /// they leave is a transfer or a disband.
    ///
    /// <b>Ranks are references.</b> A member holds a <see cref="DefinitionId"/> naming a
    /// <c>GuildRankDefinition</c>; what that rank may do is read from content at check time,
    /// so re-authoring a rank changes every guild using it and no member holds a stale copy
    /// of a permission set.
    ///
    /// Flat because it has to persist: <c>guild</c> plus <c>guild_member</c> rows.
    /// </remarks>
    public sealed class GuildState : IPersistentState
    {
        private readonly List<GuildMember> _members = new List<GuildMember>();

        private string _name;
        private CharacterId _leader;
        private Revision _revision;

        public GuildState(GuildId id, string name, CharacterId leader, DefinitionId leaderRank,
            long createdTicks = 0L)
        {
            Id = id;
            _name = name;
            _leader = leader;
            CreatedTicks = createdTicks;
            _revision = Revision.Initial;

            if (leader.IsValid) _members.Add(new GuildMember(leader, leaderRank, createdTicks));
        }

        public GuildId Id { get; }

        /// <summary>Display only. Never an identity.</summary>
        public string Name => _name;

        public long CreatedTicks { get; }

        public Revision Revision => _revision;

        public CharacterId Leader => _leader;

        public IReadOnlyList<GuildMember> Members => _members;

        public int MemberCount => _members.Count;

        public bool IsActive => _members.Count > 0;

        public bool Contains(CharacterId character)
        {
            return IndexOf(character) >= 0;
        }

        public bool IsLeader(CharacterId character)
        {
            return character.IsValid && _leader == character;
        }

        /// <summary>The membership record, or an invalid one.</summary>
        public GuildMember MemberOf(CharacterId character)
        {
            int index = IndexOf(character);
            return index < 0 ? default : _members[index];
        }

        /// <summary>The rank a character holds, or none.</summary>
        public DefinitionId RankOf(CharacterId character)
        {
            int index = IndexOf(character);
            return index < 0 ? DefinitionId.None : _members[index].Rank;
        }

        /// <summary>Adds a member at a rank. Refuses a duplicate.</summary>
        /// <remarks>Capacity is <see cref="GuildService"/>'s decision, because the limit is
        /// authored content and a state should not read a registry.</remarks>
        public bool TryAdd(CharacterId character, DefinitionId rank, long joinedTicks)
        {
            if (!character.IsValid || Contains(character)) return false;

            _members.Add(new GuildMember(character, rank, joinedTicks));
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Removes a member. Refuses to remove the leader.</summary>
        public bool TryRemove(CharacterId character)
        {
            if (!character.IsValid || character == _leader) return false;

            int index = IndexOf(character);
            if (index < 0) return false;

            _members.RemoveAt(index);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Moves a member to another rank.</summary>
        public bool TrySetRank(CharacterId character, DefinitionId rank)
        {
            int index = IndexOf(character);
            if (index < 0 || !rank.IsValid) return false;
            if (_members[index].Rank == rank) return false;

            _members[index] = _members[index].WithRank(rank);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Hands leadership over, moving both characters' ranks in one step.
        /// </summary>
        /// <remarks>Atomic by construction: one revision, and no moment at which the guild
        /// has two leaders, none, or a leader holding a member's rank.</remarks>
        public bool TryTransferLeadership(CharacterId target, DefinitionId leaderRank,
            DefinitionId formerLeaderRank)
        {
            if (!target.IsValid || target == _leader) return false;

            int targetIndex = IndexOf(target);
            int leaderIndex = IndexOf(_leader);

            if (targetIndex < 0 || leaderIndex < 0) return false;

            _members[targetIndex] = _members[targetIndex].WithRank(leaderRank);
            _members[leaderIndex] = _members[leaderIndex].WithRank(formerLeaderRank);
            _leader = target;

            _revision = _revision.Next();
            return true;
        }

        /// <summary>Renames the guild. Display only; nothing keys on it.</summary>
        public bool TryRename(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.Equals(_name, name)) return false;

            _name = name;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Ends the guild. The one place it may become leaderless.</summary>
        public bool Disband()
        {
            if (_members.Count == 0) return false;

            _members.Clear();
            _leader = CharacterId.None;
            _revision = _revision.Next();
            return true;
        }

        private int IndexOf(CharacterId character)
        {
            if (!character.IsValid) return -1;

            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Character == character) return i;
            }

            return -1;
        }
    }
}
