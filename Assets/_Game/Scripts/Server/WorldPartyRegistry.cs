using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using UnityEngine;

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
            _revisions.Remove(party.Value);

            return _byParty.Remove(party.Value);
        }

        /// <summary>
        /// Brings a character's party back from storage, if they have one.
        /// </summary>
        /// <remarks>
        /// <b>Lazily, when a member arrives.</b> Loading every party in the database at
        /// world boot would make startup cost grow with the number of parties that ever
        /// existed, most of whose members are not logged in. A party is needed when one of
        /// its members is here, so that is when it is fetched.
        ///
        /// <b>Once, however many members arrive.</b> Six people reconnecting at the same
        /// moment must attach to one <see cref="PartyState"/>, not six. The party id is
        /// checked first, so the second and later arrivals find the object the first one
        /// built and add nothing.
        ///
        /// <b>Storage is not asked again for a party already here.</b> A member rejoining a
        /// party the world is already running reads nothing, which is also what stops a
        /// reconnect from overwriting live membership with an older row.
        /// </remarks>
        public PartyState Restore(SessionId session, CharacterId character,
            IPartyStateStore store)
        {
            if (store == null || !character.IsValid) return null;

            // Already running: attach, do not re-read.
            if (TryGetPartyOf(character, out PartyState running)) return running;

            PartyPersistenceResult loaded = store.Load(session);

            if (!loaded.IsOk)
            {
                // A backend that could not answer must not silently drop somebody out of
                // their party -- they simply arrive partyless until it can.
                Debug.LogWarning("[party] could not restore " + character + ": "
                    + loaded.Failure);

                return null;
            }

            PersistedParty stored = loaded.Party;

            if (!stored.Exists) return null;

            // Another member restored it while this one was in flight.
            if (_byParty.TryGetValue(stored.Party.Value, out PartyState existing)
                && existing != null && existing.IsActive)
            {
                _directory.Register(existing);

                return existing;
            }

            var party = new PartyState(stored.Party, stored.Leader, stored.LootPolicy);

            // In stored order, because round-robin walks the member list and a party that
            // came back shuffled would change whose turn it is.
            for (var i = 0; i < stored.Members.Count; i++)
            {
                party.TryAdd(stored.Members[i]);
            }

            _revisions[stored.Party.Value] = stored.Revision;

            Register(party);

            return party;
        }

        /// <summary>
        /// Writes a party back as it now stands.
        /// </summary>
        /// <remarks>The whole membership, every time: join, leave, kick and a policy change
        /// are one shape, so no two write paths can disagree about what a party is. An
        /// empty party is stored as ended.</remarks>
        public PartyPersistenceResult Persist(SessionId session, PartyState party,
            IPartyStateStore store)
        {
            if (store == null || party == null)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.InvalidParty);
            }

            var members = new List<CharacterId>(party.Members);

            PartyPersistenceResult saved = store.Save(session, new PersistedParty(
                party.Id, party.Leader, party.LootPolicy, members,
                RevisionOf(party.Id)));

            if (saved.IsOk) _revisions[party.Id.Value] = saved.Revision;

            return saved;
        }

        /// <summary>The stored revision this world last saw, for refusing a stale write.</summary>
        public int RevisionOf(PartyId party)
        {
            if (!party.IsValid) return 0;

            return _revisions.TryGetValue(party.Value, out int revision) ? revision : 0;
        }

        private readonly Dictionary<string, int> _revisions = new Dictionary<string, int>();

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
