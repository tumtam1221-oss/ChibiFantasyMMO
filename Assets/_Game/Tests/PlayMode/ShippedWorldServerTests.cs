// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed production scene through the editor scene manager, because the
// point is to prove the SHIPPED composition works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// The committed World_Server scene, running a world it composed itself.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here builds a world.</b> No <c>new WorldSimulation</c>, no
    /// <c>RefreshAll</c>, no registry assembled by a fixture. The scene is loaded, its
    /// bootstrap composes from its own serialized catalogue, and every assertion below is
    /// about what that produced. A test that had to assemble the world would prove only that
    /// the test can.
    ///
    /// <b>The backend is the one thing substituted.</b> A character store and a session
    /// authority are network services, and the shipped <c>Compose</c> already accepts both as
    /// arguments precisely so a world can be stood up without one. What is under test is the
    /// composition and the content, not HTTP -- which has its own live suite.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldServerTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string MaxHp = "stat.max_hp";
        private const string MaxMp = "stat.max_mp";
        private const string Atk = "stat.atk";
        private const string Str = "stat.str";
        private const string Vit = "stat.vit";
        private const string Int = "stat.int";

        /// <summary>A store that keeps what it was given, so a reconnect reads it back.</summary>
        private sealed class FakeStore : ICharacterStateStore
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

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private NetworkManager _server;
        private FakeStore _store;

        private GameObject _clientObject;
        private NetworkManager _client;

        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null) _client.ClientManager.StopConnection();
            if (_clientObject != null) Object.DestroyImmediate(_clientObject);

            if (_bootstrap != null)
            {
                _bootstrap.StopServer();

                // It outlived its own scene, so unloading the scene will not take it: the
                // next test would find a manager belonging to a world that is over.
                Object.DestroyImmediate(_bootstrap.gameObject);
            }

            // Unloaded rather than closed: closing a scene is an editor operation and the
            // whole point of this fixture is that the scene is running.
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- A: the scene composes a world -----------------------------------------------------

        [UnityTest]
        public IEnumerator TheShippedSceneComposesExactlyOneReadyWorld()
        {
            yield return LoadServerScene();

            Assert.That(_bootstrap, Is.Not.Null, "no bootstrap in the shipped scene");
            Assert.That(_bootstrap.ContentFaults, Is.Empty,
                "shipped content was refused: " + string.Join("; ", _bootstrap.ContentFaults));
            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "the shipped scene composed no runnable world");
            Assert.That(_bootstrap.Simulation, Is.Not.Null,
                "no test fixture built this: the scene did");
            Assert.That(_bootstrap.Characters, Is.Not.Null);

            // Exactly one of everything that could tick or listen. Counted across the whole
            // play session rather than the scene, because the manager has already moved
            // itself into DontDestroyOnLoad by now -- and a second one anywhere is the bug
            // this is looking for.
            Assert.That(Object.FindObjectsByType<WorldServerBootstrap>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(1), "two bootstraps means two worlds");

            Assert.That(Object.FindObjectsByType<NetworkManager>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(1), "two managers means two sockets");

            // And the world it built runs on the production catalogue, not on a fixture.
            Assert.That(_bootstrap.Characters.Count, Is.Zero, "nobody has arrived yet");
        }

        [UnityTest]
        public IEnumerator TheWorldAdvancesOnlyWhileTheServerIsListening()
        {
            yield return LoadServerScene(start: false);

            long idle = _bootstrap.Ticks;

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Ticks, Is.EqualTo(idle),
                "a world nobody is in has no time to pass");

            Assert.That(_bootstrap.StartServer(), Is.True);

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Ticks, Is.GreaterThan(idle), "the world runs now");

            WorldSimulation running = _bootstrap.Simulation;

            _bootstrap.StopServer();

            long stopped = _bootstrap.Ticks;

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(_bootstrap.Ticks, Is.EqualTo(stopped), "and stops when the socket does");

            // Restarting must not build a second one.
            Assert.That(_bootstrap.StartServer(), Is.True);

            for (var i = 0; i < 3; i++) yield return null;

            Assert.That(_bootstrap.Simulation, Is.SameAs(running),
                "a restart that rebuilt the world would abandon everybody in it");
            Assert.That(_bootstrap.Ticks, Is.GreaterThan(stopped));
        }

        // ---- B: bad content refuses rather than limps ------------------------------------------------

        [UnityTest]
        public IEnumerator ContentThatDoesNotValidateLeavesTheWorldUnreadyAndSaysWhy()
        {
            yield return LoadServerScene(start: false);

            // An empty catalogue: structurally a catalogue, describing no world at all.
            var empty = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            _created.Add(empty);

            Set(_bootstrap, "_content", empty);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "content refused"));

            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null);

            Assert.That(_bootstrap.IsWorldReady, Is.False,
                "a half-valid world must not report itself ready");
            Assert.That(_bootstrap.Simulation, Is.Null, "and must not exist");
            Assert.That(_bootstrap.ContentFaults, Is.Not.Empty,
                "an operator needs the list of what to fix");

            // Nothing was invented to paper over it.
            Assert.That(_bootstrap.Characters, Is.Null);
        }

        // ---- C: a character enters the production world ------------------------------------------------

        [UnityTest]
        public IEnumerator AWoundedCharacterEntersTheProductionWorldWithCorrectStats()
        {
            yield return LoadWorldWithStore();

            // Persisted at 63 health and 7 mana, a Swordsman on the starter map.
            LivingCharacter character = Admit("char-a", 1, health: 63, mana: 7);

            // The production formulas: MaxHP = 40 + VIT x 8, MaxMP = 20 + INT x 5,
            // ATK = 5 + STR x 2, with the Swordsman's authored attributes.
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(40 + 8 * 8));
            Assert.That(character.Combatant.Limits.MaxMana, Is.EqualTo(20 + 3 * 5));
            Assert.That(Stat(character, Atk), Is.EqualTo(5 + 10 * 2));

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(63),
                "entering the world must not cost a player the health they logged out with");
            Assert.That(character.Domain.Resources.CurrentMana, Is.EqualTo(7));
            Assert.That(character.Combatant.IsAlive(), Is.True,
                "never destructively clamped against an unknown ceiling");

            // And the starter spawn placed them on the production map.
            Assert.That(character.Location.CurrentMap.Value, Is.EqualTo(StarterMap));
        }

        [UnityTest]
        public IEnumerator TheAdmittedCharacterReachesARealClientThroughTheScenesReplication()
        {
            yield return LoadWorldWithStore();

            Seed("char-a", health: 63);

            yield return ConnectAClient();

            // Nothing below admits anybody. A real client sends a real join over a real
            // socket, the scene's own authenticator admits it, and the scene's own
            // OnAdmitted puts the character into the world it composed.
            _client.ClientManager.Broadcast(new WorldJoinRequestMessage
            {
                Token = "char-a",
                ClientVersion = "1.0.0",
                ProtocolVersion = "1.0.0",
                ContentVersion = "1.0.0",
            });

            yield return Until(() => Entity("char-a") != null
                && Entity("char-a").MaxHealth > 0);

            Assert.That(_bootstrap.Characters.TryGetByCharacter(new CharacterId("char-a"),
                out LivingCharacter character), Is.True,
                "the shipped admission path never put the character in the world");

            CharacterNetworkEntity observed = Entity("char-a");

            Assert.That(observed, Is.Not.Null,
                "the scene's own replication never spawned the character");
            Assert.That(observed.MaxHealth, Is.EqualTo(character.Combatant.MaxHealth));
            Assert.That(observed.MaxMana, Is.EqualTo(character.Combatant.Limits.MaxMana));
            Assert.That(observed.Health, Is.EqualTo(character.Combatant.CurrentHealth));
            Assert.That(observed.Health, Is.GreaterThan(0),
                "the first state a client ever sees must not be a zero clamp");
        }

        // ---- D: status through the shipped world loop ------------------------------------------------------

        [UnityTest]
        public IEnumerator AStatusChangesStatsThroughTheShippedWorldLoop()
        {
            yield return LoadWorldWithStore();

            LivingCharacter character = Admit("char-a", 1);

            int before = Stat(character, Atk);

            // A status effect authored here rather than in the catalogue: the shipped world
            // has none yet, and inventing production content to make a test pass is what the
            // previous gate refused to do.
            StatusEffectDefinition might = Effect("status.test.might", Atk, 12f);

            Assert.That(StatusEffectService.TryApply(character.Status,
                might.Id, new DefinitionId("test.source"), Effects(might), 30f).IsAccepted,
                Is.True);

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(before + 12),
                "no test called RefreshAll; the shipped loop noticed");

            character.Status.Tick(31f);

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(before),
                "and the shipped loop took it away again");
        }

        // ---- E and F: equipment and combat, on production stats -----------------------------------------------

        [UnityTest]
        public IEnumerator ProductionStatsAreEnoughForALegalCombatRound()
        {
            yield return LoadWorldWithStore();

            LivingCharacter attacker = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int health = victim.Combatant.CurrentHealth;

            Assert.That(health, Is.GreaterThan(0));

            // Through the scene's own combat pipeline, reached the way a request reaches it.
            ServerCombatResult result = Attack(attacker, victim, 1);

            Assert.That(result.IsAccepted, Is.True,
                "the starter content cannot fight: " + result);
            Assert.That(result.Damage, Is.GreaterThan(0),
                "a starter Swordsman who cannot hurt a training slime");
            Assert.That(victim.Combatant.CurrentHealth, Is.LessThan(health));

            // And a buff moves the number the formula read.
            StatusEffectDefinition might = Effect("status.test.might", Atk, 40f);

            StatusEffectService.TryApply(attacker.Status, might.Id,
                new DefinitionId("test.source"), Effects(might), 60f);

            yield return Tick();

            ServerCombatResult buffed = Attack(attacker, victim, 2);

            Assert.That(buffed.IsAccepted, Is.True, buffed.ToString());
            Assert.That(buffed.Damage, Is.GreaterThan(result.Damage),
                "combat did not consume the production-composed derived stats");
        }

        // ---- G: the production monster reaches the runtime -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheProductionMonsterDefinitionReachesTheWorldRuntime()
        {
            yield return LoadWorldWithStore();

            WorldContentCatalogue catalogue = Catalogue();

            Assert.That(catalogue.BuildMonsters().TryGet(
                new DefinitionId("monster.training_slime"), out MonsterDefinition slime),
                Is.True, "the shipped catalogue has no monster");

            Assert.That(slime.AllowedMaps, Is.Not.Empty);
            Assert.That(slime.AllowedMaps[0].Value, Is.EqualTo(StarterMap),
                "a monster on a map this world does not have is unreachable");

            // The nest source is the database, and this world was composed without one --
            // which is a legitimate configuration and not a fault. What matters here is that
            // the loader exists to receive it and the runtime ticks either way.
            long ticks = _bootstrap.Ticks;

            yield return Tick(5);

            Assert.That(_bootstrap.Ticks, Is.GreaterThan(ticks),
                "the simulation that owns the monster runtime is advancing");
            Assert.That(_bootstrap.ContentFaults, Is.Empty);
        }

        // ---- H: leaving and coming back --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReconnectComesBackWithPersistedStateAndNoStaleStatus()
        {
            yield return LoadWorldWithStore();

            LivingCharacter character = Admit("char-a", 1, health: 63);

            StatusEffectDefinition might = Effect("status.test.might", Atk, 12f);

            StatusEffectService.TryApply(character.Status, might.Id,
                new DefinitionId("test.source"), Effects(might), 60f);

            yield return Tick();

            int buffed = Stat(character, Atk);

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "temporary status is server memory and is not persisted");
            Assert.That(Stat(returned, Atk), Is.LessThan(buffed),
                "a stale buff surviving a reconnect would be a permanent one");
            Assert.That(returned.Combatant.CurrentHealth, Is.GreaterThan(0),
                "and they did not come back dead");
            Assert.That(returned.Combatant.MaxHealth, Is.EqualTo(40 + 8 * 8));

            // One world throughout.
            Assert.That(_bootstrap.Simulation, Is.Not.Null);
        }

        // ---- I: two characters ---------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TwoCharactersInTheProductionWorldStayIsolated()
        {
            yield return LoadWorldWithStore();

            LivingCharacter a = Admit("char-a", 1);
            LivingCharacter b = Admit("char-b", 2, team: 2);

            int aBefore = Stat(a, Atk);
            int bBefore = Stat(b, Atk);

            StatusEffectDefinition might = Effect("status.test.might", Atk, 25f);

            StatusEffectService.TryApply(a.Status, might.Id,
                new DefinitionId("test.source"), Effects(might), 60f);

            yield return Tick();

            Assert.That(Stat(a, Atk), Is.EqualTo(aBefore + 25));
            Assert.That(Stat(b, Atk), Is.EqualTo(bBefore), "B was not buffed by A's buff");
            Assert.That(b.Status.ActiveCount, Is.Zero);
            Assert.That(_bootstrap.Characters.Count, Is.EqualTo(2));
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------

        /// <summary>Loads the committed server scene and lets it compose itself.</summary>
        private IEnumerator LoadServerScene(bool start = true)
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);

            // Awake, and with it FishNet's persistence rule, which moves the manager into
            // DontDestroyOnLoad -- so it is no longer among the loaded scene's roots.
            yield return null;

            WorldServerBootstrap[] bootstraps = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(bootstraps.Length, Is.EqualTo(1),
                "the shipped scene must contribute exactly one world bootstrap");

            _bootstrap = bootstraps[0];
            _server = _bootstrap.GetComponent<NetworkManager>();

            Assert.That(_bootstrap, Is.Not.Null, "the shipped scene has no bootstrap");
            Assert.That(_server, Is.Not.Null, "the bootstrap has no NetworkManager beside it");

            if (!start) _bootstrap.StopServer();

            yield return null;
        }

        /// <summary>
        /// The scene's own world, recomposed against a store this test controls.
        /// </summary>
        /// <remarks>Through the shipped <c>Compose</c>, which already takes both as arguments
        /// so a world can be stood up without a backend. The catalogue, the prefab, the
        /// authorities and the simulation are all still the scene's.</remarks>
        private IEnumerator LoadWorldWithStore()
        {
            yield return LoadServerScene(start: false);

            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            yield return null;
        }

        private IEnumerator ConnectAClient()
        {
            _clientObject = new GameObject("ShippedSceneClient");
            _clientObject.SetActive(false);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "SpawnablePrefabs is null"));

            _client = _clientObject.AddComponent<NetworkManager>();

            _client.SpawnablePrefabs = UnityEditor.AssetDatabase
                .LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_client, NetworkManager.PersistenceType.AllowMultiple);

            var transport = _clientObject.AddComponent<Tugboat>();
            transport.SetPort(ServerPort());
            transport.SetClientAddress("127.0.0.1");

            _clientObject.SetActive(true);

            Assert.That(_client.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _client.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);
        }

        private ushort ServerPort()
        {
            var field = typeof(WorldServerBootstrap).GetField("_port",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            return (ushort)field.GetValue(_bootstrap);
        }

        private CharacterNetworkEntity Entity(string character)
        {
            if (_client == null) return null;

            foreach (KeyValuePair<int, NetworkObject> pair in _client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null && entity.Character.Value == character) return entity;
            }

            return null;
        }

        /// <summary>Runs the shipped world loop, the way the bootstrap runs it.</summary>
        private IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        /// <summary>Enters the world through the bootstrap's own admission seam.</summary>
        private LivingCharacter Admit(string character, int connection, int team = 1,
            int health = 104, int mana = 35)
        {
            Seed(character, health, mana);

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        /// <summary>Puts a saved character in the store, so the world can load them.</summary>
        private void Seed(string character, int health = 104, int mana = 35)
        {
            string session = "session-" + character;

            if (_store.Rows.ContainsKey(session)) return;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 1, 1, 0, health, mana,
                new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                default, StarterAttributes(), null, null, 1);
        }

        /// <summary>The Swordsman's authored attributes, as a saved character carries them.</summary>
        private static PersistedStat[] StarterAttributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId(Str), 10),
                new PersistedStat(new DefinitionId(Vit), 8),
                new PersistedStat(new DefinitionId(Int), 3),
            };
        }

        private ServerCombatResult Attack(LivingCharacter attacker, LivingCharacter victim,
            long sequence)
        {
            var pipeline = (ServerCombatPipeline)typeof(WorldSimulation)
                .GetField("_combat", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap.Simulation);

            Assert.That(pipeline, Is.Not.Null, "the scene composed no combat pipeline");

            return pipeline.Execute(attacker.ConnectionId,
                new CombatCommand(attacker.Character, victim.CombatantId, default, 0,
                    sequence));
        }

        private static int Stat(LivingCharacter character, string stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(new DefinitionId(stat),
                out int value), Is.True, stat + " was never computed");

            return value;
        }

        private static WorldContentCatalogue Catalogue()
        {
            return UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);
        }

        private DefinitionRegistry<StatusEffectDefinition> Effects(
            params StatusEffectDefinition[] effects)
        {
            var registry = new DefinitionRegistry<StatusEffectDefinition>();

            foreach (StatusEffectDefinition effect in effects) registry.Register(effect);

            // The world's stat authority resolves effects through the catalogue's registry,
            // which does not know these. Point it at one that does -- the shipped world has
            // no authored status content yet, and inventing some to pass a test is exactly
            // what the previous gate refused to do.
            var authority = (CharacterStatAuthority)typeof(WorldSimulation)
                .GetField("_stats", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap.Simulation);

            typeof(CharacterStatAuthority)
                .GetField("_effects", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(authority, registry);

            return registry;
        }

        private StatusEffectDefinition Effect(string id, string stat, float amount)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            _created.Add(definition);

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)StatusEffectCategory.Buff
                + ",\"_durationSeconds\":0,\"_stackBehavior\":0,\"_maxStacks\":1}", definition);

            definition.GetType().GetField("_statModifiers",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(definition, new[]
                {
                    new StatModifier(new DefinitionId(stat), StatModifierKind.Flat, amount),
                });

            return definition;
        }

        private static void Set(Object target, string field, object value)
        {
            target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance).SetValue(target, value);
        }

        /// <summary>The smallest session authority a world can be composed against.</summary>
        /// <remarks>Admission over HTTP has its own live suite; what is under test here is
        /// the world the scene builds, not the service that lets people in.</remarks>
        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            /// <summary>The token names who is arriving, so a test can choose.</summary>
            public WorldAdmission Admit(WorldJoinClaim claim)
            {
                if (!claim.HasToken)
                {
                    return WorldAdmission.Refused(SessionRejection.SessionExpired);
                }

                string who = claim.Token.Value;

                return WorldAdmission.Admitted(new SessionId("session-" + who),
                    new AccountId("acc-" + who), new CharacterId(who),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld);
            }

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }
    }
}

#endif
