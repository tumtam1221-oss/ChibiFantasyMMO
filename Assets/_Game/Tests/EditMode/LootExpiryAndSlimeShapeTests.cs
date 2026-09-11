using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Loot that clears itself away, and the shape of the monster that dropped it.
    /// </summary>
    /// <remarks>
    /// <b>Loot.</b> The expiry machinery was already there and already ticking -- a lifetime
    /// on the pile, an age, a sweep in the registry, a republish when the count changes. What
    /// nobody had is a reason to trust it, because no test drove a pile all the way from
    /// dropped to gone. These do, including the parts that are easy to get wrong when it is
    /// added later: a pile that was picked up must not also expire, an expired pile must not
    /// still be collectible, and the map index must be cleared along with the pile or the
    /// registry leaks a row per kill forever.
    ///
    /// <b>Shape.</b> Asserted against the prefab the catalogue actually hands the runtime,
    /// not against the Blender file or the FBX. "The source was updated" is not the same
    /// claim as "the thing that spawns has no curl on it", and only the second one is worth
    /// testing.
    /// </remarks>
    [TestFixture]
    internal sealed class LootExpiryAndSlimeShapeTests : MonsterTestBase
    {
        private const string Slime = "monster.training_slime";

        private const string VisualsPath =
            "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset";

        private const string FbxPath =
            "Assets/_Game/Art/Monsters/TrainingSlime/Monster_TrainingSlime.fbx";

        // ---- loot clears itself away ------------------------------------------------------

        private LootObjectState Pile(float lifetimeSeconds, params string[] items)
        {
            var contents = new List<LootResult>();

            foreach (string item in items)
            {
                contents.Add(new LootResult(InstanceId.New(), new DefinitionId(item), 1));
            }

            return new LootObjectState(InstanceId.New(), InstanceId.New(),
                new CombatPosition(4f, 0f, 2f), contents, LootPolicy.FreeForAll,
                default, lifetimeSeconds);
        }

        private MonsterLootRegistry Registry()
        {
            return new MonsterLootRegistry(null, Items);
        }

        [Test]
        public void UncollectedLootDisappearsOnceItsLifetimeIsUp()
        {
            MonsterLootRegistry registry = Registry();
            LootObjectState pile = Pile(30f, Coin);

            Assert.That(registry.Add(pile, new DefinitionId(HomeMap)), Is.True);
            Assert.That(registry.Count, Is.EqualTo(1));

            // Most of the way there, and still lying on the ground.
            Assert.That(registry.Tick(29f), Is.Zero);
            Assert.That(registry.Count, Is.EqualTo(1));

            Assert.That(registry.Tick(2f), Is.EqualTo(1), "it never expired");
            Assert.That(registry.Count, Is.Zero);
        }

        [Test]
        public void AnExpiredPileCannotBeCollected()
        {
            MonsterLootRegistry registry = Registry();
            LootObjectState pile = Pile(10f, Coin);

            registry.Add(pile, new DefinitionId(HomeMap));
            registry.Tick(11f);

            LootPickupOutcome outcome = registry.Pickup(pile.LootId, 0, Character);

            Assert.That(outcome.IsAccepted, Is.False,
                "loot that has gone from the world was still handed to somebody");
        }

        [Test]
        public void AnExpiredPileLeavesNoEntryBehindInTheRegistry()
        {
            // Both indexes. The pile table and the map table are swept together, or the
            // second one grows by a row for every monster ever killed.
            MonsterLootRegistry registry = Registry();
            LootObjectState pile = Pile(5f, Coin);

            registry.Add(pile, new DefinitionId(HomeMap));
            registry.Tick(6f);

            Assert.That(registry.TryGet(pile.LootId, out _), Is.False, "pile table");
            Assert.That(registry.TryGetMap(pile.LootId, out _), Is.False, "map table");
            Assert.That(registry.All(), Is.Empty);
        }

        [Test]
        public void APileThatWasEmptiedGoesWithoutWaitingForItsLifetime()
        {
            // Picking the last item out is the ordinary way a pile ends, and it must not
            // linger for the rest of its lifetime as an empty marker somebody can click.
            MonsterLootRegistry registry = Registry();
            LootObjectState pile = Pile(600f, Coin);

            registry.Add(pile, new DefinitionId(HomeMap));

            Assert.That(pile.TryClaim(0), Is.True, "fixture: the last entry was taken");

            Assert.That(registry.Tick(0.1f), Is.EqualTo(1));
            Assert.That(registry.Count, Is.Zero);
        }

        [Test]
        public void APileIsOnlySweptOnce()
        {
            // Expiring twice would be a second despawn of something already gone, and -- if
            // anything downstream ever counts sweeps -- a second of whatever that costs.
            MonsterLootRegistry registry = Registry();

            registry.Add(Pile(5f, Coin), new DefinitionId(HomeMap));

            Assert.That(registry.Tick(6f), Is.EqualTo(1));
            Assert.That(registry.Tick(6f), Is.Zero, "it was swept a second time");
            Assert.That(registry.Tick(600f), Is.Zero);
        }

        [Test]
        public void ManyKillsDoNotLeaveAGrowingPileOfPiles()
        {
            // The property the whole thing exists for: a camp fought in for a long time must
            // not accumulate loot without bound.
            MonsterLootRegistry registry = Registry();

            var highWater = 0;

            for (var kill = 0; kill < 40; kill++)
            {
                registry.Add(Pile(30f, Coin), new DefinitionId(HomeMap));

                // one kill every five seconds
                registry.Tick(5f);

                if (registry.Count > highWater) highWater = registry.Count;
            }

            Assert.That(highWater, Is.LessThanOrEqualTo(7),
                "the ground held " + highWater + " piles at once, which grows without bound");

            registry.Tick(60f);

            Assert.That(registry.Count, Is.Zero, "and it all clears when the killing stops");
        }

        [Test]
        public void APileWithNoLifetimeStillNeverExpires()
        {
            // Zero means "no lifetime" and has to keep meaning it: the held-reward path
            // builds piles that are republished rather than aged, and content may want a
            // permanent drop one day.
            MonsterLootRegistry registry = Registry();

            registry.Add(Pile(0f, Coin), new DefinitionId(HomeMap));

            Assert.That(registry.Tick(100000f), Is.Zero);
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheWorldAuthorsALifetimeShortEnoughToClearTheGround()
        {
            // The figure the running server uses, read from the prefab it is serialized on
            // rather than from the field's default -- a default does not apply to a
            // component that was serialized before it changed.
            string prefab = System.IO.File.ReadAllText(
                "Assets/_Game/Prefabs/Network/World_NetworkManager.prefab");

            int at = prefab.IndexOf("_lootLifetimeSeconds:", System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThanOrEqualTo(0), "the world authors no loot lifetime");

            string line = prefab.Substring(at, prefab.IndexOf('\n', at) - at);
            string value = line.Split(':')[1].Trim();

            Assert.That(float.TryParse(value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float seconds),
                Is.True, "unreadable: " + line);

            Assert.That(seconds, Is.GreaterThan(0f),
                "loot never expires, so the ground fills up and stays full");
            Assert.That(seconds, Is.LessThanOrEqualTo(120f),
                "loot lies around for " + seconds + "s, which is long enough to read as "
                + "permanent while a camp is being farmed");
        }

        // ---- the monster that dropped it ----------------------------------------------------

        private static GameObject RuntimePrefab()
        {
            var visuals = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(VisualsPath);

            Assert.That(visuals, Is.Not.Null, VisualsPath + " is missing");

            GameObject prefab = visuals.PrefabFor(new DefinitionId(Slime));

            Assert.That(prefab, Is.Not.Null, Slime + " resolves to no model");

            return prefab;
        }

        [Test]
        public void TheThingThatSpawnsHasNoCurlAndIsDrawnInOnePiece()
        {
            // Asserted on the prefab the catalogue hands the runtime, because "the Blender
            // file was updated" and "the monster in the world has no curl on it" are
            // different claims and only the second one matters.
            //
            // This used to ban the word "bubble" outright, back when the thing to be rid of
            // was a bubble fused to the old model's head. The slime now has two floating
            // ones on purpose and they are covered by their own fixture; what is banned here
            // is the curl, and anything drawn in a second renderer -- the bubbles live in the
            // same mesh, so they cost nothing extra to draw.
            var instance = (GameObject)Object.Instantiate(RuntimePrefab());

            try
            {
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                {
                    foreach (string banned in new[] { "Curl", "Horn", "Antenna", "Nub" })
                    {
                        Assert.That(child.name, Does.Not.Contain(banned),
                            "the runtime model still carries " + child.name);
                    }
                }

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                Assert.That(skinned, Is.Not.Null);

                Assert.That(skinned.bones.Length, Is.LessThanOrEqualTo(10),
                    "the rig has grown to " + skinned.bones.Length + " bones; a beginner "
                    + "monster is the body, its crown and three bones for each bubble");

                Assert.That(instance.GetComponentsInChildren<Renderer>(true).Length,
                    Is.EqualTo(1),
                    "more than one renderer, so a detached piece is being drawn separately");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheBodyIsAWideRoundedBlob()
        {
            // The body only. The renderer's bounds used to be a fair stand-in for it and are
            // not any more: two bubbles now float above the crown, and folding their height
            // into the slime's makes a squat blob measure as very nearly a sphere.
            var instance = (GameObject)Object.Instantiate(RuntimePrefab());

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                System.Collections.Generic.List<int> body =
                    SlimeParts.Islands(skinned.sharedMesh)[0];

                var baked = new Mesh();

                try
                {
                    skinned.BakeMesh(baked, true);

                    Vector3[] posed = baked.vertices;
                    float lo = float.MaxValue, hi = float.MinValue;
                    float left = float.MaxValue, right = float.MinValue;

                    foreach (int i in body)
                    {
                        Vector3 v = instance.transform.InverseTransformPoint(
                            skinned.transform.TransformPoint(posed[i]));

                        if (v.y < lo) lo = v.y;
                        if (v.y > hi) hi = v.y;
                        if (v.x < left) left = v.x;
                        if (v.x > right) right = v.x;
                    }

                    float tall = hi - lo;
                    float wide = right - left;

                    Assert.That(tall, Is.LessThan(wide),
                        "it is taller than it is wide, which is not a squat blob");

                    float ratio = tall / wide;

                    Assert.That(ratio, Is.GreaterThan(0.6f),
                        "height is only " + ratio.ToString("0.00") + " of its width, which is "
                        + "a pancake rather than a jelly");
                    Assert.That(ratio, Is.LessThan(0.9f));
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheModelComesFromTheOneFbxAndThereIsOnlyOne()
        {
            // A second, older FBX somewhere in the project is exactly how a remodel appears
            // to have been done and not to have arrived.
            string[] candidates = System.IO.Directory.GetFiles("Assets", "*.fbx",
                System.IO.SearchOption.AllDirectories);

            var slimes = new List<string>();

            foreach (string file in candidates)
            {
                string normalized = file.Replace('\\', '/');

                if (normalized.ToLowerInvariant().Contains("slime")) slimes.Add(normalized);
            }

            Assert.That(slimes.Count, Is.EqualTo(1),
                "there is more than one slime model in the project: "
                + string.Join(", ", slimes));

            var instance = (GameObject)Object.Instantiate(RuntimePrefab());

            try
            {
                var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);

                Assert.That(AssetDatabase.GetAssetPath(skinned.sharedMesh),
                    Is.EqualTo(FbxPath),
                    "the runtime mesh does not come from the one shipped model");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheNameplateStillClearsTheShorterBody()
        {
            var visuals = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(VisualsPath);
            var instance = (GameObject)Object.Instantiate(RuntimePrefab());

            try
            {
                instance.transform.position = Vector3.zero;

                float plate = visuals.NameplateHeightFor(new DefinitionId(Slime));
                float top = instance.GetComponentInChildren<SkinnedMeshRenderer>(true).bounds.max.y;

                Assert.That(plate - WorldMonsterPresenter.BarDrop, Is.GreaterThan(top),
                    "the health bar would be drawn inside the slime");
                Assert.That(plate, Is.LessThan(top + 0.45f),
                    "the name floats " + (plate - top).ToString("0.00") + " m above a "
                    + top.ToString("0.00") + " m creature, which reads as unattached");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---- and the range it attacks from ----------------------------------------------------

        [Test]
        public void TheSlimeClosesBeforeItSwings()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(
                "Assets/_Game/Data/Production/WorldContentCatalogue.asset");

            Assert.That(catalogue.BuildMonsters().TryGet(new DefinitionId(Slime),
                out MonsterDefinition slime), Is.True);

            var instance = (GameObject)Object.Instantiate(RuntimePrefab());

            float radius;

            try
            {
                instance.transform.position = Vector3.zero;
                radius = instance.GetComponentInChildren<SkinnedMeshRenderer>(true)
                    .bounds.extents.x;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            // It used to swing from 1.5 m, which on a creature 0.17 m across left most of a
            // body length of empty air between them.
            Assert.That(slime.AttackRange, Is.LessThanOrEqualTo(1.0f),
                "it starts attacking from " + slime.AttackRange + " m, which leaves a gap");

            Assert.That(slime.AttackRange, Is.GreaterThan(radius * 2f),
                "it would have to overlap the player before it could attack");
        }
    }
}
