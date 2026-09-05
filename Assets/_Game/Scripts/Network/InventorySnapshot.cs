using System.Collections.Generic;

namespace ChibiFantasy.Network
{
    /// <summary>One item, as a client is told about it.</summary>
    /// <remarks>
    /// <b>Ids and numbers, deliberately.</b> No live object, no domain type, nothing a
    /// client could hold a reference to and write through. Everything a slot needs to be
    /// drawn -- what it is, how many, which upgrades it carries -- and nothing else.
    ///
    /// <b>The instance id is the server's.</b> The same item that is in the server's bag is
    /// the item the client sees, with the identity the server minted; a snapshot never
    /// invents one. That is what lets a client name an item in a request and the server know
    /// exactly which one it meant.
    ///
    /// A struct with public fields rather than properties, because FishNet serialises fields
    /// and generating a serialiser for a property-only type is machinery this does not need.
    /// </remarks>
    public struct InventoryItemSnapshot
    {
        /// <summary>Where it sits in the bag. Negative when the piece is worn.</summary>
        public int Slot;

        /// <summary>Which equipment slot it is worn in, by ordinal. Zero when bagged.</summary>
        public int EquipmentSlot;

        /// <summary>The server's identity for this exact copy.</summary>
        public string InstanceId;

        public string DefinitionId;

        public int Quantity;

        /// <summary>Mirrors <c>ItemLockState</c>, so a client can grey out a listed item.</summary>
        public int LockState;

        public int EnhancementLevel;

        /// <summary>Per-copy rarity override. Empty means the definition's own.</summary>
        public string RarityId;

        /// <summary>How many status stones are socketed. Enough to draw the pips.</summary>
        public int EnchantCount;

        /// <summary>How many cards are socketed.</summary>
        public int CardCount;

        public bool IsEquipped => EquipmentSlot > 0;

        public override string ToString()
        {
            return DefinitionId + " x" + Quantity
                + (IsEquipped ? " worn " + EquipmentSlot : " @" + Slot);
        }
    }

    /// <summary>
    /// Everything one character owns, as of one authoritative moment.
    /// </summary>
    /// <remarks>
    /// <b>Replacement, not a delta.</b> The server builds the whole picture and the client
    /// throws away the previous one. A delta would mean the client maintaining state by
    /// applying changes -- which is the client keeping its own inventory, which is exactly
    /// what must not happen. Correctness over bandwidth, as this gate asks.
    ///
    /// <b><see cref="Revision"/> is the server's.</b> It exists so a client can drop a
    /// snapshot that arrives after a newer one, not so it can argue about which is right. A
    /// client never chooses it, and it is not the persistence save revision -- that is a
    /// concurrency token for the database and has no business on the wire.
    ///
    /// <b>Owner-scoped.</b> This is sent to the connection that owns the character and to
    /// nobody else. A player's bag is not public information, and the monster observer
    /// limitation does not apply here: this is a targeted message, not an observed value.
    /// </remarks>
    public struct InventorySnapshot
    {
        /// <summary>Who this belongs to. The client checks it against its own character.</summary>
        public string CharacterId;

        /// <summary>How many slots the bag has.</summary>
        public int Capacity;

        /// <summary>Advances on every authoritative change. The server owns it.</summary>
        public int Revision;

        /// <summary>Bagged items and worn pieces together, as the server holds them.</summary>
        public InventoryItemSnapshot[] Items;

        /// <summary>Items in the bag, ignoring what is worn.</summary>
        public IEnumerable<InventoryItemSnapshot> Bagged
        {
            get
            {
                if (Items == null) yield break;

                for (int i = 0; i < Items.Length; i++)
                {
                    if (!Items[i].IsEquipped) yield return Items[i];
                }
            }
        }

        /// <summary>What the character is wearing.</summary>
        public IEnumerable<InventoryItemSnapshot> Worn
        {
            get
            {
                if (Items == null) yield break;

                for (int i = 0; i < Items.Length; i++)
                {
                    if (Items[i].IsEquipped) yield return Items[i];
                }
            }
        }

        public int Count => Items == null ? 0 : Items.Length;

        public override string ToString()
        {
            return CharacterId + ": " + Count + " items, revision " + Revision;
        }
    }

    /// <summary>What a client is asking to do with its own belongings.</summary>
    /// <remarks>
    /// Only actions with an existing authoritative service. Nothing here is a new rule --
    /// each maps to a call the domain already validates.
    /// </remarks>
    public enum InventoryAction
    {
        None = 0,

        /// <summary>Move an item from one bag slot to another.</summary>
        Move = 1,

        /// <summary>Split part of a stack into an empty slot.</summary>
        Split = 2,

        /// <summary>Merge one stack into another.</summary>
        Merge = 3,

        /// <summary>Wear the equipment in a bag slot.</summary>
        Equip = 4,

        /// <summary>Take off what is worn in an equipment slot.</summary>
        Unequip = 5,

        /// <summary>
        /// Puts a card the player owns into a piece of equipment the player owns.
        /// </summary>
        /// <remarks><c>from</c> is the inventory slot holding the card and <c>to</c> is the
        /// inventory slot holding the equipment. Two slots and nothing else: the card, the
        /// modifier it grants, the socket capacity and the owner are all read from state the
        /// server already holds, so a client can name none of them.</remarks>
        SocketCard = 7,

        /// <summary>Takes a card back out of a piece the player owns.</summary>
        /// <remarks><c>from</c> is the equipment's inventory slot and <c>to</c> is the socket
        /// index. Phase 12 already decided what removal means; this only asks for it.</remarks>
        UnsocketCard = 8,

        /// <summary>
        /// Use the item in a slot: a potion, a scroll, a Devil Fruit.
        /// </summary>
        /// <remarks>
        /// <b>The client names a slot, never an effect.</b> What using an item does is
        /// authored on the item, so this carries no effect, no definition id and no
        /// outcome -- exactly like every other action here. A request that could name what
        /// it wanted to happen would be a request to make it happen.
        /// </remarks>
        Use = 6
    }

    /// <summary>
    /// Where a client's inventory request goes once the network has carried it.
    /// </summary>
    /// <remarks>
    /// The third seam beside combat and movement, for the same reason: the authority lives
    /// in the server assembly, which already references this one.
    ///
    /// <b>Slots and a quantity, never a result.</b> A client says "move slot 3 to slot 7",
    /// not "my inventory now looks like this". The server reads what is actually in slot 3.
    /// </remarks>
    public interface ICharacterInventoryRequestSink
    {
        /// <param name="connectionId">Whose object it arrived through. Not client-supplied.</param>
        /// <param name="action">Which existing service to ask.</param>
        /// <param name="from">Source bag slot, or the equipment slot for an unequip.</param>
        /// <param name="to">Destination bag slot, where the action uses one.</param>
        /// <param name="quantity">How many, for a split.</param>
        /// <param name="sequence">The client's own ordering number, for replay rejection.</param>
        void Submit(int connectionId, InventoryAction action, int from, int to, int quantity,
            long sequence);

        /// <summary>
        /// Builds the current picture for a connection, if it has a character.
        /// </summary>
        /// <remarks>
        /// Pulled rather than only pushed, because of when a client becomes able to receive
        /// one. A targeted message needs its recipient to be observing the object, and the
        /// server spawns the object before that is true -- so the first snapshot has to be
        /// sent at the moment the owner starts observing, which is something only the object
        /// knows. It asks; the authority answers.
        /// </remarks>
        bool TryBuildSnapshot(int connectionId, out InventorySnapshot snapshot);
    }
}
