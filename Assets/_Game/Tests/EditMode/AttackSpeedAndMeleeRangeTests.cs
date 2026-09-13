using System.Collections.Generic;
using System.IO;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Attack speed as a derived stat, the server pacing swings from it, the client pacing
    /// requests and animation from the same figure, the attack clip that fits inside it,
    /// and the training slime committing to a bite only once it is close enough to land one.
    /// </summary>
    /// <remarks>
    /// <b>The figures below are the production content's.</b> The stat and formula assets
    /// are loaded from the catalogue and run through the one calculator, so a rebalance of
    /// <c>formula_aspd</c> shows up here as a changed number rather than a passing test
    /// that quietly stopped describing the game.
    /// </remarks>
    internal static class AttackSpeedFixture
    {
        public const string Catalogue = "Assets/_Game/Data/Production/WorldContentCatalogue.asset";
        public const string Agi = "stat.agi";
        public const string Aspd = "stat.aspd";

        public static WorldContentCatalogue Content()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(Catalogue);

            Assert.That(catalogue, Is.Not.Null, "no production catalogue");

            return catalogue;
        }

        /// <summary>The production attack speed of a character with this much agility.</summary>
        public static int AspdFor(int agi, IReadOnlyList<StatModifier> modifiers = null)
        {
            WorldContentCatalogue content = Content();

            var baseStats = new CharacterStatsState(new CharacterId("aspd-fixture"));
            baseStats.Set(new DefinitionId(Agi), agi);

            DerivedStatsResult derived = new DerivedStatsCalculator().Calculate(baseStats,
                content.Formulas, content.BuildStats(), modifiers ?? new List<StatModifier>());

            Assert.That(derived.TryGet(content.AttackSpeedStat, out int aspd), Is.True,
                "the production formulas produce no attack speed");

            return aspd;
        }

        public static string Source(string path)
        {
            Assert.That(File.Exists(path), path + " is missing");

            return File.ReadAllText(path);
        }

        /// <summary>The source with its comments removed, so a remark cannot trip a scan.</summary>
        public static string Code(string path)
        {
            var kept = new List<string>();

            foreach (string line in Source(path).Split('\n'))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                kept.Add(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return string.Join("\n", kept);
        }
    }

    // =====================================================================================
    // B. the stat
    // =====================================================================================

    [TestFixture]
    public sealed class AttackSpeedFormulaTests
    {
        [Test]
        public void TheProductionWorldNamesAnAttackSpeedStatWithAFormula()
        {
            WorldContentCatalogue content = AttackSpeedFixture.Content();

            Assert.That(content.AttackSpeedStat.IsValid, Is.True,
                "the catalogue names no attack-speed stat, so every character swings at the "
                + "default and AGI does nothing");
            Assert.That(content.AttackSpeedStat.Value, Is.EqualTo(AttackSpeedFixture.Aspd));
            Assert.That(content.BuildStats().TryGet(content.AttackSpeedStat, out StatDefinition stat),
                Is.True, "the stat is named but not defined");
            Assert.That(stat.IsPrimary, Is.False, "attack speed is derived, never allocated");

            var found = false;

            foreach (DerivedStatFormulaDefinition formula in content.Formulas)
            {
                if (formula != null && formula.DerivedStat == content.AttackSpeedStat) found = true;
            }

            Assert.That(found, Is.True, "no formula produces the attack-speed stat");
            var faults = new List<string>();

            Assert.That(content.Validate(faults), Is.True,
                "the production catalogue no longer validates: " + string.Join("; ", faults));
        }

        [Test]
        public void AgilityRaisesAttackSpeedAndShortensTheInterval()
        {
            int one = AttackSpeedFixture.AspdFor(1);
            int twenty = AttackSpeedFixture.AspdFor(20);
            int fifty = AttackSpeedFixture.AspdFor(50);
            int hundred = AttackSpeedFixture.AspdFor(100);

            Assert.That(twenty, Is.GreaterThan(one), "20 AGI swings no faster than 1");
            Assert.That(fifty, Is.GreaterThan(twenty));
            Assert.That(hundred, Is.GreaterThan(fifty));

            Assert.That(AttackSpeed.IntervalSeconds(hundred),
                Is.LessThan(AttackSpeed.IntervalSeconds(one)));

            // The figures the report quotes, so the report cannot drift from the content.
            Assert.That(one, Is.EqualTo(100));
            Assert.That(twenty, Is.EqualTo(110));
            Assert.That(fifty, Is.EqualTo(125));
            Assert.That(hundred, Is.EqualTo(150));
        }

        [Test]
        public void MoreAgilityNeverMakesTheIntervalLonger()
        {
            float last = float.MaxValue;

            for (var agi = 0; agi <= 400; agi += 4)
            {
                float interval = AttackSpeed.IntervalSeconds(AttackSpeedFixture.AspdFor(agi));

                Assert.That(interval, Is.LessThanOrEqualTo(last + 1e-6f),
                    "the interval got longer going from " + (agi - 4) + " to " + agi + " AGI");

                last = interval;
            }
        }

        [Test]
        public void EachPointOfAgilityBuysLessTimeThanTheOneBefore()
        {
            // Diminishing returns on the interval: 1 -> 20 AGI saves more time than 81 -> 100.
            float low = AttackSpeed.IntervalSeconds(AttackSpeedFixture.AspdFor(1))
                - AttackSpeed.IntervalSeconds(AttackSpeedFixture.AspdFor(20));
            float high = AttackSpeed.IntervalSeconds(AttackSpeedFixture.AspdFor(81))
                - AttackSpeed.IntervalSeconds(AttackSpeedFixture.AspdFor(100));

            Assert.That(low, Is.GreaterThan(high));
            Assert.That(high, Is.GreaterThan(0f), "the top of the range is flat");
        }

        [Test]
        public void TheBaselineBeginnerSwingsAboutOnceASecond()
        {
            // The development character has no stat rows at all, which is what a brand-new
            // character has: agility zero, attack speed the formula's constant.
            int aspd = AttackSpeedFixture.AspdFor(0);
            float perSecond = AttackSpeed.SwingsPerSecond(aspd);

            Assert.That(perSecond, Is.InRange(0.9f, 1.2f),
                "the baseline is " + perSecond + " swings a second");
        }

        [Test]
        public void AttackSpeedIsBoundedInContentAndInCode_AndTheTwoAgree()
        {
            WorldContentCatalogue content = AttackSpeedFixture.Content();

            Assert.That(content.BuildStats().TryGet(content.AttackSpeedStat, out StatDefinition stat),
                Is.True);

            Assert.That(stat.MinValue, Is.EqualTo(AttackSpeed.Minimum),
                "the content floor and the code floor differ; one of them is a lie");
            Assert.That(stat.MaxValue, Is.EqualTo(AttackSpeed.Maximum));

            // Absurd agility: the calculator clamps to the definition ...
            Assert.That(AttackSpeedFixture.AspdFor(100000), Is.EqualTo(AttackSpeed.Maximum));

            // ... and the code clamps anything that arrives from a definition that did not.
            Assert.That(AttackSpeed.Clamp(100000), Is.EqualTo(AttackSpeed.Maximum));
            Assert.That(AttackSpeed.Clamp(-5), Is.EqualTo(AttackSpeed.Default), "unset is the default");
            Assert.That(AttackSpeed.Clamp(0), Is.EqualTo(AttackSpeed.Default));
            Assert.That(AttackSpeed.Clamp(7), Is.EqualTo(AttackSpeed.Minimum));

            Assert.That(AttackSpeed.IntervalSeconds(AttackSpeed.Maximum), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(AttackSpeed.IntervalSeconds(AttackSpeed.Minimum), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(AttackSpeed.IntervalSeconds(int.MaxValue), Is.GreaterThanOrEqualTo(0.5f),
                "there is a way to get an interval shorter than the floor");
        }

        [Test]
        public void FlatAndPercentModifiersReachAttackSpeedThroughTheOneCalculator()
        {
            var stat = new DefinitionId(AttackSpeedFixture.Aspd);

            int bare = AttackSpeedFixture.AspdFor(20);

            int flat = AttackSpeedFixture.AspdFor(20, new[]
            {
                new StatModifier(stat, StatModifierKind.Flat, 15f),
            });

            int percent = AttackSpeedFixture.AspdFor(20, new[]
            {
                new StatModifier(stat, StatModifierKind.Percent, 0.10f),
            });

            Assert.That(bare, Is.EqualTo(110));
            Assert.That(flat, Is.EqualTo(125), "+15 ASPD flat");
            Assert.That(percent, Is.EqualTo(121), "+10% attack speed on 110");
        }
    }

    // =====================================================================================
    // B. equipment, through the authored modifier path and no special case
    // =====================================================================================

    [TestFixture]
    internal sealed class AttackSpeedEquipmentTests : CollectibleTestBase
    {
        private const string SwiftRing = "item.swift_ring";
        private const string HeavyMaul = "item.heavy_maul";

        [Test]
        public void ARingWithAnAttackSpeedModifierSpeedsUpItsWearer()
        {
            var stat = new DefinitionId(AttackSpeedFixture.Aspd);

            AddEquipment(SwiftRing, EquipmentSlot.Accessory, EquipmentCategory.Accessory,
                EquipmentSubtype.Accessory,
                modifiers: new[] { new StatModifier(stat, StatModifierKind.Percent, 0.10f) });

            var context = new EquipmentModifierResolver.Context(Items);
            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(Equipment(SwiftRing), context, modifiers);

            Assert.That(modifiers.Count, Is.EqualTo(1), "the ring's modifier was not collected");

            int bare = AttackSpeedFixture.AspdFor(20);
            int worn = AttackSpeedFixture.AspdFor(20, modifiers);

            Assert.That(worn, Is.GreaterThan(bare));
            Assert.That(worn, Is.EqualTo(121), "110 and ten percent");
            Assert.That(AttackSpeed.IntervalSeconds(worn), Is.LessThan(AttackSpeed.IntervalSeconds(bare)));
        }

        [Test]
        public void AWeaponCanCarryItsOwnBaseSpeedAsAFlatModifier()
        {
            // How a weapon category gets a base speed later: a flat figure on the weapon,
            // authored, collected like any other modifier. No weapon type appears in code.
            var stat = new DefinitionId(AttackSpeedFixture.Aspd);

            AddEquipment(HeavyMaul, EquipmentSlot.MainHand, EquipmentCategory.Weapon,
                EquipmentSubtype.OneHandSword,
                modifiers: new[] { new StatModifier(stat, StatModifierKind.Flat, -20f) });

            var modifiers = new List<StatModifier>();

            EquipmentModifierResolver.Collect(Equipment(HeavyMaul),
                new EquipmentModifierResolver.Context(Items), modifiers);

            Assert.That(AttackSpeedFixture.AspdFor(20, modifiers), Is.EqualTo(90),
                "a slow weapon takes twenty off the wearer's figure");

            foreach (string file in new[]
                {
                    "Assets/_Game/Scripts/Gameplay/AttackSpeed.cs",
                    "Assets/_Game/Scripts/Server/ServerCombatPipeline.cs",
                    "Assets/_Game/Scripts/Gameplay/BasicAttackRules.cs",
                })
            {
                string source = AttackSpeedFixture.Source(file);

                Assert.That(source, Does.Not.Contain("item."),
                    file + " names an item id; equipment reaches attack speed through data only");
                Assert.That(source, Does.Not.Match(@"\bdagger\b|\bbow\b|\bstaff\b").IgnoreCase.Or.Contain("///"),
                    file + " special-cases a weapon type");
            }
        }
    }

    // =====================================================================================
    // B. the server
    // =====================================================================================

    [TestFixture]
    internal sealed class AttackSpeedServerTests : MonsterTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private const string Target = "monster.punching_bag";
        private const int Connection = 11;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _runtime;
        private CombatCommandAuthority _commands;
        private CharacterStatAuthority _stats;
        private WorldContentCatalogue _content;
        private readonly List<Object> _local = new List<Object>();
        private long _sequence;

        [SetUp]
        public void SetUpWorld()
        {
            _content = AttackSpeedFixture.Content();

            AddMonster(Target, level: 3, stats: new[]
            {
                new StatValue(new DefinitionId(MaxHp), 100000),
                new StatValue(new DefinitionId(Atk), 1f),
                new StatValue(new DefinitionId(Def), 0),
            });

            _store = new FakeStore();

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, 20);
            _runtime = new MonsterWorldRuntime(_players, Monsters, new DefinitionId(MaxHp), Enemies);
            _commands = new CombatCommandAuthority(_players, _ => true, _runtime);

            // The production stat content, so the attack speed the pipeline reads is the one
            // the one calculator produced from agility -- not a number typed into a test.
            _stats = new CharacterStatAuthority(_players, _content.Formulas, _content.BuildStats(),
                null, default, _content.MaxHealthStat, _content.MaxManaStat);

            _sequence = 0;
        }

        [TearDown]
        public void TearDownWorld()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            _local.Add(spawn);

            return spawn;
        }

        private LivingCharacter AddPlayer(string character, int agi)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 14, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap), default,
                new[]
                {
                    new PersistedStat(new DefinitionId(MaxHp), 100),
                    new PersistedStat(new DefinitionId(Atk), 20),
                    new PersistedStat(new DefinitionId(Def), 5),
                    new PersistedStat(new DefinitionId(AttackSpeedFixture.Agi), agi),
                },
                null, null, 1);

            WorldSpawnResult result = _players.Spawn(Connection,
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-" + character),
                    new CharacterId(character), new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    Contracts.SessionState.EnteringWorld),
                new ResourceLimits(100, 50), Players);

            Assert.That(result.IsSpawned, Is.True, "player fixture: " + result.Detail);

            result.Character.Location.Position = new CombatPosition(0f, 0f, 0f);
            result.Character.Combatant.Position = new CombatPosition(0f, 0f, 0f);

            Assert.That(_stats.Force(result.Character), Is.True, "stats were not computed");

            return result.Character;
        }

        private LivingMonster SpawnTarget()
        {
            _runtime.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Target),
                new CombatPosition(1f, 0f, 0f), 0f, 1, 0f, new DefinitionId(HomeMap)));
            _runtime.PopulateAll();

            foreach (LivingMonster living in _runtime.All())
            {
                if (living.IsAlive) return living;
            }

            Assert.Fail("no target");

            return null;
        }

        private ServerCombatPipeline Pipeline()
        {
            return new ServerCombatPipeline(_commands, _runtime, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 3f)
                    .WithAttackSpeed(_content.AttackSpeedStat));
        }

        private CombatCommand Attack(LivingMonster monster)
        {
            return new CombatCommand(default, monster.Instance, default, 0, ++_sequence);
        }

        [Test]
        public void TheServerComputesTheIntervalFromTheCharactersOwnAgility()
        {
            LivingCharacter character = AddPlayer("char-quick", agi: 100);
            LivingMonster target = SpawnTarget();
            ServerCombatPipeline pipeline = Pipeline();

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True);

            Assert.That(pipeline.LastAttackSpeed, Is.EqualTo(150), "100 AGI is 150 ASPD in production");
            Assert.That(pipeline.TryGetAttackInterval(character.Character, out float interval), Is.True);
            Assert.That(interval, Is.EqualTo(AttackSpeed.IntervalSeconds(150)).Within(1e-4f));
            Assert.That(interval, Is.EqualTo(2f / 3f).Within(1e-3f));
        }

        [Test]
        public void AClientAskingFasterThanItsIntervalIsRefusedUntilTheIntervalHasPassed()
        {
            AddPlayer("char-eager", agi: 0);
            LivingMonster target = SpawnTarget();
            ServerCombatPipeline pipeline = Pipeline();

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True);

            // Asked again at once, and again after most of the interval: refused, not served.
            ServerCombatResult again = pipeline.Execute(Connection, Attack(target));

            Assert.That(again.IsAccepted, Is.False);
            Assert.That(again.AttackRejection, Is.EqualTo(AttackRejection.NotReady));

            pipeline.Tick(0.9f);

            Assert.That(pipeline.Execute(Connection, Attack(target)).AttackRejection,
                Is.EqualTo(AttackRejection.NotReady), "0.9 s is inside a 1.0 s interval");

            pipeline.Tick(0.11f);

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True,
                "the interval has passed and the swing is still refused");
        }

        [Test]
        public void MoreAgilityLetsTheServerAcceptSoonerAndNothingTheClientSendsDoes()
        {
            AddPlayer("char-agile", agi: 100);
            LivingMonster target = SpawnTarget();
            ServerCombatPipeline pipeline = Pipeline();

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True);

            pipeline.Tick(0.7f);

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True,
                "at 150 ASPD the next swing is allowed after two thirds of a second");

            // The command carries a target and a sequence. There is no field a client could
            // put an attack speed in, so nothing in a request can move the interval.
            string command = AttackSpeedFixture.Source("Assets/_Game/Scripts/Server/CombatCommandAuthority.cs");

            Assert.That(command, Does.Not.Match(@"AttackSpeed|Aspd|Interval"),
                "the command boundary has learned about attack speed");
        }

        [Test]
        public void RulesThatNameNoStatKeepWhateverTimingThePipelineWasBuiltWith()
        {
            AddPlayer("char-legacy", agi: 100);
            LivingMonster target = SpawnTarget();

            var pipeline = new ServerCombatPipeline(_commands, _runtime, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 3f));

            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True);
            Assert.That(pipeline.Execute(Connection, Attack(target)).IsAccepted, Is.True,
                "instant timing, as every pipeline before the stat existed");
        }

        [Test]
        public void TheProductionServerPacesFromTheCatalogueStatAndPublishesTheSameOne()
        {
            string bootstrap = AttackSpeedFixture.Source("Assets/_Game/Scripts/Server/WorldServerBootstrap.cs");

            Assert.That(bootstrap, Does.Contain(".WithAttackSpeed(_content.AttackSpeedStat)"),
                "the production pipeline is not paced from the content's attack-speed stat");
            Assert.That(bootstrap, Does.Contain("replication.UseAttackSpeedStat(_content.AttackSpeedStat)"),
                "the figure clients see is not the one the server paces with");
        }
    }

    // =====================================================================================
    // A + B + F: the client -- cadence, animation, one clean cycle
    // =====================================================================================

    [TestFixture]
    public sealed class AttackCadenceAndAnimationTests
    {
        private const string Controller = "Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller";
        private const string Presenter = "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs";
        private const string Pointer = "Assets/_Game/Scripts/Client/World/WorldPointerInput.cs";
        private const string CombatInput = "Assets/_Game/Scripts/Client/World/WorldCombatInput.cs";
        private const string Feedback = "Assets/_Game/Scripts/Client/World/CombatFeedback.cs";
        private const string MaleClip = "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy@CrossPunch.fbx";
        private const string MaleGuard = "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy@GuardIdle.fbx";
        private const string FemaleClip = "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy@CrossPunch.fbx";
        private const string FemaleGuard = "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy@GuardIdle.fbx";
        private const string MaleModel = "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy.fbx";
        private const string FemaleModel = "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy.fbx";

        private static AnimationClip Clip(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var clip = asset as AnimationClip;

                if (clip != null && !clip.name.StartsWith("__preview")) return clip;
            }

            Assert.Fail("no clip in " + path);

            return null;
        }

        // ---- F. cadence -------------------------------------------------------------------

        [Test]
        public void RequestsAreSpacedByTheCharactersAttackSpeedRatherThanAConstant()
        {
            var pacer = new AttackRequestPacer();

            float slow = AttackSpeed.IntervalSeconds(100);
            float fast = AttackSpeed.IntervalSeconds(200);

            Assert.That(pacer.TryBegin(0f, slow), Is.True);
            Assert.That(pacer.TryBegin(0.6f, slow), Is.False, "the old 0.6 s cadence is back");
            Assert.That(pacer.TryBegin(1.0f, slow), Is.False, "asked a hair early is refused by the server");
            Assert.That(pacer.TryBegin(1.0f + AttackRequestPacer.SlackSeconds, slow), Is.True);

            pacer.Reset();

            Assert.That(pacer.TryBegin(0f, fast), Is.True);
            Assert.That(pacer.TryBegin(0.3f, fast), Is.False);
            Assert.That(pacer.TryBegin(0.5f + AttackRequestPacer.SlackSeconds, fast), Is.True,
                "a faster character asks sooner");
            Assert.That(pacer.Sent, Is.EqualTo(4), "two requests went out in each half");
        }

        [Test]
        public void ThePointerNoLongerCarriesItsOwnAttackInterval()
        {
            string pointer = AttackSpeedFixture.Source(Pointer);
            string input = AttackSpeedFixture.Source(CombatInput);

            Assert.That(pointer, Does.Not.Contain("_attackInterval"),
                "the pointer still paces attacks from a typed-in number");
            Assert.That(pointer, Does.Not.Contain("_nextAttack"));
            Assert.That(pointer, Does.Contain("RequestAttackWhenReady()"),
                "the pointer bypasses the paced request");

            Assert.That(input, Does.Not.Contain("_attackInterval"));
            Assert.That(input, Does.Contain("AttackSpeed.IntervalSeconds("),
                "the cadence is not derived from the replicated attack speed");
            Assert.That(input, Does.Contain("owned.AttackSpeed"));
        }

        [Test]
        public void TheClientNeverTellsTheServerItsAttackSpeed()
        {
            string input = AttackSpeedFixture.Source(CombatInput);
            string entity = AttackSpeedFixture.Source("Assets/_Game/Scripts/Network/CharacterNetworkEntity.cs");

            int request = entity.IndexOf("public void RequestAttack(", System.StringComparison.Ordinal);

            Assert.That(request, Is.GreaterThan(0));
            Assert.That(entity.Substring(request, 200), Does.Not.Contain("AttackSpeed").And.Not.Contain("aspd"),
                "the attack request carries an attack speed, which the server would have to disbelieve");

            // The replicated figure is written by one server method and read everywhere else.
            Assert.That(entity, Does.Contain("_attackSpeed.Value = attackSpeed"));
            Assert.That(entity.Split(new[] { "_attackSpeed.Value =" }, System.StringSplitOptions.None).Length,
                Is.EqualTo(2), "the attack speed is written from more than one place");
            Assert.That(input, Does.Not.Contain("_attackSpeed"), "the input writes the replicated stat");
        }

        // ---- ASPD -> animation ----------------------------------------------------------

        [Test]
        public void TheAttackAnimationSpeedFollowsTheAcceptedInterval()
        {
            float clip = CombatFeedback.BasicAttackClipSeconds;
            float max = CombatFeedback.MaxPlaybackRate;

            Assert.That(AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(100), clip, max),
                Is.EqualTo(1f), "at the default speed the clip plays as authored");
            Assert.That(AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(50), clip, max),
                Is.EqualTo(1f), "a slow character is never played in slow motion");

            float at150 = AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(150), clip, max);
            float at200 = AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(200), clip, max);

            Assert.That(at150, Is.EqualTo(1.3f).Within(1e-3f), "0.867 s of clip into two thirds of a second");
            Assert.That(at200, Is.GreaterThan(at150));
            Assert.That(at200, Is.LessThanOrEqualTo(max));
            Assert.That(max, Is.LessThan(2f), "past twice speed the contact pose is a blur");
        }

        [Test]
        public void OneAcceptedSwingIsOneCycleThatFitsInsideTheIntervalAtEverySpeed()
        {
            // The property that makes restarting mid-swing unnecessary: at every attack speed
            // the server can grant, the cycle the client draws is over before the server can
            // accept the next swing.
            for (int aspd = AttackSpeed.Minimum; aspd <= AttackSpeed.Maximum; aspd++)
            {
                float interval = AttackSpeed.IntervalSeconds(aspd);
                float rate = AttackSpeed.PlaybackRate(interval, CombatFeedback.BasicAttackClipSeconds,
                    CombatFeedback.MaxPlaybackRate);
                float cycle = CombatFeedback.BasicAttackClipSeconds / rate;

                Assert.That(cycle, Is.LessThanOrEqualTo(interval + 1e-4f),
                    "at " + aspd + " ASPD the cycle (" + cycle + " s) outlasts the interval ("
                    + interval + " s), so the next swing would restart it");
            }
        }

        [Test]
        public void TheContactFrameArrivesEarlierWhenTheClipPlaysFaster_AndTheHitIsHeldForExactlyThat()
        {
            var host = new GameObject("feedback");

            try
            {
                var feedback = host.AddComponent<CombatFeedback>();

                // A swing at 1.5x: contact is two thirds as far away.
                feedback.NoteSwing(CombatFeedback.BasicAttackImpactSeconds / 1.5f, now: 10f);

                float held = feedback.ImpactDelayAt(10.02f);

                Assert.That(held, Is.EqualTo(CombatFeedback.BasicAttackImpactSeconds / 1.5f).Within(1e-5f));

                // And the monster's queue holds the blow for that long, no more, no less.
                var queue = new MonsterHitQueue(30);
                queue.Observe(22, 10.02f, held);

                Assert.That(queue.TryDraw(10.02f + held - 0.01f, out DrawnHit _), Is.False, "drawn early");
                Assert.That(queue.TryDraw(10.02f + held + 0.001f, out DrawnHit hit), Is.True, "never drawn");
                Assert.That(hit.Amount, Is.EqualTo(8), "the number is not the server's difference");

                // A blow with no fresh swing to explain it falls back to the clip's own contact.
                Assert.That(feedback.ImpactDelayAt(11f), Is.EqualTo(CombatFeedback.BasicAttackImpactSeconds));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ThePresenterScalesOnlyTheAttackStateAndNeverTheAnimator()
        {
            string presenter = AttackSpeedFixture.Source(Presenter);

            Assert.That(presenter, Does.Contain("SetFloat(AttackSpeedHash, PlaybackRate)"),
                "the attack state's rate is never written");
            Assert.That(presenter, Does.Not.Match(@"_animator\.speed\s*="),
                "the whole animator is scaled, so running speeds up with attack speed");
            Assert.That(presenter, Does.Contain("AttackSpeed.PlaybackRate("),
                "the rate is not the one conversion");
            Assert.That(presenter, Does.Contain("AttackSpeed.IntervalSeconds(_entity.AttackSpeed)"),
                "the rate is not derived from the replicated attack speed");
            Assert.That(presenter, Does.Contain("feedback.NoteSwing("),
                "monsters are not told how fast the swing that is about to hit them is playing");
        }

        [Test]
        public void TheControllerScalesOnlyTheAttackState()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Controller);

            Assert.That(controller, Is.Not.Null);

            var hasParameter = false;

            foreach (AnimatorControllerParameter p in controller.parameters)
            {
                if (p.name == "AttackSpeed")
                {
                    hasParameter = true;
                    Assert.That(p.type, Is.EqualTo(AnimatorControllerParameterType.Float));
                    Assert.That(p.defaultFloat, Is.EqualTo(1f), "a controller nobody wrote to plays the clip slow");
                }
            }

            Assert.That(hasParameter, Is.True, "no AttackSpeed parameter");

            UnityEditor.Animations.AnimatorState attack = null;

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name == "BasicPunch")
                {
                    attack = child.state;

                    continue;
                }

                Assert.That(child.state.speedParameterActive, Is.False,
                    child.state.name + " reads a speed parameter; only the attack may");
                Assert.That(child.state.speed, Is.EqualTo(1f), child.state.name + " is not at speed one");
            }

            Assert.That(attack, Is.Not.Null, "no attack state");
            Assert.That(attack.speedParameterActive, Is.True, "the attack state ignores AttackSpeed");
            Assert.That(attack.speedParameter, Is.EqualTo("AttackSpeed"));
            Assert.That(attack.speed, Is.EqualTo(1f));
            Assert.That(attack.motion, Is.Not.Null);
            Assert.That(attack.motion.name, Is.EqualTo("MeshyCrossPunch_M"),
                "the attack state plays " + attack.motion.name + ", not the production cross punch");
        }

        // ---- A. the clip ----------------------------------------------------------------

        [Test]
        public void TheBasicAttackIsTheProjectsOwnClipAndItsLengthIsWhatTheCodeSaysItIs()
        {
            AnimationClip male = Clip(MaleClip);
            AnimationClip female = Clip(FemaleClip);

            foreach (AnimationClip clip in new[] { male, female })
            {
                Assert.That(clip.length, Is.EqualTo(CombatFeedback.BasicAttackClipSeconds).Within(1e-3f),
                    clip.name + " is " + clip.length + " s; the presenter holds the swing for "
                    + CombatFeedback.BasicAttackClipSeconds);
                Assert.That(clip.humanMotion, Is.True, clip.name + " is not humanoid");
                Assert.That(clip.isLooping, Is.False, clip.name + " loops; a swing must not");
            }

            var over = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(
                "Assets/_Game/Prefabs/Prototype/Proto_Locomotion_Female.overrideController");
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);

            var mapped = false;

            foreach (KeyValuePair<AnimationClip, AnimationClip> pair in pairs)
            {
                if (pair.Key == male) { mapped = pair.Value == female; }
            }

            Assert.That(mapped, Is.True, "the female character would swing with the male clip");
        }

        [Test]
        public void TheClipIsTheApprovedCrossPunch_GuardCoilDriveLockOutRetract()
        {
            // Measured on BOTH production rigs, the way the impact frame was found. The clip
            // is the Mixamo "Cross Punch" (source frames 14-40 at 30 fps) retargeted onto each
            // Meshy rig in Blender, with the torso and head matched between the sexes, so the
            // female is held to exactly the same numbers as the male: GUARD (frame 0: a boxer's
            // stance bladed ~80 deg off the target, both fists up, the rear fist by the cheek)
            // -> COIL (6: the chest and hips turn a little further away, the pelvis sinks, the
            // rear fist and elbow stay chambered) -> DRIVE (12-17: hips and chest come round
            // ~90 deg to square, the rear knee straightens from ~110 to ~155 deg, the torso
            // pitches forward ~20 deg, the punching shoulder comes through 15 cm, the fist is
            // fastest three to four frames before it lands) -> LOCK-OUT (21: the fist at its
            // furthest, the arm within 25 deg of straight and held there for three frames, the
            // chest and hips square, the fist at chin height on the centre line) -> RETRACT
            // (22-26: the elbow folds and the fist comes back 13 cm along a HIGH path while the
            // chest unwinds; the animator's blend into the guard loop finishes the return).
            // The guard hand stays up the whole way; the lead toe is planted and the rear foot
            // pivots on its ball; the transform never moves.
            foreach (string[] rig in new[] { new[] { MaleClip, MaleModel, "male" }, new[] { FemaleClip, FemaleModel, "female" } })
            {
                AssertCrossPunchMechanics(Clip(rig[0]), rig[1], rig[2]);
            }
        }

        private static void AssertCrossPunchMechanics(AnimationClip clip, string modelPath, string who)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);

            try
            {
                instance.transform.position = new Vector3(0f, 1000f, 0f);

                var animator = instance.GetComponent<Animator>();
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform lead = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform leadToe = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                Transform rearToe = animator.GetBoneTransform(HumanBodyBones.RightToes);
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                Transform leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                Transform upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Transform rearKneeBone = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

                Assert.That(leadToe, Is.Not.Null, who + ": no lead toe bone");
                Assert.That(rearToe, Is.Not.Null, who + ": no rear toe bone");

                int frames = Mathf.RoundToInt(clip.length * clip.frameRate);

                Assert.That(frames, Is.EqualTo(26), who + ": not the 26-frame cross punch");

                const int load = 6;
                const int start = 12;
                const int contact = 21;
                const int follow = 22;

                var handAt = new Vector3[frames + 1];
                var leadAt = new Vector3[frames + 1];
                var hipsAt = new Vector3[frames + 1];
                var headAt = new Vector3[frames + 1];
                var leftAt = new Vector3[frames + 1];
                var rightAt = new Vector3[frames + 1];
                var leadToeAt = new Vector3[frames + 1];
                var rearToeAt = new Vector3[frames + 1];
                var shoulderZ = new float[frames + 1];
                var chestYaw = new float[frames + 1];
                var hipsYaw = new float[frames + 1];
                var torsoPitch = new float[frames + 1];     // hips->neck from vertical, forward positive
                var elbowBend = new float[frames + 1];      // degrees short of a straight arm
                var rearKnee = new float[frames + 1];       // interior angle, 180 = straight

                AnimationMode.StartAnimationMode();

                try
                {
                    for (var f = 0; f <= frames; f++)
                    {
                        AnimationMode.SampleAnimationClip(instance, clip, f / clip.frameRate);

                        Vector3 origin = instance.transform.position;
                        handAt[f] = hand.position - origin;
                        leadAt[f] = lead.position - origin;
                        hipsAt[f] = hips.position - origin;
                        headAt[f] = head.position - origin;
                        leftAt[f] = left.position - origin;
                        rightAt[f] = right.position - origin;
                        leadToeAt[f] = leadToe.position - origin;
                        rearToeAt[f] = rearToe.position - origin;
                        shoulderZ[f] = (upperArm.position - origin).z;
                        elbowBend[f] = 180f - Vector3.Angle(upperArm.position - elbow.position, hand.position - elbow.position);
                        rearKnee[f] = Vector3.Angle(rightHip.position - rearKneeBone.position, right.position - rearKneeBone.position);
                        chestYaw[f] = Vector3.SignedAngle(Vector3.forward,
                            Vector3.ProjectOnPlane(chest.forward, Vector3.up), Vector3.up);
                        // the pelvis heading from the hip-joint line, the way the chest is read
                        // from the shoulders (the Hips bone's own axes are not the body's)
                        hipsYaw[f] = Vector3.SignedAngle(Vector3.left,
                            Vector3.ProjectOnPlane(leftHip.position - rightHip.position, Vector3.up), Vector3.up);
                        Vector3 spine = neck.position - hips.position;
                        torsoPitch[f] = Mathf.Atan2(spine.z, spine.y) * Mathf.Rad2Deg;
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }

                float[] speed = new float[frames + 1];

                for (var f = 1; f <= frames; f++)
                {
                    speed[f] = Vector3.Distance(handAt[f], handAt[f - 1]) * clip.frameRate;
                }

                // 1. GUARD: both fists up at chin height, the lead fist out in front, a long
                // boxing stance with the lead foot forward and the hips over the feet, the
                // whole body bladed ~80 deg away from the target (the Mixamo boxer's guard,
                // which is also what the GuardIdle loop holds between punches)
                float shoulder = headAt[0].y - 0.08f;
                float feetCentre = (leftAt[0].z + rightAt[0].z) / 2f;

                Assert.That(handAt[0].y, Is.GreaterThan(shoulder - 0.08f), who + ": the rear fist hangs instead of guarding");
                Assert.That(leadAt[0].y, Is.GreaterThan(shoulder - 0.06f), who + ": the lead fist hangs instead of guarding");
                Assert.That(leadAt[0].z, Is.GreaterThan(hipsAt[0].z + 0.05f), who + ": the lead fist is not in front of the body");
                Assert.That(leftAt[0].z - rightAt[0].z, Is.InRange(0.20f, 0.36f), who + ": the stance is not a long fighting stance with the lead foot forward");
                Assert.That(Mathf.Abs(feetCentre), Is.LessThan(0.03f), who + ": the stance is not under the character's origin");
                Assert.That(hipsAt[0].z - feetCentre, Is.InRange(-0.03f, 0.06f), who + ": the hips are not over the feet (" + (hipsAt[0].z - feetCentre) + " m)");
                Assert.That(Mathf.Abs(chestYaw[0]), Is.InRange(70f, 95f), who + ": the guard is not bladed like a boxer's (" + chestYaw[0] + " deg)");
                Assert.That(Mathf.Abs(hipsYaw[0]), Is.InRange(70f, 95f), who + ": the hips are not bladed with the chest (" + hipsYaw[0] + " deg)");
                Assert.That(torsoPitch[0], Is.InRange(-10f, 2f), who + ": the guard torso is not upright (" + torsoPitch[0] + " deg; positive is forward)");

                // 2. COIL: the chest and hips turn a little further away, the pelvis sinks onto
                // the bent legs, the rear fist drifts back and stays folded at a near stop --
                // no big chamber, the cross is thrown from the guard itself
                float chamber = handAt[0].z - handAt[load].z;

                Assert.That(chamber, Is.GreaterThan(0.02f), who + ": the fist does not settle back for the coil");
                Assert.That(Mathf.Abs(chestYaw[load]), Is.GreaterThan(Mathf.Abs(chestYaw[0]) + 8f), who + ": the chest does not coil further away");
                Assert.That(Mathf.Abs(hipsYaw[load]), Is.GreaterThan(Mathf.Abs(hipsYaw[0]) + 5f), who + ": the hips do not coil with the chest");
                Assert.That(hipsAt[load].y, Is.LessThan(hipsAt[0].y - 0.01f), who + ": the pelvis does not sink into the coil");
                Assert.That(elbowBend[load], Is.GreaterThan(120f), who + ": the rear arm is not folded at the coil");
                Assert.That(rearKnee[load], Is.LessThan(125f), who + ": the rear knee is not bent at the coil (" + rearKnee[load] + " deg)");

                // the punch: the fist accelerates through the drive, is fastest three to four
                // frames before it lands and decelerates INTO the lock-out (a straight punch
                // arriving, not a swing passing through)
                float fastest = 0f;
                var fastestFrame = -1;

                for (var f = 1; f <= contact; f++)
                {
                    if (speed[f] > fastest) { fastest = speed[f]; fastestFrame = f; }
                }

                float coilPeak = 0f;

                for (var f = 1; f <= load; f++) coilPeak = Mathf.Max(coilPeak, speed[f]);

                Assert.That(fastestFrame, Is.InRange(contact - 5, contact - 2), who + ": the fist peaks on frame " + fastestFrame + ", not on the drive into contact");
                Assert.That(fastest, Is.GreaterThan(coilPeak * 3f), who + ": the punch is not clearly faster than the coil");
                Assert.That(speed[load], Is.LessThan(fastest * 0.3f), who + ": the fist never settles in the coil");
                Assert.That(speed[contact], Is.LessThan(fastest * 0.2f), who + ": the fist does not arrive and stop on the target");

                for (var f = start + 1; f <= fastestFrame; f++)
                {
                    Assert.That(speed[f], Is.GreaterThan(fastest * 0.5f), who + ": the punch stalls on frame " + f);
                }

                Assert.That(CombatFeedback.BasicAttackImpactSeconds, Is.EqualTo(contact / clip.frameRate).Within(1e-4f),
                    "the code's contact moment is not the clip's lock-out frame");

                // 3. DRIVE: the lower body leads -- at punch start the hips and chest have
                // already begun to come round while the fist is still beside the body
                Assert.That(Mathf.Abs(chestYaw[start]), Is.LessThan(Mathf.Abs(chestYaw[load]) - 25f), who + ": the chest has not started turning in at punch start");
                Assert.That(Mathf.Abs(hipsYaw[start]), Is.LessThan(Mathf.Abs(hipsYaw[load]) - 25f), who + ": the hips have not started turning in at punch start");
                Assert.That(handAt[start].z, Is.LessThan(handAt[0].z + 0.02f), who + ": the fist leads the punch instead of the body");

                // 4. LOCK-OUT: the fist at its furthest on the contact frame, the arm nearly
                // straight and held that way for three frames, the whole side driven through
                for (var f = 0; f <= frames; f++)
                {
                    Assert.That(handAt[contact].z, Is.GreaterThanOrEqualTo(handAt[f].z), who + ": the fist is further out on frame " + f + " than on the contact frame");
                }

                Assert.That(handAt[contact].z - handAt[0].z, Is.GreaterThan(0.28f), who + ": the punch barely leaves the guard");
                Assert.That(handAt[contact].z, Is.GreaterThan(headAt[contact].z + 0.1f), who + ": the fist is not ahead of the head");
                Assert.That(handAt[contact].z, Is.GreaterThan(leadAt[contact].z + 0.05f), who + ": the fist is not ahead of the guard hand");
                Assert.That(Mathf.Abs(handAt[contact].x), Is.LessThan(0.08f), who + ": the fist lands off the centre line");
                Assert.That(handAt[contact].y, Is.InRange(headAt[contact].y - 0.12f, headAt[contact].y + 0.02f), who + ": the punch is not at chin height (a body shot, or over the head)");

                for (var f = contact - 2; f <= contact; f++)
                {
                    Assert.That(elbowBend[f], Is.InRange(0f, 25f), who + ": the arm is not locked out on frame " + f + " (" + elbowBend[f] + " deg bent)");
                }

                Assert.That(elbowBend[follow], Is.InRange(0f, 30f), who + ": the arm folds before the follow-through is over");
                // the chest and hips come round INTO the punch: ~90 deg from the coil to square
                Assert.That(Mathf.Abs(chestYaw[load]) - Mathf.Abs(chestYaw[contact]), Is.GreaterThan(70f), who + ": the chest does not come round into the punch");
                Assert.That(Mathf.Abs(chestYaw[contact]), Is.LessThan(10f), who + ": the chest is not square at contact (" + chestYaw[contact] + " deg)");
                Assert.That(Mathf.Abs(hipsYaw[load]) - Mathf.Abs(hipsYaw[contact]), Is.GreaterThan(60f), who + ": the hips do not turn into the punch (" + hipsYaw[load] + " -> " + hipsYaw[contact] + ")");
                Assert.That(Mathf.Abs(hipsYaw[contact]), Is.LessThan(10f), who + ": the hips are not square at contact (" + hipsYaw[contact] + " deg)");
                // the punching shoulder is driven through, the torso pitches into the punch as
                // one chain (the whole hips->neck line, so a back arch cannot pass), the rear
                // leg straightens under the weight, the pelvis rides forward and up on it
                Assert.That(shoulderZ[contact] - shoulderZ[load], Is.GreaterThan(0.10f), who + ": the punching shoulder does not come through");
                Assert.That(torsoPitch[contact] - torsoPitch[0], Is.GreaterThan(12f), who + ": the torso does not pitch into the punch");
                Assert.That(torsoPitch[contact], Is.InRange(8f, 25f), who + ": the torso is arched back or thrown over at contact (" + torsoPitch[contact] + " deg)");
                Assert.That(rearKnee[contact], Is.GreaterThan(145f), who + ": the rear leg does not extend into the strike (" + rearKnee[contact] + " deg)");
                Assert.That(rearKnee[contact] - rearKnee[load], Is.GreaterThan(35f), who + ": the rear leg does not visibly go from coiled to driving");
                Assert.That(hipsAt[contact].z, Is.GreaterThan(hipsAt[0].z + 0.03f), who + ": the weight does not transfer forward");
                Assert.That(headAt[contact].z, Is.GreaterThan(headAt[0].z + 0.05f), who + ": the upper body does not come forward");
                Assert.That(hipsAt[contact].y - hipsAt[0].y, Is.InRange(-0.03f, 0.04f), who + ": the impact is a squat or a hop");

                // 5. FOLLOW-THROUGH: the fist and the body stay out for a frame after contact
                Assert.That(handAt[follow].z, Is.GreaterThanOrEqualTo(handAt[contact].z - 0.01f), who + ": the fist snaps back the instant it lands");
                Assert.That(headAt[follow].z, Is.GreaterThanOrEqualTo(headAt[contact].z - 0.005f), who + ": the body pulls back the instant the fist lands");

                // 6. RETRACT: the elbow folds and the fist comes home along a HIGHER path than
                // it went out (never reversed playback), fast but below the punch's own peak,
                // the chest unwinding with it; the clip ends part-way back and the animator's
                // blend into the guard loop covers the last stretch
                float retractPeak = 0f;
                float retractLow = float.PositiveInfinity;
                float outLow = float.PositiveInfinity;

                for (var f = follow + 1; f <= frames; f++) retractPeak = Mathf.Max(retractPeak, speed[f]);
                for (var f = contact + 1; f <= frames; f++) retractLow = Mathf.Min(retractLow, handAt[f].y);
                for (var f = 1; f <= contact; f++) outLow = Mathf.Min(outLow, handAt[f].y);

                Assert.That(retractPeak, Is.LessThan(fastest * 1.2f), who + ": the fist snaps back faster than it was thrown");
                Assert.That(retractPeak, Is.GreaterThan(fastest * 0.3f), who + ": the retract is sluggish");
                Assert.That(handAt[contact].z - handAt[frames].z, Is.GreaterThan(0.10f), who + ": the fist has not started home by the end of the clip");
                Assert.That(elbowBend[frames], Is.GreaterThan(100f), who + ": the elbow has not folded by the end of the clip");
                Assert.That(retractLow, Is.GreaterThan(outLow + 0.03f), who + ": the fist retracts back down the path it went out (reversed playback)");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(chestYaw[contact], chestYaw[frames])), Is.GreaterThan(20f), who + ": the torso does not unwind on the retract");
                Assert.That(Vector3.Distance(handAt[frames], handAt[0]), Is.LessThan(0.25f), who + ": the clip ends too far from the guard for the blend to hide");

                // the guard hand stays a guard the whole way: up, near where it started, never
                // mirroring the punch or flying out; the punching fist never goes wide or high
                for (var f = 0; f <= frames; f++)
                {
                    Assert.That(leadAt[f].y, Is.GreaterThan(shoulder - 0.08f), who + ": the guard hand drops on frame " + f);
                    Assert.That(Vector3.Distance(leadAt[f], leadAt[0]), Is.LessThan(0.12f), who + ": the guard hand wanders on frame " + f);
                    Assert.That(Mathf.Abs(handAt[f].x), Is.LessThan(0.2f), who + ": the arm swings wide on frame " + f);
                    Assert.That(handAt[f].y, Is.LessThan(headAt[f].y + 0.05f), who + ": the fist goes over the head on frame " + f);
                    Assert.That(Mathf.Abs(chestYaw[f]), Is.LessThan(100f), who + ": the torso turns " + chestYaw[f] + " degrees on frame " + f);
                    Assert.That(torsoPitch[f], Is.InRange(-12f, 25f), who + ": the torso arches or dives on frame " + f + " (" + torsoPitch[f] + " deg)");
                }

                // feet: the lead toe is planted (the heel may rock a little), the rear foot
                // pivots on its ball -- the toe within a few cm of its spot, the heel swinging
                // out and up under a hand's width -- never a step, never a hop; the body's
                // travel is the drive over the planted lead foot, no more
                for (var f = 0; f <= frames; f++)
                {
                    Assert.That(Vector3.Distance(leadToeAt[f], leadToeAt[0]), Is.LessThan(0.015f), who + ": the lead toe leaves its spot on frame " + f);
                    Assert.That(Vector3.Distance(leftAt[f], leftAt[0]), Is.LessThan(0.03f), who + ": the lead foot steps on frame " + f);
                    Vector2 rearToeFlat = new Vector2(rearToeAt[f].x - rearToeAt[0].x, rearToeAt[f].z - rearToeAt[0].z);
                    Vector2 rearFlat = new Vector2(rightAt[f].x - rightAt[0].x, rightAt[f].z - rightAt[0].z);
                    Assert.That(rearToeFlat.magnitude, Is.LessThan(0.05f), who + ": the rear toe slides on frame " + f + " (a pivot on the ball is allowed, a step is not)");
                    Assert.That(rearToeAt[f].y - rearToeAt[0].y, Is.InRange(-0.005f, 0.02f), who + ": the rear toe lifts or sinks on frame " + f);
                    Assert.That(rearFlat.magnitude, Is.LessThan(0.10f), who + ": the rear heel swings further than a foot's length on frame " + f);
                    Assert.That(rightAt[f].y - rightAt[0].y, Is.InRange(-0.005f, 0.05f), who + ": the rear foot hops or sinks on frame " + f);
                    Assert.That(Vector2.Distance(new Vector2(hipsAt[f].x, hipsAt[f].z), new Vector2(hipsAt[0].x, hipsAt[0].z)),
                        Is.LessThan(0.10f), who + ": the body travels further than the drive over the lead foot on frame " + f);
                    Assert.That(Mathf.Abs(hipsAt[f].y - hipsAt[0].y), Is.LessThan(0.05f), who + ": the body hops or squats on frame " + f);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheActiveBasicAttackIsTheCrossPunchClip_AndNothingElseIsWiredIn()
        {
            // The animator's one attack state plays the production punch imported from the
            // approved Blender retarget of the Mixamo Cross Punch: 26 frames at 30 fps,
            // humanoid, no root motion, the lock-out at frame 21. The rejected placeholder
            // punches (the sword-clip Attack01, the Kevin Iglesias combat set, the raw Mixamo
            // clip kept in Shared/ as source material, the retired hand-authored v12 punch)
            // are not referenced by the attack state or the female override.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Controller);
            UnityEditor.Animations.AnimatorState punch = null;

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name == "BasicPunch") punch = child.state;
                Assert.That(child.state.motion == null || !child.state.motion.name.Contains("Attack01"), Is.True,
                    child.state.name + " still plays the rejected Attack01 clip");
            }

            Assert.That(punch, Is.Not.Null, "no BasicPunch state");
            Assert.That(AssetDatabase.GetAssetPath(punch.motion), Is.EqualTo(MaleClip), "the attack state does not play the production punch FBX");
            Assert.That(punch.motion.name, Is.EqualTo("MeshyCrossPunch_M"));

            var clip = (AnimationClip)punch.motion;
            var importer = (ModelImporter)AssetImporter.GetAtPath(MaleClip);
            ModelImporterClipAnimation settings = importer.clipAnimations.Length > 0 ? importer.clipAnimations[0] : importer.defaultClipAnimations[0];

            Assert.That(clip.frameRate, Is.EqualTo(30f));
            Assert.That(Mathf.RoundToInt(clip.length * clip.frameRate), Is.EqualTo(26), "not the 26-frame cross punch");
            Assert.That(clip.humanMotion, Is.True, "not imported as humanoid");
            Assert.That(clip.isLooping, Is.False, "a punch must not loop");
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            Assert.That(settings.loopTime, Is.False);
            Assert.That(settings.loopPose, Is.False);
            Assert.That(settings.mirror, Is.False);
            Assert.That(settings.cycleOffset, Is.EqualTo(0f));
            Assert.That(settings.lockRootPositionXZ && settings.lockRootHeightY && settings.lockRootRotation, Is.True,
                "root motion is not baked into the pose: the punch would carry the network body");

            var over = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(
                "Assets/_Game/Prefabs/Prototype/Proto_Locomotion_Female.overrideController");
            var pairs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);
            var female = false;

            foreach (System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip> pair in pairs)
            {
                if (pair.Key != null && pair.Key.name == "MeshyCrossPunch_M")
                {
                    female = pair.Value != null && AssetDatabase.GetAssetPath(pair.Value) == FemaleClip;
                }
            }

            Assert.That(female, Is.True, "the female character does not punch with her own cross punch");

            // nothing retired is still wired in, not even as a stale override pair
            string overrideText = AttackSpeedFixture.Source("Assets/_Game/Prefabs/Prototype/Proto_Locomotion_Female.overrideController");
            string controllerText = AttackSpeedFixture.Source(Controller);

            foreach (string retired in new[]
            {
                "Assets/_Game/Art/Characters/Production/Shared/CrossPunch.fbx",
                "Assets/_Game/Art/Characters/Production/Shared/HookPunch.fbx",
                "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy@BasicPunch.fbx",
                "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy@BasicPunch.fbx",
            })
            {
                string guid = AssetDatabase.AssetPathToGUID(retired);

                if (string.IsNullOrEmpty(guid)) continue;

                Assert.That(controllerText, Does.Not.Contain(guid), "the controller still references " + retired);
                Assert.That(overrideText, Does.Not.Contain(guid), "the female override still references " + retired);
            }
        }

        [Test]
        public void ASwingArrivingBeforeContactNeverRestartsTheDrawnPunch_OneAfterContactStartsTheNext()
        {
            // The server's pacing keeps one accepted swing per cycle, so this is the guard for
            // the case pacing cannot cover (a late packet, a later skill): a swing that lands
            // while the drawn punch has not reached its contact frame is absorbed -- counted,
            // its number still held to contact, but the animator is not touched, so the
            // wind-up and the blow are never cut short. Once the fist has landed, the next
            // swing starts the punch from its first frame. Never a restart every frame.
            string presenter = AttackSpeedFixture.Source(Presenter);

            Assert.That(presenter, Does.Contain("bool beforeImpact = _swinging && Time.time < _swingImpactAt;"),
                "the presenter does not know whether the drawn punch has landed");
            Assert.That(presenter, Does.Contain("if (beforeImpact) return;"), "a swing before contact restarts the punch");
            Assert.That(presenter.IndexOf("if (beforeImpact) return;"), Is.LessThan(presenter.IndexOf("_animator.SetTrigger(AttackHash);")),
                "the trigger is set before the guard, so the punch would retrigger");
            Assert.That(presenter.IndexOf("if (beforeImpact) SwingsAbsorbed++;"), Is.GreaterThan(0), "absorbed swings are not counted");
            Assert.That(presenter, Does.Contain("_swingImpactAt = Time.time + CombatFeedback.BasicAttackImpactSeconds / PlaybackRate;"),
                "contact is not timed at the rate the clip plays");
            Assert.That(presenter, Does.Contain("_animator.Play(current.fullPathHash, 0, 0f);"),
                "a swing after contact does not start the next punch from its first frame");
            Assert.That(presenter, Does.Not.Contain("Animator.speed"), "the whole animator is scaled");
        }

        [Test]
        public void TheAttackTransitionsAreShortEnoughToKeepTheChamberAndTheImpact()
        {
            // A 0.2 s cross-fade into a 0.77 s punch eats the first six frames -- the guard
            // and most of the wind-up. The entries are quick and deliberate, the exit into the
            // guard short, and nothing is a zero-length snap.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Controller);
            UnityEditor.Animations.AnimatorState punch = null, guard = null, locomotion = null;

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name == "BasicPunch") punch = child.state;
                if (child.state.name == "Guard") guard = child.state;
                if (child.state.name == "Locomotion") locomotion = child.state;
            }

            foreach (UnityEditor.Animations.AnimatorStateTransition t in locomotion.transitions)
            {
                if (t.destinationState == punch)
                {
                    Assert.That(t.hasFixedDuration, Is.True);
                    Assert.That(t.duration, Is.InRange(0.03f, 0.1f), "locomotion -> punch blends away the wind-up, or snaps");
                    Assert.That(t.hasExitTime, Is.False, "the punch waits for the walk cycle");
                }
            }

            foreach (UnityEditor.Animations.AnimatorStateTransition t in guard.transitions)
            {
                if (t.destinationState == punch) Assert.That(t.duration, Is.InRange(0.03f, 0.08f), "guard -> punch is not immediate");
            }

            foreach (UnityEditor.Animations.AnimatorStateTransition t in punch.transitions)
            {
                if (t.destinationState == guard) Assert.That(t.duration, Is.InRange(0.05f, 0.12f), "punch -> guard blends the recovery away or pops");
                if (t.destinationState == locomotion) Assert.That(t.duration, Is.InRange(0.08f, 0.2f), "walking off a punch pops or drags");
                Assert.That(t.hasExitTime, Is.False, "the punch state leaves on the presenter's phase, not on its own");
            }
        }

        [Test]
        public void TheHandsCloseIntoFistsForTheFight_AndOpenAgainAfterIt()
        {
            // The rigs have no finger bones, so a fist is a blend shape on the production
            // meshes -- both of them -- that the presenter closes while a swing or the guard
            // is being shown and opens again when the fight is over. The locomotion clips
            // are untouched: they never mention the hands.
            foreach (string path in new[] { MaleModel, "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy.fbx" })
            {
                Mesh mesh = null;

                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o is Mesh m) mesh = m;
                }

                Assert.That(mesh, Is.Not.Null, path + " has no mesh");
                Assert.That(mesh.GetBlendShapeIndex(CharacterVisualPresenter.FistBlendShape), Is.GreaterThanOrEqualTo(0),
                    path + " has no '" + CharacterVisualPresenter.FistBlendShape + "' blend shape, so the character would punch with an open hand");
            }

            // closed within FistSeconds of the fight starting, open again the same time after
            float weight = 0f;
            float step = CharacterVisualPresenter.FistSeconds / 6f;

            for (var i = 0; i < 6; i++) weight = CharacterVisualPresenter.NextFistWeight(weight, true, step);

            Assert.That(weight, Is.EqualTo(100f).Within(1e-3f), "the fists are not closed by the time the wind-up is under way");

            weight = CharacterVisualPresenter.NextFistWeight(weight, true, 1f);

            Assert.That(weight, Is.EqualTo(100f), "the weight runs past a closed fist");

            for (var i = 0; i < 6; i++) weight = CharacterVisualPresenter.NextFistWeight(weight, false, step);

            Assert.That(weight, Is.EqualTo(0f).Within(1e-3f), "the hands do not open again after the fight");
            Assert.That(CharacterVisualPresenter.NextFistWeight(50f, false, 0f), Is.EqualTo(50f), "a zero-length tick moves the hands");
            Assert.That(CharacterVisualPresenter.FistSeconds, Is.LessThan(CombatFeedback.BasicAttackImpactSeconds / 2f),
                "the fists close later than the punch lands");

            string presenter = AttackSpeedFixture.Source(Presenter);

            Assert.That(presenter, Does.Contain("SettleFists(deltaSeconds)"), "the presenter never drives the fists");
            Assert.That(presenter, Does.Contain("(_swinging || _guarding) && !_presentedDead"), "the fists are not tied to the swing and the guard");
            Assert.That(presenter, Does.Contain("GetBlendShapeIndex(FistBlendShape)"), "the fist blend shape is not looked up on the model");
            Assert.That(presenter, Does.Contain("SetBlendShapeWeight(_fistShape[i], _fistWeight)"), "the weight never reaches the mesh");
        }

        [Test]
        public void TheGuardIsHeldBetweenPunches_InTheControllerAndInThePresenter()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(Controller);
            UnityEditor.Animations.AnimatorState guard = null, punch = null, locomotion = null;

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name == "Guard") guard = child.state;
                if (child.state.name == "BasicPunch") punch = child.state;
                if (child.state.name == "Locomotion") locomotion = child.state;
            }

            Assert.That(guard, Is.Not.Null, "no guard state: the character drops its hands between punches");
            Assert.That(guard.motion, Is.Not.Null.And.Property("name").EqualTo("MeshyGuardIdle_M"));
            Assert.That(((AnimationClip)guard.motion).isLooping, Is.True, "the guard does not loop");
            Assert.That(guard.speedParameterActive, Is.False, "the guard is scaled by attack speed; only the punch is");

            var punchToGuard = false;
            var guardToPunch = false;
            var guardToLocomotion = false;

            foreach (UnityEditor.Animations.AnimatorStateTransition t in punch.transitions)
            {
                if (t.destinationState == guard) punchToGuard = Has(t, "AttackPhase", 2);
            }

            foreach (UnityEditor.Animations.AnimatorStateTransition t in guard.transitions)
            {
                if (t.destinationState == punch) guardToPunch = Has(t, "AttackPhase", 1);
                if (t.destinationState == locomotion) guardToLocomotion = Has(t, "AttackPhase", 0);
            }

            Assert.That(punchToGuard, Is.True, "the punch does not end in the guard (phase 2)");
            Assert.That(guardToPunch, Is.True, "the next punch cannot be thrown from the guard (phase 1)");
            Assert.That(guardToLocomotion, Is.True, "the guard never drops back to locomotion (phase 0)");

            string presenter = AttackSpeedFixture.Source(Presenter);

            Assert.That(presenter, Does.Contain("SetInteger(AttackPhaseHash, 2)"), "the presenter never raises the guard");
            Assert.That(presenter, Does.Contain("GuardHoldSeconds"), "the guard is not held for a beat after the punch");
            Assert.That(presenter, Does.Contain("SettleGuard(speed)"), "walking off does not drop the guard");
            Assert.That(CharacterVisualPresenter.GuardHoldSeconds, Is.InRange(1.5f, 2.5f),
                "after a kill the fighter should hold the combat idle for a beat and then ease back to the normal idle");

            // the female fights with her own guard and punch
            var over = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(
                "Assets/_Game/Prefabs/Prototype/Proto_Locomotion_Female.overrideController");
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            over.GetOverrides(pairs);

            var guardMapped = false;

            foreach (KeyValuePair<AnimationClip, AnimationClip> pair in pairs)
            {
                if (pair.Key == guard.motion) guardMapped = pair.Value == Clip(FemaleGuard);
            }

            Assert.That(guardMapped, Is.True, "the female character would guard with the male clip");
        }

        private static bool Has(UnityEditor.Animations.AnimatorStateTransition t, string parameter, int value)
        {
            foreach (UnityEditor.Animations.AnimatorCondition c in t.conditions)
            {
                if (c.parameter == parameter && c.mode == UnityEditor.Animations.AnimatorConditionMode.Equals
                    && Mathf.RoundToInt(c.threshold) == value)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void ThePlayerStandsWhereTheFistReaches_NotAtSwordsLength()
        {
            // The approach distance is measured, not typed: the punch's contact frame puts
            // the fist a known distance in front of the character's origin, the slime is
            // drawn at a known radius, and the character should stop where the fist ends up
            // within a hand's breadth of the slime -- close enough to read as a hit, not so
            // close that the two bodies overlap.
            AnimationClip clip = Clip(MaleClip);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MaleModel);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            float fistReach;

            try
            {
                instance.transform.position = new Vector3(0f, 1000f, 0f);
                var animator = instance.GetComponent<Animator>();
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);

                AnimationMode.StartAnimationMode();

                try
                {
                    AnimationMode.SampleAnimationClip(instance, clip, CombatFeedback.BasicAttackImpactSeconds);
                    fistReach = (hand.position - instance.transform.position).z + 0.04f;   // wrist plus a hand
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            string pointer = AttackSpeedFixture.Source(Pointer);
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(pointer,
                @"_attackMetres\s*=\s*([0-9.]+)f");

            Assert.That(m.Success, Is.True, "the pointer has no attack approach distance");

            float stand = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            const float slimeRadius = 0.361f * 0.75f / 2f;
            const float playerHalfWidth = 0.12f;

            float gapAtImpact = stand - slimeRadius - fistReach;

            Assert.That(stand, Is.LessThan(1.0f), "the character still punches from " + stand + " m");
            Assert.That(gapAtImpact, Is.GreaterThanOrEqualTo(-0.02f), "the fist would pass through the slime");
            Assert.That(gapAtImpact, Is.LessThan(0.15f), "the fist stops " + gapAtImpact + " m short of the slime");
            Assert.That(stand - slimeRadius - playerHalfWidth, Is.GreaterThan(0.1f), "the bodies overlap");

            // and the server's reach covers that stance with room for a step short, no more
            string prefab = AttackSpeedFixture.Source("Assets/_Game/Prefabs/Network/World_NetworkManager.prefab");
            System.Text.RegularExpressions.Match r = System.Text.RegularExpressions.Regex.Match(prefab,
                @"_meleeReachMetres:\s*([0-9.]+)");

            Assert.That(r.Success, Is.True);

            float reach = float.Parse(r.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            Assert.That(reach, Is.GreaterThan(stand + 0.2f), "arriving a step short would be refused");
            Assert.That(reach, Is.LessThanOrEqualTo(1.0f), "the server still lets a fist land from " + reach + " m");
        }

        // ---- D. particles ---------------------------------------------------------------

        [Test]
        public void RepeatedHitsRaiseNoParticleErrors_AndPooledSystemsAreBuiltAsleep()
        {
            string feedbackSource = AttackSpeedFixture.Source(Feedback);

            int sleep = feedbackSource.IndexOf("host.SetActive(false);", System.StringComparison.Ordinal);
            int add = feedbackSource.IndexOf("host.AddComponent<ParticleSystem>()", System.StringComparison.Ordinal);

            Assert.That(sleep, Is.GreaterThan(0).And.LessThan(add),
                "the spark host is live when the system is added, so it plays before its duration is set");

            var host = new GameObject("feedback");

            try
            {
                var feedback = host.AddComponent<CombatFeedback>();
                feedback.Compose(null);

                for (var i = 0; i < 40; i++)
                {
                    feedback.ShowImpact(new Vector3(i, 0f, 0f), i % 2 == 0 ? ImpactKind.Physical : ImpactKind.Soft);
                    feedback.Tick(0.05f);
                }

                Assert.That(feedback.ImpactsPlayed, Is.EqualTo(40));

                // the pool never grows and, once a system has settled, it is asleep again
                for (var i = 0; i < 20; i++) feedback.Tick(0.1f);

                var systems = host.GetComponentsInChildren<ParticleSystem>(true);

                Assert.That(systems.Length, Is.EqualTo(8), "the impact pool grew");

                foreach (ParticleSystem system in systems)
                {
                    Assert.That(system.gameObject.activeSelf, Is.False, "a settled spark is still awake");
                    Assert.That(system.main.playOnAwake, Is.False, "a spark plays on awake, before it is configured");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- what this gate must not have touched ----------------------------------------

        [Test]
        public void TheMovementFilesNeverLearnedAboutAttackSpeed()
        {
            foreach (string locked in Directory.GetFiles("Assets/_Game/Scripts", "CharacterMovement*.cs",
                SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(locked);

                foreach (string token in new[] { "AttackSpeed", "AttackRequestPacer", "Aspd", "CombatFeedback" })
                {
                    Assert.That(source, Does.Not.Contain(token), locked + " reaches into combat");
                }
            }

            string camera = AttackSpeedFixture.Source("Assets/_Game/Scripts/Client/World/WorldCameraDirector.cs");

            Assert.That(camera, Does.Not.Contain("AttackSpeed"));

            // the pointer changed in exactly one place: what it calls when a monster is in reach
            string pointer = AttackSpeedFixture.Source(Pointer);

            Assert.That(pointer, Does.Not.Contain("AttackSpeed"),
                "the pointer computes cadence itself instead of asking the combat input");
            Assert.That(pointer, Does.Contain("_movement.Intent = Vector2.zero;"),
                "the arrive-and-stop that click-to-move relies on is gone");
        }
    }

    // =====================================================================================
    // C. the slime's bite
    // =====================================================================================

    [TestFixture]
    internal sealed class TrainingSlimeMeleeRangeTests : MonsterTestBase
    {
        private const string Slime = "monster.training_slime";

        private static MonsterDefinition ProductionSlime()
        {
            WorldContentCatalogue content = AttackSpeedFixture.Content();

            Assert.That(content.BuildMonsters().TryGet(new DefinitionId(Slime), out MonsterDefinition slime),
                Is.True, "no training slime in the production catalogue");

            return slime;
        }

        [Test]
        public void TheSlimeCommitsFromDataAndFromMuchCloserThanItUsedTo()
        {
            MonsterDefinition slime = ProductionSlime();

            Assert.That(slime.AttackStartRange, Is.LessThan(0.75f),
                "it still begins its wind-up from " + slime.AttackStartRange + " m");
            Assert.That(slime.AttackStartRange, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slime.AttackRange, Is.EqualTo(0.85f).Within(1e-4f),
                "the impact validation range is not what the jump was measured against");
            Assert.That(slime.AttackStartRange, Is.LessThan(slime.AttackRange),
                "the lunge has nothing to cover");

            // Neither range is in code: the AI reads the definition, the authority reads the
            // definition, and the only place either number appears is the asset.
            string ai = AttackSpeedFixture.Source("Assets/_Game/Scripts/Gameplay/MonsterAiController.cs");
            string authority = AttackSpeedFixture.Source("Assets/_Game/Scripts/Server/MonsterAttackAuthority.cs");

            Assert.That(ai, Does.Contain("definition.AttackStartRange"), "the AI commits on the impact range");
            Assert.That(authority, Does.Contain("Definition.AttackRange"), "the blow is validated on the start range");
            Assert.That(ai + authority, Does.Not.Contain("training_slime"));
            Assert.That(ai + authority, Does.Not.Match(@"0\.5f|0\.85f"));
        }

        [Test]
        public void TheGapAtWindupIsSmallerThanTheJumpThatCoversIt()
        {
            // The visual lunge: the slime's body bone reaches 0.60 m forward in the model at
            // contact, scaled 0.875 by the animator and 0.75 by the presentation. The server
            // start range minus roughly one body radius of each party is the air between
            // them when the wind-up starts; the jump has to cover it, and does.
            const float lungeMetres = 0.60f * 0.875f * 0.75f;
            const float slimeRadius = 0.361f * 0.75f / 2f;
            const float playerRadius = 0.17f;

            MonsterDefinition slime = ProductionSlime();

            float gap = slime.AttackStartRange - slimeRadius - playerRadius;

            Assert.That(gap, Is.GreaterThan(0f), "it would have to overlap the player before committing");
            Assert.That(gap, Is.LessThan(lungeMetres), "the jump does not reach the player");
            Assert.That(slime.AttackRange, Is.LessThanOrEqualTo(slime.AttackStartRange + lungeMetres),
                "the blow can land from farther than the jump carries it");
        }

        [Test]
        public void ItChasesInsideTheOldRangeAndCommitsOnlyInsideTheNewOne()
        {
            MonsterDefinition definition = AddMonster("monster.biter",
                aggression: MonsterAggressionType.Aggressive, detection: 30f,
                attackRange: 0.85f, cooldown: 2.5f, leash: 100f);

            SetPrivate(definition, "_attackStartRange", 0.5f);
            SetPrivate(definition, "_attackWindupSeconds", 0.8f);
            SetPrivate(definition, "_moveSpeed", 0f);   // held still so the distance is the test's

            Assert.That(definition.TryGetStat(new DefinitionId(MaxHp), out int health), Is.True);

            var state = new MonsterRuntimeState(new InstanceId("m:biter"), definition,
                new CombatPosition(0f, 0f, 0f), health, Enemies);
            var ai = new MonsterAiController(state);

            // 0.7 m away: inside the range it used to bite from, outside the one it bites from now.
            FakeCombatant player = Player(0.7f, 0f, 0f);
            var candidates = new ICombatant[] { player };

            for (var i = 0; i < 40; i++) ai.Tick(0.05f, candidates);

            Assert.That(ai.IsCommittedToASwing, Is.False, "it committed from 0.7 m");
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Chase), "it is not closing in");

            // Close enough now.
            player.Position = new CombatPosition(0.45f, 0f, 0f);

            for (var i = 0; i < 12 && !ai.IsCommittedToASwing; i++) ai.Tick(0.05f, candidates);

            Assert.That(ai.IsCommittedToASwing, Is.True, "it never committed from 0.45 m");
            Assert.That(ai.State, Is.EqualTo(MonsterAiState.Attack));
        }

        [Test]
        public void ADefinitionThatCommitsFromBeyondItsReachIsRefusedByValidation()
        {
            MonsterDefinition bad = AddMonster("monster.hopeful", aggression: MonsterAggressionType.Aggressive,
                detection: 10f, attackRange: 0.5f);

            SetPrivate(bad, "_attackStartRange", 0.9f);

            var report = new ValidationReport();
            var lookup = new CompositeDefinitionLookup();

            new WorldContentValidationRule().Validate(bad, lookup, report);

            Assert.That(report.ErrorCount, Is.GreaterThan(0), "a swing that can only miss passed validation");
        }

        [Test]
        public void LeavingTheImpactRangeDuringTheWindupIsAMiss()
        {
            MonsterDefinition slime = ProductionSlime();

            FakeCombatant biter = new FakeCombatant("m:slime", Enemies.Value, 50, 50)
                .WithStat(CombatTestIds.AttackPower, 10);
            FakeCombatant player = new FakeCombatant("p:one", Players.Value, 100, 100)
                .WithStat(CombatTestIds.Defense, 0);

            biter.Position = new CombatPosition(0f, 0f, 0f);
            player.Position = new CombatPosition(slime.AttackStartRange - 0.05f, 0f, 0f);

            // The authority rebuilds the rules from the definition's impact range per swing.
            BasicAttackRules impact = BasicAttackRules.Melee(CombatTestIds.AttackPowerStat,
                CombatTestIds.DefenseStat, 1, slime.AttackRange);

            AttackResult landed = BasicAttackExecutor.Execute(new AttackIntent(biter, player), impact);

            Assert.That(landed.IsHit, Is.True, "standing where it committed, the bite lands");
            Assert.That(player.CurrentHealth, Is.EqualTo(90));

            // A step back during the 0.8 s wind-up -- less than a quarter of a second of walking.
            player.Position = new CombatPosition(slime.AttackRange + 0.2f, 0f, 0f);

            AttackResult missed = BasicAttackExecutor.Execute(new AttackIntent(biter, player), impact);

            Assert.That(missed.IsHit, Is.False);
            Assert.That(missed.Reason, Is.EqualTo(AttackRejection.OutOfRange));
            Assert.That(player.CurrentHealth, Is.EqualTo(90), "phantom damage");
        }

        [Test]
        public void TheRestOfTheSlimesTuningIsUntouched()
        {
            MonsterDefinition slime = ProductionSlime();

            Assert.That(slime.AttackCooldownSeconds, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(slime.AttackWindupSeconds, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(slime.AttackRecoverySeconds, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slime.MoveSpeed, Is.EqualTo(0.95f).Within(1e-4f));
            Assert.That(slime.DetectionRange, Is.EqualTo(6f).Within(1e-4f));
        }
    }
}
