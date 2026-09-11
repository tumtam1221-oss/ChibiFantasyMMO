using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Turns a client's "I'll take it" into the quest service's own answer.
    /// </summary>
    /// <remarks>
    /// <b>A door, not a second quest system.</b> Whether a quest exists, whether it is
    /// already taken, whether it was finished and cannot be repeated, whether the character
    /// is high enough level and whether its prerequisites are done were all decided by
    /// <see cref="QuestService"/> long before this existed. This adds the three things a
    /// networked player needs and a local test never did: an identity taken from the
    /// connection, a giver they must actually be standing next to, and an answer sent back
    /// to exactly one person.
    ///
    /// <b>The giver is checked, not trusted.</b> A quest that an NPC offers is taken by
    /// walking up to that NPC. The request names the giver the player believes they are
    /// talking to; this confirms the NPC really offers that quest and that the player really
    /// is within its authored reach -- through <see cref="NpcInteractionService"/>, so there
    /// is one proximity rule in the game rather than a second one that drifts. Without that
    /// check the quest list would be a way to accept every quest in the world from a bench
    /// in the town square.
    ///
    /// <b>Nothing is granted here.</b> Turn-in pays out through
    /// <see cref="QuestService.TryTurnIn"/>, which owns the experience, the items and the
    /// refusal when the bag is full. A client that could name a reward would be choosing its
    /// own.
    /// </remarks>
    public sealed class CharacterQuestAuthority : ICharacterQuestRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly IDefinitionRegistry<QuestDefinition> _quests;
        private readonly IDefinitionRegistry<ItemDefinition> _items;
        private readonly CharacterProgressionDefinition _progression;
        private readonly NpcInteractionService.Context _npcs;
        private readonly CharacterReplicationService _replication;

        public CharacterQuestAuthority(WorldCharacterRegistry characters,
            IDefinitionRegistry<QuestDefinition> quests,
            NpcInteractionService.Context npcs,
            IDefinitionRegistry<ItemDefinition> items = null,
            CharacterReplicationService replication = null,
            CharacterProgressionDefinition progression = null)
        {
            _progression = progression;
            _characters = characters;
            _quests = quests;
            _npcs = npcs;
            _items = items;
            _replication = replication;
        }

        /// <summary>How many requests were handled, accepted or not.</summary>
        public int Handled { get; private set; }

        /// <summary>What the last request decided.</summary>
        public QuestResult LastResult { get; private set; }

        /// <summary>What was last put on the wire, for a test that has no network.</summary>
        public QuestCommandSnapshot LastSnapshot { get; private set; }

        public void Submit(int connectionId, string questId, int command, string npcId,
            long sequence)
        {
            Handled++;

            QuestCommandSnapshot snapshot =
                Apply(connectionId, questId, command, npcId, sequence);

            LastSnapshot = snapshot;

            if (_characters == null
                || !_characters.TryGet(connectionId, out LivingCharacter asker))
            {
                return;
            }

            Answer(asker, snapshot);

            // The log is republished whatever the answer was. A refusal can still mean the
            // client's copy was stale -- it asked to take a quest it already had -- and
            // sending the truth is how that corrects itself rather than sticking.
            PublishLog(asker);
        }

        /// <summary>Decides the request, without a network.</summary>
        public QuestCommandSnapshot Apply(int connectionId, string questId, int command,
            string npcId, long sequence)
        {
            var quest = new DefinitionId(questId ?? string.Empty);

            if (_characters == null || _quests == null)
            {
                return Refuse(quest, command, QuestRejection.MissingContext, sequence);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter asker))
            {
                // The identity is the connection's, so nobody takes a quest for somebody else.
                return Refuse(quest, command, QuestRejection.MissingContext, sequence);
            }

            if (!System.Enum.IsDefined(typeof(QuestCommand), command))
            {
                return Refuse(quest, command, QuestRejection.MissingContext, sequence);
            }

            var wanted = (QuestCommand)command;

            // The giver is checked before the quest rules, because "you are not standing
            // next to anybody" is the more useful thing to be told and the cheaper thing to
            // decide. Abandoning needs no giver: a player may give up anywhere.
            if (wanted != QuestCommand.Abandon
                && !StandingWithAGiverOffering(asker, npcId, quest))
            {
                return Refuse(quest, command, QuestRejection.MissingContext, sequence);
            }

            var context = new QuestService.Context(_quests, _items,
                asker.Domain == null || asker.Domain.Progression == null
                    ? 1
                    : asker.Domain.Progression.Level,
                new OwnerId(asker.Character.Value),
                Days.Today);

            QuestResult result;

            switch (wanted)
            {
                case QuestCommand.Accept:
                    result = QuestService.TryAccept(asker.Quests, quest, context);
                    break;

                case QuestCommand.TurnIn:
                    result = QuestService.TryTurnIn(asker.Quests, quest, asker.Inventory,
                        context);
                    break;

                default:
                    result = QuestService.TryAbandon(asker.Quests, quest, context);
                    break;
            }

            LastResult = result;

            // Taking, finishing or giving up a quest all change what has to be written down.
            //
            // Without this the log lived only in memory: a save is skipped for a character
            // that is not dirty, and nothing about a quest marked one. Accepting appeared to
            // persist only because the kill that followed it granted experience and marked
            // the character dirty on its way past -- so a quest accepted and never acted on
            // vanished, and a quest handed in was handed in again after the next login.
            if (result.IsAccepted) asker.MarkDirty();

            // The experience a finished quest is worth.
            //
            // QuestService grants the items itself and deliberately does not touch
            // progression -- levelling is not an item rule, and a service that reached into
            // it would be two systems deciding what a level is. So it reports the number and
            // this pays it, through the same curve a monster kill goes through.
            //
            // Without this the quest completed, the item landed, and the experience it
            // promised was quietly never paid.
            if (result.IsAccepted && result.ExperienceGranted > 0 && _progression != null
                && asker.Domain != null && asker.Domain.Progression != null
                && asker.Domain.Progression.CanAdd(result.ExperienceGranted, _progression))
            {
                asker.Domain.Progression.AddExperience(result.ExperienceGranted, _progression);

                // Levelling changes the character row, so it has to reach the database.
                asker.MarkDirty();
            }

            return new QuestCommandSnapshot
            {
                QuestId = quest.Value ?? string.Empty,
                Command = command,
                Accepted = result.IsAccepted,
                Reason = (int)result.Reason,
                Status = (int)asker.Quests.StatusOf(quest),
                Sequence = sequence,
            };
        }

        /// <summary>
        /// Whether the player is standing with an NPC that really offers this quest.
        /// </summary>
        /// <remarks>
        /// <b>Two claims, both checked.</b> That the NPC offers the quest is read from the
        /// NPC's own authored list, so naming a blacksmith to take a guide's quest fails.
        /// That the player is close enough is <see cref="NpcInteractionService"/>'s answer,
        /// which is the same check clicking the NPC already goes through -- one proximity
        /// rule, not two.
        ///
        /// <b>A quest nobody gives can be taken anywhere.</b> Not every quest has to come
        /// from a person; a world event or a map entry may grant one. Requiring a giver for
        /// those would make them impossible rather than safe.
        /// </remarks>
        public bool StandingWithAGiverOffering(LivingCharacter asker, string npcId,
            DefinitionId quest)
        {
            if (asker == null) return false;

            if (!AnybodyGives(quest)) return true;

            var giver = new DefinitionId(npcId ?? string.Empty);

            if (!giver.IsValid) return false;

            if (!_npcs.IsUsable || !_npcs.Npcs.TryGet(giver, out NPCDefinition npc)
                || npc == null)
            {
                return false;
            }

            if (!Offers(npc, quest)) return false;

            return NpcInteractionService.CanReach(asker.Location, npc, _npcs);
        }

        /// <summary>Whether any NPC in the world offers this quest.</summary>
        private bool AnybodyGives(DefinitionId quest)
        {
            if (!_npcs.IsUsable || !quest.IsValid) return false;

            IReadOnlyList<NPCDefinition> all = _npcs.Npcs.All;

            for (var i = 0; i < all.Count; i++)
            {
                if (Offers(all[i], quest)) return true;
            }

            return false;
        }

        /// <summary>Whether one NPC's authored list contains this quest.</summary>
        public static bool Offers(NPCDefinition npc, DefinitionId quest)
        {
            if (npc == null || !npc.Enabled || !quest.IsValid) return false;

            if (!npc.HasRole(NpcRole.Quest)) return false;

            DefinitionId[] offered = npc.Quests;

            for (var i = 0; i < offered.Length; i++)
            {
                if (offered[i] == quest) return true;
            }

            return false;
        }

        private QuestCommandSnapshot Refuse(DefinitionId quest, int command,
            QuestRejection reason, long sequence)
        {
            LastResult = QuestResult.Rejected(reason, quest);

            return new QuestCommandSnapshot
            {
                QuestId = quest.Value ?? string.Empty,
                Command = command,
                Accepted = false,
                Reason = (int)reason,
                Status = (int)QuestStatus.NotStarted,
                Sequence = sequence,
            };
        }

        // ---- telling the player --------------------------------------------------------

        /// <summary>Sends one player the answer to their own request.</summary>
        public bool Answer(LivingCharacter character, in QuestCommandSnapshot snapshot)
        {
            CharacterNetworkEntity entity = EntityFor(character);

            if (entity == null) return false;

            entity.ServerPublishQuestCommand(snapshot);

            return true;
        }

        /// <summary>Sends one player their whole quest log.</summary>
        public bool PublishLog(LivingCharacter character)
        {
            CharacterNetworkEntity entity = EntityFor(character);

            if (entity == null) return false;

            entity.ServerPublishQuestLog(SnapshotFor(character));

            return true;
        }

        /// <summary>
        /// Advances every active quest that a kill satisfies, and tells the player.
        /// </summary>
        /// <remarks>
        /// <b>The one door progress comes through.</b> The world reports what happened --
        /// a monster died, and which monster it was -- and
        /// <see cref="QuestService.ReportProgress"/> decides what that satisfies. Nothing
        /// here knows the quest, so a second kill quest costs no code at all.
        ///
        /// <b>Published only when something moved.</b> A player killing monsters no quest
        /// cares about costs one comparison per kill and no traffic.
        /// </remarks>
        /// <summary>
        /// What day the database thinks it is, for the quests that care.
        /// </summary>
        /// <remarks>Held here rather than on each character because it is a property of the
        /// world, not of a player: every daily in the world turns over on the same midnight.
        /// Synced whenever a character loads, which is often enough on a server people are
        /// joining and cheap enough to be free.</remarks>
        public WorldDayClock Days { get; } = new WorldDayClock();

        /// <summary>Records what the backend said the date was when a character loaded.</summary>
        public void SyncDay(int day, int secondsUntilNextDay)
        {
            Days.Sync(day, secondsUntilNextDay);
        }

        public int ReportKill(LivingCharacter killer, DefinitionId monster, int amount = 1)
        {
            if (killer == null || !monster.IsValid || amount <= 0) return 0;

            int advanced = QuestService.ReportProgress(killer.Quests,
                QuestObjectiveType.KillMonster, monster, amount,
                new QuestService.Context(_quests, _items, 1, default, Days.Today));

            if (advanced > 0)
            {
                // Progress is state too. The kill that caused it marks the character dirty
                // on its own path, but a quest advanced by anything else -- an item picked
                // up, a place reached -- would otherwise never be saved.
                killer.MarkDirty();

                PublishLog(killer);
            }

            return advanced;
        }

        /// <summary>One player's log, as the wire carries it.</summary>
        public QuestLogSnapshot SnapshotFor(LivingCharacter character)
        {
            var entries = new List<QuestProgressSnapshot>();

            if (character != null && character.Quests != null)
            {
                foreach (KeyValuePair<DefinitionId, QuestProgress> pair in
                    character.Quests.All)
                {
                    QuestProgress progress = pair.Value;

                    if (progress == null) continue;

                    var counters = new int[progress.ObjectiveCount];

                    for (var i = 0; i < counters.Length; i++)
                    {
                        counters[i] = progress.CountAt(i);
                    }

                    entries.Add(new QuestProgressSnapshot
                    {
                        QuestId = pair.Key.Value ?? string.Empty,
                        Status = (int)progress.Status,
                        Counters = counters,
                        CompletedDay = progress.CompletedDay,
                    });
                }
            }

            return new QuestLogSnapshot
            {
                CharacterId = character == null ? string.Empty
                    : character.Character.Value ?? string.Empty,
                Quests = entries.ToArray(),

                // The client answers availability with the same rules this server does, so
                // it needs the same date. Sent with the log rather than fetched, so the two
                // halves can never be a midnight apart.
                Today = Days.Today,
            };
        }

        private CharacterNetworkEntity EntityFor(LivingCharacter character)
        {
            if (character == null || _replication == null) return null;

            if (!_replication.TryGet(character.Character,
                out FishNet.Object.NetworkObject networkObject))
            {
                return null;
            }

            return networkObject == null
                ? null
                : networkObject.GetComponent<CharacterNetworkEntity>();
        }
    }
}
