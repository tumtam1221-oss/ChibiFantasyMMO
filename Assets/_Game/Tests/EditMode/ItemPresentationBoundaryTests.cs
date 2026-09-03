using System;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The two presentation boundaries PHASE 08.3 introduced, and the rules that guard them.
    /// </summary>
    /// <remarks>
    /// An <see cref="AssetRef"/> becomes a sprite in exactly one place, and a
    /// <see cref="LocalizationKey"/> becomes text in exactly one place -- both inside the UI
    /// assembly. The point of the boundaries is what stays out of Gameplay: no
    /// <c>Sprite</c> on a definition, no translated string below the UI. Those are asserted
    /// structurally, because a violation would still look correct on screen.
    /// </remarks>
    internal sealed class ItemPresentationBoundaryTests : ItemContainerTestBase
    {
        private const string Named = "item.named";
        private const string Iconless = "item.iconless";

        private Sprite _sprite;

        [SetUp]
        public void CreateSprite()
        {
            var texture = new Texture2D(4, 4);
            _sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }

        [TearDown]
        public void DestroySprite()
        {
            if (_sprite == null) return;

            Texture2D texture = _sprite.texture;
            UnityEngine.Object.DestroyImmediate(_sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }

        // ---- icons ---------------------------------------------------------------------

        [Test]
        public void A_valid_address_resolves_to_its_sprite()
        {
            var resolver = new IconResolver(address => address == "icons/potion" ? _sprite : null);

            Sprite resolved;
            Assert.That(resolver.TryResolve(new AssetRef("icons/potion"), out resolved), Is.True);
            Assert.That(resolved, Is.SameAs(_sprite));
            Assert.That(resolver.Resolve(new AssetRef("icons/potion")), Is.SameAs(_sprite));
        }

        [Test]
        public void An_unauthored_address_gives_the_placeholder_without_asking_the_loader()
        {
            int attempts = 0;
            var resolver = new IconResolver(address =>
            {
                attempts++;
                return _sprite;
            });

            resolver.Placeholder = null;

            Sprite resolved;
            Assert.That(resolver.TryResolve(AssetRef.None, out resolved), Is.False);
            Assert.That(resolved, Is.Null);
            Assert.That(attempts, Is.EqualTo(0),
                "there is no address to load, so nothing should have been attempted");
        }

        [Test]
        public void An_address_that_resolves_to_nothing_gives_the_placeholder()
        {
            var resolver = new IconResolver(address => null);
            resolver.Placeholder = _sprite;

            Sprite resolved;
            Assert.That(resolver.TryResolve(new AssetRef("icons/missing"), out resolved), Is.False);
            Assert.That(resolved, Is.Null, "TryResolve reports the truth");
            Assert.That(resolver.Resolve(new AssetRef("icons/missing")), Is.SameAs(_sprite),
                "and Resolve substitutes the placeholder");
        }

        [Test]
        public void Repeated_refreshes_load_each_address_at_most_once()
        {
            var resolver = new IconResolver(address => _sprite);

            for (int i = 0; i < 50; i++)
            {
                resolver.Resolve(new AssetRef("icons/a"));
                resolver.Resolve(new AssetRef("icons/b"));
            }

            Assert.That(resolver.LoadAttempts, Is.EqualTo(2),
                "fifty refreshes of two icons is two loads");
            Assert.That(resolver.CachedCount, Is.EqualTo(2));
        }

        [Test]
        public void A_failed_load_is_remembered_so_it_is_not_retried_every_refresh()
        {
            var resolver = new IconResolver(address => null);

            for (int i = 0; i < 20; i++) resolver.Resolve(new AssetRef("icons/missing"));

            Assert.That(resolver.LoadAttempts, Is.EqualTo(1),
                "a bag of unauthored icons must not re-attempt on every redraw");
        }

        [Test]
        public void Clearing_forgets_the_cache_without_unloading_anything()
        {
            var resolver = new IconResolver(address => _sprite);
            resolver.Resolve(new AssetRef("icons/a"));

            resolver.Clear();
            Assert.That(resolver.CachedCount, Is.EqualTo(0));

            resolver.Resolve(new AssetRef("icons/a"));
            Assert.That(resolver.LoadAttempts, Is.EqualTo(2));
            Assert.That(_sprite, Is.Not.Null,
                "the sprite is Unity's and may be shared; this layer must not destroy it");
        }

        [Test]
        public void A_panel_refreshes_with_and_without_a_resolver()
        {
            var go = new GameObject("Panel");
            try
            {
                var panel = go.AddComponent<ItemContainerPanel>();
                panel.Build(4);

                var data = new System.Collections.Generic.List<ItemSlotViewData>();
                ItemDefinition definition;
                Items.TryGet(new DefinitionId(Potion), out definition);
                for (int i = 0; i < 4; i++) data.Add(ItemSlotViewData.Empty(i));
                data[0] = ItemSlotViewData.From(0, new DefinitionId(Potion),
                    InstanceId.New(), 3, definition);

                var resolver = new IconResolver(address => _sprite);

                Assert.DoesNotThrow(() => panel.Refresh(data, ItemSelection.None, null));
                Assert.DoesNotThrow(() => panel.Refresh(data, ItemSelection.None, resolver));
                Assert.That(panel.SlotCount, Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void No_gameplay_or_data_type_holds_a_unity_sprite()
        {
            // The reason AssetRef is a string. A Sprite field on a definition would make
            // content unloadable outside a renderer.
            Assembly[] assemblies =
            {
                typeof(ItemDefinition).Assembly,
                typeof(ItemContainerState).Assembly
            };

            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static
                        | BindingFlags.Public | BindingFlags.NonPublic);

                    foreach (FieldInfo field in fields)
                    {
                        Assert.That(field.FieldType, Is.Not.EqualTo(typeof(Sprite)),
                            assembly.GetName().Name + "." + type.Name + "." + field.Name
                            + " holds a Sprite; use an AssetRef");
                        Assert.That(field.FieldType, Is.Not.EqualTo(typeof(Texture2D)),
                            assembly.GetName().Name + "." + type.Name + "." + field.Name
                            + " holds a Texture2D; use an AssetRef");
                    }
                }
            }
        }

        // ---- localization --------------------------------------------------------------

        [Test]
        public void A_known_key_resolves_to_its_text()
        {
            var table = new LocalizationTable();
            table.Set(new LocalizationKey("item.potion.name"), "Red Potion");

            Assert.That(LocalizedText.Resolve(table, new LocalizationKey("item.potion.name")),
                Is.EqualTo("Red Potion"));
        }

        [Test]
        public void An_unknown_key_falls_back_to_the_raw_key()
        {
            var table = new LocalizationTable();

            Assert.That(LocalizedText.Resolve(table, new LocalizationKey("item.missing.name")),
                Is.EqualTo("item.missing.name"),
                "the untranslated string must stay visible to whoever has to add it");
        }

        [Test]
        public void No_source_at_all_still_renders()
        {
            Assert.That(LocalizedText.Resolve(null, new LocalizationKey("item.x.name")),
                Is.EqualTo("item.x.name"),
                "this is what every screen sees until a localisation system exists");
        }

        [Test]
        public void An_invalid_key_and_an_empty_translation_are_both_safe()
        {
            var table = new LocalizationTable();
            table.Set(new LocalizationKey("item.blank"), string.Empty);

            Assert.That(LocalizedText.Resolve(table, LocalizationKey.None), Is.Empty);
            Assert.That(LocalizedText.Resolve(table, new LocalizationKey("item.blank")),
                Is.EqualTo("item.blank"), "a blank translation is treated as missing");

            Assert.That(LocalizedText.ResolveOr(table, LocalizationKey.None, "item.fallback"),
                Is.EqualTo("item.fallback"));
        }

        [Test]
        public void Setting_an_invalid_key_is_ignored_rather_than_stored()
        {
            var table = new LocalizationTable();
            table.Set(LocalizationKey.None, "nothing");

            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void No_gameplay_type_can_resolve_a_localized_string()
        {
            // Gameplay stays language-neutral: the source interface lives in the UI
            // assembly, which Gameplay does not reference.
            string[] referenced = typeof(ItemContainerState).Assembly
                .GetReferencedAssemblies().Select(a => a.Name).ToArray();

            Assert.That(referenced, Does.Not.Contain("ChibiFantasy.UI"));
            Assert.That(typeof(ILocalizedTextSource).Assembly.GetName().Name,
                Is.EqualTo("ChibiFantasy.UI"));
        }

        // ---- tooltip -------------------------------------------------------------------

        [Test]
        public void A_tooltip_title_uses_the_translation_when_there_is_one()
        {
            AddItem(Named, stackable: true, maxStack: 99);
            SetPrivate(FindDefinition(Named), "_nameKey", new LocalizationKey("item.named.name"));

            var table = new LocalizationTable();
            table.Set(new LocalizationKey("item.named.name"), "Fancy Thing");

            ItemTooltipData tooltip = ItemTooltipData.From(new DefinitionId(Named), 4,
                FindDefinition(Named));

            Assert.That(ItemTooltipView.FormatTitle(tooltip, table), Is.EqualTo("Fancy Thing x4"));
            Assert.That(ItemTooltipView.FormatTitle(tooltip, null), Is.EqualTo("item.named.name x4"),
                "and falls back to the key without a table");
        }

        [Test]
        public void A_tooltip_says_an_item_is_usable_only_when_content_configured_it()
        {
            const string Usable = "item.usable";
            const string Configured = "item.configured.off";

            AddUsable(Usable, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 100)
            });

            AddUsable(Configured, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 100)
            }, usable: false);

            ItemTooltipData usable = ItemTooltipData.From(new DefinitionId(Usable), 1,
                FindDefinition(Usable));
            ItemTooltipData off = ItemTooltipData.From(new DefinitionId(Configured), 1,
                FindDefinition(Configured));

            Assert.That(usable.IsUsable, Is.True);
            Assert.That(usable.UseType, Is.EqualTo(ItemUseType.Recovery));
            Assert.That(ItemTooltipView.FormatBody(usable), Does.Contain("Use:"));

            Assert.That(off.IsUsable, Is.False);
            Assert.That(ItemTooltipView.FormatBody(off), Does.Not.Contain("Use:"),
                "promising a use that is refused would be worse than showing nothing");
        }

        [Test]
        public void A_warp_tooltip_shows_the_destinations_own_name()
        {
            const string Scroll = "item.scroll.city";
            AddMap("map.city.a", MapCategory.Town, isTown: true, nameKey: "map.city.a.name");
            AddUsable(Scroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId("map.city.a"))
            });

            ItemContainerState bag = Container(4);
            bag.Add(Stack(Scroll, 1), Items);

            ItemTooltipData tooltip = InventoryViewAdapter.BuildTooltip(bag, 0, Items, Maps);

            Assert.That(tooltip.HasWarpDestination, Is.True);
            Assert.That(tooltip.WarpDestination, Is.EqualTo(new DefinitionId("map.city.a")));
            Assert.That(tooltip.WarpDestinationName,
                Is.EqualTo(new LocalizationKey("map.city.a.name")),
                "the name comes off the MapDefinition, never out of the scroll or the UI");

            var table = new LocalizationTable();
            table.Set(new LocalizationKey("map.city.a.name"), "Aldergate");

            Assert.That(ItemTooltipView.FormatBody(tooltip, table), Does.Contain("Aldergate"));
            Assert.That(ItemTooltipView.FormatBody(tooltip, null), Does.Contain("map.city.a.name"));
        }

        [Test]
        public void A_warp_tooltip_without_a_map_registry_shows_the_destination_id()
        {
            const string Scroll = "item.scroll.noregistry";
            AddUsable(Scroll, ItemUseType.WarpTown, new[]
            {
                new ItemUseEffect(ItemEffectKind.WarpToMap,
                    destinationMap: new DefinitionId("map.somewhere"))
            });

            ItemContainerState bag = Container(4);
            bag.Add(Stack(Scroll, 1), Items);

            ItemTooltipData tooltip = InventoryViewAdapter.BuildTooltip(bag, 0, Items, null);

            Assert.That(tooltip.HasWarpDestination, Is.True);
            Assert.That(tooltip.WarpDestinationName.IsValid, Is.False);
            Assert.That(ItemTooltipView.FormatBody(tooltip), Does.Contain("map.somewhere"));
        }

        [Test]
        public void A_non_warp_item_reports_no_destination()
        {
            const string Plain = "item.plain.usable";
            AddUsable(Plain, ItemUseType.Recovery, new[]
            {
                new ItemUseEffect(ItemEffectKind.RestoreResource, ItemResource.Health, 100)
            });

            ItemTooltipData tooltip = ItemTooltipData.From(new DefinitionId(Plain), 1,
                FindDefinition(Plain));

            Assert.That(tooltip.HasWarpDestination, Is.False);
            Assert.That(ItemTooltipView.FormatBody(tooltip), Does.Not.Contain("Warp to:"));
        }

        [Test]
        public void An_item_with_no_authored_icon_is_still_drawable()
        {
            AddItem(Iconless, stackable: false, maxStack: 1);

            ItemSlotViewData slot = ItemSlotViewData.From(0, new DefinitionId(Iconless),
                InstanceId.New(), 1, FindDefinition(Iconless));

            Assert.That(slot.IsOccupied, Is.True);
            Assert.That(slot.HasIcon, Is.False);

            var resolver = new IconResolver(address => _sprite);
            Assert.That(resolver.Resolve(slot.Icon), Is.Null, "no address, no sprite, no error");
            Assert.That(resolver.LoadAttempts, Is.EqualTo(0));
        }

        private ItemDefinition FindDefinition(string id)
        {
            ItemDefinition definition;
            Items.TryGet(new DefinitionId(id), out definition);
            return definition;
        }
    }
}
