using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the party and guild panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for party and guild.</b> Every change these panels can cause
    /// goes through a submit method here, and each calls <see cref="PartyService"/> or
    /// <see cref="GuildService"/>. No view holds a party, a guild or a directory, so there is
    /// nowhere else a membership could change. The same shape the inventory, quest, world and
    /// collectible controllers already keep.
    ///
    /// <b>It decides nothing.</b> Not one rule about who may invite, kick, promote or disband
    /// appears below. Every submit forwards to the service that owns the rule and reports
    /// what came back, which is why a panel cannot authorise anything by being wrong about
    /// state.
    ///
    /// <b>Nothing is polled.</b> Panels rebuild when a revision moves, not every frame.
    /// </remarks>
    public sealed class SocialUiController : MonoBehaviour
    {
        private readonly List<PartyMemberViewData> _partyMembers =
            new List<PartyMemberViewData>();

        private readonly List<GuildMemberViewData> _guildMembers =
            new List<GuildMemberViewData>();

        private PartyState _party;
        private PartyDirectory _partyDirectory;
        private GuildState _guild;
        private GuildDirectory _guildDirectory;
        private IGuildNameAuthority _guildNames;

        private IDefinitionRegistry<GuildRankDefinition> _ranks;
        private SocialConfiguration _configuration;

        private CharacterId _viewer;
        private bool _bound;
        private Revision _lastPartyRevision;
        private Revision _lastGuildRevision;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>Resolves display names. Optional; without it, identities are shown.</summary>
        public SocialAdapter.NameResolver Names { get; set; }

        /// <summary>Supplies member vitals. Optional; without it, rows show no vitals.</summary>
        public SocialAdapter.VitalsResolver Vitals { get; set; }

        /// <summary>Caller-supplied time, for invitation expiry.</summary>
        public long TimestampTicks { get; set; }

        /// <summary>The answer to the last party operation submitted.</summary>
        public PartyResult LastPartyResult { get; private set; }

        /// <summary>The answer to the last guild operation submitted.</summary>
        public GuildResult LastGuildResult { get; private set; }

        public PartyViewData Party { get; private set; }

        public GuildViewData Guild { get; private set; }

        public IReadOnlyList<PartyMemberViewData> PartyMembers => _partyMembers;

        public IReadOnlyList<GuildMemberViewData> GuildMembers => _guildMembers;

        /// <summary>Raised when a party invitation is created for somebody to answer.</summary>
        public event System.Action<PartyInvite> PartyInvited;

        /// <summary>Raised when a guild invitation is created.</summary>
        public event System.Action<GuildInvite> GuildInvited;

        /// <summary>Points the UI at a character's social state.</summary>
        public void Bind(CharacterId viewer, PartyState party = null,
            PartyDirectory partyDirectory = null, GuildState guild = null,
            GuildDirectory guildDirectory = null,
            IDefinitionRegistry<GuildRankDefinition> ranks = null,
            IGuildNameAuthority guildNames = null,
            SocialConfiguration configuration = null)
        {
            _viewer = viewer;
            _party = party;
            _partyDirectory = partyDirectory;
            _guild = guild;
            _guildDirectory = guildDirectory;
            _ranks = ranks;
            _guildNames = guildNames;
            _configuration = configuration;

            _bound = true;
            Refresh();
        }

        /// <summary>The registries the adapter reads through.</summary>
        public SocialAdapter.Context ViewContext =>
            new SocialAdapter.Context(null, _ranks, _configuration, Names);

        private PartyService.Context PartyContext =>
            new PartyService.Context(_configuration, _partyDirectory, TimestampTicks);

        private GuildService.Context GuildContext =>
            new GuildService.Context(_ranks, _configuration, _guildNames, _guildDirectory,
                TimestampTicks);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current gameplay state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            Party = SocialAdapter.BuildParty(_party, _viewer, ViewContext);
            Guild = SocialAdapter.BuildGuild(_guild, _viewer, ViewContext);

            SocialAdapter.BuildPartyMembers(_party, ViewContext, _partyMembers, Vitals);
            SocialAdapter.BuildGuildMembers(_guild, ViewContext, _guildMembers, Vitals);

            if (_party != null) _lastPartyRevision = _party.Revision;
            if (_guild != null) _lastGuildRevision = _guild.Revision;
        }

        /// <summary>
        /// Redraws only if something actually changed.
        /// </summary>
        /// <remarks>A revision comparison rather than a per-frame rebuild, matching every
        /// other controller in this assembly.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound) return false;

            bool partyMoved = _party != null && _party.Revision != _lastPartyRevision;
            bool guildMoved = _guild != null && _guild.Revision != _lastGuildRevision;

            if (!partyMoved && !guildMoved) return false;

            Refresh();
            return true;
        }

        // ---- party commands ------------------------------------------------------------

        /// <summary>Forms a party with the viewer as leader.</summary>
        public PartyResult SubmitCreateParty()
        {
            LastPartyResult = PartyService.TryCreate(_viewer, PartyContext);

            if (LastPartyResult.IsAccepted) _party = LastPartyResult.Party;

            Refresh();
            return LastPartyResult;
        }

        /// <summary>Invites somebody. Leader only, enforced by the service.</summary>
        public PartyResult SubmitPartyInvite(CharacterId target)
        {
            LastPartyResult = PartyService.TryInvite(_party, _viewer, target, PartyContext);

            if (LastPartyResult.IsAccepted && LastPartyResult.Invite != null)
            {
                var handler = PartyInvited;
                if (handler != null) handler(LastPartyResult.Invite);
            }

            Refresh();
            return LastPartyResult;
        }

        /// <summary>Accepts an invitation the viewer received.</summary>
        public PartyResult SubmitPartyAccept(PartyInvite invite, PartyState party)
        {
            LastPartyResult = PartyService.TryAccept(invite, party, _viewer, PartyContext);

            if (LastPartyResult.IsAccepted) _party = party;

            Refresh();
            return LastPartyResult;
        }

        /// <summary>Declines an invitation.</summary>
        public PartyResult SubmitPartyReject(PartyInvite invite)
        {
            LastPartyResult = PartyService.TryReject(invite, _viewer, _party);
            Refresh();
            return LastPartyResult;
        }

        public PartyResult SubmitPartyLeave()
        {
            LastPartyResult = PartyService.TryLeave(_party, _viewer, PartyContext);

            if (LastPartyResult.IsAccepted) _party = null;

            Refresh();
            return LastPartyResult;
        }

        public PartyResult SubmitPartyKick(CharacterId target)
        {
            LastPartyResult = PartyService.TryKick(_party, _viewer, target, PartyContext);
            Refresh();
            return LastPartyResult;
        }

        public PartyResult SubmitPartyTransferLeadership(CharacterId target)
        {
            LastPartyResult = PartyService.TryTransferLeadership(_party, _viewer, target,
                PartyContext);

            Refresh();
            return LastPartyResult;
        }

        public PartyResult SubmitPartyDisband()
        {
            LastPartyResult = PartyService.TryDisband(_party, _viewer, PartyContext);

            if (LastPartyResult.IsAccepted) _party = null;

            Refresh();
            return LastPartyResult;
        }

        public PartyResult SubmitPartyLootPolicy(PartyLootPolicy policy)
        {
            LastPartyResult = PartyService.TrySetLootPolicy(_party, _viewer, policy, PartyContext);
            Refresh();
            return LastPartyResult;
        }

        // ---- guild commands ------------------------------------------------------------

        public GuildResult SubmitCreateGuild(string name, DefinitionId leaderRank)
        {
            LastGuildResult = GuildService.TryCreate(_viewer, name, leaderRank, GuildContext);

            if (LastGuildResult.IsAccepted) _guild = LastGuildResult.Guild;

            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildInvite(CharacterId target)
        {
            LastGuildResult = GuildService.TryInvite(_guild, _viewer, target, GuildContext);

            if (LastGuildResult.IsAccepted && LastGuildResult.Invite != null)
            {
                var handler = GuildInvited;
                if (handler != null) handler(LastGuildResult.Invite);
            }

            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildAccept(GuildInvite invite, GuildState guild,
            DefinitionId memberRank)
        {
            LastGuildResult = GuildService.TryAccept(invite, guild, _viewer, memberRank,
                GuildContext);

            if (LastGuildResult.IsAccepted) _guild = guild;

            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildReject(GuildInvite invite)
        {
            LastGuildResult = GuildService.TryReject(invite, _viewer, _guild);
            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildLeave()
        {
            LastGuildResult = GuildService.TryLeave(_guild, _viewer, GuildContext);

            if (LastGuildResult.IsAccepted) _guild = null;

            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildKick(CharacterId target)
        {
            LastGuildResult = GuildService.TryKick(_guild, _viewer, target, GuildContext);
            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildPromote(CharacterId target)
        {
            LastGuildResult = GuildService.TryPromote(_guild, _viewer, target, GuildContext);
            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildDemote(CharacterId target)
        {
            LastGuildResult = GuildService.TryDemote(_guild, _viewer, target, GuildContext);
            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildTransferLeadership(CharacterId target,
            DefinitionId formerLeaderRank)
        {
            LastGuildResult = GuildService.TryTransferLeadership(_guild, _viewer, target,
                formerLeaderRank, GuildContext);

            Refresh();
            return LastGuildResult;
        }

        public GuildResult SubmitGuildDisband()
        {
            LastGuildResult = GuildService.TryDisband(_guild, _viewer, GuildContext);

            if (LastGuildResult.IsAccepted) _guild = null;

            Refresh();
            return LastGuildResult;
        }
    }
}
