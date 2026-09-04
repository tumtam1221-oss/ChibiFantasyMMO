using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Turning a stored row into the character the domain already knows, and back.
    /// </summary>
    /// <remarks>
    /// The property this fixture exists for is negative: <b>a row the domain refuses must
    /// produce a typed failure, not an exception</b>. Loading happens during world entry,
    /// where a throw leaves a half-built character and a connection nobody disconnects. The
    /// database column for a stat is signed so a corrupt row is storable and visible rather
    /// than wrapping to four billion; the domain refuses negative stats; those two decisions
    /// meet in the mapper, and this is where that meeting is checked.
    /// </remarks>
    [TestFixture]
    internal sealed class PersistedCharacterMapperTests
    {
        private static readonly ResourceLimits Limits = new ResourceLimits(200, 100);

        private static PersistedCharacter Row(
            int level = 12,
            long experience = 4500,
            int health = 87,
            int mana = 33,
            IReadOnlyList<PersistedStat> stats = null,
            IReadOnlyList<PersistedSkill> skills = null,
            IReadOnlyList<PersistedAppearance> appearance = null,
            string character = "char-1",
            string account = "acc-1")
        {
            return new PersistedCharacter(
                new CharacterId(character),
                new AccountId(account),
                new ServerId("srv-1"),
                "Ayla",
                (int)CharacterGender.Female,
                level,
                experience,
                health,
                mana,
                new DefinitionId("class.novice"),
                new DefinitionId("job.none"),
                new DefinitionId("map.town"),
                new DefinitionId("spawn.town.plaza"),
                stats ?? new[]
                {
                    new PersistedStat(new DefinitionId("stat.strength"), 14),
                    new PersistedStat(new DefinitionId("stat.agility"), 9),
                },
                appearance ?? new[]
                {
                    new PersistedAppearance((int)AppearanceSlot.Face, new DefinitionId("face.round")),
                    new PersistedAppearance((int)AppearanceSlot.Hair, new DefinitionId("hair.short")),
                },
                skills ?? new[]
                {
                    new PersistedSkill(new DefinitionId("skill.slash"), 3),
                },
                7);
        }

        // ---- a good row -------------------------------------------------------------------

        [Test]
        public void AStoredRowBecomesTheExistingDomainAggregate()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(Row(), Limits);

            Assert.That(outcome.IsOk, Is.True, outcome.Detail);

            // Every one of these is a Phase 04-08 type. There is no server-side character
            // model, because a second model is a second set of rules that will disagree.
            Assert.That(outcome.Character, Is.TypeOf<Character>());
            Assert.That(outcome.Character.Identity, Is.TypeOf<CharacterState>());
            Assert.That(outcome.Character.Class, Is.TypeOf<CharacterClassState>());
            Assert.That(outcome.Character.Appearance, Is.TypeOf<CharacterAppearanceState>());
            Assert.That(outcome.Character.Progression, Is.TypeOf<CharacterProgressionState>());
            Assert.That(outcome.Character.Stats, Is.TypeOf<CharacterStatsState>());
            Assert.That(outcome.Character.Resources, Is.TypeOf<CharacterResourceState>());
        }

        [Test]
        public void IdentityLevelExperienceAndResourcesAllSurvive()
        {
            Character character = PersistedCharacterMapper.ToDomain(Row(), Limits).Character;

            Assert.That(character.Identity.CharacterId.Value, Is.EqualTo("char-1"));
            Assert.That(character.Identity.Name, Is.EqualTo("Ayla"));
            Assert.That(character.Identity.Gender, Is.EqualTo(CharacterGender.Female));
            Assert.That(character.Progression.Level, Is.EqualTo(12));
            Assert.That(character.Progression.Experience, Is.EqualTo(4500));
            Assert.That(character.Resources.CurrentHealth, Is.EqualTo(87));
            Assert.That(character.Resources.CurrentMana, Is.EqualTo(33));
        }

        [Test]
        public void OwnershipIsProjectedFromTheAccountRatherThanStoredSeparately()
        {
            Character character = PersistedCharacterMapper.ToDomain(Row(), Limits).Character;

            Assert.That(character.Identity.Owner.Value, Is.EqualTo("acc-1"),
                "the same projection AuthenticatedAccount and WorldAdmission make");
        }

        [Test]
        public void StatsAppearanceAndSkillsAllArrive()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(Row(), Limits);

            Assert.That(outcome.Character.Stats.TryGet(new DefinitionId("stat.strength"),
                out int strength), Is.True);
            Assert.That(strength, Is.EqualTo(14));

            Assert.That(outcome.Character.Appearance.Get(AppearanceSlot.Face).Value,
                Is.EqualTo("face.round"));
            Assert.That(outcome.Character.Appearance.Get(AppearanceSlot.Hair).Value,
                Is.EqualTo("hair.short"));

            Assert.That(outcome.Skills.Knows(new DefinitionId("skill.slash")), Is.True);
            Assert.That(outcome.Skills.GetRankOrDefault(new DefinitionId("skill.slash"), 0),
                Is.EqualTo(3));
        }

        [Test]
        public void AnUnsetJobLeavesTheCharacterUnchanged()
        {
            PersistedCharacter row = new PersistedCharacter(
                new CharacterId("char-1"), new AccountId("acc-1"), new ServerId("srv-1"),
                "Ayla", 2, 5, 0, 10, 10, new DefinitionId("class.novice"), default,
                new DefinitionId("map.town"), default, null, null, null, 0);

            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(row, Limits);

            Assert.That(outcome.IsOk, Is.True, outcome.Detail);
            Assert.That(outcome.Character.Class.HasChangedJob, Is.False,
                "an empty job column means no job, not a job called empty");
        }

        [Test]
        public void HealthSavedAboveTheCurrentCeilingIsClampedRatherThanRefused()
        {
            // What happens when a ring comes off while a player is offline. Not corruption.
            Character character = PersistedCharacterMapper
                .ToDomain(Row(health: 5000), Limits).Character;

            Assert.That(character.Resources.CurrentHealth, Is.EqualTo(Limits.MaxHealth));
        }

        // ---- rows the domain refuses ---------------------------------------------------------

        [Test]
        public void ANegativeStatIsReportedRatherThanThrownOrClamped()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(
                Row(stats: new[]
                {
                    new PersistedStat(new DefinitionId("stat.strength"), -3),
                }), Limits);

            Assert.That(outcome.IsOk, Is.False);
            Assert.That(outcome.Failure, Is.EqualTo(CharacterPersistenceFailure.Corrupt));
            Assert.That(outcome.Detail, Does.Contain("stat.strength"),
                "an operator has to be told which row to fix");
        }

        [Test]
        public void ANegativeStatDoesNotThrowOutOfWorldEntry()
        {
            // The exact failure this design exists to prevent: CharacterStatsState.Set
            // throws on a negative, and a throw here would leave a half-built character
            // and a connection nobody disconnects.
            Assert.DoesNotThrow(() => PersistedCharacterMapper.ToDomain(
                Row(stats: new[] { new PersistedStat(new DefinitionId("stat.x"), -1) }),
                Limits));
        }

        [TestCase(0)]
        [TestCase(-4)]
        public void ALevelBelowOneIsRefused(int level)
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(Row(level: level),
                Limits);

            Assert.That(outcome.IsOk, Is.False);
            Assert.That(outcome.Failure, Is.EqualTo(CharacterPersistenceFailure.Corrupt));
        }

        [Test]
        public void NegativeExperienceIsRefused()
        {
            Assert.That(PersistedCharacterMapper.ToDomain(Row(experience: -1), Limits).IsOk,
                Is.False);
        }

        [Test]
        public void ASkillBelowLevelOneIsRefused()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(
                Row(skills: new[] { new PersistedSkill(new DefinitionId("skill.slash"), 0) }),
                Limits);

            Assert.That(outcome.IsOk, Is.False);
            Assert.That(outcome.Detail, Does.Contain("skill.slash"));
        }

        [Test]
        public void ARowWithNoCharacterIdIsRefused()
        {
            Assert.That(PersistedCharacterMapper.ToDomain(Row(character: ""), Limits).IsOk,
                Is.False);
        }

        [Test]
        public void ARowWithNoAccountIsRefused()
        {
            Assert.That(PersistedCharacterMapper.ToDomain(Row(account: ""), Limits).IsOk,
                Is.False);
        }

        [Test]
        public void NoRowAtAllIsRefusedRatherThanCrashing()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(null, Limits);

            Assert.That(outcome.IsOk, Is.False);
            Assert.That(outcome.Character, Is.Null);
        }

        [Test]
        public void AnEmptyStatIdIsRefusedRatherThanSilentlySkipped()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(
                Row(stats: new[] { new PersistedStat(default, 5) }), Limits);

            Assert.That(outcome.IsOk, Is.False,
                "a stat with no id is a broken row, not an absent one");
        }

        [Test]
        public void AnEmptyAppearanceOptionIsSkippedRatherThanRefused()
        {
            // Unlike a stat, an unset appearance slot is ordinary: a character may simply
            // not have chosen a hair colour.
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(
                Row(appearance: new[]
                {
                    new PersistedAppearance((int)AppearanceSlot.Face, new DefinitionId("face.round")),
                    new PersistedAppearance((int)AppearanceSlot.HairColor, default),
                }), Limits);

            Assert.That(outcome.IsOk, Is.True, outcome.Detail);
            Assert.That(outcome.Character.Appearance.Get(AppearanceSlot.HairColor).IsValid,
                Is.False);
        }

        [Test]
        public void AppearanceSlotNoneIsIgnored()
        {
            CharacterLoadOutcome outcome = PersistedCharacterMapper.ToDomain(
                Row(appearance: new[]
                {
                    new PersistedAppearance(0, new DefinitionId("nonsense")),
                }), Limits);

            Assert.That(outcome.IsOk, Is.True, outcome.Detail);
        }

        // ---- round trip -------------------------------------------------------------------

        [Test]
        public void ACharacterSurvivesTheRoundTripUnchanged()
        {
            CharacterLoadOutcome loaded = PersistedCharacterMapper.ToDomain(Row(), Limits);

            var location = new CharacterLocationState(new CharacterId("char-1"),
                new DefinitionId("map.town"), new DefinitionId("spawn.town.plaza"));

            PersistedCharacter round = PersistedCharacterMapper.ToPersisted(
                loaded.Character, loaded.Skills, location, new ServerId("srv-1"),
                new AccountId("acc-1"), 7);

            Assert.That(round.Character.Value, Is.EqualTo("char-1"));
            Assert.That(round.Level, Is.EqualTo(12));
            Assert.That(round.Experience, Is.EqualTo(4500));
            Assert.That(round.CurrentHealth, Is.EqualTo(87));
            Assert.That(round.Class.Value, Is.EqualTo("class.novice"));
            Assert.That(round.Stats.Count, Is.EqualTo(2));
            Assert.That(round.Skills.Count, Is.EqualTo(1));
            Assert.That(round.Appearance.Count, Is.EqualTo(2));
        }

        [Test]
        public void TheLocationComesFromTheLocationStateAndNotFromTheCharacter()
        {
            CharacterLoadOutcome loaded = PersistedCharacterMapper.ToDomain(Row(), Limits);

            // Phase 11 owns where a character is. Reading it from anywhere else would be
            // the second source of truth this project keeps avoiding.
            var moved = new CharacterLocationState(new CharacterId("char-1"),
                new DefinitionId("map.cave"), new DefinitionId("spawn.cave.mouth"));

            PersistedCharacter round = PersistedCharacterMapper.ToPersisted(
                loaded.Character, loaded.Skills, moved, new ServerId("srv-1"),
                new AccountId("acc-1"), 7);

            Assert.That(round.Map.Value, Is.EqualTo("map.cave"));
            Assert.That(round.Spawn.Value, Is.EqualTo("spawn.cave.mouth"));
        }

        [Test]
        public void TheSaveRevisionIsCarriedThroughUnchanged()
        {
            CharacterLoadOutcome loaded = PersistedCharacterMapper.ToDomain(Row(), Limits);

            PersistedCharacter round = PersistedCharacterMapper.ToPersisted(
                loaded.Character, loaded.Skills, null, new ServerId("srv-1"),
                new AccountId("acc-1"), 7);

            // A writer presents what it loaded. Incrementing here would let a server
            // overwrite a newer save simply by saving twice.
            Assert.That(round.SaveRevision, Is.EqualTo(7));
        }

        [Test]
        public void NoLocationYieldsNoMapRatherThanAFabricatedOne()
        {
            CharacterLoadOutcome loaded = PersistedCharacterMapper.ToDomain(Row(), Limits);

            PersistedCharacter round = PersistedCharacterMapper.ToPersisted(
                loaded.Character, loaded.Skills, null, new ServerId("srv-1"),
                new AccountId("acc-1"), 0);

            Assert.That(round.Map.IsValid, Is.False);
        }

        [Test]
        public void MappingNoCharacterBackYieldsNothing()
        {
            Assert.That(PersistedCharacterMapper.ToPersisted(null, null, null, default,
                default, 0), Is.Null);
        }
    }
}
