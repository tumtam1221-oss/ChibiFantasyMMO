using System.Collections.Generic;
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
    /// What a kill drops, who owns it, and how it gets into a bag.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here is random.</b> Every roll is injected through the seam Phase 10
    /// already defines, so a one-in-ten-million drop is a boundary test rather than a
    /// billion iterations. The production generator is tested separately, for the properties
    /// a generator can actually be held to.
    ///
    /// <b>The pipeline is Phase 08's and Phase 10's.</b> The rolling is
    /// <c>DropResolver</c>, the pile is <c>LootObjectState</c>, the transfer is
    /// <c>LootPickupService</c>, the bag is <c>ItemContainerState</c>. What is tested here is
    /// the server layer over them: that a client names a pile and an entry and never an item,
    /// a quantity, an owner or a chance.
    /// </remarks>
    [TestFixture]
    internal sealed class MonsterLootTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public readonly List<PersistedCharacter> Saved = new List<PersistedCharacter>();

            public CharacterPersistenceFailure FailWith = CharacterPersistenceFailure.None;

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                if (FailWith != CharacterPersistenceFailure.None)
                {
                    return CharacterPersistenceResult.Failed(FailWith, "test");
                }

                Saved.Add(c);

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private const string Rat = "monster.rat";
        private const string Overlord = "monster.overlord";

        private const string CommonTable = "drop.common";
        private const string RareTable = "drop.rare";
        private const string BossTable = "drop.boss";
        private const string StackTable = "drop.stacks";

        private const string Fruit = "item.devilfruit";
        private const string Card = "item.card";
        private const string PetEgg = "item.petegg";

        private const int Capacity = 4;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private MonsterLootRegistry _loot;
        private CharacterProgressionDefinition _curve;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpLoot()
        {
            AddItem(Fruit, maxStack: 1);
            AddItem(Card, maxStack: 1);
            AddItem(PetEgg, maxStack: 1);

            _curve = Curve();

            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));
            spawns.Register(PlayerSpawn("spawn.other", OtherMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, Capacity);

            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp),
                Enemies);

            _loot = new MonsterLootRegistry(_players, Items);
        }

        [TearDown]
        public void TearDownLoot()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private CharacterProgressionDefinition Curve()
        {
            var definition = ScriptableObject.CreateInstance<CharacterProgressionDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"progression.loot-test\"},\"_minLevel\":1,"
                + "\"_maxLevel\":20,\"_experienceToNextLevel\":[100,100,100,100,100,100,100,"
                + "100,100,100,100,100,100,100,100,100,100,100,100]}", definition);

            _local.Add(definition);

            return definition;
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

        private LivingCharacter AddPlayer(string character, string map = HomeMap,
            int level = 5)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, level, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(character.GetHashCode(),
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(map),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            return result.Character;
        }

        /// <summary>An authority wired to drop, with the rolls a test chooses.</summary>
        private MonsterRewardAuthority Authority(IRandomResultSource rolls = null,
            IRandomRangeSource quantities = null, float lifetime = 0f)
        {
            return new MonsterRewardAuthority(_runtime, _players, _curve, _loot, Items,
                DropTables, rolls, quantities, lifetime);
        }

        private LivingMonster Corpse(string monster, string map = HomeMap)
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(map)));

            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.State.DefinitionId.Value != monster || !living.IsAlive) continue;

                living.State.ApplyHealthDelta(-10000);

                return living;
            }

            Assert.Fail("no living monster '" + monster + "'");

            return null;
        }

        private static InstanceId Of(LivingCharacter character)
        {
            return character.Combatant.CombatantId;
        }

        /// <summary>A rat with a table of one guaranteed coin.</summary>
        private void AuthorRat(int min = 1, int max = 1, float chance = 0f)
        {
            AddDropTable(CommonTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), min, max, chance),
            });

            AddMonster(Rat, level: 5, lootTable: CommonTable);
        }

        // ---- 1-10: what the roll produces ---------------------------------------------------

        [Test]
        public void AValidEntryProducesAnItemOfTheAuthoredKind()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result =
                Authority().Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.True, result.ToString());
            Assert.That(_loot.TryGet(result.LootPile, out LootObjectState pile), Is.True);
            Assert.That(pile.Contents[0].Item.Value, Is.EqualTo(Coin));
            Assert.That(pile.Contents[0].Quantity, Is.EqualTo(1));
        }

        [Test]
        public void AChanceOfZeroIsGuaranteedAndNotImpossible()
        {
            // The authored contract: zero or less means certain, because an unauthored
            // chance must not read as "never" for content written before odds existed.
            AuthorRat(chance: 0f);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority(AlwaysFails.Instance)
                .Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.True,
                "a blank chance is a guaranteed drop, not a disabled one");
        }

        [Test]
        public void AChanceOfOneAlwaysDrops()
        {
            AuthorRat(chance: 1f);

            LivingCharacter killer = AddPlayer("char-killer");

            // The real generator, not a stub: no roll ever reaches one.
            MonsterRewardResult result = Authority(new SystemRandomSource(1234))
                .Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.True);
        }

        [TestCase(1e-7f, TestName = "one in ten million")]
        [TestCase(1e-6f, TestName = "one in a million")]
        public void ATinyChanceIsAFractionAndNotAPercentage(float chance)
        {
            // 0.00001% is 1e-7. A roll just below it succeeds and a roll just above it
            // fails, which is what proves the number is read as a fraction of one rather
            // than as a percentage -- read as a percentage, both of these would be a
            // hundred times more common than authored.
            var justUnder = new ThresholdResultSource(chance * 0.5f);
            var justOver = new ThresholdResultSource(chance * 2f);

            Assert.That(justUnder.Succeeds(chance), Is.True);
            Assert.That(justOver.Succeeds(chance), Is.False);

            // And a roll a percentage-reader would call a hit is a miss.
            Assert.That(new ThresholdResultSource(chance * 100f).Succeeds(chance), Is.False,
                "a value 100x the chance must fail, or the number is being read as a "
                + "percentage");
        }

        [Test]
        public void ARollExactlyOnTheChanceFails()
        {
            // roll < chance, so the boundary belongs to the failure side. That convention
            // is what makes a zero chance impossible on a zero roll.
            Assert.That(new ThresholdResultSource(0.25f).Succeeds(0.25f), Is.False);
            Assert.That(new ThresholdResultSource(0.249f).Succeeds(0.25f), Is.True);
        }

        [Test]
        public void AnUnusableChanceIsSkippedRatherThanRolled()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1, float.NaN),
                new DropEntry(new DefinitionId(Coin), 1, 1, 2f),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority(AlwaysSucceeds.Instance)
                .Grant(Corpse(Rat).Instance, Of(killer));

            // NaN compares false against everything, so an entry carrying one would look
            // like a drop that merely never happens; a chance above one is not a
            // probability at all. Both are refused rather than clamped.
            Assert.That(result.HasLoot, Is.False,
                "a malformed row must not become a drop");
        }

        [Test]
        public void EachEntryGetsItsOwnRoll()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1, 0.5f),
                new DropEntry(new DefinitionId(Hide), 1, 1, 0.5f),
                new DropEntry(new DefinitionId(Relic), 1, 1, 0.5f),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            // Yes, no, yes. A monster is not "one item": a table can guarantee a coin and
            // rarely give a relic in the same kill.
            var rolls = new ScriptedResultSource(true, false, true);

            MonsterRewardResult result = Authority(rolls).Grant(Corpse(Rat).Instance,
                Of(killer));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(rolls.Calls, Is.EqualTo(3), "one roll per entry");
            Assert.That(pile.Count, Is.EqualTo(2));
            Assert.That(pile.Contents[0].Item.Value, Is.EqualTo(Coin));
            Assert.That(pile.Contents[1].Item.Value, Is.EqualTo(Relic));
        }

        [Test]
        public void TheQuantityComesFromTheAuthoredRange()
        {
            AuthorRat(min: 2, max: 9);

            LivingCharacter killer = AddPlayer("char-killer");

            var rolls = new ScriptedResultSource(true).WithNumbers(5);

            MonsterRewardResult result = Authority(rolls, rolls)
                .Grant(Corpse(Rat).Instance, Of(killer));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Contents[0].Quantity, Is.EqualTo(5));
        }

        [Test]
        public void AnEntryThatCanOnlyProduceNothingIsRefused()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 0, 0),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            Assert.That(Authority().Grant(Corpse(Rat).Instance, Of(killer)).HasLoot,
                Is.False, "a quantity of zero is not a drop");
        }

        [Test]
        public void AnEntryForContentThisBuildDoesNotHaveIsSkipped()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId("item.removed-in-a-patch"), 1, 1),
                new DropEntry(new DefinitionId(Coin), 1, 1),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Count, Is.EqualTo(1),
                "loot nobody could ever pick up must not be created");
        }

        [Test]
        public void NothingAboutADropCanBeNamedByACaller()
        {
            // The authority is handed two ids the server minted. There is no item, no
            // quantity, no chance and no owner in any signature a request could reach.
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterRewardAuthority).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.EqualTo(typeof(InstanceId)),
                        method.Name + " takes a " + parameter.ParameterType.Name);
                }
            }

            // Pickup names a pile, an entry and a character -- never an item or a quantity.
            System.Reflection.ParameterInfo[] pickup =
                typeof(MonsterLootRegistry).GetMethod("Pickup").GetParameters();

            Assert.That(pickup[0].ParameterType, Is.EqualTo(typeof(InstanceId)));
            Assert.That(pickup[1].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(pickup[2].ParameterType, Is.EqualTo(typeof(CharacterId)));
            Assert.That(pickup.Length, Is.EqualTo(3));
        }

        // ---- 11-14: it hangs off a real defeat -------------------------------------------------

        [Test]
        public void ALivingMonsterDropsNothing()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Rat),
                new CombatPosition(0f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));
            _runtime.PopulateAll();

            LivingMonster alive = _runtime.All()[0];

            Assert.That(Authority().Grant(alive.Instance, Of(killer)).HasLoot, Is.False);
            Assert.That(_loot.Count, Is.Zero);
        }

        [Test]
        public void ADuplicateDefeatDropsOnce()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");
            MonsterRewardAuthority authority = Authority();
            LivingMonster corpse = Corpse(Rat);

            for (int i = 0; i < 5; i++) authority.Grant(corpse.Instance, Of(killer));

            Assert.That(_loot.Count, Is.EqualTo(1),
                "the one defeat claim is what stops a second pile existing");
        }

        [Test]
        public void ADefeatClaimedElsewhereDropsNothing()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");
            LivingMonster corpse = Corpse(Rat);

            Assert.That(corpse.State.TryClaimDefeat(), Is.True, "precondition");

            Assert.That(Authority().Grant(corpse.Instance, Of(killer)).HasLoot, Is.False);
            Assert.That(_loot.Count, Is.Zero);
        }

        [Test]
        public void ARefusedRewardLeavesTheMonsterClaimableAndDropsNothing()
        {
            AuthorRat();

            LivingMonster corpse = Corpse(Rat);

            // No such killer, so nothing is owed and nothing is claimed.
            Assert.That(Authority().Grant(corpse.Instance, new InstanceId("nobody"))
                .IsGranted, Is.False);
            Assert.That(corpse.State.IsDefeatClaimed, Is.False);
            Assert.That(_loot.Count, Is.Zero);
        }

        // ---- 15-18: ownership ------------------------------------------------------------------

        [Test]
        public void TheKillerOwnsWhatFell()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Policy, Is.EqualTo(LootPolicy.OwnerOnly));
            Assert.That(pile.EligibleCharacter.Value, Is.EqualTo("char-killer"));
        }

        [Test]
        public void ABystanderCannotTakeIt()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");
            LivingCharacter bystander = AddPlayer("char-bystander");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-bystander"));

            Assert.That(outcome.IsAccepted, Is.False);
            Assert.That(outcome.Reason, Is.EqualTo(LootPickupRejection.NotEligible));
            Assert.That(bystander.Inventory.OccupiedSlots, Is.Zero);
        }

        [Test]
        public void ACharacterThisServerDoesNotHoldCannotTakeAnything()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(_loot.Pickup(result.LootPile, 0, new CharacterId("char-ghost"))
                .Reason, Is.EqualTo(LootPickupRejection.NotEligible));
            Assert.That(_loot.Pickup(result.LootPile, 0, default).Reason,
                Is.EqualTo(LootPickupRejection.NotEligible));
        }

        [Test]
        public void NothingLetsACallerChangeWhoOwnsAPile()
        {
            foreach (string absent in new[] { "SetOwner", "SetPolicy", "SetEligible" })
            {
                Assert.That(typeof(LootObjectState).GetMethod(absent), Is.Null,
                    absent + " must not exist");
            }

            Assert.That(typeof(LootObjectState).GetProperty("EligibleCharacter").CanWrite,
                Is.False);
            Assert.That(typeof(LootObjectState).GetProperty("Policy").CanWrite, Is.False);
        }

        // ---- 19-26: pickup ---------------------------------------------------------------------

        [Test]
        public void AValidPickupPutsTheItemInTheBag()
        {
            AuthorRat(min: 3, max: 3);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(outcome.IsAccepted, Is.True, outcome.ToString());
            Assert.That(outcome.Pickup.QuantityTaken, Is.EqualTo(3));
            Assert.That(killer.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(3));
            Assert.That(outcome.IsPersisted, Is.True, "and it was written down");
        }

        [Test]
        public void ASecondPickupOfTheSameEntryIsRefused()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(_loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"))
                .IsAccepted, Is.True);

            LootPickupOutcome second = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Reason, Is.EqualTo(LootPickupRejection.AlreadyTaken));
            Assert.That(killer.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(1),
                "one item, however many times it was asked for");
        }

        [Test]
        public void FivePickupsProduceOneItem()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            var accepted = 0;

            for (int i = 0; i < 5; i++)
            {
                if (_loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"))
                    .IsAccepted)
                {
                    accepted++;
                }
            }

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(killer.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(1));
        }

        [Test]
        public void AFullBagRefusesAndTheLootStaysWhereItIs()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            // Relic has a maximum stack of one, so four of them fill a four-slot bag.
            for (int i = 0; i < Capacity; i++)
            {
                killer.Inventory.Add(new ItemInstance(InstanceId.New(),
                    new DefinitionId(Relic), killer.Owner, 1), Items);
            }

            Assert.That(killer.Inventory.IsFull, Is.True, "precondition");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(outcome.Reason, Is.EqualTo(LootPickupRejection.InventoryFull));
            Assert.That(killer.Inventory.OccupiedSlots, Is.EqualTo(Capacity),
                "no partial transfer");

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.IsTaken(0), Is.False, "the loot is still there to come back for");
            Assert.That(_store.Saved, Is.Empty, "and nothing was written");
        }

        [Test]
        public void APickupStacksByPhase08sRulesAndReturnsWhatWillNotFit()
        {
            // Coin stacks to 999. A bag holding 997 has room for two of a stack of five.
            AddDropTable(StackTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 5, 5),
            });

            AddMonster(Rat, level: 5, lootTable: StackTable);

            LivingCharacter killer = AddPlayer("char-killer");

            killer.Inventory.Add(new ItemInstance(InstanceId.New(), new DefinitionId(Coin),
                killer.Owner, 997), Items);

            for (int i = 1; i < Capacity; i++)
            {
                killer.Inventory.Add(new ItemInstance(InstanceId.New(),
                    new DefinitionId(Relic), killer.Owner, 1), Items);
            }

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(outcome.IsAccepted, Is.True);
            Assert.That(outcome.Pickup.QuantityTaken, Is.EqualTo(2));
            Assert.That(outcome.Pickup.Remainder, Is.EqualTo(3));
            Assert.That(killer.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(999));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Contents[0].Quantity, Is.EqualTo(3),
                "what would not fit stays in the world rather than being destroyed");
        }

        [Test]
        public void ANonStackableItemTakesAWholeSlot()
        {
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(Relic), 1, 1),
                new DropEntry(new DefinitionId(Relic), 1, 1),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"));
            _loot.Pickup(result.LootPile, 1, new CharacterId("char-killer"));

            Assert.That(killer.Inventory.OccupiedSlots, Is.EqualTo(2),
                "two relics do not stack");
        }

        [Test]
        public void LootCannotBeTakenFromAnotherMap()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer", HomeMap);
            AddPlayer("char-tourist", OtherMap);

            MonsterRewardResult result = Authority().Grant(
                Corpse(Rat, HomeMap).Instance, Of(killer));

            // The tourist is not the owner either, but the map is checked first and on its
            // own terms: a pile is part of the world it fell in.
            Assert.That(_loot.TryGetMap(result.LootPile, out DefinitionId map), Is.True);
            Assert.That(map.Value, Is.EqualTo(HomeMap));
            Assert.That(_loot.Pickup(result.LootPile, 0, new CharacterId("char-tourist"))
                .Reason, Is.EqualTo(LootPickupRejection.NotEligible));
        }

        [Test]
        public void ExpiredLootIsGoneAndCannotBeTaken()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority(lifetime: 10f)
                .Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(_loot.Count, Is.EqualTo(1));

            _loot.Tick(4f);

            Assert.That(_loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"))
                .IsAccepted, Is.True, "still fresh");

            MonsterRewardResult second = Authority(lifetime: 10f)
                .Grant(Corpse(Rat).Instance, Of(killer));

            _loot.Tick(11f);

            Assert.That(_loot.Count, Is.Zero, "swept once nobody can take it");
            Assert.That(_loot.Pickup(second.LootPile, 0, new CharacterId("char-killer"))
                .Reason, Is.EqualTo(LootPickupRejection.AlreadyTaken));
        }

        [Test]
        public void AnEmptiedPileLeavesTheWorld()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"));

            Assert.That(_loot.Count, Is.Zero,
                "a pile with nothing in it is a request that can only be refused");
        }

        // ---- 27-30: atomicity ---------------------------------------------------------------

        [Test]
        public void AFailedSaveKeepsTheItemAndSaysSo()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _store.FailWith = CharacterPersistenceFailure.Unreachable;

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(outcome.IsAccepted, Is.True, "the item is in the bag");
            Assert.That(outcome.IsPersisted, Is.False, "and the database has not heard");
            Assert.That(outcome.PersistenceFailure,
                Is.EqualTo(CharacterPersistenceFailure.Unreachable));
            Assert.That(killer.Inventory.CountOf(new DefinitionId(Coin)), Is.EqualTo(1));
            Assert.That(killer.IsDirty, Is.True, "still queued for the next save");
        }

        [Test]
        public void APersistenceRetryWritesTheSameBagAndNotASecondItem()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _store.FailWith = CharacterPersistenceFailure.Unreachable;

            _loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"));

            Assert.That(_store.Saved, Is.Empty, "precondition");

            _store.FailWith = CharacterPersistenceFailure.None;

            Assert.That(_players.Save(killer).IsOk, Is.True);
            Assert.That(_store.Saved, Has.Count.EqualTo(1));
            Assert.That(_store.Saved[0].Items, Has.Count.EqualTo(1));
            Assert.That(_store.Saved[0].Items[0].Quantity, Is.EqualTo(1),
                "the retry wrote the bag as it is, not a second coin");
        }

        [Test]
        public void ARefusedPickupWritesNothingAtAll()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");
            AddPlayer("char-bystander");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.Pickup(result.LootPile, 0, new CharacterId("char-bystander"));

            Assert.That(_store.Saved, Is.Empty);
        }

        [Test]
        public void TheSavedBagCarriesInstanceIdentityAndSlot()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            _loot.Pickup(result.LootPile, 0, new CharacterId("char-killer"));

            PersistedItem saved = _store.Saved[0].Items[0];

            Assert.That(saved.Item.Value, Is.EqualTo(Coin));
            Assert.That(saved.Instance.IsValid, Is.True,
                "an item without its own identity could not be traded, locked or found again");
            Assert.That(saved.SlotIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(_store.Saved[0].InventoryCapacity, Is.EqualTo(Capacity));
        }

        // ---- 31-33: World Boss ------------------------------------------------------------------

        [Test]
        public void AWorldBossOnlyEntryNeverDropsFromAnOrdinaryMonster()
        {
            AddDropTable(BossTable, new[]
            {
                new DropEntry(new DefinitionId(Fruit), 1, 1, 0f, default, 0, 0, true,
                    MonsterRank.WorldBoss),
            });

            AddMonster(Rat, level: 5, lootTable: BossTable, rank: MonsterRank.Normal);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority(AlwaysSucceeds.Instance)
                .Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.False,
                "a guaranteed roll must still not hand a rat a World Boss drop");
        }

        [Test]
        public void AWorldBossDropsItsOwnRestrictedEntry()
        {
            AddDropTable(BossTable, new[]
            {
                new DropEntry(new DefinitionId(Fruit), 1, 1, 0f, default, 0, 0, true,
                    MonsterRank.WorldBoss),
            });

            AddMonster(Overlord, level: 60, lootTable: BossTable,
                rank: MonsterRank.WorldBoss);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority()
                .Grant(Corpse(Overlord).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.True);

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Contents[0].Item.Value, Is.EqualTo(Fruit));
        }

        [Test]
        public void TheRankGateIsAFloorSoARarerMonsterStillDropsCommonEntries()
        {
            AddDropTable(BossTable, new[]
            {
                new DropEntry(new DefinitionId(Coin), 1, 1),
                new DropEntry(new DefinitionId(Fruit), 1, 1, 0f, default, 0, 0, true,
                    MonsterRank.Boss),
            });

            AddMonster(Overlord, level: 60, lootTable: BossTable,
                rank: MonsterRank.WorldBoss);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority()
                .Grant(Corpse(Overlord).Instance, Of(killer));

            _loot.TryGet(result.LootPile, out LootObjectState pile);

            Assert.That(pile.Count, Is.EqualTo(2),
                "a World Boss also drops what a Boss drops");
        }

        [Test]
        public void TheRestrictionIsReadOffTheMonsterThatDiedAndNotFromTheCaller()
        {
            // The context a caller supplies carries Normal; the monster overload replaces it
            // with the rank of whatever actually died. That is the whole mechanism.
            AddDropTable(BossTable, new[]
            {
                new DropEntry(new DefinitionId(Fruit), 1, 1, 0f, default, 0, 0, true,
                    MonsterRank.WorldBoss),
            });

            AddMonster(Overlord, level: 60, lootTable: BossTable,
                rank: MonsterRank.WorldBoss);

            MonsterRuntimeState boss = Spawn(Overlord);

            var loot = new List<LootResult>();

            // A caller claiming nothing about rank still gets the World Boss entry, because
            // the monster is the authority on what it is.
            DropResolver.Resolve(boss,
                new DropResolver.Context(Items, DropTables, AlwaysSucceeds.Instance), loot);

            Assert.That(loot, Has.Count.EqualTo(1));

            // And naming the table directly, with no monster, gets nothing: an unknown rank
            // is treated as ordinary rather than as permitted.
            loot.Clear();

            DropResolver.Resolve(boss.InstanceId, new DefinitionId(BossTable),
                new DropResolver.Context(Items, DropTables, AlwaysSucceeds.Instance), loot);

            Assert.That(loot, Is.Empty,
                "fail closed: a caller that does not say what died gets no restricted drops");
        }

        // ---- 34-36: the special items are ordinary items -------------------------------------

        [TestCase(Fruit)]
        [TestCase(Card)]
        [TestCase(PetEgg)]
        public void ACollectibleTravelsTheOrdinaryPipeline(string item)
        {
            // No special-case generator: a Devil Fruit, a Card and a Pet egg are item
            // definitions on drop entries, and the same roll, pile, pickup and bag handle
            // all three.
            AddDropTable(RareTable, new[]
            {
                new DropEntry(new DefinitionId(item), 1, 1, 0.25f),
            });

            AddMonster(Rat, level: 5, lootTable: RareTable);

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority(new ThresholdResultSource(0.1f))
                .Grant(Corpse(Rat).Instance, Of(killer));

            Assert.That(result.HasLoot, Is.True);

            LootPickupOutcome outcome = _loot.Pickup(result.LootPile, 0,
                new CharacterId("char-killer"));

            Assert.That(outcome.IsAccepted, Is.True);
            Assert.That(killer.Inventory.CountOf(new DefinitionId(item)), Is.EqualTo(1));
        }

        // ---- 37-42: the client decides none of it ---------------------------------------------

        [Test]
        public void TheAuthoredDropTableCannotBeChangedAtRuntime()
        {
            foreach (string property in new[]
                     { "Chance", "MinQuantity", "MaxQuantity", "Item", "MinMonsterRank" })
            {
                Assert.That(typeof(DropEntry).GetProperty(property).CanWrite, Is.False,
                    property + " is authored content, not runtime state");
            }
        }

        [Test]
        public void NothingLetsACallerPutAnItemIntoAPile()
        {
            foreach (string absent in new[] { "Add", "AddItem", "SetContents", "Insert" })
            {
                Assert.That(typeof(LootObjectState).GetMethod(absent), Is.Null,
                    absent + " would let loot be invented after the roll");
            }
        }

        [Test]
        public void APickupNamesAPileAndAnEntryAndNothingElse()
        {
            AuthorRat();

            LivingCharacter killer = AddPlayer("char-killer");

            MonsterRewardResult result = Authority().Grant(Corpse(Rat).Instance, Of(killer));

            // An index outside the pile is refused rather than wrapped or clamped.
            Assert.That(_loot.Pickup(result.LootPile, 99, new CharacterId("char-killer"))
                .IsAccepted, Is.False);
            Assert.That(_loot.Pickup(result.LootPile, -1, new CharacterId("char-killer"))
                .IsAccepted, Is.False);
            Assert.That(_loot.Pickup(new InstanceId("no-such-pile"), 0,
                new CharacterId("char-killer")).Reason,
                Is.EqualTo(LootPickupRejection.AlreadyTaken));

            Assert.That(killer.Inventory.OccupiedSlots, Is.Zero);
        }

        [Test]
        public void ThereIsNoPathFromAConnectionToLoot()
        {
            foreach (System.Reflection.MethodInfo method in
                typeof(MonsterLootRegistry).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.Name.ToLowerInvariant(),
                        Does.Not.Contain("connection"), method.Name);
                }
            }
        }

        [Test]
        public void AServerWithNoItemRegistryHoldsNoBagAndHandsOutNothing()
        {
            AuthorRat();

            // A world composed without items: characters have no inventory at all, which is
            // the honest answer rather than a bag that accepts ids it cannot resolve.
            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.bare", HomeMap));

            var bare = new WorldCharacterRegistry(_store, spawns);

            _store.Rows["session-char-bare"] = new PersistedCharacter(
                new CharacterId("char-bare"), new AccountId("acc-bare"),
                new ServerId("srv-1"), "char-bare", 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1);

            WorldSpawnResult spawned = bare.Spawn(99,
                WorldAdmission.Admitted(new SessionId("session-char-bare"),
                    new AccountId("acc-bare"), new CharacterId("char-bare"),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(spawned.Character.Inventory, Is.Null);

            var registry = new MonsterLootRegistry(bare, Items);

            Assert.That(registry.Pickup(InstanceId.New(), 0, new CharacterId("char-bare"))
                .IsAccepted, Is.False);
        }

        // ---- the production generator ------------------------------------------------------------

        [Test]
        public void TheRealGeneratorNeverDropsAZeroChanceAndAlwaysDropsACertainOne()
        {
            var random = new SystemRandomSource(20260904);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(random.Succeeds(1f), Is.True, "a certainty must never fail");
            }

            // Zero or less is the authored "guaranteed", matching every other implementation.
            Assert.That(random.Succeeds(0f), Is.True);
            Assert.That(random.Succeeds(float.NaN), Is.False, "a broken row is not a drop");
        }

        [Test]
        public void TheRealGeneratorStaysInsideAnAuthoredRange()
        {
            var random = new SystemRandomSource(7);

            for (int i = 0; i < 500; i++)
            {
                int value = random.Range(2, 5);

                Assert.That(value, Is.InRange(2, 5));
            }

            Assert.That(random.Range(4, 4), Is.EqualTo(4));
            Assert.That(random.Range(9, 3), Is.EqualTo(9), "an inverted range is a fixed one");
        }

        [Test]
        public void TheRealGeneratorReplaysExactlyFromASeed()
        {
            var first = new SystemRandomSource(4242);
            var second = new SystemRandomSource(4242);

            for (int i = 0; i < 50; i++)
            {
                Assert.That(second.Succeeds(0.5f), Is.EqualTo(first.Succeeds(0.5f)));
                Assert.That(second.Range(1, 100), Is.EqualTo(first.Range(1, 100)));
            }
        }

        [Test]
        public void AOneInTenMillionChanceIsNotRoundedIntoSomethingCommon()
        {
            // 1e-7 is below the spacing of a float near a typical roll, which is why the
            // comparison is done in double. Ten thousand rolls of a one-in-ten-million
            // chance should produce nothing; if the value were being widened wrongly or
            // read as a percentage, this would fail almost immediately.
            var random = new SystemRandomSource(99);

            for (int i = 0; i < 10000; i++)
            {
                Assert.That(random.Succeeds(1e-7f), Is.False);
            }
        }
    }
}
