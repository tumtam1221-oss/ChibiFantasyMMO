using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>Why a pet request was refused.</summary>
    public enum PetRequestRejection
    {
        None = 0,

        /// <summary>No registry, or a world composed without pets.</summary>
        MissingContext = 1,

        /// <summary>The connection resolves to no character here.</summary>
        NoCharacter = 2,

        /// <summary>An older request arriving after a newer one.</summary>
        OutOfOrder = 3,

        /// <summary>Phase 12 refused it: not owned, unknown, disabled.</summary>
        Refused = 4
    }

    /// <summary>What a pet request did.</summary>
    public readonly struct CharacterPetResult
    {
        private CharacterPetResult(bool accepted, PetRequestRejection rejection,
            PetResult pet)
        {
            IsAccepted = accepted;
            Rejection = rejection;
            Pet = pet;
        }

        public bool IsAccepted { get; }

        public PetRequestRejection Rejection { get; }

        /// <summary>Phase 12's own answer, when it was the one that decided.</summary>
        public PetResult Pet { get; }

        public static CharacterPetResult Refused(PetRequestRejection rejection)
        {
            return new CharacterPetResult(false, rejection, default);
        }

        public static CharacterPetResult From(in PetResult result)
        {
            return new CharacterPetResult(result.IsAccepted,
                result.IsAccepted ? PetRequestRejection.None : PetRequestRejection.Refused,
                result);
        }

        public static CharacterPetResult Accepted()
        {
            return new CharacterPetResult(true, PetRequestRejection.None, default);
        }

        public override string ToString()
        {
            return IsAccepted ? "accepted" : "refused: " + Rejection;
        }
    }

    /// <summary>
    /// Which pet a character has out, decided by the server.
    /// </summary>
    /// <remarks>
    /// <b>Phase 12 decides; this only asks.</b> Whether a pet may be summoned, what happens
    /// to the one already out, whether the new one is an aura and which buff it grants are
    /// all <see cref="PetService"/>'s, and none of them is restated here. This is the seam
    /// between a connection and that service, in the same shape as the inventory and loot
    /// authorities beside it.
    ///
    /// <b>A request names a pet and nothing else.</b> Not a character, not a level, not a
    /// buff -- the connection says who is asking and the server reads everything else off
    /// state it already holds. A request that could name any of those would be a request to
    /// invent a companion.
    ///
    /// <b>There is no second ownership model.</b> Pets live on the character that owns them
    /// and the active one lives on that character's companion state; this class holds no pet
    /// state of its own and could be discarded between calls without losing anything.
    /// </remarks>
    public sealed class CharacterPetAuthority : ChibiFantasy.Network.ICharacterPetRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly IDefinitionRegistry<PetDefinition> _pets;
        private readonly IDefinitionRegistry<ItemDefinition> _items;
        private readonly IDefinitionRegistry<StatusEffectDefinition> _effects;

        /// <summary>The last request's answer, for a caller that asked through a void seam.</summary>
        public CharacterPetResult LastResult { get; private set; }

        public CharacterPetAuthority(WorldCharacterRegistry characters,
            IDefinitionRegistry<PetDefinition> pets,
            IDefinitionRegistry<ItemDefinition> items = null,
            IDefinitionRegistry<StatusEffectDefinition> effects = null)
        {
            _characters = characters;
            _pets = pets;
            _items = items;
            _effects = effects;
        }

        /// <summary>
        /// Puts one of this character's own pets out.
        /// </summary>
        /// <remarks>
        /// <b>Ownership is checked twice, and neither check trusts the request.</b> The
        /// character comes from the connection, the pet is looked up among the ones that
        /// character owns, and Phase 12 checks the owner again on the instance itself. A
        /// pet somebody else owns cannot be found by the first check and would be refused
        /// by the second.
        ///
        /// <b>One at a time.</b> Swapping is Phase 12's business: it dismisses whatever is
        /// out first, so the previous pet's buff cannot outlive it and two cannot stack.
        /// </remarks>
        public CharacterPetResult Activate(int connectionId, InstanceId pet)
        {
            if (_characters == null || _pets == null)
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.MissingContext));
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter living))
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.NoCharacter));
            }

            // Only among the pets this character owns. A pet named by somebody else's
            // identity is simply not here.
            if (!living.TryGetPet(pet, out PetInstance owned))
            {
                return Remember(CharacterPetResult.Refused(PetRequestRejection.Refused));
            }

            CharacterPetResult result = CharacterPetResult.From(
                PetService.TrySummon(living.Companion, owned, ContextFor(living)));

            if (result.IsAccepted) Settle(living);

            return Remember(result);
        }

        /// <summary>Puts away whatever is out, if anything is.</summary>
        /// <remarks>Phase 12's dismiss, which is also what takes the buff away -- so a
        /// character who put their pet away stops being buffed by it in the one place that
        /// applied it.</remarks>
        public CharacterPetResult Deactivate(int connectionId)
        {
            if (_characters == null || _pets == null)
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.MissingContext));
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter living))
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.NoCharacter));
            }

            if (!PetService.Dismiss(living.Companion, ContextFor(living)))
            {
                // Nothing was out. Not a failure: the world is already how they asked for
                // it to be, and a refusal would make a harmless repeat look like an error.
                return Remember(CharacterPetResult.Accepted());
            }

            Settle(living);

            return Remember(CharacterPetResult.Accepted());
        }

        /// <summary>
        /// Gives a character a pet, through Phase 12's own acquisition.
        /// </summary>
        /// <remarks>
        /// <b>Server-side only, and provisional.</b> There is no live drop, quest or shop
        /// that hands out a pet yet, so this is the authoritative path a future one would
        /// call rather than a player-facing action -- nothing on the wire reaches it.
        /// PetService decides whether the pet may be acquired and mints the instance; this
        /// only files it under the character that now owns it.
        /// </remarks>
        public CharacterPetResult Grant(int connectionId, DefinitionId pet)
        {
            if (_characters == null || _pets == null)
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.MissingContext));
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter living))
            {
                return Remember(CharacterPetResult.Refused(
                    PetRequestRejection.NoCharacter));
            }

            PetResult acquired = PetService.TryAcquire(pet, living.Owner,
                ContextFor(living));

            if (!acquired.IsAccepted) return Remember(CharacterPetResult.From(acquired));

            if (!living.AddPet(acquired.Pet))
            {
                return Remember(CharacterPetResult.Refused(PetRequestRejection.Refused));
            }

            Settle(living);

            return Remember(CharacterPetResult.From(acquired));
        }

        /// <summary>
        /// Where this character's pet is, right now.
        /// </summary>
        /// <remarks>
        /// <b>Derived, not stored.</b> A follower has no position of its own to drift, to
        /// replicate or to be moved by a client: it is wherever its owner is, plus the
        /// offset the pet's own definition authors. That is the whole tether -- a pet cannot
        /// be left behind because there is nothing to leave behind, and the maximum distance
        /// §16 asks for is enforced by construction rather than by a correction step.
        ///
        /// <b>No client may write it.</b> There is no setter and no request that reaches
        /// one. A client that wanted to move somebody's pet would have to move the owner,
        /// which the movement authority already refuses to let it do.
        ///
        /// Returns false when nothing is out, which is how a caller knows to show nothing.
        /// </remarks>
        public bool TryFollowPoint(int connectionId, out CombatPosition point)
        {
            point = default;

            if (_characters == null) return false;

            if (!_characters.TryGet(connectionId, out LivingCharacter living)) return false;

            return TryFollowPoint(living, out point);
        }

        /// <summary>Where a known character's pet is, if one is out.</summary>
        public bool TryFollowPoint(LivingCharacter living, out CombatPosition point)
        {
            point = default;

            if (living == null || living.Companion == null) return false;

            if (!living.Companion.IsSummoned) return false;

            // An aura is on the character rather than beside them, so it has no follow
            // point of its own -- 18.17C decides what that looks like.
            if (living.Companion.IsAuraForm) return false;

            var offset = 0f;

            if (_pets != null && living.Companion.Summoned != null
                && _pets.TryGet(living.Companion.Summoned.DefinitionId,
                    out PetDefinition definition) && definition != null)
            {
                offset = definition.VerticalOffset;
            }

            CombatPosition owner = living.Combatant.Position;

            point = new CombatPosition(owner.X, owner.Y + offset, owner.Z);

            return true;
        }

        /// <summary>The network seam. Explicit, so the void shape is not this class's own.</summary>
        void ChibiFantasy.Network.ICharacterPetRequestSink.Activate(int connectionId,
            InstanceId pet)
        {
            Activate(connectionId, pet);
        }

        void ChibiFantasy.Network.ICharacterPetRequestSink.Deactivate(int connectionId)
        {
            Deactivate(connectionId);
        }

        /// <summary>The registries and owner a pet decision is made against.</summary>
        private PetService.Context ContextFor(LivingCharacter living)
        {
            return new PetService.Context(_pets, _items, _effects, living.Status,
                living.Owner);
        }

        /// <summary>
        /// Marks the character changed and lets the world publish it.
        /// </summary>
        /// <remarks>The same lifecycle every other authority uses: the change is written at
        /// the next save point rather than immediately, and what observers see is decided by
        /// the replication service rather than by this class.</remarks>
        private void Settle(LivingCharacter living)
        {
            // Marked, not published. What observers see is decided by the replication
            // service on the world's own tick, which is the ordering every other authority
            // already relies on -- pushing from here would put a pet ahead of the stats it
            // is meant to arrive with.
            living.MarkDirty();
        }

        private CharacterPetResult Remember(CharacterPetResult result)
        {
            LastResult = result;

            return result;
        }
    }
}
