using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why one player may not attack another.</summary>
    /// <remarks>
    /// Every value is a different sentence to a player, which is why they are not collapsed
    /// into one refusal. "This is a town" and "your channel has PK off" are both no, and a
    /// player who cannot tell them apart will assume the game is broken.
    /// </remarks>
    public enum PkRejection
    {
        None = 0,

        /// <summary>No policy, no map, or a combatant that is not a player.</summary>
        MissingContext = 1,

        /// <summary>The channel this player is on has player combat switched off.</summary>
        ChannelDisabled = 2,

        /// <summary>The map does not permit player combat.</summary>
        MapDisabled = 3,

        /// <summary>A safe zone. Nothing may be attacked here.</summary>
        SafeZone = 4,

        /// <summary>A town. Towns are safe unless authored otherwise.</summary>
        Town = 5,

        /// <summary>Attacker and target are on different maps.</summary>
        DifferentMap = 6,

        /// <summary>A player cannot attack themselves.</summary>
        Self = 7,

        /// <summary>Both are in the same party.</summary>
        SameParty = 8,

        /// <summary>Both are in the same guild.</summary>
        SameGuild = 9,

        /// <summary>One of them is below the level at which PK applies.</summary>
        BelowMinimumLevel = 10
    }

    /// <summary>Whether one player may attack another, and why not.</summary>
    public readonly struct PkVerdict
    {
        private PkVerdict(bool allowed, PkRejection reason)
        {
            IsAllowed = allowed;
            Reason = reason;
        }

        public bool IsAllowed { get; }

        public PkRejection Reason { get; }

        public static PkVerdict Allowed => new PkVerdict(true, PkRejection.None);

        public static PkVerdict Refused(PkRejection reason)
        {
            return new PkVerdict(false, reason);
        }

        public override string ToString()
        {
            return IsAllowed ? "pk allowed" : "pk refused: " + Reason;
        }
    }

    /// <summary>
    /// The settings that decide whether players may fight, none of them a literal.
    /// </summary>
    /// <remarks>
    /// <b>Supplied by the authority, never by a client.</b> <see cref="ChannelEnabled"/> is
    /// the <c>pk_enabled</c> column the Phase 15 <c>server_channel</c> table already holds and
    /// the Phase 14 <c>ChannelInfo</c> already carries — a client receives it so a UI can grey
    /// a button, and the server reads its own copy regardless. That is why there is no method
    /// anywhere that turns PK on: the value arrives from a database row.
    ///
    /// <see cref="MinimumLevel"/> exists because low-level players being farmed is a
    /// well-known way to lose a playerbase. Zero disables the rule, so a server that wants no
    /// floor authors none.
    /// </remarks>
    public readonly struct PkSettings
    {
        public PkSettings(bool channelEnabled, int minimumLevel = 0,
            bool allowSameParty = false, bool allowSameGuild = false)
        {
            ChannelEnabled = channelEnabled;
            MinimumLevel = minimumLevel < 0 ? 0 : minimumLevel;
            AllowSameParty = allowSameParty;
            AllowSameGuild = allowSameGuild;
        }

        /// <summary>From the channel row. The master switch.</summary>
        public bool ChannelEnabled { get; }

        /// <summary>Below this, a player can neither attack nor be attacked. Zero disables.</summary>
        public int MinimumLevel { get; }

        /// <summary>Whether party members may fight each other. Off by default.</summary>
        public bool AllowSameParty { get; }

        /// <summary>Whether guild members may fight each other. Off by default.</summary>
        public bool AllowSameGuild { get; }

        /// <summary>
        /// PK off, no floor, no friendly fire.
        /// </summary>
        /// <remarks>The default of a <c>default</c> struct, and deliberately the safe one: a
        /// misconfigured server refuses player combat rather than permitting it. An
        /// unconfigured server that allowed PK would be a bug that only shows up as players
        /// killing each other in a starting town.</remarks>
        public static PkSettings Disabled => default;
    }

    /// <summary>
    /// Decides whether one player may attack another.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is hard-coded and nothing here is a client's to say.</b> The channel
    /// switch comes from a database row, the map rules from authored content, and the social
    /// relationships from the Phase 13 services. A client has no message that carries any of
    /// them.
    ///
    /// <b>The order is from cheapest and broadest to most specific</b>, so the common answer
    /// on a PvE server -- "the channel has it off" -- costs one boolean and nothing else is
    /// consulted.
    ///
    /// <b>Refusing is the default everywhere.</b> No map, no settings, no policy: refused. A
    /// server that permitted player combat because something was unconfigured would be a bug
    /// discovered by players killing each other in a starting town.
    /// </remarks>
    public static class PkPolicy
    {
        /// <summary>
        /// Whether an attack between two players is permitted.
        /// </summary>
        /// <param name="settings">Channel switch and thresholds, from the authority.</param>
        /// <param name="map">The map the attacker is on, from authored content.</param>
        /// <param name="attackerLevel">Read from the server's own character.</param>
        /// <param name="targetLevel">Read from the server's own character.</param>
        /// <param name="sameMap">Whether both stand on the same map.</param>
        /// <param name="sameCharacter">Whether the two are the same player.</param>
        /// <param name="sameParty">From PartyService. Never from the client.</param>
        /// <param name="sameGuild">From GuildService. Never from the client.</param>
        public static PkVerdict Evaluate(in PkSettings settings, MapDefinition map,
            int attackerLevel, int targetLevel, bool sameMap, bool sameCharacter,
            bool sameParty = false, bool sameGuild = false)
        {
            if (map == null)
            {
                return PkVerdict.Refused(PkRejection.MissingContext);
            }

            // The master switch, and the common answer on a PvE server.
            if (!settings.ChannelEnabled)
            {
                return PkVerdict.Refused(PkRejection.ChannelDisabled);
            }

            if (!map.PkAllowed)
            {
                return PkVerdict.Refused(PkRejection.MapDisabled);
            }

            if (map.IsSafeZone)
            {
                return PkVerdict.Refused(PkRejection.SafeZone);
            }

            // Towns are safe by default. A map authored as both a town and PK-allowed is
            // still refused here, because a town that is not safe is almost always an
            // authoring mistake rather than an intent.
            if (map.IsTown)
            {
                return PkVerdict.Refused(PkRejection.Town);
            }

            if (sameCharacter)
            {
                return PkVerdict.Refused(PkRejection.Self);
            }

            if (!sameMap)
            {
                return PkVerdict.Refused(PkRejection.DifferentMap);
            }

            if (settings.MinimumLevel > 0
                && (attackerLevel < settings.MinimumLevel || targetLevel < settings.MinimumLevel))
            {
                // Either side being under the floor stops it. Protecting only the victim
                // would let a level-one alt attack with impunity.
                return PkVerdict.Refused(PkRejection.BelowMinimumLevel);
            }

            if (sameParty && !settings.AllowSameParty)
            {
                return PkVerdict.Refused(PkRejection.SameParty);
            }

            if (sameGuild && !settings.AllowSameGuild)
            {
                return PkVerdict.Refused(PkRejection.SameGuild);
            }

            return PkVerdict.Allowed;
        }
    }
}
