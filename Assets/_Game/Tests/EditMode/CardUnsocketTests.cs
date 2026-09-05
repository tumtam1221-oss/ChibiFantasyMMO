using System.Collections.Generic;
using System.Linq;
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
    /// Taking a card back out, and the state that has to follow it.
    /// </summary>
    /// <remarks>
    /// <b>Phase 12 already decided what removal means.</b> The card comes back as an item
    /// carrying the identity it went in with, after somewhere to put it has been found.
    /// Nothing here invents an extraction cost, a destruction chance or a new item -- these
    /// tests pin what the service already does, so a later change to it has to be deliberate.
    ///
    /// <b>The collection is the truth.</b> What a piece is carrying after a removal is
    /// whatever <see cref="EquipmentInstance.Cards"/> now says, and persistence has to say
    /// exactly the same thing -- including when the answer is "nothing".
    /// </remarks>
    [TestFixture]
    internal sealed class CardUnsocketTests : MonsterTestBase
    {
        private const string CardA = "card.probe_a";
        private const string CardB = "card.probe_b";
        private const string Blade = "item.probe_blade";

        private DefinitionRegistry<CardDefinition> _cards;
        private readonly List<Object> _created = new List<Object>();

        private OwnerId _owner;

        [SetUp]
        public void SetUpCards()
        {
            _owner = Owner;

            AddItem(CardA, maxStack: 1);
            AddItem(CardB, maxStack: 1);

            AuthorBlade(Blade, cardSlots: 2);

            _cards = new DefinitionRegistry<CardDefinition>();
            _cards.Register(Card(CardA, "stat.max_hp", 0.05f));
            _cards.Register(Card(CardB, "stat.max_hp", 0.10f));
        }

        [TearDown]
        public void TearDownCards()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- what removal already means ----------------------------------------------

        [Test]
        public void RemovingACardPutsTheSameCardBackInTheBag()
        {
            EquipmentInstance piece = Socketed(CardA, out InstanceId cardInstance);
            ItemContainerState bag = Bag(4);

            CardSocketResult removed = CardSocketService.TryRemove(piece, 0, bag, Context());

            Assert.That(removed.IsAccepted, Is.True, removed.Reason.ToString());

            Assert.That(piece.CardCount, Is.Zero, "the socket kept the card");

            // The identity that went in, not a new one wearing its name.
            Assert.That(bag.IndexOf(cardInstance), Is.GreaterThanOrEqualTo(0),
                "the card that came back is not the card that went in");

            Assert.That(Carried(bag, CardA), Is.EqualTo(1),
                "removal produced more or less than one card");
        }

        [Test]
        public void RemovalIsRefusedWhenThereIsNowhereToPutTheCard()
        {
            // Checked before anything is taken apart, so a full bag cannot destroy a card.
            EquipmentInstance piece = Socketed(CardA, out InstanceId _);
            ItemContainerState full = Bag(1);

            full.Add(new ItemInstance(InstanceId.New(), new DefinitionId(Relic), _owner, 1),
                Items);

            CardSocketResult refused = CardSocketService.TryRemove(piece, 0, full, Context());

            Assert.That(refused.IsAccepted, Is.False);
            Assert.That(refused.Reason,
                Is.EqualTo(CardSocketRejection.NoRoomForCard));

            Assert.That(piece.CardCount, Is.EqualTo(1),
                "a refused removal emptied the socket anyway");
        }

        [Test]
        public void RemovingAnEmptySocketIsRefusedAndCreatesNothing()
        {
            EquipmentInstance piece = new EquipmentInstance(InstanceId.New(),
                new DefinitionId(Blade), _owner);

            ItemContainerState bag = Bag(4);

            Assert.That(CardSocketService.TryRemove(piece, 0, bag, Context()).IsAccepted,
                Is.False);

            Assert.That(Carried(bag, CardA), Is.Zero, "an empty socket produced a card");
        }

        [Test]
        public void RemovingTheSameSocketTwiceYieldsOneCard()
        {
            EquipmentInstance piece = Socketed(CardA, out InstanceId _);
            ItemContainerState bag = Bag(4);

            Assert.That(CardSocketService.TryRemove(piece, 0, bag, Context()).IsAccepted,
                Is.True);

            // The replayed request: the socket is already empty, so there is nothing to
            // take and nothing to create.
            Assert.That(CardSocketService.TryRemove(piece, 0, bag, Context()).IsAccepted,
                Is.False);

            Assert.That(Carried(bag, CardA), Is.EqualTo(1),
                "a replayed removal produced a second card");
        }

        [Test]
        public void RemovingOneOfTwoLeavesTheOtherInItsOwnSocket()
        {
            EquipmentInstance piece = new EquipmentInstance(InstanceId.New(),
                new DefinitionId(Blade), _owner);

            piece.AddCard(new EquipmentCardSocket(new DefinitionId(CardA), 0,
                new InstanceId("card-a")));
            piece.AddCard(new EquipmentCardSocket(new DefinitionId(CardB), 1,
                new InstanceId("card-b")));

            ItemContainerState bag = Bag(4);

            Assert.That(CardSocketService.TryRemove(piece, 0, bag, Context()).IsAccepted,
                Is.True);

            Assert.That(piece.CardCount, Is.EqualTo(1));
            Assert.That(piece.Cards[0].SocketIndex, Is.EqualTo(1),
                "removing socket zero moved the card in socket one");
            Assert.That(piece.Cards[0].Card.Value, Is.EqualTo(CardB));

            Assert.That(Carried(bag, CardA), Is.EqualTo(1));
            Assert.That(Carried(bag, CardB), Is.Zero, "the wrong card came out");
        }

        [Test]
        public void RemovalTouchesOnlyThePieceItWasAskedAbout()
        {
            EquipmentInstance first = Socketed(CardA, out InstanceId _);
            EquipmentInstance second = Socketed(CardA, out InstanceId _);

            ItemContainerState bag = Bag(4);

            CardSocketService.TryRemove(first, 0, bag, Context());

            Assert.That(first.CardCount, Is.Zero);
            Assert.That(second.CardCount, Is.EqualTo(1),
                "removing a card from one piece emptied another of the same kind");
        }

        // ---- the modifier goes with it -------------------------------------------------

        [Test]
        public void TheCardsModifierDisappearsWhenTheCardDoes()
        {
            EquipmentInstance piece = Socketed(CardA, out InstanceId _);

            var context = new EquipmentModifierResolver.Context(Items, cards: _cards);

            var before = new List<StatModifier>();

            EquipmentModifierResolver.Collect(piece, context, before);

            Assert.That(before.Any(m => m.Stat.Value == "stat.max_hp"), Is.True,
                "the fixture never granted anything to remove");

            CardSocketService.TryRemove(piece, 0, Bag(4), Context());

            var after = new List<StatModifier>();

            EquipmentModifierResolver.Collect(piece, context, after);

            Assert.That(after.Any(m => m.Stat.Value == "stat.max_hp"), Is.False,
                "the card's modifier outlived the card");
        }

        // ---- and so does what is written down --------------------------------------------

        [Test]
        public void AnEmptiedPieceIsPersistedAsCarryingNothing()
        {
            // The write boundary is the thing that matters: what the mapper produces is
            // what the repository replaces the stored rows with.
            EquipmentInstance piece = Socketed(CardA, out InstanceId _);

            Assert.That(Persisted(piece).Cards.Count, Is.EqualTo(1),
                "the fixture was not socketed to begin with");

            CardSocketService.TryRemove(piece, 0, Bag(4), Context());

            PersistedItem after = Persisted(piece);

            Assert.That(after.Cards.Count, Is.Zero,
                "an emptied piece is still written down as carrying a card");

            Assert.That(after.Instance, Is.EqualTo(piece.InstanceId),
                "the row is not addressed by the piece's own identity");
        }

        [Test]
        public void APieceThatStillHasACardIsPersistedWithExactlyThatOne()
        {
            EquipmentInstance piece = new EquipmentInstance(InstanceId.New(),
                new DefinitionId(Blade), _owner);

            piece.AddCard(new EquipmentCardSocket(new DefinitionId(CardA), 0,
                new InstanceId("card-a")));
            piece.AddCard(new EquipmentCardSocket(new DefinitionId(CardB), 1,
                new InstanceId("card-b")));

            CardSocketService.TryRemove(piece, 0, Bag(4), Context());

            PersistedItem after = Persisted(piece);

            Assert.That(after.Cards.Count, Is.EqualTo(1));
            Assert.That(after.Cards[0].SocketIndex, Is.EqualTo(1));
            Assert.That(after.Cards[0].CardInstance.Value, Is.EqualTo("card-b"));
        }

        // ---- one system ---------------------------------------------------------------------

        [Test]
        public void SocketStateStillLivesOnlyOnTheEquipmentInstance()
        {
            string[] runtime = System.IO.Directory.GetFiles(
                System.IO.Path.Combine(Application.dataPath, "_Game/Scripts"),
                "*.cs", System.IO.SearchOption.AllDirectories);

            // Declaring the collection is what makes something an owner. Reading it
            // through IReadOnlyList -- which the service and the UI both do -- is not a
            // second source of truth, and a guard that counted those would be counting
            // callers rather than owners.
            var owners = new List<string>();

            foreach (string file in runtime)
            {
                string code = System.IO.File.ReadAllText(file);

                if (code.Contains("private List<EquipmentCardSocket>")
                    || code.Contains("private readonly List<EquipmentCardSocket>"))
                {
                    owners.Add(System.IO.Path.GetFileName(file));
                }
            }

            Assert.That(owners, Is.EqualTo(new[] { "EquipmentInstance.cs" }),
                "socket state is held somewhere other than the equipment aggregate: "
                + string.Join(", ", owners));
        }

        [Test]
        public void NoRuntimeCodeBranchesOnAParticularCardWhenRemovingOne()
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath, "_Game/Scripts/Gameplay/CardSocketService.cs"));

            foreach (string forbidden in new[] { "ancient_slime_king", "card.probe" })
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    "the socket service names a particular card");
            }
        }

        // ---- fixture -------------------------------------------------------------------------

        private CardSocketService.Context Context()
        {
            return new CardSocketService.Context(Items, _cards, null, _owner);
        }

        private EquipmentInstance Socketed(string card, out InstanceId cardInstance)
        {
            var piece = new EquipmentInstance(InstanceId.New(), new DefinitionId(Blade),
                _owner);

            cardInstance = InstanceId.New();

            piece.AddCard(new EquipmentCardSocket(new DefinitionId(card), 0, cardInstance));

            return piece;
        }

        private ItemContainerState Bag(int capacity)
        {
            return new ItemContainerState(_owner, capacity);
        }

        private static PersistedItem Persisted(EquipmentInstance piece)
        {
            var cards = new List<PersistedCard>();

            for (var i = 0; i < piece.Cards.Count; i++)
            {
                EquipmentCardSocket socket = piece.Cards[i];

                cards.Add(new PersistedCard(socket.Card, socket.SocketIndex,
                    socket.CardInstance));
            }

            return new PersistedItem(piece.InstanceId, piece.DefinitionId, 1, 0, 0, 0,
                piece.EnhancementLevel, piece.Rarity, null, cards);
        }

        private int Carried(ItemContainerState bag, string item)
        {
            var total = 0;

            for (var i = 0; i < bag.Capacity; i++)
            {
                var instance = bag.GetSlot(i).Content as ItemInstance;

                if (instance != null && instance.DefinitionId.Value == item)
                {
                    total += instance.Quantity;
                }
            }

            return total;
        }

        private CardDefinition Card(string id, string stat, float percent)
        {
            var card = ScriptableObject.CreateInstance<CardDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},"
                + "\"_statModifiers\":[{\"_stat\":{\"_value\":\"" + stat + "\"},"
                + "\"_kind\":1,\"_value\":" + percent.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) + "}],"
                + "\"_allowedSlot\":6,\"_maxPerEquipment\":1,\"_disabled\":false}", card);

            _created.Add(card);

            return card;
        }

        private void AuthorBlade(string id, int cardSlots)
        {
            var piece = ScriptableObject.CreateInstance<EquipmentDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_category\":2,"
                + "\"_slot\":6,\"_equipmentCategory\":1,\"_maxStackSize\":1,"
                + "\"_cardSlots\":" + cardSlots + "}", piece);

            _created.Add(piece);

            Items.Register(piece);
        }
    }
}
