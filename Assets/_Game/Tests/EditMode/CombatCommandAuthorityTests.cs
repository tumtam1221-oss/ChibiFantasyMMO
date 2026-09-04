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
    /// Turning a client's combat request into something the Phase 07 rules can run.
    /// </summary>
    /// <remarks>
    /// What is under test is identity, not combat. Range, cooldown, resource cost and damage
    /// belong to <c>SkillUseValidator</c> and <c>SkillExecutor</c>, which already have their
    /// own tests and are deliberately not reimplemented.
    ///
    /// The property here is that <b>the attacker and the target are looked up, never sent</b>.
    /// Forging either is not something the server detects; it is something the message has no
    /// way to express, and these tests are what keep that true.
    /// </remarks>
    [TestFixture]
    internal sealed class CombatCommandAuthorityTests
    {
        /// <summary>A combatant with exactly the surface Phase 07 asks for.</summary>
        private sealed class TestCombatant : ICombatant
        {
            public TestCombatant(string id, CombatTeam team, int health = 100)
            {
                CombatantId = new InstanceId(id);
                Team = team;
                CurrentHealth = health;
            }

            public InstanceId CombatantId { get; }

            public CombatTeam Team { get; }

            public int CurrentHealth { get; private set; }

            public int MaxHealth => 100;

            public CombatPosition Position { get; set; }

            public bool TryGetCombatStat(DefinitionId stat, out int value)
            {
                value = 10;

                return true;
            }

            public void ApplyHealthDelta(long delta)
            {
                CurrentHealth = (int)System.Math.Max(0, CurrentHealth + delta);
            }
        }

        private sealed class FakeCombatants : ICombatantResolver
        {
            private readonly Dictionary<string, ICombatant> _combatants =
                new Dictionary<string, ICombatant>();

            private readonly Dictionary<string, DefinitionId> _maps =
                new Dictionary<string, DefinitionId>();

            public FakeCombatants Holds(ICombatant combatant, string map = "map.town")
            {
                _combatants[combatant.CombatantId.Value] = combatant;
                _maps[combatant.CombatantId.Value] = new DefinitionId(map);

                return this;
            }

            public bool TryResolve(InstanceId instance, out ICombatant combatant)
            {
                combatant = null;

                return instance.IsValid && _combatants.TryGetValue(instance.Value, out combatant);
            }

            public bool TryGetMap(InstanceId instance, out DefinitionId map)
            {
                map = default;

                return instance.IsValid && _maps.TryGetValue(instance.Value, out map);
            }
        }

        private sealed class FakeStore : ICharacterStateStore
        {
            public PersistedCharacter Row;

            public CharacterPersistenceResult Load(SessionId session)
            {
                return CharacterPersistenceResult.Loaded(Row);
            }

            public CharacterPersistenceResult Save(SessionId session, PersistedCharacter c, int r)
            {
                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private const string MyCharacter = "char-mine";
        private const string TheirCharacter = "char-theirs";

        private WorldCharacterRegistry _characters;
        private FakeCombatants _combatants;
        private CombatCommandAuthority _authority;
        private LivingCharacter _mine;
        private TestCombatant _me;
        private TestCombatant _monster;
        private bool _canAct = true;
        private readonly List<SpawnPointDefinition> _created = new List<SpawnPointDefinition>();

        [SetUp]
        public void SetUp()
        {
            _canAct = true;

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(Spawn("spawn.town", "map.town"));

            var store = new FakeStore
            {
                Row = new PersistedCharacter(
                    new CharacterId(MyCharacter), new AccountId("acc-1"), new ServerId("srv-1"),
                    "Ayla", 2, 12, 100, 87, 33, new DefinitionId("class.novice"), default,
                    new DefinitionId("map.town"), new DefinitionId("spawn.town"),
                    null, null, null, 1),
            };

            _characters = new WorldCharacterRegistry(store, spawns);

            _mine = _characters.Spawn(1, WorldAdmission.Admitted(
                new SessionId("s1"), new AccountId("acc-1"), new CharacterId(MyCharacter),
                new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId("map.town"),
                new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(200, 100)).Character;

            Assert.That(_mine, Is.Not.Null, "precondition: the character spawned");

            // CombatTeam is an opaque integer, deliberately: factions are content, so
            // there is no Player or Enemy member to name.
            _me = new TestCombatant(MyCharacter, new CombatTeam(1));
            _monster = new TestCombatant("monster-1", new CombatTeam(2));

            _combatants = new FakeCombatants().Holds(_me).Holds(_monster);

            _authority = new CombatCommandAuthority(_characters, id => _canAct, _combatants);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (SpawnPointDefinition spawn in _created)
            {
                if (spawn != null) Object.DestroyImmediate(spawn);
            }

            _created.Clear();
        }

        private SpawnPointDefinition Spawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":0,\"_y\":0,\"_z\":0}",
                spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static CombatCommand Attack(string target = "monster-1", long sequence = 1,
            string claimedAttacker = MyCharacter, string skill = null, int rank = 1)
        {
            return new CombatCommand(new CharacterId(claimedAttacker), new InstanceId(target),
                skill == null ? default : new DefinitionId(skill), rank, sequence);
        }

        // ---- the ordinary case ----------------------------------------------------------

        [Test]
        public void AWellFormedCommandResolvesToTheConnectionsOwnCharacter()
        {
            CombatCommandResolution result = _authority.Resolve(1, Attack());

            Assert.That(result.IsResolved, Is.True, result.Reason.ToString());
            Assert.That(result.Attacker.Character.Value, Is.EqualTo(MyCharacter));
            Assert.That(result.Target.CombatantId.Value, Is.EqualTo("monster-1"));
        }

        [Test]
        public void ACommandThatClaimsNoAttackerStillResolvesToTheRightOne()
        {
            // The claim is compared when present and simply absent otherwise. Either way
            // the attacker comes from the connection.
            CombatCommandResolution result = _authority.Resolve(1, Attack(claimedAttacker: ""));

            Assert.That(result.IsResolved, Is.True);
            Assert.That(result.Attacker.Character.Value, Is.EqualTo(MyCharacter));
        }

        // ---- 17.22: forged identity ---------------------------------------------------------

        [Test]
        public void ForgedAttacker_ClaimingSomebodyElsesCharacterIsRefused()
        {
            CombatCommandResolution result = _authority.Resolve(1,
                Attack(claimedAttacker: TheirCharacter));

            Assert.That(result.IsResolved, Is.False);
            Assert.That(result.Reason, Is.EqualTo(CombatCommandRejection.NotYourCharacter));
        }

        [Test]
        public void ForgedTarget_NamingSomethingTheServerDoesNotHoldIsRefused()
        {
            CombatCommandResolution result = _authority.Resolve(1,
                Attack(target: "monster-that-does-not-exist"));

            Assert.That(result.Reason, Is.EqualTo(CombatCommandRejection.UnknownTarget));
        }

        [Test]
        public void ACommandWithNoTargetIsRefused()
        {
            Assert.That(_authority.Resolve(1, Attack(target: "")).Reason,
                Is.EqualTo(CombatCommandRejection.Malformed));
        }

        [Test]
        public void ATargetOnAnotherMapIsRefusedRatherThanLeftToTheRangeCheck()
        {
            var faraway = new TestCombatant("monster-cave", new CombatTeam(2));
            _combatants.Holds(faraway, "map.cave");

            CombatCommandResolution result = _authority.Resolve(1, Attack(target: "monster-cave"));

            Assert.That(result.Reason, Is.EqualTo(CombatCommandRejection.DifferentMap),
                "range alone would let a player hit through a loading screen if two maps "
                + "overlap in coordinates");
        }

        [Test]
        public void ThereIsNowhereInACommandToPutDamageHealthOrACooldown()
        {
            // The reason forged damage is not something this has to detect: the message
            // cannot express it. A field appearing here must be a deliberate act.
            System.Type command = typeof(CombatCommand);

            foreach (string absent in new[]
                     { "Damage", "Health", "CurrentHealth", "Cooldown", "IsCritical", "Hit",
                       "Experience", "Reward" })
            {
                Assert.That(command.GetProperty(absent), Is.Null,
                    absent + " must not be something a client can send");
            }
        }

        // ---- connection state ------------------------------------------------------------------

        [Test]
        public void AConnectionWithNoCharacterCannotAttack()
        {
            Assert.That(_authority.Resolve(99, Attack()).Reason,
                Is.EqualTo(CombatCommandRejection.NoCharacter));
        }

        [Test]
        public void AStaleConnectionCannotAttack()
        {
            _canAct = false;

            Assert.That(_authority.Resolve(1, Attack()).Reason,
                Is.EqualTo(CombatCommandRejection.StaleConnection),
                "a displaced socket must not act on a character its replacement controls");
        }

        [Test]
        public void AStaleConnectionIsRefusedBeforeAnythingIsLookedUp()
        {
            _canAct = false;

            // Naming a nonexistent target as well: the cheap check wins, so a flood from a
            // dead socket costs nothing.
            Assert.That(_authority.Resolve(1, Attack(target: "nonexistent")).Reason,
                Is.EqualTo(CombatCommandRejection.StaleConnection));
        }

        [Test]
        public void ADeadAttackerCannotAttack()
        {
            _me.ApplyHealthDelta(-1000);

            Assert.That(_authority.Resolve(1, Attack()).Reason,
                Is.EqualTo(CombatCommandRejection.AttackerDead));
        }

        // ---- replay --------------------------------------------------------------------------------

        [Test]
        public void AReplayedCommandIsRefused()
        {
            CombatCommandResolution first = _authority.Resolve(1, Attack(sequence: 5));
            Assert.That(first.IsResolved, Is.True);

            CombatCommandAuthority.Commit(first.Attacker, Attack(sequence: 5));

            Assert.That(_authority.Resolve(1, Attack(sequence: 5)).Reason,
                Is.EqualTo(CombatCommandRejection.OutOfOrder));
        }

        [Test]
        public void AnOlderCommandIsRefused()
        {
            CombatCommandResolution first = _authority.Resolve(1, Attack(sequence: 9));
            CombatCommandAuthority.Commit(first.Attacker, Attack(sequence: 9));

            Assert.That(_authority.Resolve(1, Attack(sequence: 3)).Reason,
                Is.EqualTo(CombatCommandRejection.OutOfOrder));
        }

        [Test]
        public void ResolvingDoesNotConsumeTheSequenceOnItsOwn()
        {
            // A resolution the domain then refuses -- out of range, on cooldown -- must not
            // burn the sequence. A player whose skill was refused has to be able to press
            // it again.
            _authority.Resolve(1, Attack(sequence: 5));

            Assert.That(_authority.Resolve(1, Attack(sequence: 5)).IsResolved, Is.True,
                "only an accepted command commits");
        }

        [Test]
        public void CommittingAdvancesTheSequence()
        {
            CombatCommandResolution result = _authority.Resolve(1, Attack(sequence: 5));

            CombatCommandAuthority.Commit(result.Attacker, Attack(sequence: 5));

            Assert.That(_mine.LastCombatSequence, Is.EqualTo(5));
        }

        [Test]
        public void CommittingNothingIsHarmless()
        {
            Assert.DoesNotThrow(() => CombatCommandAuthority.Commit(null, Attack()));
        }

        [Test]
        public void CommittingACombatCommandDoesNotDisturbTheMovementSequence()
        {
            CombatCommandResolution result = _authority.Resolve(1, Attack(sequence: 42));

            CombatCommandAuthority.Commit(result.Attacker, Attack(sequence: 42));

            // The two streams are independent: a player attacking must not advance the
            // counter their next movement is measured against, or vice versa.
            Assert.That(_mine.LastCombatSequence, Is.EqualTo(42));
            Assert.That(_mine.LastMovementSequence, Is.Zero);
        }

        // ---- handing off to the Phase 07 rules -------------------------------------------------------

        [Test]
        public void AResolvedSkillCommandBecomesTheRequestTheExistingValidatorTakes()
        {
            CombatCommandResolution resolution = _authority.Resolve(1,
                Attack(skill: "skill.slash", rank: 3));

            SkillUseRequest request = CombatCommandAuthority.ToSkillRequest(resolution,
                Attack(skill: "skill.slash", rank: 3));

            Assert.That(request.Skill.Value, Is.EqualTo("skill.slash"));
            Assert.That(request.Rank, Is.EqualTo(3));
            Assert.That(request.Caster, Is.SameAs(resolution.AttackerCombatant));
            Assert.That(request.Target, Is.SameAs(resolution.Target));
            Assert.That(request.IsStructurallyComplete, Is.True);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void ANonsenseRankIsClampedRatherThanPassedThrough(int rank)
        {
            CombatCommandResolution resolution = _authority.Resolve(1,
                Attack(skill: "skill.slash", rank: rank));

            SkillUseRequest request = CombatCommandAuthority.ToSkillRequest(resolution,
                Attack(skill: "skill.slash", rank: rank));

            Assert.That(request.Rank, Is.EqualTo(1),
                "the validator's rank check is about what was learned, not arithmetic");
        }

        [Test]
        public void AResolvedAttackBecomesTheIntentTheExistingExecutorTakes()
        {
            CombatCommandResolution resolution = _authority.Resolve(1, Attack(sequence: 7));

            AttackIntent intent = CombatCommandAuthority.ToAttackIntent(resolution,
                Attack(sequence: 7), new DefinitionId("attack.basic"));

            Assert.That(intent.Attacker, Is.SameAs(resolution.AttackerCombatant));
            Assert.That(intent.Target, Is.SameAs(resolution.Target));
            Assert.That(intent.AttackDefinition.Value, Is.EqualTo("attack.basic"));
            Assert.That(intent.IsStructurallyComplete, Is.True);
        }

        [Test]
        public void ACommandWithNoSkillIsABasicAttack()
        {
            Assert.That(Attack().IsSkill, Is.False);
            Assert.That(Attack(skill: "skill.slash").IsSkill, Is.True);
        }

        // ---- misconfiguration -------------------------------------------------------------------------

        [Test]
        public void AnAuthorityWithNoResolverRefusesRatherThanGuessing()
        {
            var blind = new CombatCommandAuthority(_characters, id => true, null);

            Assert.That(blind.Resolve(1, Attack()).IsResolved, Is.False);
        }

        [Test]
        public void AnAuthorityWithNoCharacterRegistryRefuses()
        {
            var blind = new CombatCommandAuthority(null, id => true, _combatants);

            Assert.That(blind.Resolve(1, Attack()).IsResolved, Is.False);
        }
    }
}
