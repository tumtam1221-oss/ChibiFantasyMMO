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
                + ",\"_levelRequirement\":1,\"_statusStoneSlots\":2,\"_cardSlots\":1}",
                sword);

            _created.Add(sword);
            items.Register(sword);

            // A card to put in it. Its own item, because the socket row in MySQL has a
            // foreign key to the copy that was consumed.
            var card = ScriptableObject.CreateInstance<ItemDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"item.itest-card\"},\"_category\":5,"
                + "\"_stackable\":false,\"_maxStackSize\":1}", card);

            _created.Add(card);
            items.Register(card);

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

        /// <summary>
        /// Whose turn it is, through the real stack and back.
        /// </summary>
        /// <remarks>The cursor is the half of a party that used to be left behind: the
        /// members reached MySQL in order and the position in that order did not, so every
        /// restart handed the next drop back to the first member. This proves the column
        /// exists, that PHP carries it both ways, and that MySQL refuses one that addresses
        /// nobody.</remarks>
        [Test]
        public void ARoundRobinTurnSurvivesARealRoundTripThroughPhpAndMySql()
        {
            var store = new HttpPartyStateStore(_transport, new ApiToken(_api));

            var me = new CharacterId(_fixture.RewardCharacterId);
            var mate = new CharacterId("char-live-18-14a-mate");
            var id = new PartyId("party-live-18-14a");

            // Whatever ran before, this character starts in no party.
            store.Save(_api.Session, new PersistedParty(id, me, PartyLootPolicy.Personal,
                new CharacterId[0], 0));

            // Two members, and it is the second one's turn.
            PartyPersistenceResult saved = store.Save(_api.Session, new PersistedParty(
                id, me, PartyLootPolicy.RoundRobin, new[] { me, mate }, 0, 1));

            Assert.That(saved.IsOk, Is.True, "live save failed: " + saved.Detail);

            // A brand new store and a brand new world: nothing in this process remembers
            // the number, so anything read back came out of MySQL.
            var reader = new HttpPartyStateStore(_transport, new ApiToken(_api));
            var world = new WorldPartyRegistry();

            PartyState restored = world.Restore(_api.Session, me, reader);

            Assert.That(restored, Is.Not.Null, "a fresh world could not restore the party");

            Assert.That(world.RotationOf(restored.Id), Is.EqualTo(1),
                "the loot turn did not survive MySQL");

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored,
                    world.RotationOf(restored.Id)), Is.EqualTo(mate),
                "the restored turn names the wrong member");

            // A turn past the end of the party is refused by the database rather than
            // wrapped, and reported as a malformed request rather than a lost race --
            // a world told it lost a race would re-send this forever.
            PartyPersistenceResult refused = store.Save(_api.Session, new PersistedParty(
                id, me, PartyLootPolicy.RoundRobin, new[] { me, mate }, 0, 5));

            Assert.That(refused.IsOk, Is.False, "MySQL stored a turn addressing nobody");
            Assert.That(refused.Failure,
                Is.EqualTo(PartyPersistenceFailure.InvalidParty));

            Assert.That(new HttpPartyStateStore(_transport, new ApiToken(_api))
                    .Load(_api.Session).Party.Cursor, Is.EqualTo(1),
                "a refused write moved the stored turn anyway");

            // A policy nobody authored is refused by the same route.
            Assert.That(store.Save(_api.Session, new PersistedParty(id, me,
                (PartyLootPolicy)99, new[] { me, mate }, 0, 0)).IsOk, Is.False,
                "MySQL stored a loot policy nobody authored");

            // Disbanded, so the next run of this fixture starts clean.
            Assert.That(store.Save(_api.Session, new PersistedParty(id, me,
                PartyLootPolicy.Personal, new CharacterId[0], 0)).IsOk, Is.True);
        }

        /// <summary>A real transport that can be taken away and given back.</summary>
        /// <remarks>Only the wire is broken. PHP, MySQL and every line of the store above it
        /// are the production ones, so what this proves about ordering is a fact about the
        /// real chain rather than about a stub.</remarks>
        private sealed class InterruptibleTransport : IHttpTransport
        {
            private readonly IHttpTransport _inner;

            public InterruptibleTransport(IHttpTransport inner) => _inner = inner;

            public bool Broken { get; set; }

            public HttpExchange Send(string method, string path, string jsonBody,
                string bearerToken)
            {
                return Broken
                    ? HttpExchange.Unreachable("the network is down for this test")
                    : _inner.Send(method, path, jsonBody, bearerToken);
            }
        }

        /// <summary>
        /// A turn that would not reach MySQL is a turn nobody spent.
        /// </summary>
        /// <remarks>
        /// The defect 18.14A left behind, proved closed against the real database: the
        /// runtime cursor used to move first and log the failure afterwards, so a restart
        /// offered the same member a turn they had already taken.
        /// </remarks>
        [Test]
        public void ATurnThatCannotReachMySqlIsNotSpentByTheRunningWorld()
        {
            var wire = new InterruptibleTransport(_transport);
            var store = new HttpPartyStateStore(wire, new ApiToken(_api));

            var me = new CharacterId(_fixture.RewardCharacterId);
            var second = new CharacterId("char-live-18-14b-second");
            var third = new CharacterId("char-live-18-14b-third");
            var id = new PartyId("party-live-18-14b");

            CharacterId[] members = { me, second, third };

            // Whatever ran before, this character starts in no party.
            store.Save(_api.Session, new PersistedParty(id, me, PartyLootPolicy.Personal,
                new CharacterId[0], 0));

            Assert.That(store.Save(_api.Session, new PersistedParty(id, me,
                PartyLootPolicy.RoundRobin, members, 0, 0)).IsOk, Is.True);

            var world = new WorldPartyRegistry();

            Assert.That(world.Restore(_api.Session, me, store), Is.Not.Null);
            Assert.That(world.RotationOf(id), Is.Zero);

            // 1. A turn that commits. MySQL and memory move together.
            Assert.That(world.TryCommitNextRotation(id).IsOk, Is.True);
            Assert.That(world.RotationOf(id), Is.EqualTo(1));
            Assert.That(Stored(me).Cursor, Is.EqualTo(1),
                "the committed turn did not reach MySQL");

            // 2. A turn that cannot be written. Nothing moves.
            wire.Broken = true;

            PartyPersistenceResult refused = world.TryCommitNextRotation(id);

            Assert.That(refused.IsOk, Is.False);
            Assert.That(refused.Failure, Is.EqualTo(PartyPersistenceFailure.Unreachable));

            Assert.That(world.RotationOf(id), Is.EqualTo(1),
                "the running world spent a turn it could not write down");

            wire.Broken = false;

            Assert.That(Stored(me).Cursor, Is.EqualTo(1),
                "MySQL moved despite the refusal");

            // 3. A fresh world, as a restart is. The same member is still next.
            var restarted = new WorldPartyRegistry();

            PartyState restored = restarted.Restore(_api.Session, me, store);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restarted.RotationOf(id), Is.EqualTo(1),
                "the restarted world disagreed with MySQL about whose turn it is");

            Assert.That(PartyLootPolicyService.MemberOnTurn(restored,
                restarted.RotationOf(id)), Is.EqualTo(second),
                "the member who never got their turn lost it to a failed write");

            // 4. With the wire back, that same turn commits and moves on exactly one.
            Assert.That(restarted.TryCommitNextRotation(id).IsOk, Is.True);
            Assert.That(restarted.RotationOf(id), Is.EqualTo(2));
            Assert.That(Stored(me).Cursor, Is.EqualTo(2),
                "the recovered turn did not reach MySQL");

            // Disbanded, so the next run of this fixture starts clean.
            Assert.That(store.Save(_api.Session, new PersistedParty(id, me,
                PartyLootPolicy.Personal, new CharacterId[0], 0)).IsOk, Is.True);
        }

        /// <summary>What MySQL holds right now, read through a store that remembers nothing.</summary>
        private PersistedParty Stored(CharacterId member)
        {
            PartyPersistenceResult read =
                new HttpPartyStateStore(_transport, new ApiToken(_api)).Load(_api.Session);

            Assert.That(read.IsOk, Is.True, "live read failed: " + read.Detail);

            return read.Party;
        }

        /// <summary>
        /// A decided defeat, through the real stack and back, and finished exactly once.
        /// </summary>
        /// <remarks>
        /// Unity -> HTTP -> PHP -> MySQL -> a new store -> back. The decision that goes in is
        /// the one that comes out: the same defeat, the same claimant, the same item, and the
        /// same one in ten million roll that was already spent. Nothing here re-decides
        /// anything, which is the entire reason the row exists.
        /// </remarks>
        [Test]
        public void ADecidedDefeatSurvivesARealRoundTripThroughPhpAndMySql()
        {
            var outbox = new HttpMonsterRewardOutbox(_transport, new ApiToken(_api));

            var me = new CharacterId(_fixture.RewardCharacterId);
            var defeat = new InstanceId("defeat-live-18-15-" + RequestId.New().Value);
            var loot = new InstanceId("loot-live-18-15-" + RequestId.New().Value);
            string rewardId = "reward-live-18-15-" + RequestId.New().Value;

            var decided = new PersistedMonsterReward(rewardId, defeat,
                new DefinitionId("monster.ancient_slime_king"),
                new DefinitionId("map.harbor_town"), me,
                loot, 1, me, 1.5f, 2.25f, -3.75f,
                new PartyId("party-live-18-15"), 1, true,
                new[] { new MonsterRewardGrant(me, 450) },
                new[]
                {
                    new MonsterRewardLootEntry(0,
                        new DefinitionId("item.devil_fruit.darkness"), 1, default, false,
                        default, new InstanceId("item-" + rewardId)),
                });

            // A: the decision reaches MySQL.
            MonsterRewardOutboxResult recorded = outbox.Record(_api.Session, decided);

            Assert.That(recorded.IsOk, Is.True, "live record failed: " + recorded.Detail);
            Assert.That(recorded.WasAlreadyRecorded, Is.False);

            // The same defeat again, as a world that never heard the answer would ask.
            MonsterRewardOutboxResult twice = outbox.Record(_api.Session,
                new PersistedMonsterReward("reward-live-18-15-second", defeat,
                    decided.Monster, decided.Map, me, loot, 1, me, 0f, 0f, 0f,
                    decided.Party, 1, true, decided.Experience, decided.Entries));

            Assert.That(twice.IsOk, Is.True);
            Assert.That(twice.WasAlreadyRecorded, Is.True,
                "one defeat produced a second reward in MySQL");
            Assert.That(twice.RewardId, Is.EqualTo(recorded.RewardId));

            // B: a brand new store, as a restarted world would use.
            var reader = new HttpMonsterRewardOutbox(_transport, new ApiToken(_api));

            PersistedMonsterReward restored = Pending(reader, defeat);

            Assert.That(restored.Exists, Is.True, "the decision did not survive MySQL");
            Assert.That(restored.Killer, Is.EqualTo(me));
            Assert.That(restored.Claimant, Is.EqualTo(me),
                "the decided claimant did not come back");
            Assert.That(restored.Loot, Is.EqualTo(loot),
                "the pile lost the identity it was decided with");
            Assert.That(restored.HasCursor, Is.True);
            Assert.That(restored.Cursor, Is.EqualTo(1),
                "the turn this defeat must land on was forgotten");
            Assert.That(restored.X, Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(restored.Z, Is.EqualTo(-3.75f).Within(0.01f),
                "a fractional coordinate was rounded on the way through PHP");

            // E: the item is the one that was rolled, named by its authored id.
            Assert.That(restored.Entries.Count, Is.EqualTo(1));
            Assert.That(restored.Entries[0].Item.Value,
                Is.EqualTo("item.devil_fruit.darkness"),
                "the decided drop came back as a different item");
            Assert.That(restored.Entries[0].IsClaimed, Is.False);

            // C: half delivered, then read back by yet another store.
            MonsterRewardOutboxResult half = reader.Progress(_api.Session,
                restored.RewardId, restored.Revision, new[] { me }, null, true, true,
                false);

            Assert.That(half.IsOk, Is.True, "live progress failed: " + half.Detail);

            PersistedMonsterReward midway = Pending(
                new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)), defeat);

            Assert.That(midway.Exists, Is.True, "a half-delivered reward stopped being owed");
            Assert.That(midway.Experience[0].IsDelivered, Is.True,
                "the payment that landed was not remembered");
            Assert.That(midway.IsCursorCommitted, Is.True);
            Assert.That(midway.Entries[0].IsClaimed, Is.False,
                "an item nobody took was marked as taken");

            // A stale attempt, as a second recovery would make. It must change nothing.
            MonsterRewardOutboxResult stale = reader.Progress(_api.Session,
                restored.RewardId, restored.Revision, new[] { me }, null, null, null, true);

            Assert.That(stale.IsOk, Is.False, "two recoveries both believed they were first");
            Assert.That(stale.Failure,
                Is.EqualTo(MonsterRewardOutboxFailure.StaleRevision));

            // D: finished, and no longer owed.
            MonsterRewardOutboxResult done = reader.Progress(_api.Session,
                midway.RewardId, midway.Revision, null,
                new[]
                {
                    new MonsterRewardLootEntry(0, midway.Entries[0].Item, 1, default,
                        true, me),
                }, null, null, true);

            Assert.That(done.IsOk, Is.True, "live completion failed: " + done.Detail);

            Assert.That(Pending(new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)),
                    defeat).Exists, Is.False,
                "a completed reward is still being handed out as pending");
        }

        /// <summary>One pending reward for a defeat, or nothing when it is no longer owed.</summary>
        private PersistedMonsterReward Pending(HttpMonsterRewardOutbox outbox,
            InstanceId defeat)
        {
            foreach (PersistedMonsterReward reward in outbox.Pending(_api.Session))
            {
                if (reward.Defeat == defeat) return reward;
            }

            return default;
        }

        /// <summary>
        /// An item already carried, reconciled against MySQL rather than handed out twice.
        /// </summary>
        /// <remarks>
        /// The crash window this gate closes, proved against the real database: the bag is
        /// durable, the delivery stamp is not, and a fresh world has to work out that the
        /// entry is already delivered rather than putting it back on the ground.
        /// </remarks>
        [Test]
        public void AnItemAlreadyOwnedIsReconciledInMySqlRatherThanDeliveredTwice()
        {
            var outbox = new HttpMonsterRewardOutbox(_transport, new ApiToken(_api));

            var me = new CharacterId(_fixture.RewardCharacterId);
            string tag = RequestId.New().Value;

            var defeat = new InstanceId("defeat-18-15a-" + tag);
            var loot = new InstanceId("loot-18-15a-" + tag);
            var item = new InstanceId("item-18-15a-" + tag);
            string rewardId = "reward-18-15a-" + tag;

            var decided = new PersistedMonsterReward(rewardId, defeat,
                new DefinitionId("monster.ancient_slime_king"),
                new DefinitionId("map.harbor_town"), me,
                loot, 3, me, 0f, 0f, 0f, default, 0, false,
                new[] { new MonsterRewardGrant(me, 450) },
                new[]
                {
                    new MonsterRewardLootEntry(0,
                        new DefinitionId("item.devil_fruit.darkness"), 1, default, false,
                        default, item),
                });

            Assert.That(outbox.Record(_api.Session, decided).IsOk, Is.True,
                "the decision did not reach MySQL");

            // A: the identity is in the database, and nothing has been delivered.
            PersistedMonsterReward stored = Pending(
                new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)), defeat);

            Assert.That(stored.Exists, Is.True);
            Assert.That(stored.Entries[0].Instance, Is.EqualTo(item),
                "the decided item identity did not survive PHP and MySQL");
            Assert.That(stored.Entries[0].IsClaimed, Is.False,
                "an undelivered entry came back stamped");

            // A second recording of the same defeat -- the retry a world makes when it
            // never heard the first answer -- must not mint a second identity.
            MonsterRewardOutboxResult again = outbox.Record(_api.Session, decided);

            Assert.That(again.IsOk, Is.True, again.Detail);
            Assert.That(again.WasAlreadyRecorded, Is.True);

            Assert.That(Pending(new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)),
                    defeat).Entries[0].Instance, Is.EqualTo(item),
                "the retry changed the identity the item will be delivered as");

            // B and C: the world reconciles -- the bag already holds it -- and stamps the
            // delivery through a store that has never seen this reward before.
            var reader = new HttpMonsterRewardOutbox(_transport, new ApiToken(_api));

            MonsterRewardOutboxResult reconciled = reader.Progress(_api.Session,
                stored.RewardId, stored.Revision, null,
                new[]
                {
                    new MonsterRewardLootEntry(0, stored.Entries[0].Item, 1, default, true,
                        me, item),
                }, null, true, false);

            Assert.That(reconciled.IsOk, Is.True, reconciled.Detail);

            PersistedMonsterReward after = Pending(
                new HttpMonsterRewardOutbox(_transport, new ApiToken(_api)), defeat);

            Assert.That(after.Entries[0].IsClaimed, Is.True,
                "the reconciliation did not reach MySQL");
            Assert.That(after.Entries[0].ClaimedBy, Is.EqualTo(me));
            Assert.That(after.Entries[0].Instance, Is.EqualTo(item),
                "stamping the delivery changed the item's identity");

            // And nothing anywhere claims a second item with that identity.
            var conflicting = new PersistedMonsterReward("reward-18-15a-clash-" + tag,
                new InstanceId("defeat-18-15a-clash-" + tag), decided.Monster, decided.Map,
                me, new InstanceId("loot-clash-" + tag), 3, me, 0f, 0f, 0f, default, 0,
                false, new[] { new MonsterRewardGrant(me, 1) },
                new[]
                {
                    new MonsterRewardLootEntry(0, decided.Entries[0].Item, 1, default,
                        false, default, item),
                });

            Assert.That(outbox.Record(_api.Session, conflicting).IsOk, Is.False,
                "MySQL let two rewards become the same item");

            // Finished, so the fixture leaves nothing owed behind.
            Assert.That(reader.Progress(_api.Session, after.RewardId, after.Revision,
                new[] { me }, null, null, null, true).IsOk, Is.True);
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

        /// <summary>
        /// A card put into a sword, taken back out, and MySQL agreeing both times.
        /// </summary>
        /// <remarks>
        /// The gap Phase 18.16 reported: a socket row that outlived the card. It turned out
        /// to be a test that asked for a first save twice rather than a persistence fault,
        /// and this is the proof against the real database -- row present after socketing,
        /// row gone after removal, and a second world server reading neither back.
        /// </remarks>
        [Test]
        public void ACardSocketedThenRemovedLeavesNothingBehindInMySql()
        {
            WorldCharacterRegistry registry = NewRegistry();
            LivingCharacter character = Enter(registry);

            Assert.That(character.Inventory, Is.Not.Null);

            var sword = new EquipmentInstance(new InstanceId("itest-card-sword"),
                new DefinitionId("item.itest-sword"), character.Owner);

            var cardInstance = new InstanceId("itest-card-instance");

            // This fixture shares one live character with every other test in the file,
            // and a full-suite run leaves that bag full of other fixtures' belongings. The
            // bag is emptied rather than hoped about: every test here establishes what it
            // needs, so starting from nothing is the only state that is the same whether
            // this runs alone or thirteenth.
            EmptyTheBag(character);

            // The card is a real owned item, because the socket row has a foreign key to it.
            Assert.That(character.Inventory.Add(new ItemInstance(cardInstance,
                new DefinitionId("item.itest-card"), character.Owner, 1),
                ItemRegistry()).IsAccepted, Is.True, "the card would not go into the bag");

            sword.AddCard(new EquipmentCardSocket(new DefinitionId("card.itest"), 0,
                cardInstance));

            Assert.That(character.Inventory.Add(sword, ItemRegistry()).IsAccepted, Is.True,
                "the sword would not go into the bag");

            character.MarkDirty();

            Assert.That(registry.Save(character).IsOk, Is.True,
                "the real PHP save refused the socketed sword");

            // A: the row is there, read back through PHP from MySQL.
            PersistedItem stored = RowFor(_store.Load(_api.Session), "itest-card-sword");

            Assert.That(stored.Cards, Has.Count.EqualTo(1),
                "the socket did not reach MySQL");
            Assert.That(stored.Cards[0].Card.Value, Is.EqualTo("card.itest"));
            Assert.That(stored.Cards[0].CardInstance, Is.EqualTo(cardInstance),
                "the exact card that was consumed was not recorded");

            // B: removed, and saved again through the ordinary lifecycle.
            //
            // Taken from the bag rather than from the local variable: the container is what
            // the save reads, and a piece that went in is not necessarily the same object
            // that comes back out of a slot.
            EquipmentInstance inBag = null;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var held = character.Inventory.GetSlot(i).Content as EquipmentInstance;

                if (held != null && held.InstanceId.Value == "itest-card-sword")
                {
                    inBag = held;
                }
            }

            Assert.That(inBag, Is.Not.Null, "the sword is not in the bag that gets saved");

            Assert.That(inBag.RemoveCardAt(0, out EquipmentCardSocket _), Is.True);

            character.MarkDirty();

            Assert.That(registry.Save(character).IsOk, Is.True,
                "the real PHP save refused the emptied sword");

            // C: the row is gone.
            PersistedItem after = RowFor(_store.Load(_api.Session), "itest-card-sword");

            Assert.That(after.Instance.Value, Is.EqualTo("itest-card-sword"),
                "the sword itself vanished");

            Assert.That(after.Cards, Is.Empty,
                "the socket row outlived the card being taken out");

            // D: a second world server reading the same database sees an empty sword.
            WorldCharacterRegistry second = NewRegistry();
            LivingCharacter returned = Enter(second, connectionId: 4);

            EquipmentInstance reloaded = null;

            for (var i = 0; i < returned.Inventory.Capacity; i++)
            {
                var piece = returned.Inventory.GetSlot(i).Content as EquipmentInstance;

                if (piece != null && piece.InstanceId.Value == "itest-card-sword")
                {
                    reloaded = piece;
                }
            }

            Assert.That(reloaded, Is.Not.Null, "the sword did not come back at all");
            Assert.That(reloaded.CardCount, Is.Zero,
                "a fresh world restored a card that had been taken out");
        }

        /// <summary>Clears every slot, so this test starts from a bag it fully controls.</summary>
        private static void EmptyTheBag(LivingCharacter character)
        {
            character.Inventory.Clear();

            Assert.That(character.Inventory.FreeSlots,
                Is.EqualTo(character.Inventory.Capacity),
                "the bag would not empty, so this test cannot control its own fixture");
        }

        /// <summary>One item row out of a loaded character, by its instance id.</summary>
        private static PersistedItem RowFor(CharacterPersistenceResult loaded,
            string instanceId)
        {
            Assert.That(loaded.IsOk, Is.True, "live load failed: " + loaded.Detail);

            for (var i = 0; i < loaded.Character.Items.Count; i++)
            {
                if (loaded.Character.Items[i].Instance.Value == instanceId)
                {
                    return loaded.Character.Items[i];
                }
            }

            Assert.Fail("no row for " + instanceId);

            return default;
        }

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
