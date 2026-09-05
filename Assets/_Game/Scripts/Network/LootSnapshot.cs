using FishNet.Serializing;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// One pile of loot on the ground, as a player is allowed to see it.
    /// </summary>
    /// <remarks>
    /// <b>Only what is needed to walk over and press a button.</b> Where it is, what it is
    /// called, and how many. Not the roll that produced it, not the drop table it came from,
    /// not the chance, not a database id, not a persistence revision -- none of which a
    /// client needs and all of which would tell a player something about the world they are
    /// supposed to discover by playing.
    ///
    /// <b>Item identity is public on purpose.</b> A pile a player cannot identify is a pile
    /// they cannot decide to walk to, so the item id travels. Everything that item then
    /// does is still resolved on the server from authored content.
    /// </remarks>
    public struct LootEntrySnapshot
    {
        /// <summary>Which pile. The id a pickup request names.</summary>
        public string LootId;

        /// <summary>Which slot within the pile.</summary>
        public int Index;

        /// <summary>What it is, so the client can draw a name.</summary>
        public string ItemId;

        public int Quantity;

        public float X;
        public float Y;
        public float Z;

        public override string ToString()
        {
            return "loot " + LootId + "[" + Index + "] " + ItemId + " x" + Quantity;
        }
    }

    /// <summary>What is on the ground near one player.</summary>
    /// <remarks>Sent to one owner rather than broadcast, for the same reason the bag is:
    /// it is answered per character and there is no shared view to keep consistent.</remarks>
    public struct LootSnapshot
    {
        public string CharacterId;

        public LootEntrySnapshot[] Entries;

        public int Count => Entries == null ? 0 : Entries.Length;

        public override string ToString()
        {
            return "loot snapshot for " + CharacterId + ": " + Count;
        }
    }

    /// <summary>
    /// Where a client's pickup request lands.
    /// </summary>
    /// <remarks>The request names a pile and a slot. It cannot name an item, a quantity, an
    /// owner or a result, because none of those are things a client is entitled to
    /// decide -- exactly as the inventory sink already works.</remarks>
    public interface ICharacterLootRequestSink
    {
        void Submit(int connectionId, string lootId, int index, long sequence);

        bool TryBuildLootSnapshot(int connectionId, out LootSnapshot snapshot);
    }

    /// <summary>FishNet needs to be told how to put these on the wire.</summary>
    public static class LootSnapshotSerializer
    {
        public static void WriteLootEntrySnapshot(this Writer writer, LootEntrySnapshot value)
        {
            writer.WriteString(value.LootId);
            writer.WriteInt32(value.Index);
            writer.WriteString(value.ItemId);
            writer.WriteInt32(value.Quantity);
            writer.WriteSingle(value.X);
            writer.WriteSingle(value.Y);
            writer.WriteSingle(value.Z);
        }

        public static LootEntrySnapshot ReadLootEntrySnapshot(this Reader reader)
        {
            return new LootEntrySnapshot
            {
                LootId = reader.ReadString(),
                Index = reader.ReadInt32(),
                ItemId = reader.ReadString(),
                Quantity = reader.ReadInt32(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle(),
            };
        }
    }
}
