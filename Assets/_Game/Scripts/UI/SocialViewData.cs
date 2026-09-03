using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// What a panel needs to draw one party member.
    /// </summary>
    /// <remarks>
    /// <b>A snapshot of identities and numbers.</b> No character object, no state, no
    /// service. A row that held a live character would let a panel change one, and would keep
    /// it alive after the player logged out.
    ///
    /// Health and mana are copied as plain integers with a flag saying whether they were
    /// available at all. A party member on another map may not have live vitals to show, and
    /// a view that could not tell "zero health" from "no information" would draw a dead
    /// player.
    /// </remarks>
    public readonly struct PartyMemberViewData
    {
        public PartyMemberViewData(CharacterId character, string displayName, int level,
            int health, int maxHealth, int mana, int maxMana, bool vitalsKnown, bool isLeader,
            bool isOnline)
        {
            Character = character;
            DisplayName = displayName;
            Level = level;
            Health = health;
            MaxHealth = maxHealth;
            Mana = mana;
            MaxMana = maxMana;
            VitalsKnown = vitalsKnown;
            IsLeader = isLeader;
            IsOnline = isOnline;
        }

        public CharacterId Character { get; }

        /// <summary>
        /// What to show. Never an identity.
        /// </summary>
        /// <remarks>A name rather than a localization key, because a player's name is not
        /// authored content and has nothing to translate to.</remarks>
        public string DisplayName { get; }

        public int Level { get; }

        public int Health { get; }

        public int MaxHealth { get; }

        public int Mana { get; }

        public int MaxMana { get; }

        /// <summary>Whether the vitals above mean anything.</summary>
        public bool VitalsKnown { get; }

        public bool IsLeader { get; }

        public bool IsOnline { get; }

        public bool IsValid => Character.IsValid;

        public static PartyMemberViewData None => default;
    }

    /// <summary>What a panel needs to draw a party.</summary>
    public readonly struct PartyViewData
    {
        public PartyViewData(PartyId party, CharacterId leader, int memberCount, int maxMembers,
            PartyLootPolicy lootPolicy, bool viewerIsLeader)
        {
            Party = party;
            Leader = leader;
            MemberCount = memberCount;
            MaxMembers = maxMembers;
            LootPolicy = lootPolicy;
            ViewerIsLeader = viewerIsLeader;
        }

        public PartyId Party { get; }

        public CharacterId Leader { get; }

        public int MemberCount { get; }

        /// <summary>The authored ceiling, so a panel can show "4 / 6" without knowing it is six.</summary>
        public int MaxMembers { get; }

        public PartyLootPolicy LootPolicy { get; }

        /// <summary>
        /// Whether the viewer may use the leader-only actions.
        /// </summary>
        /// <remarks>Advisory. The domain checks again on every request, so a panel that
        /// showed the button anyway could not authorise anything.</remarks>
        public bool ViewerIsLeader { get; }

        public bool IsValid => Party.IsValid;

        public static PartyViewData None => default;
    }

    /// <summary>What a panel needs to draw one guild member.</summary>
    public readonly struct GuildMemberViewData
    {
        public GuildMemberViewData(CharacterId character, string displayName, DefinitionId rank,
            LocalizationKey rankNameKey, int rankOrder, bool isLeader, bool isOnline)
        {
            Character = character;
            DisplayName = displayName;
            Rank = rank;
            RankNameKey = rankNameKey;
            RankOrder = rankOrder;
            IsLeader = isLeader;
            IsOnline = isOnline;
        }

        public CharacterId Character { get; }

        public string DisplayName { get; }

        public DefinitionId Rank { get; }

        /// <summary>The rank's name, as a key: a rank <em>is</em> authored content.</summary>
        public LocalizationKey RankNameKey { get; }

        public int RankOrder { get; }

        public bool IsLeader { get; }

        public bool IsOnline { get; }

        public bool IsValid => Character.IsValid;

        public static GuildMemberViewData None => default;
    }

    /// <summary>What a panel needs to draw a guild.</summary>
    public readonly struct GuildViewData
    {
        public GuildViewData(GuildId guild, string name, CharacterId leader, int memberCount,
            int maxMembers, DefinitionId viewerRank, GuildPermission viewerPermissions)
        {
            Guild = guild;
            Name = name;
            Leader = leader;
            MemberCount = memberCount;
            MaxMembers = maxMembers;
            ViewerRank = viewerRank;
            ViewerPermissions = viewerPermissions;
        }

        public GuildId Guild { get; }

        public string Name { get; }

        public CharacterId Leader { get; }

        public int MemberCount { get; }

        public int MaxMembers { get; }

        public DefinitionId ViewerRank { get; }

        /// <summary>
        /// What the viewer's rank allows.
        /// </summary>
        /// <remarks>Copied from the rank so a panel can grey out what it must without
        /// re-deriving permissions. Advisory: the service checks again.</remarks>
        public GuildPermission ViewerPermissions { get; }

        public bool Allows(GuildPermission permission)
        {
            return permission != GuildPermission.None
                && (ViewerPermissions & permission) == permission;
        }

        public bool IsValid => Guild.IsValid;

        public static GuildViewData None => default;
    }

    /// <summary>What a wallet display needs for one currency.</summary>
    public readonly struct CurrencyViewData
    {
        public CurrencyViewData(DefinitionId currency, LocalizationKey nameKey, AssetRef icon,
            int amount, int maximum)
        {
            Currency = currency;
            NameKey = nameKey;
            Icon = icon;
            Amount = amount;
            Maximum = maximum;
        }

        public DefinitionId Currency { get; }

        public LocalizationKey NameKey { get; }

        public AssetRef Icon { get; }

        public int Amount { get; }

        public int Maximum { get; }

        public bool IsValid => Currency.IsValid;

        public static CurrencyViewData None => default;
    }

    /// <summary>
    /// What a trade window needs to draw one side.
    /// </summary>
    /// <remarks>
    /// <see cref="HasAccepted"/> is a snapshot like everything else. A panel that cached it
    /// across an offer change would show a stale tick beside a changed offer, which is the
    /// exact confusion the acceptance-reset rule exists to prevent -- so the controller
    /// rebuilds this whenever the session's revision moves.
    /// </remarks>
    public readonly struct TradeSideViewData
    {
        public TradeSideViewData(CharacterId character, string displayName, int itemCount,
            int currencyEntryCount, bool hasAccepted)
        {
            Character = character;
            DisplayName = displayName;
            ItemCount = itemCount;
            CurrencyEntryCount = currencyEntryCount;
            HasAccepted = hasAccepted;
        }

        public CharacterId Character { get; }

        public string DisplayName { get; }

        public int ItemCount { get; }

        public int CurrencyEntryCount { get; }

        public bool HasAccepted { get; }

        public bool IsValid => Character.IsValid;

        public static TradeSideViewData None => default;
    }

    /// <summary>What a trade window needs to draw the session.</summary>
    /// <remarks>Booleans rather than the gameplay state enum, because the UI assembly does
    /// not reference Gameplay and must not start.</remarks>
    public readonly struct TradeViewData
    {
        public TradeViewData(InstanceId trade, TradeSideViewData self, TradeSideViewData other,
            bool isOpen, bool bothAccepted, bool isFinished)
        {
            Trade = trade;
            Self = self;
            Other = other;
            IsOpen = isOpen;
            BothAccepted = bothAccepted;
            IsFinished = isFinished;
        }

        public InstanceId Trade { get; }

        public TradeSideViewData Self { get; }

        public TradeSideViewData Other { get; }

        public bool IsOpen { get; }

        public bool BothAccepted { get; }

        public bool IsFinished { get; }

        public bool IsValid => Trade.IsValid;

        public static TradeViewData None => default;
    }

    /// <summary>
    /// What a shop window needs to draw one listing.
    /// </summary>
    /// <remarks>
    /// Item identity, definition and price. The item's name, icon and tooltip come from the
    /// existing item view path, which is why nothing about the item's appearance is copied
    /// here -- there is one item renderer and this does not become a second.
    /// </remarks>
    public readonly struct ShopListingViewData
    {
        public ShopListingViewData(InstanceId listing, InstanceId item, DefinitionId itemDefinition,
            CharacterId seller, string sellerName, int quantity, DefinitionId currency,
            int unitPrice, bool isActive, bool viewerIsSeller, bool viewerCanAfford)
        {
            Listing = listing;
            Item = item;
            ItemDefinition = itemDefinition;
            Seller = seller;
            SellerName = sellerName;
            Quantity = quantity;
            Currency = currency;
            UnitPrice = unitPrice;
            IsActive = isActive;
            ViewerIsSeller = viewerIsSeller;
            ViewerCanAfford = viewerCanAfford;
        }

        public InstanceId Listing { get; }

        public InstanceId Item { get; }

        public DefinitionId ItemDefinition { get; }

        public CharacterId Seller { get; }

        public string SellerName { get; }

        public int Quantity { get; }

        public DefinitionId Currency { get; }

        public int UnitPrice { get; }

        public bool IsActive { get; }

        public bool ViewerIsSeller { get; }

        /// <summary>Advisory. The service checks the balance again at purchase.</summary>
        public bool ViewerCanAfford { get; }

        public bool IsValid => Listing.IsValid;

        public static ShopListingViewData None => default;
    }

    /// <summary>What a shop window needs to draw the shop itself.</summary>
    public readonly struct PlayerShopViewData
    {
        public PlayerShopViewData(InstanceId shop, string name, CharacterId owner,
            string ownerName, DefinitionId map, int activeListings, int maxListings, bool isOpen,
            bool viewerIsOwner)
        {
            Shop = shop;
            Name = name;
            Owner = owner;
            OwnerName = ownerName;
            Map = map;
            ActiveListings = activeListings;
            MaxListings = maxListings;
            IsOpen = isOpen;
            ViewerIsOwner = viewerIsOwner;
        }

        public InstanceId Shop { get; }

        public string Name { get; }

        public CharacterId Owner { get; }

        public string OwnerName { get; }

        /// <summary>Reference to a <see cref="MapDefinition"/>. A map id, never a scene name.</summary>
        public DefinitionId Map { get; }

        public int ActiveListings { get; }

        public int MaxListings { get; }

        public bool IsOpen { get; }

        public bool ViewerIsOwner { get; }

        public bool IsValid => Shop.IsValid;

        public static PlayerShopViewData None => default;
    }
}
