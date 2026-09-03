using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the map, portal and NPC panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for travel and NPC interaction.</b> Every change these panels
    /// can cause goes through a submit method here, and each calls an existing service --
    /// <see cref="TravelService"/> or <see cref="NpcInteractionService"/>. No view holds a
    /// location state, a portal or an NPC definition, so there is nowhere else a journey
    /// could start.
    ///
    /// <b>Deciding and presenting are two steps.</b> A submitted travel returns a
    /// <see cref="TravelResult"/>; only if that was accepted does the scene loader run. The
    /// loader cannot refuse a journey and the service cannot load a scene, which is the
    /// whole point of splitting them.
    ///
    /// <b>Nothing is polled.</b> Prompts are rebuilt when the player's location revision
    /// moves, not every frame.
    /// </remarks>
    public sealed class WorldUiController : MonoBehaviour
    {
        [SerializeField] private MapNameView mapNameView;
        [SerializeField] private PortalInteractionView portalView;
        [SerializeField] private NpcInteractionView npcView;
        [SerializeField] private ChibiFantasy.Client.World.MapSceneLoader sceneLoader;

        private readonly List<PortalViewData> _portals = new List<PortalViewData>();
        private readonly List<NpcViewData> _npcs = new List<NpcViewData>();

        private CharacterLocationState _location;
        private ItemContainerState _inventory;

        private IDefinitionRegistry<MapDefinition> _maps;
        private IDefinitionRegistry<SpawnPointDefinition> _spawnPoints;
        private IDefinitionRegistry<PortalDefinition> _portalDefinitions;
        private IDefinitionRegistry<NPCDefinition> _npcDefinitions;
        private IDefinitionRegistry<ShopDefinition> _shops;
        private IDefinitionRegistry<QuestDefinition> _quests;
        private IDefinitionRegistry<ItemDefinition> _items;

        private int _characterLevel = 1;
        private bool _bound;
        private Revision _lastLocationRevision;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>The answer to the last journey submitted.</summary>
        public TravelResult LastTravelResult { get; private set; }

        /// <summary>The answer to the last NPC interaction submitted.</summary>
        public NpcInteractionResult LastInteractionResult { get; private set; }

        /// <summary>The portals on the player's current map.</summary>
        public IReadOnlyList<PortalViewData> Portals => _portals;

        /// <summary>The NPCs on the player's current map.</summary>
        public IReadOnlyList<NpcViewData> Npcs => _npcs;

        /// <summary>
        /// Raised when a journey is accepted and presentation should follow.
        /// </summary>
        /// <remarks>Carries the result rather than a scene, because deciding where someone
        /// went and bringing that place up are two different jobs.</remarks>
        public event System.Action<TravelResult> Travelled;

        /// <summary>Raised when an NPC authorises a role and a screen should open.</summary>
        public event System.Action<NpcInteractionResult> InteractionAuthorised;

        /// <summary>Points the UI at a character's location and the world content.</summary>
        public void Bind(CharacterLocationState location, ItemContainerState inventory,
            IDefinitionRegistry<MapDefinition> maps,
            IDefinitionRegistry<SpawnPointDefinition> spawnPoints,
            IDefinitionRegistry<PortalDefinition> portals = null,
            IDefinitionRegistry<NPCDefinition> npcs = null,
            IDefinitionRegistry<ShopDefinition> shops = null,
            IDefinitionRegistry<QuestDefinition> quests = null,
            IDefinitionRegistry<ItemDefinition> items = null,
            int characterLevel = 1)
        {
            _location = location;
            _inventory = inventory;
            _maps = maps;
            _spawnPoints = spawnPoints;
            _portalDefinitions = portals;
            _npcDefinitions = npcs;
            _shops = shops;
            _quests = quests;
            _items = items;
            _characterLevel = characterLevel;

            HookPanels();

            if (sceneLoader != null) sceneLoader.Bind(maps, spawnPoints);

            _bound = true;
            Refresh();
        }

        private void HookPanels()
        {
            if (mapNameView != null) mapNameView.Text = Text;

            if (portalView != null)
            {
                portalView.Text = Text;
                portalView.Requested -= OnPortalRequested;
                portalView.Requested += OnPortalRequested;
            }

            if (npcView == null) return;

            npcView.Text = Text;
            npcView.RolePicked -= OnRolePicked;
            npcView.RolePicked += OnRolePicked;
        }

        /// <summary>The registries the adapter reads through.</summary>
        public WorldMapAdapter.Context ViewContext =>
            new WorldMapAdapter.Context(_maps, _spawnPoints, _portalDefinitions, _npcDefinitions);

        private TravelService.Context TravelContext =>
            new TravelService.Context(_maps, _spawnPoints, _portalDefinitions, _inventory,
                _items, _characterLevel);

        private NpcInteractionService.Context NpcContext =>
            new NpcInteractionService.Context(_npcDefinitions, _spawnPoints, _shops, _quests);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current gameplay state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            if (mapNameView != null)
            {
                mapNameView.Show(WorldMapAdapter.BuildMap(
                    _location == null ? DefinitionId.None : _location.CurrentMap, ViewContext));
            }

            WorldMapAdapter.BuildPortals(_location, ViewContext, _portals);
            WorldMapAdapter.BuildNpcs(_location, ViewContext, _npcs);

            if (portalView != null) portalView.Show(NearestOfferedPortal());
            if (npcView != null) npcView.Show(NearestOfferedNpc());

            if (_location != null) _lastLocationRevision = _location.Revision;
        }

        /// <summary>
        /// Redraws only if the player's location actually changed.
        /// </summary>
        /// <remarks>A revision comparison rather than a per-frame rebuild. Position alone
        /// does not advance the revision, so a caller that wants range prompts to follow a
        /// walking player calls <see cref="Refresh"/> at whatever rate it chooses.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound || _location == null) return false;
            if (_location.Revision == _lastLocationRevision) return false;

            Refresh();
            return true;
        }

        /// <summary>The first portal a prompt would offer, or none.</summary>
        private PortalViewData NearestOfferedPortal()
        {
            for (int i = 0; i < _portals.Count; i++)
            {
                if (_portals[i].CanOffer) return _portals[i];
            }

            // Nothing in reach: show a disabled or distant one rather than nothing, so a
            // player can see a gate exists.
            return _portals.Count > 0 ? _portals[0] : PortalViewData.None;
        }

        private NpcViewData NearestOfferedNpc()
        {
            for (int i = 0; i < _npcs.Count; i++)
            {
                if (_npcs[i].CanOffer) return _npcs[i];
            }

            return NpcViewData.None;
        }

        // ---- commands ------------------------------------------------------------------

        /// <summary>
        /// Asks gameplay to walk the player through a portal.
        /// </summary>
        /// <remarks>On acceptance the result is raised for presentation. The scene is not
        /// loaded here: <see cref="PresentAsync"/> is a separate step precisely so a refused
        /// journey can never reach a loader.</remarks>
        public TravelResult SubmitTravel(DefinitionId portalId)
        {
            LastTravelResult = TravelService.TryTraversePortal(_location, portalId,
                TravelContext);

            Refresh();

            if (!LastTravelResult.IsAccepted) return LastTravelResult;

            var handler = Travelled;
            if (handler != null) handler(LastTravelResult);

            return LastTravelResult;
        }

        /// <summary>
        /// Asks gameplay to send the player to an authored warp destination.
        /// </summary>
        /// <remarks>What a used warp scroll's resolved destination goes through. The town
        /// rule is enforced again by the service, so this cannot be used to reach a field.</remarks>
        public TravelResult SubmitWarp(DefinitionId destinationMap, DefinitionId destinationSpawn)
        {
            LastTravelResult = TravelService.TryTravelToSpawn(_location, destinationMap,
                destinationSpawn, TravelContext, requireTown: true);

            Refresh();

            if (!LastTravelResult.IsAccepted) return LastTravelResult;

            var handler = Travelled;
            if (handler != null) handler(LastTravelResult);

            return LastTravelResult;
        }

        /// <summary>Asks an NPC for a role.</summary>
        public NpcInteractionResult SubmitInteract(DefinitionId npcId, NpcRole role)
        {
            LastInteractionResult = NpcInteractionService.TryInteract(_location, npcId, role,
                NpcContext);

            if (!LastInteractionResult.IsAccepted) return LastInteractionResult;

            var handler = InteractionAuthorised;
            if (handler != null) handler(LastInteractionResult);

            return LastInteractionResult;
        }

        /// <summary>
        /// Brings up the scene an accepted journey landed in.
        /// </summary>
        /// <remarks>Refuses a rejected result outright, so presentation can never run for a
        /// journey gameplay did not allow.</remarks>
        public System.Collections.IEnumerator PresentAsync(TravelResult travel)
        {
            if (sceneLoader == null || !travel.IsAccepted) yield break;

            yield return sceneLoader.LoadAsync(travel);
        }

        /// <summary>Adapts the panel's void-returning event to the submit method.</summary>
        private void OnPortalRequested(DefinitionId portalId)
        {
            SubmitTravel(portalId);
        }

        private void OnRolePicked(DefinitionId npcId, NpcRole role)
        {
            SubmitInteract(npcId, role);
        }
    }
}
