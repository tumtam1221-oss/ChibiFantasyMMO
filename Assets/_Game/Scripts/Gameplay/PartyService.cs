using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a party operation was refused.</summary>
    public enum PartyRejection
    {
        None = 0,

        /// <summary>No party, no character or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The party has been disbanded.</summary>
        PartyInactive = 2,

        /// <summary>The acting character is not in this party.</summary>
        NotAMember = 3,

        /// <summary>The operation is the leader's alone.</summary>
        NotLeader = 4,

        /// <summary>The party already holds as many members as it may.</summary>
        PartyFull = 5,

        /// <summary>The target is already in this party.</summary>
        AlreadyAMember = 6,

        /// <summary>The target already belongs to another party.</summary>
        AlreadyInAnotherParty = 7,

        /// <summary>No such invitation, or it has already been settled.</summary>
        InviteNotPending = 8,

        /// <summary>The invitation has lapsed.</summary>
        InviteExpired = 9,

        /// <summary>The invitation is not this character's to answer.</summary>
        NotTheInvitee = 10,

        /// <summary>An invitation to this target from this party is already open.</summary>
        InviteAlreadyOpen = 11,

        /// <summary>The target could not be resolved, or names nobody.</summary>
        UnknownCharacter = 12,

        /// <summary>The target is not somebody a party may act on right now.</summary>
        Ineligible = 13,

        /// <summary>A leader cannot transfer leadership to themselves, or kick themselves.</summary>
        InvalidTarget = 14
    }

    /// <summary>What a party operation did.</summary>
    public readonly struct PartyResult
    {
        private PartyResult(bool accepted, PartyRejection reason, PartyState party,
            CharacterId subject, PartyInvite invite)
        {
            IsAccepted = accepted;
            Reason = reason;
            Party = party;
            Subject = subject;
            Invite = invite;
        }

        public bool IsAccepted { get; }

        public PartyRejection Reason { get; }

        /// <summary>The party operated on. Null when creation itself failed.</summary>
        public PartyState Party { get; }

        /// <summary>Who the operation was about: the invitee, the kicked, the new leader.</summary>
        public CharacterId Subject { get; }

        /// <summary>The invitation created or settled, when there was one.</summary>
        public PartyInvite Invite { get; }

        public PartyId PartyId => Party == null ? Core.PartyId.None : Party.Id;

        public Revision Revision => Party == null ? Revision.Initial : Party.Revision;

        public static PartyResult Accepted(PartyState party, CharacterId subject = default,
            PartyInvite invite = null)
        {
            return new PartyResult(true, PartyRejection.None, party, subject, invite);
        }

        public static PartyResult Rejected(PartyRejection reason, PartyState party = null)
        {
            return new PartyResult(false, reason, party, default, null);
        }

        public override string ToString()
        {
            return IsAccepted ? "party " + PartyId + " ok" : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Forming and running a party.
    /// </summary>
    /// <remarks>
    /// <b>The domain enforces permissions, not the UI.</b> Every leader-only operation
    /// checks <see cref="PartyState.IsLeader"/> here. A panel that hides a button is a
    /// convenience; this is the rule, and a client that sends the request anyway is refused.
    ///
    /// <b>Validate fully, then mutate.</b> Membership, capacity, leadership and invitation
    /// state are all checked before anything is written, so a refused operation leaves the
    /// party byte-identical.
    ///
    /// <b>Capacity is authored.</b> Six is <see cref="SocialConfiguration.DefaultMaxPartySize"/>
    /// and nothing else in this file names a number. There is no six-player party type.
    ///
    /// <b>One party per character.</b> Enforced through a membership index the caller keeps,
    /// so a character cannot be quietly added to a second party while still in the first.
    /// </remarks>
    public static class PartyService
    {
        /// <summary>
        /// Everything a party operation needs.
        /// </summary>
        /// <remarks>
        /// <see cref="Directory"/> is what makes "one party per character" answerable. A
        /// caller with none gets every other rule; the membership check is simply skipped,
        /// which is less information rather than a wrong answer.
        /// </remarks>
        public readonly struct Context
        {
            public Context(SocialConfiguration configuration = null, PartyDirectory directory = null,
                long timestampTicks = 0L)
            {
                Limits = SocialConfiguration.Resolve(configuration);
                Directory = directory;
                TimestampTicks = timestampTicks;
            }

            public SocialConfiguration.Limits Limits { get; }

            /// <summary>Who is in which party. Optional.</summary>
            public PartyDirectory Directory { get; }

            /// <summary>Caller-supplied time, for invitation expiry.</summary>
            public long TimestampTicks { get; }
        }

        /// <summary>
        /// Creates a party with one member.
        /// </summary>
        /// <remarks>The leader is the first member, so a party is never created empty and
        /// never created leaderless.</remarks>
        public static PartyResult TryCreate(CharacterId leader, in Context context)
        {
            if (!leader.IsValid) return PartyResult.Rejected(PartyRejection.UnknownCharacter);

            if (context.Directory != null && context.Directory.IsInAParty(leader))
                return PartyResult.Rejected(PartyRejection.AlreadyInAnotherParty);

            var party = new PartyState(PartyId.New(), leader, context.Limits.DefaultLootPolicy,
                context.TimestampTicks);

            if (context.Directory != null) context.Directory.Register(party);

            return PartyResult.Accepted(party, leader);
        }

        /// <summary>
        /// Opens an invitation.
        /// </summary>
        /// <remarks>
        /// The party must have room <em>now</em>, which is a courtesy rather than a
        /// guarantee: capacity is checked again on acceptance, because members can join
        /// between the two.
        ///
        /// A second open invitation from the same party to the same person is refused rather
        /// than stacked, so accepting cannot add a member twice.
        /// </remarks>
        public static PartyResult TryInvite(PartyState party, CharacterId inviter,
            CharacterId target, in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!target.IsValid)
                return PartyResult.Rejected(PartyRejection.UnknownCharacter, party);

            if (!party.Contains(inviter))
                return PartyResult.Rejected(PartyRejection.NotAMember, party);

            if (!party.IsLeader(inviter))
                return PartyResult.Rejected(PartyRejection.NotLeader, party);

            if (party.Contains(target))
                return PartyResult.Rejected(PartyRejection.AlreadyAMember, party);

            if (party.MemberCount >= context.Limits.MaxPartySize)
                return PartyResult.Rejected(PartyRejection.PartyFull, party);

            if (context.Directory != null)
            {
                if (context.Directory.IsInAParty(target))
                    return PartyResult.Rejected(PartyRejection.AlreadyInAnotherParty, party);

                if (context.Directory.HasOpenInvite(party.Id, target, context.TimestampTicks))
                    return PartyResult.Rejected(PartyRejection.InviteAlreadyOpen, party);
            }

            var invite = new PartyInvite(InstanceId.New(), party.Id, inviter, target,
                context.TimestampTicks);

            if (context.Directory != null) context.Directory.Register(invite);

            return PartyResult.Accepted(party, target, invite);
        }

        /// <summary>
        /// Accepts an invitation and joins.
        /// </summary>
        /// <remarks>
        /// Everything is re-checked here, not trusted from when the invitation was sent: the
        /// party may have filled up, been disbanded, or the invitee may have joined somebody
        /// else. The invitation is settled and the member added in one step that cannot half
        /// happen -- the settle is refused if it was already settled, so a duplicate accept
        /// adds nobody.
        /// </remarks>
        public static PartyResult TryAccept(PartyInvite invite, PartyState party,
            CharacterId accepter, in Context context)
        {
            if (invite == null || party == null)
                return PartyResult.Rejected(PartyRejection.MissingContext);

            if (invite.Target != accepter)
                return PartyResult.Rejected(PartyRejection.NotTheInvitee, party);

            if (!invite.IsPending)
                return PartyResult.Rejected(PartyRejection.InviteNotPending, party);

            if (invite.HasExpired(context.TimestampTicks))
            {
                invite.Resolve(PartyInviteState.Expired);
                return PartyResult.Rejected(PartyRejection.InviteExpired, party);
            }

            if (invite.Party != party.Id)
                return PartyResult.Rejected(PartyRejection.MissingContext, party);

            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (party.Contains(accepter))
                return PartyResult.Rejected(PartyRejection.AlreadyAMember, party);

            if (party.MemberCount >= context.Limits.MaxPartySize)
                return PartyResult.Rejected(PartyRejection.PartyFull, party);

            if (context.Directory != null && context.Directory.IsInAParty(accepter))
                return PartyResult.Rejected(PartyRejection.AlreadyInAnotherParty, party);

            // ---- everything is resolved and nothing below can fail ---------------------

            invite.Resolve(PartyInviteState.Accepted);
            party.TryAdd(accepter);

            if (context.Directory != null) context.Directory.Register(party);

            return PartyResult.Accepted(party, accepter, invite);
        }

        /// <summary>Declines an invitation. The party is untouched.</summary>
        public static PartyResult TryReject(PartyInvite invite, CharacterId rejecter,
            PartyState party = null)
        {
            if (invite == null) return PartyResult.Rejected(PartyRejection.MissingContext);

            if (invite.Target != rejecter)
                return PartyResult.Rejected(PartyRejection.NotTheInvitee, party);

            if (!invite.Resolve(PartyInviteState.Rejected))
                return PartyResult.Rejected(PartyRejection.InviteNotPending, party);

            return PartyResult.Accepted(party, rejecter, invite);
        }

        /// <summary>
        /// Leaves a party.
        /// </summary>
        /// <remarks>
        /// A leader leaving a party with other members hands leadership to the next member
        /// first, so the party is never leaderless. A leader leaving alone disbands it,
        /// because a party of nobody is not a party.
        /// </remarks>
        public static PartyResult TryLeave(PartyState party, CharacterId character,
            in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!party.Contains(character))
                return PartyResult.Rejected(PartyRejection.NotAMember, party);

            if (!party.IsLeader(character))
            {
                party.TryRemove(character);
                if (context.Directory != null) context.Directory.Forget(character);
                return PartyResult.Accepted(party, character);
            }

            if (party.MemberCount == 1) return TryDisband(party, character, context);

            CharacterId successor = NextAfterLeader(party);

            party.TryTransferLeadership(successor);
            party.TryRemove(character);

            if (context.Directory != null) context.Directory.Forget(character);

            return PartyResult.Accepted(party, character);
        }

        /// <summary>Removes another member. The leader's alone.</summary>
        public static PartyResult TryKick(PartyState party, CharacterId leader, CharacterId target,
            in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!party.Contains(leader)) return PartyResult.Rejected(PartyRejection.NotAMember, party);
            if (!party.IsLeader(leader)) return PartyResult.Rejected(PartyRejection.NotLeader, party);

            if (leader == target) return PartyResult.Rejected(PartyRejection.InvalidTarget, party);

            if (!party.Contains(target))
                return PartyResult.Rejected(PartyRejection.NotAMember, party);

            party.TryRemove(target);
            if (context.Directory != null) context.Directory.Forget(target);

            return PartyResult.Accepted(party, target);
        }

        /// <summary>Hands leadership to another member.</summary>
        public static PartyResult TryTransferLeadership(PartyState party, CharacterId leader,
            CharacterId target, in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!party.IsLeader(leader)) return PartyResult.Rejected(PartyRejection.NotLeader, party);
            if (leader == target) return PartyResult.Rejected(PartyRejection.InvalidTarget, party);

            if (!party.Contains(target))
                return PartyResult.Rejected(PartyRejection.NotAMember, party);

            party.TryTransferLeadership(target);

            return PartyResult.Accepted(party, target);
        }

        /// <summary>Ends the party. The leader's alone.</summary>
        public static PartyResult TryDisband(PartyState party, CharacterId leader,
            in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!party.IsLeader(leader)) return PartyResult.Rejected(PartyRejection.NotLeader, party);

            if (context.Directory != null) context.Directory.Dissolve(party);

            party.Disband();

            return PartyResult.Accepted(party, leader);
        }

        /// <summary>Changes how the party assigns loot. The leader's alone.</summary>
        public static PartyResult TrySetLootPolicy(PartyState party, CharacterId leader,
            PartyLootPolicy policy, in Context context)
        {
            if (party == null) return PartyResult.Rejected(PartyRejection.MissingContext);
            if (!party.IsActive) return PartyResult.Rejected(PartyRejection.PartyInactive, party);

            if (!party.IsLeader(leader)) return PartyResult.Rejected(PartyRejection.NotLeader, party);

            party.TrySetLootPolicy(policy);

            return PartyResult.Accepted(party, leader);
        }

        /// <summary>The member who takes over when a leader leaves without naming a successor.</summary>
        /// <remarks>The next in join order. Deterministic, so a server and a client agree
        /// without replicating a choice.</remarks>
        private static CharacterId NextAfterLeader(PartyState party)
        {
            IReadOnlyList<CharacterId> members = party.Members;

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != party.Leader) return members[i];
            }

            return CharacterId.None;
        }
    }

    /// <summary>
    /// Who is in which party, and which invitations are open.
    /// </summary>
    /// <remarks>
    /// <b>The index that makes "one party per character" answerable.</b> Without it, nothing
    /// could tell that an invitee already belongs somewhere, and a character could sit in
    /// two parties at once.
    ///
    /// Runtime, not persistent: it is derived from the party rows a database already holds,
    /// so storing it as well would be a second copy of the same fact.
    /// </remarks>
    public sealed class PartyDirectory : IRuntimeState
    {
        private readonly Dictionary<CharacterId, PartyId> _membership =
            new Dictionary<CharacterId, PartyId>();

        private readonly List<PartyInvite> _invites = new List<PartyInvite>();

        private Revision _revision;

        public Revision Revision => _revision;

        public int TrackedCharacters => _membership.Count;

        public IReadOnlyList<PartyInvite> Invites => _invites;

        public bool IsInAParty(CharacterId character)
        {
            return character.IsValid && _membership.ContainsKey(character);
        }

        public PartyId PartyOf(CharacterId character)
        {
            PartyId party;
            return _membership.TryGetValue(character, out party) ? party : PartyId.None;
        }

        /// <summary>Records every current member of a party.</summary>
        public void Register(PartyState party)
        {
            if (party == null) return;

            IReadOnlyList<CharacterId> members = party.Members;

            for (int i = 0; i < members.Count; i++) _membership[members[i]] = party.Id;

            _revision = _revision.Next();
        }

        public void Register(PartyInvite invite)
        {
            if (invite == null) return;

            _invites.Add(invite);
            _revision = _revision.Next();
        }

        /// <summary>Whether a pending, unexpired invitation to this target from this party exists.</summary>
        public bool HasOpenInvite(PartyId party, CharacterId target, long nowTicks)
        {
            for (int i = 0; i < _invites.Count; i++)
            {
                PartyInvite invite = _invites[i];

                if (invite.Party != party || invite.Target != target) continue;
                if (!invite.IsPending || invite.HasExpired(nowTicks)) continue;

                return true;
            }

            return false;
        }

        public void Forget(CharacterId character)
        {
            if (!_membership.Remove(character)) return;

            _revision = _revision.Next();
        }

        /// <summary>Forgets every member of a party, and cancels its open invitations.</summary>
        public void Dissolve(PartyState party)
        {
            if (party == null) return;

            IReadOnlyList<CharacterId> members = party.Members;

            for (int i = 0; i < members.Count; i++) _membership.Remove(members[i]);

            for (int i = 0; i < _invites.Count; i++)
            {
                if (_invites[i].Party != party.Id || !_invites[i].IsPending) continue;
                _invites[i].Resolve(PartyInviteState.Cancelled);
            }

            _revision = _revision.Next();
        }
    }
}
