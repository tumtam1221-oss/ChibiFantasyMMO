using FishNet.Serializing;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// The server's answer to "may I talk to that NPC, and about what".
    /// </summary>
    /// <remarks>
    /// <b>An authorisation, not an outcome.</b> Nothing has been bought, no quest has moved
    /// and no storage has opened. This says the request was allowed and which authored
    /// content the role resolved to; the screen that then opens still asks the service that
    /// owns the operation for every actual change. That split is why a client holding this
    /// message cannot have given itself anything.
    ///
    /// <b>The refusal travels too, and says why.</b> A client that is told only "no" has to
    /// guess between "walk closer", "that NPC does not do that" and "the server has not
    /// finished loading", and it will guess wrong in front of a player. The reason is the
    /// service's own enum value, so there is one vocabulary rather than a translation.
    ///
    /// <b>Ids as strings, because that is what crosses a wire.</b> They are turned back into
    /// definition ids on arrival and never used as anything else -- no behaviour keys off
    /// their text.
    /// </remarks>
    public struct NpcInteractionSnapshot
    {
        /// <summary>Which NPC was asked about. Echoed so a late reply can be matched.</summary>
        public string NpcId;

        /// <summary>Which role was asked for, as <c>NpcRole</c>.</summary>
        public int Role;

        /// <summary>Whether the player may proceed.</summary>
        public bool Accepted;

        /// <summary>Why not, as <c>NpcInteractionRejection</c>. Zero when accepted.</summary>
        public int Reason;

        /// <summary>
        /// What the role resolves to: a shop id for a vendor, empty for the rest.
        /// </summary>
        /// <remarks>Storage opens the character's own container and a quest list is read
        /// from the NPC, so neither needs content of its own here.</remarks>
        public string Content;

        /// <summary>The request this answers, so an old reply cannot overwrite a new one.</summary>
        public long Sequence;

        public override string ToString()
        {
            return Accepted
                ? "npc " + NpcId + " -> role " + Role
                : "npc " + NpcId + " refused (" + Reason + ")";
        }
    }

    /// <summary>
    /// Where a client's interaction request lands.
    /// </summary>
    /// <remarks>
    /// <b>An NPC and a role, and nothing else.</b> The request cannot name a shop, a price,
    /// a quest, a class or a result, because a client is entitled to decide none of them.
    /// Who is asking is the connection's own identity, exactly as the loot and inventory
    /// sinks already work, so nobody can interact on somebody else's behalf.
    /// </remarks>
    public interface ICharacterNpcRequestSink
    {
        void Submit(int connectionId, string npcId, int role, long sequence);
    }

    /// <summary>FishNet needs to be told how to put this on the wire.</summary>
    /// <remarks>Written field by field rather than left to the generated serializer, for
    /// the same reason the loot snapshot is: a struct that is serialized by reflection is a
    /// struct whose wire cost changes silently when somebody adds a field.</remarks>
    public static class NpcInteractionSnapshotSerializer
    {
        public static void WriteNpcInteractionSnapshot(this Writer writer,
            NpcInteractionSnapshot value)
        {
            writer.WriteString(value.NpcId);
            writer.WriteInt32(value.Role);
            writer.WriteBoolean(value.Accepted);
            writer.WriteInt32(value.Reason);
            writer.WriteString(value.Content);
            writer.WriteInt64(value.Sequence);
        }

        public static NpcInteractionSnapshot ReadNpcInteractionSnapshot(this Reader reader)
        {
            return new NpcInteractionSnapshot
            {
                NpcId = reader.ReadString(),
                Role = reader.ReadInt32(),
                Accepted = reader.ReadBoolean(),
                Reason = reader.ReadInt32(),
                Content = reader.ReadString(),
                Sequence = reader.ReadInt64(),
            };
        }
    }
}
