using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Turns the server's inventory snapshot into the view data the panels already draw.
    /// </summary>
    /// <remarks>
    /// <b>It holds no inventory.</b> There is no <c>ItemContainerState</c> on a client and
    /// deliberately so: the existing <see cref="InventoryUiController"/> binds to a live
    /// container and mutates it, which is the right model for the single-player prototype it
    /// was written for and the wrong one here. A networked client owns nothing -- it is shown
    /// a picture and it asks for changes. So this projects the picture and forwards the
    /// asking, and the two never blur.
    ///
    /// <b>Nothing moves until the server says so.</b> A click sends a request and returns;
    /// the panel changes when the next snapshot arrives, because that is the only thing that
    /// tells the truth. Moving the icon first would look faster and be a lie whenever the
    /// server disagreed -- and a player who saw their sword equip and then jump back would
    /// trust the game less than one who waited a frame.
    ///
    /// <b>The view data is the existing view data.</b> <see cref="ItemSlotViewData"/> and
    /// <see cref="EquipmentSlotViewData"/> already know how to describe a square, and their
    /// id-based factories need only the authored definition -- which a client has. No second
    /// slot model was written.
    /// </remarks>
    public sealed class NetworkInventoryPresenter
    {
        /// <summary>Every equipment slot a paperdoll draws, in a stable order.</summary>
        /// <remarks>Authored order rather than <c>Enum.GetValues</c>, so the panel's layout
        /// does not silently rearrange when somebody adds a slot to the enum.</remarks>
        private static readonly EquipmentSlot[] PaperdollOrder =
        {
            EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs, EquipmentSlot.Feet,
            EquipmentSlot.Hands, EquipmentSlot.MainHand, EquipmentSlot.OffHand,
            EquipmentSlot.Accessory, EquipmentSlot.Cape,
        };

        private readonly IDefinitionRegistry<ItemDefinition> _items;

        private readonly List<ItemSlotViewData> _bag = new List<ItemSlotViewData>();
        private readonly List<EquipmentSlotViewData> _worn = new List<EquipmentSlotViewData>();

        private CharacterNetworkEntity _entity;
        private long _sequence;

        /// <param name="items">
        /// Authored items, for icons and names. Content, which a client has locally -- the
        /// snapshot carries ids precisely so that definitions do not cross the wire.
        /// </param>
        public NetworkInventoryPresenter(IDefinitionRegistry<ItemDefinition> items)
        {
            _items = items;
        }

        /// <summary>The bag, one entry per slot including the empty ones.</summary>
        public IReadOnlyList<ItemSlotViewData> Bag => _bag;

        /// <summary>The paperdoll, one entry per slot including the empty ones.</summary>
        public IReadOnlyList<EquipmentSlotViewData> Worn => _worn;

        /// <summary>Which character this is showing, as the server named it.</summary>
        public CharacterId Character { get; private set; }

        /// <summary>The revision of the snapshot on screen. The server's number.</summary>
        public int Revision { get; private set; }

        /// <summary>Whether a snapshot has arrived at all.</summary>
        /// <remarks>False is a real state a screen must show: "waiting for the server" is
        /// honest, and an empty bag drawn before anything arrived is not.</remarks>
        public bool HasSnapshot { get; private set; }

        /// <summary>Raised when the projection changes, so a panel can redraw once.</summary>
        public event System.Action Changed;

        /// <summary>
        /// Binds the local player's own character object.
        /// </summary>
        /// <remarks>
        /// <b>Only the one this client owns.</b> A remote player's object is observed and
        /// carries no snapshot -- the server never sends one -- so binding it would show an
        /// empty bag that looks like a bug. Refusing outright is clearer, and it is a second
        /// lock on the privacy the server already enforces.
        /// </remarks>
        public bool Bind(CharacterNetworkEntity entity)
        {
            Unbind();

            if (entity == null || !entity.IsOwner) return false;

            _entity = entity;
            _entity.InventoryChanged += OnInventoryChanged;

            // A snapshot may already have arrived before anything asked to draw it.
            if (entity.Inventory.Count > 0 || !string.IsNullOrEmpty(entity.Inventory.CharacterId))
            {
                OnInventoryChanged(entity.Inventory);
            }

            return true;
        }

        /// <summary>Releases the character, for a despawn or a scene change.</summary>
        /// <remarks>Unsubscribing matters: a destroyed panel still holding an event is the
        /// usual source of null-reference noise after a disconnect.</remarks>
        public void Unbind()
        {
            if (_entity != null) _entity.InventoryChanged -= OnInventoryChanged;

            _entity = null;

            _bag.Clear();
            _worn.Clear();

            Character = default;
            Revision = 0;
            HasSnapshot = false;
        }

        // ---- asking the server -------------------------------------------------------------

        /// <summary>Asks to wear what is in a bag slot.</summary>
        /// <remarks>Returns whether the request was sent, never whether it succeeded --
        /// that answer arrives as a snapshot.</remarks>
        public bool RequestEquip(int bagSlot)
        {
            return Send(InventoryAction.Equip, bagSlot, 0, 0);
        }

        /// <summary>Asks to take off what is worn in a slot.</summary>
        public bool RequestUnequip(EquipmentSlot slot)
        {
            return Send(InventoryAction.Unequip, (int)slot, 0, 0);
        }

        /// <summary>Asks to move an item between bag slots.</summary>
        public bool RequestMove(int from, int to)
        {
            return Send(InventoryAction.Move, from, to, 0);
        }

        /// <summary>Asks to split part of a stack into another slot.</summary>
        public bool RequestSplit(int from, int to, int quantity)
        {
            return Send(InventoryAction.Split, from, to, quantity);
        }

        /// <summary>Asks to merge one stack into another.</summary>
        public bool RequestMerge(int from, int to)
        {
            return Send(InventoryAction.Merge, from, to, 0);
        }

        private bool Send(InventoryAction action, int from, int to, int quantity)
        {
            if (_entity == null || !_entity.IsOwner) return false;

            _entity.RequestInventoryAction(action, from, to, quantity, ++_sequence);

            return true;
        }

        // ---- projecting what arrived ----------------------------------------------------------

        /// <summary>
        /// Rebuilds the view from a snapshot, wholesale.
        /// </summary>
        /// <remarks>
        /// Replacement rather than a patch, matching how the server sends it. A client that
        /// applied deltas would be maintaining an inventory of its own, which is the thing
        /// this design exists to avoid -- and it would drift the first time a message was
        /// missed.
        /// </remarks>
        private void OnInventoryChanged(InventorySnapshot snapshot)
        {
            Character = new CharacterId(snapshot.CharacterId ?? string.Empty);
            Revision = snapshot.Revision;
            HasSnapshot = true;

            _bag.Clear();

            for (int i = 0; i < snapshot.Capacity; i++) _bag.Add(ItemSlotViewData.Empty(i));

            _worn.Clear();

            for (int i = 0; i < PaperdollOrder.Length; i++)
            {
                _worn.Add(EquipmentSlotViewData.Empty(PaperdollOrder[i]));
            }

            if (snapshot.Items != null)
            {
                for (int i = 0; i < snapshot.Items.Length; i++) Place(snapshot.Items[i]);
            }

            Changed?.Invoke();
        }

        private void Place(in InventoryItemSnapshot item)
        {
            var definitionId = new DefinitionId(item.DefinitionId ?? string.Empty);

            ItemDefinition definition = null;

            _items?.TryGet(definitionId, out definition);

            if (item.IsEquipped)
            {
                var slot = (EquipmentSlot)item.EquipmentSlot;

                for (int i = 0; i < _worn.Count; i++)
                {
                    if (_worn[i].Slot != slot) continue;

                    _worn[i] = EquipmentSlotViewData.From(slot, definitionId,
                        new InstanceId(item.InstanceId ?? string.Empty), definition);

                    return;
                }

                // A slot the paperdoll does not draw. Ignored rather than forced somewhere,
                // because putting a cape in the helmet square would be worse than not
                // showing it.
                return;
            }

            if (item.Slot < 0 || item.Slot >= _bag.Count) return;

            _bag[item.Slot] = ItemSlotViewData.From(item.Slot, definitionId,
                new InstanceId(item.InstanceId ?? string.Empty), item.Quantity, definition);
        }
    }
}
