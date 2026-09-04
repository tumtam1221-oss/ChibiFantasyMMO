using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a client is allowed to say, and what it is structurally unable to say.
    /// </summary>
    /// <remarks>
    /// <b>The socket half is proven in PlayMode.</b> A real client really does send this
    /// request over a real connection there. What is checked here is the shape of the door:
    /// that the message has no field for damage, health, death, experience or an attacker,
    /// that no public write exists on the replicated state, and that the handler builds a
    /// command which leaves the attacker for the server to resolve.
    ///
    /// These are the properties a future convenience overload would quietly break, which is
    /// why they are asserted rather than left to the reader of the class.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterNetworkAuthorityTests : MonsterTestBase
    {
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private const string Dummy = "monster.dummy";
        private const int Connection = 4;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private ServerCombatPipeline _pipeline;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpNetworkAuthority()
        {
            AddMonster(Dummy, level: 3, experience: 10, stats: new[]
            {
                new StatValue(new DefinitionId(MaxHp), 100f),
                new StatValue(new DefinitionId(Atk), 5f),
                new StatValue(new DefinitionId(Def), 5f),
            });

            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, 10);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            var commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            _pipeline = new ServerCombatPipeline(commands, _runtime, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 5f));
        }

        [TearDown]
        public void TearDownNetworkAuthority()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            _local.Add(spawn);

            return spawn;
        }

        private LivingCharacter AddPlayer(string character = "char-a")
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-a"), new ServerId("srv-1"),
                character, 2, 5, 0, 100, 50, new DefinitionId("class.novice"), default,
                new DefinitionId(HomeMap), default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 20),
                    new PersistedStat(new DefinitionId(Def), 5),
                },
                null, null, 1);

            WorldSpawnResult result = _players.Spawn(Connection,
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-a"),
                    new CharacterId(character), new ServerId("srv-1"),
                    new ChannelId("ch-1"), new DefinitionId(HomeMap), new Revision(1),
                    new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        private LivingMonster SpawnMonster()
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Dummy),
                new CombatPosition(1f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));

            _runtime.PopulateAll();

            return _runtime.All()[0];
        }

        // ---- the request handler ----------------------------------------------------------

        [Test]
        public void TheHandlerLeavesTheAttackerForTheServerToResolve()
        {
            AddPlayer();

            LivingMonster monster = SpawnMonster();

            var handler = new CharacterCombatRequestHandler(_pipeline);

            handler.Submit(Connection, monster.Instance, default, 0, 1);

            Assert.That(handler.Handled, Is.EqualTo(1));
            Assert.That(handler.LastResult.IsAccepted, Is.True,
                handler.LastResult.ToString());
            Assert.That(handler.LastResult.Attacker.Value, Is.EqualTo("char-a"),
                "resolved from the connection, never from the message");
        }

        [Test]
        public void AConnectionWithNoCharacterGetsNothingHoweverItAsks()
        {
            SpawnMonster();

            var handler = new CharacterCombatRequestHandler(_pipeline);

            handler.Submit(999, _runtime.All()[0].Instance, default, 0, 1);

            Assert.That(handler.LastResult.Rejection,
                Is.EqualTo(CombatCommandRejection.NoCharacter));
            Assert.That(_runtime.All()[0].State.CurrentHealth, Is.EqualTo(100));
        }

        [Test]
        public void AHandlerWithNoPipelineRefusesRatherThanThrows()
        {
            var handler = new CharacterCombatRequestHandler(null);

            handler.Submit(Connection, new InstanceId("anything"), default, 0, 1);

            Assert.That(handler.LastResult.IsAccepted, Is.False);
            Assert.That(handler.LastResult.Rejection,
                Is.EqualTo(CombatCommandRejection.NoCharacter));
        }

        [Test]
        public void ReplicationIsDrivenAfterEveryRequestIncludingRefusedOnes()
        {
            AddPlayer();

            var synchronised = 0;

            var handler = new CharacterCombatRequestHandler(_pipeline,
                () => synchronised++);

            handler.Submit(Connection, SpawnMonster().Instance, default, 0, 1);

            Assert.That(synchronised, Is.EqualTo(1));

            // A refused request still ends with the client being told the truth.
            handler.Submit(Connection, new InstanceId("nothing"), default, 0, 2);

            Assert.That(synchronised, Is.EqualTo(2));
        }

        [Test]
        public void NothingOnTheHandlerAcceptsAnAuthoritativeValue()
        {
            System.Reflection.MethodInfo submit =
                typeof(CharacterCombatRequestHandler).GetMethod("Submit");

            Assert.That(submit, Is.Not.Null);

            foreach (System.Reflection.ParameterInfo parameter in submit.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("damage"));
                Assert.That(name, Does.Not.Contain("health"));
                Assert.That(name, Does.Not.Contain("experience"));
                Assert.That(name, Does.Not.Contain("loot"));
                Assert.That(name, Does.Not.Contain("attacker"),
                    "who is asking is the connection, not an argument");
            }
        }

        // ---- the message itself -------------------------------------------------------------

        [Test]
        public void TheClientRequestCarriesIntentAndNothingElse()
        {
            System.Reflection.MethodInfo request =
                typeof(CharacterNetworkEntity).GetMethod("RequestAttack");

            Assert.That(request, Is.Not.Null, "there is no client request at all");

            var names = new List<string>();

            foreach (System.Reflection.ParameterInfo parameter in request.GetParameters())
            {
                names.Add(parameter.Name.ToLowerInvariant());
            }

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "targetinstanceid", "skillid", "rank", "sequence",
            }), "a target, a skill, a rank and an ordering number -- nothing else");

            Assert.That(request.ReturnType, Is.EqualTo(typeof(void)),
                "a return value is something a client could mistake for authority");
        }

        [Test]
        public void TheRequestIsAServerRpcSoOnlyTheOwnerMaySendIt()
        {
            System.Reflection.MethodInfo request =
                typeof(CharacterNetworkEntity).GetMethod("RequestAttack");

            object[] attributes = request.GetCustomAttributes(
                typeof(FishNet.Object.ServerRpcAttribute), true);

            Assert.That(attributes, Is.Not.Empty,
                "without this the message is not routed to the server at all");

            var rpc = (FishNet.Object.ServerRpcAttribute)attributes[0];

            Assert.That(rpc.RequireOwnership, Is.True,
                "ownership is what stops a connection acting through somebody else's "
                + "character");
        }

        [Test]
        public void EveryPublishOnTheEntityIsServerOnly()
        {
            foreach (System.Reflection.MethodInfo method in
                typeof(CharacterNetworkEntity).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (!method.Name.StartsWith("Server")) continue;

                Assert.That(method.GetCustomAttributes(
                    typeof(FishNet.Object.ServerAttribute), true), Is.Not.Empty,
                    method.Name + " can be called on a client");
            }
        }

        [Test]
        public void NoReplicatedValueCanBeWrittenThroughAProperty()
        {
            foreach (System.Reflection.PropertyInfo property in
                typeof(CharacterNetworkEntity).GetProperties(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                // A private setter is not a way in; a public one is. The 18.4 snapshot
                // property has to be assigned when a message lands, so the invariant is
                // tightened to what it always meant rather than loosened.
                Assert.That(property.SetMethod == null || !property.SetMethod.IsPublic,
                    Is.True, property.Name + " can be written from outside");
            }
        }

        [Test]
        public void TheEntityCarriesNoSecret()
        {
            // Ids, numbers and a map. Nothing about an account, a session or a token.
            foreach (System.Reflection.MemberInfo member in
                typeof(CharacterNetworkEntity).GetMembers(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                string name = member.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("token"), member.Name);
                Assert.That(name, Does.Not.Contain("session"), member.Name);
                Assert.That(name, Does.Not.Contain("password"), member.Name);
                Assert.That(name, Does.Not.Contain("account"), member.Name);
                Assert.That(name, Does.Not.Contain("revision"), member.Name);
            }
        }

        [Test]
        public void NoClientCodeReachesTheServerSideOfThis()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                string named = file.Replace('\\', '/');

                Assert.That(source, Does.Not.Contain("CharacterReplicationService"), named);
                Assert.That(source, Does.Not.Contain("CharacterCombatRequestHandler"), named);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), named);
            }
        }

        [Test]
        public void ExactlyOneTypeSubmitsAClientCombatRequest()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts",
                "*.cs", System.IO.SearchOption.AllDirectories);

            var sinks = new List<string>();

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains(": ICharacterCombatRequestSink"))
                {
                    sinks.Add(file.Replace('\\', '/'));
                }
            }

            Assert.That(sinks, Has.Count.EqualTo(1), string.Join(", ", sinks));
            Assert.That(sinks[0],
                Does.EndWith("/Server/CharacterCombatRequestHandler.cs"));
        }

        // ---- the shipped prefab ---------------------------------------------------------------

        [Test]
        public void TheCharacterPrefabIsANetworkIdentityAndNotAnArtAsset()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                CharacterPrefabPath);

            Assert.That(prefab, Is.Not.Null, "no character prefab is shipped");
            Assert.That(prefab.GetComponent<FishNet.Object.NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterNetworkEntity>(), Is.Not.Null);

            // Production character art exists, but wiring a model, a rig and an animator to
            // a network object is presentation work and belongs to the gate that does it.
            // A placeholder capsule here would be a fake asset pretending to be a character.
            Assert.That(prefab.GetComponentInChildren<Renderer>(), Is.Null,
                "this is network identity only, deliberately");
        }

        [Test]
        public void TheCharacterPrefabIsInTheShippedRegistry()
        {
            var registry = UnityEditor.AssetDatabase
                .LoadAssetAtPath<FishNet.Managing.Object.DefaultPrefabObjects>(
                    "Assets/DefaultPrefabObjects.asset");

            Assert.That(registry, Is.Not.Null);

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                CharacterPrefabPath);

            var found = false;

            for (int i = 0; i < registry.GetObjectCount(); i++)
            {
                FishNet.Object.NetworkObject entry = registry.GetObject(true, i);

                if (entry != null && entry.gameObject == prefab) found = true;
            }

            Assert.That(found, Is.True,
                "an unregistered prefab cannot be spawned over the network at all");
        }
    }
}
