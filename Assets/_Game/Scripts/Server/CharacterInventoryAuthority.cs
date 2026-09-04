using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>Why an inventory request produced nothing.</summary>
    /// <remarks>
    /// Only the reasons this layer itself can produce. Everything the domain refuses --
    /// a full bag, an unstackable pair, a level requirement -- keeps its own existing
    /// rejection and is reported through <see cref="CharacterInventoryResult.Container"/>
    /// or <see cref="CharacterInventoryResult.Equip"/>, unchanged and untranslated.
    /// </remarks>
    public enum InventoryRequestRejection
    {
        None = 0,

        /// <summary>No registry, no character, or a world with no items.</summary>
        MissingContext = 1,

        /// <summary>The connection is not entitled to act.</summary>
        StaleConnection = 2,

        /// <summary>No character on that connection.</summary>
        NoCharacter = 3,

        /// <summary>Older than a request already applied.</summary>
        OutOfOrder = 4,

        /// <summary>An action this server does not expose.</summary>
        UnsupportedAction = 5,

        /// <summary>The domain service refused it. Its own reason says why.</summary>
        Refused = 6
    }

    /// <summary>What one inventory request did.</summary>
    public readonly struct CharacterInventoryResult
    {
        private CharacterInventoryResult(bool accepted, InventoryRequestRejection rejection,
            ItemContainerResult container, EquipResult equip)
        {
            IsAccepted = accepted;
            Rejection = rejection;
            Container = container;
            Equip = equip;
        }

        public bool IsAccepted { get; }

        public InventoryRequestRejection Rejection { get; }

        /// <summary>The container service's own answer, for move, split and merge.</summary>
        public ItemContainerResult Container { get; }

        /// <summary>The equipment service's own answer, for equip and unequip.</summary>
        public EquipResult Equip { get; }

        public static CharacterInventoryResult Refused(InventoryRequestRejection rejection)
        {
            return new CharacterInventoryResult(false, rejection, default, default);
        }

        public static CharacterInventoryResult FromContainer(in ItemContainerResult result)
        {
            return new CharacterInventoryResult(result.IsAccepted,
                result.IsAccepted
                    ? InventoryRequestRejection.None
                    : InventoryRequestRejection.Refused,
                result, default);
        }

        public static CharacterInventoryResult FromEquip(in EquipResult result)
        {
            return new CharacterInventoryResult(result.IsAccepted,
                result.IsAccepted
                    ? InventoryRequestRejection.None
                    : InventoryRequestRejection.Refused,
                default, result);
        }

        public override string ToString()
        {
            if (IsAccepted) return "accepted";

            return Rejection == InventoryRequestRejection.Refused
                ? "refused: " + Container + " " + Equip
                : "refused: " + Rejection;
        }
    }

    /// <summary>
    /// Runs a client's inventory and equipment requests against the authoritative character.
    /// </summary>
    /// <remarks>
    /// <b>It validates authority and shape, then asks the services that already exist.</b>
    /// Whether a stack may merge is <c>ItemContainerState</c>'s; whether a sword may be worn
    /// is <c>EquipmentService</c>'s, including the level, class and job gates. None of that
    /// is restated here, because a second copy of a rule is a rule that will disagree.
    ///
    /// <b>The identity is the connection.</b> A request arrives through a character's own
    /// network object; the object's owner names the connection; the character comes from the
    /// registry. There is no character id in the message and nothing here reads one, so a
    /// client cannot reach into another player's bag.
    ///
    /// <b>Snapshots are pushed on change, not on a timer.</b> An accepted request rebuilds
    /// the picture and sends it to the one connection that owns it; a refused one sends
    /// nothing, because nothing changed. An idle player costs no bandwidth.
    ///
    /// <b>Persistence is the existing lifecycle's.</b> An accepted mutation marks the
    /// character dirty and the registry writes it at the points it already writes at. This
    /// does not invent a save.
    /// </remarks>
    public sealed class CharacterInventoryAuthority : ICharacterInventoryRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly CombatCommandAuthority.WorldConnectionRegistryAdapter _connections;
        private readonly IDefinitionRegistry<ItemDefinition> _items;
        private readonly CharacterReplicationService _replication;

        /// <summary>The last request sequence accepted per character, so a replay is refused.</summary>
        /// <remarks>Its own stream, like combat's and movement's and for the same reason: a
        /// player rearranging their bag must not advance the counter their next attack is
        /// measured against.</remarks>
        private readonly Dictionary<string, long> _sequences = new Dictionary<string, long>();

        /// <summary>Snapshot revisions, so a client can drop one that arrives late.</summary>
        private readonly Dictionary<string, int> _revisions = new Dictionary<string, int>();

        /// <param name="characters">The authoritative registry. The only source of a character.</param>
        /// <param name="canAct">Whether a connection is still entitled to act.</param>
        /// <param name="items">Authored items, which the domain services need.</param>
        /// <param name="replication">
        /// How a snapshot reaches its owner. Optional: a world composed without it still
        /// mutates authoritatively, it simply tells nobody -- which is what a headless
        /// server with no clients wants.
        /// </param>
        public CharacterInventoryAuthority(WorldCharacterRegistry characters,
            CombatCommandAuthority.WorldConnectionRegistryAdapter canAct,
            IDefinitionRegistry<ItemDefinition> items,
            CharacterReplicationService replication = null)
        {
            _characters = characters;
            _connections = canAct;
            _items = items;
            _replication = replication;
        }

        /// <summary>How many requests have been handled, accepted or not.</summary>
        public int Handled { get; private set; }

        /// <summary>What the last request produced. Diagnostics, never sent anywhere.</summary>
        public CharacterInventoryResult LastResult { get; private set; }

        public void Submit(int connectionId, InventoryAction action, int from, int to,
            int quantity, long sequence)
        {
            Handled++;

            LastResult = Handle(connectionId, action, from, to, quantity, sequence);
        }

        /// <summary>
        /// The current picture for whoever is on a connection.
        /// </summary>
        /// <remarks>Asked by the character's own network object when its owner starts
        /// observing it, which is the first moment a targeted message can arrive.</remarks>
        public bool TryBuildSnapshot(int connectionId, out InventorySnapshot snapshot)
        {
            snapshot = default;

            if (_characters == null) return false;

            if (!_characters.TryGet(connectionId, out LivingCharacter character))
            {
                return false;
            }

            snapshot = SnapshotOf(character);

            return true;
        }

        /// <summary>
        /// Builds and sends a character their current picture.
        /// </summary>
        /// <remarks>Called after a change and on arrival, so a client that has just entered
        /// the world sees what it owns without asking.</remarks>
        public bool Publish(LivingCharacter character)
        {
            if (character == null || _replication == null) return false;

            if (!_replication.TryGet(character.Character,
                out FishNet.Object.NetworkObject networkObject))
            {
                return false;
            }

            var entity = networkObject == null
                ? null
                : networkObject.GetComponent<CharacterNetworkEntity>();

            if (entity == null) return false;

            entity.ServerPublishInventory(SnapshotOf(character));

            return true;
        }

        /// <summary>
        /// The authoritative picture of what a character owns.
        /// </summary>
        /// <remarks>
        /// Built from the live state every time rather than maintained alongside it, so it
        /// cannot drift. Identity is carried through: the instance id in the snapshot is the
        /// one the server holds, never a new one minted for the wire.
        /// </remarks>
        public InventorySnapshot SnapshotOf(LivingCharacter character)
        {
            var snapshot = new InventorySnapshot
            {
                CharacterId = character == null ? string.Empty : character.Character.Value,
                Capacity = character?.Inventory == null ? 0 : character.Inventory.Capacity,
                Revision = NextRevision(character),
                Items = System.Array.Empty<InventoryItemSnapshot>(),
            };

            if (character == null) return snapshot;

            var items = new List<InventoryItemSnapshot>();

            if (character.Inventory != null)
            {
                IReadOnlyList<ItemSlot> slots = character.Inventory.Slots;

                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Content == null) continue;

                    items.Add(Describe(slots[i].Content, slots[i].Index,
                        Data.EquipmentSlot.None));
                }
            }

            if (character.Equipment != null)
            {
                foreach (KeyValuePair<Data.EquipmentSlot, EquipmentInstance> worn in
                    character.Equipment.Equipped)
                {
                    if (worn.Value == null) continue;

                    items.Add(Describe(worn.Value, -1, worn.Key));
                }
            }

            snapshot.Items = items.ToArray();

            return snapshot;
        }

        // ---- the work ------------------------------------------------------------------

        private CharacterInventoryResult Handle(int connectionId, InventoryAction action,
            int from, int to, int quantity, long sequence)
        {
            if (_characters == null || _items == null)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.MissingContext);
            }

            if (_connections != null && !_connections(connectionId))
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.StaleConnection);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter character))
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.NoCharacter);
            }

            if (character.Inventory == null)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.MissingContext);
            }

            // Before anything is touched: a replayed request costs nothing and changes
            // nothing.
            if (_sequences.TryGetValue(character.Character.Value, out long last)
                && sequence <= last)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.OutOfOrder);
            }

            CharacterInventoryResult result = Perform(character, action, from, to, quantity);

            if (!result.IsAccepted) return result;

            // Only an accepted request consumes the sequence, so a player whose move was
            // refused can try a different one without their next request looking stale.
            _sequences[character.Character.Value] = sequence;

            character.MarkDirty();

            Publish(character);

            return result;
        }

        /// <summary>
        /// Asks the existing service for the action.
        /// </summary>
        /// <remarks>
        /// Every branch is a call into a service that already validates. This method decides
        /// which one to ask and nothing else -- there is no rule here about capacity,
        /// stacking, levels or slots.
        /// </remarks>
        private CharacterInventoryResult Perform(LivingCharacter character,
            InventoryAction action, int from, int to, int quantity)
        {
            switch (action)
            {
                case InventoryAction.Move:
                    return CharacterInventoryResult.FromContainer(
                        character.Inventory.Move(from, to, _items));

                case InventoryAction.Split:
                    return CharacterInventoryResult.FromContainer(
                        character.Inventory.Split(from, quantity, to, _items));

                case InventoryAction.Merge:
                    return CharacterInventoryResult.FromContainer(
                        character.Inventory.Merge(from, to, _items));

                case InventoryAction.Equip:
                    return character.Equipment == null
                        ? CharacterInventoryResult.Refused(
                            InventoryRequestRejection.MissingContext)
                        : CharacterInventoryResult.FromEquip(EquipmentService.Equip(
                            character.Inventory, character.Equipment, from,
                            EquipContext(character)));

                case InventoryAction.Unequip:
                    return character.Equipment == null
                        ? CharacterInventoryResult.Refused(
                            InventoryRequestRejection.MissingContext)
                        : CharacterInventoryResult.FromEquip(EquipmentService.Unequip(
                            character.Inventory, character.Equipment,
                            (Data.EquipmentSlot)from, EquipContext(character)));

                default:
                    return CharacterInventoryResult.Refused(
                        InventoryRequestRejection.UnsupportedAction);
            }
        }

        /// <summary>
        /// The gates a piece of equipment is checked against.
        /// </summary>
        /// <remarks>Level, class and job come from the authoritative character, never from
        /// the request -- which is what stops a level-one client wearing a level-sixty
        /// sword by saying it is level sixty.</remarks>
        private EquipmentService.Context EquipContext(LivingCharacter character)
        {
            return new EquipmentService.Context(_items,
                character.Domain.Progression.Level,
                character.Domain.Class.BaseClass,
                character.Domain.Class.CurrentJob);
        }

        private static InventoryItemSnapshot Describe(GameInstance content, int slot,
            Data.EquipmentSlot equipmentSlot)
        {
            var piece = content as EquipmentInstance;

            return new InventoryItemSnapshot
            {
                Slot = slot,
                EquipmentSlot = (int)equipmentSlot,
                InstanceId = content.InstanceId.Value ?? string.Empty,
                DefinitionId = content.DefinitionId.Value ?? string.Empty,
                Quantity = content is ItemInstance stack ? stack.Quantity : 1,
                LockState = (int)content.LockState,
                EnhancementLevel = piece == null ? 0 : piece.EnhancementLevel,
                RarityId = piece == null ? string.Empty : piece.Rarity.Value ?? string.Empty,
                EnchantCount = piece == null ? 0 : piece.EnchantCount,
                CardCount = piece == null ? 0 : piece.CardCount,
            };
        }

        private int NextRevision(LivingCharacter character)
        {
            if (character == null) return 0;

            string key = character.Character.Value;

            _revisions.TryGetValue(key, out int current);

            current++;

            _revisions[key] = current;

            return current;
        }
    }
}
