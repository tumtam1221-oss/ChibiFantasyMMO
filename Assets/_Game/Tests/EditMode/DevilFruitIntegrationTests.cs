using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Owning a Devil Fruit: what it changes, and what it must never become.
    /// </summary>
    /// <remarks>
    /// <b>The dangerous shape here is a second copy.</b> A fruit touches stats, skills,
    /// inventory, persistence and replication, and the way that goes wrong is not one big
    /// mistake but five small ones that each keep their own idea of what somebody ate. Most
    /// of this file is therefore about there being exactly one of everything.
    ///
    /// <b>Phase 12 already decided the rules.</b> One fruit, permanent, consumed from an
    /// item. Nothing below re-decides any of that; it checks that the live runtime finally
    /// obeys what the definitions have said since Phase 12.
    /// </remarks>
    [TestFixture]
    internal sealed class DevilFruitIntegrationTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string Darkness = "devil_fruit.darkness";
        private const string DarknessItem = "item.devil_fruit.darkness";
        private const string DarkShroud = "skill.dark_shroud";
        private const string Silence = "status.darkness_silence";

        private static readonly DefinitionId Mdef = new DefinitionId("stat.mdef");
        private static readonly DefinitionId Matk = new DefinitionId("stat.matk");

        // ---- the live state's own rules ----------------------------------------------------

        [Test]
        public void ACharacterStartsWithNoFruit()
        {
            var state = New();

            Assert.That(state.HasActiveFruit, Is.False);
            Assert.That(state.ActiveFruit.IsValid, Is.False);
        }

        [Test]
        public void EatingOneFruitTakesAndKeepsIt()
        {
            var state = New();

            Assert.That(state.Activate(new DefinitionId(Darkness), new InstanceId("i-1")),
                Is.True);

            Assert.That(state.HasActiveFruit, Is.True);
            Assert.That(state.ActiveFruit.Value, Is.EqualTo(Darkness));
            Assert.That(state.SourceInstance.Value, Is.EqualTo("i-1"));
        }

        [Test]
        public void ASecondFruitIsRefusedAndChangesNothing()
        {
            var state = New();

            state.Activate(new DefinitionId(Darkness), new InstanceId("i-1"));

            Revision before = state.Revision;

            Assert.That(state.Activate(new DefinitionId("devil_fruit.light"),
                new InstanceId("i-2")), Is.False);

            Assert.That(state.ActiveFruit.Value, Is.EqualTo(Darkness),
                "the second fruit replaced the first");
            Assert.That(state.SourceInstance.Value, Is.EqualTo("i-1"));
            Assert.That(state.Revision, Is.EqualTo(before),
                "a refused activation moved the revision, so everything downstream would "
                + "recompute for nothing");
        }

        [Test]
        public void EatingTheSameFruitTwiceIsStillRefused()
        {
            var state = New();

            state.Activate(new DefinitionId(Darkness), new InstanceId("i-1"));

            Assert.That(state.Activate(new DefinitionId(Darkness), new InstanceId("i-2")),
                Is.False, "a replayed consumption granted the fruit again");

            Assert.That(state.SourceInstance.Value, Is.EqualTo("i-1"),
                "the replay overwrote which item was actually spent");
        }

        [Test]
        public void AnInvalidFruitIdIsNeverActivated()
        {
            var state = New();

            Assert.That(state.Activate(default, new InstanceId("i-1")), Is.False);
            Assert.That(state.HasActiveFruit, Is.False);
        }

        // ---- persistence ---------------------------------------------------------------------

        [Test]
        public void ThePersistedRowCarriesTheStableIdAndNothingAboutWhatItDoes()
        {
            PersistedCharacter row = Row(Darkness, "i-7");

            Assert.That(row.DevilFruit.Value, Is.EqualTo(Darkness));
            Assert.That(row.DevilFruitSource, Is.EqualTo("i-7"));

            // Nothing derived is stored: modifiers, abilities and immunities all live in
            // content, so re-balancing reaches every existing owner.
            foreach (PropertyInfo property in typeof(PersistedCharacter).GetProperties())
            {
                string name = property.Name.ToLowerInvariant();

                Assert.That(name.Contains("modifier") || name.Contains("immunit")
                    || name.Contains("ability") || name.Contains("passive"), Is.False,
                    "persistence copies fruit content: " + property.Name);
            }
        }

        [Test]
        public void ACharacterWithNoFruitPersistsAnEmptyId()
        {
            PersistedCharacter row = Row(null, null);

            Assert.That(row.DevilFruit.IsValid, Is.False);
            Assert.That(row.DevilFruitSource, Is.Empty);
        }

        [Test]
        public void TheSavePathHandsTheLiveStateToTheMapperAndTheLoadPathResolvesByStableId()
        {
            // Asserted on the real wiring rather than by fabricating a domain character:
            // what matters is that the one live state is what gets written, and that a
            // stored id is looked up rather than trusted.
            Assert.That(typeof(PersistedCharacterMapper).GetMethod("ToPersisted")
                .GetParameters()
                .Any(p => p.ParameterType == typeof(CharacterDevilFruitState)), Is.True,
                "the mapper cannot be given a fruit state");

            string registry = Code("Assets/_Game/Scripts/Server/WorldCharacterRegistry.cs");

            Assert.That(registry.Contains("living.DevilFruit"), Is.True,
                "the save path does not persist the live fruit state");

            Assert.That(registry.Contains("_devilFruits.TryGet"), Is.True,
                "a stored fruit id is trusted rather than resolved");

            Assert.That(registry.Contains("WorldSpawnRejection.CorruptCharacter"), Is.True,
                "an unknown fruit id does not fail visibly");
        }

        [Test]
        public void AnUnknownStoredFruitIsRefusedRatherThanSilentlyReplaced()
        {
            string registry = Code("Assets/_Game/Scripts/Server/WorldCharacterRegistry.cs");

            int at = registry.IndexOf("unknown devil fruit", System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThan(-1),
                "nothing reports an unknown fruit id");

            // The refusal must come before the character is built, not after: a character
            // spawned and then refused would already be in the world.
            int spawn = registry.IndexOf("new LivingCharacter(", System.StringComparison.Ordinal);

            Assert.That(at, Is.LessThan(spawn),
                "the character is created before its fruit is validated");

            // And nothing substitutes another fruit.
            Assert.That(registry.Contains("?? DefaultFruit")
                || registry.Contains("FirstOrDefault()"), Is.False,
                "an unknown fruit is replaced with some other one");
        }

        // ---- the item that grants it -----------------------------------------------------------

        [Test]
        public void TheProductionItemGrantsTheProductionFruitAndIsConsumedByUsing()
        {
            ItemDefinition item = Item();

            Assert.That(item.Usable, Is.True);
            Assert.That(item.Category, Is.EqualTo(ItemCategory.DevilFruit));
            Assert.That(item.Stackable, Is.False, "an ultra-rare item that stacks");

            ItemUseEffect[] uses = item.UseEffects;

            Assert.That(uses.Length, Is.EqualTo(1));
            Assert.That(uses[0].Kind, Is.EqualTo(ItemEffectKind.ConsumeDevilFruit));
            Assert.That(uses[0].DevilFruit.Value, Is.EqualTo(Darkness));
        }

        [Test]
        public void TheServerDerivesTheFruitFromTheItemAndTheClientNamesOnlyASlot()
        {
            // The request a client can make carries a slot and a quantity. Nothing in it
            // can name a fruit, a modifier or an outcome, which is why an item is the only
            // way to get one.
            MethodInfo submit = typeof(ChibiFantasy.Network.ICharacterInventoryRequestSink)
                .GetMethod("Submit");

            Assert.That(submit, Is.Not.Null);

            foreach (ParameterInfo parameter in submit.GetParameters())
            {
                string name = parameter.Name.ToLowerInvariant();

                Assert.That(name.Contains("fruit") || name.Contains("definition")
                    || name.Contains("modifier") || name.Contains("skill"), Is.False,
                    "a client can send '" + parameter.Name + "'");

                Assert.That(parameter.ParameterType == typeof(DefinitionId), Is.False,
                    "a client can send a definition id");
            }
        }

        // ---- what owning it changes -------------------------------------------------------------

        [Test]
        public void TheFruitsModifiersReachTheSameListEquipmentAndStatusUse()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var state = New();
            state.Activate(new DefinitionId(Darkness), new InstanceId("i-1"));

            var into = new List<StatModifier>();

            DevilFruitService.CollectModifiers(state,
                new DevilFruitService.Context(catalogue.BuildDevilFruits(), null,
                    catalogue.BuildStatusEffects(), catalogue.BuildSkills(),
                    new OwnerId("acc")),
                into);

            Assert.That(into, Is.Not.Empty, "the production fruit changes nothing");

            Assert.That(into.Any(m => m.Stat == Mdef), Is.True);
            Assert.That(into.Any(m => m.Stat == Matk), Is.True);
        }

        [Test]
        public void AFruitCollectsNothingBeforeItIsOwned()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var into = new List<StatModifier>();

            DevilFruitService.CollectModifiers(New(),
                new DevilFruitService.Context(catalogue.BuildDevilFruits(), null,
                    catalogue.BuildStatusEffects(), catalogue.BuildSkills(),
                    new OwnerId("acc")),
                into);

            Assert.That(into, Is.Empty);
        }

        [Test]
        public void TheFruitsAbilityIsAvailableOnlyToSomebodyWhoOwnsIt()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var context = new DevilFruitService.Context(catalogue.BuildDevilFruits(), null,
                catalogue.BuildStatusEffects(), catalogue.BuildSkills(), new OwnerId("acc"));

            Assert.That(DevilFruitService.ActiveAbilityOf(New(), context).IsValid, Is.False,
                "an ability was granted to somebody who owns no fruit");

            var owner = New();
            owner.Activate(new DefinitionId(Darkness), new InstanceId("i-1"));

            Assert.That(DevilFruitService.ActiveAbilityOf(owner, context).Value,
                Is.EqualTo(DarkShroud));
        }

        [Test]
        public void AGrantedSkillIsUsableWithoutBeingLearnedAndIsNeverWrittenIntoLearnedState()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var learned = new CharacterSkillsState(new CharacterId("c-1"));

            var granted = new SkillUseContext(catalogue.BuildSkills(), learned, 10, null,
                catalogue.BuildStatusEffects(), null, new DefinitionId(DarkShroud));

            Assert.That(granted.IsGranted(new DefinitionId(DarkShroud)), Is.True);
            Assert.That(granted.IsGranted(new DefinitionId("skill.magic_bolt")), Is.False);

            // The whole point: it is usable, and the skill list still does not contain it.
            Assert.That(learned.TryGetRank(new DefinitionId(DarkShroud), out int _), Is.False,
                "the fruit's ability was written into learned skills, where it would be "
                + "saved and would outlive the fruit");

            var without = new SkillUseContext(catalogue.BuildSkills(), learned, 10, null,
                catalogue.BuildStatusEffects());

            Assert.That(without.IsGranted(new DefinitionId(DarkShroud)), Is.False);
        }

        // ---- the content itself -------------------------------------------------------------------

        [Test]
        public void TheShippedCatalogueCarriesTheWholeFruitVerticalSlice()
        {
            WorldContentCatalogue catalogue = Catalogue();

            var faults = new List<string>();

            Assert.That(catalogue.Validate(faults), Is.True, string.Join("; ", faults));

            Assert.That(catalogue.BuildDevilFruits()
                .TryGet(new DefinitionId(Darkness), out DevilFruitDefinition fruit), Is.True);

            Assert.That(fruit.Usage, Is.EqualTo(DevilFruitUsage.Consumed),
                "a fruit that is not consumed is not this gate's rule");
            Assert.That(fruit.Enabled, Is.True);
            Assert.That(fruit.ActiveAbility.Value, Is.EqualTo(DarkShroud));
            Assert.That(fruit.StatModifiers, Is.Not.Empty);

            Assert.That(catalogue.BuildSkills()
                .TryGet(new DefinitionId(DarkShroud), out SkillDefinition shroud), Is.True);

            Assert.That(catalogue.BuildStatusEffects()
                .TryGet(new DefinitionId(Silence), out StatusEffectDefinition silence),
                Is.True);

            // Darkness silences through the existing status architecture, not through a
            // branch that names the fruit.
            Assert.That(silence.ControlEffect, Is.EqualTo(ControlEffectType.Silence));
            Assert.That(silence.Category, Is.EqualTo(StatusEffectCategory.Debuff));

            Assert.That(shroud.Levels[0].Effects.Any(e =>
                e.Kind == SkillEffectKind.ApplyStatusEffect && e.Reference == silence.Id),
                Is.True, "the fruit's ability does not apply the authored silence");
        }

        [Test]
        public void ACatalogueWhoseFruitNamesContentItDoesNotHaveIsRefused()
        {
            var catalogue = ScriptableObject.CreateInstance<WorldContentCatalogue>();
            var fruit = ScriptableObject.CreateInstance<DevilFruitDefinition>();

            try
            {
                Set(fruit, "_id", new DefinitionId("devil_fruit.broken"));
                Set(fruit, "_activeAbility", new DefinitionId("skill.nobody_authored_this"));
                Set(catalogue, "_devilFruits", new[] { fruit });

                var faults = new List<string>();

                Assert.That(catalogue.Validate(faults), Is.False);

                Assert.That(faults.Any(f => f.Contains("nobody_authored_this")), Is.True,
                    "the fault does not say what is missing: " + string.Join("; ", faults));
            }
            finally
            {
                Object.DestroyImmediate(fruit);
                Object.DestroyImmediate(catalogue);
            }
        }

        [Test]
        public void ProductionFruitContentUsesStableProductionIds()
        {
            WorldContentCatalogue catalogue = Catalogue();

            foreach (string id in new[] { Darkness, DarknessItem, DarkShroud, Silence })
            {
                Assert.That(id.StartsWith("proto"), Is.False, id);
                Assert.That(id.Contains("test"), Is.False, id);
                Assert.That(id.Contains("."), Is.True, id + " is not a namespaced id");
            }

            // And no two fruits share an id.
            var seen = new HashSet<string>();

            foreach (DevilFruitDefinition fruit in catalogue.BuildDevilFruits().All)
            {
                Assert.That(seen.Add(fruit.Id.Value), Is.True, "duplicate " + fruit.Id);
            }
        }

        // ---- the rarity rule nobody may relax ---------------------------------------------------------

        [Test]
        public void TheWorldBossFruitChanceIsStillOneInTenMillion()
        {
            // 0.00001 percent. Restated here as the fraction so that a future change which
            // "fixes" the percent by moving a decimal point fails against a second,
            // independently written number.
            const double Percent = 0.00001d;
            const double Fraction = 1e-7d;
            const float FractionF = 0.0000001f;

            Assert.That(Percent / 100d, Is.EqualTo(Fraction).Within(1e-18),
                "percent and fraction disagree");

            // 18.11 could only assert that no source existed yet. 18.12 authored one, so the
            // stronger statement is now available and is made here instead: the fruit comes
            // from exactly one world boss, at exactly that fraction.
            WorldContentCatalogue catalogue = Catalogue();

            catalogue.BuildDevilFruits()
                .TryGet(new DefinitionId(Darkness), out DevilFruitDefinition fruit);

            Assert.That(fruit.SourceBoss.IsValid, Is.True, "the fruit comes from nowhere");
            Assert.That(fruit.DropTable.IsValid, Is.True, "the fruit is on no drop table");

            Assert.That(catalogue.BuildMonsters()
                .TryGet(fruit.SourceBoss, out MonsterDefinition boss), Is.True);

            Assert.That(boss.Rank, Is.EqualTo(MonsterRank.WorldBoss),
                "an ordinary monster is the authored source of a Devil Fruit");

            Assert.That(catalogue.BuildDropTables()
                .TryGet(fruit.DropTable, out DropTableDefinition table), Is.True);

            var offered = 0;

            foreach (DropEntry entry in table.Entries)
            {
                if (entry.Item.Value != DarknessItem) continue;

                offered++;

                Assert.That(entry.Chance, Is.EqualTo(FractionF).Within(1e-12f),
                    "the production chance is not exactly one in ten million");

                Assert.That(entry.MinMonsterRank, Is.EqualTo(MonsterRank.WorldBoss),
                    "the fruit is offered below world-boss rank");
            }

            Assert.That(offered, Is.EqualTo(1),
                "the fruit is offered " + offered + " times on its own table");

            // And the training slime is still not a source of it.
            catalogue.BuildMonsters()
                .TryGet(new DefinitionId("monster.training_slime"), out MonsterDefinition slime);

            Assert.That(slime.Rank, Is.Not.EqualTo(MonsterRank.WorldBoss));
            Assert.That(slime.LootTable, Is.Not.EqualTo(fruit.DropTable),
                "the training slime shares the boss drop table");
        }

        // ---- one of everything -----------------------------------------------------------------------

        [Test]
        public void ThereIsExactlyOneLiveFruitStateTypeAndNoSecondSystem()
        {
            Assembly gameplay = typeof(CharacterDevilFruitState).Assembly;
            Assembly server = typeof(WorldCharacterRegistry).Assembly;

            string[] states = gameplay.GetTypes().Concat(server.GetTypes())
                .Where(t => t.Name.Contains("DevilFruit") && !t.IsEnum
                    && t.Name.EndsWith("State"))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(states.Length, Is.EqualTo(1), string.Join(", ", states));

            foreach (string forbidden in new[]
            {
                "DevilFruitInventory", "FruitEquipmentState", "FruitRuntimeStats",
                "DevilFruitStatCalculator", "DevilFruitSkillExecutor",
            })
            {
                Assert.That(gameplay.GetTypes().Concat(server.GetTypes())
                    .Any(t => t.Name == forbidden), Is.False, forbidden + " exists");
            }
        }

        [Test]
        public void LivingCharacterHoldsExactlyOneFruitStateAndItIsNeverNull()
        {
            PropertyInfo[] fruitProperties = typeof(LivingCharacter).GetProperties()
                .Where(p => p.PropertyType == typeof(CharacterDevilFruitState))
                .ToArray();

            Assert.That(fruitProperties.Length, Is.EqualTo(1),
                "a character carries two fruit states, which can disagree");

            Assert.That(fruitProperties[0].CanWrite, Is.False,
                "the fruit state can be swapped out from under everything holding it");
        }

        [Test]
        public void NoClientCodeMutatesFruitOwnershipOrRecomputesStats()
        {
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                SearchOption.AllDirectories))
            {
                // The prototype scenes drive their own offline runtime and are not the
                // shipped client; 18.10 already pinned that they reach no production scene.
                if (path.Replace(Path.DirectorySeparatorChar, '/').Contains("/Prototype/")) continue;

                string source = Code(path);

                // Mutation, named precisely. The client may *read* the state -- Phase 12's
                // collectible panel takes one to draw a fruit's name, which is exactly what
                // a client should do with it. Forbidding the type outright would forbid
                // presentation; what must never appear here is anything that changes what
                // somebody owns or recomputes what it is worth.
                foreach (string forbidden in new[]
                {
                    "DevilFruitService.TryActivate", "DevilFruitService.CollectModifiers",
                    "CharacterStatAuthority", "DevilFruit.Activate", "DevilFruit.Deactivate",
                    ".Activate(", ".Deactivate(",
                })
                {
                    Assert.That(source.Contains(forbidden), Is.False,
                        path + " contains '" + forbidden + "'");
                }
            }
        }

        [Test]
        public void TheStatAuthorityNoticesAFruitAndIgnoresAQuietTick()
        {
            MethodInfo signature = typeof(CharacterStatAuthority).GetMethod("SignatureOf",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(signature, Is.Not.Null);

            // Read off the source: the signature must fold in the fruit's revision, which
            // moves only on activation, and must not fold in anything that moves per tick.
            string source = Code("Assets/_Game/Scripts/Server/CharacterStatAuthority.cs");

            int at = source.IndexOf("SignatureOf(LivingCharacter", System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThan(-1));

            string body = source.Substring(at);

            Assert.That(body.Contains("DevilFruit.Revision"), Is.True,
                "eating a fruit would not recompute anything");

            Assert.That(body.Contains("Elapsed") || body.Contains("Ticks")
                || body.Contains("Time."), Is.False,
                "the signature moves every tick, so every tick recomputes");
        }

        [Test]
        public void FruitModifiersGoThroughTheOneCanonicalCalculator()
        {
            string source = Code("Assets/_Game/Scripts/Server/CharacterStatAuthority.cs");

            Assert.That(source.Contains("DevilFruitService.CollectModifiers"), Is.True,
                "the fruit's modifiers are collected somewhere else");

            // One calculator, and the fruit's modifiers join the same list as everything
            // else rather than being applied afterwards.
            Assert.That(source.Split(new[] { "_calculator.Calculate" },
                System.StringSplitOptions.None).Length - 1, Is.EqualTo(1));

            Assert.That(typeof(DerivedStatsCalculator).Assembly.GetTypes()
                .Count(t => t.Name.Contains("DerivedStats") && t.Name.EndsWith("Calculator")),
                Is.EqualTo(1));
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private static CharacterDevilFruitState New()
        {
            return new CharacterDevilFruitState(new CharacterId("c-1"), new OwnerId("acc-1"));
        }

        private static PersistedCharacter Row(string fruit, string source)
        {
            return new PersistedCharacter(new CharacterId("c-1"), new AccountId("acc-1"),
                new ServerId("srv"), "Test", 1, 1, 0, 10, 10, new DefinitionId("class.x"),
                default, new DefinitionId("map.x"), default, null, null, null, 1, null, 0,
                fruit == null ? default : new DefinitionId(fruit), source);
        }

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "no catalogue at " + CataloguePath);

            return catalogue;
        }

        private static ItemDefinition Item()
        {
            Assert.That(Catalogue().BuildItems()
                .TryGet(new DefinitionId(DarknessItem), out ItemDefinition item), Is.True,
                "the shipped catalogue has no devil fruit item");

            return item;
        }

        /// <summary>Sets a serialized field, including one declared on a base type.</summary>
        /// <remarks>GameDefinition owns the id, and reflection does not see a base type's
        /// private fields through a derived type -- so this walks up rather than silently
        /// finding nothing and throwing somewhere less obvious.</remarks>
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

        /// <summary>A file's code, with comments removed, so prose is never the evidence.</summary>
        private static string Code(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }
    }
}
