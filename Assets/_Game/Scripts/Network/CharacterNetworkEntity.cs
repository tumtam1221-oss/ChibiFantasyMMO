using ChibiFantasy.Core;
using FishNet.Connection;
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
    /// Where a client's movement input goes once the network has carried it.
    /// </summary>
    /// <remarks>
    /// A second seam beside <see cref="ICharacterCombatRequestSink"/> and for the same
    /// reason: the authority lives in the server assembly, which already references this
    /// one. Two narrow interfaces rather than one wide one, so a class that only moves
    /// characters cannot be handed the ability to resolve a fight.
    ///
    /// <b>Two axes and a sequence.</b> Not a destination, not a speed, not a delta, not a
    /// map, not a character. Every one of those is the server's, and a parameter for any of
    /// them would be a parameter to forge.
    /// </remarks>
    public interface ICharacterMovementRequestSink
    {
        /// <param name="connectionId">Whose object it arrived through. Not client-supplied.</param>
        /// <param name="inputX">Sideways intent, nominally in -1..1.</param>
        /// <param name="inputZ">Forward intent, nominally in -1..1.</param>
        /// <param name="sequence">The client's own ordering number, for replay rejection.</param>
        void Submit(int connectionId, float inputX, float inputZ, long sequence);
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
    /// stands; nothing here reads a transform or takes a position from a client. Since 18.3
    /// a client can ask to move, but it asks with <i>input</i> -- which way it is pressing --
    /// and the server decides how far that gets it. There is no message carrying a position,
    /// so there is no position to disbelieve, and the value combat range is measured against
    /// is the one the server computed.
    ///
    /// <b>It carries no art.</b> Production character art exists but wiring a model, a rig
    /// and an animator to a network object is presentation work, so this prefab is the
    /// network identity alone and a model becomes a child of it when that gate arrives.
    /// </remarks>
    /// <summary>
    /// Where a character's current status effects are read from, on the server.
    /// </summary>
    /// <remarks>
    /// Read-only by design: there is no Apply and no Remove here. Status is decided by the
    /// server's own status service and this exists solely so the owner's first snapshot can
    /// be built at the moment the owner becomes able to receive one -- the timing the
    /// inventory snapshot already learned the hard way.
    /// </remarks>
    public interface ICharacterStatusSource
    {
        /// <summary>Builds the current status of the character on a connection.</summary>
        /// <returns>False when that connection has no character.</returns>
        bool TryBuildStatusSnapshot(int clientId, out StatusSnapshot snapshot);
    }

    public sealed class CharacterNetworkEntity : NetworkBehaviour
    {
        private readonly SyncVar<string> _characterId = new SyncVar<string>();
        private readonly SyncVar<string> _mapId = new SyncVar<string>();

        /// <summary>
        /// The two pieces of visual identity everybody may see.
        /// </summary>
        /// <remarks>
        /// <b>Public information, and only that.</b> A name is already above a player's head
        /// in every MMO and already in the character list; a gender already decides which of
        /// two approved models is standing there, which is visible the moment the character
        /// is. Nothing else about appearance is replicated -- hair, face and outfit are not
        /// on the wire in this project, so a remote player is the base model of their gender.
        ///
        /// <b>The gender crosses as an int on purpose.</b> <c>CharacterGender</c> is authored
        /// vocabulary in Data and this assembly does not reference Data; widening the
        /// assembly graph so a shadow object could name an enum would be the wrong trade.
        /// The same choice the inventory snapshot already makes for equipment slots.
        /// </remarks>
        private readonly SyncVar<int> _gender = new SyncVar<int>();
        private readonly SyncVar<string> _displayName = new SyncVar<string>();

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

        /// <summary>Where movement input goes. Server-side only, and never sent anywhere.</summary>
        private ICharacterMovementRequestSink _movement;

        /// <summary>Where inventory requests go. Server-side only, and never sent anywhere.</summary>
        private ICharacterInventoryRequestSink _inventory;

        /// <summary>
        /// Where the owner's status is read from. Server-side only, and never sent anywhere.
        /// </summary>
        /// <remarks>There is no matching request sink, and that is the point: no client
        /// message applies, removes, refreshes or expires a status effect, so there is no
        /// method on this object for one to arrive through.</remarks>
        private ICharacterStatusSource _status;

        /// <summary>
        /// The last inventory the server sent this client.
        /// </summary>
        /// <remarks>
        /// Held rather than raised as an event so a view can be built whenever it is ready,
        /// including after the message arrived. <see cref="InventoryChanged"/> is for
        /// anything that wants to know the moment it lands.
        /// </remarks>
        public InventorySnapshot Inventory { get; private set; }

        /// <summary>Raised on the owning client when a new snapshot arrives.</summary>
        public event System.Action<InventorySnapshot> InventoryChanged;

        /// <summary>
        /// The last status the server sent this client.
        /// </summary>
        /// <remarks>Held rather than only raised, so a bar built after the message landed
        /// still has something to draw. The same arrangement as the inventory.</remarks>
        public StatusSnapshot Status { get; private set; }

        /// <summary>Raised on the owning client when a new status snapshot arrives.</summary>
        public event System.Action<StatusSnapshot> StatusChanged;

        /// <summary>Which character this is, as the server knows it.</summary>
        public CharacterId Character => new CharacterId(_characterId.Value);

        /// <summary>Which map the server has them on.</summary>
        public DefinitionId Map => new DefinitionId(_mapId.Value);

        /// <summary>
        /// The character's gender, as the numeric value of the authored enum.
        /// </summary>
        /// <remarks>Zero means the server said nothing, which a presenter treats as "use the
        /// fallback" rather than guessing a gender.</remarks>
        public int GenderCode => _gender.Value;

        /// <summary>The name to show above them, as the server holds it.</summary>
        public string DisplayName => _displayName.Value ?? string.Empty;

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

        /// <summary>Points this object's movement input at the server's movement authority.</summary>
        [Server]
        public void ServerUseMovementSink(ICharacterMovementRequestSink sink)
        {
            _movement = sink;
        }

        /// <summary>Points this object's inventory requests at the server's authority.</summary>
        [Server]
        public void ServerUseInventorySink(ICharacterInventoryRequestSink sink)
        {
            _inventory = sink;
        }

        /// <summary>Points this object at where the server keeps this character's status.</summary>
        [Server]
        public void ServerUseStatusSource(ICharacterStatusSource source)
        {
            _status = source;
        }

        /// <summary>
        /// Sends one client its own inventory.
        /// </summary>
        /// <remarks>
        /// <b>A target message, not a synchronised value.</b> A bag is private: a SyncVar
        /// would replicate it to every observer, and the observer scoping that would be
        /// needed to prevent that does not exist in this project. Addressing the owner
        /// directly sidesteps the question entirely -- nobody else is a recipient, so nobody
        /// else can see it.
        ///
        /// <b>Whole state every time.</b> The client replaces what it had rather than
        /// applying a change, so it never maintains inventory state of its own.
        /// </remarks>
        [TargetRpc]
        private void TargetPublishInventory(NetworkConnection connection,
            InventorySnapshot snapshot)
        {
            Inventory = snapshot;

            InventoryChanged?.Invoke(snapshot);
        }

        /// <summary>
        /// Sends the owner its bag the moment it can actually receive one.
        /// </summary>
        /// <remarks>
        /// <b>Not at spawn.</b> A <see cref="TargetRpcAttribute"/> is refused while its
        /// recipient is not yet an observer of the object, and the server spawns a character
        /// before its own client is observing it. Publishing then reaches nobody -- silently,
        /// because a refused target message is a warning and not a failure. This is the
        /// moment it becomes possible, so this is where the first snapshot goes.
        ///
        /// Only for the owner. Everybody else observes this character without ever being
        /// told what is in its bag.
        /// </remarks>
        public override void OnSpawnServer(NetworkConnection connection)
        {
            base.OnSpawnServer(connection);

            if (connection == null || connection != Owner) return;

            if (_inventory != null && _inventory.TryBuildSnapshot(connection.ClientId,
                out InventorySnapshot snapshot))
            {
                TargetPublishInventory(connection, snapshot);
            }

            // Status goes out here for exactly the reason the bag does: a target message is
            // refused while its recipient is not yet an observer, so a snapshot sent at
            // spawn reaches nobody -- silently, because a refused target call is a warning
            // rather than a failure. A player reconnecting into a debuff would arrive with a
            // clean status bar and no way to find out otherwise until it expired.
            if (_status != null && _status.TryBuildStatusSnapshot(connection.ClientId,
                out StatusSnapshot statuses))
            {
                TargetPublishStatus(connection, statuses);
            }
        }

        /// <summary>Sends the owning connection a fresh snapshot.</summary>
        /// <remarks>Server-only and owner-addressed. A character with no connection -- one
        /// that is mid-disconnect -- is simply not sent anything.</remarks>
        [Server]
        public void ServerPublishInventory(in InventorySnapshot snapshot)
        {
            if (Owner == null || !Owner.IsActive) return;

            TargetPublishInventory(Owner, snapshot);
        }

        /// <summary>
        /// Sends one client its own status effects.
        /// </summary>
        /// <remarks>
        /// <b>A target message, for the same reason the bag is one.</b> What a player is
        /// buffed with tells an opponent what they are immune to and when their defensive
        /// window closes. A SyncVar would hand that to every observer, and this project has
        /// no observer scoping to prevent it.
        ///
        /// <b>Whole state every time.</b> The client replaces what it had, so it never
        /// maintains a status list of its own and a dropped removal cannot leave a debuff on
        /// screen forever.
        /// </remarks>
        [TargetRpc]
        private void TargetPublishStatus(NetworkConnection connection, StatusSnapshot snapshot)
        {
            Status = snapshot;

            StatusChanged?.Invoke(snapshot);
        }

        /// <summary>Sends the owning connection its current status effects.</summary>
        [Server]
        public void ServerPublishStatus(in StatusSnapshot snapshot)
        {
            if (Owner == null || !Owner.IsActive) return;

            TargetPublishStatus(Owner, snapshot);
        }

        /// <summary>Publishes who this object represents.</summary>
        [Server]
        public void ServerPublishIdentity(CharacterId character, DefinitionId map,
            int maxHealth, int genderCode = 0, string displayName = null)
        {
            _characterId.Value = character.Value ?? string.Empty;
            _mapId.Value = map.Value ?? string.Empty;
            _maxHealth.Value = maxHealth < 0 ? 0 : maxHealth;
            _gender.Value = genderCode < 0 ? 0 : genderCode;
            _displayName.Value = displayName ?? string.Empty;
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
        /// <summary>
        /// Asks the server to move.
        /// </summary>
        /// <remarks>
        /// <b>Intent, never a destination.</b> The client says which way it is pressing; the
        /// server decides how far that gets it, using its own speed and its own clock. A
        /// client cannot ask to be somewhere, so there is no position to disbelieve.
        ///
        /// <b>Ownership is the authentication</b>, as it is for the attack request: FishNet
        /// refuses this from a connection that does not own the object, and the server
        /// established that ownership from the admitted session. Who is moving is which
        /// object the message came through.
        ///
        /// <b>It returns nothing, and it does not touch a transform.</b> Where the character
        /// ends up arrives as replicated position, the same way every other authoritative
        /// value does. A client that wants to look smooth interpolates towards it; it does
        /// not write it.
        /// </remarks>
        /// <summary>
        /// Asks the server to rearrange or wear something.
        /// </summary>
        /// <remarks>
        /// <b>Slots and a quantity.</b> Never an item's resulting state, never an ownership
        /// claim, never a stat. The server reads what is actually in the slot named and asks
        /// the existing service whether the action is allowed.
        ///
        /// <b>Ownership is the authentication</b>, as with every other request here: FishNet
        /// refuses this from a connection that does not own the object, so a client cannot
        /// reach into another player's bag by editing a field -- there is no field.
        /// </remarks>
        [ServerRpc]
        public void RequestInventoryAction(InventoryAction action, int from, int to,
            int quantity, long sequence)
        {
            if (_inventory == null) return;

            int connectionId = Owner == null ? -1 : Owner.ClientId;

            if (connectionId < 0) return;

            _inventory.Submit(connectionId, action, from, to, quantity, sequence);
        }

        [ServerRpc]
        public void RequestMove(float inputX, float inputZ, long sequence)
        {
            if (_movement == null) return;

            int connectionId = Owner == null ? -1 : Owner.ClientId;

            if (connectionId < 0) return;

            _movement.Submit(connectionId, inputX, inputZ, sequence);
        }

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
