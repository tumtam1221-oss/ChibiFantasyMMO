using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// A character as the world server holds it: one identity, one place, and nothing
    /// the client had a say in.
    /// </summary>
    /// <remarks>
    /// <b>It is not a second character.</b> The identifier is the one the account database
    /// issued, the owner is the account projected the way Phase 08 defined, and the place is
    /// a Phase 11 <see cref="CharacterLocationState"/> — the same type the travel system
    /// moves. Nothing here is a parallel model of anything that already exists, which is
    /// rule 3 and the reason this type is so thin.
    ///
    /// <b>What it deliberately does not hold.</b> No stats, no experience, no inventory and
    /// no equipment. The Phase 15 API does not serve them, and a stat block invented here to
    /// make the type look finished would be indistinguishable from a real one until the
    /// moment it decided a fight. The gap is visible instead: <see cref="HasProfile"/> says
    /// what is known, and everything absent stays absent.
    ///
    /// <b>Every value came from the authority.</b> There is no constructor that takes loose
    /// identifiers -- only an admission -- so a runtime character built from something a
    /// client sent is not something this type can express.
    /// </remarks>
    public sealed class WorldCharacter
    {
        private WorldCharacter(in WorldAdmission admission, CharacterLocationState location,
            SpawnPointDefinition spawn)
        {
            Character = admission.Character;
            Account = admission.Account;
            Owner = admission.Owner;
            Server = admission.Server;
            Channel = admission.Channel;
            Session = admission.Session;
            Profile = admission.Profile;
            CharacterRevision = admission.CharacterRevision;

            Location = location;
            Spawn = spawn;
        }

        public CharacterId Character { get; }

        public AccountId Account { get; }

        /// <summary>The account projected onto Phase 08 ownership. Not a second model.</summary>
        public OwnerId Owner { get; }

        public ServerId Server { get; }

        public ChannelId Channel { get; }

        public SessionId Session { get; }

        /// <summary>Level, class, job, gender and appearance, as the database holds them.</summary>
        public WorldCharacterProfile Profile { get; }

        /// <summary>The character's revision when the server took authority over it.</summary>
        public Revision CharacterRevision { get; }

        /// <summary>Where it stands, in Phase 11's own type.</summary>
        public CharacterLocationState Location { get; }

        /// <summary>The authored spawn it arrived at.</summary>
        public SpawnPointDefinition Spawn { get; }

        public bool HasProfile => Profile.IsPresent;

        /// <summary>
        /// Builds the authoritative runtime character for an admitted connection.
        /// </summary>
        /// <remarks>
        /// <b>Returns null rather than a partial character.</b> A refused admission, an
        /// unknown map or a map with no authored player spawn all produce nothing, because
        /// there is no correct place to put such a character and the origin is not a
        /// fallback -- it is a position inside the terrain that a player would be told was
        /// success.
        ///
        /// The arrival goes through <see cref="CharacterLocationState.ArriveAt"/>, which
        /// requires a real spawn definition and sets the position from it. That is Phase
        /// 11's rule and it is the reason no coordinate is written anywhere in this file.
        /// </remarks>
        public static WorldCharacter Create(in WorldAdmission admission,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints)
        {
            if (!admission.IsAdmitted || !admission.HasCharacter) return null;

            SpawnPointDefinition spawn = TravelService.FindPlayerSpawn(admission.Map, spawnPoints);

            if (spawn == null) return null;

            var location = new CharacterLocationState(admission.Character);

            if (!location.ArriveAt(spawn)) return null;

            return new WorldCharacter(admission, location, spawn);
        }

        public override string ToString()
        {
            return Character + " (" + Owner + ") on " + Location;
        }
    }
}
