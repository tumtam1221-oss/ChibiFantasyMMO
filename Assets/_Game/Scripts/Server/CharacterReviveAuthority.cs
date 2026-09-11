using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>Why a revive was refused.</summary>
    public enum ReviveRejection
    {
        None = 0,

        /// <summary>Nobody is on that connection.</summary>
        NoCharacter = 1,

        /// <summary>They are still standing. There is nothing to get up from.</summary>
        StillAlive = 2,

        /// <summary>The map they fell on has no player spawn point authored.</summary>
        NowhereToWakeUp = 3,

        /// <summary>The character has no health ceiling to be restored to.</summary>
        NoHealthCeiling = 4
    }

    /// <summary>What one revive did.</summary>
    public readonly struct ReviveResult
    {
        private ReviveResult(bool revived, ReviveRejection rejection, int health,
            CombatPosition where)
        {
            IsRevived = revived;
            Rejection = rejection;
            Health = health;
            Position = where;
        }

        public bool IsRevived { get; }

        public ReviveRejection Rejection { get; }

        /// <summary>The health they got up with.</summary>
        public int Health { get; }

        /// <summary>Where they got up.</summary>
        public CombatPosition Position { get; }

        public static ReviveResult Revived(int health, CombatPosition where)
        {
            return new ReviveResult(true, ReviveRejection.None, health, where);
        }

        public static ReviveResult Refused(ReviveRejection rejection)
        {
            return new ReviveResult(false, rejection, 0, default);
        }

        public override string ToString()
        {
            return IsRevived ? "revived at " + Position + " on " + Health : "refused: " + Rejection;
        }
    }

    /// <summary>
    /// Puts a fallen character back on their feet, in town.
    /// </summary>
    /// <remarks>
    /// <b>The hole this fills.</b> Until monsters could actually hit anybody, nothing in this
    /// project had ever reduced a player to zero health, so nothing handled it. The moment
    /// they could, a character who lost a fight was left at zero forever: combat commands
    /// refuse a dead attacker, monsters stop targeting a corpse, and -- worst of it --
    /// current health is persisted, so signing out and back in restored them to exactly the
    /// zero they left. There was no way back other than editing the database.
    ///
    /// <b>Server-authoritative in every part.</b> Whether they are dead is read from
    /// replicated health the server wrote; where they wake up is the map's authored player
    /// spawn, the same one arriving in the world uses; how much health they get is a world
    /// rule read from the content catalogue. A client contributes the wish and nothing else,
    /// which is why <see cref="ICharacterReviveRequestSink.Submit"/> carries no position and
    /// no figure.
    ///
    /// <b>It reuses the spawn point rather than inventing a place.</b> That matters more than
    /// it looks: the spawn's height is baked from the map's ground, so a revived player
    /// stands on the ground instead of hanging above it or sinking -- the same rule that
    /// spawning already depends on.
    ///
    /// <b>Movement is untouched.</b> This writes the authoritative position directly, exactly
    /// as entering the world does, and goes nowhere near the movement validator: there is no
    /// client claim here to disbelieve.
    /// </remarks>
    public sealed class CharacterReviveAuthority : ICharacterReviveRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;
        private readonly float _healthFraction;

        /// <param name="characters">Who is in the world. The only place a character is found.</param>
        /// <param name="spawnPoints">
        /// Authored player spawns. Required rather than optional: without them there is
        /// nowhere to wake up, and an optional argument would let a caller silently ship a
        /// server whose revive put everybody at the origin.
        /// </param>
        /// <param name="healthFraction">
        /// How much of their ceiling a revived character gets back, 0..1. A world rule, so
        /// it comes from world content rather than from a constant here.
        /// </param>
        public CharacterReviveAuthority(WorldCharacterRegistry characters,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints, float healthFraction)
        {
            _characters = characters;
            _spawnPoints = spawnPoints;
            _healthFraction = healthFraction <= 0f ? 0.5f
                : (healthFraction > 1f ? 1f : healthFraction);
        }

        /// <summary>How many characters have been put back on their feet. For diagnostics.</summary>
        public int Revivals { get; private set; }

        /// <summary>The last refusal, for reporting.</summary>
        public ReviveRejection LastRejection { get; private set; }

        void ICharacterReviveRequestSink.Submit(int connectionId, long sequence)
        {
            Revive(connectionId);
        }

        /// <summary>
        /// Stands a character back up at their map's town spawn.
        /// </summary>
        /// <remarks>Idempotent in the way that matters: a second call from a player who is
        /// already up is refused rather than healing them again, so a client that sends the
        /// request twice cannot use it to top itself up mid-fight.</remarks>
        public ReviveResult Revive(int connectionId)
        {
            LastRejection = ReviveRejection.None;

            if (_characters == null
                || !_characters.TryGet(connectionId, out LivingCharacter character)
                || character == null)
            {
                return Refuse(ReviveRejection.NoCharacter);
            }

            CharacterCombatant combatant = character.Combatant;

            if (combatant == null) return Refuse(ReviveRejection.NoCharacter);

            // The guard that stops this being a heal. Being dead is the whole entitlement.
            if (combatant.CurrentHealth > 0) return Refuse(ReviveRejection.StillAlive);

            int ceiling = combatant.MaxHealth;

            if (ceiling <= 0) return Refuse(ReviveRejection.NoHealthCeiling);

            DefinitionId map = character.Location == null
                ? DefinitionId.None
                : character.Location.CurrentMap;

            SpawnPointDefinition spawn = TravelService.FindPlayerSpawn(map, _spawnPoints);

            if (spawn == null) return Refuse(ReviveRejection.NowhereToWakeUp);

            var where = new CombatPosition(spawn.X, spawn.Y, spawn.Z);

            int health = (int)(ceiling * _healthFraction);

            if (health < 1) health = 1;

            combatant.ApplyHealthDelta(health);

            // Both, because two things read a position: combat asks the combatant, and
            // replication and persistence ask the location. Leaving one behind is how a
            // character ends up being shot at where they used to be.
            character.Location.Position = where;
            combatant.Position = where;

            Revivals++;

            return ReviveResult.Revived(combatant.CurrentHealth, where);
        }

        private ReviveResult Refuse(ReviveRejection rejection)
        {
            LastRejection = rejection;

            return ReviveResult.Refused(rejection);
        }
    }
}
