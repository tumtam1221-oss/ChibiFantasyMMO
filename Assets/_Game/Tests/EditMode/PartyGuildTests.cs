using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Parties and guilds: membership, invitations, permissions and leadership.
    /// </summary>
    /// <remarks>
    /// Two invariants carry most of these tests. No membership list may ever hold a duplicate,
    /// and no group may exist without a leader. Both are enforced in the state rather than
    /// only in the service, so several tests reach past the service on purpose to prove the
    /// structure holds on its own.
    /// </remarks>
    [TestFixture]
    internal sealed class PartyGuildTests : SocialTestBase
    {
        // ---- party: creation and membership --------------------------------------------

        [Test]
        public void Creating_a_party_makes_the_founder_its_leader_and_only_member()
        {
            PartyResult result = PartyService.TryCreate(Alice, PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Party.Leader, Is.EqualTo(Alice));
            Assert.That(result.Party.MemberCount, Is.EqualTo(1));
            Assert.That(result.Party.Contains(Alice), Is.True);
            Assert.That(result.Party.Id.IsValid, Is.True);
        }

        [Test]
        public void A_party_is_named_by_its_id_not_by_its_leader()
        {
            PartyState party = PartyOf(Alice, Bob);
            PartyId before = party.Id;

            PartyService.TryTransferLeadership(party, Alice, Bob, PartyContext());

            Assert.That(party.Id, Is.EqualTo(before),
                "changing leader must not change which party this is");
        }

        [Test]
        public void An_invited_player_joins_on_accepting()
        {
            PartyState party = PartyOf(Alice);

            PartyResult invited = PartyService.TryInvite(party, Alice, Bob, PartyContext());

            Assert.That(invited.IsAccepted, Is.True);
            Assert.That(invited.Invite.IsPending, Is.True);
            Assert.That(party.Contains(Bob), Is.False, "inviting is not joining");

            PartyResult accepted = PartyService.TryAccept(invited.Invite, party, Bob,
                PartyContext());

            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(party.Contains(Bob), Is.True);
            Assert.That(invited.Invite.State, Is.EqualTo(PartyInviteState.Accepted));
        }

        [Test]
        public void Rejecting_an_invitation_leaves_the_party_alone()
        {
            PartyState party = PartyOf(Alice);
            PartyResult invited = PartyService.TryInvite(party, Alice, Bob, PartyContext());

            Revision before = party.Revision;

            PartyResult rejected = PartyService.TryReject(invited.Invite, Bob, party);

            Assert.That(rejected.IsAccepted, Is.True);
            Assert.That(invited.Invite.State, Is.EqualTo(PartyInviteState.Rejected));
            Assert.That(party.Contains(Bob), Is.False);
            Assert.That(party.Revision, Is.EqualTo(before));
        }

        [Test]
        public void An_invitation_can_only_be_accepted_once()
        {
            PartyState party = PartyOf(Alice);
            PartyResult invited = PartyService.TryInvite(party, Alice, Bob, PartyContext());

            PartyService.TryAccept(invited.Invite, party, Bob, PartyContext());

            PartyResult again = PartyService.TryAccept(invited.Invite, party, Bob, PartyContext());

            Assert.That(again.IsAccepted, Is.False);
            Assert.That(party.MemberCount, Is.EqualTo(2), "a repeated accept adds nobody");
        }

        [Test]
        public void A_second_open_invitation_to_the_same_player_is_refused()
        {
            PartyState party = PartyOf(Alice);

            PartyService.TryInvite(party, Alice, Bob, PartyContext());

            PartyResult second = PartyService.TryInvite(party, Alice, Bob, PartyContext());

            Assert.That(second.Reason, Is.EqualTo(PartyRejection.InviteAlreadyOpen));
        }

        [Test]
        public void Only_the_invitee_may_answer_an_invitation()
        {
            PartyState party = PartyOf(Alice);
            PartyResult invited = PartyService.TryInvite(party, Alice, Bob, PartyContext());

            PartyResult wrong = PartyService.TryAccept(invited.Invite, party, Carol,
                PartyContext());

            Assert.That(wrong.Reason, Is.EqualTo(PartyRejection.NotTheInvitee));
            Assert.That(invited.Invite.IsPending, Is.True);
        }

        [Test]
        public void An_expired_invitation_is_refused()
        {
            PartyState party = PartyOf(Alice);

            var invite = new PartyInvite(InstanceId.New(), party.Id, Alice, Bob, 0L, 100L);

            PartyResult result = PartyService.TryAccept(invite, party, Bob, PartyContext(200L));

            Assert.That(result.Reason, Is.EqualTo(PartyRejection.InviteExpired));
            Assert.That(party.Contains(Bob), Is.False);
        }

        [Test]
        public void A_party_holds_the_authored_maximum_and_refuses_the_next()
        {
            PartyState party = PartyOf(SixCharacters);

            Assert.That(party.MemberCount, Is.EqualTo(Configuration.MaxPartySize));
            Assert.That(Configuration.MaxPartySize, Is.EqualTo(MaxParty));

            PartyResult seventh = PartyService.TryInvite(party, Alice, Grace, PartyContext());

            Assert.That(seventh.Reason, Is.EqualTo(PartyRejection.PartyFull));
            Assert.That(party.MemberCount, Is.EqualTo(Configuration.MaxPartySize));
        }

        [Test]
        public void The_party_ceiling_is_authored_not_written_in_code()
        {
            // Re-author the limit. Nothing in code changes and the service follows.
            SocialConfiguration bigger = AddConfiguration(maxParty: 3);
            var context = new PartyService.Context(bigger, new PartyDirectory());

            PartyResult created = PartyService.TryCreate(Alice, context);
            PartyState party = created.Party;

            for (int i = 1; i < 3; i++)
            {
                PartyResult invited = PartyService.TryInvite(party, Alice, SixCharacters[i],
                    context);

                PartyService.TryAccept(invited.Invite, party, SixCharacters[i], context);
            }

            Assert.That(party.MemberCount, Is.EqualTo(3));
            Assert.That(PartyService.TryInvite(party, Alice, Dave, context).Reason,
                Is.EqualTo(PartyRejection.PartyFull));
        }

        [Test]
        public void A_character_cannot_belong_to_two_parties()
        {
            PartyOf(Alice, Bob);

            PartyState other = PartyOf(Carol);

            PartyResult invited = PartyService.TryInvite(other, Carol, Bob, PartyContext());

            Assert.That(invited.Reason, Is.EqualTo(PartyRejection.AlreadyInAnotherParty));
        }

        [Test]
        public void A_member_list_never_holds_a_duplicate()
        {
            PartyState party = PartyOf(Alice, Bob);

            // Reaching past the service on purpose: the invariant is structural.
            Assert.That(party.TryAdd(Bob), Is.False);
            Assert.That(party.MemberCount, Is.EqualTo(2));

            var seen = new HashSet<CharacterId>();

            for (int i = 0; i < party.Members.Count; i++)
            {
                Assert.That(seen.Add(party.Members[i]), Is.True);
            }
        }

        // ---- party: permissions and leadership -----------------------------------------

        [Test]
        public void Only_the_leader_may_invite()
        {
            PartyState party = PartyOf(Alice, Bob);

            Assert.That(PartyService.TryInvite(party, Bob, Carol, PartyContext()).Reason,
                Is.EqualTo(PartyRejection.NotLeader));
        }

        [Test]
        public void Only_the_leader_may_kick()
        {
            PartyState party = PartyOf(Alice, Bob, Carol);

            Assert.That(PartyService.TryKick(party, Bob, Carol, PartyContext()).Reason,
                Is.EqualTo(PartyRejection.NotLeader));
            Assert.That(party.Contains(Carol), Is.True);
        }

        [Test]
        public void The_leader_may_kick_a_member()
        {
            PartyState party = PartyOf(Alice, Bob);

            PartyResult result = PartyService.TryKick(party, Alice, Bob, PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(party.Contains(Bob), Is.False);
            Assert.That(Parties.IsInAParty(Bob), Is.False);
        }

        [Test]
        public void A_leader_cannot_kick_themselves()
        {
            PartyState party = PartyOf(Alice, Bob);

            Assert.That(PartyService.TryKick(party, Alice, Alice, PartyContext()).Reason,
                Is.EqualTo(PartyRejection.InvalidTarget));
        }

        [Test]
        public void Leadership_transfer_is_one_step()
        {
            PartyState party = PartyOf(Alice, Bob);
            Revision before = party.Revision;

            PartyResult result = PartyService.TryTransferLeadership(party, Alice, Bob,
                PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(party.Leader, Is.EqualTo(Bob));
            Assert.That(party.Contains(Alice), Is.True, "the former leader stays a member");
            Assert.That(party.Revision.Value, Is.EqualTo(before.Value + 1),
                "one atomic change, one revision");
        }

        [Test]
        public void Leadership_cannot_be_given_to_a_non_member()
        {
            PartyState party = PartyOf(Alice, Bob);

            Assert.That(PartyService.TryTransferLeadership(party, Alice, Grace, PartyContext())
                .Reason, Is.EqualTo(PartyRejection.NotAMember));
            Assert.That(party.Leader, Is.EqualTo(Alice));
        }

        [Test]
        public void A_leader_leaving_hands_the_party_on_rather_than_leaving_it_headless()
        {
            PartyState party = PartyOf(Alice, Bob, Carol);

            PartyResult result = PartyService.TryLeave(party, Alice, PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(party.Contains(Alice), Is.False);
            Assert.That(party.Leader, Is.EqualTo(Bob));
            Assert.That(party.IsActive, Is.True);
        }

        [Test]
        public void The_last_member_leaving_disbands_the_party()
        {
            PartyState party = PartyOf(Alice);

            PartyResult result = PartyService.TryLeave(party, Alice, PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(party.IsActive, Is.False);
            Assert.That(party.MemberCount, Is.EqualTo(0));
        }

        [Test]
        public void Disbanding_empties_the_party_and_frees_its_members()
        {
            PartyState party = PartyOf(Alice, Bob, Carol);

            PartyResult result = PartyService.TryDisband(party, Alice, PartyContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(party.IsActive, Is.False);
            Assert.That(Parties.IsInAParty(Bob), Is.False);
            Assert.That(Parties.IsInAParty(Carol), Is.False);
        }

        [Test]
        public void Only_the_leader_may_disband()
        {
            PartyState party = PartyOf(Alice, Bob);

            Assert.That(PartyService.TryDisband(party, Bob, PartyContext()).Reason,
                Is.EqualTo(PartyRejection.NotLeader));
            Assert.That(party.IsActive, Is.True);
        }

        [Test]
        public void A_disbanded_party_accepts_nothing_further()
        {
            PartyState party = PartyOf(Alice, Bob);
            PartyService.TryDisband(party, Alice, PartyContext());

            Assert.That(PartyService.TryInvite(party, Alice, Carol, PartyContext()).Reason,
                Is.EqualTo(PartyRejection.PartyInactive));
        }

        [Test]
        public void A_refused_party_operation_changes_no_revision()
        {
            PartyState party = PartyOf(Alice, Bob);
            Revision before = party.Revision;

            PartyService.TryInvite(party, Bob, Carol, PartyContext());
            PartyService.TryKick(party, Bob, Alice, PartyContext());
            PartyService.TryDisband(party, Bob, PartyContext());

            Assert.That(party.Revision, Is.EqualTo(before));
        }

        // ---- party loot ----------------------------------------------------------------

        [Test]
        public void Personal_loot_behaves_exactly_as_it_did_without_a_party()
        {
            PartyState party = PartyOf(Alice, Bob);

            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Alice), Is.True);
            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Bob), Is.False);
            Assert.That(PartyLootPolicyService.CanClaim(null, Alice, Alice), Is.True);
        }

        [Test]
        public void Round_robin_rotates_deterministically()
        {
            PartyState party = PartyOf(Alice, Bob, Carol);
            PartyService.TrySetLootPolicy(party, Alice, PartyLootPolicy.RoundRobin,
                PartyContext());

            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 0), Is.EqualTo(Alice));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 1), Is.EqualTo(Bob));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 2), Is.EqualTo(Carol));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 3), Is.EqualTo(Alice));

            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Bob, 1), Is.True);
            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Carol, 1), Is.False);
        }

        [Test]
        public void Need_greed_makes_every_member_eligible()
        {
            PartyState party = PartyOf(Alice, Bob, Carol);
            PartyService.TrySetLootPolicy(party, Alice, PartyLootPolicy.NeedGreed, PartyContext());

            var eligible = new List<CharacterId>();
            PartyLootPolicyService.EligibleClaimants(party, Alice, 0, eligible);

            Assert.That(eligible.Count, Is.EqualTo(3));
            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Carol), Is.True);
        }

        [Test]
        public void A_non_member_can_never_claim_party_loot()
        {
            PartyState party = PartyOf(Alice, Bob);
            PartyService.TrySetLootPolicy(party, Alice, PartyLootPolicy.NeedGreed, PartyContext());

            Assert.That(PartyLootPolicyService.CanClaim(party, Alice, Grace), Is.False);
        }

        [Test]
        public void The_loot_policy_creates_no_loot_object()
        {
            // Structural: the policy answers a question and returns identities. It has no
            // way to produce a loot object, which is what stops six members getting six
            // copies of one drop.
            System.Reflection.MethodInfo[] methods =
                typeof(PartyLootPolicyService).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (System.Reflection.MethodInfo method in methods)
            {
                Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(LootObjectState)),
                    method.Name);

                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(LootObjectState)),
                        method.Name + " takes a loot object it could mutate");
                }
            }
        }

        // ---- party experience ----------------------------------------------------------

        [Test]
        public void Experience_shares_sum_to_exactly_the_award()
        {
            var eligible = new List<CharacterId>(SixCharacters);
            var shares = new List<PartyExperienceShare>();

            PartyExperiencePolicy.Share(100, eligible, shares);

            int total = 0;
            for (int i = 0; i < shares.Count; i++) total += shares[i].Experience;

            Assert.That(shares.Count, Is.EqualTo(6));
            Assert.That(total, Is.EqualTo(100), "no point is created or destroyed");
        }

        [Test]
        public void The_remainder_is_distributed_rather_than_dropped()
        {
            var eligible = new List<CharacterId> { Alice, Bob, Carol };
            var shares = new List<PartyExperienceShare>();

            PartyExperiencePolicy.Share(10, eligible, shares);

            Assert.That(shares[0].Experience, Is.EqualTo(4));
            Assert.That(shares[1].Experience, Is.EqualTo(3));
            Assert.That(shares[2].Experience, Is.EqualTo(3));
        }

        [Test]
        public void Sharing_nothing_produces_nothing()
        {
            var shares = new List<PartyExperienceShare>();

            PartyExperiencePolicy.Share(0, new List<CharacterId> { Alice }, shares);

            Assert.That(shares.Count, Is.EqualTo(0));
        }

        // ---- guild: creation -----------------------------------------------------------

        [Test]
        public void Creating_a_guild_makes_the_founder_its_leader()
        {
            GuildResult result = GuildService.TryCreate(Alice, "Wanderers",
                new DefinitionId(RankLeader), GuildContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Guild.Leader, Is.EqualTo(Alice));
            Assert.That(result.Guild.MemberCount, Is.EqualTo(1));
            Assert.That(result.Guild.RankOf(Alice), Is.EqualTo(new DefinitionId(RankLeader)));
        }

        [Test]
        public void A_guild_is_named_by_its_id_not_by_its_name()
        {
            GuildState guild = GuildOf(Alice, "Wanderers");
            GuildId before = guild.Id;

            guild.TryRename("Nomads");

            Assert.That(guild.Id, Is.EqualTo(before));
            Assert.That(guild.Name, Is.EqualTo("Nomads"));
        }

        [Test]
        public void A_taken_name_is_refused_through_the_authority()
        {
            GuildOf(Alice, "Wanderers");

            GuildResult second = GuildService.TryCreate(Bob, "wanderers",
                new DefinitionId(RankLeader), GuildContext());

            Assert.That(second.Reason, Is.EqualTo(GuildRejection.NameTaken),
                "case must not be enough to distinguish two guilds");
        }

        [Test]
        public void A_reserved_name_is_refused()
        {
            GuildNames.Reserve("Administrators");

            GuildResult result = GuildService.TryCreate(Alice, "Administrators",
                new DefinitionId(RankLeader), GuildContext());

            Assert.That(result.Reason, Is.EqualTo(GuildRejection.NameReserved));
        }

        [Test]
        public void A_name_of_the_wrong_shape_is_refused()
        {
            Assert.That(GuildService.TryCreate(Alice, "ab", new DefinitionId(RankLeader),
                GuildContext()).Reason, Is.EqualTo(GuildRejection.InvalidName), "too short");

            Assert.That(GuildService.TryCreate(Alice, new string('x', 40),
                new DefinitionId(RankLeader), GuildContext()).Reason,
                Is.EqualTo(GuildRejection.InvalidName), "too long");

            Assert.That(GuildService.TryCreate(Alice, "Bad<Name>", new DefinitionId(RankLeader),
                GuildContext()).Reason, Is.EqualTo(GuildRejection.InvalidName), "bad characters");

            Assert.That(GuildService.TryCreate(Alice, "   ", new DefinitionId(RankLeader),
                GuildContext()).Reason, Is.EqualTo(GuildRejection.InvalidName), "blank");
        }

        [Test]
        public void A_refused_creation_does_not_reserve_the_name()
        {
            GuildService.TryCreate(Alice, "Bad<Name>", new DefinitionId(RankLeader),
                GuildContext());

            Assert.That(GuildNames.ClaimedCount, Is.EqualTo(0));
        }

        [Test]
        public void Disbanding_releases_the_name()
        {
            GuildState guild = GuildOf(Alice, "Wanderers");

            GuildService.TryDisband(guild, Alice, GuildContext());

            Assert.That(GuildNames.IsAvailable("Wanderers"), Is.True);
            Assert.That(guild.IsActive, Is.False);
        }

        // ---- guild: membership and permissions -----------------------------------------

        [Test]
        public void An_officer_may_invite_and_a_member_may_not()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);
            JoinGuild(guild, Alice, Carol, RankMember);

            Assert.That(GuildService.TryInvite(guild, Bob, Dave, GuildContext()).IsAccepted,
                Is.True);

            Assert.That(GuildService.TryInvite(guild, Carol, Erin, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.PermissionDenied));
        }

        [Test]
        public void Permission_is_read_from_the_authored_rank()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Carol, RankMember);

            // Re-author the member rank to allow inviting. Nothing in code changes.
            GuildRankDefinition member;
            Ranks.TryGet(new DefinitionId(RankMember), out member);
            SetPrivate(member, "_permissions", GuildPermission.Invite);

            Assert.That(GuildService.TryInvite(guild, Carol, Dave, GuildContext()).IsAccepted,
                Is.True);
        }

        [Test]
        public void An_officer_cannot_kick_another_officer()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);
            JoinGuild(guild, Alice, Carol, RankOfficer);

            Assert.That(GuildService.TryKick(guild, Bob, Carol, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.InsufficientSeniority));
            Assert.That(guild.Contains(Carol), Is.True);
        }

        [Test]
        public void An_officer_may_kick_a_member()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);
            JoinGuild(guild, Alice, Carol, RankMember);

            Assert.That(GuildService.TryKick(guild, Bob, Carol, GuildContext()).IsAccepted,
                Is.True);
            Assert.That(guild.Contains(Carol), Is.False);
        }

        [Test]
        public void Nobody_may_kick_the_leader()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            Assert.That(GuildService.TryKick(guild, Bob, Alice, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.InsufficientSeniority));
            Assert.That(guild.Leader, Is.EqualTo(Alice));
        }

        [Test]
        public void Promotion_moves_a_member_up_one_authored_rank()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Carol, RankMember);

            GuildResult result = GuildService.TryPromote(guild, Alice, Carol, GuildContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(guild.RankOf(Carol), Is.EqualTo(new DefinitionId(RankOfficer)));
        }

        [Test]
        public void Promotion_never_reaches_the_leaders_rank()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            Assert.That(GuildService.TryPromote(guild, Alice, Bob, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.RankUnavailable),
                "becoming leader is a transfer, not a promotion");
        }

        [Test]
        public void Demotion_moves_a_member_down_one_rank()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            GuildResult result = GuildService.TryDemote(guild, Alice, Bob, GuildContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(guild.RankOf(Bob), Is.EqualTo(new DefinitionId(RankMember)));
        }

        [Test]
        public void A_member_at_the_lowest_rank_cannot_be_demoted_further()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Carol, RankMember);

            Assert.That(GuildService.TryDemote(guild, Alice, Carol, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.RankUnavailable));
        }

        [Test]
        public void Guild_leadership_transfer_moves_both_ranks_in_one_step()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            Revision before = guild.Revision;

            GuildResult result = GuildService.TryTransferLeadership(guild, Alice, Bob,
                new DefinitionId(RankOfficer), GuildContext());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(guild.Leader, Is.EqualTo(Bob));
            Assert.That(guild.RankOf(Bob), Is.EqualTo(new DefinitionId(RankLeader)));
            Assert.That(guild.RankOf(Alice), Is.EqualTo(new DefinitionId(RankOfficer)));
            Assert.That(guild.Revision.Value, Is.EqualTo(before.Value + 1));
        }

        [Test]
        public void A_leader_may_not_simply_leave()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            Assert.That(GuildService.TryLeave(guild, Alice, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.InvalidTarget));
            Assert.That(guild.Leader, Is.EqualTo(Alice));
        }

        [Test]
        public void A_member_may_leave()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankMember);

            Assert.That(GuildService.TryLeave(guild, Bob, GuildContext()).IsAccepted, Is.True);
            Assert.That(guild.Contains(Bob), Is.False);
            Assert.That(Guilds.IsInAGuild(Bob), Is.False);
        }

        [Test]
        public void A_character_cannot_belong_to_two_guilds()
        {
            GuildState first = GuildOf(Alice, "Wanderers");
            JoinGuild(first, Alice, Bob, RankMember);

            GuildState second = GuildOf(Carol, "Nomads");

            Assert.That(GuildService.TryInvite(second, Carol, Bob, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.AlreadyInAnotherGuild));
        }

        [Test]
        public void A_guild_member_list_never_holds_a_duplicate()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankMember);

            Assert.That(guild.TryAdd(Bob, new DefinitionId(RankMember), 0L), Is.False);
            Assert.That(guild.MemberCount, Is.EqualTo(2));
        }

        [Test]
        public void Only_a_rank_with_disband_may_end_the_guild()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Bob, RankOfficer);

            Assert.That(GuildService.TryDisband(guild, Bob, GuildContext()).Reason,
                Is.EqualTo(GuildRejection.PermissionDenied));
            Assert.That(guild.IsActive, Is.True);
        }

        [Test]
        public void A_refused_guild_operation_changes_no_revision()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Carol, RankMember);

            Revision before = guild.Revision;

            GuildService.TryInvite(guild, Carol, Dave, GuildContext());
            GuildService.TryKick(guild, Carol, Alice, GuildContext());
            GuildService.TryDisband(guild, Carol, GuildContext());

            Assert.That(guild.Revision, Is.EqualTo(before));
        }

        [Test]
        public void Permission_can_be_asked_without_attempting_the_operation()
        {
            GuildState guild = GuildOf(Alice);
            JoinGuild(guild, Alice, Carol, RankMember);

            Assert.That(GuildService.HasPermission(guild, Alice, GuildPermission.Kick,
                GuildContext()), Is.True);

            Assert.That(GuildService.HasPermission(guild, Carol, GuildPermission.Kick,
                GuildContext()), Is.False);
        }
    }
}
