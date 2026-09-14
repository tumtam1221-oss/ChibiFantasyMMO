using ChibiFantasy.Network;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The dropped-loot presentation a shipped client draws.
    /// </summary>
    /// <remarks>
    /// The same failure as monsters had before 19F: a slime died, the server offered its
    /// gel, and nothing appeared on the ground because the only thing that drew a pile was
    /// the development visualizer, compiled out of a player's build. These pin that the
    /// production presenter ships, that the development cube stands aside for it, and that a
    /// pile still carries only the identity a pickup names -- never what is in it.
    /// </remarks>
    internal sealed class WorldLootPresentationTests
    {
        private const string Presenter =
            "Assets/_Game/Scripts/Client/World/WorldLootPresenter.cs";

        private const string Placeholder =
            "Assets/_Game/Scripts/Client/World/DevelopmentLootVisualizer.cs";

        private const string Bootstrap =
            "Assets/_Game/Scripts/Client/ClientApplicationBootstrap.cs";

        [Test]
        public void TheProductionLootPresenterIsNotCompiledOutOfAPlayersBuild()
        {
            string source = System.IO.File.ReadAllText(Presenter);

            int declared = source.IndexOf("public sealed class WorldLootPresenter",
                System.StringComparison.Ordinal);
            int guarded = source.IndexOf("#if ", System.StringComparison.Ordinal);

            Assert.That(declared, Is.GreaterThanOrEqualTo(0), "the loot presenter was renamed");
            Assert.That(guarded < 0 || guarded > declared, Is.True,
                "the production loot presenter is behind a compilation guard, so a player's "
                + "build would draw no loot on the ground at all");

            string placeholder = System.IO.File.ReadAllText(Placeholder);

            Assert.That(placeholder, Does.Contain("#if DEVELOPMENT_BUILD"),
                "the placeholder cube would ship to players");
        }

        [Test]
        public void TheClientComposesTheProductionPresenterOutsideTheDevelopmentGuard()
        {
            string boot = System.IO.File.ReadAllText(Bootstrap);

            int compose = boot.IndexOf("host.AddComponent<WorldLootPresenter>()",
                System.StringComparison.Ordinal);
            int devGuard = boot.IndexOf("#if DEVELOPMENT_BUILD",
                System.StringComparison.Ordinal);

            Assert.That(compose, Is.GreaterThanOrEqualTo(0),
                "nothing composes the production loot presenter, so no build draws loot");
            Assert.That(devGuard < 0 || compose < devGuard, Is.True,
                "the loot presenter is composed inside the development guard, so a shipped "
                + "client would not draw loot");

            // And the development cube is told to stand aside for it.
            Assert.That(boot, Does.Contain("piles.Presenter = LootPresenter"),
                "the development loot cube is not told to defer to the production pile");
        }

        [Test]
        public void APileDrawsAtWhatTheServerOffersAndCarriesOnlyItsIdentity()
        {
            var host = new GameObject("loot-presenter-test");

            try
            {
                var presenter = host.AddComponent<ChibiFantasy.Client.World.WorldLootPresenter>();
                var loot = host.AddComponent<ChibiFantasy.Client.World.WorldLootInput>();

                // No catalogue and no language: the label falls back to a readable id, which
                // is all this test needs and proves the presenter never requires them.
                presenter.Compose(loot, null, null);

                // A pickup request names a pile and a slot; the label is a display string.
                // Neither the drawn object nor its marker exposes what is in the pile.
                LootEntrySnapshot entry = new LootEntrySnapshot
                {
                    LootId = "loot-1",
                    Index = 2,
                    ItemId = "item.slime_gel",
                    Quantity = 3,
                    X = 4f,
                    Y = 0f,
                    Z = -5f,
                };

                Assert.That(presenter.LabelFor(entry), Does.Contain("3"),
                    "the label does not show the stack size");
                Assert.That(presenter.NameOf(new ChibiFantasy.Core.DefinitionId("item.slime_gel")),
                    Is.EqualTo("Slime Gel"),
                    "an item with no catalogue entry should read as its id made readable");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
