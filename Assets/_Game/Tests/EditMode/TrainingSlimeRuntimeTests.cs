using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The Training Slime as the first production monster, end to end through the data.
    /// </summary>
    /// <remarks>
    /// <b>What this suite is for.</b> Every mechanism the slime relies on -- spawning,
    /// aggression, leashing, the defeat claim, drops, experience, quest credit -- was built
    /// and tested in earlier phases against fixture monsters authored inside the tests. That
    /// is the right way to test a mechanism, and it proves nothing about whether the actual
    /// shipped slime is wired to any of it. A monster with no experience reward, no drop
    /// table, no allowed map or no model resolves through all of those mechanisms perfectly
    /// and is still unplayable.
    ///
    /// So these read the real content and the real catalogue. They are content tests, and
    /// they fail when somebody edits an asset rather than when somebody edits a class --
    /// which is exactly when the playable loop breaks.
    ///
    /// <b>They assert the loop, not the numbers.</b> "Experience is more than nothing" and
    /// "the drop table resolves" hold; "experience is twelve" does not, because that is
    /// balance and balance is meant to move.
    /// </remarks>
    [TestFixture]
    public sealed class TrainingSlimeRuntimeTests
    {
        private const string Slime = "monster.training_slime";
        private const string Harbor = "map.harbor_town";
        private const string FirstHunt = "quest.harbor_first_hunt";

        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private const string VisualsPath =
            "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset";

        private static WorldContentCatalogue Content()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, CataloguePath + " is missing");

            return catalogue;
        }

        private static MonsterDefinition Definition()
        {
            Assert.That(Content().BuildMonsters().TryGet(new DefinitionId(Slime),
                out MonsterDefinition slime), Is.True, Slime + " is not in the catalogue");

            return slime;
        }

        private static MonsterVisualCatalogue Visuals()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(VisualsPath);

            Assert.That(catalogue, Is.Not.Null, VisualsPath + " is missing");

            return catalogue;
        }

        // ---- the model a player sees ----------------------------------------------------

        [Test]
        public void TheApprovedModelResolvesFromTheDefinitionId()
        {
            // Keyed by content, never by GameObject name. A monster added to the catalogue
            // is drawn without a line of code changing, and one that is not is left to the
            // development placeholder rather than drawn as something else.
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            Assert.That(prefab, Is.Not.Null,
                Slime + " resolves to no model, so a player sees the development capsule");

            Assert.That(prefab.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null,
                "the model has no skinned mesh, so nothing would be drawn or animated");
        }

        [Test]
        public void TheModelCarriesTheFiveAnimationsTheRuntimeDrives()
        {
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            var animator = prefab.GetComponentInChildren<Animator>(true);

            Assert.That(animator, Is.Not.Null, "no Animator, so no idle, hop, hit or death");
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null,
                "no controller, so the Animator has nothing to play");

            var clips = new System.Collections.Generic.HashSet<string>();

            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null) clips.Add(clip.name);
            }

            foreach (string wanted in new[]
                { "Slime_Idle", "Slime_Move", "Slime_Attack", "Slime_Hit", "Slime_Death" })
            {
                Assert.That(clips, Does.Contain(wanted), "the controller cannot play " + wanted);
            }
        }

        [Test]
        public void TheNameplateHangsAboveTheModelRatherThanInsideIt()
        {
            GameObject prefab = Visuals().PrefabFor(new DefinitionId(Slime));

            float plate = Visuals().NameplateHeightFor(new DefinitionId(Slime));

            // Instantiated rather than measured on the asset: renderer bounds on an
            // un-instantiated prefab are whatever was last serialised, which is not a
            // measurement of anything.
            GameObject instance = Object.Instantiate(prefab);

            try
            {
                instance.transform.position = Vector3.zero;

                var renderers = instance.GetComponentsInChildren<Renderer>(true);

                Assert.That(renderers.Length, Is.GreaterThan(0), "fixture");

                Bounds bounds = renderers[0].bounds;

                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                Assert.That(plate - WorldMonsterPresenter.BarDrop,
                    Is.GreaterThan(bounds.max.y),
                    "the health bar hangs below the name, and at this height it would be "
                    + "drawn inside the slime");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ThePlaceholderStandsAsideForAMonsterThatHasArt()
        {
            // Otherwise a development build draws a capsule standing inside the slime, and
            // the two together look like a rendering fault rather than like progress.
            var host = new GameObject("presenter");

            try
            {
                var presenter = host.AddComponent<WorldMonsterPresenter>();

                presenter.Compose(null, Visuals(), null, null, null);

                Assert.That(presenter.Draws(new DefinitionId(Slime)), Is.True);
                Assert.That(presenter.Draws(new DefinitionId("monster.nothing_authored")),
                    Is.False, "it must not claim a monster it has no model for");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheProductionPresenterIsNotCompiledOutOfAPlayersBuild()
        {
            // The development visualizer is wrapped in a DEVELOPMENT_BUILD guard on purpose.
            // The production presenter must not be, or a shipped client draws no monsters at
            // all -- which is the failure this whole gate exists to avoid.
            //
            // It does contain a guarded block, around the development click marker it also
            // attaches. What matters is that the class itself is outside every guard, so
            // the position compared is the declaration against the first directive.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldMonsterPresenter.cs");

            int declared = source.IndexOf("public sealed class WorldMonsterPresenter",
                System.StringComparison.Ordinal);
            int guarded = source.IndexOf("#if ", System.StringComparison.Ordinal);

            Assert.That(declared, Is.GreaterThanOrEqualTo(0), "the presenter was renamed");
            Assert.That(guarded < 0 || guarded > declared, Is.True,
                "the production monster presenter is behind a compilation guard, so a "
                + "player's build would draw no monsters at all");

            string placeholder = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/DevelopmentMonsterVisualizer.cs");

            Assert.That(placeholder, Does.Contain("#if DEVELOPMENT_BUILD"),
                "the placeholder capsule would ship to players");
        }

        [Test]
        public void TheModelIsHungOnTheMonsterItselfSoAShippedClientCanClickIt()
        {
            // Targeting resolves a collider with GetComponentInParent<MonsterNetworkEntity>.
            // Parenting the model anywhere else leaves only the development marker to find,
            // and a player's build does not compile that -- so a shipped client could see a
            // slime and never be able to attack one.
            string presenter = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldMonsterPresenter.cs");

            Assert.That(presenter, Does.Contain("Instantiate(prefab, monster.transform)"),
                "the model is not parented to the monster, so a click cannot find it");

            string input = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldCombatInput.cs");

            Assert.That(input, Does.Contain("GetComponentInParent<MonsterNetworkEntity>"),
                "targeting no longer resolves a monster from the collider's parents");
        }

        // ---- the data the loop runs on --------------------------------------------------

        [Test]
        public void TheSlimeIsAuthoredForTheMapItIsSpawnedOn()
        {
            MonsterDefinition slime = Definition();

            Assert.That(MonsterSpawnPlacement.IsMapAllowed(slime, new DefinitionId(Harbor)),
                Is.True, "every camp on " + Harbor + " would refuse to spawn it");
        }

        [Test]
        public void TheSlimeCanBeFought()
        {
            MonsterDefinition slime = Definition();

            Assert.That(slime.TryGetStat(Content().MaxHealthStat, out int health), Is.True,
                "no authored maximum health, so it would spawn already dead");
            Assert.That(health, Is.GreaterThan(0));

            Assert.That(slime.AttackRange, Is.GreaterThan(0f), "it could never reach anybody");
            Assert.That(slime.MoveSpeed, Is.GreaterThan(0f), "it could never follow anybody");
            Assert.That(slime.LeashRange, Is.GreaterThan(0f),
                "with no leash it would follow a player across the whole map");
            Assert.That(slime.DetectionRange, Is.GreaterThan(0f),
                "it could never notice the player who hit it");
        }

        [Test]
        public void TheSlimeFightsBackButDoesNotJumpAPasserBy()
        {
            // A training monster: safe to walk past, real once you swing at it. Passive
            // would never fight back at all, and aggressive would ambush a level-one
            // player walking to the next camp.
            Assert.That(Definition().AggressionType,
                Is.EqualTo(MonsterAggressionType.Defensive));
        }

        [Test]
        public void KillingOneIsWorthSomething()
        {
            MonsterDefinition slime = Definition();

            Assert.That(slime.ExperienceReward, Is.GreaterThan(0),
                "a monster a quest sends a player to kill three of pays no experience");
        }

        [Test]
        public void TheDropTableItNamesActuallyExists()
        {
            MonsterDefinition slime = Definition();

            Assert.That(slime.LootTable.IsValid, Is.True, "no drop table at all");

            Assert.That(Content().BuildDropTables().TryGet(slime.LootTable,
                out DropTableDefinition table), Is.True,
                "the slime names a drop table that is not in the catalogue: " + slime.LootTable);

            Assert.That(table.Entries.Length, Is.GreaterThan(0), "the table drops nothing");
        }

        [Test]
        public void ItComesBackAfterItIsKilled()
        {
            // Authored on the monster; a camp may override it. Without either, a cleared
            // camp stays cleared for the life of the server.
            Assert.That(Definition().Respawn.RespawnDelaySeconds, Is.GreaterThan(0f),
                "no authored respawn delay, so a camp emptied once never refills");
        }

        // ---- the quest the slime exists for ---------------------------------------------

        [Test]
        public void TheFirstHuntSendsThePlayerAfterThisSlime()
        {
            Assert.That(Content().BuildQuests().TryGet(new DefinitionId(FirstHunt),
                out QuestDefinition quest), Is.True, FirstHunt + " is missing");

            Assert.That(KillsRequired(quest), Is.GreaterThan(0),
                "the starter quest no longer asks for training slimes, so killing one "
                + "advances nothing");
        }

        [Test]
        public void EachSlimeKilledCountsOnceAndTheLastOneFinishesTheQuest()
        {
            // The whole reported loop, against the real quest and the real monster id:
            // kill, kill, kill, ready to hand in. Server-side throughout -- this is the
            // service the kill hook calls, not anything a client says.
            Assert.That(Content().BuildQuests().TryGet(new DefinitionId(FirstHunt),
                out QuestDefinition quest), Is.True, "fixture");

            var quests = new DefinitionRegistry<QuestDefinition>();

            quests.Register(quest);

            var context = new QuestService.Context(quests);
            var state = new CharacterQuestState(new CharacterId("char-hunter"));

            Assert.That(QuestService.TryAccept(state, new DefinitionId(FirstHunt), context)
                .IsAccepted, Is.True, "fixture");

            int required = KillsRequired(quest);
            int index = KillObjectiveIndex(quest);

            for (var killed = 1; killed <= required; killed++)
            {
                QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                    new DefinitionId(Slime), 1, context);

                Assert.That(state.TryGet(new DefinitionId(FirstHunt),
                    out QuestProgress progress), Is.True);

                Assert.That(progress.CountAt(index), Is.EqualTo(killed),
                    "kill " + killed + " did not count exactly once");

                bool finished = killed == required;

                Assert.That(progress.Status == QuestStatus.ReadyToComplete, Is.EqualTo(finished),
                    finished
                        ? required + " kills and it is still not ready to hand in"
                        : "it was ready to hand in after only " + killed);
            }
        }

        private static int KillsRequired(QuestDefinition quest)
        {
            foreach (QuestObjective objective in quest.Objectives)
            {
                if (objective.Type == QuestObjectiveType.KillMonster
                    && objective.Target.Value == Slime)
                {
                    return objective.RequiredAmount;
                }
            }

            return 0;
        }

        private static int KillObjectiveIndex(QuestDefinition quest)
        {
            for (var i = 0; i < quest.Objectives.Length; i++)
            {
                if (quest.Objectives[i].Type == QuestObjectiveType.KillMonster
                    && quest.Objectives[i].Target.Value == Slime)
                {
                    return i;
                }
            }

            return -1;
        }

        [Test]
        public void KillingSomethingElseDoesNotAdvanceIt()
        {
            Assert.That(Content().BuildQuests().TryGet(new DefinitionId(FirstHunt),
                out QuestDefinition quest), Is.True, "fixture");

            var quests = new DefinitionRegistry<QuestDefinition>();

            quests.Register(quest);

            var context = new QuestService.Context(quests);
            var state = new CharacterQuestState(new CharacterId("char-hunter"));

            QuestService.TryAccept(state, new DefinitionId(FirstHunt), context);

            QuestService.ReportProgress(state, QuestObjectiveType.KillMonster,
                new DefinitionId("monster.something_else"), 5, context);

            Assert.That(state.TryGet(new DefinitionId(FirstHunt), out QuestProgress progress),
                Is.True);
            Assert.That(progress.CountAt(KillObjectiveIndex(quest)), Is.Zero);
        }
    }
}
