// Editor-only for the same reason ShippedWorldServerTests is: it loads the committed
// production scene, because the point is to prove the SHIPPED composition resolves magic.
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
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A spell resolved by the world the shipped scene composed.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here builds a world or computes a damage figure.</b> The scene composes
    /// itself from its own catalogue; every number below comes back out of the production
    /// combat pipeline. A test that calculated the expected damage itself would be checking
    /// its own arithmetic against the server's, and would keep passing if both were wrong
    /// in the same way -- so the assertions are about how damage *moves* when a stat moves.
    ///
    /// <b>The physical numbers are the ones pinned to literals.</b> Adding magic is exactly
    /// the change that could route an ordinary sword swing through magic defence, so the
    /// sword is measured against a constant rather than against anything computed here.
    ///
    /// <b>No test calls RefreshAll.</b> Every recomputation below is the world noticing.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldMagicCombatTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string MagicBolt = "skill.magic_bolt";
        private const string Slime = "monster.training_slime";

        private static readonly DefinitionId Matk = new DefinitionId("stat.matk");
        private static readonly DefinitionId Mdef = new DefinitionId("stat.mdef");
        private static readonly DefinitionId Atk = new DefinitionId("stat.atk");
        private static readonly DefinitionId Def = new DefinitionId("stat.def");

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

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
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

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private NetworkManager _server;
        private FakeStore _store;

        private GameObject _clientObject;
        private NetworkManager _client;

        private readonly List<Object> _created = new List<Object>();
        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_client != null) _client.ClientManager.StopConnection();
            if (_clientObject != null) Object.DestroyImmediate(_clientObject);

            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
            }

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

        // ---- A: a spell that actually lands ----------------------------------------------------

        [UnityTest]
        public IEnumerator TheProductionSpellDamagesACharacterThroughTheShippedPipeline()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int health = victim.Combatant.CurrentHealth;

            ServerCombatResult cast = Cast(caster, victim);

            Assert.That(cast.IsAccepted, Is.True,
                "the shipped world refused its own spell: " + cast.SkillRejection);
            Assert.That(cast.Damage, Is.GreaterThan(0), "a spell that does nothing");
            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(health - cast.Damage));
            Assert.That(cast.TargetHealthAfter, Is.LessThan(cast.TargetHealthBefore));

            // And it is genuinely a different exchange from a sword blow.
            Assert.That(cast.Damage, Is.Not.EqualTo(Attack(caster, victim).Damage),
                "magic and physical resolved to the same number; nothing here can "
                + "distinguish them");
        }

        [UnityTest]
        public IEnumerator ASpellReachesARealClientAsHealthTheServerDecided()
        {
            yield return LoadWorld();

            Seed("char-a", skills: true);

            yield return ConnectAClient();

            _client.ClientManager.Broadcast(Join("char-a"));

            yield return Until(() => Entity("char-a") != null && Entity("char-a").MaxHealth > 0);

            Assert.That(_bootstrap.Characters.TryGetByCharacter(new CharacterId("char-a"),
                out LivingCharacter caster), Is.True);

            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int before = victim.Combatant.CurrentHealth;

            ServerCombatResult cast = Cast(caster, victim);

            Assert.That(cast.IsAccepted, Is.True, cast.SkillRejection.ToString());

            yield return Tick(3);

            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(before - cast.Damage));
            Assert.That(Entity("char-a"), Is.Not.Null, "the caster stopped being replicated");
        }

        // ---- B: magic defence ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MagicDefenceReducesRealSpellDamageAndGivesItBackWhenItExpires()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int unwarded = Measure(caster, victim);

            int mdefBefore = Stat(victim, Mdef);

            Apply(victim, Effect("status.test.ward", Mdef, 6f), 30f);

            yield return Tick();

            Assert.That(Stat(victim, Mdef), Is.EqualTo(mdefBefore + 6),
                "no test called RefreshAll; the shipped loop noticed");

            int warded = Measure(caster, victim);

            Assert.That(warded, Is.LessThan(unwarded),
                "magic defence changed and the damage did not");
            Assert.That(unwarded - warded, Is.EqualTo(6),
                "the whole of the ward should have been subtracted, once");

            // Let it run out. Nothing removes it by hand.
            victim.Status.Tick(31f);

            yield return Tick();

            Assert.That(Stat(victim, Mdef), Is.EqualTo(mdefBefore));
            Assert.That(Measure(caster, victim), Is.EqualTo(unwarded),
                "an expired ward went on defending");
        }

        [UnityTest]
        public IEnumerator MagicDefenceLeavesAnOrdinarySwordBlowCompletelyAlone()
        {
            yield return LoadWorld();

            LivingCharacter attacker = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int plain = Attack(attacker, victim).Damage;

            Apply(victim, Effect("status.test.ward", Mdef, 40f), 60f);

            yield return Tick();

            Assert.That(Stat(victim, Mdef), Is.EqualTo(3 + 40));

            Assert.That(Attack(attacker, victim).Damage, Is.EqualTo(plain),
                "magic defence answered a sword");
        }

        // ---- C: magic attack -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MagicAttackRaisesRealSpellDamageAndGivesItBack()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int plain = Measure(caster, victim);

            int matkBefore = Stat(caster, Matk);

            Apply(caster, Effect("status.test.focus", Matk, 9f), 30f);

            yield return Tick();

            Assert.That(Stat(caster, Matk), Is.EqualTo(matkBefore + 9));

            int focused = Measure(caster, victim);

            Assert.That(focused, Is.EqualTo(plain + 9),
                "the spell did not consume the caster's live magic attack");

            caster.Status.Tick(31f);

            yield return Tick();

            Assert.That(Stat(caster, Matk), Is.EqualTo(matkBefore));
            Assert.That(Measure(caster, victim), Is.EqualTo(plain));
        }

        [UnityTest]
        public IEnumerator APhysicalAttackBuffDoesNothingForASpell()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int plain = Measure(caster, victim);

            Apply(caster, Effect("status.test.rage", Atk, 50f), 60f);

            yield return Tick();

            Assert.That(Stat(caster, Atk), Is.EqualTo(25 + 50));

            Assert.That(Measure(caster, victim), Is.EqualTo(plain),
                "physical attack power reached a spell");
        }

        // ---- D: mana ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASpellIsPaidForOutOfAuthoritativeManaAndRefusedWithoutIt()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int mana = caster.Domain.Resources.CurrentMana;

            Assert.That(mana, Is.GreaterThan(0));
            Assert.That(caster.Combatant.Limits.MaxMana, Is.EqualTo(20 + 3 * 5),
                "the mana ceiling is not the live derived one");

            Assert.That(Cast(caster, victim).IsAccepted, Is.True);

            int spent = mana - caster.Domain.Resources.CurrentMana;

            Assert.That(spent, Is.GreaterThan(0), "magic was free");

            // Drain the pool and try again.
            caster.Domain.Resources.SetMana(0, caster.Combatant.Limits);

            Assert.That(caster.Domain.Resources.CurrentMana, Is.Zero);

            int health = victim.Combatant.CurrentHealth;

            ServerCombatResult broke = Cast(caster, victim);

            Assert.That(broke.IsAccepted, Is.False, "a spell cast on no mana");
            Assert.That(broke.SkillRejection, Is.EqualTo(SkillUseRejection.InsufficientResource));
            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(health),
                "a refused spell still hurt somebody");
            Assert.That(caster.Domain.Resources.CurrentMana, Is.Zero,
                "a refused spell must not go negative");
        }

        // ---- E: silence ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SilenceStopsTheSpellAndCostsTheCasterNothing()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int mana = caster.Domain.Resources.CurrentMana;
            int health = victim.Combatant.CurrentHealth;

            Apply(caster, Silence("status.test.silence"), 30f);

            yield return Tick();

            ServerCombatResult refused = Cast(caster, victim);

            Assert.That(refused.IsAccepted, Is.False, "a silenced caster cast a spell");
            Assert.That(refused.SkillRejection, Is.EqualTo(SkillUseRejection.Silenced));

            Assert.That(caster.Domain.Resources.CurrentMana, Is.EqualTo(mana), "mana was spent");
            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(health), "damage was done");
            Assert.That(refused.Damage, Is.Zero);

            // A sword is not a spell: silence must not disarm the caster entirely.
            Assert.That(Attack(caster, victim).IsAccepted, Is.True,
                "silence stopped a physical attack");

            // And when it lifts, the spell works again -- no cooldown was burned.
            caster.Status.Tick(31f);

            yield return Tick();

            Assert.That(Cast(caster, victim).IsAccepted, Is.True,
                "a refused spell consumed its cooldown");
        }

        // ---- F: the physical regression -------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnOrdinaryAttackDealsExactlyWhatItDealtBeforeMagicExisted()
        {
            yield return LoadWorld();

            LivingCharacter attacker = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            // Pinned to the constant, not to anything this test computes:
            // ATK 25 - DEF 8 = 17, exactly as 18.8C shipped it.
            Assert.That(Stat(attacker, Atk), Is.EqualTo(25));
            Assert.That(Stat(victim, Def), Is.EqualTo(8));

            ServerCombatResult blow = Attack(attacker, victim);

            Assert.That(blow.IsAccepted, Is.True, blow.ToString());
            Assert.That(blow.Damage, Is.EqualTo(17), "physical damage moved");
        }

        // ---- G: a monster ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheProductionMonsterCanBeKilledWithMagicAndResistsItWithItsOwnStat()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);

            LivingMonster slime = Spawn();

            Assert.That(Monsters.TryResolve(slime.Instance,
                out ICombatant target), Is.True);

            Assert.That(target.TryGetCombatStat(Mdef, out int mdef), Is.True,
                "the production monster carries no magic defence");
            Assert.That(target.TryGetCombatStat(Def, out int def), Is.True);
            Assert.That(mdef, Is.Not.EqualTo(def));

            int health = target.CurrentHealth;

            ServerCombatResult cast = CastAt(caster, slime.Instance);

            Assert.That(cast.IsAccepted, Is.True,
                "a spell could not reach a monster: " + cast.SkillRejection);
            Assert.That(cast.Damage, Is.GreaterThan(0));
            Assert.That(target.CurrentHealth, Is.EqualTo(health - cast.Damage));

            // The monster's own magic defence was the thing subtracted, not its armour.
            ServerCombatResult physical = Attack(caster, slime.Instance);

            Assert.That(physical.IsAccepted, Is.True);
            Assert.That(physical.Damage - cast.Damage, Is.EqualTo(mdef - def
                + (Stat(caster, Atk) - (4 + Stat(caster, Matk)))),
                "the two exchanges did not differ by exactly their stats");

            // And killing it with magic still goes through the ordinary defeat path.
            for (var i = 0; i < 20 && target.CurrentHealth > 0; i++)
            {
                Recharge(caster);
                Ready(caster);

                ServerCombatResult blow = CastAt(caster, slime.Instance);

                if (!blow.IsAccepted) break;

                if (blow.TargetDefeated)
                {
                    Assert.That(target.CurrentHealth, Is.Zero);

                    yield break;
                }
            }

            Assert.That(target.CurrentHealth, Is.Zero, "magic never finished the monster");
        }

        // ---- H: two clients ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OneClientCannotCastThroughAnotherCharacter()
        {
            yield return LoadWorld();

            LivingCharacter a = Admit("char-a", 1);
            LivingCharacter b = Admit("char-b", 2, team: 2);

            int health = b.Combatant.CurrentHealth;
            int manaA = a.Domain.Resources.CurrentMana;

            // B's connection, claiming to be A. The identity is the connection's, never the
            // claim's, so this must be refused before a single stat is read.
            ServerCombatResult forged = Combat.Execute(2,
                new CombatCommand(a.Character, b.CombatantId, new DefinitionId(MagicBolt),
                    1, ++_sequence));

            Assert.That(forged.IsAccepted, Is.False, "a client cast through someone else");
            Assert.That(b.Combatant.CurrentHealth, Is.EqualTo(health));
            Assert.That(a.Domain.Resources.CurrentMana, Is.EqualTo(manaA),
                "the forged cast spent the victim-of-forgery's mana");

            // And a command naming a skill nobody learned is refused rather than invented.
            ServerCombatResult unknown = Combat.Execute(1,
                new CombatCommand(a.Character, b.CombatantId,
                    new DefinitionId("skill.does_not_exist"), 1, ++_sequence));

            Assert.That(unknown.IsAccepted, Is.False);
            Assert.That(b.Combatant.CurrentHealth, Is.EqualTo(health));
        }

        [UnityTest]
        public IEnumerator ARankNobodyLearnedIsRefusedRatherThanScaledUp()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int health = victim.Combatant.CurrentHealth;

            // The character learned rank 1. Rank 3 hits harder and is not theirs to use.
            ServerCombatResult overreach = Combat.Execute(1,
                new CombatCommand(caster.Character, victim.CombatantId,
                    new DefinitionId(MagicBolt), 3, ++_sequence));

            Assert.That(overreach.IsAccepted, Is.False, "a client chose its own rank");
            Assert.That(overreach.SkillRejection,
                Is.EqualTo(SkillUseRejection.RankNotAvailable));
            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(health));
        }

        // ---- I: the same tick ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OneCommandNeverResolvesAgainstStateAnotherAlreadyChanged()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int plain = Measure(caster, victim);

            // What the shipped world guarantees, and what it does not. The simulation
            // settles *after* each command it handles, so two commands in one frame cannot
            // disagree about the world. It does not promise that a mutation made from
            // outside the command path is visible before the next settle or tick -- so the
            // honest test is command-to-command, with no frame in between.
            Apply(victim, Effect("status.test.ward", Mdef, 5f), 60f);

            int frame = Time.frameCount;

            Recharge(caster);
            Ready(caster);
            Submit(caster, victim);

            Recharge(caster);
            Ready(caster);
            Submit(caster, victim);

            var handler = (CharacterCombatRequestHandler)Requests;

            Assert.That(handler.LastResult.IsAccepted, Is.True,
                handler.LastResult.SkillRejection.ToString());

            Assert.That(Time.frameCount, Is.EqualTo(frame),
                "the two commands were not in the same frame, so this proves nothing");

            Assert.That(handler.LastResult.Damage, Is.EqualTo(plain - 5),
                "a second command in the same frame resolved against stale magic defence");

            // And by the next tick everything agrees, with nothing having called RefreshAll.
            yield return Tick();

            Assert.That(Stat(victim, Mdef), Is.EqualTo(3 + 5));
            Assert.That(Measure(caster, victim), Is.EqualTo(plain - 5));

            // Expired, the restored value is live again by the following tick.
            victim.Status.Tick(61f);

            yield return Tick();

            Assert.That(Stat(victim, Mdef), Is.EqualTo(3));
            Assert.That(Measure(caster, victim), Is.EqualTo(plain));
        }

        // ---- J: coming back ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReconnectedCasterKeepsItsSkillAndLosesOnlyTheTemporaryBuff()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            int plain = Measure(caster, victim);

            Apply(caster, Effect("status.test.focus", Matk, 9f), 300f);

            yield return Tick();

            Assert.That(Measure(caster, victim), Is.EqualTo(plain + 9));

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "a temporary buff survived a reconnect");

            Assert.That(Stat(returned, Matk), Is.EqualTo(11),
                "magic attack came back changed");

            Recharge(returned);
            Ready(returned);

            ServerCombatResult after = Cast(returned, victim);

            Assert.That(after.IsAccepted, Is.True,
                "the returned character forgot its skill: " + after.SkillRejection);
            Assert.That(after.Damage, Is.EqualTo(plain),
                "the buff was still being counted after a reconnect");
        }

        // ---- harness --------------------------------------------------------------------------------------

        private IEnumerator LoadWorld()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];
            _server = _bootstrap.GetComponent<NetworkManager>();

            _bootstrap.StopServer();
            _bootstrap.Compose(new AlwaysAdmits(), default, _store, null);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            yield return null;
        }

        private IEnumerator ConnectAClient()
        {
            _clientObject = new GameObject("MagicClient");
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
            transport.SetPort(Port());
            transport.SetClientAddress("127.0.0.1");

            _clientObject.SetActive(true);

            Assert.That(_client.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _client.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);
        }

        private static WorldJoinRequestMessage Join(string who)
        {
            return new WorldJoinRequestMessage
            {
                Token = who,
                ClientVersion = "1.0.0",
                ProtocolVersion = "1.0.0",
                ContentVersion = "1.0.0",
            };
        }

        private ushort Port()
        {
            return (ushort)typeof(WorldServerBootstrap)
                .GetField("_port", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap);
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

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        /// <summary>Puts a saved character in the store, with the spell already learned.</summary>
        private void Seed(string character, bool skills = true, int health = 104, int mana = 35)
        {
            string session = "session-" + character;

            if (_store.Rows.ContainsKey(session)) return;

            // Learned through the ordinary persisted-skill path. Nothing grants it at
            // runtime and no class rule is bypassed: the skill belongs to no class.
            PersistedSkill[] learned = skills
                ? new[] { new PersistedSkill(new DefinitionId(MagicBolt), 1) }
                : new PersistedSkill[0];

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 1, 1, 0, health, mana,
                new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                default, StarterAttributes(), null, learned, 1);
        }

        private LivingCharacter Admit(string character, int connection, int team = 1)
        {
            Seed(character);

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

        private static PersistedStat[] StarterAttributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId("stat.str"), 10),
                new PersistedStat(new DefinitionId("stat.vit"), 8),
                new PersistedStat(new DefinitionId("stat.int"), 3),
            };
        }

        private LivingMonster Spawn()
        {
            Assert.That(Monsters.AddSpawnPoint(new MonsterSpawnPoint(
                new DefinitionId(Slime), default, 0f, 1, 0f, new DefinitionId(StarterMap))),
                Is.True);

            Monsters.PopulateAll();

            IReadOnlyList<LivingMonster> alive = Monsters.All();

            Assert.That(alive, Is.Not.Empty, "the production monster would not spawn");

            return alive[0];
        }

        /// <summary>
        /// The damage one legal cast does, right now.
        /// </summary>
        /// <remarks>
        /// <b>Mana and cooldown are restored first, and acceptance is asserted.</b> A spell
        /// refused for cooldown reports zero damage, and zero damage read as a measurement
        /// would look exactly like a defence that absorbed everything -- so a test comparing
        /// two casts could "prove" magic defence works while actually proving the second
        /// cast never happened. Neither resource is bypassed: both are restored through the
        /// ordinary state they live in, and the cast itself goes through the same pipeline
        /// as any other.
        /// </remarks>
        private int Measure(LivingCharacter caster, LivingCharacter victim)
        {
            Recharge(caster);
            Ready(caster);

            ServerCombatResult cast = Cast(caster, victim);

            Assert.That(cast.IsAccepted, Is.True,
                "a measured cast was refused: " + cast.SkillRejection);
            Assert.That(cast.Damage, Is.GreaterThan(0), "a measured cast did nothing");

            return cast.Damage;
        }

        /// <summary>Casts through the sink a client's own RPC lands in.</summary>
        private void Submit(LivingCharacter caster, LivingCharacter victim)
        {
            Requests.Submit(caster.ConnectionId, victim.CombatantId,
                new DefinitionId(MagicBolt), 1, ++_sequence);
        }

        /// <summary>Casts the production spell through the shipped combat pipeline.</summary>
        private ServerCombatResult Cast(LivingCharacter caster, LivingCharacter target)
        {
            return CastAt(caster, target.CombatantId);
        }

        private ServerCombatResult CastAt(LivingCharacter caster, InstanceId target)
        {
            return Combat.Execute(caster.ConnectionId,
                new CombatCommand(caster.Character, target, new DefinitionId(MagicBolt), 1,
                    ++_sequence));
        }

        private ServerCombatResult Attack(LivingCharacter attacker, LivingCharacter target)
        {
            return Attack(attacker, target.CombatantId);
        }

        private ServerCombatResult Attack(LivingCharacter attacker, InstanceId target)
        {
            return Combat.Execute(attacker.ConnectionId,
                new CombatCommand(attacker.Character, target, default, 0, ++_sequence));
        }

        /// <summary>Fills the caster back up, so a repeat cast is not refused for mana.</summary>
        private static void Recharge(LivingCharacter caster)
        {
            caster.Domain.Resources.SetMana(caster.Combatant.Limits.MaxMana,
                caster.Combatant.Limits);
        }

        /// <summary>Runs the cooldown out, so a repeat cast is not refused for timing.</summary>
        private void Ready(LivingCharacter caster)
        {
            Combat.Tick(30f);
        }

        /// <summary>
        /// The world's own combat pipeline, status authority and monster runtime.
        /// </summary>
        /// <remarks><see cref="WorldSimulation"/> keeps its collaborators private, which is
        /// right: nothing in the game reaches past it. A test that needs to ask the shipped
        /// pipeline a question reaches them by name rather than by building its own, because
        /// building its own is exactly what would stop this proving anything.</remarks>
        private ServerCombatPipeline Combat => Field<ServerCombatPipeline>("_combat");

        /// <summary>
        /// The sink a client's own RPC lands in.
        /// </summary>
        /// <remarks>Above <see cref="Combat"/>, and the difference matters: this is the seam
        /// the shipped world wired to settle the simulation before resolving, so it is the
        /// only place a same-tick guarantee can honestly be tested.</remarks>
        private ICharacterCombatRequestSink Requests
        {
            get
            {
                var sink = (ICharacterCombatRequestSink)typeof(CharacterReplicationService)
                    .GetField("_combat", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
                    .GetValue(_bootstrap.Replication);

                Assert.That(sink, Is.Not.Null, "the scene wired no combat request sink");

                return sink;
            }
        }

        private MonsterWorldRuntime Monsters => Field<MonsterWorldRuntime>("_monsters");

        private CharacterStatAuthority Stats => Field<CharacterStatAuthority>("_stats");

        private T Field<T>(string name) where T : class
        {
            var value = (T)typeof(WorldSimulation)
                .GetField(name, System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap.Simulation);

            Assert.That(value, Is.Not.Null, "the scene composed no " + name);

            return value;
        }

        private static int Stat(LivingCharacter character, DefinitionId stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(stat, out int value), Is.True,
                stat + " was never computed");

            return value;
        }

        /// <summary>
        /// Puts an authored effect on somebody through the real status service.
        /// </summary>
        /// <remarks>The effects are authored here rather than in the catalogue because the
        /// shipped world has no status content yet, and inventing production content to make
        /// a test pass is what the 18.8B gate refused to do.</remarks>
        private void Apply(LivingCharacter target, StatusEffectDefinition effect, float seconds)
        {
            var registry = new DefinitionRegistry<StatusEffectDefinition>();
            registry.Register(effect);

            // Two authorities resolve an effect id and both must see the same content: the
            // stat authority to work out what it modifies, and the combat pipeline's
            // validator to work out whether it silences. Pointing one at content the other
            // does not have is how a silenced caster gets to cast.
            typeof(CharacterStatAuthority)
                .GetField("_effects", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(Stats, registry);

            typeof(ServerCombatPipeline)
                .GetField("_statusEffects", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(Combat, registry);

            StatusApplyResult applied = StatusEffectService.TryApply(target.Status, effect.Id,
                new DefinitionId("test.source"), registry, seconds);

            Assert.That(applied.IsAccepted, Is.True, applied.Reason.ToString());
        }

        private StatusEffectDefinition Effect(string id, DefinitionId stat, float amount)
        {
            StatusEffectDefinition definition = Blank(id, StatusEffectCategory.Buff);

            typeof(StatusEffectDefinition).GetField("_statModifiers",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(definition, new[]
                {
                    new StatModifier(stat, StatModifierKind.Flat, amount),
                });

            return definition;
        }

        private StatusEffectDefinition Silence(string id)
        {
            StatusEffectDefinition definition = Blank(id, StatusEffectCategory.Debuff);

            typeof(StatusEffectDefinition).GetField("_controlEffect",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(definition, ControlEffectType.Silence);

            return definition;
        }

        private StatusEffectDefinition Blank(string id, StatusEffectCategory category)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            _created.Add(definition);

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)category
                + ",\"_durationSeconds\":0,\"_stackBehavior\":0,\"_maxStacks\":1}", definition);

            return definition;
        }
    }
}

#endif
