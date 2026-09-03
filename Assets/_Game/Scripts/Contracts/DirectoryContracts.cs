using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>Whether a server is taking players.</summary>
    /// <remarks>
    /// Reported by the authority, never inferred by a client from a population number. A
    /// client that decided "busy" for itself would let a player queue into a server the
    /// operator had closed.
    /// </remarks>
    public enum ServerStatus
    {
        /// <summary>Not known. Treated as unselectable, because unknown is not permission.</summary>
        Unknown = 0,

        Online = 1,

        /// <summary>Accepting players, but heavily loaded.</summary>
        Busy = 2,

        Maintenance = 3,

        Offline = 4,

        /// <summary>Exists but should not be listed. Selection is still refused.</summary>
        Hidden = 5
    }

    /// <summary>Whether a channel is taking players.</summary>
    public enum ChannelStatus
    {
        Unknown = 0,
        Online = 1,
        Busy = 2,
        Maintenance = 3,
        Offline = 4
    }

    /// <summary>
    /// A population figure, and whether it is actually known.
    /// </summary>
    /// <remarks>
    /// <b>Unknown is a first-class answer.</b> Until a live server reports one, there is no
    /// population figure, and inventing a plausible-looking number would be a lie a player
    /// makes decisions on. A client shows a crowding bar when the reading is known and shows
    /// nothing when it is not.
    ///
    /// The same shape serves character presence, for the same reason: absent information is
    /// reported as absent rather than as "offline".
    /// </remarks>
    public readonly struct PopulationReading
    {
        private PopulationReading(bool known, int value, int capacity)
        {
            IsKnown = known;
            Value = value;
            Capacity = capacity;
        }

        /// <summary>Whether <see cref="Value"/> means anything.</summary>
        public bool IsKnown { get; }

        /// <summary>How many are present. Meaningless unless <see cref="IsKnown"/>.</summary>
        public int Value { get; }

        /// <summary>How many fit. Zero means no authored ceiling.</summary>
        public int Capacity { get; }

        /// <summary>Whether the place is at or past its ceiling. False when unknown.</summary>
        public bool IsFull => IsKnown && Capacity > 0 && Value >= Capacity;

        /// <summary>No figure is available.</summary>
        public static PopulationReading Unknown(int capacity = 0)
        {
            return new PopulationReading(false, 0, capacity);
        }

        /// <summary>A figure the authority actually reported.</summary>
        public static PopulationReading Known(int value, int capacity = 0)
        {
            return new PopulationReading(true, value < 0 ? 0 : value, capacity);
        }

        public override string ToString()
        {
            if (!IsKnown) return "population unknown";
            return Capacity > 0 ? Value + "/" + Capacity : Value.ToString();
        }
    }

    /// <summary>
    /// One server, as the authority describes it.
    /// </summary>
    /// <remarks>
    /// <b>Delivered, not authored in code.</b> The list comes from
    /// <c>IAccountApi.GetServers</c>. Nothing anywhere compares a
    /// <see cref="ServerId"/> to a literal, and there is no first-server or default-server
    /// rule: which servers exist, what they are called and whether they are open are all the
    /// authority's answers.
    ///
    /// <b>The version requirement travels with the server.</b> Two servers may be on
    /// different builds during a staged rollout, so the compatibility floor is per server
    /// rather than global.
    ///
    /// Flat because it has to travel and to persist: one row of a future
    /// <c>server_definition</c> table.
    /// </remarks>
    public readonly struct ServerInfo
    {
        public ServerInfo(ServerId server, LocalizationKey nameKey, string region,
            ServerStatus status, PopulationReading population, VersionRequirement versions,
            bool enabled = true, Revision revision = default)
        {
            Server = server;
            NameKey = nameKey;
            Region = region;
            Status = status;
            Population = population;
            Versions = versions;
            Enabled = enabled;
            Revision = revision;
        }

        public ServerId Server { get; }

        /// <summary>The display name, as a key: a server's name is authored content.</summary>
        public LocalizationKey NameKey { get; }

        /// <summary>Where it is hosted, for a player choosing by latency.</summary>
        public string Region { get; }

        public ServerStatus Status { get; }

        public PopulationReading Population { get; }

        /// <summary>What a client must be running to connect here.</summary>
        public VersionRequirement Versions { get; }

        /// <summary>Turned off by configuration, whatever its status says.</summary>
        public bool Enabled { get; }

        public Revision Revision { get; }

        public bool IsValid => Server.IsValid;

        /// <summary>
        /// Whether the authority is presenting this as selectable.
        /// </summary>
        /// <remarks>Advisory for a client greying out a row. The selection service asks the
        /// same question and is the one that decides.</remarks>
        public bool IsSelectable => Enabled
            && (Status == ServerStatus.Online || Status == ServerStatus.Busy);

        public override string ToString()
        {
            return Server + " (" + Status + ", " + Population + ")";
        }
    }

    /// <summary>
    /// One channel of one server, as the authority describes it.
    /// </summary>
    /// <remarks>
    /// <b>It names its server.</b> A channel number alone is a label -- channel 1 exists
    /// everywhere -- so the pairing is carried and checked rather than assumed. That is what
    /// stops a client selecting server A and channel 1 of server B.
    ///
    /// <b>PK is configuration.</b> <see cref="PkEnabled"/> arrives from the authority, which
    /// will read it from a database column an administrator sets. Nothing compares a channel
    /// to a number to decide it, a client cannot set it, and a future server re-reads it
    /// rather than believing what a client displays.
    /// </remarks>
    public readonly struct ChannelInfo
    {
        public ChannelInfo(ChannelId channel, ServerId server, LocalizationKey nameKey,
            ChannelStatus status, PopulationReading population, bool pkEnabled = false,
            bool enabled = true, Revision revision = default)
        {
            Channel = channel;
            Server = server;
            NameKey = nameKey;
            Status = status;
            Population = population;
            PkEnabled = pkEnabled;
            Enabled = enabled;
            Revision = revision;
        }

        public ChannelId Channel { get; }

        /// <summary>Which server this channel belongs to. Checked on selection.</summary>
        public ServerId Server { get; }

        public LocalizationKey NameKey { get; }

        public ChannelStatus Status { get; }

        public PopulationReading Population { get; }

        /// <summary>
        /// Whether player-versus-player is on here.
        /// </summary>
        /// <remarks>The configuration seam the game design asked for: an administrator turns
        /// it on in the database, the authority reports it, and the client only displays it.
        /// A future server enforces it and does not consult the client.</remarks>
        public bool PkEnabled { get; }

        public bool Enabled { get; }

        public Revision Revision { get; }

        public bool IsValid => Channel.IsValid && Server.IsValid;

        /// <summary>Advisory. The selection service decides.</summary>
        public bool IsSelectable => Enabled
            && (Status == ChannelStatus.Online || Status == ChannelStatus.Busy);

        public override string ToString()
        {
            return Channel + " on " + Server + " (" + Status + (PkEnabled ? ", PK" : string.Empty)
                + ")";
        }
    }
}
