using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Where a character is.
    /// </summary>
    /// <remarks>
    /// <b>The map is authoritative; the position is a report.</b> Which map someone is on is
    /// the fact travel, NPC interaction and monster spawning all key off, and in a served
    /// game it is the server's to set. The position is written by whatever moves the player
    /// and is only ever used for proximity checks.
    ///
    /// <b>Arriving is not a free operation.</b> <see cref="ArriveAt"/> takes a map <em>and</em>
    /// a spawn point, and there is no method that takes a map alone. That is what stops a
    /// generic <c>TeleportToMap</c> existing at all: a caller with a map id and no
    /// authored spawn cannot express the move, so every arrival came from a portal or a
    /// warp that resolved one.
    ///
    /// Persistent: where a character logged out is where they log back in.
    /// </remarks>
    public sealed class CharacterLocationState : IPersistentState
    {
        private Revision _revision;

        public CharacterLocationState(CharacterId characterId, DefinitionId map = default,
            DefinitionId spawnPoint = default)
        {
            CharacterId = characterId;
            CurrentMap = map;
            CurrentSpawnPoint = spawnPoint;
            _revision = Revision.Initial;
        }

        public CharacterId CharacterId { get; }

        /// <summary>The map they are on. Invalid before they have ever arrived anywhere.</summary>
        public DefinitionId CurrentMap { get; private set; }

        /// <summary>The spawn they last arrived at, for a respawn or a reconnect.</summary>
        public DefinitionId CurrentSpawnPoint { get; private set; }

        /// <summary>Where they stand. Written by whatever moves them.</summary>
        public CombatPosition Position { get; set; }

        public Revision Revision => _revision;

        public bool HasArrived => CurrentMap.IsValid;

        /// <summary>
        /// Records an arrival on a map, at an authored spawn.
        /// </summary>
        /// <remarks>
        /// Both are required. The absence of a map-only overload is deliberate and is the
        /// whole of rule 11.9: there is no gameplay call that puts a character on a map
        /// without a resolved destination, so portal and warp validation cannot be walked
        /// around.
        ///
        /// The position is set from the spawn, so a character never occupies the origin by
        /// default.
        /// </remarks>
        public bool ArriveAt(SpawnPointDefinition spawn)
        {
            if (spawn == null || !spawn.IsValid) return false;
            if (spawn.SpawnType != SpawnType.Player) return false;

            CurrentMap = spawn.Map;
            CurrentSpawnPoint = spawn.Id;
            Position = new CombatPosition(spawn.X, spawn.Y, spawn.Z);
            _revision = _revision.Next();
            return true;
        }

        /// <summary>Whether the character is on a given map.</summary>
        public bool IsOn(DefinitionId map)
        {
            return map.IsValid && CurrentMap == map;
        }

        public override string ToString()
        {
            return HasArrived ? CharacterId + " on " + CurrentMap : CharacterId + " nowhere";
        }
    }
}
