using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// What this player has taken and what they could take, as the client knows it.
    /// </summary>
    /// <remarks>
    /// <b>A copy of the server's answer, never a second opinion.</b> The log arrives whole
    /// from the world and is adopted whole. Nothing here accepts a quest, advances a counter
    /// or decides a quest is finished -- a request goes to the server and the next log is
    /// the answer. That is why a player editing this in memory changes nothing: they are
    /// editing a picture of state that lives somewhere else.
    ///
    /// <b>Availability is asked, not worked out.</b> Which quests may be taken comes from
    /// <see cref="QuestService.CanAccept"/> -- the same rules the server runs when the
    /// request arrives. A list that decided for itself would show a quest and then watch the
    /// server refuse it.
    ///
    /// <b>Givers come from the NPCs, not from the quest.</b> A quest does not name who hands
    /// it out; an NPC names the quests it offers. Asking in that direction means a second
    /// giver for the same quest costs no content change.
    /// </remarks>
    public sealed class QuestJournal : MonoBehaviour
    {
        // No identity. A character id is issued by the account database and this log is a
        // picture of somebody else's state -- inventing a name for it would be writing down
        // an identity, which is the one thing production code here never does.
        private readonly CharacterQuestState _state = new CharacterQuestState(default);

        private readonly List<QuestViewData> _available = new List<QuestViewData>();
        private readonly List<QuestViewData> _active = new List<QuestViewData>();
        private readonly List<QuestViewData> _completed = new List<QuestViewData>();

        private IDefinitionRegistry<QuestDefinition> _quests;
        private IDefinitionRegistry<ItemDefinition> _items;
        private IDefinitionRegistry<MonsterDefinition> _monsters;
        private IDefinitionRegistry<NPCDefinition> _npcs;

        private CharacterNetworkEntity _player;
        private long _sequence;
        private int _level = 1;
        private int _today;

        /// <summary>The player's own log, as the server last described it.</summary>
        public CharacterQuestState State => _state;

        /// <summary>Quests that could be taken right now.</summary>
        public IReadOnlyList<QuestViewData> Available => _available;

        /// <summary>Quests taken and not yet handed in.</summary>
        public IReadOnlyList<QuestViewData> Active => _active;

        /// <summary>
        /// Quests already handed in.
        /// </summary>
        /// <remarks>Built from the same log the other two come from, because the server has
        /// always sent completed quests and the client has always kept them -- there was
        /// simply nowhere on screen that showed them. A character who finishes the only quest
        /// in the game had all three lists empty, which is the honest state of the world and
        /// the exact thing a player reads as the quest system having lost their progress.</remarks>
        public IReadOnlyList<QuestViewData> Completed => _completed;

        /// <summary>Raised whenever the log or the available list changes.</summary>
        public event System.Action Changed;

        /// <summary>The server's last answer to a request. For a message to the player.</summary>
        public QuestCommandSnapshot LastAnswer { get; private set; }

        /// <summary>Points this at the world's content.</summary>
        public void Bind(IDefinitionRegistry<QuestDefinition> quests,
            IDefinitionRegistry<ItemDefinition> items = null,
            IDefinitionRegistry<MonsterDefinition> monsters = null,
            IDefinitionRegistry<NPCDefinition> npcs = null)
        {
            _quests = quests;
            _items = items;
            _monsters = monsters;
            _npcs = npcs;

            Rebuild();
        }

        /// <summary>Points this at the player, once they exist.</summary>
        public void UsePlayer(CharacterNetworkEntity player)
        {
            if (_player != null)
            {
                _player.QuestLogChanged -= OnLogChanged;
                _player.QuestCommandAnswered -= OnAnswered;
            }

            _player = player;

            if (_player == null)
            {
                // Leaving the world takes the log with it. A journal that kept showing the
                // last character's quests would be lying to the next one.
                _state.Clear();
                Rebuild();

                return;
            }

            _player.QuestLogChanged += OnLogChanged;
            _player.QuestCommandAnswered += OnAnswered;

            // Before the log arrives, so the first rebuild already answers the level rules
            // correctly rather than hiding every level-gated quest for one frame.
            _level = LevelOf(_player);

            OnLogChanged(_player.QuestLog);
        }

        /// <summary>
        /// Keeps the availability rules answering against the level the player actually is.
        /// </summary>
        /// <remarks>
        /// <b>The bug this closes.</b> <see cref="UseLevel"/> existed and nothing in the
        /// client ever called it, so the journal answered every availability question as
        /// though the player were level 1. Any quest with a level requirement was therefore
        /// invisible: <c>CanAccept</c> returned <c>LevelTooLow</c>, the quest never reached
        /// the Available list, and the giver wore no mark -- while the server, which reads
        /// the real level, would have accepted it happily. Two of Harbor Town's five quests
        /// were unreachable that way and nothing anywhere said why.
        ///
        /// <b>Polled, because the level is a SyncVar.</b> Replicated values raise nothing
        /// this layer can subscribe to -- the heads-up display reads them the same way. The
        /// cost is one integer comparison per frame, and a rebuild happens only when the
        /// number actually moves, which is a handful of times in a session.
        /// </remarks>
        private void Update()
        {
            if (_player == null) return;

            int now = LevelOf(_player);

            if (now == _level) return;

            UseLevel(now);
        }

        private static int LevelOf(CharacterNetworkEntity player)
        {
            return player == null || player.Level < 1 ? 1 : player.Level;
        }

        /// <summary>What level the availability rules should be answered against.</summary>
        public void UseLevel(int level)
        {
            _level = level < 1 ? 1 : level;

            Rebuild();
        }

        /// <summary>
        /// Asks the server to take a quest, naming the giver being spoken to.
        /// </summary>
        /// <remarks>The giver travels because the server checks it. A quest list with no NPC
        /// in front of it passes none, which is refused -- that is the whole of the rule
        /// that a quest list cannot be used to take quests from across the town.</remarks>
        public bool RequestAccept(DefinitionId quest, DefinitionId giver = default)
        {
            return Request(quest, QuestCommand.Accept, giver);
        }

        /// <summary>Asks the server to hand a quest in to the giver being spoken to.</summary>
        public bool RequestTurnIn(DefinitionId quest, DefinitionId giver = default)
        {
            return Request(quest, QuestCommand.TurnIn, giver);
        }

        private bool Request(DefinitionId quest, QuestCommand command, DefinitionId giver)
        {
            if (_player == null || !quest.IsValid) return false;

            _sequence++;

            _player.RequestQuestCommand(quest.Value, (int)command,
                giver.Value ?? string.Empty, _sequence);

            return true;
        }

        // ---- what the world says ----------------------------------------------------------

        private void OnLogChanged(QuestLogSnapshot snapshot)
        {
            // Adopted whole. The server sends the entire log, so a quest that has left it --
            // abandoned, or wiped by a character reset -- leaves the client's copy too.
            _state.Clear();

            QuestProgressSnapshot[] entries = snapshot.Quests;

            if (entries != null)
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    QuestProgressSnapshot entry = entries[i];

                    _state.AdoptAuthoritative(new DefinitionId(entry.QuestId ?? string.Empty),
                        (QuestStatus)entry.Status, entry.Counters, entry.CompletedDay);
                }
            }

            // The server's date, so this journal offers exactly what the server would
            // accept. A client deciding the date itself would show a daily as available
            // while the server refused it, which reads as a button that does nothing.
            _today = snapshot.Today;

            Rebuild();
        }

        private void OnAnswered(QuestCommandSnapshot snapshot)
        {
            LastAnswer = snapshot;
        }

        /// <summary>Rebuilds all three lists from the log and the catalogue.</summary>
        /// <remarks>On change rather than per frame: a quest list that rebuilt every frame
        /// would allocate a view per quest per frame for a panel nobody has open.</remarks>
        public void Rebuild()
        {
            _available.Clear();
            _active.Clear();
            _completed.Clear();

            if (_quests == null)
            {
                Changed?.Invoke();

                return;
            }

            WorldViewAdapter.Context context = ViewContext;
            var rules = new QuestService.Context(_quests, _items, _level, default, _today);

            IReadOnlyList<QuestDefinition> all = _quests.All;

            for (var i = 0; i < all.Count; i++)
            {
                QuestDefinition quest = all[i];

                if (quest == null || !quest.Id.IsValid) continue;

                QuestStatus status = _state.StatusOf(quest.Id);

                if (status == QuestStatus.Active || status == QuestStatus.ReadyToComplete)
                {
                    QuestViewData view = WorldViewAdapter.BuildQuest(_state, quest.Id, context);

                    if (view.IsValid) _active.Add(view);

                    continue;
                }

                if (status == QuestStatus.Completed)
                {
                    QuestViewData done = WorldViewAdapter.BuildQuest(_state, quest.Id, context);

                    if (done.IsValid) _completed.Add(done);

                    // A repeatable quest is both finished and on offer again, so it falls
                    // through to the availability rules rather than stopping here. The
                    // history is not a reason to hide the next run of it.
                    if (!quest.Repeatable) continue;
                }

                if (QuestService.CanAccept(_state, quest.Id, rules) != QuestRejection.None)
                {
                    continue;
                }

                // Available means "not started", whatever a previous run of a repeatable
                // quest left in the log. Showing those counters here put a full bar on a
                // quest the player had not taken.
                QuestViewData offered = WorldViewAdapter.BuildQuestOffer(quest.Id, context);

                if (offered.IsValid) _available.Add(offered);
            }

            Changed?.Invoke();
        }

        /// <summary>The registries the view adapter reads through.</summary>
        public WorldViewAdapter.Context ViewContext =>
            new WorldViewAdapter.Context(_items, _monsters, _quests, _npcs);

        // ---- who hands what out -----------------------------------------------------------

        /// <summary>
        /// The NPC that offers a quest, or invalid when nobody does.
        /// </summary>
        /// <remarks>Asked of the NPCs rather than the quest, because that is the direction
        /// the content is authored in. The first one wins: a quest offered by two givers is
        /// legitimate, and a detail panel only has room to name where to start.</remarks>
        public DefinitionId GiverOf(DefinitionId quest)
        {
            if (_npcs == null || !quest.IsValid) return default;

            IReadOnlyList<NPCDefinition> all = _npcs.All;

            for (var i = 0; i < all.Count; i++)
            {
                NPCDefinition npc = all[i];

                if (npc == null || !npc.Enabled || !npc.HasRole(NpcRole.Quest)) continue;

                DefinitionId[] offered = npc.Quests;

                for (var q = 0; q < offered.Length; q++)
                {
                    if (offered[q] == quest) return npc.Id;
                }
            }

            return default;
        }

        /// <summary>
        /// What an NPC should be showing above its head.
        /// </summary>
        /// <remarks>
        /// <b>Derived from the three things the gate names and nothing else:</b> what the NPC
        /// offers, what the player's log says, and what the quest rules allow. No NPC id is
        /// compared to a literal, so a second quest giver needs no code at all.
        ///
        /// <b>An active quest gets no icon.</b> A town where every guide wears a mark the
        /// whole time is a noisy town, and the mark stops meaning "there is something here".
        /// </remarks>
        public QuestMarker MarkerFor(NPCDefinition npc)
        {
            if (npc == null || !npc.Enabled || _quests == null) return QuestMarker.None;

            if (!npc.HasRole(NpcRole.Quest)) return QuestMarker.None;

            DefinitionId[] offered = npc.Quests;
            var rules = new QuestService.Context(_quests, _items, _level, default, _today);

            var available = false;

            for (var i = 0; i < offered.Length; i++)
            {
                DefinitionId quest = offered[i];

                if (!quest.IsValid) continue;

                // Ready to hand in wins outright: it is the thing the player came back for.
                if (_state.StatusOf(quest) == QuestStatus.ReadyToComplete)
                {
                    return QuestMarker.ReadyToTurnIn;
                }

                if (QuestService.CanAccept(_state, quest, rules) == QuestRejection.None)
                {
                    available = true;
                }
            }

            return available ? QuestMarker.Available : QuestMarker.None;
        }

        private void OnDestroy()
        {
            if (_player == null) return;

            _player.QuestLogChanged -= OnLogChanged;
            _player.QuestCommandAnswered -= OnAnswered;
        }
    }

    /// <summary>What is drawn above a quest giver's head.</summary>
    /// <remarks>Three states because there are three things worth saying: come here, come
    /// back, and nothing. An active quest deliberately has no mark -- see
    /// <see cref="QuestJournal.MarkerFor"/>.</remarks>
    public enum QuestMarker
    {
        None = 0,

        /// <summary>Something to take. Drawn as "!".</summary>
        Available = 1,

        /// <summary>Something to hand in. Drawn as "?".</summary>
        ReadyToTurnIn = 2
    }
}
