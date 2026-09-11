using ChibiFantasy.Core;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// A monster, as clients see it.
    /// </summary>
    /// <remarks>
    /// <b>This is a shadow, not a monster.</b> The monster is
    /// <c>MonsterRuntimeState</c> on the server, driven by <c>MonsterAiController</c> and
    /// moved by <c>MonsterMovement</c>. This carries a copy of the few facts a client needs
    /// in order to draw something, and owns none of them. Deleting it would cost the game
    /// its visuals and not one rule.
    ///
    /// <b>Every value is <see cref="WritePermission.ServerOnly"/>, which is FishNet's
    /// default and is relied on deliberately.</b> A client that assigned to one of these
    /// would be assigning to its own copy and nothing would leave the machine. That is what
    /// makes "the client cannot become authoritative" structural rather than a rule somebody
    /// has to remember: there is no wire from a client's write back to the server.
    ///
    /// <b>It carries no art.</b> The project has no monster model -- <c>Art/Monsters</c>
    /// holds a <c>.gitkeep</c> and nothing else -- so this prefab is the network identity
    /// alone, and a mesh becomes a child of it when one exists. Attaching a placeholder cube
    /// and calling it a monster would be the fake asset this gate forbids; a network entity
    /// with no renderer is simply the honest state of the project.
    ///
    /// <b>Primitives and ids only.</b> No engine object, no domain object, no live
    /// reference. What crosses the wire is what a renderer needs and nothing more.
    /// </remarks>
    public sealed class MonsterNetworkEntity : NetworkBehaviour
    {
        private readonly SyncVar<string> _instanceId = new SyncVar<string>();
        private readonly SyncVar<string> _definitionId = new SyncVar<string>();
        private readonly SyncVar<string> _mapId = new SyncVar<string>();

        private readonly SyncVar<int> _health = new SyncVar<int>();
        private readonly SyncVar<int> _maxHealth = new SyncVar<int>();

        private readonly SyncVar<float> _x = new SyncVar<float>();
        private readonly SyncVar<float> _y = new SyncVar<float>();
        private readonly SyncVar<float> _z = new SyncVar<float>();

        /// <summary>
        /// How many times the server has had this monster swing.
        /// </summary>
        /// <remarks>
        /// <b>A count, not an event.</b> A client that joins late, or misses a packet, sees
        /// the number it should and simply does not replay the swings it was not there for.
        /// An RPC per swing would be a message per monster per second, and a missed one
        /// would leave a monster attacking in silence.
        ///
        /// <b>A hit needs no equivalent.</b> Health already falls when a monster is struck,
        /// and the presentation reads the fall -- so a hit reaction costs nothing on the
        /// wire, and cannot disagree with the health it is reacting to.
        /// </remarks>
        private readonly SyncVar<int> _swings = new SyncVar<int>();

        /// <summary>
        /// Which way the monster is facing, in degrees of yaw.
        /// </summary>
        /// <remarks>
        /// <b>Replicated rather than inferred, because of the jump attack.</b> Facing used to
        /// be guessed on the client from the direction the monster was seen to travel, which
        /// is fine while it is walking and useless at the moment it matters most: a monster
        /// standing still in reach, about to leap, has no travel to infer from, so it would
        /// jump whichever way it last happened to walk.
        ///
        /// The server knows what it is looking at. One float is the cheapest honest answer,
        /// and it also makes a chase face its target exactly rather than approximately.
        /// </remarks>
        private readonly SyncVar<float> _facing = new SyncVar<float>();

        /// <summary>Which monster this is, as the server knows it.</summary>
        public InstanceId Instance => new InstanceId(_instanceId.Value);

        /// <summary>What it is, by authored definition.</summary>
        public DefinitionId Definition => new DefinitionId(_definitionId.Value);

        /// <summary>Where it belongs. A client on another map should never observe it.</summary>
        public DefinitionId Map => new DefinitionId(_mapId.Value);

        public int Health => _health.Value;

        public int MaxHealth => _maxHealth.Value;

        public float X => _x.Value;

        public float Y => _y.Value;

        public float Z => _z.Value;

        /// <summary>Which way the server says it is facing, in degrees of yaw.</summary>
        public float Facing => _facing.Value;

        /// <summary>How many swings the server has had this monster throw.</summary>
        /// <remarks>Presentation reads the change, never the value: what matters is that it
        /// went up, which is when an attack animation should play.</remarks>
        public int Swings => _swings.Value;

        /// <summary>Whether the server says it is standing.</summary>
        /// <remarks>Derived from replicated health rather than synced separately, so the two
        /// cannot disagree on the wire.</remarks>
        public bool IsAlive => _health.Value > 0;

        /// <summary>
        /// Publishes the identity of the monster this object represents.
        /// </summary>
        /// <remarks>
        /// Server-only, and guarded twice: by the attribute, which FishNet enforces, and by
        /// the write permission on every value, which makes a client's assignment local and
        /// pointless. Called once, immediately after the server spawns the object.
        /// </remarks>
        [Server]
        public void ServerPublishIdentity(InstanceId instance, DefinitionId definition,
            DefinitionId map, int maxHealth)
        {
            _instanceId.Value = instance.Value ?? string.Empty;
            _definitionId.Value = definition.Value ?? string.Empty;
            _mapId.Value = map.Value ?? string.Empty;
            _maxHealth.Value = maxHealth < 0 ? 0 : maxHealth;
        }

        /// <summary>
        /// Publishes what changed this tick.
        /// </summary>
        /// <remarks>
        /// Position and health together, because they are what a renderer needs and they
        /// change on the same tick. Called by the server's replication service after the
        /// authoritative runtime has already decided both -- this never computes either.
        /// </remarks>
        [Server]
        public void ServerPublishState(float x, float y, float z, int health, float facing)
        {
            _x.Value = x;
            _y.Value = y;
            _z.Value = z;
            _health.Value = health < 0 ? 0 : health;

            // Compared before assigning: yaw changes far less often than position, and a
            // SyncVar written with the value it already holds is still a write.
            if (_facing.Value != facing) _facing.Value = facing;
        }

        /// <summary>Publishes how many times this monster has swung.</summary>
        /// <remarks>Separate from the state above because it is written by the thing that
        /// resolves attacks rather than by the thing that replicates positions, and because
        /// it changes far less often than either.</remarks>
        [Server]
        public void ServerPublishSwings(int swings)
        {
            if (swings < 0) swings = 0;

            if (_swings.Value == swings) return;

            _swings.Value = swings;
        }
    }
}
