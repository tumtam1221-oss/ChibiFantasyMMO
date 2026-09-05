// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world drops this.
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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// A world boss dying in the shipped world, and what it leaves behind.
    /// </summary>
    /// <remarks>
    /// <b>The chance is never touched.</b> Every test below runs against the authored
    /// one-in-ten-million, and the only thing substituted is the roll -- the same seam the
    /// production code already takes its randomness through. A test that raised the rate to
    /// see a drop would be testing a game nobody ships.
    ///
    /// <b>Nothing is placed in a bag by hand.</b> The fruit that reaches an inventory here
    /// came out of a boss that a character killed through the ordinary combat pipeline, and
    /// was taken with the ordinary pickup request. That is the whole point of the file.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldBossLootTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string Slime = "monster.training_slime";
        private const string DarknessItem = "item.devil_fruit.darkness";
        private const string Darkness = "devil_fruit.darkness";
        private const string DarkShroud = "skill.dark_shroud";

        /// <summary>The authored fraction. Read, never written.</summary>
        private const float Fraction = 0.0000001f;

        /// <summary>A roll a test chooses, against a chance it never changes.</summary>
        private sealed class ScriptedRandom : IRandomResultSource, IRandomRangeSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public readonly List<float> ChancesAsked = new List<float>();

            public bool Succeeds(float successChance)
            {
                ChancesAsked.Add(successChance);

                return _roll < successChance;
            }

            public int Range(int minInclusive, int maxInclusive) => minInclusive;
        }

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
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private FakeStore _store;
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

        // ---- A: the boss exists in the shipped world ----------------------------------------------

        [UnityTest]
        public IEnumerator TheProductionBossSpawnsThroughOrdinaryConfiguration()
        {
            yield return LoadWorld(roll: 1f);

            LivingMonster boss = Spawn(Boss);

            Assert.That(boss.State.Definition.Id.Value, Is.EqualTo(Boss));
            Assert.That(boss.State.Definition.Rank, Is.EqualTo(MonsterRank.WorldBoss));
            Assert.That(boss.State.MaxHealth, Is.GreaterThan(500),
                "a world boss with a training monster's health");

            Assert.That(_bootstrap.Loot, Is.Not.Null, "the shipped world composed no loot");
            Assert.That(_bootstrap.Rewards, Is.Not.Null, "the shipped world pays nobody");
            Assert.That(_bootstrap.LootAuthority, Is.Not.Null);
            Assert.That(_bootstrap.Loot.Count, Is.Zero, "nothing has died yet");
        }

        // ---- B: the rare roll lands -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator KillingTheBossOnALuckyRollLeavesExactlyOneDevilFruit()
        {
            var rolls = new ScriptedRandom(0f);

            yield return LoadWorld(rolls);

            LivingCharacter hero = Admit("char-a", 1);
            LivingMonster boss = Spawn(Boss);

            ServerCombatResult killing = Kill(hero, boss);

            Assert.That(killing.TargetDefeated, Is.True, "the boss would not die");

            // The chance the world asked about is the authored one. Nothing raised it.
            Assert.That(rolls.ChancesAsked, Does.Contain(Fraction).Using<float>(
                (a, b) => Mathf.Abs(a - b) < 1e-12f ? 0 : 1),
                "the roll was made against a chance that is not one in ten million");

            Assert.That(_bootstrap.Loot.Count, Is.EqualTo(1), "the boss left no pile");

            LootObjectState pile = _bootstrap.Loot.All()[0];

            var fruits = 0;

            foreach (LootResult stack in pile.Contents)
            {
                if (stack.Item.Value == DarknessItem) fruits += stack.Quantity;
            }

            Assert.That(fruits, Is.EqualTo(1), "the boss dropped " + fruits + " Devil Fruits");
        }

        // ---- C: the ordinary roll ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator KillingTheBossOnAnOrdinaryRollLeavesNoDevilFruitButStillPaysExperience()
        {
            yield return LoadWorld(roll: 0.5f);

            LivingCharacter hero = Admit("char-a", 1);

            long before = hero.Domain.Progression.Experience;

            ServerCombatResult killing = Kill(hero, Spawn(Boss));

            Assert.That(killing.TargetDefeated, Is.True);

            Assert.That(FruitsOnTheGround(), Is.Zero,
                "an ordinary roll produced the rarest item in the game");

            // The boss is still worth killing.
            Assert.That(hero.Domain.Progression.Experience, Is.GreaterThan(before),
                "a boss kill paid no experience");
        }

        // ---- D: an ordinary monster ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheTrainingSlimeNeverDropsTheFruitEvenOnALuckyRoll()
        {
            // The luckiest possible roll. Eligibility has to refuse before chance matters.
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);

            ServerCombatResult killing = Kill(hero, Spawn(Slime));

            Assert.That(killing.TargetDefeated, Is.True, "the slime would not die");

            Assert.That(FruitsOnTheGround(), Is.Zero,
                "a training slime dropped a Devil Fruit on a lucky roll");
        }

        // ---- E and F: taking it -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheFruitIsTakenThroughTheOrdinaryPickupRequestExactlyOnce()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);

            Kill(hero, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            Assert.That(Held(hero, DarknessItem), Is.Zero);

            // Walk over to it, then ask. The identity comes from the connection.
            StandOn(hero, pile);

            LootPickupOutcome taken = Pickup(hero, pile, ++_sequence);

            Assert.That(taken.IsAccepted, Is.True, "pickup refused: " + taken.Reason);
            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1),
                "the fruit did not reach the bag");

            // The boss drops a card as well as a fruit, so one pickup no longer empties
            // the pile. Taking the rest is what proves the pile goes when it is empty.
            Drain(hero, pile);

            Assert.That(_bootstrap.Loot.Count, Is.Zero, "the pile survived being emptied");

            // The same request again, and a fresh one for a pile that is gone.
            Assert.That(Pickup(hero, pile, _sequence).IsAccepted, Is.False,
                "a replayed pickup was accepted");

            Assert.That(Pickup(hero, pile, ++_sequence).IsAccepted, Is.False,
                "a pile that was already taken was taken again");

            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1),
                "a duplicate request produced a second Devil Fruit");
        }

        [UnityTest]
        public IEnumerator APileOutOfReachIsRefusedUntilTheCharacterWalksToIt()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);

            Kill(hero, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            Move(hero, pile.Position.X + 50f, pile.Position.Y, pile.Position.Z + 50f);

            Assert.That(Pickup(hero, pile, ++_sequence).IsAccepted, Is.False,
                "a character looted a pile fifty metres away");

            Assert.That(Held(hero, DarknessItem), Is.Zero);

            // A refused request must not burn the sequence, or walking closer would not help.
            StandOn(hero, pile);

            Assert.That(Pickup(hero, pile, ++_sequence).IsAccepted, Is.True,
                "walking to the pile did not help");
        }

        // ---- G: two clients ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator OnlyOneOfTwoCharactersCanTakeTheOneFruit()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter a = Admit("char-a", 1);
            LivingCharacter b = Admit("char-b", 2, team: 2);

            Kill(a, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            StandOn(a, pile);
            StandOn(b, pile);

            LootPickupOutcome first = Pickup(a, pile, ++_sequence);
            LootPickupOutcome second = Pickup(b, pile, ++_sequence);

            Assert.That(first.IsAccepted, Is.True, "the killer could not take their own drop");
            Assert.That(second.IsAccepted, Is.False, "both characters took the same fruit");

            Assert.That(Held(a, DarknessItem), Is.EqualTo(1));
            Assert.That(Held(b, DarknessItem), Is.Zero, "B received a copy of A's fruit");

            // And B cannot consume out of A's bag.
            Submit(b.ConnectionId, InventoryAction.Use, Slot(a, DarknessItem), ++_sequence);

            Assert.That(Held(a, DarknessItem), Is.EqualTo(1),
                "B consumed the item out of A's bag");
            Assert.That(b.DevilFruit.HasActiveFruit, Is.False);
        }

        // ---- H and I: eating it ----------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheTakenFruitIsEatenThroughTheOrdinaryInventoryRequest()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);
            LivingCharacter victim = Admit("char-b", 2, team: 2);

            Kill(hero, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];
            StandOn(hero, pile);

            Assert.That(Pickup(hero, pile, ++_sequence).IsAccepted, Is.True);

            int mdef = Stat(hero, "stat.mdef");

            // The fruit ability is not theirs yet.
            Assert.That(Cast(hero, victim, DarkShroud).SkillRejection,
                Is.EqualTo(SkillUseRejection.NotLearned));

            CharacterInventoryResult eaten = Submit(hero.ConnectionId, InventoryAction.Use,
                Slot(hero, DarknessItem), ++_sequence);

            Assert.That(eaten.IsAccepted, Is.True, "the looted fruit could not be eaten: "
                + eaten);

            Assert.That(hero.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));
            Assert.That(Held(hero, DarknessItem), Is.Zero, "the item was not consumed");

            yield return Tick();

            Assert.That(Stat(hero, "stat.mdef"), Is.EqualTo(mdef + 10),
                "the fruit's modifiers are not live");

            Assert.That(Cast(hero, victim, DarkShroud).IsAccepted, Is.True,
                "the fruit granted no ability");

            Assert.That(victim.Status.ActiveCount, Is.GreaterThan(0),
                "Dark Shroud applied no status");
        }

        [UnityTest]
        public IEnumerator AnOwnerOfAFruitKeepsASecondOneRatherThanLosingIt()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);

            Kill(hero, Spawn(Boss));

            LootObjectState first = _bootstrap.Loot.All()[0];
            StandOn(hero, first);
            Pickup(hero, first, ++_sequence);

            Assert.That(Submit(hero.ConnectionId, InventoryAction.Use,
                Slot(hero, DarknessItem), ++_sequence).IsAccepted, Is.True);

            // Emptied, so the first boss's pile is gone before the second one drops. A
            // boss leaves a card as well as a fruit, and a pile still holding something
            // would still be lying there when the next one appears.
            Drain(hero, first);

            // A second boss, a second fruit: they may take it, and may not eat it.
            Kill(hero, Spawn(Boss));

            LootObjectState second = _bootstrap.Loot.All()[_bootstrap.Loot.All().Count - 1];
            StandOn(hero, second);

            Assert.That(Pickup(hero, second, ++_sequence).IsAccepted, Is.True,
                "ordinary inventory rules refused an ordinary item");

            CharacterInventoryResult refused = Submit(hero.ConnectionId, InventoryAction.Use,
                Slot(hero, DarknessItem), ++_sequence);

            Assert.That(refused.IsAccepted, Is.False, "a character ate two Devil Fruits");

            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1),
                "the refused fruit was destroyed rather than left in the bag");
            Assert.That(hero.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness));
        }

        // ---- J and K: the whole slice ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheWholeChainFromBossToReconnectHoldsTogether()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);

            // boss -> combat -> rare roll -> loot
            Kill(hero, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];
            StandOn(hero, pile);

            // loot -> pickup -> inventory
            Assert.That(Pickup(hero, pile, ++_sequence).IsAccepted, Is.True);
            Assert.That(Held(hero, DarknessItem), Is.EqualTo(1));

            // inventory -> consume
            Assert.That(Submit(hero.ConnectionId, InventoryAction.Use,
                Slot(hero, DarknessItem), ++_sequence).IsAccepted, Is.True);

            yield return Tick();

            int mdef = Stat(hero, "stat.mdef");

            // save -> disconnect -> reconnect
            Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

            yield return Tick();

            LivingCharacter returned = Admit("char-a", 1);

            Assert.That(returned.DevilFruit.ActiveFruit.Value, Is.EqualTo(Darkness),
                "the fruit did not survive the reconnect");
            Assert.That(Held(returned, DarknessItem), Is.Zero,
                "the consumed item came back");
            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "a temporary status survived, which is the opposite policy");

            yield return Tick();

            Assert.That(Stat(returned, "stat.mdef"), Is.EqualTo(mdef),
                "the modifiers were not restored");

            LivingCharacter victim = Admit("char-b", 2, team: 2);

            Assert.That(Cast(returned, victim, DarkShroud).IsAccepted, Is.True,
                "the ability was lost across a reconnect");
        }

        // ---- L: exactly once -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASecondLethalBlowPaysNothingAndDropsNothingExtra()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-a", 1);
            LivingMonster boss = Spawn(Boss);

            Kill(hero, boss);

            long experience = hero.Domain.Progression.Experience;
            int piles = _bootstrap.Loot.Count;
            int fruits = FruitsOnTheGround();

            Assert.That(fruits, Is.EqualTo(1));

            // Hit the corpse again, twice.
            for (var i = 0; i < 2; i++)
            {
                Ready(hero);

                _bootstrap.Simulation.Combat().Execute(hero.ConnectionId,
                    new CombatCommand(hero.Character, boss.Instance, default, 0, ++_sequence));
            }

            Assert.That(hero.Domain.Progression.Experience, Is.EqualTo(experience),
                "a second lethal signal paid experience twice");
            Assert.That(_bootstrap.Loot.Count, Is.EqualTo(piles),
                "a second lethal signal dropped a second pile");
            Assert.That(FruitsOnTheGround(), Is.EqualTo(fruits),
                "a second lethal signal duplicated the Devil Fruit");

            Assert.That(_bootstrap.Rewards.HasGranted(boss.Instance), Is.True);
        }

        // ---- harness -------------------------------------------------------------------------------------------

        private IEnumerator LoadWorld(float roll)
        {
            yield return LoadWorld(new ScriptedRandom(roll));
        }

        private IEnumerator LoadWorld(ScriptedRandom rolls)
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();

            // The roll, not the rate. Set before Compose so the world is built with it.
            _bootstrap.UseRandom(rolls, rolls);

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

        private LivingCharacter Admit(string character, int connection, int team = 1)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 30, 0, 104, 35,
                    new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1);
            }

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        /// <summary>Spawns a monster through the runtime's own configuration seam.</summary>
        private LivingMonster Spawn(string monster)
        {
            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            int before = monsters.All().Count;

            Assert.That(monsters.AddSpawnPoint(new MonsterSpawnPoint(
                new DefinitionId(monster), default, 0f, 1, 0f, new DefinitionId(StarterMap))),
                Is.True);

            monsters.PopulateAll();

            IReadOnlyList<LivingMonster> alive = monsters.All();

            Assert.That(alive.Count, Is.GreaterThan(before), "nothing spawned for " + monster);

            for (var i = alive.Count - 1; i >= 0; i--)
            {
                if (alive[i].State.Definition.Id.Value == monster) return alive[i];
            }

            Assert.Fail("no living " + monster);

            return null;
        }

        /// <summary>Kills a monster through the ordinary combat pipeline.</summary>
        private ServerCombatResult Kill(LivingCharacter hero, LivingMonster monster)
        {
            _bootstrap.Simulation.Monsters().TryResolve(monster.Instance,
                out ICombatant target);

            ServerCombatResult last = default;

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                Ready(hero);

                last = _bootstrap.Simulation.Combat().Execute(hero.ConnectionId,
                    new CombatCommand(hero.Character, monster.Instance, default, 0,
                        ++_sequence));

                if (!last.IsAccepted) break;
            }

            Assert.That(target.CurrentHealth, Is.Zero,
                "the monster would not die: " + last);

            return last;
        }

        private void Ready(LivingCharacter hero)
        {
            _bootstrap.Simulation.Combat().Tick(10f);
        }

        private int FruitsOnTheGround()
        {
            var fruits = 0;

            foreach (LootObjectState pile in _bootstrap.Loot.All())
            {
                foreach (LootResult stack in pile.Contents)
                {
                    if (stack.Item.Value == DarknessItem) fruits += stack.Quantity;
                }
            }

            return fruits;
        }

        private LootPickupOutcome Pickup(LivingCharacter taker, LootObjectState pile,
            long sequence)
        {
            return _bootstrap.LootAuthority.Apply(taker.ConnectionId, pile.LootId.Value, 0,
                sequence);
        }

        /// <summary>Takes everything left in a pile, so it empties and is swept.</summary>
        /// <remarks>A boss drops more than one thing now. Tests that care about the pile
        /// disappearing have to take all of it, exactly as a player would.</remarks>
        private void Drain(LivingCharacter taker, LootObjectState pile)
        {
            for (var index = 0; index < 8; index++)
            {
                _bootstrap.LootAuthority.Apply(taker.ConnectionId, pile.LootId.Value,
                    index, ++_sequence);
            }
        }

        private CharacterInventoryResult Submit(int connection, InventoryAction action,
            int slot, long sequence)
        {
            CharacterInventoryAuthority inventory = _bootstrap.Simulation.Inventory(
                _bootstrap.Replication);

            inventory.Submit(connection, action, slot, 0, 1, sequence);

            return inventory.LastResult;
        }

        private ServerCombatResult Cast(LivingCharacter caster, LivingCharacter target,
            string skill)
        {
            caster.Domain.Resources.SetMana(caster.Combatant.Limits.MaxMana,
                caster.Combatant.Limits);

            Ready(caster);

            return _bootstrap.Simulation.Combat().Execute(caster.ConnectionId,
                new CombatCommand(caster.Character, target.CombatantId,
                    new DefinitionId(skill), 1, ++_sequence));
        }

        private static void StandOn(LivingCharacter character, LootObjectState pile)
        {
            Move(character, pile.Position.X, pile.Position.Y, pile.Position.Z);
        }

        private static void Move(LivingCharacter character, float x, float y, float z)
        {
            character.Combatant.Position = new CombatPosition(x, y, z);
        }

        private static int Held(LivingCharacter character, string item)
        {
            var count = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                var instance = character.Inventory.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item)
                {
                    count += instance.Quantity;
                }
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

        private static int Stat(LivingCharacter character, string stat)
        {
            Assert.That(character.Combatant.TryGetCombatStat(new DefinitionId(stat),
                out int value), Is.True, stat + " was never computed");

            return value;
        }
    }

    /// <summary>Reaches the world's own collaborators, which it rightly keeps private.</summary>
    internal static class ShippedWorldAccess
    {
        public static ServerCombatPipeline Combat(this WorldSimulation world)
        {
            return Field<ServerCombatPipeline>(world, "_combat");
        }

        public static MonsterWorldRuntime Monsters(this WorldSimulation world)
        {
            return Field<MonsterWorldRuntime>(world, "_monsters");
        }

        public static CharacterInventoryAuthority Inventory(this WorldSimulation world,
            CharacterReplicationService replication)
        {
            var authority = (CharacterInventoryAuthority)typeof(CharacterReplicationService)
                .GetField("_inventory", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(replication);

            Assert.That(authority, Is.Not.Null, "the scene wired no inventory authority");

            return authority;
        }

        private static T Field<T>(WorldSimulation world, string name) where T : class
        {
            var value = (T)typeof(WorldSimulation)
                .GetField(name, System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .GetValue(world);

            Assert.That(value, Is.Not.Null, "the scene composed no " + name);

            return value;
        }
    }
}

#endif
