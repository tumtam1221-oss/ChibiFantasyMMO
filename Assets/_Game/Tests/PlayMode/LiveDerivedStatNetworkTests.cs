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
    /// Live effective stats, over a real socket, consumed by real combat.
    /// </summary>
    /// <remarks>
    /// <b>The interesting claim is not that a number changed.</b> It is that the number the
    /// damage formula reads is the same one the buff changed. Until 18.8 the calculator ran
    /// at character creation and never again, so a live character's attack stat was frozen at
    /// whatever it was born with -- a sword changed nothing and a buff changed nothing, and
    /// no test would have noticed because nothing asked.
    ///
    /// <b>Damage is measured, never asserted at a literal.</b> Every test below compares one
    /// real attack against another real attack through <c>ServerCombatPipeline</c>. Writing
    /// the expected damage down would make these tests a copy of the formula rather than a
    /// check on it.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveDerivedStatNetworkTests
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

        private const string Might = "status.might";         // +40 flat attack
        private const string IronSkin = "status.ironskin";    // +30 flat defence
        private const string Fortitude = "status.fortitude";  // +100 flat max health
        private const string Sword = "item.sword";            // +25 flat attack

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
        private List<DerivedStatFormulaDefinition> _formulas;
        private CharacterStatAuthority _statAuthority;
        private CharacterStatusAuthority _statusAuthority;
        private CharacterCombatRequestHandler _combat;
        private CharacterReplicationService _replication;

        private readonly Dictionary<NetworkManager, WorldHudScreen> _huds =
            new Dictionary<NetworkManager, WorldHudScreen>();

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(55100, 57900);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("StatServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("StatClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("StatClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            _stats = new DefinitionRegistry<StatDefinition>();
            foreach (string id in new[] { Str, Vit, Atk, Def, MaxHp, MaxMp }) Stat(id);

            // FIXTURES: attack is strength, defence is vitality, and the two pools are the
            // usual constant-plus-attribute shape.
            _formulas = new List<DerivedStatFormulaDefinition>
            {
                Formula("f.atk", Atk, 0, new StatTerm(new DefinitionId(Str), 1, 1)),
                Formula("f.def", Def, 0, new StatTerm(new DefinitionId(Vit), 1, 1)),
                Formula("f.hp", MaxHp, 100, new StatTerm(new DefinitionId(Vit), 10, 1)),
                Formula("f.mp", MaxMp, 20, new StatTerm(new DefinitionId(Str), 2, 1)),
            };

            _effects = new DefinitionRegistry<StatusEffectDefinition>();
            _effects.Register(Effect(Might, StatusEffectCategory.Buff, Flat(Atk, 40f)));
            _effects.Register(Effect(IronSkin, StatusEffectCategory.Buff, Flat(Def, 30f)));
            _effects.Register(Effect(Fortitude, StatusEffectCategory.Buff, Flat(MaxHp, 100f)));

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

            // The reach is generous so range never masks the figure under test, and the
            // damage floor is one so a defended hit still lands something measurable.
            var pipeline = new ServerCombatPipeline(commands, monsters, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 50f),
                default, null, default, _effects);

            _combat = new CharacterCombatRequestHandler(pipeline, () =>
            {
                _replication.Synchronise();
                _statusAuthority.PublishChanged();
            });

            _replication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), _combat);

            _statusAuthority = new CharacterStatusAuthority(_players, _effects, _replication);
            _replication.UseStatus(_statusAuthority);

            _statAuthority = new CharacterStatAuthority(_players, _formulas, _stats, _effects,
                new EquipmentModifierResolver.Context(_items),
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));
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

        // ---- A: a buff moves the authoritative number -----------------------------------------------

        [UnityTest]
        public IEnumerator AStatusAppliedByTheServerChangesTheAuthoritativeStat()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            int before = Stat(character, Atk);

            Assert.That(before, Is.GreaterThan(0), "the base stat was computed at all");

            Apply(character, Might);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Stat(character, Atk), Is.EqualTo(before + 40));

            // And the buff is on the client's bar, through 18.7's owner-scoped path.
            _statusAuthority.PublishChanged();

            yield return Until(() => Entity(_clientA, "char-a").Status.Count > 0);

            Assert.That(Entity(_clientA, "char-a").Status.Effects[0].EffectId,
                Is.EqualTo(Might));
        }

        // ---- B: and the fight reads it ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CombatDamageFollowsTheRecomputedAttackStat()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter attacker = EnterWorld("char-a", connections[0]);
            LivingCharacter victim = EnterWorld("char-b", connections[1], team: 2);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-b") != null);

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.IsAccepted, Is.True,
                "the unbuffed swing was refused: " + _combat.LastResult);

            int plain = _combat.LastResult.Damage;

            Assert.That(plain, Is.GreaterThan(0), "the unbuffed swing landed something");

            // The server buffs them and recomputes through the production path.
            Apply(attacker, Might);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1));

            int handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 2);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.IsAccepted, Is.True);

            int buffed = _combat.LastResult.Damage;

            Assert.That(buffed, Is.GreaterThan(plain),
                "the damage formula read the frozen creation-time stat until 18.8");

            // Expired, and the damage comes back down.
            attacker.Status.Tick(9999f);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1));

            handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 3);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.Damage, Is.EqualTo(plain),
                "the buff left nothing behind");
        }

        // ---- defence, from the other side ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADefensiveBuffOnTheTargetReducesTheDamageItTakes()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter attacker = EnterWorld("char-a", connections[0]);
            LivingCharacter victim = EnterWorld("char-b", connections[1], team: 2);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-b") != null);

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.IsAccepted, Is.True,
                "the swing was refused: " + _combat.LastResult);

            int undefended = _combat.LastResult.Damage;

            Assert.That(undefended, Is.GreaterThan(1), "there is room for defence to matter");

            // The defender is buffed, not the attacker.
            Apply(victim, IronSkin);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Stat(victim, Def), Is.GreaterThan(0));

            int handled = _combat.Handled;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, string.Empty,
                0, 2);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.Damage, Is.LessThan(undefended),
                "the existing formula consumed the target's effective defence");
        }

        // ---- D: a moved ceiling reaches the client ------------------------------------------------------

        [UnityTest]
        public IEnumerator AMaxHealthBuffReplicatesAndTheHudFollows()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => hud.Current.MaxHealth > 0);

            int ceiling = character.Combatant.MaxHealth;

            Assert.That(hud.Current.MaxHealth, Is.EqualTo(ceiling));

            Apply(character, Fortitude);

            _statAuthority.RefreshAll();
            _replication.Synchronise();

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(ceiling + 100));

            yield return Until(() => hud.Current.MaxHealth == ceiling + 100);

            Assert.That(Entity(_clientA, "char-a").MaxHealth, Is.EqualTo(ceiling + 100),
                "a maximum published once at spawn would never have moved");
            Assert.That(hud.Current.MaxHealth, Is.EqualTo(ceiling + 100));

            // Filled to the new ceiling, then the buff goes.
            character.Combatant.ApplyHealthDelta(9999);

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(ceiling + 100));

            character.Status.Remove(new DefinitionId(Fortitude));

            _statAuthority.RefreshAll();
            _replication.Synchronise();

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(ceiling));
            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(ceiling),
                "losing a maximum-health buff must never leave health above the ceiling");

            yield return Until(() => hud.Current.MaxHealth == ceiling
                && hud.Current.Health == ceiling);

            Assert.That(hud.Current.HealthLabel, Is.EqualTo(ceiling + " / " + ceiling));
        }

        [UnityTest]
        public IEnumerator TheManaPoolIsReplicatedNowThatTheServerComputesOne()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => hud.Current.MaxMana > 0);

            Assert.That(Entity(_clientA, "char-a").MaxMana,
                Is.EqualTo(character.Combatant.Limits.MaxMana));
            Assert.That(hud.Current.MaxMana, Is.EqualTo(character.Combatant.Limits.MaxMana));
            Assert.That(hud.Current.ManaLabel, Is.Not.Empty,
                "18.5 drew no mana because nothing computed a ceiling; now something does");
        }

        // ---- E: equipment and status together ---------------------------------------------------------

        [UnityTest]
        public IEnumerator EquipmentAndStatusModifiersComposeAndComeApartIndependently()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            int bare = Stat(character, Atk);

            // Worn through the existing equipment state, not through a second path.
            var sword = new EquipmentInstance(new InstanceId("item-1"),
                new DefinitionId(Sword), character.Owner);

            Assert.That(character.Equipment.Restore(EquipmentSlot.MainHand, sword), Is.True);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1),
                "equipping is a change of inputs");
            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 25));

            // And a buff on top.
            Apply(character, Might);

            _statAuthority.RefreshAll();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 25 + 40),
                "both sources are summed by the one calculator");

            // Removing only the status leaves the sword.
            character.Status.Remove(new DefinitionId(Might));

            _statAuthority.RefreshAll();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 25),
                "taking off a buff must not take off a weapon");

            // And unequipping through the existing equipment service leaves nothing.
            EquipResult removed = EquipmentService.Unequip(character.Inventory,
                character.Equipment, EquipmentSlot.MainHand,
                new EquipmentService.Context(_items, character.Domain.Progression.Level));

            Assert.That(removed.IsAccepted, Is.True, removed.ToString());

            _statAuthority.RefreshAll();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare));
        }

        // ---- F: death ------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AStatusAndItsModifierBothSurviveDeath()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            int bare = Stat(character, Atk);

            Apply(character, Might, duration: 30f);

            _statAuthority.RefreshAll();

            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 40));

            character.Combatant.ApplyHealthDelta(-character.Combatant.CurrentHealth);

            _replication.Synchronise();

            yield return Until(() => !Entity(_clientA, "char-a").IsAlive);

            // 18.7's policy, unchanged by 18.8.
            Assert.That(character.Status.Has(new DefinitionId(Might)), Is.True);
            Assert.That(_statAuthority.RefreshAll(), Is.Zero, "dying changed no modifier");
            Assert.That(Stat(character, Atk), Is.EqualTo(bare + 40));

            // And it still expires on the server's clock.
            character.Status.Tick(31f);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Stat(character, Atk), Is.EqualTo(bare));
        }

        // ---- G: reconnect ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReconnectRestoresEquipmentButNotTemporaryStatus()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            LivingCharacter character = EnterWorld("char-a", connection, new[]
            {
                Row("item-1", Sword, -1, equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            int withSword = Stat(character, Atk);

            Apply(character, Might);

            _statAuthority.RefreshAll();

            Assert.That(Stat(character, Atk), Is.EqualTo(withSword + 40));

            Assert.That(_players.Despawn(connection).IsOk, Is.True);

            _statAuthority.Forget(character.Character);
            _statusAuthority.Forget(character.Character);
            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") == null);

            LivingCharacter returned = EnterWorld("char-a", connection);

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "temporary status is server memory and is not persisted");
            Assert.That(_statAuthority.Force(returned), Is.True);

            Assert.That(returned.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.True,
                "the sword came back, because equipment is persisted");
            Assert.That(Stat(returned, Atk), Is.EqualTo(withSword),
                "the sword's bonus survived and the buff did not");
        }

        // ---- H: two clients ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BuffingOnePlayerLeavesTheOtherExactlyAsTheyWere()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter a = EnterWorld("char-a", connections[0]);
            LivingCharacter b = EnterWorld("char-b", connections[1], team: 2);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientB, "char-b") != null
                && Entity(_clientA, "char-b") != null);

            int aBefore = Stat(a, Atk);
            int bBefore = Stat(b, Atk);

            Apply(a, Might);

            Assert.That(_statAuthority.RefreshAll(), Is.EqualTo(1),
                "only one character's inputs moved");

            Assert.That(Stat(a, Atk), Is.EqualTo(aBefore + 40));
            Assert.That(Stat(b, Atk), Is.EqualTo(bBefore), "B was not buffed by A's buff");
            Assert.That(b.Status.ActiveCount, Is.Zero);

            _statusAuthority.PublishChanged();

            yield return Until(() => Entity(_clientA, "char-a").Status.Count > 0);

            // 18.7's privacy, still holding: B is told nothing about A's buff, and the
            // public health values remain correct for both.
            Assert.That(Entity(_clientB, "char-a").Status.Count, Is.Zero,
                "a buff tells an opponent when to engage");
            Assert.That(Entity(_clientB, "char-b").Status.Count, Is.Zero);

            Assert.That(Entity(_clientB, "char-a").MaxHealth,
                Is.EqualTo(a.Combatant.MaxHealth));
            Assert.That(Entity(_clientA, "char-b").MaxHealth,
                Is.EqualTo(b.Combatant.MaxHealth));

            // And B cannot forge anything about A: the only combat message names a target,
            // a skill, a rank and a sequence, and arrives as whoever owns the object.
            int handled = _combat.Handled;

            Entity(_clientB, "char-a").RequestAttack(b.CombatantId.Value, string.Empty, 0, 99);

            yield return Until(() => false, 30);

            Assert.That(_combat.Handled, Is.EqualTo(handled),
                "FishNet refuses a request through an object the sender does not own");
            Assert.That(Stat(a, Atk), Is.EqualTo(aBefore + 40));
            Assert.That(Stat(b, Atk), Is.EqualTo(bBefore));
        }

        // ---- no work without a reason ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TickingAWorldFullOfCountdownsRecomputesNothing()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            Apply(character, Might, duration: 60f);

            _statAuthority.RefreshAll();

            // Applying it is worth one recomputation and one packet. What must cost nothing
            // is every frame after that.
            _statusAuthority.PublishChanged();

            int computed = _statAuthority.Recomputations;
            int published = _statusAuthority.Published;

            for (var i = 0; i < 120; i++)
            {
                _statusAuthority.Tick(1f / 60f);
                _statAuthority.RefreshAll();
            }

            Assert.That(_statAuthority.Recomputations, Is.EqualTo(computed),
                "a stat block recomputed once a frame because a number is counting down");
            Assert.That(_statusAuthority.Published, Is.EqualTo(published),
                "and no packet went out for it either");
        }

        // ---- helpers ------------------------------------------------------------------------------------------

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

        /// <summary>
        /// Applies a status the way the server does: through the one service.
        /// </summary>
        /// <remarks>A finite default, because these fixtures are authored with no duration
        /// of their own and an indefinite effect cannot be ticked out -- a test that expired
        /// one would wait for ever and report nothing.</remarks>
        private void Apply(LivingCharacter character, string effect, float duration = 60f)
        {
            StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                new DefinitionId(effect), new DefinitionId("skill.buff"), _effects, duration);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        // ---- harness ---------------------------------------------------------------------------------------------

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

        private LivingCharacter EnterWorld(string character, int connection,
            PersistedItem[] items = null, int team = 1)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, 200, 40,
                    new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                    default, Attributes(), null, null, 1, items, 8);
            }

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                // The persisted ceilings, as a real composition supplies them. Spawning
                // with none would clamp the loaded health to zero before the authority
                // ever gets to compute the authored ones -- which reads as a character
                // who arrived dead.
                new ResourceLimits(200, 40), new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            // What a live world does the moment a character exists: compute their stats
            // once, because a combatant is constructed with none.
            Assert.That(_statAuthority.Force(spawned.Character), Is.True);

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

        // ---- fixtures ----------------------------------------------------------------------------------------------

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

        private StatusEffectDefinition Effect(string id, StatusEffectCategory category,
            StatModifier[] modifiers)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)category + ",\"_durationSeconds\":0,"
                + "\"_stackBehavior\":0,\"_maxStacks\":1}", definition);

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
