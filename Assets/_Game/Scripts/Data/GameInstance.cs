using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Base for runtime, player-owned state.
    /// </summary>
    /// <remarks>
    /// Inheritance is used rather than composition because every instance carries exactly
    /// the same four identity members with the same rules, and a shared base lets the
    /// revision counter be advanced in one place that subclasses cannot forget.
    ///
    /// Plain serializable C#: not a MonoBehaviour, not a NetworkBehaviour, not a
    /// ScriptableObject. It holds no gameplay behaviour, and it references definitions by
    /// <see cref="DefinitionId"/> rather than by object, so an instance never pins a
    /// ScriptableObject in memory and stays valid across content patches and asset
    /// reimports.
    ///
    /// Mutating state is deliberately explicit and always advances the revision. Nothing
    /// here is authoritative: the client is untrusted, and a server must validate every
    /// field it receives before accepting it.
    /// </remarks>
    /// <summary>
    /// Why an owned object is not free to change hands.
    /// </summary>
    /// <remarks>
    /// <b>One state, not four booleans.</b> The alternative was <c>IsTrading</c>,
    /// <c>IsInShop</c>, <c>IsReservedForShop</c> and friends, which can contradict each
    /// other: an item both trading and listed is representable with flags and meaningless in
    /// fact. A single value cannot be in two states at once.
    ///
    /// <b>Only what this mechanism owns.</b> Equipped and socketed are deliberately absent.
    /// Those facts already have authoritative homes -- <c>CharacterEquipmentState</c> and
    /// <c>EquipmentInstance.Cards</c> -- and copying them here would create a second answer
    /// that could disagree with the first. <c>ItemTransferRules</c> asks each authority in
    /// turn and reports them as reasons; this enum holds only the reservations trade and
    /// shops actually set, so no value here is ever declared and never assigned.
    /// </remarks>
    public enum ItemLockState
    {
        /// <summary>Free to be used, equipped, traded or listed.</summary>
        Available = 0,

        /// <summary>Held by an open trade session.</summary>
        Reserved = 1,

        /// <summary>Held by a player shop listing.</summary>
        Listed = 2,

        /// <summary>Bound to its owner and never transferable.</summary>
        Bound = 3
    }

    [Serializable]
    public abstract class GameInstance : IGameInstance
    {
        [SerializeField] private InstanceId _instanceId;
        [SerializeField] private DefinitionId _definitionId;
        [SerializeField] private OwnerId _owner;
        [SerializeField] private Revision _revision;
        [SerializeField] private ItemLockState _lockState;

        /// <summary>Exists for deserializers, which construct before populating.</summary>
        /// <remarks>An instance created this way is not valid until deserialization fills
        /// it in. Prefer the parameterised constructor in code.</remarks>
        protected GameInstance()
        {
        }

        protected GameInstance(InstanceId instanceId, DefinitionId definitionId, OwnerId owner)
        {
            if (!instanceId.IsValid)
            {
                throw new ArgumentException("An instance requires a valid identity.", nameof(instanceId));
            }

            if (!definitionId.IsValid)
            {
                throw new ArgumentException(
                    "An instance requires the definition it is a copy of.", nameof(definitionId));
            }

            _instanceId = instanceId;
            _definitionId = definitionId;
            _owner = owner;
            _revision = Revision.Initial;
        }

        public InstanceId InstanceId => _instanceId;

        public DefinitionId DefinitionId => _definitionId;

        public OwnerId Owner => _owner;

        public Revision Revision => _revision;

        /// <summary>
        /// Whether something currently holds this object against transfer.
        /// </summary>
        /// <remarks>
        /// <see cref="ItemLockState.Available"/> by default, which is what an instance
        /// created or deserialized before this field existed reads as -- the harmless answer.
        /// A lock is a claim by one system; whether an object may actually move also depends
        /// on facts held elsewhere, and <c>ItemTransferRules</c> is the one place all of them
        /// are asked together.
        /// </remarks>
        public ItemLockState LockState => _lockState;

        public bool IsLocked => _lockState != ItemLockState.Available;

        /// <summary>
        /// Records a reservation and advances the revision.
        /// </summary>
        /// <remarks>
        /// Assignment only. Whether a claim is legitimate is decided by the service making
        /// it, and releasing one is the same call with
        /// <see cref="ItemLockState.Available"/>.
        ///
        /// Refuses to overwrite one lock with a different one: an item already held by a
        /// trade must not be quietly taken over by a shop listing, because whichever system
        /// released it second would leave the object marked free while the other still
        /// believed it held it. A caller must release before re-claiming, and gets false
        /// rather than a silent theft.
        ///
        /// Setting the state it already holds changes nothing and does not advance the
        /// revision, so a no-op cannot look like a mutation.
        /// </remarks>
        public bool TrySetLockState(ItemLockState state)
        {
            if (_lockState == state) return false;

            if (_lockState != ItemLockState.Available && state != ItemLockState.Available)
            {
                return false;
            }

            _lockState = state;
            AdvanceRevision();
            return true;
        }

        /// <summary>
        /// Reassigns ownership and advances the revision.
        /// </summary>
        /// <remarks>
        /// State assignment only. Whether a transfer is permitted, and any trade, mail or
        /// drop flow around it, is server-authoritative logic that lives elsewhere.
        /// </remarks>
        public void SetOwner(OwnerId owner)
        {
            _owner = owner;
            AdvanceRevision();
        }

        /// <summary>Advances the revision. Call after any mutation of subclass state.</summary>
        protected void AdvanceRevision()
        {
            _revision = _revision.Next();
        }

        /// <summary>
        /// Repoints this instance at a different definition, without advancing the revision.
        /// </summary>
        /// <remarks>
        /// <b>Protected, and deliberately awkward.</b> An instance's definition is normally
        /// fixed for life: a sword does not become a different sword. One thing genuinely
        /// changes what it is -- a pet that evolves -- and the alternative was to destroy the
        /// owned pet and create a second one, losing its identity, its owner and its history
        /// to represent something a player experiences as the same creature growing up.
        ///
        /// <b>It advances no revision on purpose.</b> A caller changing a definition is
        /// changing more than one field, and advancing here would count one logical mutation
        /// twice. The subclass calls <see cref="AdvanceRevision"/> once, at the end. That is
        /// why this is not public: there is no correct way to call it alone.
        ///
        /// Refuses an invalid id rather than blanking the instance, since an instance with no
        /// definition cannot be resolved by anything.
        /// </remarks>
        protected bool ReplaceDefinitionId(DefinitionId definitionId)
        {
            if (!definitionId.IsValid) return false;

            _definitionId = definitionId;
            return true;
        }
    }
}
