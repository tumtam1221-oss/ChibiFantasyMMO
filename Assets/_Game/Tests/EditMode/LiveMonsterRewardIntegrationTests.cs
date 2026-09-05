using System.Collections.Generic;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A monster's experience, from the authored definition to a row in MySQL and back.
    /// </summary>
    /// <remarks>
    /// <b>The reward is only real once it survives a reload.</b> Everything else about
    /// experience can be proven with a fake store, but not this: that the level and the
    /// within-level remainder the server computed are the ones a player finds when they log
    /// back in. That crosses PHP, an optimistic revision check and two MySQL columns, and a
    /// mock cannot tell you whether any of them agreed.
    ///
    /// It also proves the retry story end to end. Granting the same defeat again and saving
    /// again must leave the database exactly as it was -- a duplicated kill reward is the
    /// kind of bug players find before operators do.
    ///
    /// <b>How to run it.</b> Seed the fixture and serve the API:
    /// <code>
    ///   php backend/bin/integration-fixture.php
    ///   php -S 127.0.0.1:8099 -t backend/public
    /// </code>
    /// Without them every test here skips with a reason rather than failing.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveMonsterRewardIntegrationTests
    {
        private const string BaseAddress = "http://127.0.0.1:8099";

        private const string MaxHp = "stat.maxhp";
        private const string Monster = "monster.itest-reward";
        private const string DropTable = "drop.itest-reward";
        private const string Coin = "item.itest-coin";
        private const int Capacity = 12;
        private const int MaxLevel = 60;
        private const int LevelCost = 1000;

        private IntegrationFixture _fixture;
        private UnityWebRequestTransport _transport;
        private HttpAccountApi _api;
        private HttpCharacterStateStore _store;
        private readonly List<Object> _created = new List<Object>();

        /// <summary>Hands out the bearer the API already holds for this session.</summary>
        /// <remarks>In production this is <c>HttpWorldSessionAuthority</c>. Here the test is
        /// the thing that signed in, so it is the thing that holds the token -- and it is
        /// still the only copy.</remarks>
        private sealed class ApiToken : HttpCharacterStateStore.ITokenSource
        {
            private readonly HttpAccountApi _api;

            public ApiToken(HttpAccountApi api) => _api = api;

            public bool TryGetToken(SessionId session, out string token)
            {
                token = _api.SessionToken;

                return session == _api.Session && !string.IsNullOrEmpty(token);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _fixture = IntegrationFixture.Load();

            if (!_fixture.IsAvailable)
            {
                Assert.Ignore("no live backend fixture: " + _fixture.Reason);
            }

            _transport = new UnityWebRequestTransport(BaseAddress, 15);
            _api = new HttpAccountApi(_transport);

            HttpExchange health = _transport.Send("GET", "/api/health", null, null);

            if (!health.IsSuccess)
            {
                Assert.Ignore("no PHP server on " + BaseAddress + " ("
                    + health.FailureKind + ") -- start it with: "
                    + "php -S 127.0.0.1:8099 -t backend/public");
            }

            EnterWorld();

            _store = new HttpCharacterStateStore(_transport, new ApiToken(_api));
        }

        [TearDown]
        public void TearDown()
        {
            if (_api != null && !string.IsNullOrEmpty(_api.SessionToken))
            {
                _api.ReleaseSession(RequestId.New());
            }

            _transport?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        /// <summary>Signs in and walks the real state machine to in-world.</summary>
        private void EnterWorld()
        {
            _api.PendingLoginIdentifier = _fixture.RewardLoginIdentifier;
            _api.PendingPassword = _fixture.RewardPassword;

            ApiResult<AuthenticatedAccount> login = _api.Authenticate(new LoginRequest(
                RequestId.New(), new VersionSet(new VersionNumber(1, 0, 0),
                    new VersionNumber(1, 0, 0), new VersionNumber(1, 0, 0))));

            Assert.That(login.IsOk, Is.True, "login failed: " + login.Error);

            Assert.That(_api.SelectServer(RequestId.New(),
                new ServerId(_fixture.ServerId)).IsOk, Is.True);
            Assert.That(_api.SelectChannel(RequestId.New(),
                new ChannelId(_fixture.ChannelId)).IsOk, Is.True);
            Assert.That(_api.SelectCharacter(RequestId.New(),
                new CharacterId(RewardCharacter)).IsOk, Is.True);

            ApiResult<bool> entered = _api.NotifyWorldEntry(login.Value.Account, _api.Session,
                new CharacterId(RewardCharacter), new ServerId(_fixture.ServerId),
                new ChannelId(_fixture.ChannelId));

            Assert.That(entered.IsOk, Is.True, "enter-world failed: " + entered.Error);
        }

        // ---- the world this test builds around the real store -------------------------------

        /// <summary>
        /// The character this suite is allowed to change.
        /// </summary>
        /// <remarks>On its own account, not the fixture's main one. Granting experience
        /// moves a level and adds a row, and other suites assert both the seeded level and
        /// the character list of the main account.</remarks>
        private string RewardCharacter => _fixture.RewardCharacterId;

        private WorldCharacterRegistry NewRegistry()
        {
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(Spawn());

            return new WorldCharacterRegistry(_store, spawns, ItemRegistry(), Capacity);
        }

        /// <summary>The authored items this fixture can drop.</summary>
        private DefinitionRegistry<ItemDefinition> ItemRegistry()
        {
            var items = new DefinitionRegistry<ItemDefinition>();

            var sword = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"item.itest-sword\"},\"_stackable\":false,"
                + "\"_maxStackSize\":1,\"_slot\":" + (int)EquipmentSlot.MainHand
                + ",\"_levelRequirement\":1,\"_statusStoneSlots\":2}", sword);

            _created.Add(sword);
            items.Register(sword);

            var coin = ScriptableObject.CreateInstance<ItemDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + Coin + "\"},\"_stackable\":true,"
                + "\"_maxStackSize\":999}", coin);

            _created.Add(coin);
            items.Register(coin);

            return items;
        }

        /// <summary>A table that always drops one coin, so the roll is not what is on trial.</summary>
        private DefinitionRegistry<DropTableDefinition> DropTableRegistry()
        {
            var tables = new DefinitionRegistry<DropTableDefinition>();

            var table = ScriptableObject.CreateInstance<DropTableDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + DropTable + "\"},\"_maxEntries\":0}", table);

            SetPrivate(table, "_entries", new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
            });

            _created.Add(table);
            tables.Register(table);

            return tables;
        }

        private LivingCharacter Enter(WorldCharacterRegistry registry, int connectionId = 1)
        {
            WorldSpawnResult result = registry.Spawn(connectionId,
                WorldAdmission.Admitted(_api.Session,
                    new AccountId(_fixture.RewardAccountId),
                    new CharacterId(RewardCharacter),
                    new ServerId(_fixture.ServerId), new ChannelId(_fixture.ChannelId),
                    new DefinitionId(_fixture.MapId), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(result.IsSpawned, Is.True,
                "the real character did not load: " + result.Detail);

            return result.Character;
        }

        private MonsterRewardAuthority NewAuthority(WorldCharacterRegistry registry,
            out MonsterWorldRuntime runtime)
        {
            return NewAuthority(registry, out runtime, out _);
        }

        private MonsterRewardAuthority NewAuthority(WorldCharacterRegistry registry,
            out MonsterWorldRuntime runtime, out MonsterLootRegistry loot)
        {
            var monsters = new DefinitionRegistry<MonsterDefinition>();
            monsters.Register(MonsterDefinitionFor(Monster, experience: 250));

            runtime = new MonsterWorldRuntime(registry, monsters, new DefinitionId(MaxHp),
                new CombatTeam(2));

            DefinitionRegistry<ItemDefinition> items = ItemRegistry();

            loot = new MonsterLootRegistry(registry, items);

            return new MonsterRewardAuthority(runtime, registry, Curve(), loot, items,
                DropTableRegistry());
        }

        private LivingMonster Corpse(MonsterWorldRuntime runtime)
        {
            runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f,
                new DefinitionId(_fixture.MapId)));

            runtime.PopulateAll();

            LivingMonster monster = runtime.All()[0];

            monster.State.ApplyHealthDelta(-10000);

            return monster;
        }

        // ---- MySQL is the last word -------------------------------------------------------------

        [Test]
        public void ExperienceEarnedOnTheServerSurvivesAReload()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            int levelBefore = character.Domain.Progression.Level;
            long experienceBefore = character.Domain.Progression.Experience;

            MonsterRewardAuthority rewards = NewAuthority(registry,
                out MonsterWorldRuntime runtime);

            MonsterRewardResult granted = rewards.Grant(Corpse(runtime).Instance,
                character.Combatant.CombatantId);

            Assert.That(granted.IsGranted, Is.True, granted.ToString());
            Assert.That(granted.ExperienceGranted, Is.EqualTo(250));
            Assert.That(granted.IsPersisted, Is.True,
                "the real PHP save refused: " + granted.Reason);

            // Everything above happened in memory. This is the part only MySQL can answer.
            CharacterPersistenceResult reloaded = _store.Load(_api.Session);

            Assert.That(reloaded.IsOk, Is.True, "reload failed: " + reloaded.Detail);
            Assert.That(reloaded.Character.Level, Is.EqualTo(granted.LevelAfter));
            Assert.That(reloaded.Character.Experience, Is.EqualTo(granted.ExperienceAfter));

            // And it is the arithmetic the server did, not a coincidence.
            long expected = experienceBefore + 250;
            int expectedLevel = levelBefore;

            while (expectedLevel < MaxLevel && expected >= LevelCost)
            {
                expected -= LevelCost;
                expectedLevel++;
            }

            Assert.That(reloaded.Character.Level, Is.EqualTo(expectedLevel));
            Assert.That(reloaded.Character.Experience, Is.EqualTo(expected));
        }

        [Test]
        public void ARetriedRewardLeavesTheDatabaseExactlyAsItWas()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            MonsterRewardAuthority rewards = NewAuthority(registry,
                out MonsterWorldRuntime runtime);

            LivingMonster monster = Corpse(runtime);

            Assert.That(rewards.Grant(monster.Instance,
                character.Combatant.CombatantId).IsPersisted, Is.True);

            CharacterPersistenceResult afterFirst = _store.Load(_api.Session);

            Assert.That(afterFirst.IsOk, Is.True);

            // The same defeat, processed again -- a repeated packet, a retried tick, a
            // reconnect. It must not pay twice, and it must not write.
            MonsterRewardResult retry = rewards.Grant(monster.Instance,
                character.Combatant.CombatantId);

            Assert.That(retry.IsGranted, Is.False);
            Assert.That(retry.Reason,
                Is.EqualTo(MonsterRewardRejection.RewardAlreadyGranted));

            // Saving again is also safe: there is nothing new to write.
            Assert.That(registry.Save(character).IsOk, Is.True);

            CharacterPersistenceResult afterRetry = _store.Load(_api.Session);

            Assert.That(afterRetry.Character.Level,
                Is.EqualTo(afterFirst.Character.Level));
            Assert.That(afterRetry.Character.Experience,
                Is.EqualTo(afterFirst.Character.Experience),
                "a retry that added experience would be the bug this gate exists to prevent");
        }

        [Test]
        public void AReloadedCharacterCarriesTheEarnedProgressionIntoTheNextSession()
        {
            WorldCharacterRegistry first = NewRegistry();
            LivingCharacter character = Enter(first);

            MonsterRewardAuthority rewards = NewAuthority(first,
                out MonsterWorldRuntime runtime);

            MonsterRewardResult granted = rewards.Grant(Corpse(runtime).Instance,
                character.Combatant.CombatantId);

            Assert.That(granted.IsPersisted, Is.True);

            // A second world server, a second registry, the same database. This is what a
            // player logging back in actually gets.
            WorldCharacterRegistry second = NewRegistry();
            LivingCharacter returned = Enter(second, connectionId: 2);

            Assert.That(returned.Domain.Progression.Level, Is.EqualTo(granted.LevelAfter));
            Assert.That(returned.Domain.Progression.Experience,
                Is.EqualTo(granted.ExperienceAfter));
        }

        // ---- parties reach MySQL --------------------------------------------------------------------

        /// <summary>
        /// A party, through the real stack and back.
        /// </summary>
        /// <remarks>Unity -> HTTP -> PHP -> MySQL -> a new store -> back. The second read
        /// goes through a repository that has never seen the first, which is what makes it
        /// a persistence test rather than a memory one.</remarks>
        [Test]
        public void APartySurvivesARealRoundTripThroughPhpAndMySql()
        {
            var store = new HttpPartyStateStore(_transport, new ApiToken(_api));

            CharacterId me = new CharacterId(_fixture.RewardCharacterId);

            // Whatever ran before, this character starts in no party.
            store.Save(_api.Session, new PersistedParty(new PartyId("party-live-18-14"),
                me, PartyLootPolicy.Personal, new CharacterId[0], 0));

            PartyPersistenceResult empty = store.Load(_api.Session);

            Assert.That(empty.IsOk, Is.True, "live load failed: " + empty.Detail);
            Assert.That(empty.Party.Exists, Is.False, "the character already has a party");

            var party = new PersistedParty(new PartyId("party-live-18-14"), me,
                PartyLootPolicy.RoundRobin, new[] { me }, 0);

            PartyPersistenceResult saved = store.Save(_api.Session, party);

            Assert.That(saved.IsOk, Is.True, "live save failed: " + saved.Detail);

            // A brand new store over the same wire: nothing in this process remembers it.
            var reader = new HttpPartyStateStore(_transport, new ApiToken(_api));

            PartyPersistenceResult loaded = reader.Load(_api.Session);

            Assert.That(loaded.IsOk, Is.True, loaded.Detail);
            Assert.That(loaded.Party.Exists, Is.True, "the party did not survive MySQL");
            Assert.That(loaded.Party.Party.Value, Is.EqualTo("party-live-18-14"));
            Assert.That(loaded.Party.Leader, Is.EqualTo(me));
            Assert.That(loaded.Party.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin),
                "the loot policy did not round-trip");
            Assert.That(loaded.Party.Members, Is.EqualTo(new[] { me }));

            // And a fresh world restores it, which is the shape a server restart takes.
            var world = new WorldPartyRegistry();

            PartyState restored = world.Restore(_api.Session, me, reader);

            Assert.That(restored, Is.Not.Null, "a fresh world could not restore the party");
            Assert.That(restored.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin));

            // Two members restoring the same persisted party share one runtime object.
            Assert.That(world.Restore(_api.Session, me, reader), Is.SameAs(restored));
            Assert.That(world.Count, Is.EqualTo(1));

            // Disbanded, and gone for good.
            Assert.That(store.Save(_api.Session, new PersistedParty(restored.Id, me,
                PartyLootPolicy.Personal, new CharacterId[0], 0)).IsOk, Is.True);

            Assert.That(new HttpPartyStateStore(_transport, new ApiToken(_api))
                .Load(_api.Session).Party.Exists, Is.False,
                "a disbanded party came back from MySQL");
        }

        // ---- devil fruit reaches MySQL ------------------------------------------------------------

        /// <summary>
        /// A Devil Fruit, through the real stack and back.
        /// </summary>
        /// <remarks>
        /// <b>This closes what 18.11A could only prove once.</b> That gate stood the chain up
        /// by hand, watched a fruit survive MySQL, and threw the fixture away -- so the
        /// evidence was real and not repeatable. Here it lives with the other live tests and
        /// runs whenever they do.
        ///
        /// <b>Only the stable id crosses.</b> What the fruit does is authored content; what
        /// is stored is which one it was.
        /// </remarks>
        [Test]
        public void ADevilFruitSurvivesARealRoundTripThroughPhpAndMySql()
        {
            const string Darkness = "devil_fruit.darkness";

            CharacterPersistenceResult loaded = _store.Load(_api.Session);

            Assert.That(loaded.IsOk, Is.True, "live load failed: " + loaded.Detail);

            PersistedCharacter before = loaded.Character;

            Assert.That(before.DevilFruit.IsValid, Is.False,
                "the fixture character already owns a fruit");

            PersistedCharacter owning = WithFruit(before, new DefinitionId(Darkness),
                "inst-live-18-12");

            CharacterPersistenceResult saved = _store.Save(_api.Session, owning,
                before.SaveRevision);

            Assert.That(saved.IsOk, Is.True, "live save failed: " + saved.Detail);

            // Read back over the wire. Nothing in this process remembers it.
            PersistedCharacter after = _store.Load(_api.Session).Character;

            Assert.That(after.DevilFruit.Value, Is.EqualTo(Darkness),
                "the fruit did not survive MySQL");
            Assert.That(after.DevilFruitSource, Is.EqualTo("inst-live-18-12"));

            // And the rest of the character came back untouched.
            Assert.That(after.CurrentHealth, Is.EqualTo(before.CurrentHealth));
            Assert.That(after.CurrentMana, Is.EqualTo(before.CurrentMana));
            Assert.That(after.Level, Is.EqualTo(before.Level));
            Assert.That(after.Items.Count, Is.EqualTo(before.Items.Count),
                "equipment or inventory changed");

            // Put it back, so this test can run again tomorrow.
            CharacterPersistenceResult restored = _store.Save(_api.Session,
                WithFruit(after, default, null), after.SaveRevision);

            Assert.That(restored.IsOk, Is.True, restored.Detail);

            Assert.That(_store.Load(_api.Session).Character.DevilFruit.IsValid, Is.False,
                "clearing the fruit did not reach MySQL");
        }

        /// <summary>The same character, owning a different fruit. Everything else is theirs.</summary>
        private static PersistedCharacter WithFruit(PersistedCharacter source,
            DefinitionId fruit, string instance)
        {
            return new PersistedCharacter(source.Character, source.Account, source.Server,
                source.Name, source.Gender, source.Level, source.Experience,
                source.CurrentHealth, source.CurrentMana, source.Class, source.Job,
                source.Map, source.Spawn, source.Stats, source.Appearance, source.Skills,
                source.SaveRevision, source.Items, source.InventoryCapacity, fruit, instance);
        }

        // ---- loot reaches MySQL exactly once -----------------------------------------------------

        [Test]
        public void APickedUpItemSurvivesAReloadExactlyOnce()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            int before = CoinsIn(character);

            MonsterRewardAuthority rewards = NewAuthority(registry,
                out MonsterWorldRuntime runtime, out MonsterLootRegistry loot);

            MonsterRewardResult granted = rewards.Grant(Corpse(runtime).Instance,
                character.Combatant.CombatantId);

            Assert.That(granted.HasLoot, Is.True, "nothing dropped: " + granted);

            LootPickupOutcome taken = loot.Pickup(granted.LootPile, 0,
                new CharacterId(RewardCharacter));

            Assert.That(taken.IsAccepted, Is.True, taken.ToString());
            Assert.That(taken.IsPersisted, Is.True,
                "the real PHP save refused: " + taken.PersistenceFailure);

            // Only MySQL can answer this part.
            CharacterPersistenceResult reloaded = _store.Load(_api.Session);

            Assert.That(reloaded.IsOk, Is.True, reloaded.Detail);
            Assert.That(CoinsIn(reloaded.Character), Is.EqualTo(before + 1),
                "one coin, in the database, after a real round trip");

            // And the item kept its own identity rather than being minted again.
            Assert.That(FindCoin(reloaded.Character).Instance.Value,
                Is.EqualTo(FindCoin(character).Instance.Value));
        }

        [Test]
        public void ARetriedPickupLeavesTheDatabaseExactlyAsItWas()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            MonsterRewardAuthority rewards = NewAuthority(registry,
                out MonsterWorldRuntime runtime, out MonsterLootRegistry loot);

            MonsterRewardResult granted = rewards.Grant(Corpse(runtime).Instance,
                character.Combatant.CombatantId);

            Assert.That(loot.Pickup(granted.LootPile, 0,
                new CharacterId(RewardCharacter)).IsPersisted, Is.True);

            CharacterPersistenceResult afterFirst = _store.Load(_api.Session);

            // The same request again -- a repeated packet, a retried tick, a reconnect.
            LootPickupOutcome retry = loot.Pickup(granted.LootPile, 0,
                new CharacterId(RewardCharacter));

            Assert.That(retry.IsAccepted, Is.False);
            Assert.That(registry.Save(character).IsOk, Is.True);

            CharacterPersistenceResult afterRetry = _store.Load(_api.Session);

            Assert.That(CoinsIn(afterRetry.Character),
                Is.EqualTo(CoinsIn(afterFirst.Character)),
                "a retry that added an item would be the duplication bug this gate exists "
                + "to prevent");
        }

        [Test]
        public void ABagArrangedInOneSessionComesBackTheSameWayInTheNext()
        {
            WorldCharacterRegistry first = NewRegistry();
            LivingCharacter character = Enter(first);

            MonsterRewardAuthority rewards = NewAuthority(first,
                out MonsterWorldRuntime runtime, out MonsterLootRegistry loot);

            MonsterRewardResult granted = rewards.Grant(Corpse(runtime).Instance,
                character.Combatant.CombatantId);

            loot.Pickup(granted.LootPile, 0, new CharacterId(RewardCharacter));

            int slot = SlotOfCoin(character);
            int coins = CoinsIn(character);

            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "precondition: a coin is in the bag");

            // A second world server, the same database. This is what a player logging back
            // in actually gets.
            WorldCharacterRegistry second = NewRegistry();
            LivingCharacter returned = Enter(second, connectionId: 2);

            Assert.That(returned.Inventory, Is.Not.Null);
            Assert.That(CoinsIn(returned), Is.EqualTo(coins));
            Assert.That(SlotOfCoin(returned), Is.EqualTo(slot),
                "the bag arrives arranged as it was left");
            Assert.That(returned.Inventory.Capacity, Is.EqualTo(Capacity));
        }

        private static int CoinsIn(LivingCharacter character)
        {
            return character.Inventory == null
                ? 0
                : character.Inventory.CountOf(new DefinitionId(Coin));
        }

        private static int CoinsIn(PersistedCharacter row)
        {
            var total = 0;

            for (int i = 0; i < row.Items.Count; i++)
            {
                if (row.Items[i].Item.Value == Coin) total += row.Items[i].Quantity;
            }

            return total;
        }

        private static PersistedItem FindCoin(PersistedCharacter row)
        {
            for (int i = 0; i < row.Items.Count; i++)
            {
                if (row.Items[i].Item.Value == Coin) return row.Items[i];
            }

            Assert.Fail("no coin in the persisted bag");

            return default;
        }

        private static PersistedItem FindCoin(LivingCharacter character)
        {
            IReadOnlyList<ItemSlot> slots = character.Inventory.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Content != null && slots[i].DefinitionId.Value == Coin)
                {
                    return new PersistedItem(slots[i].InstanceId, slots[i].DefinitionId, 1,
                        slots[i].Index);
                }
            }

            Assert.Fail("no coin in the live bag");

            return default;
        }

        private static int SlotOfCoin(LivingCharacter character)
        {
            IReadOnlyList<ItemSlot> slots = character.Inventory.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Content != null && slots[i].DefinitionId.Value == Coin)
                {
                    return slots[i].Index;
                }
            }

            return -1;
        }

        // ---- 18.4: equipment through real PHP and real MySQL ---------------------------------

        [Test]
        public void AWornPieceAndItsUpgradesSurviveARealRoundTrip()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            Assert.That(character.Inventory, Is.Not.Null);
            Assert.That(character.Equipment, Is.Not.Null,
                "a world with items gives a character somewhere to wear them");

            // A +8 epic sword with a stone in it, worn. Every one of those is a per-copy
            // fact that no definition can supply, and a load that dropped any of them would
            // strip an upgrade a player paid for.
            var sword = new EquipmentInstance(new InstanceId("itest-sword"),
                new DefinitionId("item.itest-sword"), character.Owner);

            sword.SetEnhancementLevel(8);
            sword.SetRarity(new DefinitionId("rarity.epic"));
            sword.AddEnchant(new EquipmentEnchant(new DefinitionId("stone.fire"), 0, 3));

            // A previous run may have left this character wearing it -- which is the whole
            // point of the test -- so the slot is emptied first rather than assumed free.
            if (character.Equipment.TryGet(EquipmentSlot.MainHand, out EquipmentInstance _))
            {
                EquipmentService.Unequip(character.Inventory, character.Equipment,
                    EquipmentSlot.MainHand,
                    new EquipmentService.Context(ItemRegistry(),
                        character.Domain.Progression.Level));
            }

            Assert.That(character.Equipment.Restore(EquipmentSlot.MainHand, sword), Is.True);

            character.MarkDirty();

            CharacterPersistenceResult saved = registry.Save(character);

            Assert.That(saved.IsOk, Is.True, "the real PHP save refused: " + saved.Detail);

            // Only MySQL can answer this part.
            CharacterPersistenceResult reloaded = _store.Load(_api.Session);

            Assert.That(reloaded.IsOk, Is.True, reloaded.Detail);

            PersistedItem row = default;

            for (int i = 0; i < reloaded.Character.Items.Count; i++)
            {
                if (reloaded.Character.Items[i].Instance.Value == "itest-sword")
                {
                    row = reloaded.Character.Items[i];
                }
            }

            Assert.That(row.Instance.Value, Is.EqualTo("itest-sword"),
                "the sword came back with the identity it always had");
            Assert.That(row.IsEquipped, Is.True, "and still worn");
            Assert.That(row.EquipmentSlot, Is.EqualTo((int)EquipmentSlot.MainHand));
            Assert.That(row.EnhancementLevel, Is.EqualTo(8));
            Assert.That(row.Rarity.Value, Is.EqualTo("rarity.epic"));
            Assert.That(row.Enchants, Has.Count.EqualTo(1));
            Assert.That(row.Enchants[0].Stone.Value, Is.EqualTo("stone.fire"));
            Assert.That(row.Enchants[0].Rank, Is.EqualTo(3));

            // And a second world server reading the same database wears it too.
            WorldCharacterRegistry second = NewRegistry();
            LivingCharacter returned = Enter(second, connectionId: 3);

            Assert.That(returned.Equipment.TryGet(EquipmentSlot.MainHand,
                out EquipmentInstance worn), Is.True);
            Assert.That(worn.EnhancementLevel, Is.EqualTo(8));
            Assert.That(worn.Rarity.Value, Is.EqualTo("rarity.epic"));
            Assert.That(worn.EnchantCount, Is.EqualTo(1));
        }

        // ---- fixtures ------------------------------------------------------------------------------

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            var costs = new System.Text.StringBuilder();

            for (int level = 1; level < MaxLevel; level++)
            {
                if (level > 1) costs.Append(',');

                costs.Append(LevelCost);
            }

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.itest\"},\"_minLevel\":1,\"_maxLevel\":"
                + MaxLevel + ",\"_experienceToNextLevel\":[" + costs + "]}", definition);

            _created.Add(definition);

            return definition;
        }

        private SpawnPointDefinition Spawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.itest\"},\"_map\":{\"_value\":\""
                + _fixture.MapId + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private MonsterDefinition MonsterDefinitionFor(string id, int experience)
        {
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_level\":5,\"_aggressionType\":2,"
                + "\"_experienceReward\":" + experience + ",\"_attackRange\":2,"
                + "\"_lootTable\":{\"_value\":\"" + DropTable + "\"},"
                + "\"_respawn\":{\"_respawnDelaySeconds\":0,\"_maxAliveInArea\":1}}",
                definition);

            SetPrivate(definition, "_baseStats",
                new[] { new StatValue(new DefinitionId(MaxHp), 100f) });

            _created.Add(definition);

            return definition;
        }

        /// <summary>Sets a private serialized field, including one on a base type.</summary>
        private static void SetPrivate(Object target, string field, object value)
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

            Assert.Fail("no field '" + field + "' on " + target.GetType().Name);
        }
    }
}
