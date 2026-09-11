using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Client.World;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEditor;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Every quest the game ships can actually be finished.
    /// </summary>
    /// <remarks>
    /// <b>The trap this exists for.</b> <c>QuestObjectiveType</c> names six kinds of
    /// objective and the world server advances exactly one of them: a kill. Authoring
    /// "collect five slime gels" would compile, validate, appear in the journal, be
    /// acceptable, and then sit at 0 / 5 forever -- because nothing on the authoritative side
    /// ever reports an item being picked up. A quest that cannot be finished is worse than a
    /// quest that does not exist: it occupies the log permanently and there is no way for a
    /// player to get rid of it.
    ///
    /// So the rule is written down here rather than in somebody's memory. When the server
    /// learns to report a second kind of progress, this list grows by one line and the
    /// content is free to use it.
    /// </remarks>
    [TestFixture]
    public sealed class QuestContentTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        /// <summary>
        /// The objective kinds <c>CharacterQuestAuthority</c> can actually advance.
        /// </summary>
        /// <remarks>One entry, and it is not an oversight: <c>ReportKill</c> is the only door
        /// progress comes through on the server. The client-side reporters for items and
        /// conversations exist, but the server owns the log and overwrites whatever a client
        /// believes -- so a quest resting on them would show progress that vanished on the
        /// next publish.</remarks>
        private static readonly QuestObjectiveType[] Advanceable =
        {
            QuestObjectiveType.KillMonster
        };

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, CataloguePath + " is missing");

            return catalogue;
        }

        [Test]
        public void EveryAuthoredQuestUsesAnObjectiveTheServerCanAdvance()
        {
            var stuck = new List<string>();

            foreach (QuestDefinition quest in Catalogue().BuildQuests().All)
            {
                foreach (QuestObjective objective in quest.Objectives)
                {
                    if (System.Array.IndexOf(Advanceable, objective.Type) >= 0) continue;

                    stuck.Add(quest.Id.Value + " needs " + objective.Type
                        + ", which nothing on the server reports");
                }
            }

            Assert.That(stuck, Is.Empty,
                "these quests could be taken and never finished: " + string.Join("; ", stuck));
        }

        [Test]
        public void EveryQuestAsksForAMonsterTheWorldActuallyContains()
        {
            // A quest to kill something that is not in the game is unfinishable in exactly
            // the same way, and reads to a player as a monster they cannot find.
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<MonsterDefinition> monsters = catalogue.BuildMonsters();

            var missing = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                foreach (QuestObjective objective in quest.Objectives)
                {
                    if (objective.Type != QuestObjectiveType.KillMonster) continue;

                    MonsterDefinition monster;

                    if (!monsters.TryGet(objective.Target, out monster) || monster == null)
                    {
                        missing.Add(quest.Id.Value + " -> " + objective.Target.Value);
                    }
                }
            }

            Assert.That(missing, Is.Empty,
                "no such monster: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryRewardNamesSomethingThatExists()
        {
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<ItemDefinition> items = catalogue.BuildItems();

            var missing = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                foreach (QuestReward reward in quest.Rewards)
                {
                    if (reward.Type != QuestRewardType.Item) continue;

                    ItemDefinition item;

                    if (!items.TryGet(reward.Target, out item) || item == null)
                    {
                        missing.Add(quest.Id.Value + " pays " + reward.Target.Value);
                    }
                }
            }

            Assert.That(missing, Is.Empty,
                "a quest promises an item nobody can receive: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryPrerequisiteNamesAQuestThatShips()
        {
            // A prerequisite nothing satisfies locks the quest away forever, silently -- the
            // journal simply never offers it and there is nothing on screen to explain why.
            WorldContentCatalogue catalogue = Catalogue();

            var known = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                known.Add(quest.Id.Value);
            }

            var dangling = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                foreach (DefinitionId prerequisite in quest.PrerequisiteQuests)
                {
                    if (!prerequisite.IsValid || known.Contains(prerequisite.Value)) continue;

                    dangling.Add(quest.Id.Value + " waits on " + prerequisite.Value);
                }
            }

            Assert.That(dangling, Is.Empty,
                "unreachable quests: " + string.Join(", ", dangling));
        }

        [Test]
        public void SomethingIsAlwaysAvailableToTake()
        {
            // The report this answers: a player who had finished the only quest in the game
            // opened the journal to "nothing here" and read it as the quest system being
            // broken. Something must always be takeable.
            //
            // It used to be one endlessly repeatable quest, which solved the empty journal
            // and created a level-one errand that a level-thirty player still had to read
            // past. Dailies replaced it: they come back tomorrow, and they retire. So what
            // this asserts is the property, not the mechanism.
            var comesBack = new List<string>();

            foreach (QuestDefinition quest in Catalogue().BuildQuests().All)
            {
                if (quest.ResetsDaily || quest.Repeatable) comesBack.Add(quest.Id.Value);
            }

            Assert.That(comesBack, Is.Not.Empty,
                "finishing every quest leaves a player with an empty journal and no way to "
                + "tell that apart from a bug");
        }

        [Test]
        public void TheJournalAnswersAgainstThePlayersRealLevel()
        {
            // The bug: QuestJournal.UseLevel existed and nothing ever called it, so the
            // availability rules ran at level 1 forever. Every level-gated quest was
            // invisible in the journal and unmarked above its giver, while the server would
            // have accepted it -- a quest that exists, is reachable, and cannot be found.
            //
            // Asserted as "the journal reads the level from somewhere" rather than by driving
            // a networked player: what must not come back is the constant.
            System.Reflection.MethodInfo update = typeof(ChibiFantasy.Client.World.QuestJournal)
                .GetMethod("Update", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);

            Assert.That(update, Is.Not.Null,
                "nothing keeps the journal's level up to date, so level-gated quests will "
                + "never be offered");
        }

        [Test]
        public void TheLevelGatedQuestsAreReachableByTheLevelsTheyAskFor()
        {
            // A level requirement above anything the content can reach would be a quest
            // authored into a locked room.
            var tooHigh = new List<string>();

            foreach (QuestDefinition quest in Catalogue().BuildQuests().All)
            {
                if (quest.LevelRequirement > 60) tooHigh.Add(quest.Id.Value);
            }

            Assert.That(tooHigh, Is.Empty,
                "these ask for a level beyond the progression curve: "
                + string.Join(", ", tooHigh));
        }

        [Test]
        public void EveryQuestIsOfferedBySomebody()
        {
            // A quest in the catalogue that no NPC hands out can be seen in the journal and
            // never accepted: taking one requires standing in front of a giver, and the panel
            // would tell the player to find somebody who does not exist.
            WorldContentCatalogue catalogue = Catalogue();

            var offered = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (NPCDefinition npc in catalogue.BuildNpcs().All)
            {
                if (!npc.HasRole(NpcRole.Quest)) continue;

                foreach (DefinitionId quest in npc.Quests) offered.Add(quest.Value);
            }

            var orphaned = new List<string>();

            foreach (QuestDefinition quest in catalogue.BuildQuests().All)
            {
                if (!offered.Contains(quest.Id.Value)) orphaned.Add(quest.Id.Value);
            }

            Assert.That(orphaned, Is.Empty,
                "nobody hands these out: " + string.Join(", ", orphaned));
        }

        [Test]
        public void AQuestGiverOnlyOffersQuestsThatExist()
        {
            WorldContentCatalogue catalogue = Catalogue();

            IDefinitionRegistry<QuestDefinition> quests = catalogue.BuildQuests();

            var missing = new List<string>();

            foreach (NPCDefinition npc in catalogue.BuildNpcs().All)
            {
                foreach (DefinitionId quest in npc.Quests)
                {
                    QuestDefinition found;

                    if (!quests.TryGet(quest, out found) || found == null)
                    {
                        missing.Add(npc.Id.Value + " offers " + quest.Value);
                    }
                }
            }

            Assert.That(missing, Is.Empty,
                "a giver names a quest that does not ship: " + string.Join(", ", missing));
        }
    }
}
