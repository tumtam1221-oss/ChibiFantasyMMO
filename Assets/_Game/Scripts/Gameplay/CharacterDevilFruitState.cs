using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Which Devil Fruit a character is carrying the power of.
    /// </summary>
    /// <remarks>
    /// <b>A reference, not a copy of the item.</b> The fruit was and remains a normal
    /// <see cref="ItemInstance"/> in a normal container; the inventory owns it and is the
    /// only authority on ownership. This records which fruit's power is active and which
    /// copy was spent to get it, in four flat fields. Holding the item here instead would
    /// make two places disagree about who owns what the first time either changed.
    ///
    /// <b>One at a time.</b> A character has at most one active fruit. The rule is
    /// structural rather than advisory: there is no list, so a second fruit has nowhere to
    /// go, and <see cref="DevilFruitService"/> refuses rather than replacing. Nothing here
    /// can destroy a fruit a player already ate.
    ///
    /// <b>Flat because it has to persist.</b> One row of a future
    /// <c>character_devil_fruit</c> table is a character, an owner, a fruit id, the instance
    /// id it came from and a revision.
    /// </remarks>
    public sealed class CharacterDevilFruitState : IPersistentState
    {
        private DefinitionId _activeFruit;
        private InstanceId _sourceInstance;
        private Revision _revision;

        public CharacterDevilFruitState(CharacterId characterId = default, OwnerId owner = default)
        {
            CharacterId = characterId;
            Owner = owner;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        /// <summary>Who this belongs to. A server refuses anyone else's activation.</summary>
        public OwnerId Owner { get; }

        public Revision Revision => _revision;

        /// <summary>Reference to the active <see cref="DevilFruitDefinition"/>. Invalid when none.</summary>
        public DefinitionId ActiveFruit => _activeFruit;

        /// <summary>
        /// The item copy that was consumed to activate it.
        /// </summary>
        /// <remarks>Kept for audit rather than for ownership: it names the copy that was
        /// spent, which is what a support ticket or a server log needs. The copy itself is
        /// gone from the container by then.</remarks>
        public InstanceId SourceInstance => _sourceInstance;

        public bool HasActiveFruit => _activeFruit.IsValid;

        /// <summary>
        /// Records an activated fruit and advances the revision.
        /// </summary>
        /// <remarks>
        /// Assignment only, and refuses to overwrite. Whether the character was eligible,
        /// whether the fruit's references resolve and whether the item may be spent are all
        /// <see cref="DevilFruitService"/>'s decisions; this is what it writes once they
        /// pass. Refusing here as well means no caller can bypass the one-fruit rule by
        /// reaching past the service.
        /// </remarks>
        public bool Activate(DefinitionId fruit, InstanceId source)
        {
            if (!fruit.IsValid) return false;
            if (_activeFruit.IsValid) return false;

            _activeFruit = fruit;
            _sourceInstance = source;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Clears the active fruit.
        /// </summary>
        /// <remarks>
        /// Exists so a server can undo an activation it later rejects, and so a future
        /// authored removal mechanic has somewhere to land. It is not a replacement path:
        /// nothing in this phase calls it as a step toward eating a second fruit, because
        /// silently swapping is exactly what the one-fruit rule forbids.
        /// </remarks>
        public bool Deactivate()
        {
            if (!_activeFruit.IsValid) return false;

            _activeFruit = default;
            _sourceInstance = default;
            _revision = _revision.Next();
            return true;
        }
    }
}
