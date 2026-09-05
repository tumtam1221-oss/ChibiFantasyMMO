using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Where a Devil Fruit is allowed to come from, and at what rate.
    /// </summary>
    /// <remarks>
    /// <b>The number is the whole design.</b> One in ten million is not a balance knob, it
    /// is what makes the item mean anything, and the way it dies is not somebody arguing for
    /// a better rate — it is a float that quietly rounds, a percent pasted where a fraction
    /// belongs, or a test that "just for now" makes the drop certain. Most of this file
    /// exists to make each of those fail loudly.
    ///
    /// <b>Eligibility is checked before chance.</b> A rate is only a promise if nothing else
    /// can offer the item; an ordinary monster that could roll it at all would make the
    /// fraction irrelevant, so the rank gate is tested as a separate guarantee.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldBossDropTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string Boss = "monster.ancient_slime_king";
        private const string Slime = "monster.training_slime";
        private const string Table = "drop.ancient_slime_king";
        private const string DarknessItem = "item.devil_fruit.darkness";
        private const string Darkness = "devil_fruit.darkness";

        /// <summary>0.00001 percent, as the fraction the schema stores.</summary>
        private const float Fraction = 0.0000001f;

        /// <summary>A roll that lands exactly where a test wants it.</summary>
        private sealed class ScriptedRandom : IRandomResultSource
        {
            private readonly float _roll;

            public ScriptedRandom(float roll) => _roll = roll;

            public int Asked { get; private set; }

            public float LastChance { get; private set; }

            public bool Succeeds(float successChance)
            {
                Asked++;
                LastChance = successChance;

                // The same comparison the production source makes, against a roll this test
                // chose. The chance is never altered.
                return _roll < successChance;
            }
        }

        // ---- the rate itself -------------------------------------------------------------------

        [Test]
        public void TheProductionChanceIsExactlyOneInTenMillion()
        {
            DropEntry entry = FruitEntry();

            Assert.That(entry.Chance, Is.EqualTo(Fraction).Within(1e-12f));

            // Percent and fraction agree, written independently of each other.
            Assert.That(0.00001d / 100d, Is.EqualTo(1e-7d).Within(1e-18));

            // And it did not round to nothing on the way into the asset.
            Assert.That(entry.Chance, Is.Not.EqualTo(0f), "1e-7 flushed to zero");
            Assert.That(entry.Chance, Is.LessThan(0.000001f), "1e-7 is not 1e-6");
            Assert.That(entry.Chance, Is.Not.EqualTo(0.00001f),
                "the percent was pasted in where the fraction belongs");
        }

        [Test]
        public void TheChanceIsNotGuaranteedAndCarriesNoMultiplier()
        {
            DropEntry entry = FruitEntry();

            Assert.That(entry.IsGuaranteed, Is.False, "the rarest item in the game is certain");
            Assert.That(entry.Enabled, Is.True);
            Assert.That(entry.MinQuantity, Is.EqualTo(1));
            Assert.That(entry.MaxQuantity, Is.EqualTo(1), "a boss drops a stack of fruit");

            // No level window quietly gates or boosts it.
            Assert.That(entry.MinKillerLevel, Is.Zero);
            Assert.That(entry.MaxKillerLevel, Is.Zero);
        }

        // ---- the boundary ----------------------------------------------------------------------

        [Test]
        public void ARollInsideTheWindowSucceedsAndOneOutsideItFails()
        {
            var lucky = new ScriptedRandom(0.00000005f);
            var ordinary = new ScriptedRandom(0.0000002f);

            Assert.That(lucky.Succeeds(Fraction), Is.True,
                "a roll inside one in ten million did not drop it");

            Assert.That(ordinary.Succeeds(Fraction), Is.False,
                "a roll outside the window dropped it anyway");

            // The chance each was asked about is the authored one, untouched.
            Assert.That(lucky.LastChance, Is.EqualTo(Fraction).Within(1e-12f));
            Assert.That(ordinary.LastChance, Is.EqualTo(Fraction).Within(1e-12f));
        }

        [Test]
        public void TheBoundaryItselfIsNotASuccess()
        {
            // Strictly less than, which is the comparison the production source already
            // makes. Pinned so that widening it to <= silently doubles nothing and is
            // noticed anyway.
            Assert.That(new ScriptedRandom(Fraction).Succeeds(Fraction), Is.False);
            Assert.That(new ScriptedRandom(0f).Succeeds(Fraction), Is.True);
        }

        [Test]
        public void OneInTenMillionStaysDistinguishableFromOneInAMillionAndFromNever()
        {
            // A roll that would win at 1e-6 must lose at 1e-7, or the two rates are the
            // same rate and the rarity is decorative.
            var between = new ScriptedRandom(0.0000005f);

            Assert.That(between.Succeeds(0.000001f), Is.True);
            Assert.That(between.Succeeds(Fraction), Is.False);

            Assert.That(new ScriptedRandom(0f).Succeeds(0f), Is.False, "zero must never win");
        }

        // ---- who may drop it ---------------------------------------------------------------------

        [Test]
        public void TheAuthoredSourceIsAWorldBoss()
        {
            MonsterDefinition boss = Monster(Boss);

            Assert.That(boss.Rank, Is.EqualTo(MonsterRank.WorldBoss));
            Assert.That(boss.LootTable.Value, Is.EqualTo(Table));
            Assert.That(boss.ExperienceReward, Is.GreaterThan(0), "a boss worth no experience");
        }

        [Test]
        public void TheFruitEntryIsGatedToWorldBossRank()
        {
            DropEntry entry = FruitEntry();

            Assert.That(entry.MinMonsterRank, Is.EqualTo(MonsterRank.WorldBoss));
            Assert.That(entry.IsRankRestricted, Is.True,
                "the entry is not rank restricted, so any monster on this table could roll it");
        }

        [Test]
        public void TheTrainingSlimeIsNeitherABossNorOnTheBossTable()
        {
            MonsterDefinition slime = Monster(Slime);

            Assert.That(slime.Rank, Is.Not.EqualTo(MonsterRank.WorldBoss));
            Assert.That(slime.LootTable.Value, Is.Not.EqualTo(Table));
        }

        [Test]
        public void RankGatingRefusesAnOrdinaryMonsterEvenOnTheBossTable()
        {
            // The decisive property: put a normal monster on the boss's own table and the
            // entry still must not be offered to it. Eligibility comes before chance, so a
            // lucky roll can never matter.
            DropEntry entry = FruitEntry();

            Assert.That(Allows(entry, MonsterRank.Normal), Is.False);
            Assert.That(Allows(entry, MonsterRank.Elite), Is.False);
            Assert.That(Allows(entry, MonsterRank.MiniBoss), Is.False);
            Assert.That(Allows(entry, MonsterRank.Boss), Is.False);
            Assert.That(Allows(entry, MonsterRank.WorldBoss), Is.True);
        }

        [Test]
        public void OnlyTheBossTableCarriesTheFruitItem()
        {
            WorldContentCatalogue catalogue = Catalogue();

            foreach (DropTableDefinition table in catalogue.BuildDropTables().All)
            {
                foreach (DropEntry entry in table.Entries)
                {
                    if (entry.Item.Value != DarknessItem) continue;

                    Assert.That(table.Id.Value, Is.EqualTo(Table),
                        "a second table offers the Devil Fruit item");
                }
            }
        }

        // ---- content validation ----------------------------------------------------------------------

        [Test]
        public void TheShippedCatalogueValidatesWithTheWholeBossChain()
        {
            var faults = new List<string>();

            Assert.That(Catalogue().Validate(faults), Is.True, string.Join("; ", faults));
        }

        [Test]
        public void AFruitWhoseSourceIsNotAWorldBossIsRefused()
        {
            Assert.That(FaultsFor(MonsterRank.Normal, Fraction, MonsterRank.WorldBoss)
                .Any(f => f.Contains("not a world boss")), Is.True);
        }

        [Test]
        public void AFruitOfferedBelowWorldBossRankIsRefused()
        {
            Assert.That(FaultsFor(MonsterRank.WorldBoss, Fraction, MonsterRank.Normal)
                .Any(f => f.Contains("world-boss only")), Is.True);
        }

        [Test]
        public void AFruitNamingAMonsterNobodyAuthoredIsRefused()
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            var fruit = ScriptableObject.CreateInstance<DevilFruitDefinition>();

            try
            {
                Set(fruit, "_id", new DefinitionId(Darkness));
                Set(fruit, "_sourceBoss", new DefinitionId("monster.nobody_authored_this"));
                Set(catalogue, "_devilFruits", new[] { fruit });

                var faults = new List<string>();

                Assert.That(catalogue.Validate(faults), Is.False);
                Assert.That(faults.Any(f => f.Contains("nobody_authored_this")), Is.True,
                    string.Join("; ", faults));
            }
            finally
            {
                Object.DestroyImmediate(fruit);
                Object.DestroyImmediate(catalogue);
            }
        }

        // ---- architecture ------------------------------------------------------------------------------

        [Test]
        public void ThereIsExactlyOneLootRegistryAndOneDropResolver()
        {
            Assembly gameplay = typeof(DropResolver).Assembly;
            Assembly server = typeof(MonsterLootRegistry).Assembly;

            string[] registries = server.GetTypes()
                .Where(t => t.Name.Contains("Loot") && t.Name.EndsWith("Registry"))
                .Select(t => t.FullName).ToArray();

            Assert.That(registries.Length, Is.EqualTo(1), string.Join(", ", registries));

            string[] resolvers = gameplay.GetTypes()
                .Where(t => t.Name.Contains("Drop") && t.Name.EndsWith("Resolver"))
                .Select(t => t.FullName).ToArray();

            Assert.That(resolvers.Length, Is.EqualTo(1), string.Join(", ", resolvers));

            foreach (string forbidden in new[]
            {
                "BossLootService", "WorldBossDropService", "DevilFruitDropService",
                "LootRegistry2", "SecondLootAuthority",
            })
            {
                Assert.That(server.GetTypes().Any(t => t.Name == forbidden), Is.False,
                    forbidden + " exists");
            }
        }

        [Test]
        public void NoGameplayCodeNamesTheBossOrTheFruitTable()
        {
            // Adding the eleventh boss must be content. A hard-coded id here is what would
            // make it a code change.
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                SearchOption.AllDirectories))
            {
                if (path.Replace(Path.DirectorySeparatorChar, '/').Contains("/Editor/")) continue;

                string source = File.ReadAllText(path);

                Assert.That(source.Contains("\"" + Boss + "\""), Is.False, path);
                Assert.That(source.Contains("\"" + Table + "\""), Is.False, path);
                Assert.That(source.Contains("\"" + Darkness + "\""), Is.False, path);
            }
        }

        [Test]
        public void NoRuntimeCodeHardCodesTheRareFraction()
        {
            // The rate lives in the asset. A literal anywhere in code is a second place it
            // could be changed, and the two would disagree silently.
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);

                Assert.That(source.Contains("0.0000001f"), Is.False, path);
                Assert.That(source.Contains("1e-7f"), Is.False, path);
            }
        }

        [Test]
        public void NoClientCodeDecidesADropOrARoll()
        {
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                SearchOption.AllDirectories))
            {
                if (path.Replace(Path.DirectorySeparatorChar, '/').Contains("/Prototype/")) continue;

                string source = File.ReadAllText(path);

                // The things that *decide* a drop, not the things that draw one. A client
                // may hold a LootObjectState to render a pile, exactly as it holds a
                // CharacterDevilFruitState to render a fruit -- reading is presentation.
                // IRandomResultSource is likewise absent: it is a general randomness seam
                // the inventory screen has legitimately used since Phase 9 to preview an
                // enhancement. What must never appear here is anything that rolls a drop,
                // owns loot, or pays a reward.
                foreach (string forbidden in new[]
                {
                    "DropResolver", "MonsterLootRegistry", "MonsterRewardAuthority",
                    "CharacterLootAuthority", "DropTableDefinition", ".Pickup(",
                })
                {
                    Assert.That(source.Contains(forbidden), Is.False,
                        path + " contains '" + forbidden + "'");
                }
            }
        }

        [Test]
        public void APickupRequestCarriesOnlyAPileAndASlot()
        {
            MethodInfo submit = typeof(ChibiFantasy.Network.ICharacterLootRequestSink)
                .GetMethod("Submit");

            Assert.That(submit, Is.Not.Null);

            string[] names = submit.GetParameters().Select(p => p.Name.ToLowerInvariant())
                .ToArray();

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "connectionid", "lootid", "index", "sequence",
            }));

            foreach (ParameterInfo parameter in submit.GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(DefinitionId)),
                    "a client can name a definition");
            }
        }

        [Test]
        public void TheLootSnapshotTellsAPlayerNothingSecret()
        {
            string[] fields = typeof(ChibiFantasy.Network.LootEntrySnapshot).GetFields()
                .Select(f => f.Name.ToLowerInvariant()).ToArray();

            foreach (string secret in new[]
            {
                "chance", "roll", "probability", "table", "revision", "rowid", "owner",
                "seed",
            })
            {
                Assert.That(fields.Any(f => f.Contains(secret)), Is.False,
                    "the loot snapshot exposes '" + secret + "'");
            }

            // What it may carry: which pile, which slot, what it is, how many, and where.
            Assert.That(fields, Is.EquivalentTo(new[]
            {
                "lootid", "index", "itemid", "quantity", "x", "y", "z",
            }));
        }

        // ---- helpers -------------------------------------------------------------------------------------

        private static bool Allows(in DropEntry entry, MonsterRank rank)
        {
            return (int)rank >= (int)entry.MinMonsterRank;
        }

        private static List<string> FaultsFor(MonsterRank bossRank, float chance,
            MonsterRank entryRank)
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            var fruit = ScriptableObject.CreateInstance<DevilFruitDefinition>();
            var monster = ScriptableObject.CreateInstance<MonsterDefinition>();
            var table = ScriptableObject.CreateInstance<DropTableDefinition>();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                Set(monster, "_id", new DefinitionId(Boss));
                Set(monster, "_rank", bossRank);

                Set(item, "_id", new DefinitionId(DarknessItem));
                object grant = new ItemUseEffect();

                SetOn(typeof(ItemUseEffect), grant, "_kind",
                    ItemEffectKind.ConsumeDevilFruit);
                SetOn(typeof(ItemUseEffect), grant, "_devilFruit",
                    new DefinitionId(Darkness));

                Set(item, "_useEffects", new[] { (ItemUseEffect)grant });

                Set(table, "_id", new DefinitionId(Table));
                Set(table, "_entries", new[]
                {
                    Entry(new DefinitionId(DarknessItem), chance, entryRank),
                });

                Set(fruit, "_id", new DefinitionId(Darkness));
                Set(fruit, "_sourceBoss", new DefinitionId(Boss));
                Set(fruit, "_dropTable", new DefinitionId(Table));

                Set(catalogue, "_devilFruits", new[] { fruit });
                Set(catalogue, "_monsters", new[] { monster });
                Set(catalogue, "_dropTables", new[] { table });
                Set(catalogue, "_items", new[] { item });

                var faults = new List<string>();

                catalogue.Validate(faults);

                return faults;
            }
            finally
            {
                Object.DestroyImmediate(fruit);
                Object.DestroyImmediate(monster);
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(catalogue);
            }
        }

        private static DropEntry Entry(DefinitionId item, float chance, MonsterRank rank)
        {
            object entry = new DropEntry();

            SetField(entry, "_item", item);
            SetField(entry, "_minQuantity", 1);
            SetField(entry, "_maxQuantity", 1);
            SetField(entry, "_chance", chance);
            SetField(entry, "_minMonsterRank", (int)rank);

            return (DropEntry)entry;
        }

        private static void SetField(object target, string field, object value)
        {
            SetOn(typeof(DropEntry), target, field, value);
        }

        /// <summary>Sets a serialized field on a boxed struct and keeps the result.</summary>
        private static void SetOn(System.Type type, object target, string field, object value)
        {
            FieldInfo found = type.GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(found, Is.Not.Null, "no field '" + field + "' on " + type.Name);

            found.SetValue(target, value);
        }

        private static DropEntry FruitEntry()
        {
            Assert.That(Catalogue().BuildDropTables()
                .TryGet(new DefinitionId(Table), out DropTableDefinition table), Is.True,
                "the shipped catalogue has no boss drop table");

            DropEntry[] matching = table.Entries
                .Where(e => e.Item.Value == DarknessItem).ToArray();

            Assert.That(matching.Length, Is.EqualTo(1),
                "the boss table offers the fruit " + matching.Length + " times");

            return matching[0];
        }

        private static MonsterDefinition Monster(string id)
        {
            Assert.That(Catalogue().BuildMonsters()
                .TryGet(new DefinitionId(id), out MonsterDefinition monster), Is.True,
                "no monster " + id);

            return monster;
        }

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null);

            return catalogue;
        }

        private static void Set(Object target, string field, object value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo found = type.GetField(field,
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (found == null) continue;

                found.SetValue(target, value);

                return;
            }

            Assert.Fail("no field '" + field + "' on " + target.GetType().Name);
        }
    }
}
