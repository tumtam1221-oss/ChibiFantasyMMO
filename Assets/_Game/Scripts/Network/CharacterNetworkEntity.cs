using ChibiFantasy.Core;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// Where a client's combat request goes once the network has carried it.
    /// </summary>
    /// <remarks>
    /// <b>An interface because the arrow points the wrong way.</b> The combat pipeline lives
    /// in the server assembly, which already references this one; a network object cannot
    /// reference it back without making the two mutually dependent. So the entity knows only
    /// that a request goes somewhere, and the server hands it the somewhere when it spawns
    /// the object.
    ///
    /// <b>The connection id is the first argument and is not a parameter of the message.</b>
    /// It is read from the object's owner, which FishNet established at spawn. A client
    /// cannot put a different one in.
    /// </remarks>
    public interface ICharacterCombatRequestSink
    {
        /// <summary>
        /// Submits one combat request on behalf of a connection.
        /// </summary>
        /// <param name="connectionId">Whose object it arrived through. Not client-supplied.</param>
        /// <param name="target">What they want to hit.</param>
        /// <param name="skill">Which skill, or none for a basic attack.</param>
        /// <param name="rank">Which rank of it.</param>
        /// <param name="sequence">The client's own ordering number, for replay rejection.</param>
        void Submit(int connectionId, InstanceId target, DefinitionId skill, int rank,
            long sequence);
    }

    /// <summary>
    /// A character, as clients see it -- and the one door a client's combat request enters by.
    /// </summary>
    /// <remarks>
    /// <b>A shadow, not a character.</b> The character is <c>LivingCharacter</c> on the
    /// server: a Phase 04 aggregate, a Phase 05 progression state, a Phase 07 combatant, a
    /// Phase 08 bag and a Phase 11 location. This carries a copy of the few facts a client
    /// needs to draw a health bar and an experience bar, and owns none of them. Deleting it
    /// would cost the game its visuals and not one rule.
    ///
    /// <b>Every value is <see cref="WritePermission.ServerOnly"/>, FishNet's default, relied
    /// on deliberately.</b> A client that assigned to one of these would be assigning to its
    /// own copy; nothing leaves the machine. That is what makes "the client cannot become
    /// authoritative" structural rather than a rule somebody has to remember.
    ///
    /// <b>The request carries intent and nothing else.</b> A target, a skill, a rank, a
    /// sequence. No damage, no health, no death, no experience, no loot, and -- crucially --
    /// no attacker: who is asking is the object's owner, established by the server at spawn.
    /// A player cannot swing as somebody else because there is nowhere to say who they are.
    ///
    /// <b>Position is replicated, not accepted.</b> The server writes where a character
    /// stands; nothing here reads a transform or takes a position from a client. Movement
    /// replication is a later gate, and until it exists a client that walks around is doing
    /// so in its own presentation only -- the server's position, which is what combat range
    /// is measured against, does not move.
    ///
    /// <b>It carries no art.</b> Production character art exists but wiring a model, a rig
    /// and an animator to a network object is presentation work, so this prefab is the
    /// network identity alone and a model becomes a child of it when that gate arrives.
    /// </remarks>
    public sealed class CharacterNetworkEntity : NetworkBehaviour
    {
        private readonly SyncVar<string> _characterId = new SyncVar<string>();
        private readonly SyncVar<string> _mapId = new SyncVar<string>();

        private readonly SyncVar<int> _health = new SyncVar<int>();
        private readonly SyncVar<int> _maxHealth = new SyncVar<int>();
        private readonly SyncVar<int> _level = new SyncVar<int>();
        private readonly SyncVar<long> _experience = new SyncVar<long>();

        private readonly SyncVar<float> _x = new SyncVar<float>();
        private readonly SyncVar<float> _y = new SyncVar<float>();
        private readonly SyncVar<float> _z = new SyncVar<float>();

        /// <summary>
        /// Where a request goes. Server-side only, and never sent anywhere.
        /// </summary>
        /// <remarks>Assigned on the server instance of this object when it spawns. The
        /// client's copy of the object has none, which is correct: a client has nothing to
        /// submit a request to.</remarks>
        private ICharacterCombatRequestSink _sink;

        /// <summary>Which character this is, as the server knows it.</summary>
        public CharacterId Character => new CharacterId(_characterId.Value);

        /// <summary>Which map the server has them on.</summary>
        public DefinitionId Map => new DefinitionId(_mapId.Value);

        public int Health => _health.Value;

        public int MaxHealth => _maxHealth.Value;

        public int Level => _level.Value;

        /// <summary>Progress within the current level, in Phase 05 terms.</summary>
        public long Experience => _experience.Value;

        public float X => _x.Value;

        public float Y => _y.Value;

        public float Z => _z.Value;

        /// <summary>Whether the server says they are standing.</summary>
        /// <remarks>Derived from replicated health rather than synced separately, so the two
        /// cannot disagree on the wire.</remarks>
        public bool IsAlive => _health.Value > 0;

        /// <summary>Points this object's requests at the server's combat pipeline.</summary>
        /// <remarks>Server-only, like everything else that writes here. Called once, right
        /// after the server spawns the object.</remarks>
        [Server]
        public void ServerUseCombatSink(ICharacterCombatRequestSink sink)
        {
            _sink = sink;
        }

        /// <summary>Publishes who this object represents.</summary>
        [Server]
        public void ServerPublishIdentity(CharacterId character, DefinitionId map,
            int maxHealth)
        {
            _characterId.Value = character.Value ?? string.Empty;
            _mapId.Value = map.Value ?? string.Empty;
            _maxHealth.Value = maxHealth < 0 ? 0 : maxHealth;
        }

        /// <summary>
        /// Publishes what the server decided this tick.
        /// </summary>
        /// <remarks>Position, health and progression together, because a client showing a
        /// character needs all of them and they change on the same tick. Every value is
        /// already decided elsewhere; nothing is computed here.</remarks>
        [Server]
        public void ServerPublishState(float x, float y, float z, int health, int level,
            long experience)
        {
            _x.Value = x;
            _y.Value = y;
            _z.Value = z;
            _health.Value = health < 0 ? 0 : health;
            _level.Value = level < 0 ? 0 : level;
            _experience.Value = experience < 0 ? 0 : experience;
        }

        /// <summary>
        /// Asks the server to attack something.
        /// </summary>
        /// <remarks>
        /// <b>Ownership is the authentication.</b> FishNet refuses a
        /// <see cref="ServerRpcAttribute"/> from a connection that does not own the object,
        /// and the server established that ownership from the authenticated session when it
        /// spawned this. So "which character is attacking" is answered by which object the
        /// message arrived through, and there is no field to forge.
        ///
        /// <b>Ids as strings, deliberately.</b> <see cref="InstanceId"/> and
        /// <see cref="DefinitionId"/> are structs wrapping a string; sending the string and
        /// rebuilding them here keeps the wire format something FishNet already serialises
        /// without a custom serialiser, and a malformed id resolves to nothing on the server
        /// rather than crashing a reader.
        ///
        /// <b>It returns nothing.</b> What happened arrives as replicated state -- the
        /// monster's health, the character's experience -- rather than as a return value a
        /// client could mistake for authority.
        /// </remarks>
        [ServerRpc]
        public void RequestAttack(string targetInstanceId, string skillId, int rank,
            long sequence)
        {
            if (_sink == null) return;

            // Owner rather than a parameter. A client that edits its own copy of this call
            // still arrives as itself.
            int connectionId = Owner == null ? -1 : Owner.ClientId;

            if (connectionId < 0) return;

            _sink.Submit(connectionId, new InstanceId(targetInstanceId ?? string.Empty),
                new DefinitionId(skillId ?? string.Empty), rank, sequence);
        }
    }
}
