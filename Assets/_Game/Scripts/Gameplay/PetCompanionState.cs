using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>What a summoned pet is doing.</summary>
    /// <remarks>
    /// Closed technical category: each value is a different intent a presenter acts on, and
    /// each gates different behaviour. Intents, not positions -- see
    /// <see cref="PetCompanionState"/> for why no coordinate appears in this file.
    /// </remarks>
    public enum PetFollowMode
    {
        /// <summary>Not summoned.</summary>
        Dismissed = 0,

        /// <summary>Keeping pace with its owner.</summary>
        Follow = 1,

        /// <summary>Holding where it is.</summary>
        Idle = 2,

        /// <summary>Making its way back to its owner.</summary>
        Return = 3
    }

    /// <summary>
    /// The pet a character currently has out.
    /// </summary>
    /// <remarks>
    /// <b>An intent, never a transform.</b> There is no position, no rotation and no
    /// distance in this file. Gameplay says a pet is following, idling or returning; the
    /// client turns that into movement. That split is what keeps this assembly engine-free
    /// and what lets a future server run the same state with nothing to draw.
    ///
    /// <b>Runtime, not persistent.</b> Which pet a player owns and how far it has come is
    /// <see cref="PetInstance"/>, and that is the thing a database stores. Whether it happens
    /// to be out right now is not worth persisting, and marking it
    /// <see cref="IRuntimeState"/> is what stops it drifting into a save file.
    ///
    /// <b>The aura form lives here as state, not as a second entity.</b> An evolved pet that
    /// becomes light around its owner is this same record with
    /// <see cref="IsAuraForm"/> true -- the presenter stops drawing a follower and starts
    /// drawing an aura. Creating an invisible pet object to stand in for it would make the
    /// authoritative answer depend on something only the client can see.
    /// </remarks>
    public sealed class PetCompanionState : IRuntimeState
    {
        private PetInstance _summoned;
        private PetFollowMode _mode = PetFollowMode.Dismissed;
        private bool _auraForm;
        private Revision _revision;

        public PetCompanionState(CharacterId characterId = default)
        {
            CharacterId = characterId;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        public Revision Revision => _revision;

        /// <summary>The pet that is out, or null.</summary>
        public PetInstance Summoned => _summoned;

        public PetFollowMode Mode => _mode;

        public bool IsSummoned => _summoned != null && _mode != PetFollowMode.Dismissed;

        /// <summary>
        /// Whether the pet is present as an aura on its owner rather than as a follower.
        /// </summary>
        /// <remarks>Authored on the evolution stage and recorded here, so presentation reads
        /// one answer instead of re-deriving it from a definition every frame.</remarks>
        public bool IsAuraForm => _auraForm;

        /// <summary>
        /// Brings a pet out.
        /// </summary>
        /// <remarks>Assignment only; ownership and eligibility are <see cref="PetService"/>'s
        /// decisions. Summoning a pet that is already out changes nothing rather than
        /// re-summoning it, so a repeated request is not a mutation.</remarks>
        public bool Summon(PetInstance pet, bool auraForm)
        {
            if (pet == null) return false;

            if (_summoned == pet && _mode != PetFollowMode.Dismissed && _auraForm == auraForm)
            {
                return false;
            }

            _summoned = pet;
            _auraForm = auraForm;
            _mode = PetFollowMode.Follow;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Puts the pet away. The pet itself is untouched and still owned.</summary>
        public bool Dismiss()
        {
            if (_summoned == null && _mode == PetFollowMode.Dismissed) return false;

            _summoned = null;
            _auraForm = false;
            _mode = PetFollowMode.Dismissed;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Changes what the pet is doing.
        /// </summary>
        /// <remarks>Refuses to set a mode on nothing, and refuses
        /// <see cref="PetFollowMode.Dismissed"/> -- putting a pet away is
        /// <see cref="Dismiss"/>, which also clears the aura. Allowing it here would leave a
        /// dismissed pet still recorded as out.</remarks>
        public bool SetMode(PetFollowMode mode)
        {
            if (_summoned == null || mode == PetFollowMode.Dismissed) return false;
            if (_mode == mode) return false;

            _mode = mode;
            _revision = _revision.Next();
            return true;
        }

        /// <summary>
        /// Records that the summoned pet became an aura form.
        /// </summary>
        /// <remarks>Called after an evolution that authored one, so a pet already out does
        /// not have to be dismissed and re-summoned to change how it appears.</remarks>
        public bool SetAuraForm(bool auraForm)
        {
            if (_summoned == null || _auraForm == auraForm) return false;

            _auraForm = auraForm;
            _revision = _revision.Next();
            return true;
        }
    }
}
