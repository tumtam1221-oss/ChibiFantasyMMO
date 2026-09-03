using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>How a party decides who may claim what a kill dropped.</summary>
    /// <remarks>
    /// Closed technical category: each value is a different assignment rule the domain has
    /// to implement, and one nothing implements would be authored content that silently
    /// behaves as Personal.
    ///
    /// It decides <em>eligibility</em> only. Loot still comes from the one
    /// <c>LootObjectState</c> Phase 10 created, and no policy here creates, copies or
    /// duplicates a loot object.
    /// </remarks>
    public enum PartyLootPolicy
    {
        /// <summary>What Phase 10 does: whoever the loot was attributed to may claim it.</summary>
        Personal = 0,

        /// <summary>Eligibility rotates through the members in join order.</summary>
        RoundRobin = 1,

        /// <summary>Every member is eligible; who wins is decided by a roll the caller runs.</summary>
        NeedGreed = 2
    }

    /// <summary>
    /// The authored limits the social and economic systems obey.
    /// </summary>
    /// <remarks>
    /// <b>One definition rather than five.</b> Party size, guild size, price bounds and
    /// listing limits are all the same kind of thing -- a number an operator tunes -- and
    /// splitting them across five assets would mean five lookups and five chances for a
    /// service to be handed the wrong one.
    ///
    /// <b>Every default is the permissive-but-sane reading of an unauthored field.</b> A
    /// service handed no configuration at all uses <see cref="Default"/>, which carries the
    /// values this phase ships with, rather than zeroing out and refusing everything. That
    /// matters because a zero maximum party size would make parties impossible rather than
    /// unconfigured.
    ///
    /// Flat and DB-friendly: one row of a future <c>social_configuration</c> table.
    /// </remarks>
    public sealed class SocialConfiguration : GameDefinition
    {
        [Tooltip("Most members one party may hold. Zero or less falls back to the shipped default.")]
        [SerializeField] private int _maxPartySize = DefaultMaxPartySize;

        [Tooltip("Most members one guild may hold. Zero or less means no limit.")]
        [SerializeField] private int _maxGuildMembers;

        [Tooltip("Shortest allowed guild name.")]
        [SerializeField] private int _minGuildNameLength = 3;

        [Tooltip("Longest allowed guild name.")]
        [SerializeField] private int _maxGuildNameLength = 24;

        [Tooltip("Lowest price a player shop listing may set. Below one reads as one.")]
        [SerializeField] private int _minListingPrice = 1;

        [Tooltip("Highest price a listing may set. Zero or less means no authored ceiling.")]
        [SerializeField] private int _maxListingPrice;

        [Tooltip("Most listings one shop may hold. Zero or less means no limit.")]
        [SerializeField] private int _maxShopListings;

        [Tooltip("Most item entries one side of a trade may offer. Zero or less means no limit.")]
        [SerializeField] private int _maxTradeItems;

        [Tooltip("Default loot policy for a newly created party.")]
        [SerializeField] private PartyLootPolicy _defaultLootPolicy = PartyLootPolicy.Personal;

        /// <summary>The party size this game ships with.</summary>
        /// <remarks>A named constant rather than a literal scattered through the services,
        /// and still only the <em>default</em>: <see cref="MaxPartySize"/> is what anything
        /// actually reads, and content overrides it.</remarks>
        public const int DefaultMaxPartySize = 6;

        public int MaxPartySize => _maxPartySize > 0 ? _maxPartySize : DefaultMaxPartySize;

        /// <summary>Zero means unlimited.</summary>
        public int MaxGuildMembers => _maxGuildMembers > 0 ? _maxGuildMembers : int.MaxValue;

        public int MinGuildNameLength => _minGuildNameLength > 0 ? _minGuildNameLength : 1;

        public int MaxGuildNameLength => _maxGuildNameLength > 0 ? _maxGuildNameLength : 24;

        public int MinListingPrice => _minListingPrice > 0 ? _minListingPrice : 1;

        public int MaxListingPrice => _maxListingPrice > 0 ? _maxListingPrice : int.MaxValue;

        public int MaxShopListings => _maxShopListings > 0 ? _maxShopListings : int.MaxValue;

        public int MaxTradeItems => _maxTradeItems > 0 ? _maxTradeItems : int.MaxValue;

        public PartyLootPolicy DefaultLootPolicy => _defaultLootPolicy;

        /// <summary>
        /// The values used when no configuration asset was supplied.
        /// </summary>
        /// <remarks>
        /// A readonly struct rather than a constructed <see cref="ScriptableObject"/>,
        /// because a definition cannot be created outside the editor and a service must
        /// still work in a headless server with no assets loaded.
        /// </remarks>
        public readonly struct Limits
        {
            public Limits(int maxPartySize, int maxGuildMembers, int minGuildNameLength,
                int maxGuildNameLength, int minListingPrice, int maxListingPrice,
                int maxShopListings, int maxTradeItems, PartyLootPolicy defaultLootPolicy)
            {
                MaxPartySize = maxPartySize;
                MaxGuildMembers = maxGuildMembers;
                MinGuildNameLength = minGuildNameLength;
                MaxGuildNameLength = maxGuildNameLength;
                MinListingPrice = minListingPrice;
                MaxListingPrice = maxListingPrice;
                MaxShopListings = maxShopListings;
                MaxTradeItems = maxTradeItems;
                DefaultLootPolicy = defaultLootPolicy;
            }

            public int MaxPartySize { get; }

            public int MaxGuildMembers { get; }

            public int MinGuildNameLength { get; }

            public int MaxGuildNameLength { get; }

            public int MinListingPrice { get; }

            public int MaxListingPrice { get; }

            public int MaxShopListings { get; }

            public int MaxTradeItems { get; }

            public PartyLootPolicy DefaultLootPolicy { get; }
        }

        /// <summary>The shipped defaults.</summary>
        public static Limits Default => new Limits(DefaultMaxPartySize, int.MaxValue, 3, 24,
            1, int.MaxValue, int.MaxValue, int.MaxValue, PartyLootPolicy.Personal);

        /// <summary>This asset's values, in the form services read.</summary>
        public Limits ToLimits()
        {
            return new Limits(MaxPartySize, MaxGuildMembers, MinGuildNameLength,
                MaxGuildNameLength, MinListingPrice, MaxListingPrice, MaxShopListings,
                MaxTradeItems, DefaultLootPolicy);
        }

        /// <summary>The limits an optional configuration supplies, or the shipped defaults.</summary>
        public static Limits Resolve(SocialConfiguration configuration)
        {
            return configuration == null ? Default : configuration.ToLimits();
        }
    }
}
