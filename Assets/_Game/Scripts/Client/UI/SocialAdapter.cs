using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns parties, guilds, wallets, trades and shops into view data. The read half.
    /// </summary>
    /// <remarks>
    /// <b>Reads only.</b> Nothing here invites, kicks, offers, buys or credits; every output
    /// is a snapshot. Building a panel twenty times costs nothing and changes nothing.
    ///
    /// <b>Permission hints are asked, not derived.</b> A guild row's
    /// <see cref="GuildViewData.ViewerPermissions"/> comes from the rank definition the
    /// service reads, and a shop row's affordability comes from the wallet the service will
    /// check. The UI and the domain therefore give the same answer, and where they cannot,
    /// the domain wins -- every hint is re-checked at the command boundary.
    ///
    /// <b>Names are supplied, not invented.</b> A character's display name is not authored
    /// content and has no localization key; the caller passes a resolver, and with none the
    /// id is shown rather than a fabricated name.
    /// </remarks>
    public static class SocialAdapter
    {
        /// <summary>Resolves a character's display name.</summary>
        /// <remarks>A delegate rather than a registry because names come from the account
        /// system, not from content, and this assembly must not reach into it.</remarks>
        public delegate string NameResolver(CharacterId character);

        /// <summary>The registries and helpers these views need.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<CurrencyDefinition> currencies = null,
                IDefinitionRegistry<GuildRankDefinition> ranks = null,
                SocialConfiguration configuration = null,
                NameResolver names = null)
            {
                Currencies = currencies;
                Ranks = ranks;
                Limits = SocialConfiguration.Resolve(configuration);
                Names = names;
            }

            public IDefinitionRegistry<CurrencyDefinition> Currencies { get; }

            public IDefinitionRegistry<GuildRankDefinition> Ranks { get; }

            public SocialConfiguration.Limits Limits { get; }

            public NameResolver Names { get; }

            /// <summary>The display name, or the identity when nothing can resolve it.</summary>
            public string NameOf(CharacterId character)
            {
                if (Names == null) return character.ToString();

                string resolved = Names(character);
                return string.IsNullOrEmpty(resolved) ? character.ToString() : resolved;
            }
        }

        /// <summary>What a member row shows. Vitals are optional and flagged when absent.</summary>
        public readonly struct MemberVitals
        {
            public MemberVitals(int level, int health, int maxHealth, int mana, int maxMana,
                bool online)
            {
                Level = level;
                Health = health;
                MaxHealth = maxHealth;
                Mana = mana;
                MaxMana = maxMana;
                Online = online;
                Known = true;
            }

            public int Level { get; }

            public int Health { get; }

            public int MaxHealth { get; }

            public int Mana { get; }

            public int MaxMana { get; }

            public bool Online { get; }

            public bool Known { get; }
        }

        /// <summary>Supplies a member's vitals, or nothing when they are not known.</summary>
        public delegate bool VitalsResolver(CharacterId character, out MemberVitals vitals);

        // ---- party ---------------------------------------------------------------------

        /// <summary>What a panel should say about a party.</summary>
        public static PartyViewData BuildParty(PartyState party, CharacterId viewer,
            in Context context)
        {
            if (party == null || !party.IsActive) return PartyViewData.None;

            return new PartyViewData(party.Id, party.Leader, party.MemberCount,
                context.Limits.MaxPartySize, party.LootPolicy, party.IsLeader(viewer));
        }

        /// <summary>Fills <paramref name="into"/> with one row per member, in join order.</summary>
        public static void BuildPartyMembers(PartyState party, in Context context,
            List<PartyMemberViewData> into, VitalsResolver vitals = null)
        {
            if (into == null) return;

            into.Clear();
            if (party == null || !party.IsActive) return;

            IReadOnlyList<CharacterId> members = party.Members;

            for (int i = 0; i < members.Count; i++)
            {
                CharacterId member = members[i];

                MemberVitals known = default;
                bool haveVitals = vitals != null && vitals(member, out known);

                into.Add(new PartyMemberViewData(member, context.NameOf(member),
                    haveVitals ? known.Level : 0,
                    haveVitals ? known.Health : 0, haveVitals ? known.MaxHealth : 0,
                    haveVitals ? known.Mana : 0, haveVitals ? known.MaxMana : 0,
                    haveVitals, party.IsLeader(member), haveVitals && known.Online));
            }
        }

        // ---- guild ---------------------------------------------------------------------

        /// <summary>What a panel should say about a guild.</summary>
        public static GuildViewData BuildGuild(GuildState guild, CharacterId viewer,
            in Context context)
        {
            if (guild == null || !guild.IsActive) return GuildViewData.None;

            DefinitionId rankId = guild.RankOf(viewer);
            GuildPermission permissions = GuildPermission.None;

            if (context.Ranks != null && rankId.IsValid)
            {
                GuildRankDefinition rank;
                if (context.Ranks.TryGet(rankId, out rank) && rank != null)
                {
                    permissions = rank.Permissions;
                }
            }

            return new GuildViewData(guild.Id, guild.Name, guild.Leader, guild.MemberCount,
                context.Limits.MaxGuildMembers, rankId, permissions);
        }

        /// <summary>Fills <paramref name="into"/> with one row per guild member.</summary>
        public static void BuildGuildMembers(GuildState guild, in Context context,
            List<GuildMemberViewData> into, VitalsResolver vitals = null)
        {
            if (into == null) return;

            into.Clear();
            if (guild == null || !guild.IsActive) return;

            IReadOnlyList<GuildMember> members = guild.Members;

            for (int i = 0; i < members.Count; i++)
            {
                GuildMember member = members[i];

                LocalizationKey rankName = default;
                int order = 0;

                if (context.Ranks != null)
                {
                    GuildRankDefinition rank;
                    if (context.Ranks.TryGet(member.Rank, out rank) && rank != null)
                    {
                        rankName = rank.NameKey;
                        order = rank.Order;
                    }
                }

                MemberVitals known = default;
                bool haveVitals = vitals != null && vitals(member.Character, out known);

                into.Add(new GuildMemberViewData(member.Character,
                    context.NameOf(member.Character), member.Rank, rankName, order,
                    guild.IsLeader(member.Character), haveVitals && known.Online));
            }
        }

        // ---- currency ------------------------------------------------------------------

        /// <summary>Fills <paramref name="into"/> with a row per currency the wallet holds.</summary>
        /// <remarks>Every authored currency is shown when a registry is supplied, so a player
        /// sees a zero balance rather than a missing row; without one, only what is held.</remarks>
        public static void BuildWallet(CharacterWalletState wallet, in Context context,
            List<CurrencyViewData> into)
        {
            if (into == null) return;

            into.Clear();
            if (wallet == null) return;

            if (context.Currencies == null)
            {
                foreach (KeyValuePair<DefinitionId, int> held in wallet.Balances)
                {
                    into.Add(new CurrencyViewData(held.Key, default, default, held.Value,
                        int.MaxValue));
                }

                return;
            }

            IReadOnlyList<CurrencyDefinition> all = context.Currencies.All;

            for (int i = 0; i < all.Count; i++)
            {
                CurrencyDefinition currency = all[i];
                if (currency == null || !currency.Enabled) continue;

                into.Add(new CurrencyViewData(currency.Id, currency.NameKey, currency.Icon,
                    wallet.BalanceOf(currency.Id), currency.MaximumBalance));
            }
        }

        // ---- trade ---------------------------------------------------------------------

        /// <summary>What a trade window should say.</summary>
        public static TradeViewData BuildTrade(TradeSession session, CharacterId viewer,
            in Context context)
        {
            if (session == null || !session.Involves(viewer)) return TradeViewData.None;

            TradeOffer mine = session.OfferOf(viewer);
            TradeOffer theirs = session.CounterpartyOf(viewer);

            return new TradeViewData(session.TradeId, BuildSide(mine, context),
                BuildSide(theirs, context), session.IsOpen, session.BothAccepted,
                session.IsTerminal);
        }

        private static TradeSideViewData BuildSide(TradeOffer offer, in Context context)
        {
            if (offer == null) return TradeSideViewData.None;

            return new TradeSideViewData(offer.Character, context.NameOf(offer.Character),
                offer.Items.Count, offer.Currency.Count, offer.HasAccepted);
        }

        /// <summary>Fills <paramref name="into"/> with the item instances one side offered.</summary>
        /// <remarks>Identities only. The rows are drawn by the existing item view path, which
        /// is why no name or icon is copied here.</remarks>
        public static void BuildTradeItems(TradeOffer offer, List<InstanceId> into)
        {
            if (into == null) return;

            into.Clear();
            if (offer == null) return;

            IReadOnlyList<TradeOfferItem> items = offer.Items;

            for (int i = 0; i < items.Count; i++) into.Add(items[i].Instance);
        }

        // ---- player shop ---------------------------------------------------------------

        /// <summary>What a shop window should say about the shop.</summary>
        public static PlayerShopViewData BuildShop(PlayerShop shop, CharacterId viewer,
            in Context context)
        {
            if (shop == null) return PlayerShopViewData.None;

            return new PlayerShopViewData(shop.ShopId, shop.Name, shop.Owner,
                context.NameOf(shop.Owner), shop.Placement.Map, shop.ActiveListingCount,
                context.Limits.MaxShopListings, shop.IsOpen, shop.Owner == viewer);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the shop's listings.
        /// </summary>
        /// <remarks>Affordability is read from the viewer's wallet as a hint so a button can
        /// be greyed; the service checks the balance again when the purchase is actually
        /// submitted, so a stale hint cannot authorise anything.</remarks>
        public static void BuildListings(PlayerShop shop, CharacterId viewer,
            CharacterWalletState viewerWallet, in Context context,
            List<ShopListingViewData> into, bool activeOnly = true)
        {
            if (into == null) return;

            into.Clear();
            if (shop == null) return;

            IReadOnlyList<PlayerShopListing> listings = shop.Listings;

            for (int i = 0; i < listings.Count; i++)
            {
                PlayerShopListing listing = listings[i];
                if (activeOnly && !listing.IsActive) continue;

                bool affordable = viewerWallet != null
                    && viewerWallet.CanAfford(listing.Currency, listing.UnitPrice);

                into.Add(new ShopListingViewData(listing.ListingId, listing.Item,
                    listing.ItemDefinition, listing.Seller, context.NameOf(listing.Seller),
                    listing.Quantity, listing.Currency, listing.UnitPrice, listing.IsActive,
                    listing.Seller == viewer, affordable));
            }
        }
    }
}
