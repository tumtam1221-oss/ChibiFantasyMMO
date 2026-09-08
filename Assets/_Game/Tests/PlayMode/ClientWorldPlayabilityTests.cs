#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Client;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Managing;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What a person can actually do, through the composition a person actually gets.
    /// </summary>
    /// <remarks>
    /// <b>The client here is the production one.</b> It is built by
    /// <see cref="ClientApplicationBootstrap"/> from the shipped network prefab, connects
    /// over a real socket to the shipped <c>World_Server</c> scene, and everything it draws
    /// and sends goes through the production presentation, the production character object
    /// and the production combat request. Nothing in this fixture composes a network manager
    /// or a presenter itself.
    ///
    /// <b>What it deliberately does not cover.</b> Logging in through PHP and MySQL is
    /// proved by <c>LiveClientFlowIntegrationTests</c> against the real backend; this fixture
    /// starts from an admitted session, because what is under test here is the half that had
    /// no production composition at all -- the world.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientWorldPlayabilityTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string NetworkPrefabPath =
            "Assets/_Game/Prefabs/Network/World_NetworkManager.prefab";

        private const string ContentPath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";

        private sealed class CharacterStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        /// <summary>Admits the connection with a character, as the account API would.</summary>
        private sealed class AdmitsOne : IWorldSessionAuthority
        {
            private readonly CharacterStore _store;

            private int _next;

            public AdmitsOne(CharacterStore store) => _store = store;

            public WorldAdmission Admit(WorldJoinClaim claim)
            {
                string character = "char-player-" + _next++;
                string session = "session-" + character;

                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, (int)CharacterGender.Male, 20, 0,
                    240, 60, new DefinitionId(StarterClass), default,
                    new DefinitionId(StarterMap), default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 40),
                        new PersistedStat(new DefinitionId("stat.vit"), 20),
                        new PersistedStat(new DefinitionId("stat.int"), 5),
                    }, null, null, 1, null, 0, default, null, null, default);

                return WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld);
            }

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        /// <summary>Makes every authored drop land, so a pickup has something to pick up.</summary>
        /// <remarks>The drop tables and their chances are content and are not changed; this
        /// only decides the coin flips, which is what a random source is for. Without it a
        /// test of picking loot up would be a test of the drop rate.</remarks>
        private sealed class AlwaysDrops : IRandomResultSource, IRandomRangeSource
        {
            public bool Succeeds(float chance) => chance > 0f;

            public int Range(int min, int max) => min;
        }

        private Scene _scene;
        private WorldServerBootstrap _server;
        private CharacterStore _characters;

        private GameObject _clientObject;
        private ClientApplicationBootstrap _client;
        private GameObject _hudObject;
        private GameObject _bagObject;
        private GameObject _presentationObject;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null)
            {
                _client.LeaveWorld();

                Object.DestroyImmediate(_client.gameObject);
                _client = null;
            }

            yield return null;

            if (_presentationObject != null) Object.DestroyImmediate(_presentationObject);
            if (_hudObject != null) Object.DestroyImmediate(_hudObject);
            if (_bagObject != null) Object.DestroyImmediate(_bagObject);
            if (_clientObject != null) Object.DestroyImmediate(_clientObject);

            if (_server != null)
            {
                _server.StopServer();
                Object.DestroyImmediate(_server.gameObject);
                _server = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        // ---- composition -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheClientRootComposesOneWorldConnection()
        {
            yield return StartServer();

            yield return StartClient();

            Assert.That(_client.NetworkManager, Is.Not.Null, "no network manager was built");
            Assert.That(_client.World, Is.Not.Null, "no client bootstrap was composed");
            Assert.That(_client.WorldCompositions, Is.EqualTo(1));

            // A client that kept the server half would compose an entire authoritative world.
            Assert.That(_client.NetworkManager.GetComponent<WorldServerBootstrap>(), Is.Null,
                "the client's network object still carries the server bootstrap");

            // Composing again is idempotent: one world, one connection.
            _client.ComposeWorld();

            Assert.That(_client.WorldCompositions, Is.EqualTo(1));

            Assert.That(Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)
                .Length, Is.EqualTo(2),
                "expected exactly one server manager and one client manager");
        }

        [UnityTest]
        public IEnumerator TheOwnedCharacterSpawnsAndThePresentationBindsToIt()
        {
            yield return StartServer();

            yield return StartClient();

            yield return Until(() => Owned() != null, 900);

            CharacterNetworkEntity owned = Owned();

            Assert.That(owned, Is.Not.Null, "the client never received a character it owns");
            Assert.That(owned.IsOwner, Is.True);

            // The approved model, built by the production presenter.
            var visual = owned.GetComponent<CharacterVisualPresenter>();

            yield return Until(() => visual != null && visual.HasVisual, 900);

            Assert.That(visual.HasVisual, Is.True, "no approved model was built");
            Assert.That(visual.Model.name, Does.Contain("_Meshy"),
                "something other than an approved production model was drawn");

            // And the HUD is showing this character rather than nothing.
            var hud = _hudObject.GetComponent<WorldHudScreen>();

            yield return Until(() => hud.IsBound, 600);

            Assert.That(hud.IsBound, Is.True, "the HUD never bound to the owned character");
            Assert.That(hud.Current.Character.Value, Is.EqualTo(owned.Character.Value));
        }

        // ---- playing ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator APersonCanTargetAMonsterAndTheServerDamagesIt()
        {
            yield return StartServer();

            yield return StartClient();

            yield return Until(() => Owned() != null, 900);

            SpawnMonsters(1);

            yield return Until(() => FirstMonsterOnClient() != null, 900);

            MonsterNetworkEntity monster = FirstMonsterOnClient();

            Assert.That(monster, Is.Not.Null, "the client was sent no monster");

            // What a click does, without a mouse: the same method the click calls.
            _client.Combat.Select(monster);

            Assert.That(_client.Combat.Target, Is.EqualTo(monster));
            Assert.That(_client.Combat.TargetsSelected, Is.EqualTo(1));

            LivingMonster living = FirstLivingMonster();

            // Standing next to it, decided by the server: melee reach is authored and the
            // server refuses a swing from across the map.
            _server.Characters.TryGetByCharacter(Owned().Character, out LivingCharacter hero);

            hero.Combatant.Position = living.State.Position;

            int before = living.State.CurrentHealth;

            for (var i = 0; i < 60 && living.State.CurrentHealth >= before; i++)
            {
                _client.Combat.RequestAttack();

                yield return null;
            }

            Assert.That(_client.Combat.AttacksRequested, Is.GreaterThan(0));

            Assert.That(living.State.CurrentHealth, Is.LessThan(before),
                "the server never applied damage from the player's attack");

            // And the player is told what happened to what they hit.
            yield return Until(() => monster.Health < before, 600);

            Assert.That(monster.Health, Is.LessThan(before),
                "the client's copy of the monster never lost health");
        }

        [UnityTest]
        public IEnumerator KillingAMonsterAwardsExperienceThroughTheRewardPipeline()
        {
            yield return StartServer();

            yield return StartClient();

            yield return Until(() => Owned() != null, 900);

            SpawnMonsters(1);

            yield return Until(() => FirstMonsterOnClient() != null, 900);

            _server.Characters.TryGetByCharacter(Owned().Character, out LivingCharacter hero);

            long experienceBefore = hero.Domain.Progression.Experience;

            LivingMonster living = FirstLivingMonster();

            hero.Combatant.Position = living.State.Position;

            _client.Combat.Select(FirstMonsterOnClient());

            for (var i = 0; i < 400 && living.State.CurrentHealth > 0; i++)
            {
                _client.Combat.RequestAttack();

                yield return null;
            }

            Assert.That(living.State.CurrentHealth, Is.Zero, "the monster would not die");

            yield return Tick(60);

            Assert.That(hero.Domain.Progression.Experience,
                Is.GreaterThan(experienceBefore),
                "a kill through the player's own request paid no experience");

            // And the number the player sees is the server's.
            CharacterNetworkEntity owned = Owned();

            yield return Until(() => owned.Experience > experienceBefore, 600);

            Assert.That(owned.Experience, Is.EqualTo(hero.Domain.Progression.Experience));
        }

        [UnityTest]
        public IEnumerator LeavingTheWorldLeavesNothingBehind()
        {
            yield return StartServer();

            yield return StartClient();

            yield return Until(() => Owned() != null, 900);

            _client.LeaveWorld();

            yield return Tick(10);

            Assert.That(_client.NetworkManager, Is.Null);
            Assert.That(_client.World, Is.Null);
            Assert.That(_client.Combat, Is.Null);
            Assert.That(_client.Loot, Is.Null);

            Assert.That(Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)
                .Length, Is.EqualTo(1), "the client's network manager outlived the world");

            // And going back in composes exactly one more.
            _client.ComposeWorld();

            yield return Until(() => Owned() != null, 900);

            Assert.That(_client.WorldCompositions, Is.EqualTo(2));
            Assert.That(Owned(), Is.Not.Null, "a reconnecting player got no character");

            Assert.That(Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)
                .Length, Is.EqualTo(2), "reconnecting left a second client manager behind");
        }

        /// <summary>
        /// The last link a person needs: what the monster left, taken with one key.
        /// </summary>
        /// <remarks>The pile is published to the one player entitled to it, and the pickup
        /// goes back through the authoritative path. Nothing here mints an item, decides
        /// what dropped, or puts anything in a bag.</remarks>
        [UnityTest]
        public IEnumerator WhatAMonsterLeavesCanBePickedUp()
        {
            yield return StartServer();

            yield return StartClient();

            yield return Until(() => Owned() != null, 900);

            SpawnMonsters(1);

            yield return Until(() => FirstMonsterOnClient() != null, 900);

            _server.Characters.TryGetByCharacter(Owned().Character, out LivingCharacter hero);

            LivingMonster living = FirstLivingMonster();

            hero.Combatant.Position = living.State.Position;

            _client.Combat.Select(FirstMonsterOnClient());

            for (var i = 0; i < 400 && living.State.CurrentHealth > 0; i++)
            {
                _client.Combat.RequestAttack();

                yield return null;
            }

            Assert.That(living.State.CurrentHealth, Is.Zero, "the monster would not die");

            // The pile the server decided to leave.
            yield return Until(() => _server.Loot != null && _server.Loot.Count > 0, 900);

            Assert.That(_server.Loot.Count, Is.GreaterThan(0),
                "the monster left no pile, so there is nothing to pick up");

            // Standing on it: reach is enforced by the server, and a pile is only offered
            // to a player who is near enough to take it.
            LootObjectState pile = _server.Loot.All()[0];

            hero.Combatant.Position = new CombatPosition(pile.Position.X, pile.Position.Y,
                pile.Position.Z);

            yield return Until(() => _client.Loot != null && _client.Loot.Available > 0, 900);

            Assert.That(_client.Loot.Available, Is.GreaterThan(0),
                "the server offered this player nothing to pick up");

            int carriedBefore = Carried(hero);

            Assert.That(_client.Loot.RequestPickup(), Is.True,
                "the pickup request was never sent");

            yield return Until(() => Carried(hero) > carriedBefore, 900);

            Assert.That(Carried(hero), Is.GreaterThan(carriedBefore),
                "the server never gave the player what they asked for");
        }

        /// <summary>How many item stacks the server says this character is carrying.</summary>
        private static int Carried(LivingCharacter character)
        {
            var counted = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                if (character.Inventory.GetSlot(i).Content != null) counted++;
            }

            return counted;
        }

        // ---- harness ---------------------------------------------------------------------

        private IEnumerator StartServer()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _server = found[0];

            _server.StopServer();

            var rolls = new AlwaysDrops();

            _server.UseRandom(rolls, rolls);

            _server.Compose(new AdmitsOne(_characters), default, _characters, null, null,
                null);

            Assert.That(_server.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _server.ContentFaults));

            Assert.That(_server.StartServer(), Is.True);

            yield return null;
        }

        /// <summary>Builds the production client root and its world screens.</summary>
        private IEnumerator StartClient()
        {
            // The screens a client scene carries. Built here because this fixture does not
            // load GameWorld -- the composition under test is what the root does with them.
            _hudObject = new GameObject("HUD");
            _hudObject.AddComponent<WorldHudScreen>();

            _bagObject = new GameObject("Bag");
            _bagObject.AddComponent<InventoryScreen>();

            _presentationObject = new GameObject("Presentation");
            _presentationObject.AddComponent<WorldCameraDirector>();
            _presentationObject.AddComponent<WorldPresentationBinder>();

            _clientObject = new GameObject("Client Root");
            _clientObject.SetActive(false);

            _client = _clientObject.AddComponent<ClientApplicationBootstrap>();

            var flags = System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;

            typeof(ClientApplicationBootstrap).GetField("_networkManagerPrefab", flags)
                .SetValue(_client, UnityEditor.AssetDatabase
                    .LoadAssetAtPath<GameObject>(NetworkPrefabPath));

            typeof(ClientApplicationBootstrap).GetField("_content", flags)
                .SetValue(_client, UnityEditor.AssetDatabase
                    .LoadAssetAtPath<WorldContentCatalogue>(ContentPath));

            _clientObject.SetActive(true);

            // This process is already running a world, which FishNet would otherwise treat
            // as a reason to destroy the client's own manager.
            _client.AllowMultipleNetworkManagers = true;

            // The session this client presents. In a shipped client this is what the
            // login produced; here the world's authority is the fixture's own, and the
            // server still refuses a join that presents no token at all.
            _client.WorldToken = new SessionToken("playability-fixture");

            _client.UseWorldEndpoint("127.0.0.1", ServerPort());

            _client.ComposeWorld();

            yield return Until(() => _client.NetworkManager != null
                && _client.NetworkManager.ClientManager.Started, 900);

            Assert.That(_client.NetworkManager.ClientManager.Started, Is.True,
                "the production client never connected to the world");
        }

        private ushort ServerPort()
        {
            return (ushort)typeof(WorldServerBootstrap)
                .GetField("_port", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_server);
        }

        private void SpawnMonsters(int count)
        {
            MonsterWorldRuntime monsters = _server.Simulation.Monsters();

            for (var i = 0; i < count; i++)
            {
                monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                    new CombatPosition(i * 5f, 0f, 0f), 0f, 1, 0f,
                    new DefinitionId(StarterMap)));
            }

            monsters.PopulateAll();
        }

        private LivingMonster FirstLivingMonster()
        {
            IReadOnlyList<LivingMonster> all = _server.Simulation.Monsters().All();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].IsAlive) return all[i];
            }

            return null;
        }

        private CharacterNetworkEntity Owned()
        {
            if (_client == null || _client.NetworkManager == null) return null;

            FishNet.Connection.NetworkConnection connection =
                _client.NetworkManager.ClientManager.Connection;

            if (connection == null || !connection.IsValid) return null;

            foreach (NetworkObject owned in connection.Objects)
            {
                if (owned == null) continue;

                if (owned.TryGetComponent(out CharacterNetworkEntity entity)) return entity;
            }

            return null;
        }

        private MonsterNetworkEntity FirstMonsterOnClient()
        {
            if (_client == null || _client.NetworkManager == null) return null;

            foreach (KeyValuePair<int, NetworkObject> pair in
                _client.NetworkManager.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                var monster = pair.Value.GetComponent<MonsterNetworkEntity>();

                if (monster != null) return monster;
            }

            return null;
        }

        private static IEnumerator Tick(int frames)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }
    }
}

#endif
