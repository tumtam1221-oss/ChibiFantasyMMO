// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world grants this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
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
    /// Eating a Devil Fruit in the world the shipped scene composed.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here grants a fruit directly.</b> Every activation below happens by
    /// putting the authored item in a bag and sending the ordinary inventory request a
    /// client can send. A test that called <c>Activate</c> itself would prove the state
    /// object works and nothing about whether a player can ever get one.
    ///
    /// <b>The backend is the one substitution.</b> A character store is a network service
    /// and the shipped <c>Compose</c> already takes one, so a world can be stood up without
    /// HTTP. What the store round-trips is the real persisted row, fruit id and all.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldDevilFruitTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Darkness = "devil_fruit.darkness";
        private const string DarknessItem = "item.devil_fruit.darkness";
        private const string DarkShroud = "skill.dark_shroud";
        private const string MagicBolt = "skill.magic_bolt";

        private static readonly DefinitionId Mdef = new DefinitionId("stat.mdef");
        private static readonly DefinitionId Matk = new DefinitionId("stat.matk");

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public int Saves;

            /// <summary>When set, every save is refused, as a backend outage would.</summary>
            public bool Broken;

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Saves++;

                if (Broken)
                {
                    return CharacterPersistenceResult.Failed(
                        CharacterPersistenceFailure.Unreachable);
                }

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
        }

        // ---- A: eating one ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator UsingTheAuthoredItemGrantsTheFruitAndSpendsTheItem()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            Give(character, DarknessItem);

            Assert.That(character.DevilFruit.HasActiveFruit, Is.False);
            Assert.That(Held(character, DarknessItem), Is.EqualTo(1));

            // The ordinary inventory request. Nothing names a fruit.
            CharacterInventoryResult used = Use(character, slot: 0);

            Assert.That(used.IsAccepted, Is.True, "the shipped world refused its own item: "
                + used);

            Assert.That(character.DevilFruit.HasActiveFruit, Is.True, "no fruit was granted");
            Assert.That(character.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));

            Assert.That(Held(character, DarknessItem), Is.Zero,
                "the fruit was eaten and the item is still in the bag");
        }

        // ---- B: only one -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASecondFruitIsRefusedAndStaysInTheBag()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            Give(character, DarknessItem);
            Give(character, DarknessItem);

            Assert.That(Use(character, 0).IsAccepted, Is.True);

            yield return Tick();

            Revision revision = character.DevilFruit.Revision;
            int mdef = Stat(character, Mdef);

            Assert.That(mdef, Is.EqualTo(3 + 10), "the first fruit did not take effect");

            CharacterInventoryResult second = Use(character, Slot(character, DarknessItem));

            Assert.That(second.IsAccepted, Is.False, "a character ate two Devil Fruits");

            Assert.That(Held(character, DarknessItem), Is.EqualTo(1),
                "the refused fruit was consumed anyway");
            Assert.That(character.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));
            Assert.That(character.DevilFruit.Revision, Is.EqualTo(revision),
                "a refusal moved the revision");

            yield return Tick();

            Assert.That(Stat(character, Mdef), Is.EqualTo(mdef),
                "a refused fruit changed a stat");
        }

        // ---- C: the modifier ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFruitChangesRealStatsThroughTheCanonicalCalculator()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            int mdefBefore = Stat(character, Mdef);
            int matkBefore = Stat(character, Matk);
            int recomputes = Stats.Recomputations;

            Give(character, DarknessItem);

            Assert.That(Use(character, 0).IsAccepted, Is.True);

            yield return Tick();

            // The authored modifiers: +10 magic defence, +15 magic attack. No test called
            // RefreshAll; the shipped loop noticed the fruit.
            Assert.That(Stat(character, Mdef), Is.EqualTo(mdefBefore + 10));
            Assert.That(Stat(character, Matk), Is.EqualTo(matkBefore + 15));

            Assert.That(Stats.Recomputations, Is.GreaterThan(recomputes));

            // And quiet frames afterwards recompute nothing.
            int settled = Stats.Recomputations;

            yield return Tick(5);

            Assert.That(Stats.Recomputations, Is.EqualTo(settled),
                "owning a fruit recomputes stats every frame");
        }

        // ---- D: the ability -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFruitsAbilityBecomesUsableOnlyOnceItIsOwned()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            // Before: the skill exists in the world, and this character may not use it.
            ServerCombatResult tooSoon = Cast(caster, victim, DarkShroud);

            Assert.That(tooSoon.IsAccepted, Is.False,
                "a fruit ability was usable by somebody who owns no fruit");
            Assert.That(tooSoon.SkillRejection, Is.EqualTo(SkillUseRejection.NotLearned));
            Assert.That(victim.Status.ActiveCount, Is.Zero);

            Give(caster, DarknessItem);

            Assert.That(Use(caster, 0).IsAccepted, Is.True);

            yield return Tick();

            ServerCombatResult now = Cast(caster, victim, DarkShroud);

            Assert.That(now.IsAccepted, Is.True,
                "the fruit granted no ability: " + now.SkillRejection);

            // And it was never written into the skills they learned.
            Assert.That(caster.Skills.TryGetRank(new DefinitionId(DarkShroud), out int _),
                Is.False, "the fruit ability was saved as a learned skill");
        }

        // ---- E: the passive, through the existing status architecture ----------------------------------

        [UnityTest]
        public IEnumerator TheDarknessAbilitySilencesThroughTheOrdinaryStatusPath()
        {
            yield return LoadWorld();

            LivingCharacter caster = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            // The victim can cast before being silenced.
            Assert.That(Cast(victim, caster, MagicBolt).IsAccepted, Is.True);

            Give(caster, DarknessItem);
            Assert.That(Use(caster, 0).IsAccepted, Is.True);

            yield return Tick();

            Assert.That(Cast(caster, victim, DarkShroud).IsAccepted, Is.True);

            Assert.That(victim.Status.ActiveCount, Is.GreaterThan(0),
                "the ability applied no status");

            // Silenced, by the same rule Phase 18.7 already enforces -- no fruit-specific
            // branch anywhere decided this.
            Recharge(victim);
            Ready(victim);

            ServerCombatResult silenced = Cast(victim, caster, MagicBolt);

            Assert.That(silenced.IsAccepted, Is.False, "the silence did not take");
            Assert.That(silenced.SkillRejection, Is.EqualTo(SkillUseRejection.Silenced));

            // And a sword still works, exactly as the existing rules say.
            Assert.That(Attack(victim, caster).IsAccepted, Is.True,
                "silence disarmed a physical attack");
        }

        // ---- F: coming back --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFruitSurvivesAReconnectAndTheSpentItemDoesNot()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            Give(character, DarknessItem);
            Assert.That(Use(character, 0).IsAccepted, Is.True);

            yield return Tick();

            int mdef = Stat(character, Mdef);

            // A temporary status, which must NOT come back -- the two policies differ.
            StatusEffectService.TryApply(character.Status, new DefinitionId("status.test.x"),
                new DefinitionId("test"), Effects(), 300f);

            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.DevilFruit.HasActiveFruit, Is.True,
                "the fruit did not survive a reconnect");
            Assert.That(returned.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));

            Assert.That(Held(returned, DarknessItem), Is.Zero,
                "the eaten fruit came back as an item");

            yield return Tick();

            Assert.That(Stat(returned, Mdef), Is.EqualTo(mdef),
                "the fruit's modifiers were not restored");

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "a temporary status survived, which is the opposite policy from the fruit");

            LivingCharacter victim = Admit("char-b", 2, team: 2);

            Assert.That(Cast(returned, victim, DarkShroud).IsAccepted, Is.True,
                "the fruit's ability was lost across a reconnect");
        }

        // ---- G: death ------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DyingDoesNotTakeTheFruitAway()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);
            LivingCharacter killer = Admit("char-b", 2, team: 2);

            Give(character, DarknessItem);
            Assert.That(Use(character, 0).IsAccepted, Is.True);

            yield return Tick();

            int mdef = Stat(character, Mdef);

            // Killed through the ordinary combat path.
            for (var i = 0; i < 40 && character.Combatant.IsAlive(); i++)
            {
                Ready(killer);
                Attack(killer, character);
            }

            Assert.That(character.Combatant.IsAlive(), Is.False, "the victim would not die");

            yield return Tick();

            Assert.That(character.DevilFruit.HasActiveFruit, Is.True,
                "death took the fruit, which is a status-effect policy and not an "
                + "ownership one");
            Assert.That(Stat(character, Mdef), Is.EqualTo(mdef));
        }

        // ---- H: two clients -----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OneCharactersFruitReachesNobodyElse()
        {
            yield return LoadWorld();

            LivingCharacter a = Admit("char-a", 1);
            LivingCharacter b = Admit("char-b", 2, team: 2);

            Give(a, DarknessItem);
            Assert.That(Use(a, 0).IsAccepted, Is.True);

            yield return Tick();

            Assert.That(b.DevilFruit.HasActiveFruit, Is.False, "B was given A's fruit");

            int bMdef = Stat(b, Mdef);

            Assert.That(bMdef, Is.EqualTo(3), "A's fruit changed B's stats");

            // B cannot use A's ability.
            Assert.That(Cast(b, a, DarkShroud).SkillRejection,
                Is.EqualTo(SkillUseRejection.NotLearned));

            // B cannot cast A's ability through A's character either: the identity is the
            // connection's, never the claim's.
            ServerCombatResult forged = Combat.Execute(b.ConnectionId,
                new CombatCommand(a.Character, b.CombatantId, new DefinitionId(DarkShroud),
                    1, ++_sequence));

            Assert.That(forged.IsAccepted, Is.False, "B cast through A's character");

            // And B cannot use what is in A's bag.
            Give(a, DarknessItem);

            int aSlot = Slot(a, DarknessItem);

            Submit(b.ConnectionId, InventoryAction.Use, aSlot, ++_sequence);

            Assert.That(Held(a, DarknessItem), Is.EqualTo(1),
                "B consumed an item out of A's bag");
            Assert.That(b.DevilFruit.HasActiveFruit, Is.False,
                "B was granted a fruit from A's bag");
        }

        // ---- I: the same request twice ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AReplayedRequestCannotGrantTheFruitTwiceOrEatAnotherItem()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            Give(character, DarknessItem);
            Give(character, DarknessItem);

            long sequence = ++_sequence;

            CharacterInventoryResult first = Submit(1, InventoryAction.Use, 0, sequence);

            Assert.That(first.IsAccepted, Is.True);

            Revision revision = character.DevilFruit.Revision;
            int held = Held(character, DarknessItem);

            // The same sequence again: a delayed packet, or a client that sent twice.
            CharacterInventoryResult replay = Submit(1, InventoryAction.Use, 0, sequence);

            Assert.That(replay.IsAccepted, Is.False, "a replayed request was accepted");

            Assert.That(character.DevilFruit.Revision, Is.EqualTo(revision),
                "the replay granted the fruit a second time");
            Assert.That(Held(character, DarknessItem), Is.EqualTo(held),
                "the replay ate the other fruit as well");
        }

        // ---- J: forged and impossible requests ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NothingAboutTheFruitCanBeForgedThroughARequest()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            // An empty slot.
            Assert.That(Use(character, 5).IsAccepted, Is.False);
            Assert.That(character.DevilFruit.HasActiveFruit, Is.False);

            // A slot that does not exist.
            Assert.That(Use(character, 9999).IsAccepted, Is.False);
            Assert.That(Use(character, -1).IsAccepted, Is.False);
            Assert.That(character.DevilFruit.HasActiveFruit, Is.False);

            // An item that is not a fruit: used, and grants nothing.
            Give(character, "item.devil_fruit.darkness");

            CharacterInventoryResult ok = Use(character, 0);

            Assert.That(ok.IsAccepted, Is.True);
            Assert.That(character.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));

            // The request type itself cannot carry a fruit id: proved structurally, because
            // there is no argument that could hold one.
            foreach (System.Reflection.ParameterInfo parameter in
                typeof(ICharacterInventoryRequestSink).GetMethod("Submit").GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(DefinitionId)));
            }
        }

        // ---- K: a save that fails --------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AFruitIsOnlyRememberedIfTheSaveThatCarriedItSucceeded()
        {
            yield return LoadWorld();

            LivingCharacter character = Admit("char-a", 1);

            // The item is stored first, so the row this test later inspects genuinely
            // contains it. Without that, "the item came back" would be indistinguishable
            // from "the item was never written in the first place".
            Give(character, DarknessItem);

            Assert.That(_bootstrap.Characters.Save(character, force: true).IsOk, Is.True);

            Assert.That(Use(character, 0).IsAccepted, Is.True);
            Assert.That(character.DevilFruit.HasActiveFruit, Is.True);

            // The backend goes away before the character is written out.
            _store.Broken = true;

            CharacterPersistenceResult release = _bootstrap.Simulation.Release(1);

            Assert.That(release.IsOk, Is.False,
                "a failed save reported success, so the fruit would be silently lost");

            _store.Broken = false;

            yield return Tick();

            // The stored row is the one from before the fruit, because the save that
            // carried it never landed -- and the item they spent is still in it, because
            // the same row holds both and it moves as one. This is the honest limit of the
            // current architecture: a failed save loses the whole session's progress
            // rather than half of it, which is the outcome that keeps the two consistent.
            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.DevilFruit.HasActiveFruit, Is.False,
                "a fruit was remembered from a save that failed");

            Assert.That(Held(returned, DarknessItem), Is.EqualTo(1),
                "the item was spent even though the fruit was not kept: the row must "
                + "move as one, or an ultra-rare item is destroyed for nothing");
        }

        // ---- harness ---------------------------------------------------------------------------------------------

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

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private void Seed(string character)
        {
            string session = "session-" + character;

            if (_store.Rows.ContainsKey(session)) return;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 1, 1, 0, 104, 35,
                new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                default, StarterAttributes(),
                null,
                new[] { new PersistedSkill(new DefinitionId(MagicBolt), 1) }, 1);
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

        /// <summary>Puts a real authored item in a real bag, as a drop would.</summary>
        private void Give(LivingCharacter character, string item)
        {
            Assert.That(character.Inventory, Is.Not.Null, "the world composed no inventory");

            ItemContainerResult added = character.Inventory.Add(
                new ItemInstance(InstanceId.New(), new DefinitionId(item),
                    character.Owner, 1),
                Items);

            Assert.That(added.IsAccepted, Is.True, added.ToString());
        }

        private static int Held(LivingCharacter character, string item)
        {
            var count = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item) count++;
            }

            return count;
        }

        private static int Slot(LivingCharacter character, string item)
        {
            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item) return i;
            }

            return -1;
        }

        /// <summary>Sends the ordinary inventory request a client sends.</summary>
        private CharacterInventoryResult Use(LivingCharacter character, int slot)
        {
            return Submit(character.ConnectionId, InventoryAction.Use, slot, ++_sequence);
        }

        /// <summary>The request sink a client's own inventory RPC lands in.</summary>
        private CharacterInventoryResult Submit(int connection, InventoryAction action,
            int slot, long sequence)
        {
            Inventory.Submit(connection, action, slot, 0, 1, sequence);

            return Inventory.LastResult;
        }

        private ServerCombatResult Cast(LivingCharacter caster, LivingCharacter target,
            string skill)
        {
            Recharge(caster);
            Ready(caster);

            return Combat.Execute(caster.ConnectionId,
                new CombatCommand(caster.Character, target.CombatantId,
                    new DefinitionId(skill), 1, ++_sequence));
        }

        private ServerCombatResult Attack(LivingCharacter attacker, LivingCharacter target)
        {
            return Combat.Execute(attacker.ConnectionId,
                new CombatCommand(attacker.Character, target.CombatantId, default, 0,
                    ++_sequence));
        }

        private static void Recharge(LivingCharacter caster)
        {
            caster.Domain.Resources.SetMana(caster.Combatant.Limits.MaxMana,
                caster.Combatant.Limits);
        }

        private void Ready(LivingCharacter caster)
        {
            Combat.Tick(60f);
        }

        private static int Stat(LivingCharacter character, DefinitionId stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(stat, out int value), Is.True,
                stat + " was never computed");

            return value;
        }

        private DefinitionRegistry<StatusEffectDefinition> Effects()
        {
            var registry = new DefinitionRegistry<StatusEffectDefinition>();

            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"status.test.x\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":1,\"_durationSeconds\":0,\"_stackBehavior\":0,"
                + "\"_maxStacks\":1}", definition);

            registry.Register(definition);

            return registry;
        }

        // The world's own authorities. WorldSimulation keeps them private, which is right:
        // nothing in the game reaches past it, and a test that built its own would prove
        // only that it can.
        private ServerCombatPipeline Combat => Field<ServerCombatPipeline>("_combat");

        private CharacterStatAuthority Stats => Field<CharacterStatAuthority>("_stats");

        private CharacterInventoryAuthority Inventory
        {
            get
            {
                var authority = (CharacterInventoryAuthority)typeof(CharacterReplicationService)
                    .GetField("_inventory", System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance)
                    .GetValue(_bootstrap.Replication);

                Assert.That(authority, Is.Not.Null, "the scene wired no inventory authority");

                return authority;
            }
        }

        private IDefinitionRegistry<ItemDefinition> Items
        {
            get
            {
                var catalogue = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<WorldContentCatalogue>(
                        "Assets/_Game/Data/Production/WorldContentCatalogue.asset");

                return catalogue.BuildItems();
            }
        }

        private T Field<T>(string name) where T : class
        {
            var value = (T)typeof(WorldSimulation)
                .GetField(name, System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(_bootstrap.Simulation);

            Assert.That(value, Is.Not.Null, "the scene composed no " + name);

            return value;
        }
    }
}

#endif
