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
    ///
    /// <b>And written down when it moves.</b> Held only in memory, the cursor is reset by
    /// every restart, so the first member in join order takes the first drop after each
    /// one -- over a week of restarts that is a real share of a party's loot going to
    /// whoever happens to sort first. It is saved on the same event that already saves the
    /// character who was paid, and read back when a member arrives.
    /// </remarks>
    public sealed class WorldPartyRegistry
    {
        private readonly Dictionary<string, PartyState> _byParty =
            new Dictionary<string, PartyState>();

        private readonly Dictionary<string, int> _rotation = new Dictionary<string, int>();

        /// <summary>The session each member arrived on, so a turn can be written back.</summary>
        /// <remarks>Per member rather than per party: the one who restored the party may
        /// well have logged out by the time it takes a drop, and the party's turn must not
        /// become unsaveable because of it.</remarks>
        private readonly Dictionary<string, SessionId> _sessions =
            new Dictionary<string, SessionId>();

        /// <summary>The store this world last spoke to, remembered so a turn spent during
        /// combat can be written where it is spent.</summary>
        private IPartyStateStore _store;

        public WorldPartyRegistry(IPartyStateStore store = null)
        {
            _store = store;
        }

        /// <summary>
        /// Whether this world's parties outlive it.
        /// </summary>
        /// <remarks>A world composed without a party store keeps its parties in memory and
        /// loses them when it stops, so there is no durable turn for a runtime one to run
        /// ahead of. Callers that must not spend a turn they cannot write down ask this
        /// first -- refusing to hand out RoundRobin loot in a world that never persisted a
        /// cursor would protect nothing and break every drop.</remarks>
        public bool IsDurable => _store != null;

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

            _rotation[party.Value] = RotationOf(party) + 1;

            // A turn that exists only in memory is the turn a restart hands back to the
            // first member, who then receives twice. Written at the moment it is spent,
            // on the same event that already writes the character who was paid.
            WriteTurn(party);

            return RotationOf(party);
        }

        /// <summary>
        /// The turn after this one, worked out without spending it.
        /// </summary>
        /// <remarks>Separate from <see cref="AdvanceRotation"/> because a caller that must
        /// write the turn down before acting on it needs to know the number first. Reading
        /// it changes nothing, so a caller that then fails has spent no turn.</remarks>
        public int NextRotation(PartyId party)
        {
            if (!party.IsValid) return 0;

            if (!_byParty.TryGetValue(party.Value, out PartyState state) || state == null)
            {
                return 0;
            }

            return Bounded(RotationOf(party) + 1, state.MemberCount);
        }

        /// <summary>
        /// Writes the next turn down, and only then makes it this world's.
        /// </summary>
        /// <remarks>
        /// <b>Storage first, memory second.</b> <see cref="AdvanceRotation"/> moves the
        /// turn and then tries to save it, which is right for a policy that does not hand
        /// the pile out by rotation -- nothing is owed to anybody in particular, so a failed
        /// write costs only a re-send. Under RoundRobin the turn *is* the claim: if this
        /// world spends it and the write fails, the restarted world offers the same member
        /// the same turn again and they receive twice.
        ///
        /// So the runtime cursor is never allowed to lead the durable one. On failure
        /// nothing here changes at all, and the caller still holds an unspent turn.
        /// </remarks>
        public PartyPersistenceResult TryCommitNextRotation(PartyId party)
        {
            if (!party.IsValid)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.InvalidParty,
                    "no party");
            }

            if (!_byParty.TryGetValue(party.Value, out PartyState state) || state == null
                || !state.IsActive)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.InvalidParty,
                    "that party is not running here");
            }

            if (_store == null)
            {
                return PartyPersistenceResult.Failed(PartyPersistenceFailure.Unreachable,
                    "this world has no party store");
            }

            int next = Bounded(RotationOf(party) + 1, state.MemberCount);

            PartyPersistenceResult saved = WriteTo(state, next);

            if (saved.IsOk) _rotation[party.Value] = next;

            return saved;
        }

        /// <summary>
        /// Saves the party because its turn moved, if this world can.
        /// </summary>
        /// <remarks>
        /// <b>Any member's session will do.</b> The party is what is being written, not a
        /// character, and the API scopes the write to a member -- so the first member still
        /// connected is asked, and one member logging out cannot strand the cursor.
        ///
        /// <b>A refusal does not stop the loot.</b> The turn has already moved in memory
        /// and the pile has already been handed over; failing the drop because a database
        /// was briefly unreachable would be a worse outcome than a cursor that is re-sent
        /// with the party's next write.
        /// </remarks>
        private void WriteTurn(PartyId party)
        {
            if (_store == null) return;

            if (!_byParty.TryGetValue(party.Value, out PartyState state)) return;

            if (state == null || !state.IsActive) return;

            int cursor = Bounded(RotationOf(party), state.MemberCount);

            if (WriteTo(state, cursor).IsOk)
            {
                _rotation[party.Value] = cursor;

                return;
            }

            Debug.LogWarning("[party] could not save the loot turn for " + party
                + "; it will go out with this party's next write.");
        }

        /// <summary>
        /// Writes a party and a turn, through whichever member this world can speak as.
        /// </summary>
        /// <remarks>Any member's session will do: the party is what is being written, not
        /// a character. Tried in join order and stopped at the first that is accepted, so
        /// one member logging out cannot make a party's turn unwritable. What comes back is
        /// the last refusal, because a caller that must not proceed needs to know why.
        /// </remarks>
        private PartyPersistenceResult WriteTo(PartyState state, int cursor)
        {
            IReadOnlyList<CharacterId> members = state.Members;

            PartyPersistenceResult last = PartyPersistenceResult.Failed(
                PartyPersistenceFailure.NotAMember,
                "no member of this party is connected to this world");

            for (var i = 0; i < members.Count; i++)
            {
                if (!_sessions.TryGetValue(members[i].Value, out SessionId session))
                {
                    continue;
                }

                last = Write(session, state, _store, cursor);

                if (last.IsOk) return last;
            }

            return last;
        }

        /// <summary>The one place a party actually goes out over the wire.</summary>
        private PartyPersistenceResult Write(SessionId session, PartyState party,
            IPartyStateStore store, int cursor)
        {
            var members = new List<CharacterId>(party.Members);

            PartyPersistenceResult saved = store.Save(session, new PersistedParty(
                party.Id, party.Leader, party.LootPolicy, members,
                RevisionOf(party.Id), cursor));

            if (saved.IsOk)
            {
                _revisions[party.Id.Value] = saved.Revision;

                _store = store;
            }

            return saved;
        }

        /// <summary>
        /// A rotation count reduced to the member it names.
        /// </summary>
        /// <remarks>The same arithmetic <see cref="PartyLootPolicyService.MemberOnTurn"/>
        /// applies when it picks a member, and it has to stay the same: this is what turns
        /// an in-memory count into the index that is stored, and the stored index must
        /// select the member the running world would have selected.</remarks>
        private static int Bounded(int rotation, int members)
        {
            if (members <= 0) return 0;

            int index = rotation % members;

            return index < 0 ? index + members : index;
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

            // Remembered whether or not this character turns out to have a party, because
            // what they are needed for is writing somebody else's turn back later.
            _store = store;
            _sessions[character.Value] = session;

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

            if (!stored.IsCursorValid)
            {
                // Refused rather than folded back into range. A cursor that addresses no
                // member is a row somebody has to look at, and quietly taking it modulo
                // the party size would give the next drop away while looking like a
                // clean restore.
                Debug.LogWarning("[party] refusing " + stored.Party
                    + ": the stored loot turn addresses no member");

                return null;
            }

            var party = new PartyState(stored.Party, stored.Leader, stored.LootPolicy);

            // In stored order, because round-robin walks the member list and a party that
            // came back shuffled would change whose turn it is.
            for (var i = 0; i < stored.Members.Count; i++)
            {
                party.TryAdd(stored.Members[i]);
            }

            _revisions[stored.Party.Value] = stored.Revision;

            // Whose turn it was when the party was last written. Without this the members
            // come back in order and the rotation restarts at the first of them.
            _rotation[stored.Party.Value] = stored.Cursor;

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

            // Reduced against the membership actually being written, so what lands in
            // storage is an index into that list rather than a count that would grow for
            // as long as the world runs. This is also where a party that shrank gets its
            // turn brought back into range, because a leave, a kick and a disband all
            // arrive here as "the party now looks like this".
            int cursor = Bounded(RotationOf(party.Id), party.MemberCount);

            PartyPersistenceResult saved = Write(session, party, store, cursor);

            // Memory follows storage. A world that saved and a world that restarted after
            // saving then hold the same number, instead of two that agree only until the
            // membership changes.
            if (saved.IsOk) _rotation[party.Id.Value] = cursor;

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
