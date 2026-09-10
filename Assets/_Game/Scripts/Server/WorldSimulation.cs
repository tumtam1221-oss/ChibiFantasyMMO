using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// The server's world lifecycle: one tick, in one order.
    /// </summary>
    /// <remarks>
    /// <b>What this closes.</b> Every authority this project has built -- movement, combat,
    /// monsters, status, stats, replication -- was composed only inside test fixtures, each
    /// of which drove them in whatever order that test needed. Nothing ran them in a shipped
    /// server. This is the missing half: the one place that owns them and the one order they
    /// run in.
    ///
    /// <b>The order is the design, not an implementation detail.</b> Status expires before
    /// stats recompute, and stats recompute before anything is published. Any other order
    /// produces a visible tick in which a player's buff icon has gone but their attack is
    /// still buffed -- or worse, the reverse. Written out:
    ///
    /// <list type="number">
    /// <item>status: timers advance, expired effects are removed, owners are told</item>
    /// <item>stats: the changed set of modifiers is noticed and derived stats recomputed</item>
    /// <item>movement and combat: the server clock advances swing timers and cooldowns</item>
    /// <item>monsters: spawning, AI and retirement</item>
    /// <item>replication: whatever all of that produced is published</item>
    /// </list>
    ///
    /// <b>Everything is optional.</b> A world composed without monsters simply has none; a
    /// combat sandbox with no status content silences nobody. Absence is a legitimate
    /// configuration and is not the same as a fault, so nothing here throws for a missing
    /// authority -- it does less.
    ///
    /// <b>It decides nothing.</b> Not one rule lives here. Every line either advances a
    /// clock somebody else owns or asks somebody else whether anything changed.
    /// </remarks>
    public sealed class WorldSimulation
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly CharacterStatusAuthority _status;
        private readonly CharacterStatAuthority _stats;
        private readonly CharacterMovementAuthority _movement;
        private readonly ServerCombatPipeline _combat;
        private readonly MonsterWorldRuntime _monsters;
        private readonly MonsterLootRegistry _loot;

        /// <summary>
        /// Who tells players what is on the ground. Optional.
        /// </summary>
        /// <remarks>A world composed without one still drops loot and still lets a player
        /// pick it up; it simply never volunteers that a pile is there, which is what the
        /// world did until 18.18B1.</remarks>
        private readonly CharacterLootAuthority _lootAuthority;

        /// <summary>Who tells players what is in their bag. Optional.</summary>
        /// <remarks>A world composed without one still grants items and still persists
        /// them; it simply never tells the player they arrived, which is what the world did
        /// until this gate.</remarks>
        private readonly CharacterInventoryAuthority _inventoryAuthority;
        private readonly MonsterRewardAuthority _rewards;
        private readonly CharacterReplicationService _replication;
        private readonly MonsterReplicationService _monsterReplication;
        private readonly WorldClock _clock;
        private readonly WeatherDirector _weather;

        public WorldSimulation(WorldCharacterRegistry characters,
            CharacterReplicationService replication = null,
            CharacterStatusAuthority status = null,
            CharacterStatAuthority stats = null,
            CharacterMovementAuthority movement = null,
            ServerCombatPipeline combat = null,
            MonsterWorldRuntime monsters = null,
            MonsterLootRegistry loot = null,
            MonsterReplicationService monsterReplication = null,
            MonsterRewardAuthority rewards = null,
            CharacterLootAuthority lootAuthority = null,
            CharacterInventoryAuthority inventoryAuthority = null,
            WorldClock clock = null,
            WeatherDirector weather = null)
        {
            _lootAuthority = lootAuthority;
            _inventoryAuthority = inventoryAuthority;
            _characters = characters;
            _replication = replication;
            _status = status;
            _stats = stats;
            _movement = movement;
            _combat = combat;
            _monsters = monsters;
            _loot = loot;
            _monsterReplication = monsterReplication;
            _rewards = rewards;
            _clock = clock ?? new WorldClock();
            _weather = weather ?? new WeatherDirector();
        }

        /// <summary>How many ticks have run. For diagnostics and for the no-work test.</summary>
        public long Ticks { get; private set; }

        /// <summary>The seconds of world time this simulation has advanced.</summary>
        public double Elapsed { get; private set; }

        /// <summary>
        /// The sky every player stands under.
        /// </summary>
        /// <remarks>Exposed rather than hidden because the thing that replicates it lives in
        /// the network assembly and has to read it once a tick. It is read-only from out
        /// here: <see cref="Tick"/> is the only thing that advances it, so the world's time
        /// cannot be moved by whoever happens to hold a reference.</remarks>
        public WorldClock Clock => _clock;

        /// <summary>
        /// The weather every player is standing in.
        /// </summary>
        /// <remarks>Exposed for the same reason as <see cref="Clock"/>: the thing that
        /// broadcasts a change lives in the network assembly and subscribes to
        /// <see cref="WeatherDirector.Changed"/>. Advancing it stays with
        /// <see cref="Tick"/>.</remarks>
        public WeatherDirector Weather => _weather;

        /// <summary>
        /// Admits a character into the world with correct stats from its first instant.
        /// </summary>
        /// <remarks>
        /// <b>The ordering that makes a safe spawn safe.</b> The character is placed in the
        /// registry, its derived stats are computed from its persisted attributes and its
        /// persisted equipment, and only then does anything else look at it. Nothing is
        /// replicated in between, so no client ever receives the moment before the ceilings
        /// existed.
        ///
        /// <b>No ceilings are supplied.</b> Handing <c>Spawn</c> a guessed
        /// <see cref="ResourceLimits"/> is what used to clamp a character loaded with
        /// seventy-five health down to zero before the real maximum was known. The authority
        /// reads the maximum out of the authored formula a moment later, which is the only
        /// figure that was ever correct.
        /// </remarks>
        public WorldSpawnResult Admit(int connectionId, in WorldAdmission admission,
            CombatTeam team = default)
        {
            if (_characters == null)
            {
                return WorldSpawnResult.Refused(WorldSpawnRejection.NotAdmitted,
                    "no character registry");
            }

            WorldSpawnResult spawned = _characters.Spawn(connectionId, admission,
                ResourceLimits.None, team);

            if (!spawned.IsSpawned) return spawned;

            // Before anything reads a stat, publishes a ceiling or resolves a fight.
            _stats?.Force(spawned.Character);

            return spawned;
        }

        /// <summary>
        /// Removes a character and forgets everything cached about it.
        /// </summary>
        /// <remarks>Forgetting matters on a reconnect: a returning player is a new character
        /// object whose signature and last-published status may coincidentally match the old
        /// one, and "unchanged since last time" would then skip telling them anything.</remarks>
        public CharacterPersistenceResult Release(int connectionId)
        {
            if (_characters == null)
            {
                return CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            CharacterId character = _characters.TryGet(connectionId, out LivingCharacter living)
                ? living.Character
                : default;

            CharacterPersistenceResult result = _characters.Despawn(connectionId);

            if (character.IsValid)
            {
                _stats?.Forget(character);
                _status?.Forget(character);
                _combat?.Forget(character);
            }

            _replication?.Synchronise();

            return result;
        }

        /// <summary>
        /// One authoritative tick of the whole world.
        /// </summary>
        /// <remarks>Time arrives as an argument, matching every authority underneath. The
        /// checks are cheap -- a revision comparison per character -- and the work behind
        /// them happens only when something actually moved.</remarks>
        public void Tick(float deltaSeconds)
        {
            Ticks++;

            if (deltaSeconds > 0f) Elapsed += deltaSeconds;

            // 0. The sky. First because it is the cheapest thing here and because every
            //    other system is entitled to ask what time it is during its own tick.
            _clock.Advance(deltaSeconds);

            // 0b. And the weather, which announces itself when it turns rather than being
            //     polled. Nothing downstream reads it, so its place in the order is free.
            _weather.Tick(deltaSeconds);

            // 1. Status first: an effect that expires this tick must be gone before
            //    anything asks what modifiers are in force.
            _status?.Tick(deltaSeconds);

            // 2. Stats second, so the expiry above is reflected in the same tick rather
            //    than a frame later. This is where the icon and the number stay in step.
            _stats?.RefreshAll();

            // 3. The clocks combat depends on.
            _movement?.Tick(deltaSeconds);
            _combat?.Tick(deltaSeconds);

            // 3b. Anybody who has walked since they were last written down. Nothing else
            //     saves a walking character, so without this a player who moved and then
            //     lost their connection comes back where they started.
            _characters?.TickAutosave(deltaSeconds);

            // 4. Monsters: spawning, thinking, retiring, and the piles they left.
            _monsters?.Tick(deltaSeconds);
            _loot?.Tick(deltaSeconds);

            // And anybody standing near a pile that has just appeared, gone, or been dipped
            // into is told. Nothing is sent when the ground has not changed.
            _lootAuthority?.PublishChanged();

            // And anybody whose bag changed -- because they picked something up, or because
            // they have just arrived and have never been told what they are carrying.
            _inventoryAuthority?.PublishChanged();

            // A defeat whose party turn would not commit is decided but unpaid. Retried
            // here because this is already the step that owns monsters and their piles,
            // and because the pile it is holding belongs in this world, not another one.
            _rewards?.RetryHeld();

            // 5. And only now is any of it published.
            _replication?.Synchronise();
            _monsterReplication?.Synchronise();
        }

        /// <summary>
        /// Brings the world back into agreement immediately after a change.
        /// </summary>
        /// <remarks>
        /// <b>For mutations that do not wait for a tick.</b> A combat command and an
        /// inventory action are handled the moment they arrive, not on the next frame, so a
        /// skill that lands a debuff changes the modifier set between ticks. Without this, a
        /// second command arriving in the same frame would be resolved against the stats the
        /// first one just invalidated.
        ///
        /// It is the change-driven half of <see cref="Tick"/> and nothing else: no clock is
        /// advanced, so calling it cannot make time pass twice.
        /// </remarks>
        public void Settle()
        {
            _stats?.RefreshAll();
            _status?.PublishChanged();
            _replication?.Synchronise();
            _monsterReplication?.Synchronise();
        }
    }
}
