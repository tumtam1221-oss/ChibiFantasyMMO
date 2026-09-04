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
    /// A live character's effective stats, and what does and does not move them.
    /// </summary>
    /// <remarks>
    /// <b>The arithmetic is not retested here.</b> How base stats, flats and percents combine
    /// is <c>DerivedStatsCalculator</c>'s, and it has its own suite. What is new is that a
    /// living character's stats are recomputed at all -- until now the calculator ran at
    /// character creation and never again, so a buff or a sword changed the authoritative
    /// numbers not at all.
    ///
    /// <b>The negative cases carry most of the weight.</b> That a buff raises attack is easy;
    /// that a countdown does not cause a recomputation, that a refused status changes
    /// nothing, and that a dropped maximum cannot leave health above its ceiling are the
    /// properties a server actually depends on.
    /// </remarks>
    [TestFixture]
    internal sealed class LiveDerivedStatTests : CharacterCreationTestBase
    {
        private const string Atk = "stat.atk";
        private const string HomeMap = "map.home";

        private const string Might = "status.might";       // +10 flat attack
        private const string Weakness = "status.weakness"; // -5 flat attack
        private const string Fortitude = "status.fortitude"; // +100 flat max health
        private const string Poison = "status.poison";     // no modifiers at all

        /// <summary>A store that keeps what it was given, so a reconnect reads it back.</summary>
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
        private DefinitionRegistry<StatusEffectDefinition> _effects;
        private CharacterStatAuthority _authority;

        [SetUp]
        public void SetUpWorld()
        {
            // FIXTURE: attack is simply strength, so a strength or attack modifier is
            // visible in one step.
            AddStat(Atk, false);
            Formulas.Add(Formula("f.atk", Atk, 0, new StatTerm(new DefinitionId(Str), 1, 1)));

            _effects = new DefinitionRegistry<StatusEffectDefinition>();
            _effects.Register(Effect(Might, StatusEffectCategory.Buff,
                Flat(Atk, 10f)));
            _effects.Register(Effect(Weakness, StatusEffectCategory.Debuff,
                Flat(Atk, -5f)));
            _effects.Register(Effect(Fortitude, StatusEffectCategory.Buff,
                Flat(MaxHp, 100f)));
            _effects.Register(Effect(Poison, StatusEffectCategory.DamageOverTime));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns);

            _authority = new CharacterStatAuthority(_players, Formulas, Stats, _effects,
                default, new DefinitionId(MaxHp), new DefinitionId(MaxMp));
        }

        // ---- the calculator stays the only one ------------------------------------------------

        [Test]
        public void ThereIsStillExactlyOneDerivedStatCalculatorAndOneModifierResolver()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var calculators = 0;
            var resolvers = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class DerivedStatsCalculator")) calculators++;
                if (source.Contains("class EquipmentModifierResolver")) resolvers++;
            }

            Assert.That(calculators, Is.EqualTo(1),
                "a second calculator would start disagreeing about percent stacking");
            Assert.That(resolvers, Is.EqualTo(1));

            // And the new authority computes nothing itself: no arithmetic, no formula.
            string authority = Code(
                "Assets/_Game/Scripts/Server/CharacterStatAuthority.cs");

            Assert.That(authority, Does.Contain("_calculator.Calculate"),
                "the one calculator is what produces the numbers");
            Assert.That(authority, Does.Not.Contain("PercentScale"));
            Assert.That(authority, Does.Not.Contain("StatModifierKind.Percent"));
            Assert.That(authority, Does.Not.Contain("StatModifierKind.Flat"),
                "summing modifiers here would be a second stacking rule");
        }

        // ---- base, before anything is applied ---------------------------------------------------

        [Test]
        public void ALiveCharacterStartsWithItsBaseDerivedStats()
        {
            LivingCharacter character = EnterWorld();

            // Swordsman: STR 10, VIT 8. Attack = STR = 10; MaxHP = 50 + 8x10 = 130.
            Assert.That(Attack(character), Is.EqualTo(10));
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(130),
                "the ceiling comes out of the authored formula, not out of the save row");
            Assert.That(_authority.Recomputations, Is.EqualTo(1),
                "computed once on entering the world");
        }

        // ---- status modifiers -----------------------------------------------------------------------

        [Test]
        public void AStatModifyingStatusChangesTheAuthoritativeStatAndRemovalRestoresIt()
        {
            LivingCharacter character = EnterWorld();

            int before = Attack(character);

            Apply(character, Might);

            Assert.That(_authority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Attack(character), Is.EqualTo(before + 10));

            // Removal, through the runtime's own path.
            Assert.That(character.Status.Remove(new DefinitionId(Might)), Is.True);
            Assert.That(_authority.RefreshAll(), Is.EqualTo(1));

            Assert.That(Attack(character), Is.EqualTo(before),
                "a removed buff must leave nothing behind");
        }

        [Test]
        public void ExpiryRestoresTheStatThroughTheSamePath()
        {
            LivingCharacter character = EnterWorld();

            int before = Attack(character);

            Apply(character, Might, duration: 2f);
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(before + 10));

            // The server's clock, not the client's.
            character.Status.Tick(1f);

            Assert.That(_authority.RefreshAll(), Is.Zero,
                "a timer that merely moved is not a different set of modifiers");
            Assert.That(Attack(character), Is.EqualTo(before + 10));

            character.Status.Tick(1.5f);

            Assert.That(_authority.RefreshAll(), Is.EqualTo(1), "expiry is a real change");
            Assert.That(Attack(character), Is.EqualTo(before));
        }

        [Test]
        public void ADebuffLowersTheStatAndComposesWithABuff()
        {
            LivingCharacter character = EnterWorld();

            int before = Attack(character);

            Apply(character, Weakness);
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(before - 5));

            Apply(character, Might);
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(before + 5),
                "both are in force at once, summed by the one calculator");
        }

        [Test]
        public void StacksMultiplyAFlatModifierExactlyAsTheRuntimeAlreadyDecides()
        {
            LivingCharacter character = EnterWorld();

            // FIXTURE: an effect authored to stack three deep.
            _effects.Register(Effect("status.rage", StatusEffectCategory.Buff, Flat(Atk, 4f),
                stacking: StatusEffectStackBehavior.AddStack, maxStacks: 3));

            int before = Attack(character);

            Apply(character, "status.rage");
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(before + 4));

            Apply(character, "status.rage");

            Assert.That(_authority.RefreshAll(), Is.EqualTo(1), "a stack change is a change");
            Assert.That(Attack(character), Is.EqualTo(before + 8),
                "a flat modifier contributes once per stack, which is what a stack means");

            Apply(character, "status.rage");
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(before + 12));
        }

        // ---- what must not cause work -------------------------------------------------------------

        [Test]
        public void ACountdownAloneNeverRecomputesAnything()
        {
            LivingCharacter character = EnterWorld();

            Apply(character, Might, duration: 60f);
            _authority.RefreshAll();

            int computed = _authority.Recomputations;

            // A minute of frames at sixty a second, with a buff running the whole time.
            for (var i = 0; i < 600; i++)
            {
                character.Status.Tick(1f / 60f);
                _authority.RefreshAll();
            }

            Assert.That(_authority.Recomputations, Is.EqualTo(computed),
                "recomputing a stat block every frame because a number is counting down is "
                + "the defect this check exists to catch");
            Assert.That(Attack(character), Is.EqualTo(10 + 10), "and it is still in force");
        }

        [Test]
        public void ARefreshThatChangesNoMagnitudeDoesNotRecompute()
        {
            LivingCharacter character = EnterWorld();

            Apply(character, Might, duration: 30f);
            _authority.RefreshAll();

            int computed = _authority.Recomputations;

            // Re-applied. The duration is what moves; the modifier is identical.
            StatusEffectService.TryApply(character.Status, new DefinitionId(Might),
                new DefinitionId("skill.buff"), _effects, durationOverride: 60f);

            Assert.That(_authority.RefreshAll(), Is.Zero);
            Assert.That(_authority.Recomputations, Is.EqualTo(computed),
                "a longer timer is the same set of modifiers");
        }

        [Test]
        public void ARefusedStatusRecomputesNothingAndChangesNoStat()
        {
            LivingCharacter character = EnterWorld();

            character.Status.AddImmunity(new StatusImmunity(new DefinitionId("fruit.light"),
                default, StatusEffectCategory.Debuff));

            _authority.RefreshAll();

            int computed = _authority.Recomputations;
            int before = Attack(character);

            StatusApplyResult refused = StatusEffectService.TryApply(character.Status,
                new DefinitionId(Weakness), new DefinitionId("skill.curse"), _effects);

            Assert.That(refused.IsAccepted, Is.False);
            Assert.That(refused.Reason, Is.EqualTo(StatusApplyRejection.Immune));

            Assert.That(_authority.RefreshAll(), Is.Zero, "nothing was applied to react to");
            Assert.That(_authority.Recomputations, Is.EqualTo(computed));
            Assert.That(Attack(character), Is.EqualTo(before));
        }

        [Test]
        public void AStatusWithNoModifiersStillRecomputesOnceAndChangesNothing()
        {
            LivingCharacter character = EnterWorld();

            int before = Attack(character);

            Apply(character, Poison);

            // The set of applied effects did change, so the check cannot know it was
            // irrelevant without asking the calculator. Recomputing once is correct; the
            // answer is simply the same.
            Assert.That(_authority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Attack(character), Is.EqualTo(before));
        }

        // ---- maxima ----------------------------------------------------------------------------------

        [Test]
        public void AMaxHealthBuffRaisesTheCeilingWithoutHealingAnybody()
        {
            LivingCharacter character = EnterWorld();

            int ceiling = character.Combatant.MaxHealth;

            character.Combatant.ApplyHealthDelta(-30);

            int wounded = character.Combatant.CurrentHealth;

            Apply(character, Fortitude);
            _authority.RefreshAll();

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(ceiling + 100));
            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(wounded),
                "raising a ceiling is not a heal");
        }

        [Test]
        public void LosingAMaxHealthBuffCanNeverLeaveHealthAboveTheCeiling()
        {
            LivingCharacter character = EnterWorld();

            Apply(character, Fortitude);
            _authority.RefreshAll();

            int raised = character.Combatant.MaxHealth;

            // Filled to the raised ceiling.
            character.Combatant.ApplyHealthDelta(raised);

            Assert.That(character.Combatant.CurrentHealth, Is.EqualTo(raised));

            character.Status.Remove(new DefinitionId(Fortitude));
            _authority.RefreshAll();

            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(raised - 100));
            Assert.That(character.Combatant.CurrentHealth,
                Is.EqualTo(character.Combatant.MaxHealth),
                "the existing clamp policy: current health is left alone unless it now "
                + "exceeds the maximum, in which case it comes down to it");

            // The invariant, stated outright.
            Assert.That(character.Combatant.CurrentHealth, Is.InRange(0,
                character.Combatant.MaxHealth));
        }

        [Test]
        public void TheMaximaComeFromTheAuthoredFormulasAndNotFromAnyLiteral()
        {
            LivingCharacter character = EnterWorld();

            // MaxHP = 50 + VIT x 10, MaxMP = 10 + STR x 2, both fixtures.
            Assert.That(character.Combatant.MaxHealth, Is.EqualTo(130));
            Assert.That(character.Combatant.Limits.MaxMana, Is.EqualTo(30));

            string authority = Code(
                "Assets/_Game/Scripts/Server/CharacterStatAuthority.cs");

            Assert.That(authority, Does.Contain("ResourceLimits.From"),
                "the ceilings are read out of the calculator's own result");
            Assert.That(authority, Does.Not.Contain("MaxHealth ="));
            Assert.That(authority, Does.Not.Contain("MaxMana ="));
        }

        // ---- reconnect and death -----------------------------------------------------------------------

        [Test]
        public void AReconnectHasNoTemporaryStatusAndNoStaleModifier()
        {
            LivingCharacter character = EnterWorld();

            Apply(character, Might);
            _authority.RefreshAll();

            Assert.That(Attack(character), Is.EqualTo(20));

            Assert.That(_players.Despawn(1).IsOk, Is.True);

            _authority.Forget(character.Character);

            LivingCharacter returned = EnterWorld();

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "18.7's policy: temporary status is server memory and is not persisted");
            Assert.That(Attack(returned), Is.EqualTo(10),
                "a stale buff surviving a reconnect would be a permanent one");
        }

        [Test]
        public void AStatusSurvivesDeathAndSoDoesItsModifier()
        {
            LivingCharacter character = EnterWorld();

            Apply(character, Might, duration: 30f);
            _authority.RefreshAll();

            character.Combatant.ApplyHealthDelta(-character.Combatant.CurrentHealth);

            Assert.That(character.Combatant.CurrentHealth, Is.Zero);

            // 18.7 established that nothing clears status on death. 18.8 does not change it.
            Assert.That(character.Status.Has(new DefinitionId(Might)), Is.True);
            Assert.That(_authority.RefreshAll(), Is.Zero, "death changed no modifier");
            Assert.That(Attack(character), Is.EqualTo(20));

            // And it still expires normally afterwards.
            character.Status.Tick(999f);

            Assert.That(_authority.RefreshAll(), Is.EqualTo(1));
            Assert.That(Attack(character), Is.EqualTo(10));
        }

        // ---- isolation ---------------------------------------------------------------------------------

        [Test]
        public void OneCharactersBuffNeverReachesAnother()
        {
            LivingCharacter a = EnterWorld("char-a", 1);
            LivingCharacter b = EnterWorld("char-b", 2);

            Apply(a, Might);

            Assert.That(_authority.RefreshAll(), Is.EqualTo(1),
                "only one character's inputs moved");

            Assert.That(Attack(a), Is.EqualTo(20));
            Assert.That(Attack(b), Is.EqualTo(10), "B was not buffed by A's buff");
            Assert.That(b.Status.ActiveCount, Is.Zero);
        }

        // ---- guards -------------------------------------------------------------------------------------

        [Test]
        public void NoClientCodeReachesTheStatAuthorityOrComputesEffectiveStats()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string named = file.Replace('\\', '/');

                if (named.Contains("/Prototype/")) continue;

                string source = Code(file);

                // What a client may not do: compute a character's effective stats, decide a
                // resource ceiling, or reach the authority that does.
                Assert.That(source, Does.Not.Contain("CharacterStatAuthority"), named);
                Assert.That(source, Does.Not.Contain("DerivedStatsCalculator"), named);
                Assert.That(source, Does.Not.Contain("ResourceLimits.From"), named);
                Assert.That(source, Does.Not.Contain("SetLimits"), named);

                // StatusEffectRuntimeState is not forbidden here: the Phase 12 collectible
                // controller drives an offline harness that legitimately holds one, and the
                // networked status path is guarded on its own files by 18.7's suite.

                // EquipmentModifierResolver is deliberately not forbidden: a tooltip saying
                // what a sword grants is reading that item's authored modifiers, which is
                // presentation of content and not a second opinion about a character.
            }

            // But no client turns modifiers into a character's stats.
            foreach (string file in files)
            {
                string named = file.Replace("\\", "/");

                if (named.Contains("/Prototype/")) continue;

                Assert.That(Code(file), Does.Not.Contain(".Calculate("), named);
            }
        }

        [Test]
        public void NoClientMessageCarriesAStatAModifierOrACeiling()
        {
            System.Reflection.MethodInfo[] methods =
                typeof(ChibiFantasy.Network.CharacterNetworkEntity).GetMethods();

            foreach (System.Reflection.MethodInfo method in methods)
            {
                bool isServerRpc = method.GetCustomAttributes(
                    typeof(FishNet.Object.ServerRpcAttribute), true).Length > 0;

                if (!isServerRpc) continue;

                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(StatModifier)),
                        method.Name);
                    Assert.That(parameter.ParameterType,
                        Is.Not.EqualTo(typeof(DerivedStatsResult)), method.Name);
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(ResourceLimits)),
                        method.Name);
                }

                Assert.That(method.Name.ToLowerInvariant(), Does.Not.Contain("modifier"),
                    method.Name + " lets a client speak about modifiers");
            }

            // The one combat request a client can make carries a target, a skill, a rank and
            // a sequence -- no attack value, no defence value, no attribute.
            System.Reflection.MethodInfo attack =
                typeof(ChibiFantasy.Network.CharacterNetworkEntity).GetMethod("RequestAttack");

            Assert.That(attack, Is.Not.Null);

            foreach (System.Reflection.ParameterInfo parameter in attack.GetParameters())
            {
                Assert.That(new[] { "targetInstanceId", "skillId", "rank", "sequence" },
                    Contains.Item(parameter.Name),
                    "a client may not name a number that decides a fight");
            }
        }

        [Test]
        public void OnlyTheServerAssemblyDrivesRecomputation()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var callers = new List<string>();

            foreach (string file in files)
            {
                string named = file.Replace('\\', '/');

                if (named.Contains("/Server/")) continue;

                if (Code(file).Contains("CharacterStatAuthority")) callers.Add(named);
            }

            Assert.That(callers, Is.Empty,
                "recomputation is the server's, and nothing outside it can ask");
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>A file with its comments removed, so prose cannot trip a guard.</summary>
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

        private void Apply(LivingCharacter character, string effect, float duration = 0f)
        {
            StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                new DefinitionId(effect), new DefinitionId("skill.buff"), _effects, duration);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        private LivingCharacter EnterWorld(string character = "char-a", int connection = 1)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 1, 0, 130, 30,
                    new DefinitionId(Swordsman), default, new DefinitionId(HomeMap),
                    default, Attributes(), null, null, 1);
            }

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                // The persisted ceilings, as a real composition supplies them. Spawning
                // with none would clamp the loaded health to zero before the authority
                // ever gets to compute the authored ones -- which reads as a character
                // who arrived dead.
                new ResourceLimits(130, 30), new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            // What a world does the moment a character is live: compute their stats once,
            // because a combatant is constructed with none.
            Assert.That(_authority.Force(spawned.Character), Is.True);

            return spawned.Character;
        }

        /// <summary>The persisted attributes a swordsman fixture carries.</summary>
        private static PersistedStat[] Attributes()
        {
            return new[]
            {
                new PersistedStat(new DefinitionId(Str), 10),
                new PersistedStat(new DefinitionId(Vit), 8),
            };
        }

        private static StatModifier[] Flat(string stat, float value)
        {
            return new[]
            {
                new StatModifier(new DefinitionId(stat), StatModifierKind.Flat, value),
            };
        }

        private StatusEffectDefinition Effect(string id, StatusEffectCategory category,
            StatModifier[] modifiers = null,
            StatusEffectStackBehavior stacking = StatusEffectStackBehavior.RefreshDuration,
            int maxStacks = 1)
        {
            var definition = Track(ScriptableObject.CreateInstance<StatusEffectDefinition>());

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)category
                + ",\"_durationSeconds\":0"
                + ",\"_stackBehavior\":" + (int)stacking
                + ",\"_maxStacks\":" + maxStacks + "}", definition);

            if (modifiers != null) SetPrivate(definition, "_statModifiers", modifiers);

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
