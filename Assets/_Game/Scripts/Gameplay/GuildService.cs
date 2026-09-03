using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a guild operation was refused.</summary>
    public enum GuildRejection
    {
        None = 0,

        /// <summary>No guild, no registry or no character was supplied.</summary>
        MissingContext = 1,

        /// <summary>The guild has been disbanded.</summary>
        GuildInactive = 2,

        /// <summary>The acting character does not belong to this guild.</summary>
        NotAMember = 3,

        /// <summary>The acting character's rank does not carry the permission.</summary>
        PermissionDenied = 4,

        /// <summary>The guild already holds as many members as it may.</summary>
        GuildFull = 5,

        /// <summary>The target already belongs to this guild.</summary>
        AlreadyAMember = 6,

        /// <summary>The target already belongs to another guild.</summary>
        AlreadyInAnotherGuild = 7,

        /// <summary>No such invitation, or it has already been settled.</summary>
        InviteNotPending = 8,

        /// <summary>The invitation has lapsed.</summary>
        InviteExpired = 9,

        /// <summary>The invitation is not this character's to answer.</summary>
        NotTheInvitee = 10,

        /// <summary>The target could not be resolved, or names nobody.</summary>
        UnknownCharacter = 11,

        /// <summary>The rank could not be resolved.</summary>
        UnknownRank = 12,

        /// <summary>The name is missing, the wrong length, or contains disallowed characters.</summary>
        InvalidName = 13,

        /// <summary>Another guild already holds the name.</summary>
        NameTaken = 14,

        /// <summary>The name is reserved.</summary>
        NameReserved = 15,

        /// <summary>Acting on oneself where that makes no sense.</summary>
        InvalidTarget = 16,

        /// <summary>The target already holds the most senior rank available, or the least.</summary>
        RankUnavailable = 17,

        /// <summary>A member may not act on somebody at or above their own rank.</summary>
        InsufficientSeniority = 18
    }

    /// <summary>What a guild operation did.</summary>
    public readonly struct GuildResult
    {
        private GuildResult(bool accepted, GuildRejection reason, GuildState guild,
            CharacterId subject, DefinitionId rank, GuildInvite invite)
        {
            IsAccepted = accepted;
            Reason = reason;
            Guild = guild;
            Subject = subject;
            Rank = rank;
            Invite = invite;
        }

        public bool IsAccepted { get; }

        public GuildRejection Reason { get; }

        public GuildState Guild { get; }

        /// <summary>Who the operation was about.</summary>
        public CharacterId Subject { get; }

        /// <summary>The rank they ended up at, where one applies.</summary>
        public DefinitionId Rank { get; }

        public GuildInvite Invite { get; }

        public GuildId GuildId => Guild == null ? Core.GuildId.None : Guild.Id;

        public Revision Revision => Guild == null ? Revision.Initial : Guild.Revision;

        public static GuildResult Accepted(GuildState guild, CharacterId subject = default,
            DefinitionId rank = default, GuildInvite invite = null)
        {
            return new GuildResult(true, GuildRejection.None, guild, subject, rank, invite);
        }

        public static GuildResult Rejected(GuildRejection reason, GuildState guild = null)
        {
            return new GuildResult(false, reason, guild, default, default, null);
        }

        public override string ToString()
        {
            return IsAccepted ? "guild " + GuildId + " ok" : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Decides whether a guild name may be used.
    /// </summary>
    /// <remarks>
    /// <b>An interface because the answer is not local.</b> Uniqueness across a whole shard
    /// is a property of the database, not of whatever guilds this process happens to know
    /// about. A local set would answer "unique" for a name another server minted a second
    /// ago, and two guilds would end up sharing it.
    ///
    /// <see cref="GuildService"/> therefore treats a local implementation as a
    /// <em>pre-check</em> and the future server-side one as the authority. That distinction
    /// is stated rather than hidden: nothing in this phase claims shard-wide uniqueness.
    /// </remarks>
    public interface IGuildNameAuthority
    {
        /// <summary>Whether the name is free.</summary>
        bool IsAvailable(string name);

        /// <summary>Whether the name may not be used at all, whoever asks.</summary>
        bool IsReserved(string name);

        /// <summary>Records a name as taken. Called only after a guild is actually created.</summary>
        void Claim(string name, GuildId guild);

        /// <summary>Releases a name when its guild is disbanded.</summary>
        void Release(string name);
    }

    /// <summary>
    /// Forming and running a guild.
    /// </summary>
    /// <remarks>
    /// <b>The domain enforces permissions.</b> Every operation resolves the actor's rank and
    /// asks <see cref="GuildRankDefinition.Allows"/>. Nothing branches on a rank id, so a
    /// guild with five custom ranks needs no code change, and a client that sends a request
    /// its UI hid is refused all the same.
    ///
    /// <b>Seniority is checked as well as permission.</b> An officer who may kick still
    /// cannot kick the leader or another officer: permission says what kind of act is
    /// allowed, <see cref="GuildRankDefinition.Order"/> says whom it may be aimed at. Without
    /// the second check, one permission would let a guild eat itself.
    ///
    /// <b>Validate fully, then mutate.</b> A refused operation leaves the guild
    /// byte-identical, and leadership transfer moves both ranks in a single step.
    /// </remarks>
    public static class GuildService
    {
        /// <summary>Everything a guild operation needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<GuildRankDefinition> ranks,
                SocialConfiguration configuration = null,
                IGuildNameAuthority names = null,
                GuildDirectory directory = null,
                long timestampTicks = 0L)
            {
                Ranks = ranks;
                Limits = SocialConfiguration.Resolve(configuration);
                Names = names;
                Directory = directory;
                TimestampTicks = timestampTicks;
            }

            public IDefinitionRegistry<GuildRankDefinition> Ranks { get; }

            public SocialConfiguration.Limits Limits { get; }

            /// <summary>Where name uniqueness is decided. Optional; see the interface.</summary>
            public IGuildNameAuthority Names { get; }

            /// <summary>Who belongs to which guild. Optional.</summary>
            public GuildDirectory Directory { get; }

            public long TimestampTicks { get; }

            public bool IsUsable => Ranks != null;
        }

        // ---- creation ------------------------------------------------------------------

        /// <summary>
        /// Creates a guild with its founder as leader.
        /// </summary>
        /// <remarks>The name is validated for shape locally and for availability through the
        /// authority, and only claimed once the guild actually exists -- so a refused
        /// creation does not reserve a name nobody is using.</remarks>
        public static GuildResult TryCreate(CharacterId founder, string name,
            DefinitionId leaderRank, in Context context)
        {
            if (!context.IsUsable) return GuildResult.Rejected(GuildRejection.MissingContext);
            if (!founder.IsValid) return GuildResult.Rejected(GuildRejection.UnknownCharacter);

            GuildRankDefinition rank;
            if (!context.Ranks.TryGet(leaderRank, out rank) || rank == null)
                return GuildResult.Rejected(GuildRejection.UnknownRank);

            GuildRejection nameCheck = ValidateName(name, context);
            if (nameCheck != GuildRejection.None) return GuildResult.Rejected(nameCheck);

            if (context.Directory != null && context.Directory.IsInAGuild(founder))
                return GuildResult.Rejected(GuildRejection.AlreadyInAnotherGuild);

            // ---- everything is resolved and nothing below can fail ---------------------

            var guild = new GuildState(GuildId.New(), name, founder, leaderRank,
                context.TimestampTicks);

            if (context.Names != null) context.Names.Claim(name, guild.Id);
            if (context.Directory != null) context.Directory.Register(guild);

            return GuildResult.Accepted(guild, founder, leaderRank);
        }

        /// <summary>
        /// Whether a name may be used.
        /// </summary>
        /// <remarks>
        /// Shape first, then the authority. Length bounds are authored on
        /// <see cref="SocialConfiguration"/>; the character rule is letters, digits, spaces
        /// and a small punctuation set, which is restrictive on purpose -- a name is
        /// displayed to other players and rendered by a font that may not have every glyph.
        /// </remarks>
        public static GuildRejection ValidateName(string name, in Context context)
        {
            if (string.IsNullOrWhiteSpace(name)) return GuildRejection.InvalidName;

            string trimmed = name.Trim();

            if (trimmed.Length < context.Limits.MinGuildNameLength) return GuildRejection.InvalidName;
            if (trimmed.Length > context.Limits.MaxGuildNameLength) return GuildRejection.InvalidName;

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!IsAllowedNameCharacter(trimmed[i])) return GuildRejection.InvalidName;
            }

            if (context.Names == null) return GuildRejection.None;

            if (context.Names.IsReserved(trimmed)) return GuildRejection.NameReserved;

            return context.Names.IsAvailable(trimmed)
                ? GuildRejection.None
                : GuildRejection.NameTaken;
        }

        private static bool IsAllowedNameCharacter(char c)
        {
            if (char.IsLetterOrDigit(c)) return true;

            return c == ' ' || c == '-' || c == '\'' || c == '.';
        }

        // ---- membership ----------------------------------------------------------------

        /// <summary>Opens an invitation. Needs <see cref="GuildPermission.Invite"/>.</summary>
        public static GuildResult TryInvite(GuildState guild, CharacterId inviter,
            CharacterId target, in Context context)
        {
            GuildRejection allowed = CheckPermission(guild, inviter, GuildPermission.Invite,
                context);

            if (allowed != GuildRejection.None) return GuildResult.Rejected(allowed, guild);

            if (!target.IsValid)
                return GuildResult.Rejected(GuildRejection.UnknownCharacter, guild);

            if (guild.Contains(target))
                return GuildResult.Rejected(GuildRejection.AlreadyAMember, guild);

            if (guild.MemberCount >= context.Limits.MaxGuildMembers)
                return GuildResult.Rejected(GuildRejection.GuildFull, guild);

            if (context.Directory != null && context.Directory.IsInAGuild(target))
                return GuildResult.Rejected(GuildRejection.AlreadyInAnotherGuild, guild);

            var invite = new GuildInvite(InstanceId.New(), guild.Id, inviter, target,
                context.TimestampTicks);

            if (context.Directory != null) context.Directory.Register(invite);

            return GuildResult.Accepted(guild, target, default, invite);
        }

        /// <summary>
        /// Accepts an invitation and joins at a rank.
        /// </summary>
        /// <remarks>Everything is re-checked here rather than trusted from when the
        /// invitation was sent, and the invitation is settled in the same step, so a
        /// duplicate accept adds nobody.</remarks>
        public static GuildResult TryAccept(GuildInvite invite, GuildState guild,
            CharacterId accepter, DefinitionId memberRank, in Context context)
        {
            if (invite == null || guild == null || !context.IsUsable)
                return GuildResult.Rejected(GuildRejection.MissingContext);

            if (invite.Target != accepter)
                return GuildResult.Rejected(GuildRejection.NotTheInvitee, guild);

            if (!invite.IsPending)
                return GuildResult.Rejected(GuildRejection.InviteNotPending, guild);

            if (invite.HasExpired(context.TimestampTicks))
            {
                invite.Resolve(GuildInviteState.Expired);
                return GuildResult.Rejected(GuildRejection.InviteExpired, guild);
            }

            if (invite.Guild != guild.Id)
                return GuildResult.Rejected(GuildRejection.MissingContext, guild);

            if (!guild.IsActive) return GuildResult.Rejected(GuildRejection.GuildInactive, guild);

            if (guild.Contains(accepter))
                return GuildResult.Rejected(GuildRejection.AlreadyAMember, guild);

            if (guild.MemberCount >= context.Limits.MaxGuildMembers)
                return GuildResult.Rejected(GuildRejection.GuildFull, guild);

            GuildRankDefinition rank;
            if (!context.Ranks.TryGet(memberRank, out rank) || rank == null)
                return GuildResult.Rejected(GuildRejection.UnknownRank, guild);

            if (context.Directory != null && context.Directory.IsInAGuild(accepter))
                return GuildResult.Rejected(GuildRejection.AlreadyInAnotherGuild, guild);

            // ---- everything is resolved and nothing below can fail ---------------------

            invite.Resolve(GuildInviteState.Accepted);
            guild.TryAdd(accepter, memberRank, context.TimestampTicks);

            if (context.Directory != null) context.Directory.Register(guild);

            return GuildResult.Accepted(guild, accepter, memberRank, invite);
        }

        /// <summary>Declines an invitation. The guild is untouched.</summary>
        public static GuildResult TryReject(GuildInvite invite, CharacterId rejecter,
            GuildState guild = null)
        {
            if (invite == null) return GuildResult.Rejected(GuildRejection.MissingContext);

            if (invite.Target != rejecter)
                return GuildResult.Rejected(GuildRejection.NotTheInvitee, guild);

            if (!invite.Resolve(GuildInviteState.Rejected))
                return GuildResult.Rejected(GuildRejection.InviteNotPending, guild);

            return GuildResult.Accepted(guild, rejecter, default, invite);
        }

        /// <summary>
        /// Leaves a guild.
        /// </summary>
        /// <remarks>A leader may not simply leave: they transfer leadership or disband, both
        /// of which are explicit. Silently promoting somebody would hand a guild to whoever
        /// happened to be listed next.</remarks>
        public static GuildResult TryLeave(GuildState guild, CharacterId character,
            in Context context)
        {
            if (guild == null) return GuildResult.Rejected(GuildRejection.MissingContext);
            if (!guild.IsActive) return GuildResult.Rejected(GuildRejection.GuildInactive, guild);

            if (!guild.Contains(character))
                return GuildResult.Rejected(GuildRejection.NotAMember, guild);

            if (guild.IsLeader(character))
                return GuildResult.Rejected(GuildRejection.InvalidTarget, guild);

            guild.TryRemove(character);
            if (context.Directory != null) context.Directory.Forget(character);

            return GuildResult.Accepted(guild, character);
        }

        /// <summary>Removes another member. Needs <see cref="GuildPermission.Kick"/> and seniority.</summary>
        public static GuildResult TryKick(GuildState guild, CharacterId actor, CharacterId target,
            in Context context)
        {
            GuildRejection allowed = CheckPermission(guild, actor, GuildPermission.Kick, context);
            if (allowed != GuildRejection.None) return GuildResult.Rejected(allowed, guild);

            if (actor == target) return GuildResult.Rejected(GuildRejection.InvalidTarget, guild);

            if (!guild.Contains(target))
                return GuildResult.Rejected(GuildRejection.NotAMember, guild);

            if (guild.IsLeader(target))
                return GuildResult.Rejected(GuildRejection.InsufficientSeniority, guild);

            GuildRejection senior = CheckSeniority(guild, actor, target, context);
            if (senior != GuildRejection.None) return GuildResult.Rejected(senior, guild);

            guild.TryRemove(target);
            if (context.Directory != null) context.Directory.Forget(target);

            return GuildResult.Accepted(guild, target);
        }

        // ---- ranks ---------------------------------------------------------------------

        /// <summary>
        /// Moves a member up one rank.
        /// </summary>
        /// <remarks>Never to the leader's rank: becoming leader is a transfer, which moves
        /// two people at once. Promoting into it would leave the guild with two leader ranks
        /// and one <see cref="GuildState.Leader"/>.</remarks>
        public static GuildResult TryPromote(GuildState guild, CharacterId actor,
            CharacterId target, in Context context)
        {
            return TryChangeRank(guild, actor, target, GuildPermission.Promote, true, context);
        }

        /// <summary>Moves a member down one rank.</summary>
        public static GuildResult TryDemote(GuildState guild, CharacterId actor,
            CharacterId target, in Context context)
        {
            return TryChangeRank(guild, actor, target, GuildPermission.Demote, false, context);
        }

        private static GuildResult TryChangeRank(GuildState guild, CharacterId actor,
            CharacterId target, GuildPermission permission, bool up, in Context context)
        {
            GuildRejection allowed = CheckPermission(guild, actor, permission, context);
            if (allowed != GuildRejection.None) return GuildResult.Rejected(allowed, guild);

            if (actor == target) return GuildResult.Rejected(GuildRejection.InvalidTarget, guild);

            if (!guild.Contains(target))
                return GuildResult.Rejected(GuildRejection.NotAMember, guild);

            if (guild.IsLeader(target))
                return GuildResult.Rejected(GuildRejection.InsufficientSeniority, guild);

            GuildRejection senior = CheckSeniority(guild, actor, target, context);
            if (senior != GuildRejection.None) return GuildResult.Rejected(senior, guild);

            GuildRankDefinition current;
            if (!context.Ranks.TryGet(guild.RankOf(target), out current) || current == null)
                return GuildResult.Rejected(GuildRejection.UnknownRank, guild);

            GuildRankDefinition next = Adjacent(current, up, context);
            if (next == null) return GuildResult.Rejected(GuildRejection.RankUnavailable, guild);

            // A promotion must not reach the leader's rank; that is a transfer.
            if (up && next.IsLeaderRank)
                return GuildResult.Rejected(GuildRejection.RankUnavailable, guild);

            // Nor may it overtake the person granting it.
            GuildRankDefinition actorRank;
            context.Ranks.TryGet(guild.RankOf(actor), out actorRank);

            if (up && actorRank != null && next.Order >= actorRank.Order)
                return GuildResult.Rejected(GuildRejection.InsufficientSeniority, guild);

            guild.TrySetRank(target, next.Id);

            return GuildResult.Accepted(guild, target, next.Id);
        }

        /// <summary>
        /// The next rank up or down from one, by authored order.
        /// </summary>
        /// <remarks>Nearest neighbour rather than a list position, so ranks may be authored
        /// in any order and a gap in the numbering is not a missing rank.</remarks>
        private static GuildRankDefinition Adjacent(GuildRankDefinition from, bool up,
            in Context context)
        {
            IReadOnlyList<GuildRankDefinition> all = context.Ranks.All;
            GuildRankDefinition best = null;

            for (int i = 0; i < all.Count; i++)
            {
                GuildRankDefinition candidate = all[i];
                if (candidate == null || candidate.Id == from.Id) continue;

                if (up)
                {
                    if (candidate.Order <= from.Order) continue;
                    if (best == null || candidate.Order < best.Order) best = candidate;
                }
                else
                {
                    if (candidate.Order >= from.Order) continue;
                    if (best == null || candidate.Order > best.Order) best = candidate;
                }
            }

            return best;
        }

        // ---- leadership ----------------------------------------------------------------

        /// <summary>
        /// Hands the guild to another member.
        /// </summary>
        /// <remarks>The former leader drops to the rank supplied, and both moves happen in
        /// one call on the state, so there is no moment with two leaders or none.</remarks>
        public static GuildResult TryTransferLeadership(GuildState guild, CharacterId leader,
            CharacterId target, DefinitionId formerLeaderRank, in Context context)
        {
            GuildRejection allowed = CheckPermission(guild, leader,
                GuildPermission.TransferLeadership, context);

            if (allowed != GuildRejection.None) return GuildResult.Rejected(allowed, guild);

            if (!guild.IsLeader(leader))
                return GuildResult.Rejected(GuildRejection.PermissionDenied, guild);

            if (leader == target) return GuildResult.Rejected(GuildRejection.InvalidTarget, guild);

            if (!guild.Contains(target))
                return GuildResult.Rejected(GuildRejection.NotAMember, guild);

            DefinitionId leaderRank = guild.RankOf(leader);

            GuildRankDefinition demoted;
            if (!context.Ranks.TryGet(formerLeaderRank, out demoted) || demoted == null)
                return GuildResult.Rejected(GuildRejection.UnknownRank, guild);

            guild.TryTransferLeadership(target, leaderRank, formerLeaderRank);

            return GuildResult.Accepted(guild, target, leaderRank);
        }

        /// <summary>Ends the guild. Needs <see cref="GuildPermission.Disband"/>.</summary>
        public static GuildResult TryDisband(GuildState guild, CharacterId actor,
            in Context context)
        {
            GuildRejection allowed = CheckPermission(guild, actor, GuildPermission.Disband,
                context);

            if (allowed != GuildRejection.None) return GuildResult.Rejected(allowed, guild);

            if (!guild.IsLeader(actor))
                return GuildResult.Rejected(GuildRejection.PermissionDenied, guild);

            if (context.Names != null) context.Names.Release(guild.Name);
            if (context.Directory != null) context.Directory.Dissolve(guild);

            guild.Disband();

            return GuildResult.Accepted(guild, actor);
        }

        // ---- permissions ---------------------------------------------------------------

        /// <summary>
        /// Whether a character's rank carries a permission.
        /// </summary>
        /// <remarks>What a panel asks to decide whether to offer a button, so the UI and the
        /// service read the same answer from the same place rather than each deciding.</remarks>
        public static bool HasPermission(GuildState guild, CharacterId character,
            GuildPermission permission, in Context context)
        {
            return CheckPermission(guild, character, permission, context) == GuildRejection.None;
        }

        private static GuildRejection CheckPermission(GuildState guild, CharacterId character,
            GuildPermission permission, in Context context)
        {
            if (guild == null || !context.IsUsable) return GuildRejection.MissingContext;
            if (!guild.IsActive) return GuildRejection.GuildInactive;
            if (!guild.Contains(character)) return GuildRejection.NotAMember;

            GuildRankDefinition rank;
            if (!context.Ranks.TryGet(guild.RankOf(character), out rank) || rank == null)
                return GuildRejection.UnknownRank;

            return rank.Allows(permission)
                ? GuildRejection.None
                : GuildRejection.PermissionDenied;
        }

        /// <summary>
        /// Whether the actor outranks the target.
        /// </summary>
        /// <remarks>Permission says what kind of act is allowed; this says whom it may be
        /// aimed at. Without it, one officer could kick every other officer.</remarks>
        private static GuildRejection CheckSeniority(GuildState guild, CharacterId actor,
            CharacterId target, in Context context)
        {
            GuildRankDefinition actorRank;
            GuildRankDefinition targetRank;

            if (!context.Ranks.TryGet(guild.RankOf(actor), out actorRank) || actorRank == null)
                return GuildRejection.UnknownRank;

            if (!context.Ranks.TryGet(guild.RankOf(target), out targetRank) || targetRank == null)
                return GuildRejection.UnknownRank;

            return actorRank.Order > targetRank.Order
                ? GuildRejection.None
                : GuildRejection.InsufficientSeniority;
        }
    }

    /// <summary>
    /// Who belongs to which guild, and which invitations are open.
    /// </summary>
    /// <remarks>The index that makes "one character, one guild" answerable, for the reason
    /// <see cref="PartyDirectory"/> exists. Runtime, because it is derived from rows a
    /// database already holds.</remarks>
    public sealed class GuildDirectory : IRuntimeState
    {
        private readonly Dictionary<CharacterId, GuildId> _membership =
            new Dictionary<CharacterId, GuildId>();

        private readonly List<GuildInvite> _invites = new List<GuildInvite>();

        private Revision _revision;

        public Revision Revision => _revision;

        public int TrackedCharacters => _membership.Count;

        public IReadOnlyList<GuildInvite> Invites => _invites;

        public bool IsInAGuild(CharacterId character)
        {
            return character.IsValid && _membership.ContainsKey(character);
        }

        public GuildId GuildOf(CharacterId character)
        {
            GuildId guild;
            return _membership.TryGetValue(character, out guild) ? guild : GuildId.None;
        }

        public void Register(GuildState guild)
        {
            if (guild == null) return;

            IReadOnlyList<GuildMember> members = guild.Members;

            for (int i = 0; i < members.Count; i++) _membership[members[i].Character] = guild.Id;

            _revision = _revision.Next();
        }

        public void Register(GuildInvite invite)
        {
            if (invite == null) return;

            _invites.Add(invite);
            _revision = _revision.Next();
        }

        public void Forget(CharacterId character)
        {
            if (!_membership.Remove(character)) return;

            _revision = _revision.Next();
        }

        public void Dissolve(GuildState guild)
        {
            if (guild == null) return;

            IReadOnlyList<GuildMember> members = guild.Members;

            for (int i = 0; i < members.Count; i++) _membership.Remove(members[i].Character);

            for (int i = 0; i < _invites.Count; i++)
            {
                if (_invites[i].Guild != guild.Id || !_invites[i].IsPending) continue;
                _invites[i].Resolve(GuildInviteState.Cancelled);
            }

            _revision = _revision.Next();
        }
    }

    /// <summary>
    /// A local guild-name register.
    /// </summary>
    /// <remarks>
    /// <b>A pre-check, never the authority.</b> It knows only the names this process has
    /// seen. Shard-wide uniqueness belongs to the database, behind
    /// <see cref="IGuildNameAuthority"/>, and this exists so the domain and its tests have a
    /// working implementation without pretending to be one.
    ///
    /// Comparison is case-insensitive and trimmed, because two guilds differing only in
    /// capitalisation are indistinguishable to a player reading a list.
    /// </remarks>
    public sealed class LocalGuildNameRegister : IGuildNameAuthority
    {
        private readonly Dictionary<string, GuildId> _claimed =
            new Dictionary<string, GuildId>(System.StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _reserved =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public int ClaimedCount => _claimed.Count;

        /// <summary>Marks a name as never usable. Content or an operator supplies these.</summary>
        public void Reserve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _reserved.Add(name.Trim());
        }

        public bool IsAvailable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return !_claimed.ContainsKey(name.Trim());
        }

        public bool IsReserved(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return _reserved.Contains(name.Trim());
        }

        public void Claim(string name, GuildId guild)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _claimed[name.Trim()] = guild;
        }

        public void Release(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _claimed.Remove(name.Trim());
        }
    }
}
