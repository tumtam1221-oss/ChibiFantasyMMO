using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Content and characters for the party, guild, economy, trade and shop tests.
    /// </summary>
    /// <remarks>
    /// <b>Every limit is a fixture value.</b> Party size, price bounds and currency ceilings
    /// are authored on a <see cref="SocialConfiguration"/>, exactly as an operator would set
    /// them. Nothing in the services knows any of them, which is the property these fixtures
    /// exist to prove -- so a test asserting "six" against a hard-coded six would be testing
    /// the wrong thing, and instead asserts against the authored value.
    ///
    /// <b>Seven characters.</b> One more than a party holds, so the seventh's rejection is a
    /// real case rather than a contrived one.
    /// </remarks>
    internal abstract class SocialTestBase
    {
        protected DefinitionRegistry<ItemDefinition> Items;
        protected DefinitionRegistry<CurrencyDefinition> Currencies;
        protected DefinitionRegistry<GuildRankDefinition> Ranks;
        protected DefinitionRegistry<MapDefinition> Maps;

        protected SocialConfiguration Configuration;
        protected TransactionLedger Ledger;
        protected LocalGuildNameRegister GuildNames;
        protected PartyDirectory Parties;
        protected GuildDirectory Guilds;

        private List<Object> _created;

        // ---- characters ----------------------------------------------------------------

        protected CharacterId Alice;
        protected CharacterId Bob;
        protected CharacterId Carol;
        protected CharacterId Dave;
        protected CharacterId Erin;
        protected CharacterId Frank;
        protected CharacterId Grace;      // the seventh

        protected OwnerId AliceOwner;
        protected OwnerId BobOwner;
        protected OwnerId CarolOwner;

        protected CharacterId[] SixCharacters;

        // ---- content -------------------------------------------------------------------

        protected const string Gold = "currency.gold";
        protected const string Token = "currency.token";     // ceiling of 1000
        protected const string OffCurrency = "currency.off"; // disabled

        protected const string RankLeader = "guildrank.leader";
        protected const string RankOfficer = "guildrank.officer";
        protected const string RankMember = "guildrank.member";

        protected const string Potion = "item.potion";        // stackable to 99, tradable
        protected const string Sword = "equip.sword";         // tradable equipment
        protected const string Helm = "equip.helm";           // tradable equipment
        protected const string Bound = "item.bound";          // authored untradable
        protected const string Card = "card.stat";            // tradable card item

        protected const string TownMap = "map.town";
        protected const string BossMap = "map.boss";

        /// <summary>The party ceiling this fixture authors. Read, never assumed.</summary>
        protected const int MaxParty = 6;

        [SetUp]
        public void SetUpSocialFixtures()
        {
            Items = new DefinitionRegistry<ItemDefinition>();
            Currencies = new DefinitionRegistry<CurrencyDefinition>();
            Ranks = new DefinitionRegistry<GuildRankDefinition>();
            Maps = new DefinitionRegistry<MapDefinition>();

            _created = new List<Object>();

            Ledger = new TransactionLedger();
            GuildNames = new LocalGuildNameRegister();
            Parties = new PartyDirectory();
            Guilds = new GuildDirectory();

            Alice = new CharacterId("char:alice");
            Bob = new CharacterId("char:bob");
            Carol = new CharacterId("char:carol");
            Dave = new CharacterId("char:dave");
            Erin = new CharacterId("char:erin");
            Frank = new CharacterId("char:frank");
            Grace = new CharacterId("char:grace");

            AliceOwner = new OwnerId("account:alice");
            BobOwner = new OwnerId("account:bob");
            CarolOwner = new OwnerId("account:carol");

            SixCharacters = new[] { Alice, Bob, Carol, Dave, Erin, Frank };

            Configuration = AddConfiguration();

            AddCurrency(Gold);
            AddCurrency(Token, maximumBalance: 1000);
            AddCurrency(OffCurrency, enabled: false);

            AddRank(RankLeader, order: 100, isLeader: true,
                permissions: GuildPermission.Invite | GuildPermission.Kick
                    | GuildPermission.Promote | GuildPermission.Demote
                    | GuildPermission.TransferLeadership | GuildPermission.Disband
                    | GuildPermission.EditSettings);

            AddRank(RankOfficer, order: 50,
                permissions: GuildPermission.Invite | GuildPermission.Kick);

            AddRank(RankMember, order: 10, permissions: GuildPermission.None);

            AddItem(Potion, ItemCategory.Consumable, stackable: true, maxStack: 99);
            AddItem(Bound, ItemCategory.Misc, stackable: false, maxStack: 1, tradable: false);
            AddItem(Card, ItemCategory.Card, stackable: false, maxStack: 1);

            AddEquipment(Sword, EquipmentSlot.MainHand);
            AddEquipment(Helm, EquipmentSlot.Head);

            AddMap(TownMap, isBossArea: false);
            AddMap(BossMap, isBossArea: true);
        }

        [TearDown]
        public void TearDownSocialFixtures()
        {
            foreach (Object created in _created) Object.DestroyImmediate(created);
        }

        // ---- authoring -----------------------------------------------------------------

        protected SocialConfiguration AddConfiguration(int maxParty = MaxParty,
            int maxGuild = 0, int minPrice = 1, int maxPrice = 0, int maxListings = 0,
            int maxTradeItems = 0, PartyLootPolicy lootPolicy = PartyLootPolicy.Personal)
        {
            var definition = ScriptableObject.CreateInstance<SocialConfiguration>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"config.social\"},"
                + "\"_maxPartySize\":" + maxParty
                + ",\"_maxGuildMembers\":" + maxGuild
                + ",\"_minListingPrice\":" + minPrice
                + ",\"_maxListingPrice\":" + maxPrice
                + ",\"_maxShopListings\":" + maxListings
                + ",\"_maxTradeItems\":" + maxTradeItems
                + ",\"_minGuildNameLength\":3"
                + ",\"_maxGuildNameLength\":24"
                + ",\"_defaultLootPolicy\":" + (int)lootPolicy + "}", definition);

            Track(definition);
            return definition;
        }

        protected CurrencyDefinition AddCurrency(string id, long maximumBalance = 0,
            bool enabled = true, string backingItem = null)
        {
            var definition = ScriptableObject.CreateInstance<CurrencyDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_maximumBalance\":" + maximumBalance
                + ",\"_backingItem\":{\"_value\":\"" + (backingItem ?? string.Empty) + "\"}"
                + ",\"_disabled\":" + (enabled ? "false" : "true") + "}", definition);

            Track(definition);
            Currencies.Register(definition);
            return definition;
        }

        protected GuildRankDefinition AddRank(string id, int order, GuildPermission permissions,
            bool isLeader = false)
        {
            var definition = ScriptableObject.CreateInstance<GuildRankDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_order\":" + order
                + ",\"_permissions\":" + (int)permissions
                + ",\"_isLeaderRank\":" + (isLeader ? "true" : "false") + "}", definition);

            Track(definition);
            Ranks.Register(definition);
            return definition;
        }

        protected ItemDefinition AddItem(string id, ItemCategory category, bool stackable,
            int maxStack, bool tradable = true)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":" + (stackable ? "true" : "false")
                + ",\"_maxStackSize\":" + maxStack
                + ",\"_tradable\":" + (tradable ? "true" : "false")
                + ",\"_category\":" + (int)category + "}", definition);

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        protected EquipmentDefinition AddEquipment(string id, EquipmentSlot slot,
            bool tradable = true, int cardSlots = 1)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_stackable\":false,\"_maxStackSize\":1,"
                + "\"_category\":" + (int)ItemCategory.Equipment
                + ",\"_slot\":" + (int)slot
                + ",\"_cardSlots\":" + cardSlots
                + ",\"_tradable\":" + (tradable ? "true" : "false") + "}", definition);

            Track(definition);
            Items.Register(definition);
            return definition;
        }

        protected MapDefinition AddMap(string id, bool isBossArea)
        {
            var definition = ScriptableObject.CreateInstance<MapDefinition>();
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_category\":" + (int)(isBossArea ? MapCategory.BossArena : MapCategory.Town)
                + ",\"_isTown\":" + (isBossArea ? "false" : "true")
                + ",\"_isBossArea\":" + (isBossArea ? "true" : "false") + "}", definition);

            Track(definition);
            Maps.Register(definition);
            return definition;
        }

        // ---- convenience ---------------------------------------------------------------

        protected ItemContainerState Container(OwnerId owner, int capacity = 10)
        {
            return new ItemContainerState(owner, capacity);
        }

        protected ItemInstance Stack(string id, OwnerId owner, int quantity = 1)
        {
            return new ItemInstance(InstanceId.New(), new DefinitionId(id), owner, quantity);
        }

        protected EquipmentInstance Equipment(string id, OwnerId owner)
        {
            return new EquipmentInstance(InstanceId.New(), new DefinitionId(id), owner);
        }

        protected CharacterWalletState Wallet(OwnerId owner, CharacterId character = default,
            int gold = 0)
        {
            var wallet = new CharacterWalletState(owner, character);

            if (gold > 0)
            {
                EconomyService.TryCredit(wallet, new DefinitionId(Gold), gold,
                    EconomySource.SystemReward, RequestId.New(), Economy());
            }

            return wallet;
        }

        protected EconomyService.Context Economy(long ticks = 0L)
        {
            return new EconomyService.Context(Currencies, Ledger, ticks);
        }

        protected PartyService.Context PartyContext(long ticks = 0L)
        {
            return new PartyService.Context(Configuration, Parties, ticks);
        }

        protected GuildService.Context GuildContext(long ticks = 0L)
        {
            return new GuildService.Context(Ranks, Configuration, GuildNames, Guilds, ticks);
        }

        protected PlayerShopService.Context ShopContext(long ticks = 0L)
        {
            return new PlayerShopService.Context(Items, Currencies, Ledger, Maps, Configuration,
                ticks);
        }

        protected ItemTransferRules.Context Rules(CharacterEquipmentState equipment = null,
            CharacterDevilFruitState fruit = null,
            IReadOnlyList<EquipmentInstance> socketHolders = null)
        {
            return new ItemTransferRules.Context(Items, equipment, fruit, socketHolders);
        }

        protected TradeService.Participant Participant(CharacterId character, OwnerId owner,
            ItemContainerState inventory, CharacterWalletState wallet,
            CharacterEquipmentState equipment = null,
            CharacterDevilFruitState fruit = null,
            IReadOnlyList<EquipmentInstance> socketHolders = null)
        {
            return new TradeService.Participant(character, owner, inventory, wallet,
                Rules(equipment, fruit, socketHolders));
        }

        protected TradeService.Context TradeContext(TradeService.Participant a,
            TradeService.Participant b, long ticks = 0L)
        {
            return new TradeService.Context(a, b, Items, Currencies, Ledger, Configuration, ticks);
        }

        /// <summary>Builds a party and brings it to a given size, through the service.</summary>
        protected PartyState PartyOf(params CharacterId[] members)
        {
            PartyResult created = PartyService.TryCreate(members[0], PartyContext());
            PartyState party = created.Party;

            for (int i = 1; i < members.Length; i++)
            {
                PartyResult invited = PartyService.TryInvite(party, members[0], members[i],
                    PartyContext());

                PartyService.TryAccept(invited.Invite, party, members[i], PartyContext());
            }

            return party;
        }

        /// <summary>Builds a guild with a leader, through the service.</summary>
        protected GuildState GuildOf(CharacterId leader, string name = "Testers")
        {
            GuildResult created = GuildService.TryCreate(leader, name,
                new DefinitionId(RankLeader), GuildContext());

            return created.Guild;
        }

        /// <summary>Adds a member to a guild at a rank, through the service.</summary>
        protected void JoinGuild(GuildState guild, CharacterId inviter, CharacterId target,
            string rank)
        {
            GuildResult invited = GuildService.TryInvite(guild, inviter, target, GuildContext());

            GuildService.TryAccept(invited.Invite, guild, target, new DefinitionId(rank),
                GuildContext());
        }

        protected void Track(Object created)
        {
            _created.Add(created);
        }

        protected static void SetPrivate(Object target, string field, object value)
        {
            System.Type type = target.GetType();

            while (type != null)
            {
                System.Reflection.FieldInfo info = type.GetField(field,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);

                if (info != null)
                {
                    info.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new System.ArgumentException(
                "No field '" + field + "' on " + target.GetType().Name, "field");
        }

        /// <summary>A file's lines with the comments removed.</summary>
        /// <remarks>Prose may name a type while explaining why code does not; asserting over
        /// raw text would check the documentation instead of the implementation.</remarks>
        internal static IEnumerable<string> CodeLines(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;

                yield return code;
            }
        }
    }
}
