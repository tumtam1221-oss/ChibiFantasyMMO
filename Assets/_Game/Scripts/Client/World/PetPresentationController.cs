using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Moves and shows the pet a character has out.
    /// </summary>
    /// <remarks>
    /// <b>One controller for every pet.</b> There is no wolf controller and no floating-pet
    /// controller. How a pet positions itself is
    /// <see cref="PetDefinition.FollowBehavior"/>, how high it sits is
    /// <see cref="PetDefinition.VerticalOffset"/>, and whether it appears at all is
    /// <see cref="PetCompanionState.IsAuraForm"/>. A sixth pet needs no code.
    ///
    /// <b>It obeys; it never decides.</b> Whether the pet is out, what mode it is in and
    /// whether it has become an aura were all settled by <see cref="PetService"/>. This reads
    /// that state and turns it into a transform, which is the one thing gameplay cannot do --
    /// and the reason gameplay holds no position for a pet at all.
    ///
    /// <b>The aura form is an absence, not a second object.</b> When the state says aura, the
    /// follower is hidden and the aura effect is shown on the owner. No stand-in pet object
    /// is created, so nothing that only the client can see is ever the authoritative answer
    /// to where a pet is.
    ///
    /// Transforms are updated per frame because that is what smooth movement is; no gameplay
    /// state is read per frame beyond the mode, and nothing is scanned or rebuilt.
    /// </remarks>
    public sealed class PetPresentationController : MonoBehaviour
    {
        [Tooltip("Whom the pet follows.")]
        [SerializeField] private Transform owner;

        [Tooltip("The follower's root. Hidden while the pet is an aura or dismissed.")]
        [SerializeField] private Transform follower;

        [Tooltip("Shown on the owner while the pet is an aura form.")]
        [SerializeField] private GameObject auraVisual;

        [Tooltip("How far behind the owner the pet settles.")]
        [SerializeField] private float followDistance = 2f;

        [Tooltip("How fast it closes the gap.")]
        [SerializeField] private float followSpeed = 4f;

        [Tooltip("Distance past which Return is preferred over Follow.")]
        [SerializeField] private float returnDistance = 12f;

        private PetCompanionState _companion;
        private IDefinitionRegistry<PetDefinition> _pets;

        /// <summary>
        /// The pet the server says is out, for a character whose state this client has not
        /// got. Empty when none is.
        /// </summary>
        /// <remarks>
        /// <b>Two sources, one presenter.</b> The owner's own client holds real
        /// <see cref="PetCompanionState"/>; every other viewer has only what was replicated.
        /// Both answer the same question -- which authored pet is out -- so both drive the
        /// same follower rather than a second controller for remote pets.
        ///
        /// <b>It is read, never written back.</b> Nothing here can summon, dismiss or move
        /// anybody's pet: the value arrives from the server and the follower is drawn from
        /// it. A viewer that disagreed would simply be drawing the wrong thing on its own
        /// screen, which is what presentation already is.
        /// </remarks>
        private DefinitionId _replicated;
        private bool _hasReplicated;

        /// <summary>The behaviour of the pet currently out. Follow when nothing is out.</summary>
        public PetFollowBehavior Behavior { get; private set; } = PetFollowBehavior.Follow;

        /// <summary>The authored height the current pet floats at.</summary>
        public float VerticalOffset { get; private set; }

        /// <summary>
        /// Whether the pet that is out is an aura rather than a follower.
        /// </summary>
        /// <remarks>
        /// <b>Authored, never inferred.</b> Read off the definition the pet currently is --
        /// which, after an evolution, is the evolved form. Nothing here decides that a pet
        /// is far enough along to be an aura; the server says which form is out and the
        /// content says what that form looks like.
        ///
        /// <b>Exactly one mode.</b> A follower and an aura are never both shown: this is the
        /// switch <see cref="Apply"/> makes, and a viewer that drew both would be drawing a
        /// creature the world does not contain.
        /// </remarks>
        public bool IsAuraForm { get; private set; }

        /// <summary>Points the presenter at the state it draws.</summary>
        public void Bind(PetCompanionState companion, IDefinitionRegistry<PetDefinition> pets,
            Transform ownerRoot = null)
        {
            _companion = companion;
            _pets = pets;
            if (ownerRoot != null) owner = ownerRoot;

            ReadDefinition();
            Apply();
        }

        /// <summary>
        /// Shows the pet the server says this character has out.
        /// </summary>
        /// <remarks>
        /// The presentation seam for a character this client does not own. It takes an
        /// authored id and nothing else -- no position, no offset, no behaviour -- because
        /// every one of those is either content this looks up or something only the server
        /// may decide. An empty id means nothing is out, which is how putting a pet away
        /// arrives.
        /// </remarks>
        /// <param name="petDefinitionId">The authored pet, or empty for none.</param>
        public void PresentReplicated(string petDefinitionId,
            IDefinitionRegistry<PetDefinition> pets = null)
        {
            if (pets != null) _pets = pets;

            _replicated = new DefinitionId(petDefinitionId ?? string.Empty);
            _hasReplicated = _replicated.IsValid;

            ReadDefinition();
            Apply();
        }

        /// <summary>Whether a pet should be drawn beside this owner at all.</summary>
        /// <remarks>Either source can say so; neither invents one. Aura forms draw no
        /// follower, which is <see cref="PetCompanionState.IsAuraForm"/>'s answer and not
        /// one this makes up.</remarks>
        public bool IsOut => (_companion != null && _companion.IsSummoned) || _hasReplicated;

        /// <summary>
        /// Reads the summoned pet's authored presentation values.
        /// </summary>
        /// <remarks>Called when the pet changes rather than every frame: a definition lookup
        /// per frame would be a registry scan for a value that only moves on summon and
        /// evolution.</remarks>
        public void ReadDefinition()
        {
            Behavior = PetFollowBehavior.Follow;
            VerticalOffset = 0f;
            IsAuraForm = false;

            if (_pets == null) return;

            // The owner's own state when this client has it, the replicated id otherwise.
            // Both name the same authored definition, so the lookup below is one path.
            DefinitionId id = _replicated;

            if (_companion != null && _companion.IsSummoned && _companion.Summoned != null)
            {
                id = _companion.Summoned.DefinitionId;
            }

            if (!id.IsValid) return;

            PetDefinition definition;
            if (!_pets.TryGet(id, out definition) || definition == null) return;

            Behavior = definition.FollowBehavior;
            VerticalOffset = definition.VerticalOffset;

            // The form decides the mode. An owner's own companion state says the same
            // thing, and is preferred when this client has it, because it is the state the
            // server actually mutated rather than a lookup of what it named.
            IsAuraForm = _companion != null && _companion.IsSummoned
                ? _companion.IsAuraForm
                : definition.IsAuraForm;
        }

        /// <summary>
        /// Shows or hides the follower and the aura to match the state.
        /// </summary>
        /// <remarks>Public so a caller can apply a change the moment one happens, rather than
        /// waiting a frame for <see cref="Update"/>.</remarks>
        public void Apply()
        {
            bool out_ = IsOut;
            bool aura = out_ && IsAuraForm;

            if (follower != null) follower.gameObject.SetActive(out_ && !aura);
            if (auraVisual != null) auraVisual.SetActive(aura);
        }

        /// <summary>Reacts to something the collectible systems reported.</summary>
        /// <remarks>The event carries ids only, so this re-reads state rather than trusting
        /// anything the event might have carried about where the pet is.</remarks>
        public void OnPresented(CollectiblePresentationEvent published)
        {
            switch (published.Kind)
            {
                case CollectibleEventKind.PetSummoned:
                case CollectibleEventKind.PetEvolved:
                case CollectibleEventKind.PetAuraActivated:
                case CollectibleEventKind.PetDismissed:
                    ReadDefinition();
                    Apply();
                    break;
            }
        }

        private void Update()
        {
            if (!IsOut) return;
            if (follower == null || owner == null) return;

            // An aura is on the owner, not beside them: there is no follower to move.
            if (IsAuraForm) return;

            Move(Time.deltaTime);
        }

        /// <summary>
        /// Moves the follower one step.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Update"/> and taking its own delta, so the movement can be
        /// exercised without a frame. The mode decides the target and the authored behaviour
        /// decides the offset; neither is branched on a pet's identity.
        /// </remarks>
        public void Move(float deltaSeconds)
        {
            if (follower == null || owner == null || deltaSeconds <= 0f) return;

            // A viewer without the owner's state has no mode to read. Follow is what a
            // pet beside somebody looks like, and the server's position is what it is
            // actually doing -- this only keeps the visual from standing still.
            PetFollowMode mode = _companion != null
                ? _companion.Mode
                : (_hasReplicated ? PetFollowMode.Follow : PetFollowMode.Idle);

            if (mode == PetFollowMode.Idle) return;

            Vector3 target = TargetPosition(mode);

            float speed = mode == PetFollowMode.Return ? followSpeed * 2f : followSpeed;

            follower.position = Vector3.MoveTowards(follower.position, target,
                speed * deltaSeconds);
        }

        /// <summary>Where the follower is trying to be.</summary>
        public Vector3 TargetPosition(PetFollowMode mode)
        {
            if (owner == null) return follower == null ? Vector3.zero : follower.position;

            Vector3 anchor = owner.position;

            // Returning aims at the owner directly; following settles into the authored
            // formation offset. Both then take the pet's authored height.
            if (mode != PetFollowMode.Return)
            {
                anchor += FormationOffset();
            }

            anchor.y += VerticalOffset;
            return anchor;
        }

        /// <summary>
        /// Where the pet sits relative to its owner, by authored behaviour.
        /// </summary>
        /// <remarks>A switch over a closed technical category, not over a pet. Adding a pet
        /// that orbits needs no code because <see cref="PetFollowBehavior.Orbit"/> already
        /// has a case.</remarks>
        private Vector3 FormationOffset()
        {
            switch (Behavior)
            {
                case PetFollowBehavior.Shoulder:
                    return owner.right * 0.5f;

                case PetFollowBehavior.Orbit:
                    return owner.right * followDistance;

                case PetFollowBehavior.Stationary:
                    return Vector3.zero;

                default:
                    return -owner.forward * followDistance;
            }
        }

        /// <summary>
        /// Whether the pet has fallen far enough behind to warrant returning.
        /// </summary>
        /// <remarks>An observation the client offers; setting the mode is
        /// <see cref="PetCompanionState.SetMode"/>'s job through the controller, so
        /// presentation still does not decide gameplay state.</remarks>
        public bool IsFarFromOwner()
        {
            if (follower == null || owner == null) return false;

            return (follower.position - owner.position).sqrMagnitude
                > returnDistance * returnDistance;
        }
    }
}
