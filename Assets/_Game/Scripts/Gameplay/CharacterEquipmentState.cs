using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What a character is wearing.
    /// </summary>
    /// <remarks>
    /// <b>One occupant per slot, keyed by the existing enum.</b>
    /// <see cref="EquipmentSlot"/> was authored in Phase 04 with the ten slots the game
    /// has; no slot list is defined here and no new one was invented.
    ///
    /// <b>Persistent.</b> What a character is wearing survives a restart, so this is
    /// <see cref="IPersistentState"/> alongside the containers.
    ///
    /// <b>State, not rules.</b> Nothing here decides whether a character <em>may</em> wear
    /// something: level and class gates live on <see cref="EquipmentDefinition"/> and are
    /// checked by <see cref="EquipmentService"/>. This records what it is told, which is
    /// the same division <see cref="CharacterSkillsState"/> already draws.
    ///
    /// <b>It computes no stats.</b> <see cref="CollectModifiers"/> gathers the authored
    /// <see cref="StatModifier"/> values off the equipped definitions and hands them to the
    /// existing <see cref="DerivedStatsCalculator"/>, which is the only thing that turns
    /// modifiers into numbers. No second stat system, and no resolver.
    /// </remarks>
    public sealed class CharacterEquipmentState : IPersistentState
    {
        private readonly Dictionary<EquipmentSlot, EquipmentInstance> _equipped =
            new Dictionary<EquipmentSlot, EquipmentInstance>();

        private Revision _revision;

        public CharacterEquipmentState(CharacterId characterId)
        {
            CharacterId = characterId;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        public Revision Revision => _revision;

        /// <summary>How many slots are filled.</summary>
        public int Count => _equipped.Count;

        public bool IsOccupied(EquipmentSlot slot) => _equipped.ContainsKey(slot);

        /// <summary>Reads a slot. False when nothing is worn there.</summary>
        public bool TryGet(EquipmentSlot slot, out EquipmentInstance instance)
        {
            return _equipped.TryGetValue(slot, out instance);
        }

        /// <summary>Every worn piece, for iteration.</summary>
        public IEnumerable<KeyValuePair<EquipmentSlot, EquipmentInstance>> Equipped => _equipped;

        /// <summary>Finds the slot an instance is worn in, or <see cref="EquipmentSlot.None"/>.</summary>
        public EquipmentSlot SlotOf(InstanceId instanceId)
        {
            if (!instanceId.IsValid) return EquipmentSlot.None;

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> pair in _equipped)
            {
                if (pair.Value != null && pair.Value.InstanceId == instanceId) return pair.Key;
            }

            return EquipmentSlot.None;
        }

        /// <summary>
        /// Puts a piece in a slot, replacing whatever was there.
        /// </summary>
        /// <remarks>Internal because equipping has rules. <see cref="EquipmentService"/>
        /// validates and is the only caller; letting anything else write here would put a
        /// second, unchecked path into the equipped state.</remarks>
        internal EquipmentInstance Set(EquipmentSlot slot, EquipmentInstance instance)
        {
            EquipmentInstance previous;
            _equipped.TryGetValue(slot, out previous);

            if (instance == null) _equipped.Remove(slot);
            else _equipped[slot] = instance;

            _revision = _revision.Next();
            return previous;
        }

        /// <summary>Empties a slot, returning what was there.</summary>
        internal EquipmentInstance Remove(EquipmentSlot slot)
        {
            EquipmentInstance previous;

            if (!_equipped.TryGetValue(slot, out previous)) return null;

            _equipped.Remove(slot);
            _revision = _revision.Next();
            return previous;
        }

        /// <summary>
        /// Gathers the modifiers every worn piece grants.
        /// </summary>
        /// <remarks>
        /// <b>Collects, never computes.</b> The values come straight off
        /// <see cref="EquipmentDefinition.BaseStatModifiers"/> as authored. How they stack,
        /// round and clamp is <see cref="DerivedStatsCalculator"/>'s job and is not
        /// duplicated here, which is why equipping a sword changes effective stats without
        /// a single line of stat arithmetic in this file.
        ///
        /// Enhancement, rarity and sockets are deliberately not read by <em>this</em>
        /// overload. It is the base-only view and its behaviour is frozen: callers that
        /// predate equipment progression keep getting exactly what they got before. The
        /// overload taking an <see cref="EquipmentModifierResolver.Context"/> is the one
        /// that includes them, and a caller opts in by supplying the registries it needs.
        ///
        /// The list is rebuilt from the equipped set every call, so it cannot drift and a
        /// double equip cannot leave a stale contribution behind.
        /// </remarks>
        public void CollectModifiers(IDefinitionRegistry<ItemDefinition> items,
            List<StatModifier> into)
        {
            if (into == null) return;

            into.Clear();

            if (items == null) return;

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> pair in _equipped)
            {
                EquipmentInstance worn = pair.Value;
                if (worn == null || !worn.DefinitionId.IsValid) continue;

                ItemDefinition definition;
                if (!items.TryGet(worn.DefinitionId, out definition)) continue;

                var equipment = definition as EquipmentDefinition;
                if (equipment == null) continue;

                StatModifier[] modifiers = equipment.BaseStatModifiers;
                if (modifiers == null) continue;

                for (int i = 0; i < modifiers.Length; i++) into.Add(modifiers[i]);
            }
        }

        /// <summary>Convenience overload that allocates the list.</summary>
        public List<StatModifier> CollectModifiers(IDefinitionRegistry<ItemDefinition> items)
        {
            var list = new List<StatModifier>();
            CollectModifiers(items, list);
            return list;
        }

        /// <summary>
        /// Gathers every modifier the worn set grants, progression included.
        /// </summary>
        /// <remarks>
        /// <b>The same seam, widened.</b> This is the base-only overload plus rarity,
        /// enhancement and socketed stones, delegated per piece to
        /// <see cref="EquipmentModifierResolver"/>. It performs no stat arithmetic, and
        /// <see cref="DerivedStatsCalculator"/> is still the only thing that decides how
        /// modifiers combine -- so equipment progression reaches effective stats without a
        /// second stat pipeline existing.
        ///
        /// <b>Rebuilt from the equipped set every call.</b> That is what makes drift
        /// impossible: nothing is remembered between calls, so unequipping and re-equipping
        /// cannot double a contribution, and enhancing from +1 to +5 replaces the
        /// contribution rather than adding to it.
        /// </remarks>
        public void CollectModifiers(in EquipmentModifierResolver.Context context,
            List<StatModifier> into)
        {
            if (into == null) return;

            into.Clear();

            if (!context.IsUsable) return;

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> pair in _equipped)
            {
                EquipmentModifierResolver.Collect(pair.Value, context, into);
            }
        }

        /// <summary>Convenience overload that allocates the list.</summary>
        public List<StatModifier> CollectModifiers(in EquipmentModifierResolver.Context context)
        {
            var list = new List<StatModifier>();
            CollectModifiers(context, list);
            return list;
        }

        /// <summary>Removes everything. Returns what was worn so a caller can rehome it.</summary>
        internal List<EquipmentInstance> ClearAll()
        {
            var removed = new List<EquipmentInstance>(_equipped.Count);

            foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> pair in _equipped)
            {
                if (pair.Value != null) removed.Add(pair.Value);
            }

            if (_equipped.Count > 0)
            {
                _equipped.Clear();
                _revision = _revision.Next();
            }

            return removed;
        }
    }
}
