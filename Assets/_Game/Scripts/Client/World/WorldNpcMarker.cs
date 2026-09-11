using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Something a player can walk up to and talk to.
    /// </summary>
    /// <remarks>
    /// <b>Identity is authored on the prefab, never read from the scene.</b> The id is a
    /// serialized field, so a townsperson is the NPC content says it is -- not the one its
    /// GameObject happens to be called, not the one standing nearest a coordinate, and not
    /// the one a switch statement in some UI decided. Renaming the object in the hierarchy
    /// changes nothing, which is the whole point.
    ///
    /// <b>Nothing here talks to the server, and nothing here decides anything.</b> A click
    /// walks the player over through the ordinary movement authority and then raises
    /// <see cref="Interaction"/>. What that is allowed to do is the server's answer; a
    /// marker that opened a shop by itself would be a client deciding it had bought
    /// something.
    ///
    /// <b>It makes sure it can be clicked at all.</b> The NPC art is a plain model with no
    /// collider, so without one the pointer ray passes straight through and the townsperson
    /// is scenery. Added at runtime rather than authored into the art, because the art is
    /// not this project's to edit.
    /// </remarks>
    public sealed class WorldNpcMarker : MonoBehaviour
    {
        [Tooltip("The NPCDefinition id this model stands for. Must match a definition the "
            + "catalogue ships, or the server cannot name this NPC and will refuse every "
            + "interaction with it.")]
        [SerializeField] private string _npcId = string.Empty;

        [Tooltip("Height of the clickable capsule, in metres.")]
        [SerializeField] private float _clickHeight = 1.8f;

        [Tooltip("Radius of the clickable capsule, in metres.")]
        [SerializeField] private float _clickRadius = 0.45f;

        /// <summary>
        /// Which NPC this stands for, as authored content names it.
        /// </summary>
        /// <remarks>Settable so a test can build one without a prefab; serialized so the
        /// real ones do not depend on anything running first.</remarks>
        public string NpcId
        {
            get => _npcId;
            set => _npcId = value ?? string.Empty;
        }

        /// <summary>Whether this marker names an NPC at all.</summary>
        public bool IsIdentified => !string.IsNullOrEmpty(_npcId);

        /// <summary>
        /// Raised once the player has walked into range.
        /// </summary>
        /// <remarks>An unset callback means "arrived, and nobody is listening", which is
        /// what a scene with no interaction presenter in it should do -- nothing.</remarks>
        public System.Action<WorldNpcMarker> Interaction { get; set; }

        /// <summary>How many times a player has reached this NPC. For tests.</summary>
        public int Arrivals { get; private set; }

        /// <summary>Called by the pointer input when the character is close enough.</summary>
        public void Arrive()
        {
            Arrivals++;

            Interaction?.Invoke(this);
        }

        private void Awake()
        {
            EnsureClickable();
        }

        /// <summary>
        /// Gives the model something for a pointer ray to hit.
        /// </summary>
        /// <remarks>
        /// Skipped when the object already has a collider, so an NPC that is one day given a
        /// properly authored shape is not handed a second one. The capsule is a trigger: it
        /// exists to be raycast, and a solid one would push the player away from the very
        /// person they walked over to talk to.
        /// </remarks>
        public void EnsureClickable()
        {
            if (GetComponentInChildren<Collider>() != null) return;

            var capsule = gameObject.AddComponent<CapsuleCollider>();

            capsule.isTrigger = true;
            capsule.height = _clickHeight <= 0f ? 1.8f : _clickHeight;
            capsule.radius = _clickRadius <= 0f ? 0.45f : _clickRadius;
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
        }
    }
}
