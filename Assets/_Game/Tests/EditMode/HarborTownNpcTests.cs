using System.Collections.Generic;
using System.IO;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Harbor Town's five townspeople, as they actually ship.
    /// </summary>
    /// <remarks>
    /// <b>Against the real content, not a fixture.</b> <c>NpcInteractionTests</c> already
    /// proves the rules with NPCs it authors in code. What that cannot catch is the thing
    /// that actually breaks: a definition nobody added to the catalogue, a prefab carrying
    /// the wrong id, a spawn point at coordinates the model is not standing on. Every test
    /// here loads the shipped asset, because every one of those failures leaves a player
    /// clicking a person who does not answer.
    ///
    /// <b>The placement is an assertion, not an input.</b> The five models were arranged by
    /// hand and must not move. Their positions are pinned here so that a spawn point edited
    /// without the scene -- or a scene edited without the spawn point -- fails in a second
    /// rather than becoming an NPC nobody can get close enough to talk to.
    /// </remarks>
    internal sealed class HarborTownNpcTests
    {
        private const string Catalogue =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string PrefabFolder = "Assets/_Game/Prefabs/Presentation/NPC/";

        private const string HarborTown = "Assets/_Game/Scenes/World/HarborTown.unity";

        private const string Map = "map.harbor_town";

        /// <summary>Every townsperson, with the role a click on them must resolve.</summary>
        private static readonly Townsperson[] Five =
        {
            new Townsperson("NPC_HarborGuide", "npc.harbor_guide",
                NpcRole.Quest, "spawn.harbor_town.guide", 4f, -0.25050354f, -7f, "<Guide>"),

            new Townsperson("NPC_GeneralStoreMerchant", "npc.general_store_merchant",
                NpcRole.Shop, "spawn.harbor_town.merchant", -5.5f, -0.25050354f, -3.6f,
                "<Shop>"),

            new Townsperson("NPC_Blacksmith", "npc.blacksmith",
                NpcRole.Enhancement, "spawn.harbor_town.blacksmith", 9.2f, -0.25050354f,
                -8.2f, "<Blacksmith>"),

            new Townsperson("NPC_StorageKeeper", "npc.storage_keeper",
                NpcRole.Storage, "spawn.harbor_town.storage", -13.5f, -0.25050354f, -8.2f,
                "<Storage>"),

            new Townsperson("NPC_JobGuide", "npc.job_guide",
                NpcRole.JobChange, "spawn.harbor_town.job_guide", 2.6f, -0.00024414062f,
                8.4f, "<Job>"),
        };

        private readonly struct Townsperson
        {
            public Townsperson(string prefab, string npc, NpcRole role, string spawn,
                float x, float y, float z, string tag)
            {
                Prefab = prefab;
                Npc = npc;
                Role = role;
                Spawn = spawn;
                X = x;
                Y = y;
                Z = z;
                Tag = tag;
            }

            public string Prefab { get; }
            public string Npc { get; }
            public NpcRole Role { get; }
            public string Spawn { get; }
            public float X { get; }
            public float Y { get; }
            public float Z { get; }
            public string Tag { get; }

            public override string ToString() => Npc;
        }

        private WorldContentCatalogue _content;
        private DefinitionRegistry<NPCDefinition> _npcs;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private DefinitionRegistry<ShopDefinition> _shops;
        private DefinitionRegistry<QuestDefinition> _quests;

        [SetUp]
        public void LoadShippedContent()
        {
            _content = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(Catalogue);

            Assert.That(_content, Is.Not.Null, "the production catalogue is missing");

            _npcs = _content.BuildNpcs();
            _spawns = _content.BuildSpawnPoints();
            _shops = _content.BuildShops();
            _quests = _content.BuildQuests();
        }

        private NpcInteractionService.Context Context()
            => new NpcInteractionService.Context(_npcs, _spawns, _shops, _quests);

        // ---- identity ---------------------------------------------------------------------

        [Test]
        public void All_five_townspeople_are_shipped_in_the_catalogue()
        {
            foreach (Townsperson who in Five)
            {
                Assert.That(_npcs.TryGet(new DefinitionId(who.Npc), out NPCDefinition npc),
                    Is.True, who.Npc + " is not in the catalogue, so the server could never "
                    + "name it and every interaction with it would be refused");

                Assert.That(npc.Enabled, Is.True, who.Npc + " is switched off");
                Assert.That(npc.Map.Value, Is.EqualTo(Map));
            }
        }

        [Test]
        public void Each_prefab_carries_the_id_of_the_npc_it_is()
        {
            foreach (Townsperson who in Five)
            {
                WorldNpcMarker marker = MarkerOn(who.Prefab);

                Assert.That(marker, Is.Not.Null,
                    who.Prefab + " has no WorldNpcMarker, so clicking it does nothing");

                Assert.That(marker.NpcId, Is.EqualTo(who.Npc),
                    who.Prefab + " points at the wrong NPC");
            }
        }

        [Test]
        public void No_two_townspeople_share_an_identity()
        {
            // Two models carrying one id is the failure that looks like it works: both open
            // the same screen, and only one of them is ever the NPC content meant.
            var seen = new HashSet<string>();

            foreach (Townsperson who in Five)
            {
                Assert.That(seen.Add(MarkerOn(who.Prefab).NpcId), Is.True,
                    "two NPC prefabs carry the id " + who.Npc);
            }
        }

        [Test]
        public void Nothing_decides_who_an_npc_is_from_its_name_or_its_position()
        {
            // The rule the whole gate rests on. If a GameObject name or a coordinate ever
            // appears in the runtime path, renaming an object in the hierarchy or nudging it
            // half a metre silently changes what it does.
            foreach (string file in new[]
            {
                "Assets/_Game/Scripts/Client/World/WorldNpcPresenter.cs",
                "Assets/_Game/Scripts/Client/World/WorldNpcMarker.cs",
                "Assets/_Game/Scripts/Server/CharacterNpcAuthority.cs",
                "Assets/_Game/Scripts/UI/NpcDialogueView.cs",
            })
            {
                string code = Code(file);

                foreach (Townsperson who in Five)
                {
                    Assert.That(code.Contains(who.Npc), Is.False,
                        file + " names '" + who.Npc + "'. Behaviour must come from the "
                        + "definition, never from a branch on which NPC it is");

                    Assert.That(code.Contains(who.Prefab), Is.False,
                        file + " names the GameObject '" + who.Prefab + "'");
                }
            }
        }

        // ---- roles ------------------------------------------------------------------------

        [Test]
        public void Each_townsperson_offers_the_service_they_are_there_for()
        {
            foreach (Townsperson who in Five)
            {
                _npcs.TryGet(new DefinitionId(who.Npc), out NPCDefinition npc);

                Assert.That(npc.HasRole(who.Role), Is.True,
                    who.Npc + " does not offer " + who.Role);

                Assert.That(WorldNpcPresenter.PrimaryRole(npc), Is.EqualTo(who.Role),
                    "clicking " + who.Npc + " would ask for the wrong service");
            }
        }

        [Test]
        public void The_role_tag_above_their_head_matches_what_they_do()
        {
            foreach (Townsperson who in Five)
            {
                _npcs.TryGet(new DefinitionId(who.Npc), out NPCDefinition npc);

                Assert.That(WorldNpcPresenter.RoleTag(npc), Is.EqualTo(who.Tag), who.Npc);
            }
        }

        [Test]
        public void A_townsperson_offers_only_their_own_service()
        {
            // The blacksmith must not be a bank and the storage keeper must not sell.
            foreach (Townsperson who in Five)
            {
                _npcs.TryGet(new DefinitionId(who.Npc), out NPCDefinition npc);

                foreach (Townsperson other in Five)
                {
                    if (other.Role == who.Role) continue;

                    Assert.That(npc.HasRole(other.Role), Is.False,
                        who.Npc + " also offers " + other.Role);
                }
            }
        }

        [Test]
        public void The_merchant_has_something_to_sell()
        {
            _npcs.TryGet(new DefinitionId("npc.general_store_merchant"),
                out NPCDefinition merchant);

            Assert.That(_shops.TryGet(merchant.Shop, out ShopDefinition shop), Is.True,
                "the merchant carries a shop the catalogue does not ship");

            Assert.That(shop.Entries.Length, Is.GreaterThan(0),
                "an empty shop opens and shows a player nothing, which reads as a bug");
        }

        [Test]
        public void The_guide_has_a_quest_that_resolves()
        {
            _npcs.TryGet(new DefinitionId("npc.harbor_guide"), out NPCDefinition guide);

            Assert.That(guide.Quests.Length, Is.GreaterThan(0));

            foreach (DefinitionId quest in guide.Quests)
            {
                Assert.That(_quests.TryGet(quest, out QuestDefinition _), Is.True,
                    "the guide offers '" + quest + "', which nothing ships");
            }
        }

        // ---- placement --------------------------------------------------------------------

        [Test]
        public void Each_spawn_point_is_where_the_model_actually_stands()
        {
            // The distance check measures from the spawn, not from the model. If the two
            // disagree the NPC is visibly in front of the player and refuses them anyway.
            foreach (Townsperson who in Five)
            {
                Assert.That(_spawns.TryGet(new DefinitionId(who.Spawn),
                    out SpawnPointDefinition spawn), Is.True,
                    who.Npc + " stands at '" + who.Spawn + "', which is not shipped");

                Assert.That(spawn.SpawnType, Is.EqualTo(SpawnType.Npc), who.Spawn);
                Assert.That(spawn.Map.Value, Is.EqualTo(Map), who.Spawn);

                Assert.That(spawn.X, Is.EqualTo(who.X).Within(0.001f), who.Npc + " x");
                Assert.That(spawn.Y, Is.EqualTo(who.Y).Within(0.001f), who.Npc + " y");
                Assert.That(spawn.Z, Is.EqualTo(who.Z).Within(0.001f), who.Npc + " z");
            }
        }

        [Test]
        public void The_scene_holds_exactly_one_of_each_model()
        {
            string scene = File.ReadAllText(HarborTown);

            foreach (Townsperson who in Five)
            {
                string guid = GuidOf(PrefabFolder + who.Prefab + ".prefab");

                int instances = CountPrefabInstances(scene, guid);

                Assert.That(instances, Is.EqualTo(1),
                    who.Prefab + " appears " + instances + " times in Harbor Town; a second "
                    + "copy is a second person answering to the same name");
            }
        }

        [Test]
        public void The_placed_models_still_stand_where_they_were_put()
        {
            string scene = File.ReadAllText(HarborTown);

            foreach (Townsperson who in Five)
            {
                string guid = GuidOf(PrefabFolder + who.Prefab + ".prefab");

                Assert.That(PositionOf(scene, guid, "x"), Is.EqualTo(who.X).Within(0.001f),
                    who.Prefab + " has been moved on x");

                Assert.That(PositionOf(scene, guid, "z"), Is.EqualTo(who.Z).Within(0.001f),
                    who.Prefab + " has been moved on z");
            }
        }

        // ---- what the server allows -------------------------------------------------------

        [Test]
        public void A_player_standing_next_to_them_may_talk()
        {
            foreach (Townsperson who in Five)
            {
                NpcInteractionResult result = NpcInteractionService.TryInteract(
                    StandingAt(who.X, who.Y, who.Z), new DefinitionId(who.Npc), who.Role,
                    Context());

                Assert.That(result.IsAccepted, Is.True,
                    who.Npc + " refused a player standing on top of them: " + result.Reason);

                Assert.That(result.Role, Is.EqualTo(who.Role));
            }
        }

        [Test]
        public void A_player_across_the_town_may_not()
        {
            foreach (Townsperson who in Five)
            {
                NpcInteractionResult result = NpcInteractionService.TryInteract(
                    StandingAt(who.X + 40f, who.Y, who.Z), new DefinitionId(who.Npc),
                    who.Role, Context());

                Assert.That(result.IsAccepted, Is.False, who.Npc + " from forty metres away");

                Assert.That(result.Reason, Is.EqualTo(NpcInteractionRejection.TooFar));
            }
        }

        [Test]
        public void The_edge_of_the_reach_is_where_content_says_it_is()
        {
            // Just inside and just outside the authored radius, so a change to the radius is
            // a deliberate one rather than something that drifts.
            _npcs.TryGet(new DefinitionId("npc.blacksmith"), out NPCDefinition smith);

            float reach = smith.InteractionRadius;

            Assert.That(reach, Is.GreaterThan(0f), "the blacksmith authors no reach");

            Townsperson who = Five[2];

            Assert.That(NpcInteractionService.TryInteract(
                    StandingAt(who.X + reach - 0.1f, who.Y, who.Z),
                    new DefinitionId(who.Npc), who.Role, Context()).IsAccepted,
                Is.True, "refused just inside the authored reach");

            Assert.That(NpcInteractionService.TryInteract(
                    StandingAt(who.X + reach + 0.1f, who.Y, who.Z),
                    new DefinitionId(who.Npc), who.Role, Context()).IsAccepted,
                Is.False, "allowed just outside the authored reach");
        }

        [Test]
        public void Asking_a_townsperson_for_somebody_elses_service_is_refused()
        {
            foreach (Townsperson who in Five)
            {
                foreach (Townsperson other in Five)
                {
                    if (other.Role == who.Role) continue;

                    NpcInteractionResult result = NpcInteractionService.TryInteract(
                        StandingAt(who.X, who.Y, who.Z), new DefinitionId(who.Npc),
                        other.Role, Context());

                    Assert.That(result.IsAccepted, Is.False,
                        who.Npc + " agreed to provide " + other.Role);
                }
            }
        }

        [Test]
        public void An_npc_a_client_invented_is_refused()
        {
            NpcInteractionResult result = NpcInteractionService.TryInteract(
                StandingAt(4f, 0f, -7f), new DefinitionId("npc.free_money"),
                NpcRole.Shop, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(NpcInteractionRejection.UnknownNpc));
        }

        [Test]
        public void A_player_on_another_map_is_refused_however_close_the_numbers_look()
        {
            // Coordinates repeat between maps. Standing at Harbor Town's numbers while on
            // the outskirts must not reach Harbor Town's blacksmith.
            var elsewhere = new CharacterLocationState(new CharacterId("char-1"),
                new DefinitionId("map.harbor_outskirts"),
                new DefinitionId("spawn.harbor_outskirts.entry"))
            {
                Position = new CombatPosition(9.2f, -0.25f, -8.2f),
            };

            NpcInteractionResult result = NpcInteractionService.TryInteract(elsewhere,
                new DefinitionId("npc.blacksmith"), NpcRole.Enhancement, Context());

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(NpcInteractionRejection.WrongMap));
        }

        // ---- what the player is shown -----------------------------------------------------

        [Test]
        public void Every_service_has_something_to_say()
        {
            foreach (Townsperson who in Five)
            {
                string body = NpcDialogueView.BodyFor(who.Role, null);

                Assert.That(body, Is.Not.Empty, who.Role + " opens a blank panel");
            }
        }

        [Test]
        public void The_refusal_codes_the_ui_words_match_the_ones_the_rules_use()
        {
            // The UI assembly cannot see the Gameplay enum, so it mirrors the numbers. If
            // they ever drift, "you are too far away" starts appearing for something else.
            foreach (NpcInteractionRejection reason in
                System.Enum.GetValues(typeof(NpcInteractionRejection)))
            {
                string name = reason.ToString();

                Assert.That(System.Enum.IsDefined(typeof(NpcInteractionRejectionCode), name),
                    Is.True, "the UI has no mirror for " + name);

                var mirrored = (NpcInteractionRejectionCode)System.Enum.Parse(
                    typeof(NpcInteractionRejectionCode), name);

                Assert.That((int)mirrored, Is.EqualTo((int)reason),
                    name + " has a different number on each side");
            }
        }

        [Test]
        public void A_name_is_readable_even_with_no_translation_shipped()
        {
            _npcs.TryGet(new DefinitionId("npc.general_store_merchant"),
                out NPCDefinition merchant);

            Assert.That(WorldNpcPresenter.FallbackName(merchant),
                Is.EqualTo("General Store Merchant"));
        }

        // ---- helpers ----------------------------------------------------------------------

        private static CharacterLocationState StandingAt(float x, float y, float z)
        {
            return new CharacterLocationState(new CharacterId("char-1"),
                new DefinitionId(Map), new DefinitionId("spawn.harbor_town.plaza"))
            {
                Position = new CombatPosition(x, y, z),
            };
        }

        private static WorldNpcMarker MarkerOn(string prefabName)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabFolder + prefabName + ".prefab");

            Assert.That(prefab, Is.Not.Null, prefabName + " is missing");

            return prefab.GetComponent<WorldNpcMarker>();
        }

        private static string GuidOf(string assetPath)
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);

            Assert.That(guid, Is.Not.Empty, assetPath + " is not in the project");

            return guid;
        }

        /// <summary>How many prefab instances in a scene point at one prefab.</summary>
        /// <remarks>Counted from the source reference each PrefabInstance block carries, so
        /// a stray mention of the guid elsewhere in the file is not mistaken for a copy of
        /// the object.</remarks>
        private static int CountPrefabInstances(string scene, string guid)
        {
            var marker = "m_SourcePrefab: {fileID: 100100000, guid: " + guid;

            var count = 0;
            var at = 0;

            while (true)
            {
                at = scene.IndexOf(marker, at, System.StringComparison.Ordinal);

                if (at < 0) return count;

                count++;
                at += marker.Length;
            }
        }

        private static float PositionOf(string scene, string guid, string axis)
        {
            // The modification block for this instance ends where the next document does.
            int at = scene.IndexOf("guid: " + guid, System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThanOrEqualTo(0), "no instance for guid " + guid);

            int start = scene.LastIndexOf("--- !u!1001", at, System.StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThanOrEqualTo(0), "no PrefabInstance block");

            int end = scene.IndexOf("\n--- ", at, System.StringComparison.Ordinal);

            if (end < 0) end = scene.Length;

            string block = scene.Substring(start, end - start);

            var key = "propertyPath: m_LocalPosition." + axis;

            int key_at = block.IndexOf(key, System.StringComparison.Ordinal);

            Assert.That(key_at, Is.GreaterThanOrEqualTo(0),
                "no " + axis + " override for guid " + guid);

            int value_at = block.IndexOf("value: ", key_at, System.StringComparison.Ordinal)
                + "value: ".Length;

            int line_end = block.IndexOf('\n', value_at);

            return float.Parse(block.Substring(value_at, line_end - value_at).Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A file's code, with its comments removed.</summary>
        /// <remarks>So a remark may explain which NPCs exist without the test reading that
        /// as a branch on one.</remarks>
        private static string Code(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("/*")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }
    }
}
