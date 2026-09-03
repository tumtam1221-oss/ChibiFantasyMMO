using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Where a quest stands for one character.</summary>
    /// <remarks>Closed technical category: each value gates different operations. Reuses
    /// the vocabulary the phase brief asked for rather than inventing synonyms.</remarks>
    public enum QuestStatus
    {
        /// <summary>Never taken, or taken and since reset.</summary>
        NotStarted = 0,

        /// <summary>Taken, objectives outstanding.</summary>
        Active = 1,

        /// <summary>Every objective satisfied. Waiting to be turned in.</summary>
        ReadyToComplete = 2,

        /// <summary>Turned in and paid out.</summary>
        Completed = 3
    }

    /// <summary>
    /// One character's progress on one quest.
    /// </summary>
    /// <remarks>
    /// <b>Counters, not objects.</b> Progress is an integer per authored objective, matched
    /// by position. There is no kill-quest type and no collect-quest type: what an objective
    /// means is <see cref="QuestObjectiveType"/> on the definition, and this only counts.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future <c>character_quest</c>
    /// table is a character, a quest, a status and a small array of counters.
    /// </remarks>
    public sealed class QuestProgress
    {
        private readonly int[] _counters;

        public QuestProgress(DefinitionId questId, int objectiveCount)
        {
            QuestId = questId;
            _counters = new int[objectiveCount < 0 ? 0 : objectiveCount];
            Status = QuestStatus.Active;
        }

        public DefinitionId QuestId { get; }

        public QuestStatus Status { get; internal set; }

        /// <summary>How many objectives this quest has.</summary>
        public int ObjectiveCount => _counters.Length;

        /// <summary>How many times an objective has been satisfied.</summary>
        public int CountAt(int index)
        {
            return index < 0 || index >= _counters.Length ? 0 : _counters[index];
        }

        /// <summary>
        /// Adds to one counter, never past what the objective needs.
        /// </summary>
        /// <remarks>Clamped so an over-delivery does not accumulate: killing twenty of a
        /// ten-kill objective leaves the counter at ten, and a repeatable quest therefore
        /// starts from zero rather than from a surplus.</remarks>
        /// <returns>How much was actually added.</returns>
        internal int Advance(int index, int amount, int required)
        {
            if (index < 0 || index >= _counters.Length || amount <= 0) return 0;

            int room = required - _counters[index];
            if (room <= 0) return 0;

            int added = amount < room ? amount : room;
            _counters[index] += added;
            return added;
        }

        /// <summary>Sets a counter directly. For a server correcting a client.</summary>
        internal void SetCount(int index, int value)
        {
            if (index < 0 || index >= _counters.Length) return;
            _counters[index] = value < 0 ? 0 : value;
        }

        internal void Reset()
        {
            for (int i = 0; i < _counters.Length; i++) _counters[i] = 0;
            Status = QuestStatus.Active;
        }

        public override string ToString()
        {
            return QuestId + " (" + Status + ")";
        }
    }

    /// <summary>
    /// Every quest one character has touched.
    /// </summary>
    /// <remarks>
    /// <b>Persistent, and server-owned in a served game.</b> A client asserting that a quest
    /// is complete proves nothing; this is the state a server keeps and validates against.
    ///
    /// <b>Completed quests are remembered.</b> Prerequisites and non-repeatable quests both
    /// need the history, so finishing one does not remove it -- its status becomes
    /// <see cref="QuestStatus.Completed"/> and it stays.
    /// </remarks>
    public sealed class CharacterQuestState : IPersistentState
    {
        private readonly Dictionary<DefinitionId, QuestProgress> _quests =
            new Dictionary<DefinitionId, QuestProgress>();

        private Revision _revision;

        public CharacterQuestState(CharacterId characterId)
        {
            CharacterId = characterId;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        public Revision Revision => _revision;

        public int Count => _quests.Count;

        /// <summary>Everything taken or finished, for a tracker to draw.</summary>
        public IEnumerable<KeyValuePair<DefinitionId, QuestProgress>> All => _quests;

        public QuestStatus StatusOf(DefinitionId questId)
        {
            QuestProgress progress;
            return _quests.TryGetValue(questId, out progress)
                ? progress.Status
                : QuestStatus.NotStarted;
        }

        public bool TryGet(DefinitionId questId, out QuestProgress progress)
        {
            return _quests.TryGetValue(questId, out progress);
        }

        public bool IsCompleted(DefinitionId questId)
        {
            return StatusOf(questId) == QuestStatus.Completed;
        }

        public bool IsActive(DefinitionId questId)
        {
            QuestStatus status = StatusOf(questId);
            return status == QuestStatus.Active || status == QuestStatus.ReadyToComplete;
        }

        /// <summary>Starts tracking a quest. Only <see cref="QuestService"/> should call this.</summary>
        internal QuestProgress Begin(DefinitionId questId, int objectiveCount)
        {
            QuestProgress existing;

            if (_quests.TryGetValue(questId, out existing))
            {
                existing.Reset();
                _revision = _revision.Next();
                return existing;
            }

            var progress = new QuestProgress(questId, objectiveCount);
            _quests[questId] = progress;
            _revision = _revision.Next();
            return progress;
        }

        /// <summary>Records that state moved. Called by the service after a real change.</summary>
        internal void Touch()
        {
            _revision = _revision.Next();
        }

        /// <summary>Forgets everything. For a character wipe or a test reset.</summary>
        public void Clear()
        {
            if (_quests.Count == 0) return;

            _quests.Clear();
            _revision = _revision.Next();
        }
    }
}
