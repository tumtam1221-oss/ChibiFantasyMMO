using System.Collections.Generic;
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
    /// The world's one tick, its order, and the spawn that used to kill people.
    /// </summary>
    /// <remarks>
    /// <b>What 18.8 left undone.</b> The stat authority was correct and tested, and nothing
    /// ran it: a shipped server composed sessions and never composed a world. These tests are
    /// about the composition rather than the arithmetic -- that a tick exists, that it runs
    /// things in an order that cannot show a player a stale number, and that entering the
    /// world cannot destroy the resources a character was loaded with.
    ///
    /// <b>Nothing here calls RefreshAll.</b> If a test had to kick the stat authority by hand
    /// to make a buff work, so would the game.
    /// </remarks>
    [TestFixture]
    internal sealed class WorldSimulationCompositionTests : CharacterCreationTestBase
    {
        private const string Atk = "stat.atk";
        private const string HomeMap = "map.home";
        private const string Might = "status.might";     // +10 flat attack
        private const string Sword = "item.sword";       // +25 flat attack

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private DefinitionRegistry<ItemDefinition> _items;
        private DefinitionRegistry<StatusEffectDefinition> _effects;
        private CharacterStatAuthority _statAuthority;
        private CharacterStatusAuthority _statusAuthority;
        private WorldSimulation _world;

        [SetUp]
        public void SetUpWorld()
        {
            // FIXTURE: attack is strength, so one modifier is visible in one step.
            AddStat(Atk, false);
            Formulas.Add(Formula("f.atk", Atk, 0, new StatTerm(new DefinitionId(Str), 1, 1)));

            _effects = new DefinitionRegistry<StatusEffectDefinition>();
            _effects.Register(Effect(Might, Flat(Atk, 10f)));

            _items = new DefinitionRegistry<ItemDefinition>();
            _items.Register(Weapon(Sword, Flat(Atk, 25f)));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns, _items, 8);

            _statusAuthority = new CharacterStatusAuthority(_players, _effects);

            _statAuthority = new CharacterStatAuthority(_players, Formulas, Stats, _effects,
                new EquipmentModifierResolver.Context(_items),
                new DefinitionId(MaxHp), new DefinitionId(MaxMp));

            _world = new WorldSimulation(_players, null, _statusAuthority, _statAuthority);
        }

        // ---- there is one loop, and it is composed where the server is -------------------------

        [Test]
        public void TheServerBootstrapOwnsTheOnlyWorldTick()
        {
            string bootstrap = Code(
                "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs");

            Assert.That(bootstrap, Does.Contain("Simulation.Tick(Time.deltaTime)"),
                "the composition root drives the world");
            Assert.That(bootstrap, Does.Contain("public void UseWorld("));

            // And exactly one thing in the project ticks a world.
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var simulations = 0;
            var drivers = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class WorldSimulation")) simulations++;
                if (Code(file).Contains("Simulation.Tick(")) drivers++;
            }

            Assert.That(simulations, Is.EqualTo(1),
                "a second world lifecycle would run every timer twice");
            Assert.That(drivers, Is.EqualTo(1),
                "two loops would expire a buff in half its authored duration");
        }

        [Test]
        public void TheSimulationDecidesNothingAndOwnsNoFormula()
        {
            string world = Code("Assets/_Game/Scripts/Server/WorldSimulation.cs");

            Assert.That(world, Does.Not.Contain("DerivedStatsCalculator"),
                "the calculator stays canonical and is reached only through the authority");
            Assert.That(world, Does.Not.Contain("StatModifier"));
            Assert.That(world, Does.Not.Contain("StatusEffectService"),
                "applying a status is not the lifecycle's business");
            Assert.That(world, Does.Not.Contain("BasicDamageFormula"));
        }

        [Test]
        public void NoClientCodeDrivesTheWorldOrItsStats()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string named = file.Replace("\\", "/");

                if (named.Contains("/Prototype/")) continue;

                string source = Code(file);

                Assert.That(source, Does.Not.Contain("WorldSimulation"), named);
                Assert.That(source, Does.Not.Contain("RefreshAll"), named);
                Assert.That(source, Does.Not.Contain("CharacterStatAuthority"), named);
            }
        }

        // ---- entering the world -------------------------------------------------------------------

        [Test]
        public void AdmittingACharacterComputesItsStatsBeforeAnythingElseSeesIt()
        {
            LivingCharacter character = Admit();

            // Swordsman fixture: STR 10, VIT 8. Attack = 10, MaxHP = 50 + 80 = 130.
            Assert.That(Attack(character), Is.EqualTo(10),
                "a character whose stats were never computed reads its raw base attributes");
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(130));
            Assert.That(character.Combatant.Limits.MaxMana, Is.EqualTo(30));
            Assert.That(_statAuthority.Recomputations, Is.EqualTo(1));
        }

        [Test]
        public void APersistedResourceIsNeverClampedAgainstACeilingNobodyHasComputedYet()
        {
            // Loaded wounded: 75 of an eventual 130.
            LivingCharacter character = Admit(health: 75, mana: 12);

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(75),
                "entering the world must not cost a player the health they logged out with");
            Assert.That(character.Domain.Resources.CurrentMana, Is.EqualTo(12));
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(130));

            // The invariant, stated outright.
            Assert.That(character.Combatant.CurrentHealth,
                Is.InRange(0, character.Combatant.MaxHealth));
        }

        [Test]
        public void AnUnknownCeilingIsNotAZeroCeiling()
        {
            // The trap in its purest form: hand a live combatant the empty limits a
            // combatant is constructed with, and see whether it kills them.
            LivingCharacter character = Admit(health: 75);

            character.Combatant.SetLimits(ResourceLimits.None);

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(75),
                "all-zero limits mean 'not computed yet', not 'this character may have no "
                + "health' -- clamping against them puts a loaded player into the world dead");

            Assert.That(ResourceLimits.None.IsSpecified, Is.False);
            Assert.That(new ResourceLimits(130, 30).IsSpecified, Is.True);

            // A real ceiling still clamps, which is the half that must keep working.
            character.Combatant.SetLimits(new ResourceLimits(50, 10));

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(50));
        }

        [Test]
        public void PersistedEquipmentIsAlreadyInForceOnTheFirstCalculation()
        {
            LivingCharacter character = Admit(items: new[]
            {
                Row("item-1", Sword, -1, equipmentSlot: (int)EquipmentSlot.MainHand),
            });

            Assert.That(character.Equipment.IsOccupied(EquipmentSlot.MainHand), Is.True);
            Assert.That(Attack(character), Is.EqualTo(35),
                "a character whose sword only counted after the first tick would fight one "
                + "tick weaker than they are");
            Assert.That(_statAuthority.Recomputations, Is.EqualTo(1),
                "and it took one calculation, not two");
        }

        // ---- the order within a tick ------------------------------------------------------------------

        [Test]
        public void AStatusExpiringInATickTakesItsModifierWithItInTheSameTick()
        {
            LivingCharacter character = Admit();

            Apply(character, Might, duration: 2f);

            _world.Tick(0.1f);

            Assert.That(Attack(character), Is.EqualTo(20), "the buff is in force");
            Assert.That(character.Status.Has(new DefinitionId(Might)), Is.True);

            // Past the end, in one tick. Status expires and stats recompute before the tick
            // returns -- there is never a frame where the icon is gone and the number is not.
            _world.Tick(5f);

            Assert.That(character.Status.Has(new DefinitionId(Might)), Is.False,
                "the effect expired");
            Assert.That(Attack(character), Is.EqualTo(10),
                "and its modifier went with it, in the same tick");
        }

        [Test]
        public void ApplyingAStatusTakesEffectOnTheNextTickWithNobodyAskingForIt()
        {
            LivingCharacter character = Admit();

            int before = Attack(character);

            // Applied through the one service, exactly as a skill would.
            Apply(character, Might);

            _world.Tick(0.016f);

            Assert.That(Attack(character), Is.EqualTo(before + 10),
                "no test called RefreshAll; if it had to, so would the game");
        }

        [Test]
        public void EquippingTakesEffectOnTheNextTickToo()
        {
            LivingCharacter character = Admit();

            int bare = Attack(character);

            var sword = new EquipmentInstance(new InstanceId("item-1"),
                new DefinitionId(Sword), character.Owner);

            Assert.That(character.Equipment.Restore(EquipmentSlot.MainHand, sword), Is.True);

            _world.Tick(0.016f);

            Assert.That(Attack(character), Is.EqualTo(bare + 25));
        }

        // ---- and what a tick must not cost ----------------------------------------------------------------

        [Test]
        public void SixHundredUnchangedTicksRecomputeNothing()
        {
            LivingCharacter character = Admit();

            Apply(character, Might, duration: 600f);

            _world.Tick(0.016f);

            int computed = _statAuthority.Recomputations;

            for (var i = 0; i < 600; i++) _world.Tick(1f / 60f);

            Assert.That(_world.Ticks, Is.EqualTo(601L), "the loop really did run");
            Assert.That(_statAuthority.Recomputations, Is.EqualTo(computed),
                "a stat block recomputed once a frame because a number is counting down");
            Assert.That(Attack(character), Is.EqualTo(20), "and the buff is still in force");
        }

        [Test]
        public void AnEmptyWorldTicksWithoutComplaining()
        {
            // A server that has admitted nobody still runs its clock. It must not throw, and
            // it must not do work.
            for (var i = 0; i < 10; i++) _world.Tick(0.1f);

            Assert.That(_world.Ticks, Is.EqualTo(10L));
            Assert.That(_statAuthority.Recomputations, Is.Zero);

            // And a world composed with nothing at all is a legitimate configuration.
            var bare = new WorldSimulation(null);

            Assert.DoesNotThrow(() => bare.Tick(0.1f));
            Assert.That(bare.Ticks, Is.EqualTo(1L));
        }

        // ---- leaving ---------------------------------------------------------------------------------------

        [Test]
        public void ReleasingACharacterForgetsEverythingCachedAboutIt()
        {
            LivingCharacter character = Admit();

            Apply(character, Might);

            _world.Tick(0.016f);

            Assert.That(Attack(character), Is.EqualTo(20));

            Assert.That(_world.Release(1).IsOk, Is.True);

            // Back on the same connection: a new character, no buff, correct stats at once.
            LivingCharacter returned = Admit();

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "temporary status is server memory and is not persisted");
            Assert.That(Attack(returned), Is.EqualTo(10));
            Assert.That(returned.Combatant.CurrentHealth, Is.GreaterThan(0),
                "and they did not come back dead");
        }

        // ---- helpers -----------------------------------------------------------------------------------------

        private static string Code(string path)
        {
            var kept = new List<string>();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("///") || trimmed.StartsWith("//")) continue;

                kept.Add(line);
            }

            return string.Join(" ", kept);
        }

        private static int Attack(LivingCharacter character)
        {
            Assert.That(character.Combatant.TryGetCombatStat(new DefinitionId(Atk),
                out int value), Is.True, "attack was never computed");

            return value;
        }

        private void Apply(LivingCharacter character, string effect, float duration = 60f)
        {
            StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                new DefinitionId(effect), new DefinitionId("skill.buff"), _effects, duration);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        /// <summary>Enters the world the way the production composition does.</summary>
        /// <remarks>Through <c>WorldSimulation.Admit</c>, which supplies no ceilings: the
        /// authority computes the real ones before anybody looks.</remarks>
        private LivingCharacter Admit(int health = 130, int mana = 30,
            PersistedItem[] items = null, int connection = 1, string character = "char-a")
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, health, mana,
                    new DefinitionId(Swordsman), default, new DefinitionId(HomeMap),
                    default, Attributes(), null, null, 1, items, 8);
            }

            WorldSpawnResult spawned = _world.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static PersistedStat[] Attributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId(Str), 10),
                new PersistedStat(new DefinitionId(Vit), 8),
            };
        }

        private static PersistedItem Row(string instance, string item, int slot,
            int quantity = 1, int equipmentSlot = 0)
        {
            return new PersistedItem(new InstanceId(instance), new DefinitionId(item),
                quantity, slot, 0, equipmentSlot, 0, default, null, null);
        }

        private static StatModifier[] Flat(string stat, float value)
        {
            return new[]
            {
                new StatModifier(new DefinitionId(stat), StatModifierKind.Flat, value),
            };
        }

        private StatusEffectDefinition Effect(string id, StatModifier[] modifiers)
        {
            var definition = Track(ScriptableObject.CreateInstance<StatusEffectDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)StatusEffectCategory.Buff
                + ",\"_durationSeconds\":0,\"_stackBehavior\":0,\"_maxStacks\":1}", definition);

            SetPrivate(definition, "_statModifiers", modifiers);

            return definition;
        }

        private EquipmentDefinition Weapon(string id, StatModifier[] modifiers)
        {
            var definition = Track(ScriptableObject.CreateInstance<EquipmentDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_stackable\":false,"
                + "\"_maxStackSize\":1,\"_slot\":" + (int)EquipmentSlot.MainHand
                + ",\"_levelRequirement\":1}", definition);

            SetPrivate(definition, "_baseStatModifiers", modifiers);

            return definition;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = Track(ScriptableObject.CreateInstance<SpawnPointDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            return spawn;
        }
    }
}
