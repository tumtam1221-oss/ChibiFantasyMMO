// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.UI;
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
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// The world running itself: one tick, real clients, nobody kicking anything by hand.
    /// </summary>
    /// <remarks>
    /// <b>The rule that makes these tests worth having.</b> Not one of them calls
    /// <c>RefreshAll</c>, <c>Force</c> or <c>SetLimits</c>. Everything happens because
    /// <see cref="WorldSimulation.Tick"/> ran, exactly as it runs in a shipped server. A test
    /// that had to kick the stat authority to make a buff work would be proving that the
    /// game does not.
    ///
    /// <b>Why the previous gate could not claim this.</b> 18.8 proved the arithmetic and the
    /// replication with the authority driven by the test. Nothing drove it in production --
    /// there was no production world loop to drive it from. There is now, and this is what
    /// running through it looks like.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the world rather than through login and enter-world, which has its own live
    /// suite. Everything after admission is the shipped path.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldSimulationNetworkTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string HomeMap = "map.home";

        private const string Str = "stat.str";
        private const string Vit = "stat.vit";
        private const string Atk = "stat.atk";
        private const string Def = "stat.def";
        private const string MaxHp = "stat.max_hp";
        private const string MaxMp = "stat.max_mp";

        private const string Might = "status.might";        // +40 flat attack
        private const string Fortitude = "status.fortitude"; // +100 flat max health
        private const string Sword = "item.sword";           // +25 flat attack

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

        private GameObject _serverObject;
        private GameObject _clientAObject;
        private GameObject _clientBObject;
        private NetworkManager _server;
        private NetworkManager _clientA;
        private NetworkManager _clientB;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private DefinitionRegistry<StatDefinition> _stats;
        private DefinitionRegistry<ItemDefinition> _items;
        private DefinitionRegistry<StatusEffectDefinition> _effects;
        private CharacterStatAuthority _statAuthority;
        private CharacterStatusAuthority _statusAuthority;
        private CharacterInventoryAuthority _inventory;
        private CharacterCombatRequestHandler _combat;
        private CharacterReplicationService _replication;
        private WorldSimulation _world;

        private readonly Dictionary<NetworkManager, WorldHudScreen> _huds =
            new Dictionary<NetworkManager, WorldHudScreen>();

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(58100, 60900);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("SimServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("SimClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("SimClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            _stats = new DefinitionRegistry<StatDefinition>();
            foreach (string id in new[] { Str, Vit, Atk, Def, MaxHp, MaxMp }) Stat(id);

            var formulas = new List<DerivedStatFormulaDefinition>
            {
                Formula("f.atk", Atk, 0, new StatTerm(new DefinitionId(Str), 1, 1)),
                Formula("f.def", Def, 0, new StatTerm(new DefinitionId(Vit), 1, 1)),
                Formula("f.hp", MaxHp, 100, new StatTerm(new DefinitionId(Vit), 10, 1)),
                Formula("f.mp", MaxMp, 20, new StatTerm(new DefinitionId(Str), 2, 1)),
            };

            _effects = new DefinitionRegistry<StatusEffectDefinition>();
            _effects.Register(Effect(Might, Flat(Atk, 40f)));
            _effects.Register(Effect(Fortitude, Flat(MaxHp, 100f)));

            _items = new DefinitionRegistry<ItemDefinition>();
            _items.Register(Weapon(Sword, Flat(Atk, 25f)));

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(HomeMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, _items, 8);

            var monsters = new MonsterWorldRuntime(_players,
                new DefinitionRegistry<MonsterDefinition>(), new DefinitionId(MaxHp),
                new CombatTeam(2));

            var commands = new CombatCommandAuthority(_players, _ => true, monsters);

            var pipeline = new ServerCombatPipeline(commands, monsters, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 50f),
                default, null, default, _effects);

            // A command handled between ticks settles the world immediately, so a second
            // command in the same frame is never resolved against stats the first one
            // invalidated. The lambda closes over the simulation built just below.
            _combat = new CharacterCombatRequestHandler(pipeline, () => _world.Settle());

            _replication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), _combat);

            _statusAuthority = new CharacterStatusAuthority(_players, _effects, _replication);
            _replication.UseStatus(_statusAuthority);

            _inventory = new CharacterInventoryAuthority(_players, _ => true, _items,
                _replication);

            _replication.UseInventory(_inventory);

            _statAuthority = new CharacterStatAuthority(_players, formulas, _stats, _effects,
                new EquipmentModifierResolver.Context(_items),
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            _world = new WorldSimulation(_players, _replication, _statusAuthority,
                _statAuthority, null, pipeline, monsters);
        }

        [TearDown]
        public void TearDown()
        {
            _replication?.DespawnAll();

            if (_clientB != null) _clientB.ClientManager.StopConnection();
            if (_clientA != null) _clientA.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientBObject != null) Object.DestroyImmediate(_clientBObject);
            if (_clientAObject != null) Object.DestroyImmediate(_clientAObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            _huds.Clear();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- A: a buff, through the world loop --------------------------------------------------

        [UnityTest]
        public IEnumerator AStatusAppliedOnTheServerChangesStatsOnTheNextWorldTick()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = Admit("char-a", Connections()[0]);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            int before = Stat(character, Atk);

            // Applied through the one service, as a skill or a fruit would.
            Apply(character, Might);

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(before + 40),
                "nothing in this test asked the stat authority to run");

            // And the owner was told about the effect by the same tick.
            yield return Until(() => Entity(_clientA, "char-a").Status.Count > 0);

            Assert.That(Entity(_clientA, "char-a").Status.Effects[0].EffectId,
                Is.EqualTo(Might));
        }

        // ---- B: and expiry, in the right order ----------------------------------------------------

        [UnityTest]
        public IEnumerator AnExpiringStatusLosesItsModifierInTheSameTickTheIconGoes()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter attacker = Admit("char-a", connections[0]);
            LivingCharacter victim = Admit("char-b", connections[1], team: 2);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-b") != null);

            Apply(attacker, Might, duration: 2f);

            yield return Tick();

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.IsAccepted, Is.True, _combat.LastResult.ToString());

            int buffed = _combat.LastResult.Damage;

            // Ticked past the end. Status expires and stats recompute inside one tick.
            yield return Tick(1, 3f);

            // The server side is where the ordering claim lives: the effect and its
            // modifier both went inside one tick, before anything was published.
            Assert.That(attacker.Status.Has(new DefinitionId(Might)), Is.False);

            // The client learns a moment later, at the speed of the socket.
            yield return Until(() => Entity(_clientA, "char-a").Status.Count == 0);

            Assert.That(Entity(_clientA, "char-a").Status.Count, Is.Zero,
                "the icon is gone");

            int handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 2);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.Damage, Is.LessThan(buffed),
                "there must be no tick in which the icon is gone and the damage is not");
        }

        // ---- C: a real equip request -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AClientEquipRequestChangesTheAuthoritativeStatThroughTheWorldLoop()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = Admit("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, 0),
            });

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-a").Inventory.Count > 0);

            int bare = Stat(character, Atk);

            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.False,
                "precondition: the sword is in the bag");

            // The Phase 18.4 path, in full: the client asks, the server's equipment
            // authority decides, and the world notices on its own.
            Entity(_clientA, "char-a").RequestInventoryAction(InventoryAction.Equip, 0, 0, 0, 1);

            yield return Until(() => _inventory.Handled > 0);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());
            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.True);

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 25),
                "no test called RefreshAll to make the sword count");

            // And off again through the same real request.
            int handled = _inventory.Handled;

            Entity(_clientA, "char-a").RequestInventoryAction(InventoryAction.Unequip,
                (int)EquipmentSlot.MainHand, 0, 0, 2);

            yield return Until(() => _inventory.Handled > handled);

            Assert.That(_inventory.LastResult.IsAccepted, Is.True,
                _inventory.LastResult.ToString());

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare),
                "taking the sword off took its bonus with it");
        }

        // ---- D: and combat reads all of it ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator CombatUsesWhateverTheWorldLoopLastComputed()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter attacker = Admit("char-a", connections[0], new[]
            {
                Row("item-1", Sword, 0),
            });

            LivingCharacter victim = Admit("char-b", connections[1], team: 2);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-b") != null
                && Entity(_clientA, "char-a").Inventory.Count > 0);

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.IsAccepted, Is.True, _combat.LastResult.ToString());

            int bare = _combat.LastResult.Damage;

            Assert.That(bare, Is.GreaterThan(0));

            // Equip through the real request, let the world notice, then swing again.
            Entity(_clientA, "char-a").RequestInventoryAction(InventoryAction.Equip, 0, 0, 0, 1);

            yield return Until(() => _inventory.Handled > 0);
            yield return Tick();

            int handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 2);

            yield return Until(() => _combat.Handled > handled);

            int armed = _combat.LastResult.Damage;

            Assert.That(armed, Is.GreaterThan(bare), "the sword reached the damage formula");

            // A buff on top, again with no manual refresh anywhere.
            Apply(attacker, Might);

            yield return Tick();

            handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 3);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.Damage, Is.GreaterThan(armed));
        }

        // ---- E: entering the world with resources intact ---------------------------------------------

        [UnityTest]
        public IEnumerator AWoundedCharacterEntersTheWorldStillWoundedAndNeverDead()
        {
            yield return StartServerAndOneClient();

            // Loaded at 75 health of an eventual 200, wearing a sword.
            LivingCharacter character = Admit("char-a", Connections()[0], new[]
            {
                Row("item-1", Sword, -1, equipmentSlot: (int)EquipmentSlot.MainHand),
            }, health: 75, mana: 12);

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(75),
                "entering the world must not cost a player the health they logged out with");
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(200),
                "and the ceiling is the authored formula's, not the save row's");
            Assert.That(character.Domain.Resources.CurrentMana, Is.EqualTo(12));
            Assert.That(character.Combatant.Limits.MaxMana, Is.EqualTo(100));
            Assert.That(character.Combatant.IsAlive(), Is.True);

            // The sword already counts, on the first calculation.
            Assert.That(Stat(character, Atk), Is.EqualTo(65));

            yield return Tick(2);

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => hud.Current.MaxHealth > 0);

            // The very first state a client ever sees is the correct one.
            Assert.That(Entity(_clientA, "char-a").Health, Is.EqualTo(75));
            Assert.That(Entity(_clientA, "char-a").MaxHealth, Is.EqualTo(200));
            Assert.That(Entity(_clientA, "char-a").MaxMana, Is.EqualTo(100));
            Assert.That(hud.Current.HealthLabel, Is.EqualTo("75 / 200"));
        }

        // ---- F: leaving and coming back ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReconnectComesBackCorrectWithoutAnybodyKickingTheAuthority()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            LivingCharacter character = Admit("char-a", connection, new[]
            {
                Row("item-1", Sword, -1, equipmentSlot: (int)EquipmentSlot.MainHand),
            }, health: 150);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            Apply(character, Might);

            yield return Tick();

            Assert.That(Stat(character, Atk), Is.EqualTo(105), "sword and buff both counted");

            Assert.That(_world.Release(connection).IsOk, Is.True);

            yield return Tick();

            yield return Until(() => Entity(_clientA, "char-a") == null);

            // Back again. No manual Force, no manual RefreshAll.
            LivingCharacter returned = Admit("char-a", connection);

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "temporary status is server memory and is not persisted");
            Assert.That(returned.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.True,
                "the sword is persisted and came back");
            Assert.That(Stat(returned, Atk), Is.EqualTo(65),
                "the sword's bonus survived and the buff did not");
            Assert.That(returned.Combatant.CurrentHealth, Is.GreaterThan(0),
                "and they did not come back dead");

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-a").MaxHealth > 0);

            Assert.That(Entity(_clientA, "char-a").MaxHealth, Is.EqualTo(200));
            Assert.That(Entity(_clientA, "char-a").Status.Count, Is.Zero,
                "no stale icon after a reconnect");
        }

        // ---- a moved ceiling, through the loop --------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMaxHealthBuffAndItsRemovalBothReachTheClientThroughTheLoop()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = Admit("char-a", Connections()[0]);

            yield return Tick(2);

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => hud.Current.MaxHealth > 0);

            int ceiling = character.Combatant.MaxHealth;

            Apply(character, Fortitude);

            yield return Tick();

            yield return Until(() => hud.Current.MaxHealth == ceiling + 100);

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(ceiling + 100));

            // Filled to the raised ceiling, then the buff goes.
            character.Combatant.ApplyHealthDelta(9999);

            character.Status.Remove(new DefinitionId(Fortitude));

            yield return Tick();

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(ceiling));
            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(ceiling),
                "losing the buff clamps, and never leaves health above the ceiling");

            yield return Until(() => hud.Current.MaxHealth == ceiling
                && hud.Current.Health == ceiling);

            Assert.That(hud.Current.HealthLabel, Is.EqualTo(ceiling + " / " + ceiling));
        }

        // ---- G: two clients -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BuffingOnePlayerThroughTheLoopLeavesTheOtherUntouched()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter a = Admit("char-a", connections[0]);
            LivingCharacter b = Admit("char-b", connections[1], team: 2);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientB, "char-b") != null
                && Entity(_clientB, "char-a") != null);

            int aBefore = Stat(a, Atk);
            int bBefore = Stat(b, Atk);

            Apply(a, Might);

            yield return Tick();

            Assert.That(Stat(a, Atk), Is.EqualTo(aBefore + 40));
            Assert.That(Stat(b, Atk), Is.EqualTo(bBefore), "B was not buffed by A's buff");
            Assert.That(b.Status.ActiveCount, Is.Zero);

            // 18.7's privacy, still holding through the production loop.
            yield return Until(() => Entity(_clientA, "char-a").Status.Count > 0);

            Assert.That(Entity(_clientB, "char-a").Status.Count, Is.Zero,
                "a buff tells an opponent when to engage");

            // And B cannot forge anything about A.
            int handled = _combat.Handled;

            Entity(_clientB, "char-a").RequestAttack(b.CombatantId.Value, string.Empty, 0, 99);

            yield return Tick(3);

            Assert.That(_combat.Handled, Is.EqualTo(handled),
                "FishNet refuses a request through an object the sender does not own");
            Assert.That(Stat(a, Atk), Is.EqualTo(aBefore + 40));
            Assert.That(Stat(b, Atk), Is.EqualTo(bBefore));
        }

        // ---- H: a quiet world costs nothing ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AThousandQuietTicksRecomputeNothingAndSendNothing()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = Admit("char-a", Connections()[0]);

            yield return Tick(2);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            Apply(character, Might, duration: 600f);

            yield return Tick();

            int computed = _statAuthority.Recomputations;
            int published = _statusAuthority.Published;
            long ticks = _world.Ticks;

            for (var i = 0; i < 1000; i++) _world.Tick(1f / 60f);

            Assert.That(_world.Ticks, Is.EqualTo(ticks + 1000), "the loop really did run");
            Assert.That(_statAuthority.Recomputations, Is.EqualTo(computed),
                "a stat block recomputed once a frame because a number is counting down");
            Assert.That(_statusAuthority.Published, Is.EqualTo(published),
                "and no status packet went out for it either");
            Assert.That(Stat(character, Atk), Is.EqualTo(80), "and it is still in force");
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        /// <summary>Runs the production world loop, the way the bootstrap runs it.</summary>
        private IEnumerator Tick(int frames = 1, float deltaSeconds = 0.05f)
        {
            for (var i = 0; i < frames; i++)
            {
                _world.Tick(deltaSeconds);

                yield return null;
            }
        }

        private WorldHudScreen Hud(NetworkManager client)
        {
            if (_huds.TryGetValue(client, out WorldHudScreen existing)) return existing;

            var host = new GameObject(client.name + " HUD");
            _created.Add(host);

            var hud = host.AddComponent<WorldHudScreen>();

            hud.UseStatusEffects(_effects);

            _huds[client] = hud;

            return hud;
        }

        private static int Stat(LivingCharacter character, string stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(new DefinitionId(stat),
                out int value), Is.True, stat + " was never computed");

            return value;
        }

        private void Apply(LivingCharacter character, string effect, float duration = 600f)
        {
            StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                new DefinitionId(effect), new DefinitionId("skill.buff"), _effects, duration);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        // ---- harness --------------------------------------------------------------------------------------------------

        private NetworkManager BuildManager(string name, bool listening, out GameObject host)
        {
            host = new GameObject(name);
            host.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            NetworkManager manager = host.AddComponent<NetworkManager>();

            manager.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            var transport = host.AddComponent<Tugboat>();
            transport.SetPort(_port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        private static NetworkObject Prefab(string path)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, "no prefab at " + path);

            return prefab.GetComponent<NetworkObject>();
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndOneClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_clientA.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientA.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);
        }

        private IEnumerator StartSecondClient()
        {
            Assert.That(_clientB.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientB.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 2);
        }

        private List<int> Connections()
        {
            var ids = new List<int>();

            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                ids.Add(pair.Key);
            }

            ids.Sort();

            return ids;
        }

        /// <summary>Enters the world the way the production composition does.</summary>
        /// <remarks>Through <c>WorldSimulation.Admit</c>: no ceilings are supplied and the
        /// stat authority computes the real ones before anything is published.</remarks>
        private LivingCharacter Admit(string character, int connection,
            PersistedItem[] items = null, int team = 1, int health = 200, int mana = 100)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, health, mana,
                    new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                    default, Attributes(), null, null, 1, items, 8);
            }

            WorldSpawnResult spawned = _world.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static CharacterNetworkEntity Entity(NetworkManager client, string character)
        {
            foreach (KeyValuePair<int, NetworkObject> pair in client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null && entity.Character.Value == character) return entity;
            }

            return null;
        }

        // ---- fixtures ---------------------------------------------------------------------------------------------------

        private static PersistedStat[] Attributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId(Str), 40),
                new PersistedStat(new DefinitionId(Vit), 10),
            };
        }

        private static StatModifier[] Flat(string stat, float value)
        {
            return new[]
            {
                new StatModifier(new DefinitionId(stat), StatModifierKind.Flat, value),
            };
        }

        private static PersistedItem Row(string instance, string item, int slot,
            int quantity = 1, int equipmentSlot = 0)
        {
            return new PersistedItem(new InstanceId(instance), new DefinitionId(item),
                quantity, slot, 0, equipmentSlot, 0, default, null, null);
        }

        private StatDefinition Stat(string id)
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_isPrimary\":false,"
                + "\"_minValue\":0,\"_maxValue\":999999}", definition);

            _created.Add(definition);
            _stats.Register(definition);

            return definition;
        }

        private DerivedStatFormulaDefinition Formula(string id, string derived, int constant,
            params StatTerm[] terms)
        {
            var definition = ScriptableObject.CreateInstance<DerivedStatFormulaDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_derivedStat\":{\"_value\":\""
                + derived + "\"},\"_constant\":" + constant + "}", definition);

            SetPrivate(definition, "_terms", terms);

            _created.Add(definition);

            return definition;
        }

        private StatusEffectDefinition Effect(string id, StatModifier[] modifiers)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)StatusEffectCategory.Buff
                + ",\"_durationSeconds\":0,\"_stackBehavior\":0,\"_maxStacks\":1}", definition);

            SetPrivate(definition, "_statModifiers", modifiers);

            _created.Add(definition);

            return definition;
        }

        private EquipmentDefinition Weapon(string id, StatModifier[] modifiers)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":false,"
                + "\"_maxStackSize\":1,\"_slot\":" + (int)EquipmentSlot.MainHand
                + ",\"_levelRequirement\":1}", definition);

            SetPrivate(definition, "_baseStatModifiers", modifiers);

            _created.Add(definition);

            return definition;
        }

        private MapDefinition Map(string id)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_movementRadius\":500}", map);

            _created.Add(map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            Assert.That(info, Is.Not.Null, "no field '" + field + "'");

            info.SetValue(target, value);
        }
    }
}

#endif
