using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Loads a map's monster configuration and applies it to the live runtime.
    /// </summary>
    /// <remarks>
    /// <b>The reload seam.</b> A designer changes a row in <c>monster_spawn_point</c>, an
    /// operator calls this, and the map changes without a rebuild, a redeploy or a restart.
    /// That is the whole point of the gate: spawning is data, and data can be edited by
    /// somebody who does not write C#.
    ///
    /// <b>Nothing here is client-reachable.</b> No connection id, no network message, no
    /// route. It is called from the server's own composition and by an operator's tooling.
    /// A player who could reload configuration could repopulate a map at will.
    ///
    /// <b>A failed read changes nothing.</b> The source returns null when the API is
    /// unreachable or answers with nonsense, and this leaves the world exactly as it was. A
    /// world that emptied itself because a web server restarted would be far worse than one
    /// running slightly stale configuration.
    ///
    /// <b>It names no transport.</b> The source is the <c>Contracts</c> interface, so this
    /// file knows nothing of HTTP, PHP or SQL -- the same boundary every other authority in
    /// this project keeps.
    /// </remarks>
    public sealed class MonsterConfigurationLoader
    {
        private readonly IMonsterSpawnConfigurationSource _source;
        private readonly MonsterWorldRuntime _runtime;
        private readonly IDefinitionRegistry<MapDefinition> _maps;

        /// <param name="source">Where configuration is read from. Read-only by construction.</param>
        /// <param name="runtime">The authoritative monster runtime this configures.</param>
        /// <param name="maps">
        /// Authored maps, so a nest on a map this server does not have is refused rather
        /// than created somewhere unreachable.
        /// </param>
        public MonsterConfigurationLoader(IMonsterSpawnConfigurationSource source,
            MonsterWorldRuntime runtime, IDefinitionRegistry<MapDefinition> maps)
        {
            _source = source;
            _runtime = runtime;
            _maps = maps;
        }

        /// <summary>What the last load did, for an operator's log.</summary>
        public SpawnConfigurationResult LastResult { get; private set; }

        /// <summary>Whether the last attempt could read configuration at all.</summary>
        /// <remarks>Distinct from an empty result: "the API is down" and "this map is
        /// configured to have no monsters" are different facts, and an operator needs to
        /// know which one they are looking at.</remarks>
        public bool LastReadSucceeded { get; private set; }

        /// <summary>
        /// Reads a map's configuration and applies it, filling nests to their initial count.
        /// </summary>
        /// <returns>How many monsters were spawned by this load. Zero is a normal answer on
        /// a reload, where the nests are already populated.</returns>
        public int Load(DefinitionId map)
        {
            LastReadSucceeded = false;
            LastResult = default;

            if (_source == null || _runtime == null || !map.IsValid) return 0;

            MapSpawnConfiguration configuration = _source.Load(map);

            if (configuration == null) return 0;

            LastReadSucceeded = true;
            LastResult = _runtime.ApplyConfiguration(configuration, _maps);

            if (!LastResult.IsApplied) return 0;

            return _runtime.PopulateToConfiguredCount();
        }
    }
}
