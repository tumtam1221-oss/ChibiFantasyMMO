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
            ItemContainerResult container, EquipResult equip, ItemUseResult use = default,
            CardSocketResult card = default)
        {
            IsAccepted = accepted;
            Rejection = rejection;
            Container = container;
            Equip = equip;
            Use = use;
            Card = card;
        }

        public bool IsAccepted { get; }

        public InventoryRequestRejection Rejection { get; }

        /// <summary>The container service's own answer, for move, split and merge.</summary>
        public ItemContainerResult Container { get; }

        /// <summary>The equipment service's own answer, for equip and unequip.</summary>
        public EquipResult Equip { get; }

        /// <summary>What using an item did, when the action was a use.</summary>
        public ItemUseResult Use { get; }

        /// <summary>What a socket request did, when that is what was asked.</summary>
        public CardSocketResult Card { get; }

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

        /// <summary>Carries the item-use outcome, refusal reason and all.</summary>
        /// <remarks>The reason is kept rather than flattened to "refused": a player who
        /// already owns a fruit and one whose bag slot was empty need different words, and
        /// the server is the only thing that knows which happened.</remarks>
        public static CharacterInventoryResult FromUse(in ItemUseResult result)
        {
            return new CharacterInventoryResult(result.IsAccepted,
                result.IsAccepted
                    ? InventoryRequestRejection.None
                    : InventoryRequestRejection.Refused,
                default, default, result);
        }

        /// <summary>The outcome of a card going into, or coming out of, a piece.</summary>
        /// <remarks>Shaped exactly like the others: the service already decided, and this
        /// only reports it, so no rule about cards is restated here.</remarks>
        public static CharacterInventoryResult FromCard(in CardSocketResult result)
        {
            return new CharacterInventoryResult(result.IsAccepted,
                result.IsAccepted
                    ? InventoryRequestRejection.None
                    : InventoryRequestRejection.Refused,
                default, default, default, result);
        }

        public override string ToString()
        {
            if (IsAccepted) return "accepted";

            return Rejection == InventoryRequestRejection.Refused
                ? "refused: " + Container + " " + Equip + " " + Use
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
        private readonly IDefinitionRegistry<CardDefinition> _cards;
        private readonly CharacterReplicationService _replication;

        // Content the authored effects of a used item may reach: a fruit to activate, an
        // effect to apply, an ability to check, a map to warp to. All read-only registries;
        // what any of them does is authored on the item, never decided here.
        private readonly IDefinitionRegistry<DevilFruitDefinition> _devilFruits;
        private readonly IDefinitionRegistry<StatusEffectDefinition> _statusEffects;
        private readonly IDefinitionRegistry<SkillDefinition> _skills;
        private readonly IDefinitionRegistry<MapDefinition> _maps;
        private readonly IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;

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
            CharacterReplicationService replication = null,
            IDefinitionRegistry<DevilFruitDefinition> devilFruits = null,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects = null,
            IDefinitionRegistry<SkillDefinition> skills = null,
            IDefinitionRegistry<MapDefinition> maps = null,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints = null,
            IDefinitionRegistry<CardDefinition> cards = null)
        {
            _characters = characters;
            _connections = canAct;
            _items = items;
            _cards = cards;
            _replication = replication;
            _devilFruits = devilFruits;
            _statusEffects = statusEffects;
            _skills = skills;
            _maps = maps;
            _spawnPoints = spawnPoints;
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

            // What they own now travels with what is in their bag, because using a fruit
            // changes both at once and two messages could arrive in either order.
            entity.ServerPublishDevilFruit(character.DevilFruit == null
                ? string.Empty
                : character.DevilFruit.ActiveFruit.Value ?? string.Empty);

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

                case InventoryAction.SocketCard:
                    return SocketCard(character, from, to);

                case InventoryAction.UnsocketCard:
                    return UnsocketCard(character, from, to);

                case InventoryAction.Use:
                    return Use(character, from);

                default:
                    return CharacterInventoryResult.Refused(
                        InventoryRequestRejection.UnsupportedAction);
            }
        }

        /// <summary>
        /// Puts one of this character's cards into one of this character's pieces.
        /// </summary>
        /// <remarks>
        /// <b>Two slots, and everything else is read.</b> The card, what it grants, which
        /// pieces it fits, how many sockets there are and who owns both objects all come
        /// from state the server already holds. A request that could name any of those
        /// would be a request that could invent a modifier.
        ///
        /// <b>Phase 12 decides; this asks.</b> Every rule -- is it a card, is it enabled,
        /// does it fit this piece, is the socket free, is one already in there -- lives in
        /// <see cref="CardSocketService"/>, and the consumption of the card and the writing
        /// of the socket happen together inside it, after everything that can refuse has.
        ///
        /// <b>The piece is named by its inventory slot, so it is one exact instance.</b> Two
        /// swords of the same definition are two objects, and only the one pointed at gets
        /// the card.
        /// </remarks>
        private CharacterInventoryResult SocketCard(LivingCharacter character, int cardSlot,
            int equipmentSlot)
        {
            if (_cards == null || character.Inventory == null)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.MissingContext);
            }

            if (!TryPiece(character, equipmentSlot, out EquipmentInstance piece))
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.Refused);
            }

            CardSocketResult socketed = CardSocketService.TryInsert(character.Inventory,
                cardSlot, piece, CardContext(character));

            // The bag and the piece both changed, and the piece may be worn -- so what the
            // character is worth has to be worked out again by the authority that owns that.
            if (socketed.IsAccepted) character.MarkDirty();

            return CharacterInventoryResult.FromCard(socketed);
        }

        /// <summary>Takes a card back out, if Phase 12's rules allow it.</summary>
        private CharacterInventoryResult UnsocketCard(LivingCharacter character,
            int equipmentSlot, int socketIndex)
        {
            if (_cards == null || character.Inventory == null)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.MissingContext);
            }

            if (!TryPiece(character, equipmentSlot, out EquipmentInstance piece))
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.Refused);
            }

            CardSocketResult removed = CardSocketService.TryRemove(piece, socketIndex,
                character.Inventory, CardContext(character));

            if (removed.IsAccepted) character.MarkDirty();

            return CharacterInventoryResult.FromCard(removed);
        }

        /// <summary>
        /// The registries a socket request is judged against, scoped to this character.
        /// </summary>
        /// <remarks>The owner comes from the character the connection resolved to, never
        /// from the request, which is what stops a player socketing somebody else's card
        /// into somebody else's sword.</remarks>
        /// <summary>The one exact piece of equipment sitting in that inventory slot.</summary>
        /// <remarks>By slot, so it is an instance and not a definition: two swords of the
        /// same kind are two objects, and only the one pointed at is touched.</remarks>
        private static bool TryPiece(LivingCharacter character, int slot,
            out EquipmentInstance piece)
        {
            piece = null;

            if (!character.Inventory.IsValidIndex(slot)) return false;

            piece = character.Inventory.GetSlot(slot).Content as EquipmentInstance;

            return piece != null;
        }

        private CardSocketService.Context CardContext(LivingCharacter character)
        {
            return new CardSocketService.Context(_items, _cards, null, character.Owner);
        }

        /// <summary>
        /// The gates a piece of equipment is checked against.
        /// </summary>
        /// <remarks>Level, class and job come from the authoritative character, never from
        /// the request -- which is what stops a level-one client wearing a level-sixty
        /// sword by saying it is level sixty.</remarks>
        /// <summary>
        /// Uses whatever is in a slot, and lets the item decide what that means.
        /// </summary>
        /// <remarks>
        /// <b>No branch on what the item is.</b> There is no "if this is a Devil Fruit"
        /// here: <see cref="ItemUseService"/> reads the authored effects and this hands it
        /// the state those effects are allowed to touch. A potion, a warp scroll and an
        /// ultra-rare fruit take exactly the same path, which is why adding the eleventh
        /// fruit is content and not code.
        ///
        /// <b>The item is spent by the service, not here.</b> That service validates every
        /// effect before applying any of them, so a fruit a character cannot eat is refused
        /// with the fruit still in the bag -- rather than consumed to discover the refusal.
        /// Splitting the decision across two places is what would make that possible.
        /// </remarks>
        private CharacterInventoryResult Use(LivingCharacter character, int slot)
        {
            if (character.Inventory == null)
            {
                return CharacterInventoryResult.Refused(
                    InventoryRequestRejection.MissingContext);
            }

            var context = new ItemUseService.Context(_items,
                character.Domain.Resources,
                character.Combatant.Limits,
                _statusEffects,
                _maps,
                _spawnPoints,
                character.Owner,
                null,
                _devilFruits,
                character.DevilFruit,
                null,
                _skills,
                character.Status);

            return CharacterInventoryResult.FromUse(
                ItemUseService.Use(character.Inventory, slot, context));
        }

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
