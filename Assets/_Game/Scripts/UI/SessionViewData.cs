using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What a screen is currently doing.
    /// </summary>
    /// <remarks>
    /// <b>Four states, and no progress number.</b> A percentage would have to come from
    /// somewhere, and nothing in this project can report how far a login has got. Inventing
    /// one is worse than an indeterminate spinner, because a player believes a bar.
    /// </remarks>
    public enum PanelStatus
    {
        /// <summary>Waiting for the player.</summary>
        Idle = 0,

        /// <summary>Waiting for the authority. Indeterminate; there is no progress source.</summary>
        Loading = 1,

        /// <summary>The last command was accepted.</summary>
        Success = 2,

        /// <summary>The last command was refused. See the reason carried alongside.</summary>
        Error = 3
    }

    /// <summary>What a login panel needs to draw.</summary>
    /// <remarks>
    /// <b>No credential field appears here.</b> A panel collects a secret in its own input
    /// widget and hands it to the backend; it never reaches view data, never reaches a
    /// controller's state and never reaches the domain. Nothing that could be inspected in a
    /// debugger or serialized into a bug report holds one.
    /// </remarks>
    public readonly struct LoginViewData
    {
        public LoginViewData(PanelStatus status, LoginRejection reason,
            VersionCompatibility compatibility, VersionKind failingVersion,
            VersionNumber expectedVersion, bool isSignedIn, string accountDisplayName)
        {
            Status = status;
            Reason = reason;
            Compatibility = compatibility;
            FailingVersion = failingVersion;
            ExpectedVersion = expectedVersion;
            IsSignedIn = isSignedIn;
            AccountDisplayName = accountDisplayName;
        }

        public PanelStatus Status { get; }

        public LoginRejection Reason { get; }

        public VersionCompatibility Compatibility { get; }

        /// <summary>Which version was unacceptable, so a launcher knows what to fetch.</summary>
        public VersionKind FailingVersion { get; }

        public VersionNumber ExpectedVersion { get; }

        public bool IsSignedIn { get; }

        /// <summary>Display only, and only after signing in.</summary>
        public string AccountDisplayName { get; }

        /// <summary>Whether the player should be told an update exists.</summary>
        public bool ShouldOfferUpdate => Compatibility == VersionCompatibility.OptionalUpdate;

        /// <summary>Whether the player cannot proceed without patching.</summary>
        public bool RequiresUpdate => Compatibility == VersionCompatibility.RequiredUpdate
            || Compatibility == VersionCompatibility.Incompatible;

        public static LoginViewData None => default;
    }

    /// <summary>What a server row needs to draw.</summary>
    /// <remarks>
    /// <see cref="PopulationKnown"/> exists so a bar is drawn only when there is a figure. A
    /// view that showed zero for an unknown population would tell a player a live server is
    /// empty.
    /// </remarks>
    public readonly struct ServerRowViewData
    {
        public ServerRowViewData(ServerId server, LocalizationKey nameKey, string region,
            ServerStatus status, bool populationKnown, int population, int capacity,
            bool isSelectable, bool isSelected, SessionRejection blockedBy)
        {
            Server = server;
            NameKey = nameKey;
            Region = region;
            Status = status;
            PopulationKnown = populationKnown;
            Population = population;
            Capacity = capacity;
            IsSelectable = isSelectable;
            IsSelected = isSelected;
            BlockedBy = blockedBy;
        }

        public ServerId Server { get; }

        public LocalizationKey NameKey { get; }

        public string Region { get; }

        public ServerStatus Status { get; }

        /// <summary>Whether <see cref="Population"/> means anything.</summary>
        public bool PopulationKnown { get; }

        public int Population { get; }

        public int Capacity { get; }

        /// <summary>
        /// Whether the row should be offered.
        /// </summary>
        /// <remarks>Advisory, obtained by asking the flow service. The service asks again when
        /// the player actually clicks, so a stale hint cannot let anybody in.</remarks>
        public bool IsSelectable { get; }

        public bool IsSelected { get; }

        /// <summary>Why the row is not offered, so the reason can be shown rather than guessed.</summary>
        public SessionRejection BlockedBy { get; }

        public bool IsValid => Server.IsValid;

        public static ServerRowViewData None => default;
    }

    /// <summary>What a channel row needs to draw.</summary>
    public readonly struct ChannelRowViewData
    {
        public ChannelRowViewData(ChannelId channel, ServerId server, LocalizationKey nameKey,
            ChannelStatus status, bool populationKnown, int population, int capacity,
            bool pkEnabled, bool isSelectable, bool isSelected, SessionRejection blockedBy)
        {
            Channel = channel;
            Server = server;
            NameKey = nameKey;
            Status = status;
            PopulationKnown = populationKnown;
            Population = population;
            Capacity = capacity;
            PkEnabled = pkEnabled;
            IsSelectable = isSelectable;
            IsSelected = isSelected;
            BlockedBy = blockedBy;
        }

        public ChannelId Channel { get; }

        public ServerId Server { get; }

        public LocalizationKey NameKey { get; }

        public ChannelStatus Status { get; }

        public bool PopulationKnown { get; }

        public int Population { get; }

        public int Capacity { get; }

        /// <summary>
        /// Whether this channel allows player-versus-player.
        /// </summary>
        /// <remarks>Copied from the authority's <see cref="ChannelInfo"/> purely so a badge can
        /// be shown. The client cannot set it, and a future server enforces PK from its own
        /// configuration rather than from anything displayed here.</remarks>
        public bool PkEnabled { get; }

        public bool IsSelectable { get; }

        public bool IsSelected { get; }

        public SessionRejection BlockedBy { get; }

        public bool IsValid => Channel.IsValid;

        public static ChannelRowViewData None => default;
    }

    /// <summary>What a character row needs to draw.</summary>
    /// <remarks>
    /// A projection of <see cref="CharacterSelectEntry"/>, which is itself already a summary.
    /// No stats, no inventory, no equipment: a select screen shows who a character is, and the
    /// game server loads what they own after entering the world.
    /// </remarks>
    public readonly struct CharacterRowViewData
    {
        public CharacterRowViewData(CharacterId character, string name, CharacterGender gender,
            int level, DefinitionId characterClass, DefinitionId job, DefinitionId map,
            DefinitionId appearance, CharacterAvailability availability, bool isSelectable,
            bool isSelected)
        {
            Character = character;
            Name = name;
            Gender = gender;
            Level = level;
            Class = characterClass;
            Job = job;
            Map = map;
            Appearance = appearance;
            Availability = availability;
            IsSelectable = isSelectable;
            IsSelected = isSelected;
        }

        public CharacterId Character { get; }

        public string Name { get; }

        public CharacterGender Gender { get; }

        public int Level { get; }

        public DefinitionId Class { get; }

        public DefinitionId Job { get; }

        /// <summary>Reference to a <see cref="MapDefinition"/>, never a scene name.</summary>
        public DefinitionId Map { get; }

        public DefinitionId Appearance { get; }

        public CharacterAvailability Availability { get; }

        public bool IsSelectable { get; }

        public bool IsSelected { get; }

        public bool IsValid => Character.IsValid;

        public static CharacterRowViewData None => default;
    }

    /// <summary>Where the whole flow stands, for whichever screen is showing.</summary>
    public readonly struct SessionFlowViewData
    {
        public SessionFlowViewData(SessionState state, PanelStatus status,
            SessionRejection reason, ServerId server, ChannelId channel, CharacterId character,
            int usedCharacterSlots, int maxCharacterSlots, bool canEnterWorld)
        {
            State = state;
            Status = status;
            Reason = reason;
            Server = server;
            Channel = channel;
            Character = character;
            UsedCharacterSlots = usedCharacterSlots;
            MaxCharacterSlots = maxCharacterSlots;
            CanEnterWorld = canEnterWorld;
        }

        public SessionState State { get; }

        public PanelStatus Status { get; }

        public SessionRejection Reason { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        public CharacterId Character { get; }

        public int UsedCharacterSlots { get; }

        /// <summary>The authored ceiling, so a screen shows "2 / 5" without knowing it is five.</summary>
        public int MaxCharacterSlots { get; }

        /// <summary>Advisory. The flow service decides when the button is actually pressed.</summary>
        public bool CanEnterWorld { get; }

        public bool IsSignedIn => State != SessionState.Unauthenticated
            && State != SessionState.Expired && State != SessionState.Revoked;

        public static SessionFlowViewData None => default;
    }
}
