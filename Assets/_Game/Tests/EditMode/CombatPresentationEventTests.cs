using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The presentation event contract. (STEPS 1, 6, 17, 18)
    /// </summary>
    /// <remarks>
    /// These prove the boundary rather than the drawing: an event reports what already
    /// happened, carries no live combatant, and building one changes nothing. The
    /// Unity-side presenter is exercised by the play-mode harness.
    /// </remarks>
    internal sealed class CombatPresentationEventTests : SkillTestBase
    {
        private const string Def = "stat.def";
        private const string Mdef = "stat.mdef";
        private const string Atk = "stat.atk";

        private CharacterSkillsState _learned;
        private SkillCooldownState _cooldowns;
        private FakePooledCombatant _actor;
        private FakePooledCombatant _target;
        private CombatActionRunner _runner;

        private void Actors(int targetHealth = 1000)
        {
            _learned = new CharacterSkillsState(new CharacterId("c"));
            _cooldowns = new SkillCooldownState();
            _actor = new FakePooledCombatant("actor", 1, 100, 100, 100, 100).WithStat(Atk, 40);
            _target = new FakePooledCombatant("target", 2, targetHealth, targetHealth)
                .WithStat(Def, 0).WithStat(Mdef, 0);
            _runner = new CombatActionRunner(_actor);
        }

        private SkillDefinition Author(string id, float castTime, float cooldown,
            int damage, DamageType type, SkillEffect[] effects = null)
        {
            var skill = AddSkill(id, maxLevel: 1, castTime: castTime, range: 0f, levels: new[]
            {
                Level(1, 1, 0f, cooldown, effects ?? new[]
                {
                    SkillEffect.Damage(damage, ElementType.Neutral, null, type)
                })
            });
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(skill, "_resourceType", SkillResourceType.None);

            var did = new DefinitionId(id);
            if (!_learned.Knows(did)) _learned.Learn(did);
            return skill;
        }

        private CombatActionResult Cast(string id, float recovery = 0f)
        {
            return _runner.RequestSkill(
                new SkillUseRequest(_actor, new DefinitionId(id), _target),
                new SkillUseContext(Skills, _learned, 10, _cooldowns),
                new SkillExecutionRules(new DefinitionId(Def), new DefinitionId(Mdef), 0),
                recovery);
        }

        // ---------------- the contract itself ----------------

        [Test]
        public void Building_an_event_mutates_nothing()
        {
            Actors();
            Author("s", 0f, 0f, 30, DamageType.Physical);

            var action = Cast("s").Action;
            int healthAfterExecution = _target.CurrentHealth;
            int writes = _target.ApplyCallCountOrZero();

            for (int i = 0; i < 100; i++)
            {
                CombatPresentationEvent.Started(action);
                CombatPresentationEvent.Executed(action);
                CombatPresentationEvent.Completed(action);
                CombatPresentationEvent.Cancelled(action);
            }

            Assert.That(_target.CurrentHealth, Is.EqualTo(healthAfterExecution),
                "Describing what happened must not make it happen again.");
            Assert.That(_target.ApplyCallCountOrZero(), Is.EqualTo(writes));
        }

        [Test]
        public void An_event_carries_identities_not_live_combatants()
        {
            Actors();
            Author("s", 0f, 0f, 30, DamageType.Physical);
            var action = Cast("s").Action;

            CombatPresentationEvent e = CombatPresentationEvent.Executed(action);

            Assert.That(e.ActorId, Is.EqualTo(_actor.CombatantId));
            Assert.That(e.TargetId, Is.EqualTo(_target.CombatantId));

            // Nothing on the struct exposes an ICombatant to reach through.
            var fields = typeof(CombatPresentationEvent).GetProperties();
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.That(typeof(ICombatant).IsAssignableFrom(fields[i].PropertyType), Is.False,
                    "Property '" + fields[i].Name + "' would let a presenter mutate a fight.");
            }
        }

        [Test]
        public void Executed_copies_the_gameplay_numbers_rather_than_recomputing_them()
        {
            Actors();
            _target.WithStat(Def, 12);
            Author("s", 0f, 0f, 50, DamageType.Physical);

            var action = Cast("s").Action;
            CombatPresentationEvent e = CombatPresentationEvent.Executed(action);

            Assert.That(e.Amount, Is.EqualTo(38), "50 - 12, as the executor computed it.");
            Assert.That(e.TargetHealthBefore, Is.EqualTo(1000));
            Assert.That(e.TargetHealthAfter, Is.EqualTo(962));
            Assert.That(e.HealthChange, Is.EqualTo(-38));
            Assert.That(e.IsHeal, Is.False);
        }

        // ---------------- STEP 6: damage type reaches presentation ----------------

        [Test]
        public void A_physical_skill_reports_physical_and_a_magic_skill_reports_magic()
        {
            Actors();
            Author("phys", 0f, 0f, 30, DamageType.Physical);
            var physEvent = CombatPresentationEvent.Executed(Cast("phys").Action);

            Actors();
            Author("mag", 0f, 0f, 30, DamageType.Magic);
            var magEvent = CombatPresentationEvent.Executed(Cast("mag").Action);

            Assert.That(physEvent.DamageType, Is.EqualTo(DamageType.Physical));
            Assert.That(magEvent.DamageType, Is.EqualTo(DamageType.Magic),
                "Presentation can pick a spell impact without recomputing anything.");
        }

        [Test]
        public void The_damage_type_is_carried_on_the_effect_outcome_itself()
        {
            Actors();
            Author("mag", 0f, 0f, 30, DamageType.Magic);

            SkillExecutionResult result = Cast("mag").Action.SkillResult;

            Assert.That(result.Effects[0].DamageType, Is.EqualTo(DamageType.Magic));
            Assert.That(result.Effects[0].Amount, Is.EqualTo(30),
                "Carrying the type changed no calculation.");
        }

        [Test]
        public void A_basic_attack_reports_physical()
        {
            Actors();

            var action = _runner.RequestBasicAttack(_target,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 100f),
                0f, 0f).Action;

            var e = CombatPresentationEvent.Executed(action);

            Assert.That(e.ActionType, Is.EqualTo(CombatActionType.BasicAttack));
            Assert.That(e.DamageType, Is.EqualTo(DamageType.Physical));
            Assert.That(e.Amount, Is.EqualTo(40));
        }

        [Test]
        public void A_heal_reports_a_positive_health_change()
        {
            Actors();
            _target.ApplyHealthDelta(-500);

            // Heal the enemy: the point is the sign of the change, not the targeting rule.
            var skill = AddSkill("heal", maxLevel: 1, range: 0f, levels: new[]
            {
                Level(1, 1, 0f, 0f, new[] { SkillEffect.Heal(120, SkillResourceType.Health) })
            });
            SetPrivate(skill, "_targetType", SkillTargetType.SingleEnemy);
            SetPrivate(skill, "_resourceType", SkillResourceType.None);
            _learned.Learn(new DefinitionId("heal"));

            var e = CombatPresentationEvent.Executed(Cast("heal").Action);

            Assert.That(e.HealthChange, Is.EqualTo(120));
            Assert.That(e.IsHeal, Is.True);
        }

        // ---------------- STEP 18: one execution, one report ----------------

        [Test]
        public void A_skill_whose_effects_were_all_unsupported_reports_no_health_change()
        {
            Actors();
            Author("status", 0f, 0f, 0, DamageType.Physical, new[]
            {
                SkillEffect.ApplyStatusEffect(new DefinitionId("status.stun"))
            });

            var action = Cast("status").Action;
            var e = CombatPresentationEvent.Executed(action);

            Assert.That(action.SkillResult.IsExecuted, Is.True);
            Assert.That(e.HealthChange, Is.EqualTo(0),
                "Nobody was struck, so a hit reaction must not fire.");
            Assert.That(_target.CurrentHealth, Is.EqualTo(1000));
        }

        [Test]
        public void Death_is_reported_from_the_gameplay_result()
        {
            Actors(targetHealth: 20);
            Author("s", 0f, 0f, 30, DamageType.Physical);

            var e = CombatPresentationEvent.Executed(Cast("s").Action);

            Assert.That(e.TargetDied, Is.True);
            Assert.That(e.TargetHealthAfter, Is.EqualTo(0));
            Assert.That(CombatPresentationEvent.Death(e).Kind,
                Is.EqualTo(CombatPresentationEventKind.Death));
        }

        [Test]
        public void Cancellation_and_rejection_carry_their_reasons()
        {
            Actors();
            Author("s", 2f, 0f, 30, DamageType.Physical);

            var action = Cast("s").Action;
            _runner.Cancel(CombatActionCancelReason.TargetInvalidated);

            var cancelled = CombatPresentationEvent.Cancelled(action);
            Assert.That(cancelled.Kind, Is.EqualTo(CombatPresentationEventKind.Cancelled));
            Assert.That(cancelled.CancelReason,
                Is.EqualTo(CombatActionCancelReason.TargetInvalidated));

            CombatActionResult refused = Cast("nope");
            var rejected = CombatPresentationEvent.Rejected(
                CombatActionType.Skill, _actor.CombatantId, _target.CombatantId,
                new DefinitionId("nope"), refused);

            Assert.That(rejected.Kind, Is.EqualTo(CombatPresentationEventKind.Rejected));
            Assert.That(rejected.SkillRejection, Is.EqualTo(SkillUseRejection.UnknownSkill));
        }

        // ---------------- STEP 17: gameplay independence ----------------

        [Test]
        public void Publishing_no_events_at_all_gives_the_same_gameplay_outcome()
        {
            // Run a scenario twice. The first ignores presentation entirely; the second
            // builds every event at every step. The gameplay numbers must match exactly.
            int[] silent = RunScenario(publish: false);
            int[] loud = RunScenario(publish: true);

            Assert.That(loud, Is.EqualTo(silent),
                "Presentation must not be able to change health, mana, cooldown or death.");
        }

        /// <summary>Runs a fixed fight and returns the gameplay figures that matter.</summary>
        private int[] RunScenario(bool publish)
        {
            TearDown();
            SetUp();
            Actors(targetHealth: 200);
            Author("s", 1f, 3f, 45, DamageType.Magic);

            CombatActionResult started = Cast("s", recovery: 0.5f);
            if (publish && started.IsAccepted) CombatPresentationEvent.Started(started.Action);

            for (int i = 0; i < 20; i++)
            {
                _runner.Advance(0.1f);

                if (!publish || _runner.Current == null) continue;

                CombatPresentationEvent.Started(_runner.Current);
                if (_runner.Current.HasExecuted)
                {
                    var e = CombatPresentationEvent.Executed(_runner.Current);
                    CombatPresentationEvent.Hit(e, e.DamageType);
                    if (e.TargetDied) CombatPresentationEvent.Death(e);
                }
            }

            CombatActionResult blocked = Cast("s");
            if (publish)
            {
                CombatPresentationEvent.Rejected(CombatActionType.Skill,
                    _actor.CombatantId, _target.CombatantId, new DefinitionId("s"), blocked);
            }

            return new[]
            {
                _target.CurrentHealth,
                _actor.CurrentHealth,
                _actor.CurrentMana,
                _cooldowns.IsReady(new DefinitionId("s")) ? 1 : 0,
                _target.IsAlive() ? 1 : 0,
                blocked.IsAccepted ? 1 : 0,
                (int)_runner.Phase
            };
        }
    }

    /// <summary>Small helper so the tests can read a write count off either fake.</summary>
    internal static class FakeCombatantProbe
    {
        public static int ApplyCallCountOrZero(this FakePooledCombatant c)
        {
            return c == null ? 0 : c.WriteCount;
        }
    }
}
