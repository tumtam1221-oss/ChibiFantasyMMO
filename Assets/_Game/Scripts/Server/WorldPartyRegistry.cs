using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// The parties this world is running, and whose turn it is to loot.
    /// </summary>
    /// <remarks>
    /// <b>A holder, not a party system.</b> Phase 13 already decided what a party is, who
    /// may join one, how big it may get and what its loot policy means; every one of those
    /// answers still comes from <see cref="PartyService"/> and <see cref="PartyState"/>.
    /// This is to parties what <see cref="WorldCharacterRegistry"/> is to characters: the
    /// place a running world keeps them so an authority can ask.
    ///
    /// <b>The rotation cursor lives here because a party owns it.</b>
    /// <see cref="PartyLootPolicyService.MemberOnTurn"/> takes the rotation as an argument
    /// and deliberately does not remember it, so somebody has to. Keeping it on the loot
    /// would make it per-pile and reset constantly; keeping it on the character would give
    /// six members six different ideas of whose turn it is. One number per party, moved
    /// only when a party actually claims something.
    /// </remarks>
    public sealed class WorldPartyRegistry
    {
        private readonly Dictionary<string, PartyState> _byParty =
            new Dictionary<string, PartyState>();

        private readonly Dictionary<string, int> _rotation = new Dictionary<string, int>();

        private readonly PartyDirectory _directory = new PartyDirectory();

        /// <summary>Phase 13's own directory, so membership has one answer.</summary>
        public PartyDirectory Directory => _directory;

        public int Count => _byParty.Count;

        /// <summary>Starts tracking a party, or refreshes the one already tracked.</summary>
        public bool Register(PartyState party)
        {
            if (party == null || !party.Id.IsValid) return false;

            _byParty[party.Id.Value] = party;

            _directory.Register(party);

            return true;
        }

        /// <summary>The party this character belongs to right now, if any.</summary>
        public bool TryGetPartyOf(CharacterId character, out PartyState party)
        {
            party = null;

            if (!character.IsValid) return false;

            PartyId id = _directory.PartyOf(character);

            if (!id.IsValid) return false;

            if (!_byParty.TryGetValue(id.Value, out party)) return false;

            // A party the directory still remembers but which has lost every member is
            // over. Reading it as "no party" is what makes a disbanded party behave like
            // solo rather than like an empty one.
            return party != null && party.IsActive && party.Contains(character);
        }

        /// <summary>Whose turn it is, as a number the loot policy can use.</summary>
        public int RotationOf(PartyId party)
        {
            if (!party.IsValid) return 0;

            return _rotation.TryGetValue(party.Value, out int rotation) ? rotation : 0;
        }

        /// <summary>
        /// Moves the party's turn on by one.
        /// </summary>
        /// <remarks>Called once per pile a party is actually given, never per pickup
        /// attempt -- a refused or replayed request must not cost a member their turn.</remarks>
        public int AdvanceRotation(PartyId party)
        {
            if (!party.IsValid) return 0;

            int next = RotationOf(party) + 1;

            _rotation[party.Value] = next;

            return next;
        }

        /// <summary>Forgets a party entirely, cursor and all.</summary>
        public bool Forget(PartyId party)
        {
            if (!party.IsValid) return false;

            if (_byParty.TryGetValue(party.Value, out PartyState state) && state != null)
            {
                _directory.Dissolve(state);
            }

            _rotation.Remove(party.Value);

            return _byParty.Remove(party.Value);
        }

        /// <summary>Every party this world is tracking.</summary>
        public IReadOnlyList<PartyState> All()
        {
            var all = new List<PartyState>(_byParty.Count);

            foreach (KeyValuePair<string, PartyState> pair in _byParty)
            {
                if (pair.Value != null) all.Add(pair.Value);
            }

            return all;
        }
    }

    /// <summary>
    /// Who a defeat is allowed to pay, decided once, at the moment it is claimed.
    /// </summary>
    /// <remarks>
    /// <b>Membership moves; this does not.</b> A player can leave a party, join another or
    /// disband the whole thing between a boss dying and its loot being taken. Working out
    /// eligibility again at pickup time would mean the answer depended on when you asked,
    /// and somebody who was nowhere near the fight could join the killer's party afterwards
    /// and walk off with the drop. So the answer is computed once and carried.
    ///
    /// <b>Ids, not objects.</b> It holds character ids and a party id, never the party
    /// itself -- a snapshot that pointed at live state would change underneath itself,
    /// which is the exact thing it exists to prevent.
    /// </remarks>
    public readonly struct DefeatRewardContext
    {
        public DefeatRewardContext(InstanceId monster, CharacterId killer, PartyId party,
            IReadOnlyList<CharacterId> eligible, DefinitionId map, int rotation)
        {
            Monster = monster;
            Killer = killer;
            Party = party;
            Eligible = eligible ?? System.Array.Empty<CharacterId>();
            Map = map;
            Rotation = rotation;
        }

        public InstanceId Monster { get; }

        public CharacterId Killer { get; }

        /// <summary>The party as it was at the defeat. None when the kill was solo.</summary>
        public PartyId Party { get; }

        /// <summary>Everyone the defeat may pay, in party order. Never empty for a valid kill.</summary>
        public IReadOnlyList<CharacterId> Eligible { get; }

        public DefinitionId Map { get; }

        /// <summary>The party's loot turn at the moment of the defeat.</summary>
        public int Rotation { get; }

        public bool IsParty => Party.IsValid && Eligible.Count > 1;

        public override string ToString()
        {
            return "defeat " + Monster + " by " + Killer + " for " + Eligible.Count;
        }
    }
}
