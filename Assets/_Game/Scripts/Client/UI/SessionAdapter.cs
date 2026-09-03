using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns session state, server lists and character lists into view data. The read half.
    /// </summary>
    /// <remarks>
    /// <b>Reads only.</b> Nothing here signs in, selects or enters a world; every output is a
    /// snapshot. Building a screen twenty times costs nothing and changes nothing -- in
    /// particular it advances no session revision, which is what makes a list refresh safe.
    ///
    /// <b>Selectability is asked, not derived.</b> Each row's hint comes from
    /// <see cref="SessionFlowService.CanSelectServer"/> or its channel counterpart, so a
    /// greyed-out row and a refused click give the same reason. The service is asked again on
    /// the click, so a stale hint cannot admit anybody.
    ///
    /// <b>Unknown stays unknown.</b> A population with no reading is passed through as not
    /// known rather than as zero, and character presence is not invented at all.
    /// </remarks>
    public static class SessionAdapter
    {
        // ---- login ---------------------------------------------------------------------

        /// <summary>What a login panel should show, given the last result.</summary>
        public static LoginViewData BuildLogin(PanelStatus status, in LoginResult result,
            string accountDisplayName = null)
        {
            return new LoginViewData(status, result.Reason, result.Compatibility.Compatibility,
                result.Compatibility.Kind, result.Compatibility.Expected, result.IsAccepted,
                accountDisplayName);
        }

        /// <summary>What a login panel should show before anything has been attempted.</summary>
        public static LoginViewData BuildIdleLogin()
        {
            return new LoginViewData(PanelStatus.Idle, LoginRejection.None,
                VersionCompatibility.Compatible, VersionKind.None, default, false, null);
        }

        // ---- flow ----------------------------------------------------------------------

        /// <summary>Where the flow stands, for whichever screen is showing.</summary>
        public static SessionFlowViewData BuildFlow(AccountSessionState session,
            PanelStatus status, SessionRejection reason, int usedCharacterSlots,
            SessionConfiguration configuration)
        {
            SessionConfiguration.Limits limits = SessionConfiguration.Resolve(configuration);

            if (session == null)
            {
                return new SessionFlowViewData(SessionState.Unauthenticated, status, reason,
                    default, default, default, 0, limits.MaxCharacterSlots, false);
            }

            return new SessionFlowViewData(session.State, status, reason, session.Server,
                session.Channel, session.Character, usedCharacterSlots,
                limits.MaxCharacterSlots,
                session.State == SessionState.CharacterSelected);
        }

        // ---- servers -------------------------------------------------------------------

        /// <summary>
        /// Fills <paramref name="into"/> with one row per server the authority listed.
        /// </summary>
        /// <remarks>The list arrives already filtered by the authority -- a hidden server is
        /// simply absent -- so nothing here decides who may see what.</remarks>
        public static void BuildServers(IReadOnlyList<ServerInfo> servers,
            AccountSessionState session, in SessionFlowService.Context context,
            List<ServerRowViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (servers == null) return;

            for (int i = 0; i < servers.Count; i++)
            {
                ServerInfo info = servers[i];
                if (!info.IsValid) continue;

                SessionRejection blocked = SessionFlowService.CanSelectServer(session, info,
                    context);

                into.Add(new ServerRowViewData(info.Server, info.NameKey, info.Region,
                    info.Status, info.Population.IsKnown, info.Population.Value,
                    info.Population.Capacity, blocked == SessionRejection.None,
                    session != null && session.Server == info.Server, blocked));
            }
        }

        // ---- channels ------------------------------------------------------------------

        /// <summary>Fills <paramref name="into"/> with one row per channel of the chosen server.</summary>
        public static void BuildChannels(IReadOnlyList<ChannelInfo> channels,
            AccountSessionState session, in SessionFlowService.Context context,
            List<ChannelRowViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (channels == null) return;

            for (int i = 0; i < channels.Count; i++)
            {
                ChannelInfo info = channels[i];
                if (!info.IsValid) continue;

                SessionRejection blocked = SessionFlowService.CanSelectChannel(session, info,
                    context);

                into.Add(new ChannelRowViewData(info.Channel, info.Server, info.NameKey,
                    info.Status, info.Population.IsKnown, info.Population.Value,
                    info.Population.Capacity, info.PkEnabled,
                    blocked == SessionRejection.None,
                    session != null && session.Channel == info.Channel, blocked));
            }
        }

        // ---- characters ----------------------------------------------------------------

        /// <summary>
        /// Fills <paramref name="into"/> with one row per character.
        /// </summary>
        /// <remarks>A projection of a summary, never of <c>CharacterState</c>. The persistent
        /// character is loaded by the game server after entering the world, once.</remarks>
        public static void BuildCharacters(IReadOnlyList<CharacterSelectEntry> characters,
            AccountSessionState session, List<CharacterRowViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterSelectEntry entry = characters[i];
                if (!entry.IsValid) continue;

                into.Add(new CharacterRowViewData(entry.Character, entry.Name, entry.Gender,
                    entry.Level, entry.Class, entry.Job, entry.Map, entry.Appearance,
                    entry.Availability, entry.IsPlayable,
                    session != null && session.Character == entry.Character));
            }
        }

        /// <summary>
        /// The name a character presents, for the resolvers Phase 13's panels take.
        /// </summary>
        /// <remarks>
        /// Phase 13's party and guild adapters accept a name resolver because a display name is
        /// not authored content and had no source yet. This is that source: a lookup over the
        /// character rows the account already fetched. It answers only for characters this
        /// account listed -- it is not a directory of everybody's names, and building one here
        /// would leak other accounts' data into a client.
        /// </remarks>
        public static string NameOf(IReadOnlyList<CharacterSelectEntry> characters,
            CharacterId character)
        {
            if (characters == null || !character.IsValid) return null;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].Character != character) continue;

                return characters[i].Name;
            }

            return null;
        }

        /// <summary>
        /// Whether a character is known to be in the world.
        /// </summary>
        /// <remarks>
        /// Reported as a <see cref="PopulationReading"/> so absent information stays absent:
        /// until a live server reports presence there is no answer, and returning "offline"
        /// would be a fabrication a player acts on. Phase 16 supplies the real source.
        /// </remarks>
        public static PopulationReading PresenceOf(IReadOnlyList<CharacterSelectEntry> characters,
            CharacterId character)
        {
            if (characters == null || !character.IsValid) return PopulationReading.Unknown();

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].Character != character) continue;

                // The only presence fact this phase has: the authority marked the character as
                // already in the world. Anything else is genuinely unknown.
                return characters[i].Availability == CharacterAvailability.InWorld
                    ? PopulationReading.Known(1, 1)
                    : PopulationReading.Unknown();
            }

            return PopulationReading.Unknown();
        }
    }
}
