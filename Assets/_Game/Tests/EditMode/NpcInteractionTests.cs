using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// NPCs: registry, placement, roles and interaction.
    /// </summary>
    /// <remarks>
    /// One NPC model covers every role, and the roles are derived from the capability flags
    /// Phase 04 already authored rather than stored a second time. Several tests below exist
    /// to hold exactly that: there is no shopkeeper type, no job-changer type, and no second
    /// place where "this NPC sells things" is recorded.
    ///
    /// The other property worth protecting is that interaction authorises and opens nothing:
    /// storage returns permission to open the container the character already has, so no NPC
    /// ever owns an inventory.
    /// </remarks>
    internal sealed class NpcInteractionTests : WorldTestBase
    {
        private const string Elder = "npc.elder";
        private const string Vendor = "npc.vendor";
        private const string Banker = "npc.banker";
        private const string Master = "npc.master";
        private const string Statue = "npc.statue";

        private const string ElderSpawn = "spawn.npc.elder";
        private const string GeneralStore = "shop.general";
        private const string FetchQuest = "quest.fetch";
        private const string Warrior = "class.warrior";

        [SetUp]
        public void AuthorNpcs()
        {
            AddSpawn(ElderSpawn, TownA, SpawnType.Npc, 12f, 0f, 10f);

            AddQuestDefinition(FetchQuest);
            AddClassDefinition(Warrior);
            AddShop(GeneralStore, new[]
            {
                new ShopEntry(new DefinitionId(Potion), 50),
                new ShopEntry(new DefinitionId(Key), 500, stock: 3)
            });

            AddNpc(Elder, TownA, ElderSpawn, questGiver: true,
                quests: new[] { new DefinitionId(FetchQuest) });

            AddNpc(Vendor, TownA, ElderSpawn, category: NPCCategory.Merchant,
                shop: GeneralStore);

            AddNpc(Banker, TownA, ElderSpawn, storage: true);

            AddNpc(Master, TownA, ElderSpawn, jobChanger: true,
                classes: new[] { new DefinitionId(Warrior) });

            AddNpc(Statue, TownA, ElderSpawn);
        }

        private NpcInteractionService.Context NpcContext()
        {
            return new NpcInteractionService.Context(Npcs, SpawnPoints, Shops, Quests);
        }

        /// <summary>A character standing right next to the town NPCs.</summary>
        private CharacterLocationState NextToNpcs()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(12f, 0f, 10f);
            return location;
        }

        // ---- registry ------------------------------------------------------------------

        [Test]
        public void The_registry_resolves_known_npcs_and_refuses_unknown_ones()
        {
            NPCDefinition npc;

            Assert.That(Npcs.TryGet(new DefinitionId(Elder), out npc), Is.True);
            Assert.That(npc.NameKey.IsValid, Is.True);
            Assert.That(Npcs.TryGet(new DefinitionId("npc.nobody"), out npc), Is.False);
        }

        [Test]
        public void The_registry_refuses_a_duplicate_id()
        {
            var duplicate = Track(ScriptableObject.CreateInstance<NPCDefinition>());
            JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"" + Elder + "\"}}", duplicate);

            Assert.That(Npcs.TryRegister(duplicate), Is.False);
            Assert.That(Npcs.Count, Is.EqualTo(5));
        }

        // ---- roles ---------------------------------------------------------------------

        [Test]
        public void One_definition_type_covers_every_role()
        {
            NPCDefinition elder;
            NPCDefinition vendor;
            NPCDefinition banker;

            Npcs.TryGet(new DefinitionId(Elder), out elder);
            Npcs.TryGet(new DefinitionId(Vendor), out vendor);
            Npcs.TryGet(new DefinitionId(Banker), out banker);

            Assert.That(elder.GetType(), Is.EqualTo(vendor.GetType()));
            Assert.That(vendor.GetType(), Is.EqualTo(banker.GetType()),
                "no NPC subclasses: what an NPC does comes from its capabilities");
        }

        [Test]
        public void Roles_are_derived_from_the_authored_capabilities()
        {
            NPCDefinition elder;
            NPCDefinition vendor;
            NPCDefinition statue;

            Npcs.TryGet(new DefinitionId(Elder), out elder);
            Npcs.TryGet(new DefinitionId(Vendor), out vendor);
            Npcs.TryGet(new DefinitionId(Statue), out statue);

            Assert.That(elder.HasRole(NpcRole.Quest), Is.True);
            Assert.That(elder.HasRole(NpcRole.Shop), Is.False);

            Assert.That(vendor.HasRole(NpcRole.Shop), Is.True,
                "an NPC is a vendor because it references a stock list");
            Assert.That(vendor.HasRole(NpcRole.Quest), Is.False);

            Assert.That(statue.HasRole(NpcRole.Generic), Is.True, "everyone can be talked to");
            Assert.That(statue.HasRole(NpcRole.Storage), Is.False);
        }

        [Test]
        public void One_npc_can_hold_several_roles_at_once()
        {
            AddNpc("npc.both", TownA, ElderSpawn, questGiver: true, shop: GeneralStore,
                quests: new[] { new DefinitionId(FetchQuest) });

            NPCDefinition both;
            Npcs.TryGet(new DefinitionId("npc.both"), out both);

            Assert.That(both.HasRole(NpcRole.Quest), Is.True);
            Assert.That(both.HasRole(NpcRole.Shop), Is.True,
                "a shopkeeper who also gives quests is one definition, not a new type");
        }

        [Test]
        public void The_role_model_is_not_stored_twice()
        {
            // Structural: a parallel Roles field would be a second source of the same truth,
            // and the two would drift the first time one was edited without the other.
            FieldInfo[] fields = typeof(NPCDefinition).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(NpcRole[])),
                    "roles are derived from the capability flags, never stored alongside them");
            }
        }

        // ---- interaction ---------------------------------------------------------------

        [Test]
        public void Talking_to_a_quest_giver_in_range_is_authorised()
        {
            NpcInteractionResult result = NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Elder), NpcRole.Quest, NpcContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Role, Is.EqualTo(NpcRole.Quest));
            Assert.That(result.Npc, Is.EqualTo(new DefinitionId(Elder)));
        }

        [Test]
        public void Opening_a_vendor_resolves_the_shop_it_references()
        {
            NpcInteractionResult result = NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Vendor), NpcRole.Shop, NpcContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Content, Is.EqualTo(new DefinitionId(GeneralStore)));

            ShopDefinition shop;
            Shops.TryGet(result.Content, out shop);
            Assert.That(shop.Entries.Length, Is.EqualTo(2));
        }

        [Test]
        public void A_storage_keeper_authorises_the_characters_own_container()
        {
            NpcInteractionResult result = NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Banker), NpcRole.Storage, NpcContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(result.Content.IsValid, Is.False,
                "storage opens the container the character already has; the NPC owns none");
        }

        [Test]
        public void No_npc_type_owns_an_inventory_a_quest_or_a_currency()
        {
            // The seam rule: an NPC authorises, it never holds player state.
            FieldInfo[] fields = typeof(NPCDefinition).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                string held = field.FieldType.FullName ?? string.Empty;

                Assert.That(held, Does.Not.Contain("ItemContainerState"), field.Name);
                Assert.That(held, Does.Not.Contain("CharacterQuestState"), field.Name);
                Assert.That(held, Does.Not.Contain("ItemInstance"), field.Name);
                Assert.That(held, Does.Not.Contain("ChibiFantasy.Gameplay"), field.Name);
            }
        }

        [Test]
        public void A_job_changer_authorises_when_it_offers_something()
        {
            NpcInteractionResult result = NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Master), NpcRole.JobChange, NpcContext());

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        [Test]
        public void Asking_for_a_role_an_npc_does_not_offer_is_refused()
        {
            Assert.That(NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Statue), NpcRole.Shop, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.RoleNotOffered));

            Assert.That(NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId(Elder), NpcRole.Storage, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.RoleNotOffered));
        }

        [Test]
        public void A_role_whose_content_was_deleted_is_refused_rather_than_opened_empty()
        {
            AddNpc("npc.ghostvendor", TownA, ElderSpawn, category: NPCCategory.Merchant,
                shop: "shop.deleted");

            NpcInteractionResult result = NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId("npc.ghostvendor"), NpcRole.Shop, NpcContext());

            Assert.That(result.Reason, Is.EqualTo(NpcInteractionRejection.RoleUnavailable),
                "an empty shop is something a player would report as a bug");
        }

        [Test]
        public void Standing_too_far_away_is_refused()
        {
            CharacterLocationState location = StartAt(TownASpawn);
            location.Position = new CombatPosition(900f, 0f, 900f);

            Assert.That(NpcInteractionService.TryInteract(location,
                new DefinitionId(Elder), NpcRole.Quest, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.TooFar));
        }

        [Test]
        public void An_npc_on_another_map_cannot_be_reached()
        {
            CharacterLocationState location = StartAt(FieldASpawn);

            Assert.That(NpcInteractionService.TryInteract(location,
                new DefinitionId(Elder), NpcRole.Quest, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.WrongMap),
                "a client asserting it stands elsewhere proves nothing");
        }

        [Test]
        public void A_disabled_npc_refuses_every_role()
        {
            AddNpc("npc.closed", TownA, ElderSpawn, questGiver: true,
                quests: new[] { new DefinitionId(FetchQuest) }, enabled: false);

            Assert.That(NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId("npc.closed"), NpcRole.Quest, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.NpcDisabled));
        }

        [Test]
        public void An_unplaced_or_unknown_npc_is_refused()
        {
            AddNpc("npc.floating", null, null);

            Assert.That(NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId("npc.floating"), NpcRole.Generic, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.NpcNotPlaced));

            Assert.That(NpcInteractionService.TryInteract(NextToNpcs(),
                new DefinitionId("npc.nobody"), NpcRole.Generic, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.UnknownNpc));

            Assert.That(NpcInteractionService.TryInteract(null,
                new DefinitionId(Elder), NpcRole.Generic, NpcContext()).Reason,
                Is.EqualTo(NpcInteractionRejection.MissingContext));
        }

        [Test]
        public void Interaction_never_moves_the_player_or_any_state()
        {
            CharacterLocationState location = NextToNpcs();
            Revision before = location.Revision;
            CombatPosition where = location.Position;

            NpcInteractionService.TryInteract(location, new DefinitionId(Vendor),
                NpcRole.Shop, NpcContext());
            NpcInteractionService.TryInteract(location, new DefinitionId(Statue),
                NpcRole.Shop, NpcContext());

            Assert.That(location.Revision, Is.EqualTo(before));
            Assert.That(location.Position, Is.EqualTo(where),
                "an interaction authorises; it does not act");
        }

        [Test]
        public void Reaching_asks_the_same_question_the_service_answers()
        {
            NPCDefinition elder;
            Npcs.TryGet(new DefinitionId(Elder), out elder);

            Assert.That(NpcInteractionService.CanReach(NextToNpcs(), elder, NpcContext()),
                Is.True);

            CharacterLocationState far = StartAt(TownASpawn);
            far.Position = new CombatPosition(900f, 0f, 900f);

            Assert.That(NpcInteractionService.CanReach(far, elder, NpcContext()), Is.False,
                "so a prompt never appears for something the service would refuse");
        }

        [Test]
        public void No_definition_id_is_compared_against_a_literal_in_the_service()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/NpcInteractionService.cs");

            string[] mustNotAppear =
            {
                Elder, Vendor, Banker, Master, GeneralStore, TownA,
                "Merchant\"", "Blacksmith", "Innkeeper"
            };

            foreach (string forbidden in mustNotAppear)
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    "the service names '" + forbidden + "'; roles must come from data");
            }
        }

        // ---- validation ----------------------------------------------------------------

        [Test]
        public void A_well_formed_npc_and_shop_pass()
        {
            NPCDefinition elder;
            Npcs.TryGet(new DefinitionId(Elder), out elder);

            ShopDefinition shop;
            Shops.TryGet(new DefinitionId(GeneralStore), out shop);

            Assert.That(Validate(elder).IsValid, Is.True);
            Assert.That(Validate(shop).IsValid, Is.True);
        }

        [Test]
        public void A_merchant_with_no_shop_is_an_error()
        {
            NPCDefinition broken = AddNpc("npc.emptymerchant", TownA, ElderSpawn,
                category: NPCCategory.Merchant);

            Assert.That(HasError(Validate(broken), "nothing to open"), Is.True);
        }

        [Test]
        public void A_quest_giver_with_no_quests_is_an_error()
        {
            NPCDefinition broken = AddNpc("npc.silent", TownA, ElderSpawn, questGiver: true);

            Assert.That(HasError(Validate(broken), "offers no quests"), Is.True);
        }

        [Test]
        public void A_job_changer_offering_nothing_is_an_error()
        {
            NPCDefinition broken = AddNpc("npc.idlemaster", TownA, ElderSpawn, jobChanger: true);

            Assert.That(HasError(Validate(broken), "offers no class or job"), Is.True);
        }

        [Test]
        public void An_npc_naming_deleted_content_is_an_error()
        {
            NPCDefinition broken = AddNpc("npc.ghostquests", TownA, ElderSpawn,
                questGiver: true, quests: new[] { new DefinitionId("quest.deleted") });

            Assert.That(HasError(Validate(broken), "does not resolve"), Is.True);
        }

        [Test]
        public void A_shop_with_a_negative_price_is_an_error()
        {
            ShopDefinition broken = AddShop("shop.broken", new[]
            {
                new ShopEntry(new DefinitionId(Potion), -50)
            });

            Assert.That(HasError(Validate(broken), "negative price"), Is.True);
        }

        [Test]
        public void An_unplaced_npc_warns_rather_than_failing()
        {
            NPCDefinition floating = AddNpc("npc.unplaced", null, null);

            ValidationReport report = Validate(floating);

            Assert.That(report.IsValid, Is.True, "content may exist before it is placed");
            Assert.That(report.WarningCount, Is.GreaterThan(0));
        }

        // ---- authoring helpers ---------------------------------------------------------

        private NPCDefinition AddNpc(string id, string map, string spawnPoint,
            NPCCategory category = NPCCategory.Generic, string shop = null,
            bool questGiver = false, bool jobChanger = false, bool storage = false,
            bool enabled = true, DefinitionId[] quests = null, DefinitionId[] classes = null,
            DefinitionId[] jobs = null, float radius = 3f)
        {
            var definition = Track(ScriptableObject.CreateInstance<NPCDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"},"
                + "\"_category\":" + (int)category
                + ",\"_shop\":{\"_value\":\"" + (shop ?? string.Empty) + "\"}"
                + ",\"_isQuestGiver\":" + (questGiver ? "true" : "false")
                + ",\"_isJobChanger\":" + (jobChanger ? "true" : "false")
                + ",\"_providesStorage\":" + (storage ? "true" : "false")
                + ",\"_map\":{\"_value\":\"" + (map ?? string.Empty) + "\"}"
                + ",\"_spawnPoint\":{\"_value\":\"" + (spawnPoint ?? string.Empty) + "\"}"
                + ",\"_enabled\":" + (enabled ? "true" : "false")
                + ",\"_interactionRadius\":" + F(radius) + "}", definition);

            if (quests != null) SetPrivate(definition, "_quests", quests);
            if (classes != null) SetPrivate(definition, "_classesOffered", classes);
            if (jobs != null) SetPrivate(definition, "_jobsOffered", jobs);

            Npcs.Register(definition);
            return definition;
        }

        private ShopDefinition AddShop(string id, ShopEntry[] entries)
        {
            var definition = Track(ScriptableObject.CreateInstance<ShopDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"}}",
                definition);

            SetPrivate(definition, "_entries", entries ?? new ShopEntry[0]);

            Shops.Register(definition);
            return definition;
        }

        private QuestDefinition AddQuestDefinition(string id)
        {
            var definition = Track(ScriptableObject.CreateInstance<QuestDefinition>());
            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"" + id + ".name\"}}",
                definition);

            Quests.Register(definition);
            return definition;
        }

        /// <summary>Authors a class the job changer can point at.</summary>
        /// <remarks>Registered into the item registry only so the composite lookup resolves
        /// the reference: validation asks whether an id exists, not what type it is.</remarks>
        private void AddClassDefinition(string id)
        {
            AddItem(id);
        }
    }
}
