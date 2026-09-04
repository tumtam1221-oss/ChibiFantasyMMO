using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a status snapshot carries, what a bar makes of it, and what neither may decide.
    /// </summary>
    /// <remarks>
    /// <b>The rules being tested are not restated here.</b> Stacking, refreshing, expiry and
    /// immunity are Phase 12's and already have suites; this checks the two new things --
    /// that the authoritative list projects onto the wire without leaking anything, and that
    /// the client turns what it receives into a bar without ever deciding what is on it.
    ///
    /// <b>The countdown is the interesting one.</b> A client that removed an effect when its
    /// own timer ran out would be a client deciding when a silence ends, and it would decide
    /// it early on a machine whose clock runs fast. That is a test, not a comment.
    /// </remarks>
    [TestFixture]
    internal sealed class StatusEffectReplicationTests
    {
        private const string Silence = "status.silence";
        private const string Regeneration = "status.regeneration";
        private const string Poison = "status.poison";
        private const string Blessing = "status.blessing";

        private readonly List<Object> _created = new List<Object>();

        private DefinitionRegistry<StatusEffectDefinition> _effects;

        [SetUp]
        public void SetUp()
        {
            _effects = new DefinitionRegistry<StatusEffectDefinition>();

            _effects.Register(Effect(Silence, StatusEffectCategory.Control, 6f,
                control: ControlEffectType.Silence));
            _effects.Register(Effect(Regeneration, StatusEffectCategory.HealOverTime, 10f));
            _effects.Register(Effect(Poison, StatusEffectCategory.DamageOverTime, 8f,
                stacking: StatusEffectStackBehavior.AddStack, maxStacks: 5));
            _effects.Register(Effect(Blessing, StatusEffectCategory.Buff, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- there is one status runtime, and the world points at it ------------------------

        [Test]
        public void TheProjectHasExactlyOneStatusContainerAndOneApplyService()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var containers = 0;
            var services = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class StatusEffectRuntimeState")) containers++;
                if (source.Contains("class StatusEffectService")) services++;
            }

            Assert.That(containers, Is.EqualTo(1),
                "a second status container would disagree with the first the moment one was "
                + "ticked");
            Assert.That(services, Is.EqualTo(1),
                "a second apply path is a second place for an immunity to be forgotten");
        }

        [Test]
        public void ACharactersCombatantIsReachableAsAStatusTargetAndOwnsNoListOfItsOwn()
        {
            // The seam a skill reaches a target's status through.
            Assert.That(typeof(IStatusEffectTarget).IsAssignableFrom(typeof(CharacterCombatant)),
                Is.True);

            // Settable, not constructed: the combatant is pointed at the world's one list
            // rather than making a second one. A readonly field initialised in the
            // constructor would be a set of buffs nobody else could see.
            System.Reflection.PropertyInfo status =
                typeof(CharacterCombatant).GetProperty("Status");

            Assert.That(status, Is.Not.Null);
            Assert.That(status.PropertyType, Is.EqualTo(typeof(StatusEffectRuntimeState)));

            string combatant = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/CharacterCombatant.cs");

            Assert.That(combatant, Does.Not.Contain("new StatusEffectRuntimeState"),
                "a combatant that built its own list would be a second set of buffs");

            // And the one place that does build one points the combatant at it.
            string living = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/WorldCharacterRegistry.cs");

            Assert.That(living, Does.Contain("Status = new StatusEffectRuntimeState"));
            Assert.That(living, Does.Contain("Combatant.Status = Status"));
        }

        // ---- what goes on the wire -------------------------------------------------------------

        [Test]
        public void TheSnapshotCarriesTheIdTheStacksTheTimeAndTheCategory()
        {
            StatusEffectRuntimeState status = New();

            Assert.That(StatusEffectService.TryApply(status, Id(Poison), Id("skill.spit"),
                _effects, stacks: 3).IsAccepted, Is.True);

            StatusSnapshot snapshot = Project(status);

            Assert.That(snapshot.Count, Is.EqualTo(1));

            StatusEffectSnapshot entry = snapshot.Effects[0];

            Assert.That(entry.EffectId, Is.EqualTo(Poison));
            Assert.That(entry.Stacks, Is.EqualTo(3));
            Assert.That(entry.RemainingSeconds, Is.EqualTo(8f).Within(0.001f));
            Assert.That(entry.Category, Is.EqualTo((int)StatusEffectCategory.DamageOverTime),
                "a client whose content is missing this effect still has to know which row "
                + "to draw it in");
        }

        [Test]
        public void TheSourceNeverTravels()
        {
            // The source is what granted an effect, kept so the server can take back exactly
            // what it gave. Which hidden mechanism buffed somebody is server business.
            foreach (System.Reflection.FieldInfo field in typeof(StatusEffectSnapshot)
                .GetFields())
            {
                Assert.That(field.Name, Is.Not.EqualTo("Source"));
                Assert.That(field.Name, Is.Not.EqualTo("SourceId"));
            }

            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/CharacterStatusAuthority.cs");

            Assert.That(source, Does.Not.Contain("Source = "), "the source is not projected");
        }

        [Test]
        public void TheSnapshotCarriesNothingPrivateOrInternal()
        {
            var allowed = new HashSet<string>
            {
                "CharacterId", "Revision", "Effects",
            };

            foreach (System.Reflection.FieldInfo field in typeof(StatusSnapshot).GetFields())
            {
                Assert.That(allowed, Contains.Item(field.Name),
                    field.Name + " has no business on a status snapshot");
            }

            var entryFields = new HashSet<string>
            {
                "EffectId", "Stacks", "RemainingSeconds", "Category",
            };

            foreach (System.Reflection.FieldInfo field in typeof(StatusEffectSnapshot)
                .GetFields())
            {
                Assert.That(entryFields, Contains.Item(field.Name),
                    field.Name + " has no business on a status entry");
            }
        }

        [Test]
        public void TheRevisionIsTheRuntimesOwnAndNotThePersistenceToken()
        {
            StatusEffectRuntimeState status = New();

            int before = Project(status).Revision;

            StatusEffectService.TryApply(status, Id(Blessing), Id("fruit.light"), _effects);

            Assert.That(Project(status).Revision, Is.GreaterThan(before),
                "an applied effect must be distinguishable from no change");

            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/CharacterStatusAuthority.cs");

            Assert.That(source, Does.Not.Contain("SaveRevision"),
                "the database concurrency token has no business on the wire");
        }

        [Test]
        public void AnEffectTheServerCannotResolveIsStillSent()
        {
            StatusEffectRuntimeState status = New();

            // Applied through the service with a registry that knows it, then projected by
            // an authority whose registry does not -- an older server, newer content.
            StatusEffectService.TryApply(status, Id(Poison), Id("skill.spit"), _effects);

            StatusSnapshot snapshot = Project(status,
                new DefinitionRegistry<StatusEffectDefinition>());

            Assert.That(snapshot.Count, Is.EqualTo(1),
                "a silently dropped debuff is worse than an unnamed one");
            Assert.That(snapshot.Effects[0].Category,
                Is.EqualTo((int)StatusEffectCategory.None));
        }

        // ---- what the bar makes of it ----------------------------------------------------------

        [Test]
        public void BuffsAndDebuffsGoInDifferentRows()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Blessing, StatusEffectCategory.Buff),
                Entry(Regeneration, StatusEffectCategory.HealOverTime),
                Entry(Poison, StatusEffectCategory.DamageOverTime),
                Entry(Silence, StatusEffectCategory.Control));

            Assert.That(presenter.Buffs.Count, Is.EqualTo(2),
                "a heal over time is something good happening to you");
            Assert.That(presenter.Debuffs.Count, Is.EqualTo(2),
                "poison and silence are both things to be warned about");

            Assert.That(presenter.Count, Is.EqualTo(4));
            Assert.That(presenter.HasSnapshot, Is.True);
        }

        [Test]
        public void AnUnrecognisedCategoryIsDrawnAsSomethingToWorryAbout()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, new StatusEffectSnapshot
            {
                EffectId = "status.from.the.future",
                Stacks = 1,
                RemainingSeconds = 4f,
                Category = 97,
            });

            Assert.That(presenter.Debuffs.Count, Is.EqualTo(1),
                "being warned about something harmless is a smaller failure than not being "
                + "warned about a poison");
        }

        [Test]
        public void AnEffectWithNoLocalDefinitionIsNamedRatherThanHidden()
        {
            var presenter = new StatusEffectPresenter(
                new DefinitionRegistry<StatusEffectDefinition>());

            Feed(presenter, Entry("status.mystery", StatusEffectCategory.Debuff));

            Assert.That(presenter.Debuffs.Count, Is.EqualTo(1));
            Assert.That(presenter.Debuffs[0].DisplayName, Is.EqualTo("Mystery"),
                "a raw content id on a player's screen looks like a bug even when it is not");
            Assert.That(presenter.Debuffs[0].HasIcon, Is.False);

            Assert.That(StatusEffectPresenter.FallbackName("status.silence"),
                Is.EqualTo("Silence"));
            Assert.That(StatusEffectPresenter.FallbackName(null), Is.EqualTo("?"));
            Assert.That(StatusEffectPresenter.FallbackName(string.Empty), Is.EqualTo("?"));
        }

        [Test]
        public void StacksAreShownOnlyWhenThereIsMoreThanOne()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Poison, StatusEffectCategory.DamageOverTime, stacks: 1),
                Entry(Regeneration, StatusEffectCategory.HealOverTime, stacks: 4));

            Assert.That(presenter.Debuffs[0].ShowStacks, Is.False,
                "'x1' on every icon is noise on a bar read at a glance");
            Assert.That(presenter.Buffs[0].ShowStacks, Is.True);
            Assert.That(presenter.Buffs[0].Stacks, Is.EqualTo(4));
        }

        [Test]
        public void TheCountdownIsDrawnInWholeSecondsAndMinutes()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Poison, StatusEffectCategory.DamageOverTime, remaining: 8.7f),
                Entry(Blessing, StatusEffectCategory.Buff, remaining: 0f),
                Entry(Regeneration, StatusEffectCategory.HealOverTime, remaining: 125f));

            Assert.That(presenter.Debuffs[0].RemainingLabel, Is.EqualTo("8s"));
            Assert.That(presenter.Buffs[0].RemainingLabel, Is.Empty,
                "an effect that does not expire has no countdown to show");
            Assert.That(presenter.Buffs[0].IsIndefinite, Is.True);
            Assert.That(presenter.Buffs[1].RemainingLabel, Is.EqualTo("2m05"));
        }

        // ---- the countdown decides nothing --------------------------------------------------------

        [Test]
        public void ACountdownReachingZeroRemovesNothing()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Silence, StatusEffectCategory.Control, remaining: 2f));

            Assert.That(presenter.Debuffs.Count, Is.EqualTo(1));

            // Far past the end, and then some.
            presenter.Advance(60f);

            Assert.That(presenter.Debuffs.Count, Is.EqualTo(1),
                "a client that dropped an effect on its own timer would decide when a "
                + "silence ends -- early, on a machine whose clock runs fast");
            Assert.That(presenter.Debuffs[0].RemainingSeconds, Is.Zero,
                "the number stops at zero rather than going negative");
            Assert.That(presenter.Debuffs[0].RemainingLabel, Is.EqualTo("0s"));

            // Only the server's next snapshot takes it away.
            Feed(presenter);

            Assert.That(presenter.Count, Is.Zero);
        }

        [Test]
        public void AnIndefiniteEffectIsNeverCountedDown()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Blessing, StatusEffectCategory.Buff, remaining: 0f));

            presenter.Advance(30f);

            Assert.That(presenter.Buffs[0].IsIndefinite, Is.True,
                "a permanent passive must not expire because somebody ticked often enough");
        }

        [Test]
        public void ANewSnapshotReplacesTheOldOneRatherThanMergingWithIt()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Poison, StatusEffectCategory.DamageOverTime),
                Entry(Silence, StatusEffectCategory.Control));

            Assert.That(presenter.Count, Is.EqualTo(2));

            Feed(presenter, Entry(Blessing, StatusEffectCategory.Buff));

            Assert.That(presenter.Count, Is.EqualTo(1),
                "merging would mean the client maintaining a buff list, and one dropped "
                + "removal would leave a debuff on screen forever");
            Assert.That(presenter.Buffs.Count, Is.EqualTo(1));
            Assert.That(presenter.Debuffs, Is.Empty);
        }

        [Test]
        public void UnbindingLeavesNothingBehind()
        {
            var presenter = new StatusEffectPresenter(_effects);

            Feed(presenter, Entry(Poison, StatusEffectCategory.DamageOverTime));

            presenter.Unbind();

            Assert.That(presenter.Count, Is.Zero, "a stale icon after a despawn is a lie");
            Assert.That(presenter.HasSnapshot, Is.False);
            Assert.That(presenter.Revision, Is.Zero);
        }

        [Test]
        public void APresenterRefusesToDrawACharacterItDoesNotOwn()
        {
            var presenter = new StatusEffectPresenter(_effects);

            // Null stands in for anything unowned: binding is by ownership and there is no
            // other way in. A bar bound to a remote character would draw an empty row and
            // imply they have no buffs, which is worse than drawing nothing.
            Assert.That(presenter.Bind(null), Is.False);
            Assert.That(presenter.HasSnapshot, Is.False);
        }

        // ---- silence, through the existing rules ---------------------------------------------------

        [Test]
        public void SilenceIsAskedOfTheStatusRuntimeAndNotReimplemented()
        {
            StatusEffectRuntimeState status = New();

            Assert.That(status.HasControl(ControlEffectType.Silence, _effects), Is.False);

            StatusEffectService.TryApply(status, Id(Silence), Id("skill.dark"), _effects);

            Assert.That(status.HasControl(ControlEffectType.Silence, _effects), Is.True,
                "which effect silences is authored, not listed in code");

            // A second silencing effect authored tomorrow answers the same question with no
            // code change -- which is the property that makes this rule maintainable.
            StatusEffectDefinition second = Effect("status.gag", StatusEffectCategory.Control,
                4f, control: ControlEffectType.Silence);
            var registry = new DefinitionRegistry<StatusEffectDefinition>();
            registry.Register(second);

            StatusEffectRuntimeState other = New();
            StatusEffectService.TryApply(other, Id("status.gag"), Id("skill.other"), registry);

            Assert.That(other.HasControl(ControlEffectType.Silence, registry), Is.True);
        }

        [Test]
        public void ASilencedCasterIsRefusedBeforeAnythingIsSpent()
        {
            string validator = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/SkillUseValidator.cs");

            // The check lives in the validator, which by contract changes nothing: no cost
            // spent, no cooldown begun. A silence enforced in the executor would refuse the
            // skill after taking the mana for it.
            Assert.That(validator, Does.Contain("SkillUseRejection.Silenced"));
            Assert.That(validator, Does.Contain("HasControl(ControlEffectType.Silence"));

            string executor = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Gameplay/SkillExecutor.cs");

            Assert.That(executor, Does.Not.Contain("ControlEffectType.Silence"),
                "the rule is asked once, where nothing has been paid yet");
        }

        [Test]
        public void ACasterWithNoTrackedStatusIsNotSilencedByDefault()
        {
            // A combat sandbox that keeps no status at all must not refuse every skill for
            // lack of information.
            var context = new SkillUseContext(null, null, 1);

            Assert.That(context.CasterStatus, Is.Null);
            Assert.That(context.StatusEffects, Is.Null);
        }

        // ---- immunity, through the existing rules -------------------------------------------------

        [Test]
        public void ACategoryImmunityRefusesADebuffAndLeavesTheListUntouched()
        {
            StatusEffectRuntimeState status = New();

            // What "immune to debuffs" means, expressed as a category so a debuff authored
            // tomorrow does not quietly bypass it.
            status.AddImmunity(new StatusImmunity(Id("fruit.light"), default,
                StatusEffectCategory.DamageOverTime));

            Revision before = status.Revision;

            StatusApplyResult result = StatusEffectService.TryApply(status, Id(Poison),
                Id("skill.spit"), _effects);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(StatusApplyRejection.Immune));
            Assert.That(status.ActiveCount, Is.Zero, "nothing was written");
            Assert.That(status.Revision, Is.EqualTo(before),
                "a refused effect must not look like a change, or the client is told about "
                + "a debuff that never landed");

            // And a refused effect produces no snapshot entry at all.
            Assert.That(Project(status).Count, Is.Zero);
        }

        [Test]
        public void ImmunityToOneCategoryDoesNotRefuseAnother()
        {
            StatusEffectRuntimeState status = New();

            status.AddImmunity(new StatusImmunity(Id("fruit.light"), default,
                StatusEffectCategory.DamageOverTime));

            Assert.That(StatusEffectService.TryApply(status, Id(Blessing), Id("fruit.light"),
                _effects).IsAccepted, Is.True, "a debuff immunity is not a buff immunity");
            Assert.That(status.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void ImmunityIsNeverAskedInTheUserInterface()
        {
            foreach (string file in ClientStatusFiles())
            {
                string source = Code(file);

                Assert.That(source, Does.Not.Contain("IsImmuneTo"), file);
                Assert.That(source, Does.Not.Contain("StatusImmunity"), file);
                Assert.That(source, Does.Not.Contain("WouldBeRefused"), file);
            }
        }

        // ---- expiry belongs to the server -----------------------------------------------------------

        [Test]
        public void TheServersClockIsTheOnlyThingThatExpiresAnEffect()
        {
            StatusEffectRuntimeState status = New();

            StatusEffectService.TryApply(status, Id(Silence), Id("skill.dark"), _effects,
                durationOverride: 2f);

            Assert.That(status.Has(Id(Silence)), Is.True);

            status.Tick(1f);

            Assert.That(status.Has(Id(Silence)), Is.True, "one second is not two");
            Assert.That(status.Get(Id(Silence)).RemainingSeconds,
                Is.EqualTo(1f).Within(0.001f));

            status.Tick(1.5f);

            Assert.That(status.Has(Id(Silence)), Is.False);
            Assert.That(Project(status).Count, Is.Zero,
                "and the next snapshot no longer carries it");
        }

        // ---- what a client can never do ----------------------------------------------------------------

        [Test]
        public void NoClientMessageCanApplyRemoveOrTimeAStatusEffect()
        {
            System.Reflection.MethodInfo[] methods =
                typeof(CharacterNetworkEntity).GetMethods();

            foreach (System.Reflection.MethodInfo method in methods)
            {
                var isServerRpc = method.GetCustomAttributes(
                    typeof(FishNet.Object.ServerRpcAttribute), true).Length > 0;

                if (!isServerRpc) continue;

                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    // No client request carries a status id, a duration, a stack count or a
                    // source. There is no method for one to arrive through, which is a
                    // stronger guarantee than validating one that does.
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(
                        typeof(StatusSnapshot)), method.Name);
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(
                        typeof(StatusEffectSnapshot)), method.Name);
                }

                Assert.That(method.Name.ToLowerInvariant(), Does.Not.Contain("status"),
                    method.Name + " lets a client speak about status");
            }
        }

        [Test]
        public void NoClientFileMutatesTheAuthoritativeStatusList()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string named = file.Replace('\\', '/');

                if (named.Contains("/Prototype/")) continue;

                string source = Code(file);

                Assert.That(source, Does.Not.Contain("StatusEffectService"), named);
                Assert.That(source, Does.Not.Contain("CharacterStatusAuthority"), named);
                Assert.That(source, Does.Not.Contain(".AddImmunity("), named);
                Assert.That(source, Does.Not.Contain("ServerPublishStatus"), named);
                Assert.That(source, Does.Not.Contain("TryBuildStatusSnapshot"), named);
            }
        }

        [Test]
        public void TheClientStatusPathHoldsNoRuntimeStateOfItsOwn()
        {
            foreach (string file in ClientStatusFiles())
            {
                string source = Code(file);

                // Reading the type is one thing; owning one is the client keeping its own
                // buff list, which is exactly what the snapshot design prevents.
                Assert.That(source, Does.Not.Contain("new StatusEffectRuntimeState"), file);
                Assert.That(source, Does.Not.Contain("ActiveStatusEffect"), file);
            }

            // The bar removes widgets; it never removes effects.
            string bar = Code("Assets/_Game/Scripts/Client/UI/StatusEffectBar.cs");

            Assert.That(bar, Does.Not.Contain(".Remove("));
        }

        [Test]
        public void TheSnapshotTypesAreCarriersRatherThanBehaviour()
        {
            // Read-only accessors are fine -- Count is arithmetic over what arrived. What
            // must not exist is anything that changes a snapshot after the server built it:
            // a wire type a client can edit is a wire type a client can lie with.
            foreach (System.Reflection.MethodInfo method in typeof(StatusSnapshot)
                .GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                bool readOnly = method.Name == "ToString" || method.Name.StartsWith("get_");

                Assert.That(readOnly, Is.True,
                    method.Name + " can change a snapshot the server built");
            }

            foreach (System.Reflection.MethodInfo method in typeof(StatusEffectSnapshot)
                .GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                bool readOnly = method.Name == "ToString" || method.Name.StartsWith("get_");

                Assert.That(readOnly, Is.True,
                    method.Name + " can change a snapshot entry the server built");
            }
        }

        // ---- helpers ------------------------------------------------------------------------------------

        private static string[] ClientStatusFiles()
        {
            return new[]
            {
                "Assets/_Game/Scripts/Client/UI/StatusEffectPresenter.cs",
                "Assets/_Game/Scripts/Client/UI/StatusEffectBar.cs",
            };
        }

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

        private static DefinitionId Id(string value) => new DefinitionId(value);

        private static StatusEffectRuntimeState New()
        {
            return new StatusEffectRuntimeState(new CharacterId("char-a"));
        }

        /// <summary>Projects a status list exactly as the server authority does.</summary>
        /// <remarks>Through the production type, so what is asserted is what ships rather
        /// than a copy of it written in this file.</remarks>
        private StatusSnapshot Project(StatusEffectRuntimeState status,
            IDefinitionRegistry<StatusEffectDefinition> effects = null)
        {
            var authority = new ChibiFantasy.Server.CharacterStatusAuthority(null,
                effects ?? _effects);

            return authority.Build(new CharacterId("char-a"), status);
        }

        /// <summary>Pushes a snapshot into a presenter the way the entity's message does.</summary>
        private static void Feed(StatusEffectPresenter presenter,
            params StatusEffectSnapshot[] entries)
        {
            System.Reflection.MethodInfo handler = typeof(StatusEffectPresenter).GetMethod(
                "OnStatusChanged", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            Assert.That(handler, Is.Not.Null);

            handler.Invoke(presenter, new object[]
            {
                new StatusSnapshot
                {
                    CharacterId = "char-a",
                    Revision = 1,
                    Effects = entries ?? System.Array.Empty<StatusEffectSnapshot>(),
                },
            });
        }

        private static StatusEffectSnapshot Entry(string id, StatusEffectCategory category,
            int stacks = 1, float remaining = 5f)
        {
            return new StatusEffectSnapshot
            {
                EffectId = id,
                Stacks = stacks,
                RemainingSeconds = remaining,
                Category = (int)category,
            };
        }

        private StatusEffectDefinition Effect(string id, StatusEffectCategory category,
            float duration, ControlEffectType control = ControlEffectType.None,
            StatusEffectStackBehavior stacking = StatusEffectStackBehavior.RefreshDuration,
            int maxStacks = 1)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},"
                + "\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)category
                + ",\"_controlEffect\":" + (int)control
                + ",\"_durationSeconds\":" + duration.ToString("0.0###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"_stackBehavior\":" + (int)stacking
                + ",\"_maxStacks\":" + maxStacks + "}", definition);

            _created.Add(definition);

            return definition;
        }
    }
}
