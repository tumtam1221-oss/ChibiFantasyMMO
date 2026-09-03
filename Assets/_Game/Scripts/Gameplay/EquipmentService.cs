using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why an equip or unequip was refused.</summary>
    /// <remarks>
    /// A reason rather than a bare false, matching every other refusal vocabulary in the
    /// project. Each value is something a player can be told and a server can act on.
    /// </remarks>
    public enum EquipRejection
    {
        None = 0,

        /// <summary>No inventory, equipment state or registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>The slot index is outside the inventory.</summary>
        SlotOutOfRange = 2,

        /// <summary>The inventory slot holds nothing.</summary>
        SourceEmpty = 3,

        /// <summary>No definition exists for what is held.</summary>
        UnknownDefinition = 4,

        /// <summary>The item is not equipment.</summary>
        NotEquipment = 5,

        /// <summary>The equipment authors no slot to go in.</summary>
        NoTargetSlot = 6,

        /// <summary>The character has not reached the level the piece demands.</summary>
        LevelTooLow = 7,

        /// <summary>The piece is restricted to classes this character is not.</summary>
        ClassNotAllowed = 8,

        /// <summary>The piece is restricted to jobs this character does not hold.</summary>
        JobNotAllowed = 9,

        /// <summary>Nothing is worn in that slot.</summary>
        SlotEmpty = 10,

        /// <summary>There is nowhere in the inventory to put what comes off.</summary>
        InventoryFull = 11
    }

    /// <summary>The answer to an equip or unequip request.</summary>
    public readonly struct EquipResult
    {
        private EquipResult(bool accepted, EquipRejection reason, EquipmentSlot slot,
            InstanceId equipped, InstanceId returned)
        {
            IsAccepted = accepted;
            Reason = reason;
            Slot = slot;
            EquippedInstance = equipped;
            ReturnedInstance = returned;
        }

        public bool IsAccepted { get; }

        public EquipRejection Reason { get; }

        /// <summary>The slot involved.</summary>
        public EquipmentSlot Slot { get; }

        /// <summary>What is now worn, or none.</summary>
        public InstanceId EquippedInstance { get; }

        /// <summary>What went back to the inventory, or none.</summary>
        public InstanceId ReturnedInstance { get; }

        public static EquipResult Accepted(EquipmentSlot slot, InstanceId equipped,
            InstanceId returned = default)
        {
            return new EquipResult(true, EquipRejection.None, slot, equipped, returned);
        }

        public static EquipResult Rejected(EquipRejection reason,
            EquipmentSlot slot = EquipmentSlot.None)
        {
            return new EquipResult(false, reason, slot, default, default);
        }

        public override string ToString()
        {
            return IsAccepted
                ? "equipped " + EquippedInstance + " in " + Slot
                  + (ReturnedInstance.IsValid ? " (returned " + ReturnedInstance + ")" : string.Empty)
                : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Moves equipment between the inventory and the worn slots.
    /// </summary>
    /// <remarks>
    /// <b>The only writer into equipped state.</b> <see cref="CharacterEquipmentState"/>
    /// keeps its mutators internal so this is the one path, and this validates everything
    /// before touching either side.
    ///
    /// <b>Nothing is ever destroyed.</b> Equipping into an occupied slot needs somewhere
    /// for the old piece to go, so a full inventory refuses the swap rather than dropping
    /// what the character was wearing. Unequipping into a full inventory refuses for the
    /// same reason. Both are checked before the first mutation, so a refusal leaves the
    /// inventory and the equipment byte-identical.
    ///
    /// <b>Restrictions are content.</b> The level gate and the class and job lists are read
    /// from <see cref="EquipmentDefinition"/>. No item is named here, no class is named
    /// here, and there is no branch on what kind of thing is being worn.
    ///
    /// <b>Request in, result out.</b> The shape is deliberately the one a server command
    /// would take: an instance or slot to act on, and an accepted-or-refused answer with a
    /// reason. Nothing mutates state from a caller's side effect.
    /// </remarks>
    public static class EquipmentService
    {
        /// <summary>
        /// Everything the rules need that is not in the request.
        /// </summary>
        /// <remarks>Level, class and job belong to the character and
        /// <see cref="CharacterEquipmentState"/> cannot see them, so the caller supplies
        /// them. That mirrors <see cref="SkillUseContext"/> and keeps the state types free
        /// of a character reference they would otherwise have to hold.</remarks>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items, int characterLevel,
                DefinitionId characterClass = default, DefinitionId characterJob = default)
            {
                Items = items;
                CharacterLevel = characterLevel;
                CharacterClass = characterClass;
                CharacterJob = characterJob;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public int CharacterLevel { get; }

            /// <summary>The character's base class, or none to skip the class gate.</summary>
            public DefinitionId CharacterClass { get; }

            /// <summary>The character's current job, or none to skip the job gate.</summary>
            public DefinitionId CharacterJob { get; }
        }

        /// <summary>
        /// Wears the equipment in an inventory slot.
        /// </summary>
        /// <remarks>A piece already worn in the target slot goes back to the inventory,
        /// into the slot the new piece just vacated, so the swap needs no extra space.</remarks>
        public static EquipResult Equip(ItemContainerState inventory,
            CharacterEquipmentState equipment, int inventorySlot, in Context context)
        {
            if (inventory == null || equipment == null || context.Items == null)
                return EquipResult.Rejected(EquipRejection.MissingContext);

            if (!inventory.IsValidIndex(inventorySlot))
                return EquipResult.Rejected(EquipRejection.SlotOutOfRange);

            ItemSlot source = inventory.GetSlot(inventorySlot);
            if (source.IsEmpty) return EquipResult.Rejected(EquipRejection.SourceEmpty);

            var piece = source.Content as EquipmentInstance;
            if (piece == null) return EquipResult.Rejected(EquipRejection.NotEquipment);

            ItemDefinition definition;
            if (!context.Items.TryGet(piece.DefinitionId, out definition) || definition == null)
                return EquipResult.Rejected(EquipRejection.UnknownDefinition);

            var authored = definition as EquipmentDefinition;
            if (authored == null) return EquipResult.Rejected(EquipRejection.NotEquipment);

            if (authored.Slot == EquipmentSlot.None)
                return EquipResult.Rejected(EquipRejection.NoTargetSlot);

            EquipRejection gate = CheckRestrictions(authored, context);
            if (gate != EquipRejection.None) return EquipResult.Rejected(gate, authored.Slot);

            // Everything is checked. From here nothing can fail.
            EquipmentInstance previous;
            equipment.TryGet(authored.Slot, out previous);

            inventory.RemoveAt(inventorySlot, source.Quantity);
            equipment.Set(authored.Slot, piece);

            if (previous == null)
            {
                return EquipResult.Accepted(authored.Slot, piece.InstanceId);
            }

            // The vacated slot is guaranteed free, so the old piece always has a home.
            inventory.PlaceAt(inventorySlot, previous);
            return EquipResult.Accepted(authored.Slot, piece.InstanceId, previous.InstanceId);
        }

        /// <summary>
        /// Takes a worn piece off and puts it in the inventory.
        /// </summary>
        /// <remarks>Refused when there is nowhere to put it. The alternative -- removing it
        /// anyway -- would either destroy the piece or leave it in no container at all.</remarks>
        public static EquipResult Unequip(ItemContainerState inventory,
            CharacterEquipmentState equipment, EquipmentSlot slot, in Context context)
        {
            if (inventory == null || equipment == null || context.Items == null)
                return EquipResult.Rejected(EquipRejection.MissingContext);

            EquipmentInstance worn;
            if (!equipment.TryGet(slot, out worn) || worn == null)
                return EquipResult.Rejected(EquipRejection.SlotEmpty, slot);

            int destination = inventory.FirstEmptySlot();
            if (destination < 0) return EquipResult.Rejected(EquipRejection.InventoryFull, slot);

            equipment.Remove(slot);
            inventory.PlaceAt(destination, worn);

            return EquipResult.Accepted(slot, default, worn.InstanceId);
        }

        /// <summary>
        /// Checks the authored gates.
        /// </summary>
        /// <remarks>An empty allow-list means unrestricted, which is what the schema
        /// documents; a supplied identity of none skips that gate, so a caller that does
        /// not track jobs yet is not forced to invent one.</remarks>
        private static EquipRejection CheckRestrictions(EquipmentDefinition equipment,
            in Context context)
        {
            if (context.CharacterLevel < equipment.LevelRequirement)
                return EquipRejection.LevelTooLow;

            if (!Permits(equipment.AllowedClasses, context.CharacterClass))
                return EquipRejection.ClassNotAllowed;

            if (!Permits(equipment.AllowedJobs, context.CharacterJob))
                return EquipRejection.JobNotAllowed;

            return EquipRejection.None;
        }

        private static bool Permits(DefinitionId[] allowed, DefinitionId candidate)
        {
            if (allowed == null || allowed.Length == 0) return true;
            if (!candidate.IsValid) return true;

            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == candidate) return true;
            }

            return false;
        }
    }
}
