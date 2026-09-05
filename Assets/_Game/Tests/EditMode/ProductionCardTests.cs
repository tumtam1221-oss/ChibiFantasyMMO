using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The shipped Ancient Slime King card, as content and as a modifier.
    /// </summary>
    /// <remarks>
    /// <b>Nothing here builds a card system.</b> Phase 12 already decided what a card is,
    /// what it fits, how it sockets and how its modifiers reach a character. These tests are
    /// about one authored card and one authored piece of equipment actually existing in the
    /// shipped catalogue, with the exact numbers the design calls for.
    ///
    /// <b>The chance is the point.</b> One in a million, authored as a fraction in content
    /// and never converted from a percentage at runtime -- a rate that is wrong by a factor
    /// of ten is indistinguishable from a rate that is right until somebody farms it for a
    /// month.
    /// </remarks>
    [TestFixture]
    internal sealed class ProductionCardTests
    {
        private const string Catalogue =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string CardId = "card.ancient_slime_king";
        private const string BladeId = "item.apprentice_cutlass";
        private const string Boss = "monster.ancient_slime_king";
        private const string TrainingSlime = "monster.training_slime";
        private const string BossTable = "drop.ancient_slime_king";
        private const string Fruit = "item.devil_fruit.darkness";

        private WorldContentCatalogue _content;

        [SetUp]
        public void SetUp()
        {
            _content = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(Catalogue);

            Assert.That(_content, Is.Not.Null, "the shipped catalogue is missing");
        }

        // ---- the card exists, as both halves --------------------------------------------

        [Test]
        public void TheProductionCardIsInTheShippedCatalogue()
        {
            Assert.That(_content.BuildCards().TryGet(new DefinitionId(CardId),
                out CardDefinition card), Is.True, "the card is not shipped");

            Assert.That(card.Enabled, Is.True);
            Assert.That(card.SourceMonster.Value, Is.EqualTo(Boss));
            Assert.That(card.DropTable.Value, Is.EqualTo(BossTable));
        }

        [Test]
        public void TheCardHasAnItemFormUnderTheSameIdBecauseThatIsHowItIsResolved()
        {
            // CardSocketService looks the card up by the item's own DefinitionId. An item
            // that did not match its card could never be socketed at all.
            Assert.That(_content.BuildItems().TryGet(new DefinitionId(CardId),
                out ItemDefinition item), Is.True, "the card has no item form");

            Assert.That(item.Category, Is.EqualTo(ItemCategory.Card),
                "the card item is not authored as a card");

            Assert.That(item.Stackable, Is.False,
                "a stacking card would make one instance two");
        }

        [Test]
        public void TheCardGrantsItsEffectThroughOrdinaryStatModifierData()
        {
            // Not a branch on the card's id anywhere: a modifier the generic resolver
            // already understands.
            _content.BuildCards().TryGet(new DefinitionId(CardId), out CardDefinition card);

            Assert.That(card.StatModifiers.Length, Is.EqualTo(1));

            StatModifier modifier = card.StatModifiers[0];

            Assert.That(modifier.Stat.Value, Is.EqualTo("stat.max_hp"));
            Assert.That(modifier.Kind, Is.EqualTo(StatModifierKind.Percent));
            // A fraction, not a percentage number: the calculator scales by ten thousand
            // to reach basis points, so 0.05 is five percent and 5 would be five hundred.
            Assert.That(modifier.Value, Is.EqualTo(0.05f).Within(0.00001f),
                "the shipped card no longer grants five percent maximum health");

            Assert.That(modifier.Value * 10000f, Is.EqualTo(500f).Within(0.01f),
                "the card does not resolve to five hundred basis points");
        }

        // ---- the drop ---------------------------------------------------------------------

        [Test]
        public void TheBossDropsTheCardAtExactlyOneInAMillion()
        {
            DropEntry entry = BossEntry(CardId);

            Assert.That(entry.Chance, Is.EqualTo(0.000001f),
                "the shipped card chance is not exactly 1e-6");

            // Stated again as a fraction, because a percentage converted at runtime is how
            // a rate quietly becomes ten times what it should be.
            Assert.That(entry.Chance * 1000000f, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TheDevilFruitChanceIsUntouchedAtOneInTenMillion()
        {
            Assert.That(BossEntry(Fruit).Chance, Is.EqualTo(0.0000001f),
                "the fruit chance moved while the card was being added");
        }

        [Test]
        public void TheCardAndTheFruitAreSeparateEntriesNeitherSuppressingTheOther()
        {
            DropTableDefinition table = BossTableDefinition();

            Assert.That(table.Entries.Count(e => e.Item.Value == CardId), Is.EqualTo(1));
            Assert.That(table.Entries.Count(e => e.Item.Value == Fruit), Is.EqualTo(1));

            // A table that stopped after its first success would make the two rare drops
            // compete. Zero means "no limit", which is what keeps them independent.
            Assert.That(table.MaxEntries, Is.Zero,
                "the boss table caps its entries, so one rare drop can suppress the other");
        }

        [Test]
        public void TheCardIsWorldBossOnlyByAuthoredRankAndNotByMonsterId()
        {
            DropEntry entry = BossEntry(CardId);

            Assert.That(entry.MinMonsterRank, Is.EqualTo(MonsterRank.WorldBoss),
                "the card is not gated to world bosses");

            // And the training slime is not one, so no runtime branch is needed to keep it
            // from dropping the card.
            Assert.That(_content.BuildMonsters().TryGet(new DefinitionId(TrainingSlime),
                out MonsterDefinition slime), Is.True);

            Assert.That(slime.Rank, Is.Not.EqualTo(MonsterRank.WorldBoss));
        }

        [Test]
        public void NoRuntimeCodeBranchesOnTheProductionCardId()
        {
            // The effect must come from the card's data. A branch on its id would mean the
            // next card needs another branch.
            string[] runtime = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(Application.dataPath, "_Game/Scripts"),
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in runtime)
            {
                Assert.That(System.IO.File.ReadAllText(file), Does.Not.Contain(CardId),
                    System.IO.Path.GetFileName(file) + " names the production card");
            }
        }

        // ---- the equipment it goes into -----------------------------------------------------

        [Test]
        public void TheShippedWorldHasAPieceOfEquipmentThatAcceptsACard()
        {
            Assert.That(_content.BuildItems().TryGet(new DefinitionId(BladeId),
                out ItemDefinition item), Is.True, "no socketable equipment is shipped");

            var piece = item as EquipmentDefinition;

            Assert.That(piece, Is.Not.Null, "the shipped blade is not equipment");
            Assert.That(CardSocketService.CardCapacity(piece), Is.EqualTo(1),
                "the shipped blade has no card socket");
        }

        [Test]
        public void TheCardFitsTheShippedBlade()
        {
            _content.BuildCards().TryGet(new DefinitionId(CardId), out CardDefinition card);
            _content.BuildItems().TryGet(new DefinitionId(BladeId), out ItemDefinition item);

            Assert.That(card.Fits((EquipmentDefinition)item), Is.True,
                "the shipped card cannot go into the only shipped socket");
        }

        [Test]
        public void TheCardDoesNotFitAPieceItWasNotAuthoredFor()
        {
            _content.BuildCards().TryGet(new DefinitionId(CardId), out CardDefinition card);

            var helm = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"item.probe_helm\"},\"_category\":2,"
                + "\"_slot\":1,\"_equipmentCategory\":2,\"_cardSlots\":1}", helm);

            Assert.That(card.Fits(helm), Is.False,
                "a main-hand card fitted a head slot");

            Object.DestroyImmediate(helm);
        }

        // ---- the modifier reaches a character ------------------------------------------------

        [Test]
        public void ASocketedCardAddsItsModifierThroughTheCanonicalResolver()
        {
            DefinitionRegistry<ItemDefinition> items = _content.BuildItems();
            DefinitionRegistry<CardDefinition> cards = _content.BuildCards();

            var owner = new OwnerId("owner-probe");

            var blade = new EquipmentInstance(InstanceId.New(), new DefinitionId(BladeId),
                owner);

            var context = new EquipmentModifierResolver.Context(items, cards: cards);

            var before = new List<StatModifier>();

            EquipmentModifierResolver.Collect(blade, context, before);

            var after = new List<StatModifier>();

            blade.AddCard(new EquipmentCardSocket(new DefinitionId(CardId), 0,
                InstanceId.New()));

            EquipmentModifierResolver.Collect(blade, context, after);

            Assert.That(after.Count, Is.EqualTo(before.Count + 1),
                "socketing the card added no modifier");

            StatModifier granted = after.Last(m => m.Stat.Value == "stat.max_hp");

            Assert.That(granted.Kind, Is.EqualTo(StatModifierKind.Percent));
            Assert.That(granted.Value, Is.EqualTo(0.05f).Within(0.00001f));
        }

        [Test]
        public void AWorldWithNoCardRegistryGrantsNothingRatherThanGuessing()
        {
            // The resolver cannot invent a card it has never been shown. Silence here is
            // what the bootstrap wiring exists to prevent, and the shipped world passes one.
            var blade = new EquipmentInstance(InstanceId.New(), new DefinitionId(BladeId),
                new OwnerId("owner-probe"));

            blade.AddCard(new EquipmentCardSocket(new DefinitionId(CardId), 0,
                InstanceId.New()));

            var into = new List<StatModifier>();

            EquipmentModifierResolver.Collect(blade,
                new EquipmentModifierResolver.Context(_content.BuildItems()), into);

            Assert.That(into.Any(m => m.Stat.Value == "stat.max_hp"), Is.False,
                "a card modifier appeared without the card ever being resolved");
        }

        private DropTableDefinition BossTableDefinition()
        {
            Assert.That(_content.BuildDropTables().TryGet(new DefinitionId(BossTable),
                out DropTableDefinition table), Is.True, "the boss table is missing");

            return table;
        }

        private DropEntry BossEntry(string item)
        {
            foreach (DropEntry entry in BossTableDefinition().Entries)
            {
                if (entry.Item.Value == item) return entry;
            }

            Assert.Fail("the boss table has no entry for " + item);

            return default;
        }
    }
}
