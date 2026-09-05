// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world does this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// The Ancient Slime King's card, from the boss that drops it to the sword it ends up in.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is put in place by hand.</b> The card arrives because the boss dropped it,
    /// the socket happens because a connection asked the authority for it, and the stat
    /// changes because the canonical resolver was asked again. Every step is the shipped one.
    ///
    /// <b>The rates are the fragile part.</b> One defeat owes exactly one roll at 1e-7 for
    /// the fruit and exactly one at 1e-6 for the card, whether one player killed it or six.
    /// A per-member roll would be invisible in play and enormous in aggregate.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldCardTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string TrainingSlime = "monster.training_slime";

        private const string CardId = "card.ancient_slime_king";
        private const string BladeId = "item.apprentice_cutlass";
        private const string DarknessItem = "item.devil_fruit.darkness";

        private const float FruitChance = 0.0000001f;
        private const float CardChance = 0.000001f;

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

        private sealed class ScriptedRandom : IRandomResultSource, IRandomRangeSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public readonly List<float> ChancesAsked = new List<float>();

            public int RollsAt(float chance)
            {
                return ChancesAsked.Count(c => Mathf.Abs(c - chance) < chance * 0.001f);
            }

            public bool Succeeds(float chance)
            {
                ChancesAsked.Add(chance);

                return _roll < chance;
            }

            public int Range(int min, int max) => min;
        }

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private CharacterStore _characters;
        private ScriptedRandom _rolls;
        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return TearDownWorld();
        }

        // ---- A, C: socketing, and what it is worth ---------------------------------------

        [UnityTest]
        public IEnumerator ACharacterSocketsTheirOwnCardIntoTheirOwnBladeAndGainsTheStat()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true, withCard: true);

            yield return Tick();

            int bladeSlot = Slot(hero, BladeId);
            int cardSlot = Slot(hero, CardId);

            Assert.That(bladeSlot, Is.GreaterThanOrEqualTo(0), "no blade in the bag");
            Assert.That(cardSlot, Is.GreaterThanOrEqualTo(0), "no card in the bag");

            Assert.That(Submit(hero, InventoryAction.Equip, bladeSlot, 0).IsAccepted,
                Is.True, "the blade would not equip");

            yield return Settle();

            int maxBefore = hero.Combatant.MaxHealth;

            // The blade is worn, so its inventory slot is empty now -- the piece is socketed
            // where it lives, which for a worn piece is the equipment aggregate.
            bladeSlot = Slot(hero, BladeId);

            CharacterInventoryResult socketed = bladeSlot >= 0
                ? Submit(hero, InventoryAction.SocketCard, cardSlot, bladeSlot)
                : SocketIntoWorn(hero, cardSlot);

            Assert.That(socketed.IsAccepted, Is.True,
                "the socket request was refused: " + socketed.Rejection + " / "
                + socketed.Card.Reason);

            yield return Settle();

            Assert.That(Carried(hero, CardId), Is.Zero,
                "the card is still loose in the bag after being socketed");

            Assert.That(hero.Combatant.MaxHealth, Is.GreaterThan(maxBefore),
                "socketing the card changed no authoritative stat");

            // Five percent, through the canonical calculation and nothing else.
            Assert.That(hero.Combatant.MaxHealth,
                Is.EqualTo(Mathf.RoundToInt(maxBefore * 1.05f)).Within(1),
                "the card's five percent did not arrive as five percent");
        }

        [UnityTest]
        public IEnumerator ACharacterCannotSocketIntoSomebodyElsesEquipment()
        {
            yield return LoadWorld();

            LivingCharacter ann = Admit("char-ann", 1, withBlade: true, withCard: true);
            LivingCharacter ben = Admit("char-ben", 2, withBlade: true);

            yield return Tick();

            // char-ben asks, naming slots that are meaningful only in char-ann's bag. The
            // connection decides whose inventory is read, so this can only ever touch ben's.
            CharacterInventoryResult refused = Submit(ben, InventoryAction.SocketCard,
                Slot(ann, CardId), Slot(ben, BladeId));

            Assert.That(refused.IsAccepted, Is.False,
                "a player socketed a card they do not own");

            Assert.That(Carried(ann, CardId), Is.EqualTo(1),
                "another player's card was consumed");
        }

        // ---- D: the effect follows the equipment -------------------------------------------

        [UnityTest]
        public IEnumerator TakingTheBladeOffRemovesTheCardsEffectAndPuttingItBackReturnsIt()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true, withCard: true);

            yield return Tick();

            int bare = hero.Combatant.MaxHealth;

            yield return SocketAndWear(hero);

            int carded = hero.Combatant.MaxHealth;

            Assert.That(carded, Is.GreaterThan(bare));

            Assert.That(Submit(hero, InventoryAction.Unequip,
                (int)EquipmentSlot.MainHand, 0).IsAccepted, Is.True,
                "the blade would not come off");

            yield return Settle();

            Assert.That(hero.Combatant.MaxHealth, Is.EqualTo(bare),
                "a card in a sword in the bag was still buffing the character");

            Assert.That(Submit(hero, InventoryAction.Equip, Slot(hero, BladeId), 0)
                .IsAccepted, Is.True);

            yield return Settle();

            Assert.That(hero.Combatant.MaxHealth, Is.EqualTo(carded),
                "putting the carded blade back did not restore the effect");
        }

        // ---- E: it survives coming back -----------------------------------------------------

        [UnityTest]
        public IEnumerator TheSocketAndItsEffectSurviveAReconnect()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true, withCard: true);

            yield return Tick();
            yield return SocketAndWear(hero);

            int carded = hero.Combatant.MaxHealth;

            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            yield return Tick();

            LivingCharacter back = Admit("char-ann", 1);

            yield return Settle();

            Assert.That(back.Combatant.MaxHealth, Is.EqualTo(carded),
                "the card's effect did not survive the reconnect");

            Assert.That(Carried(back, CardId), Is.Zero,
                "the consumed card reappeared as a loose item");

            Assert.That(WornCardCount(back), Is.EqualTo(1),
                "the socket did not come back exactly once");
        }

        [UnityTest]
        public IEnumerator TheSocketSurvivesTheWholeWorldBeingRebuilt()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true, withCard: true);

            yield return Tick();
            yield return SocketAndWear(hero);

            int carded = hero.Combatant.MaxHealth;

            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            yield return Tick();
            yield return TearDownWorld();
            yield return LoadWorld();

            LivingCharacter back = Admit("char-ann", 1);

            yield return Settle();

            Assert.That(WornCardCount(back), Is.EqualTo(1),
                "the socket did not survive the world restart");

            Assert.That(back.Combatant.MaxHealth, Is.EqualTo(carded));
        }

        [UnityTest]
        public IEnumerator TwoBladesOfTheSameKindDoNotShareOneCard()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true, withCard: true,
                secondBlade: true);

            yield return Tick();

            // Socket the first one only.
            int first = Slot(hero, BladeId);

            Assert.That(Submit(hero, InventoryAction.SocketCard, Slot(hero, CardId), first)
                .IsAccepted, Is.True);

            yield return Settle();

            InstanceId cardedBlade = Instance(hero, first);

            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            yield return Tick();

            LivingCharacter back = Admit("char-ann", 1);

            yield return Settle();

            var carded = 0;
            var empty = 0;

            for (var i = 0; i < back.Inventory.Capacity; i++)
            {
                var piece = back.Inventory.GetSlot(i).Content as EquipmentInstance;

                if (piece == null || piece.DefinitionId.Value != BladeId) continue;

                if (piece.CardCount > 0)
                {
                    carded++;

                    Assert.That(piece.InstanceId, Is.EqualTo(cardedBlade),
                        "the card moved to a different sword across the reconnect");
                }
                else
                {
                    empty++;
                }
            }

            Assert.That(carded, Is.EqualTo(1), "the card spread to more than one sword");
            Assert.That(empty, Is.EqualTo(1), "the second sword was not left alone");
        }

        // ---- F, G, H: the drop ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator OneBossDefeatRollsTheCardOnceAndTheFruitOnce()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn(Boss));

            Assert.That(_rolls.RollsAt(CardChance), Is.EqualTo(1),
                "the card was not rolled exactly once");

            Assert.That(_rolls.RollsAt(FruitChance), Is.EqualTo(1),
                "the fruit was not rolled exactly once");

            // Both succeeded at roll zero, and both are on the ground.
            LootObjectState pile = _bootstrap.Loot.All()[0];

            Assert.That(pile.Contents.Any(c => c.Item.Value == CardId), Is.True,
                "the card did not drop");
            Assert.That(pile.Contents.Any(c => c.Item.Value == DarknessItem), Is.True,
                "the fruit did not drop");
        }

        [UnityTest]
        public IEnumerator ASixPlayerPartyDoesNotMultiplyEitherRareRoll()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter[] members = Enumerable.Range(0, 6)
                .Select(i => Admit("char-" + i, i + 1)).ToArray();

            var party = new PartyState(new PartyId("party-cards"), members[0].Character,
                PartyLootPolicy.Personal);

            for (var i = 1; i < members.Length; i++) party.TryAdd(members[i].Character);

            Assert.That(_bootstrap.Parties.Register(party), Is.True);

            Kill(members[0], Spawn(Boss));

            Assert.That(_rolls.RollsAt(CardChance), Is.EqualTo(1),
                "six members produced " + _rolls.RollsAt(CardChance) + " card rolls");

            Assert.That(_rolls.RollsAt(FruitChance), Is.EqualTo(1),
                "six members produced " + _rolls.RollsAt(FruitChance) + " fruit rolls");

            // And one pile, owned by the policy's claimant alone.
            Assert.That(_bootstrap.Loot.All().Count, Is.EqualTo(1));
            Assert.That(_bootstrap.Loot.All()[0].EligibleCharacter,
                Is.EqualTo(members[0].Character));
        }

        [UnityTest]
        public IEnumerator TheTrainingSlimeCannotDropTheBossCardEvenOnAPerfectRoll()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn(TrainingSlime));

            Assert.That(_rolls.RollsAt(CardChance), Is.Zero,
                "a training slime rolled the world boss card");

            foreach (LootObjectState pile in _bootstrap.Loot.All())
            {
                Assert.That(pile.Contents.Any(c => c.Item.Value == CardId), Is.False,
                    "a training slime dropped the world boss card");
            }
        }

        // ---- I, J: the whole thing --------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheCardSurvivesARewardRestartWithTheIdentityItWasDecidedWith()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1);

            Kill(hero, Spawn(Boss));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            InstanceId decided = pile.Contents.First(c => c.Item.Value == CardId).Instance;

            Assert.That(decided.IsValid, Is.True,
                "the card drop was given no durable identity");

            yield return TearDownWorld();
            yield return LoadWorld(roll: 0f);

            Admit("char-ann", 1);

            yield return Tick();

            // No outbox is composed here, so nothing is recovered -- what this pins is that
            // the identity was decided at the drop rather than at the pickup.
            Assert.That(_rolls.RollsAt(CardChance), Is.Zero);
        }

        [UnityTest]
        public IEnumerator TheWholeSliceRunsFromBossToASocketedSwordAfterAReconnect()
        {
            yield return LoadWorld(roll: 0f);

            LivingCharacter hero = Admit("char-ann", 1, withBlade: true);

            yield return Tick();

            Kill(hero, Spawn(Boss));

            Assert.That(_rolls.RollsAt(CardChance), Is.EqualTo(1));

            LootObjectState pile = _bootstrap.Loot.All()[0];

            int cardEntry = -1;

            for (var i = 0; i < pile.Contents.Count; i++)
            {
                if (pile.Contents[i].Item.Value == CardId) cardEntry = i;
            }

            Assert.That(cardEntry, Is.GreaterThanOrEqualTo(0), "the boss dropped no card");

            StandOn(hero, pile);

            Assert.That(_bootstrap.LootAuthority.Apply(hero.ConnectionId,
                pile.LootId.Value, cardEntry, ++_sequence).IsAccepted, Is.True,
                "the card could not be picked up");

            Assert.That(Carried(hero, CardId), Is.EqualTo(1));

            yield return SocketAndWear(hero);

            int carded = hero.Combatant.MaxHealth;

            Assert.That(Carried(hero, CardId), Is.Zero, "the card was not consumed");

            Assert.That(_bootstrap.Simulation.Release(hero.ConnectionId).IsOk, Is.True);

            yield return Tick();

            LivingCharacter back = Admit("char-ann", 1);

            yield return Settle();

            Assert.That(WornCardCount(back), Is.EqualTo(1),
                "the socket did not survive the reconnect");

            Assert.That(back.Combatant.MaxHealth, Is.EqualTo(carded),
                "the card's effect did not come back exactly once");

            Assert.That(Carried(back, CardId), Is.Zero);

            // The counter spans the whole test, so one is the kill's roll and nothing else:
            // the reconnect added none.
            Assert.That(_rolls.RollsAt(CardChance), Is.EqualTo(1),
                "the reconnect rolled the card again");
        }

        // ---- harness -------------------------------------------------------------------------

        /// <summary>Sockets the card into the blade, then wears it.</summary>
        private IEnumerator SocketAndWear(LivingCharacter hero)
        {
            int blade = Slot(hero, BladeId);
            int card = Slot(hero, CardId);

            Assert.That(blade, Is.GreaterThanOrEqualTo(0), "no blade to socket into");
            Assert.That(card, Is.GreaterThanOrEqualTo(0), "no card to socket");

            Assert.That(Submit(hero, InventoryAction.SocketCard, card, blade).IsAccepted,
                Is.True, "the socket request was refused");

            yield return Settle();

            Assert.That(Submit(hero, InventoryAction.Equip, Slot(hero, BladeId), 0)
                .IsAccepted, Is.True, "the carded blade would not equip");

            yield return Settle();
        }

        /// <summary>Sockets into the piece already being worn, when it is no longer in the bag.</summary>
        private CharacterInventoryResult SocketIntoWorn(LivingCharacter hero, int cardSlot)
        {
            Assert.That(Submit(hero, InventoryAction.Unequip,
                (int)EquipmentSlot.MainHand, 0).IsAccepted, Is.True);

            CharacterInventoryResult result = Submit(hero, InventoryAction.SocketCard,
                cardSlot, Slot(hero, BladeId));

            Submit(hero, InventoryAction.Equip, Slot(hero, BladeId), 0);

            return result;
        }

        private CharacterInventoryResult Submit(LivingCharacter character,
            InventoryAction action, int from, int to)
        {
            CharacterInventoryAuthority inventory = _bootstrap.Simulation.Inventory(
                _bootstrap.Replication);

            inventory.Submit(character.ConnectionId, action, from, to, 1, ++_sequence);

            return inventory.LastResult;
        }

        private IEnumerator Settle()
        {
            _bootstrap.Simulation.Tick(0.1f);

            yield return null;
        }

        private IEnumerator LoadWorld(float roll = 1f)
        {
            _rolls = new ScriptedRandom(roll);

            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();
            _bootstrap.UseRandom(_rolls, _rolls);
            _bootstrap.Compose(new AlwaysAdmits(), default, _characters);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            Assert.That(_bootstrap.Cards, Is.Not.Null,
                "the shipped world composed no card registry");

            yield return null;
        }

        private IEnumerator TearDownWorld()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
                _bootstrap = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private LivingCharacter Admit(string character, int connection,
            bool withBlade = false, bool withCard = false, bool secondBlade = false)
        {
            string session = "session-" + character;

            if (!_characters.Rows.ContainsKey(session))
            {
                var items = new List<PersistedItem>();
                var slot = 0;

                if (withBlade)
                {
                    items.Add(new PersistedItem(InstanceId.New(),
                        new DefinitionId(BladeId), 1, slot++));
                }

                if (secondBlade)
                {
                    items.Add(new PersistedItem(InstanceId.New(),
                        new DefinitionId(BladeId), 1, slot++));
                }

                if (withCard)
                {
                    items.Add(new PersistedItem(InstanceId.New(),
                        new DefinitionId(CardId), 1, slot++));
                }

                _characters.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 30, 0, 104, 35,
                    new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1, items.Count == 0 ? null : items);
            }

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private LivingMonster Spawn(string monster)
        {
            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(monster), default,
                0f, 1, 0f, new DefinitionId(StarterMap)));

            monsters.PopulateAll();

            IReadOnlyList<LivingMonster> alive = monsters.All();

            for (var i = alive.Count - 1; i >= 0; i--)
            {
                if (alive[i].State.Definition.Id.Value == monster && alive[i].IsAlive)
                {
                    return alive[i];
                }
            }

            Assert.Fail("no living " + monster);

            return null;
        }

        private void Kill(LivingCharacter hero, LivingMonster monster)
        {
            _bootstrap.Simulation.Monsters().TryResolve(monster.Instance,
                out ICombatant target);

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                _bootstrap.Simulation.Combat().Tick(10f);

                ServerCombatResult result = _bootstrap.Simulation.Combat().Execute(
                    hero.ConnectionId, new CombatCommand(hero.Character, monster.Instance,
                        default, 0, ++_sequence));

                if (!result.IsAccepted) break;
            }

            Assert.That(target.CurrentHealth, Is.Zero, "the monster would not die");
        }

        private static void StandOn(LivingCharacter character, LootObjectState pile)
        {
            character.Combatant.Position = new CombatPosition(pile.Position.X,
                pile.Position.Y, pile.Position.Z);
        }

        private static int Slot(LivingCharacter character, string item)
        {
            if (character.Inventory == null) return -1;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                GameInstance held = character.Inventory.GetSlot(i).Content;

                if (held != null && held.DefinitionId.Value == item) return i;
            }

            return -1;
        }

        private static InstanceId Instance(LivingCharacter character, int slot)
        {
            GameInstance held = character.Inventory.GetSlot(slot).Content;

            return held == null ? default : held.InstanceId;
        }

        private static int Carried(LivingCharacter character, string item)
        {
            if (character.Inventory == null) return 0;

            var count = 0;

            for (var i = 0; i < character.Inventory.Capacity; i++)
            {
                GameInstance held = character.Inventory.GetSlot(i).Content;

                if (held == null || held.DefinitionId.Value != item) continue;

                var stack = held as ItemInstance;

                count += stack == null ? 1 : stack.Quantity;
            }

            return count;
        }

        /// <summary>How many cards are in the piece this character is wearing.</summary>
        private static int WornCardCount(LivingCharacter character)
        {
            if (character.Equipment == null) return 0;

            var total = 0;

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> worn
                in character.Equipment.Equipped)
            {
                if (worn.Value != null) total += worn.Value.CardCount;
            }

            return total;
        }
    }
}

#endif
