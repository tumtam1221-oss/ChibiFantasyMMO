using FishNet.Serializing;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// One quest as a player is allowed to see it.
    /// </summary>
    /// <remarks>
    /// <b>Status and counters, and nothing else.</b> Not the reward, not the objective's
    /// required amount, not the description: all of those are authored content the client
    /// already ships and resolves from the id. Sending them would put a second copy of the
    /// quest on the wire, and the two would disagree the first time content changed.
    ///
    /// <b>The counters are the server's.</b> A client can draw 2/3; it cannot decide it.
    /// </remarks>
    public struct QuestProgressSnapshot
    {
        /// <summary>Which quest. Resolved against the client's own catalogue.</summary>
        public string QuestId;

        /// <summary>Where it has got to, as <c>QuestStatus</c>.</summary>
        public int Status;

        /// <summary>
        /// One counter per authored objective, in the order the definition lists them.
        /// </summary>
        /// <remarks>Matched by position rather than by name, exactly as
        /// <c>CharacterQuestState</c> stores them -- there is no second identity for an
        /// objective to drift from.</remarks>
        public int[] Counters;

        /// <summary>The day it was finished, as the database counts days. Zero means never.</summary>
        /// <remarks>Replicated so the journal can grey out a daily that has already been
        /// done today without asking the server a second question, and so the mark above a
        /// giver's head agrees with what the server would answer.</remarks>
        public int CompletedDay;

        public int ObjectiveCount => Counters == null ? 0 : Counters.Length;

        public override string ToString()
        {
            return QuestId + " status " + Status + " (" + ObjectiveCount + " objectives)";
        }
    }

    /// <summary>Everything one player's quest log currently says.</summary>
    /// <remarks>
    /// <b>Whole log every time, and sent to one owner.</b> The whole log because a client
    /// that merged deltas would maintain quest state of its own and a dropped removal would
    /// leave a finished quest in the journal for ever; to one owner because what somebody is
    /// doing is nobody else's business and there is no observer scoping to protect it.
    ///
    /// The same argument the bag and the status list already make.
    /// </remarks>
    public struct QuestLogSnapshot
    {
        public string CharacterId;

        public QuestProgressSnapshot[] Quests;

        /// <summary>
        /// Today, as the database counts days. Zero when the world has not been told.
        /// </summary>
        /// <remarks>
        /// <b>Sent rather than read locally.</b> The client draws which quests are
        /// available, and it must reach the same answer the server would -- a journal
        /// offering a daily the server will refuse is worse than no journal. Taking the day
        /// from the player's own machine would put the two a timezone apart.
        /// </remarks>
        public int Today;

        public int Count => Quests == null ? 0 : Quests.Length;

        public override string ToString()
        {
            return "quest log for " + CharacterId + ": " + Count;
        }
    }

    /// <summary>What a player asked to do with a quest.</summary>
    public enum QuestCommand
    {
        /// <summary>Take it.</summary>
        Accept = 0,

        /// <summary>Hand it in and be paid.</summary>
        TurnIn = 1,

        /// <summary>Give it up, losing progress.</summary>
        Abandon = 2
    }

    /// <summary>
    /// The server's answer to a quest request.
    /// </summary>
    /// <remarks>The refusal travels with its reason, for the same reason the NPC one does: a
    /// client told only "no" has to guess between "walk to the guide", "you already have
    /// this" and "you are not high enough level", and it will guess wrong in front of a
    /// player.</remarks>
    public struct QuestCommandSnapshot
    {
        public string QuestId;

        /// <summary>Which command, as <c>QuestCommand</c>.</summary>
        public int Command;

        public bool Accepted;

        /// <summary>Why not, as <c>QuestRejection</c>. Zero when accepted.</summary>
        public int Reason;

        /// <summary>Where the quest ended up, as <c>QuestStatus</c>.</summary>
        public int Status;

        public long Sequence;

        public override string ToString()
        {
            return Accepted
                ? "quest " + QuestId + " -> status " + Status
                : "quest " + QuestId + " refused (" + Reason + ")";
        }
    }

    /// <summary>
    /// Where a client's quest request lands.
    /// </summary>
    /// <remarks>
    /// <b>A quest, a command, and where they are standing when they ask.</b> Never a reward,
    /// never a counter, never a status. What the quest pays, whether they may take it and
    /// how far along they are is read on the server from state this call cannot reach.
    ///
    /// <b>The NPC travels because the rule is about the NPC.</b> A quest bound to a giver is
    /// accepted by walking up to that giver, so the request says which one the player
    /// believes they are talking to, and the server checks that claim the same way it checks
    /// every other one -- by asking whether they are actually standing there.
    /// </remarks>
    public interface ICharacterQuestRequestSink
    {
        void Submit(int connectionId, string questId, int command, string npcId,
            long sequence);
    }

    /// <summary>FishNet needs to be told how to put these on the wire.</summary>
    public static class QuestSnapshotSerializer
    {
        public static void WriteQuestProgressSnapshot(this Writer writer,
            QuestProgressSnapshot value)
        {
            writer.WriteString(value.QuestId);
            writer.WriteInt32(value.Status);

            int[] counters = value.Counters;

            writer.WriteInt32(counters == null ? 0 : counters.Length);

            if (counters == null) return;

            for (var i = 0; i < counters.Length; i++) writer.WriteInt32(counters[i]);
        }

        public static QuestProgressSnapshot ReadQuestProgressSnapshot(this Reader reader)
        {
            var value = new QuestProgressSnapshot
            {
                QuestId = reader.ReadString(),
                Status = reader.ReadInt32(),
            };

            int count = reader.ReadInt32();

            // A negative or absurd length is a malformed packet, not a quest log. Refusing
            // to allocate on it is the difference between a dropped message and a server
            // that a client can make allocate a gigabyte.
            if (count <= 0 || count > 64)
            {
                value.Counters = System.Array.Empty<int>();

                return value;
            }

            var counters = new int[count];

            for (var i = 0; i < count; i++) counters[i] = reader.ReadInt32();

            value.Counters = counters;

            return value;
        }

        public static void WriteQuestCommandSnapshot(this Writer writer,
            QuestCommandSnapshot value)
        {
            writer.WriteString(value.QuestId);
            writer.WriteInt32(value.Command);
            writer.WriteBoolean(value.Accepted);
            writer.WriteInt32(value.Reason);
            writer.WriteInt32(value.Status);
            writer.WriteInt64(value.Sequence);
        }

        public static QuestCommandSnapshot ReadQuestCommandSnapshot(this Reader reader)
        {
            return new QuestCommandSnapshot
            {
                QuestId = reader.ReadString(),
                Command = reader.ReadInt32(),
                Accepted = reader.ReadBoolean(),
                Reason = reader.ReadInt32(),
                Status = reader.ReadInt32(),
                Sequence = reader.ReadInt64(),
            };
        }
    }
}
