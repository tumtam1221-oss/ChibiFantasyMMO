using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>One appearance option picked for a slot during creation.</summary>
    /// <remarks>
    /// A plain pairing so creation input can carry choices before a character exists to
    /// hang them on. It becomes a <see cref="CharacterAppearanceState"/> selection once the
    /// character has an identity.
    /// </remarks>
    public readonly struct AppearanceChoice
    {
        public AppearanceChoice(AppearanceSlot slot, DefinitionId option)
        {
            Slot = slot;
            Option = option;
        }

        public AppearanceSlot Slot { get; }

        /// <summary>Reference to an <see cref="AppearanceOptionDefinition"/>.</summary>
        public DefinitionId Option { get; }
    }
}
