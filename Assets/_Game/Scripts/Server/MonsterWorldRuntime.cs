using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>One monster the server is currently authoritative for.</summary>
    /// <remarks>
    /// The Phase 10 runtime state, the Phase 10 AI controller and the Phase 07 combatant
    /// adapter, held together. It defines none of them. There is no server-side monster
    /// model, for the same reason there is no server-side character model: a second model is
    /// a second set of rules that will disagree with the first.
    /// </remarks>
    public sealed class LivingMonster
    {
        internal LivingMonster(MonsterRuntimeState state, MonsterAiController ai,
            MonsterCombatant combatant, DefinitionId map)
        {
            State = state;
            Ai = ai;
            Combatant = combatant;
            Map = map;
        }

        /// <summary>Phase 10's state. Health, target, defeat claim and respawn live here.</summary>
        public MonsterRuntimeState State { get; }

        /// <summary>Phase 10's state machine. Idle, Wander, Detect, Chase, Attack, Return, Dead.</summary>
        public MonsterAiController Ai { get; }

        /// <summary>Phase 07's adapter, so combat sees a monster the same way it sees anybody.</summary>
        public MonsterCombatant Combatant { get; }

        /// <summary>The map it belongs to. A monster never leaves it.</summary>
        public DefinitionId Map { get; }

        public InstanceId Instance => State.InstanceId;

        public bool IsAlive => State.IsAlive;

        public override string ToString()
        {
            return State.DefinitionId + " " + Instance + " (" + Ai.State + ")";
        }
    }

    /// <summary>What one server tick did to the monsters.</summary>
    public readonly struct MonsterTickResult
    {
        internal MonsterTickResult(int spawned, int retired, int moved,
            IReadOnlyList<InstanceId> attacking)
        {
            Spawned = spawned;
            Retired = retired;
            Moved = moved;
            Attacking = attacking ?? System.Array.Empty<InstanceId>();
        }

        /// <summary>How many monsters appeared, from a first fill or a respawn.</summary>
        public int Spawned { get; }

        /// <summary>How many defeated monsters were cleared away.</summary>
        public int Retired { get; }

        /// <summary>How many monsters actually changed position this tick.</summary>
        /// <remarks>Chasing and returning monsters only. A monster standing in Idle or
        /// striking in Attack is working correctly and is not counted.</remarks>
        public int Moved { get; }

        /// <summary>
        /// Monsters whose AI decided to swing this tick.
        /// </summary>
        /// <remarks>
        /// Reported rather than executed. The AI decides; the combat runtime applies. Running
        /// damage from inside this tick would make the AI a second combat path, which is the
        /// one thing Phase 10 was careful to avoid.
        /// </remarks>
        public IReadOnlyList<InstanceId> Attacking { get; }

        public override string ToString()
        {
            return "+" + Spawned + " -" + Retired + " moved " + Moved
                + " attacking " + Attacking.Count;
        }
    }

    /// <summary>
    /// The monsters on this world server: who exists, what they are doing, and who they are.
    /// </summary>
    /// <remarks>
    /// <b>Entirely server-owned, because there is no client command for a monster.</b> Every
    /// other authority in this phase resolves something a client asked for. This one has no
    /// inbound command at all: a monster spawns, notices, chases, swings, dies and comes back
    /// without a client ever being consulted. That is not a rule this file enforces — it is
    /// the absence of any method a client could reach.
    ///
    /// <b>It composes Phase 10; it reimplements none of it.</b> Spawning and respawn timing
    /// are <see cref="MonsterSpawnService"/>'s, behaviour is
    /// <see cref="MonsterAiController"/>'s, the defeat claim is
    /// <see cref="MonsterDefeatService"/>'s, and the drop roll is <c>DropResolver</c>'s. What
    /// was missing was something to own them per map and drive them on a tick.
    ///
    /// <b>It is also the missing <see cref="ICombatantResolver"/>.</b> Phase 17.12 defined
    /// that seam and nothing implemented it, so a combat command could not resolve a target.
    /// This resolves both sides: monsters from its own table, players from the character
    /// registry. Both are looked up, never sent.
    ///
    /// <b>Map scoping is a rule, not an optimisation.</b> A monster only ever considers
    /// players standing on its own map. Without that, a player on another map would be a
    /// legal target at the same coordinates, and a monster would chase somebody it can never
    /// reach.
    ///
    /// <b>Nothing here is persisted.</b> Monsters are runtime state by design — Phase 15's
    /// schema has no monster table and should not gain one. A server restart repopulates from
    /// authored spawn points, which is the correct behaviour and why
    /// <see cref="MonsterRuntimeState"/> is an <c>IRuntimeState</c> rather than an
    /// <c>IPersistentState</c>.
    ///
    /// <b>Movement is applied here and decided nowhere near a client.</b> After the AI
    /// settles a state, <see cref="MonsterMovement.Step"/> advances the position. There is no
    /// inbound message carrying a monster destination, no method taking one, and no field a
    /// client could write -- a monster's position is a consequence of server state, never a
    /// request. That is deliberately not a validated command path: a monster claims nothing,
    /// so there is nothing to disbelieve.
    /// </remarks>
    public sealed class MonsterWorldRuntime : ICombatantResolver
    {
        private readonly WorldCharacterRegistry _players;
        private readonly IDefinitionRegistry<MonsterDefinition> _definitions;
        private readonly IDefinitionRegistry<MapDefinition> _maps;
        private readonly DefinitionId _maxHealthStat;
        private readonly CombatTeam _monsterTeam;

        private readonly List<MonsterSpawnService> _spawners = new List<MonsterSpawnService>();

        private readonly Dictionary<string, LivingMonster> _byInstance =
            new Dictionary<string, LivingMonster>();

        /// <summary>Reused per tick so a steady-state server allocates nothing for candidates.</summary>
        private readonly List<ICombatant> _candidates = new List<ICombatant>();

        private readonly List<InstanceId> _attacking = new List<InstanceId>();

        /// <summary>Reused per spawner, so retiring allocates nothing in a steady state.</summary>
        private readonly List<string> _retiring = new List<string>();

        /// <param name="players">Where the players are. Read, never written.</param>
        /// <param name="definitions">Authored monsters. No monster exists without one.</param>
        /// <param name="maxHealthStat">
        /// Which authored stat is maximum health. Supplied rather than assumed, because the
        /// stat list is content and naming one here would put content in code.
        /// </param>
        /// <param name="monsterTeam">
        /// The faction monsters belong to. Configuration, not a literal: <c>CombatTeam</c> is
        /// an opaque integer precisely so a rival guild or neutral wildlife can be added
        /// without editing an enum.
        /// </param>
        /// <param name="maps">
        /// Authored maps, read only for their movement radius. Optional: a null registry, or
        /// a map with no authored radius, means unbounded -- the same rule players are held
        /// to, so existing content keeps working and authoring a radius is what turns the
        /// check on.
        /// </param>
        public MonsterWorldRuntime(WorldCharacterRegistry players,
            IDefinitionRegistry<MonsterDefinition> definitions, DefinitionId maxHealthStat,
            CombatTeam monsterTeam, IDefinitionRegistry<MapDefinition> maps = null)
        {
            _players = players;
            _definitions = definitions;
            _maxHealthStat = maxHealthStat;
            _monsterTeam = monsterTeam;
            _maps = maps;
        }

        public int AliveCount => _byInstance.Count;

        public int SpawnerCount => _spawners.Count;

        /// <summary>Every monster currently in the world.</summary>
        public IReadOnlyList<LivingMonster> All()
        {
            var all = new List<LivingMonster>(_byInstance.Count);

            foreach (KeyValuePair<string, LivingMonster> pair in _byInstance) all.Add(pair.Value);

            return all;
        }

        /// <summary>
        /// Registers an authored spawn point.
        /// </summary>
        /// <remarks>
        /// Takes the Phase 10 <see cref="MonsterSpawnPoint"/>, which
        /// <c>MonsterSpawnPlacement.FromSpawnPoint</c> builds from an authored
        /// <c>SpawnPointDefinition</c>. Nothing here invents a position, a radius, a
        /// population cap or a respawn delay -- every one of them is content.
        /// </remarks>
        public bool AddSpawnPoint(in MonsterSpawnPoint point)
        {
            if (!point.IsValid || !point.Map.IsValid) return false;

            _spawners.Add(new MonsterSpawnService(point, _maxHealthStat));

            return true;
        }

        /// <summary>
        /// Advances every monster by one server tick.
        /// </summary>
        /// <remarks>
        /// The order is deliberate. Defeated monsters are retired first so they stop being
        /// targets and free a population slot; respawns are then due against a correct count;
        /// and behaviour runs last, against the world as it now is rather than as it was at
        /// the top of the frame.
        ///
        /// Time arrives as an argument. No clock is read here, matching every other service
        /// in this project, which is what makes a five-second chase reproducible in a test.
        /// </remarks>
        public MonsterTickResult Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;

            _attacking.Clear();

            int retired = Retire();
            int spawned = Respawn(deltaSeconds);

            int moved = DriveBehaviour(deltaSeconds);

            return new MonsterTickResult(spawned, retired, moved, _attacking.ToArray());
        }

        /// <summary>
        /// Fills every spawn point to its authored population.
        /// </summary>
        /// <remarks>What a server calls once after loading content, so a map is not empty
        /// until the first respawn timer elapses.</remarks>
        public int PopulateAll()
        {
            int spawned = 0;

            for (int i = 0; i < _spawners.Count; i++)
            {
                MonsterSpawnService spawner = _spawners[i];

                while (spawner.AliveCount < spawner.Point.MaxAlive)
                {
                    if (!TrySpawnFrom(spawner)) break;

                    spawned++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// Clears away corpses whose reward has been collected.
        /// </summary>
        /// <remarks>
        /// <b>A dead monster stays until its defeat is claimed.</b> That is Phase 10's rule,
        /// enforced by <see cref="MonsterSpawnService.RetireDefeated"/>, and it is the right
        /// one: retiring an unclaimed corpse would destroy the experience and loot it owed
        /// somebody. So a corpse remains resolvable until then, which is what lets
        /// <see cref="ClaimDefeat"/> find it at all.
        ///
        /// The first version of this method removed a monster from the lookup the moment it
        /// died. The spawn service then refused to retire it -- correctly -- leaving a
        /// monster that was in one collection and not the other, and a reward nobody could
        /// ever claim. A test caught it.
        ///
        /// Which monsters are about to go is therefore worked out <i>before</i> the retire
        /// call, because afterwards they are gone from the list that named them.
        /// </remarks>
        private int Retire()
        {
            int retired = 0;

            for (int i = 0; i < _spawners.Count; i++)
            {
                _retiring.Clear();

                foreach (MonsterRuntimeState state in _spawners[i].Alive)
                {
                    if (state.IsAlive || !state.IsDefeatClaimed) continue;

                    _retiring.Add(state.InstanceId.Value);
                }

                int count = _spawners[i].RetireDefeated();

                if (count == 0) continue;

                for (int n = 0; n < _retiring.Count; n++) _byInstance.Remove(_retiring[n]);

                retired += count;
            }

            return retired;
        }

        private int Respawn(float deltaSeconds)
        {
            int spawned = 0;

            for (int i = 0; i < _spawners.Count; i++)
            {
                int due = _spawners[i].Tick(deltaSeconds);

                for (int n = 0; n < due; n++)
                {
                    if (TrySpawnFrom(_spawners[i])) spawned++;
                }
            }

            return spawned;
        }

        private bool TrySpawnFrom(MonsterSpawnService spawner)
        {
            MonsterRuntimeState state = spawner.TrySpawn(_definitions, _monsterTeam);

            if (state == null) return false;

            var living = new LivingMonster(state, new MonsterAiController(state),
                new MonsterCombatant(state), spawner.Point.Map);

            _byInstance[state.InstanceId.Value] = living;

            return true;
        }

        /// <summary>
        /// Runs every monster's AI against the players standing on its map.
        /// </summary>
        /// <remarks>
        /// Candidates are gathered per map rather than per monster, so a map with forty
        /// monsters and six players builds one list rather than forty. The list is reused
        /// across ticks, so a steady-state server allocates nothing here.
        /// </remarks>
        private int DriveBehaviour(float deltaSeconds)
        {
            int moved = 0;

            for (int i = 0; i < _spawners.Count; i++)
            {
                DefinitionId map = _spawners[i].Point.Map;

                GatherCandidatesOn(map);

                float radius = RadiusOf(map);

                foreach (MonsterRuntimeState state in _spawners[i].Alive)
                {
                    if (!_byInstance.TryGetValue(state.InstanceId.Value, out LivingMonster living))
                    {
                        continue;
                    }

                    // Decide first, then move. The AI settles a state against the world as it
                    // is; movement is the consequence, never the cause.
                    living.Ai.Tick(deltaSeconds, _candidates);

                    if (living.Ai.WantsToAttack) _attacking.Add(living.Instance);

                    if (MonsterMovement.Step(state, living.Ai.State,
                        DestinationFor(living, map), deltaSeconds, radius).Moved)
                    {
                        moved++;
                    }
                }
            }

            return moved;
        }

        /// <summary>
        /// Where a chasing monster's target actually is, or null.
        /// </summary>
        /// <remarks>
        /// Resolved from the server's own tables, never from anything a client sent — there
        /// is no message that carries a monster destination.
        ///
        /// Null is returned for a target that has gone, died, or is on another map. A monster
        /// then does not move this tick and the AI drops it next tick, which is the correct
        /// order: behaviour notices the loss, movement merely stops.
        /// </remarks>
        private CombatPosition? DestinationFor(LivingMonster monster, DefinitionId map)
        {
            InstanceId target = monster.State.TargetId;

            if (!target.IsValid) return null;

            if (!TryResolve(target, out ICombatant combatant)) return null;

            if (!combatant.IsAlive()) return null;

            // A target on another map is not a destination. Without this a monster would
            // walk toward coordinates that mean nothing on the map it is standing on.
            if (!TryGetMap(target, out DefinitionId targetMap) || targetMap != map) return null;

            return combatant.Position;
        }

        /// <summary>The authored movement bound for a map, or zero for unbounded.</summary>
        private float RadiusOf(DefinitionId map)
        {
            if (_maps == null || !map.IsValid) return 0f;

            return _maps.TryGet(map, out MapDefinition definition) && definition != null
                ? definition.MovementRadius
                : 0f;
        }

        /// <summary>
        /// The players a monster on this map may notice.
        /// </summary>
        /// <remarks>
        /// Living players only, on this map only. A dead player is not a target -- Phase 10's
        /// targeting would skip them anyway, but leaving them in the list would have a
        /// monster stand over a corpse rather than going home.
        /// </remarks>
        private void GatherCandidatesOn(DefinitionId map)
        {
            _candidates.Clear();

            if (_players == null || !map.IsValid) return;

            foreach (LivingCharacter player in _players.All())
            {
                if (player.Location == null || !player.Location.IsOn(map)) continue;
                if (player.Combatant == null || !player.Combatant.IsAlive()) continue;

                _candidates.Add(player.Combatant);
            }
        }

        // ---- defeat ------------------------------------------------------------------------

        /// <summary>
        /// Claims a monster's defeat, exactly once.
        /// </summary>
        /// <remarks>
        /// <b>The exactly-once guarantee is Phase 10's, not a new one.</b>
        /// <see cref="MonsterDefeatService.Resolve"/> calls
        /// <see cref="MonsterRuntimeState.TryClaimDefeat"/>, which succeeds once per life. Two
        /// players landing a killing blow in the same tick, or the same player's message
        /// arriving twice, both produce one reward — and the second call returns
        /// <see cref="MonsterDefeatResult.NotClaimed"/> rather than an error, because a
        /// duplicate is a race rather than a fault.
        ///
        /// <b>It grants nothing.</b> The result says what the kill is worth; putting
        /// experience into a character and loot into a bag is a later sub-phase with its own
        /// persistence boundary. Granting here would write to a character from inside a
        /// monster tick, which is exactly the kind of hidden mutation this design avoids.
        /// </remarks>
        public MonsterDefeatResult ClaimDefeat(InstanceId monster, InstanceId killer,
            in DropResolver.Context drops, List<LootResult> loot,
            InstanceId[] participants = null)
        {
            if (!TryGetMonster(monster, out LivingMonster living))
            {
                return MonsterDefeatResult.NotClaimed;
            }

            if (living.IsAlive)
            {
                // Still standing. Claiming a defeat that has not happened would mint a
                // reward from nothing.
                return MonsterDefeatResult.NotClaimed;
            }

            return MonsterDefeatService.Resolve(living.State, killer, drops, loot, participants);
        }

        public bool TryGetMonster(InstanceId instance, out LivingMonster monster)
        {
            monster = null;

            return instance.IsValid && !string.IsNullOrEmpty(instance.Value)
                && _byInstance.TryGetValue(instance.Value, out monster);
        }

        /// <summary>Empties the world, for a shutdown or an area reset.</summary>
        public int Clear()
        {
            int cleared = _byInstance.Count;

            for (int i = 0; i < _spawners.Count; i++) _spawners[i].Clear();

            _byInstance.Clear();

            return cleared;
        }

        // ---- ICombatantResolver ---------------------------------------------------------------

        /// <summary>
        /// The combatant behind an instance id, monster or player.
        /// </summary>
        /// <remarks>
        /// <b>This is the seam 17.12 was written against and nothing filled.</b> A combat
        /// command names a target; without a resolver there was nothing to look it up in, so
        /// every command refused with <c>UnknownTarget</c>. Both sides resolve here because
        /// both are things this server is authoritative for, and a caller should not have to
        /// know which kind it asked about.
        ///
        /// Monsters are checked first: they are the common target, and the dictionary lookup
        /// is cheaper than walking the player list.
        /// </remarks>
        public bool TryResolve(InstanceId instance, out ICombatant combatant)
        {
            combatant = null;

            if (!instance.IsValid || string.IsNullOrEmpty(instance.Value)) return false;

            if (_byInstance.TryGetValue(instance.Value, out LivingMonster monster))
            {
                combatant = monster.Combatant;

                return true;
            }

            if (_players != null
                && _players.TryGetByCharacter(new CharacterId(instance.Value),
                    out LivingCharacter player)
                && player.Combatant != null)
            {
                // A player's combatant id is their character id projected onto InstanceId,
                // which is why this lookup is exact rather than a search.
                combatant = player.Combatant;

                return true;
            }

            return false;
        }

        /// <summary>Which map something is on, so a cross-map attack can be refused.</summary>
        public bool TryGetMap(InstanceId instance, out DefinitionId map)
        {
            map = default;

            if (!instance.IsValid || string.IsNullOrEmpty(instance.Value)) return false;

            if (_byInstance.TryGetValue(instance.Value, out LivingMonster monster))
            {
                map = monster.Map;

                return map.IsValid;
            }

            if (_players != null
                && _players.TryGetByCharacter(new CharacterId(instance.Value),
                    out LivingCharacter player)
                && player.Location != null)
            {
                map = player.Location.CurrentMap;

                return map.IsValid;
            }

            return false;
        }
    }
}
