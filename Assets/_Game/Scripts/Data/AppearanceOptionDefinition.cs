using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One authored appearance option: a face, a pair of eyes, a hairstyle, a hair colour
    /// or a skin tone.
    /// </summary>
    /// <remarks>
    /// <b>One type, not five.</b> Separate FaceDefinition, EyeDefinition and HairDefinition
    /// classes would buy no type safety here, because every reference in this project is a
    /// plain <see cref="DefinitionId"/> by design: an item id and a skill id are already
    /// the same type. Five classes would differ only in their name while a hair id could
    /// still be stored in a face field unnoticed. <see cref="Slot"/> carries that
    /// distinction as data, and <see cref="CharacterAppearanceValidator"/> is what actually
    /// catches a mismatch, so the guarantee lives where it can be enforced rather than
    /// where it merely looks enforced.
    ///
    /// Content, not code. Face01, Hair03 and every option the game ships are assets of this
    /// type; none appears in a class name, an enum or a constant, so the catalogue grows
    /// without a code change.
    ///
    /// <b>Identity versus address.</b> The <see cref="DefinitionId"/> is what a character
    /// persists and is never allowed to change. <see cref="Asset"/> is a presentation
    /// address that a patch may freely repoint at a new mesh or texture. Keeping the two
    /// apart is what lets art be replaced without touching a single saved character.
    /// </remarks>
    public sealed class AppearanceOptionDefinition : GameDefinition
    {
        [SerializeField] private AppearanceSlot _slot = AppearanceSlot.None;
        [SerializeField] private LocalizationKey _nameKey;
        [SerializeField] private GenderAvailability _genderAvailability = GenderAvailability.Any;
        [SerializeField] private AssetRef _asset;
        [SerializeField] private AssetRef _previewIcon;
        [SerializeField] private int _sortOrder;

        /// <summary>The part of the character this option fills.</summary>
        public AppearanceSlot Slot => _slot;

        public LocalizationKey NameKey => _nameKey;

        /// <summary>
        /// Which character genders may choose this option.
        /// </summary>
        /// <remarks>Reuses the existing enum from <see cref="ClassDefinition"/>: the
        /// question is identical, so a second availability vocabulary would be a synonym
        /// that could drift.</remarks>
        public GenderAvailability GenderAvailability => _genderAvailability;

        /// <summary>Presentation address for the mesh, texture or material. Patchable.</summary>
        public AssetRef Asset => _asset;

        /// <summary>Presentation address for the selection thumbnail. Patchable.</summary>
        public AssetRef PreviewIcon => _previewIcon;

        /// <summary>Authored ordering for selection lists. Gaps are allowed.</summary>
        public int SortOrder => _sortOrder;
    }
}
