using ChibiFantasy.Data;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Which approved model stands in for a character, and how it is animated.
    /// </summary>
    /// <remarks>
    /// <b>Authored, not coded.</b> No file in this project names a mesh, and this is why:
    /// swapping the male model, adding a female variant or retuning the walk threshold is an
    /// edit to one asset. A presenter that resolved a path by string would put art decisions
    /// in a compiled assembly and break silently the first time an artist renamed a folder.
    ///
    /// <b>Two models, deliberately.</b> Gender is the only appearance the server replicates,
    /// so it is the only appearance this can select on. Hair, face and outfit have no network
    /// representation anywhere in the project -- a remote player is the base model of their
    /// gender, and that limitation is reported rather than faked with a random look.
    ///
    /// <b>Presentation tuning only.</b> The walk speed below is a normalisation constant for
    /// an animator parameter, not a movement rule. The server owns how fast anybody actually
    /// moves and reads none of this.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/Presentation/Character Visual Catalogue",
        fileName = "CharacterVisualCatalogue")]
    public sealed class CharacterVisualCatalogue : ScriptableObject
    {
        [Header("Approved models")]
        [Tooltip("The approved male model. Instanced under the visual root; never modified.")]
        [SerializeField] private GameObject _male;

        [Tooltip("The approved female model.")]
        [SerializeField] private GameObject _female;

        [Tooltip("Used when the server has not said which. Optional: absent means no visual, "
            + "which is honest, rather than guessing a gender.")]
        [SerializeField] private GameObject _fallback;

        [Header("Animation")]
        [Tooltip("The existing locomotion controller. Idle and Walk on one Speed parameter.")]
        [SerializeField] private RuntimeAnimatorController _locomotion;

        [Tooltip("The speed the walk clip depicts, used only to normalise Speed into 0..1.")]
        [SerializeField] private float _referenceWalkSpeed = 1.2f;

        [Tooltip("Metres per second below which the character is presented as standing.")]
        [SerializeField] private float _moveThreshold = 0.05f;

        [Header("Nameplate")]
        [Tooltip("Height above the character root that the name sits at.")]
        [SerializeField] private float _nameplateHeight = 1.9f;

        [Tooltip("Show the local player their own nameplate. Off is the usual MMO choice.")]
        [SerializeField] private bool _showOwnNameplate;

        public GameObject Male => _male;

        public GameObject Female => _female;

        public GameObject Fallback => _fallback;

        public RuntimeAnimatorController Locomotion => _locomotion;

        public float ReferenceWalkSpeed => _referenceWalkSpeed <= 0.0001f
            ? 1f
            : _referenceWalkSpeed;

        public float MoveThreshold => _moveThreshold < 0f ? 0f : _moveThreshold;

        public float NameplateHeight => _nameplateHeight;

        public bool ShowOwnNameplate => _showOwnNameplate;

        /// <summary>
        /// The model for a gender, or the fallback.
        /// </summary>
        /// <remarks>Unspecified is not Male. A zero-valued enum reading as male is exactly
        /// the bug <see cref="CharacterGender.Unspecified"/> exists to prevent, and a
        /// character shown as the wrong gender because a field was never set is a bug a
        /// player reports rather than one anybody notices in a test.</remarks>
        public GameObject ModelFor(CharacterGender gender)
        {
            switch (gender)
            {
                case CharacterGender.Male: return _male != null ? _male : _fallback;
                case CharacterGender.Female: return _female != null ? _female : _fallback;
                default: return _fallback;
            }
        }

        /// <summary>The same, from the numeric value the network entity replicates.</summary>
        public GameObject ModelFor(int genderCode)
        {
            return ModelFor(GenderOf(genderCode));
        }

        /// <summary>
        /// Reads the replicated code back as authored vocabulary.
        /// </summary>
        /// <remarks>A value this build does not recognise reads as unspecified rather than
        /// as whatever enum member happens to share the number -- an older client meeting a
        /// newer server must show a fallback, not a confident wrong answer.</remarks>
        public static CharacterGender GenderOf(int genderCode)
        {
            switch (genderCode)
            {
                case (int)CharacterGender.Male: return CharacterGender.Male;
                case (int)CharacterGender.Female: return CharacterGender.Female;
                default: return CharacterGender.Unspecified;
            }
        }
    }
}
