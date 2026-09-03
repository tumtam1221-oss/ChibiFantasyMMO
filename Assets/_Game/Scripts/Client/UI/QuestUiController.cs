using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the quest and loot panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for quests and loot.</b> Every change these panels can cause
    /// goes through a submit method here, and each calls an existing service --
    /// <see cref="QuestService"/> or <see cref="LootPickupService"/>. No panel holds a
    /// quest state or a loot pile, so there is nowhere else a mutation could originate.
    /// The flow matches the inventory UI exactly: state -> adapter -> view data -> panel ->
    /// input -> here -> service -> state.
    ///
    /// <b>Nothing is polled.</b> Refresh happens after a command, and after gameplay tells
    /// the client something happened -- a kill, a pickup. Quest progress is reported into
    /// <see cref="QuestService"/> by whoever observed the event, never discovered by
    /// scanning.
    ///
    /// Separate from <see cref="InventoryUiController"/> rather than bolted onto it: they
    /// share no state, and one controller owning bags, equipment, quests and world loot
    /// would be the monolith the UI phases were careful to avoid.
    /// </remarks>
    public sealed class QuestUiController : MonoBehaviour
    {
        [SerializeField] private QuestTrackerView trackerView;
        [SerializeField] private QuestDetailView detailView;
        [SerializeField] private LootPickupView lootView;
        [SerializeField] private MonsterHealthBar targetHealthBar;

        private readonly List<QuestViewData> _questLog = new List<QuestViewData>();
        private readonly List<LootEntryViewData> _lootEntries = new List<LootEntryViewData>();

        private CharacterQuestState _quests;
        private ItemContainerState _inventory;
        private LootObjectState _loot;
        private MonsterRuntimeState _target;

        private IDefinitionRegistry<ItemDefinition> _items;
        private IDefinitionRegistry<MonsterDefinition> _monsters;
        private IDefinitionRegistry<QuestDefinition> _questDefinitions;

        private OwnerId _owner;
        private CharacterId _character;
        private int _characterLevel = 1;
        private bool _bound;

        private Revision _lastQuestRevision;
        private Revision _lastLootRevision;
        private Revision _lastTargetRevision;

        /// <summary>Which quest the detail panel is showing.</summary>
        public DefinitionId SelectedQuest { get; private set; }

        /// <summary>Icons for the loot panel. Optional.</summary>
        public IconResolver Icons { get; set; }

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>The answer to the last quest operation submitted.</summary>
        public QuestResult LastQuestResult { get; private set; }

        /// <summary>The answer to the last pickup submitted.</summary>
        public LootPickupResult LastPickupResult { get; private set; }

        /// <summary>Points the UI at a character's quests and bag.</summary>
        public void Bind(CharacterQuestState quests, ItemContainerState inventory,
            IDefinitionRegistry<ItemDefinition> items,
            IDefinitionRegistry<MonsterDefinition> monsters,
            IDefinitionRegistry<QuestDefinition> questDefinitions,
            CharacterId character, OwnerId owner, int characterLevel = 1)
        {
            _quests = quests;
            _inventory = inventory;
            _items = items;
            _monsters = monsters;
            _questDefinitions = questDefinitions;
            _character = character;
            _owner = owner;
            _characterLevel = characterLevel;

            HookPanels();

            _bound = true;
            Refresh();
        }

        private void HookPanels()
        {
            if (trackerView != null)
            {
                trackerView.Text = Text;
                trackerView.QuestSelected -= SelectQuest;
                trackerView.QuestSelected += SelectQuest;
            }

            if (detailView != null)
            {
                detailView.Text = Text;
                detailView.Accepted -= OnAcceptRequested;
                detailView.Accepted += OnAcceptRequested;
                detailView.TurnedIn -= OnTurnInRequested;
                detailView.TurnedIn += OnTurnInRequested;
            }

            if (lootView != null)
            {
                lootView.Text = Text;
                lootView.EntryClicked -= OnLootClicked;
                lootView.EntryClicked += OnLootClicked;
            }

            if (targetHealthBar != null) targetHealthBar.Text = Text;
        }

        /// <summary>The registries the adapter reads through.</summary>
        public WorldViewAdapter.Context ViewContext =>
            new WorldViewAdapter.Context(_items, _monsters, _questDefinitions);

        private QuestService.Context QuestContext =>
            new QuestService.Context(_questDefinitions, _items, _characterLevel, _owner);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current gameplay state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            WorldViewAdapter.BuildQuestLog(_quests, ViewContext, _questLog);

            if (trackerView != null) trackerView.Show(_questLog);

            if (detailView != null)
            {
                detailView.Show(SelectedQuest.IsValid
                    ? WorldViewAdapter.BuildQuest(_quests, SelectedQuest, ViewContext)
                    : QuestViewData.None);
            }

            RefreshLoot();
            RefreshTarget();
            RecordRevisions();
        }

        /// <summary>
        /// Redraws only if something actually changed.
        /// </summary>
        /// <remarks>Comparing three revision integers is not the same as rebuilding the
        /// panels: the walk, the lookups and the re-bind only happen on a real change. This
        /// is what catches a quest advanced by a kill elsewhere without the UI subscribing
        /// to gameplay it cannot see.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound) return false;
            if (!HasChanged()) return false;

            Refresh();
            return true;
        }

        private bool HasChanged()
        {
            if (_quests != null && _quests.Revision != _lastQuestRevision) return true;
            if (_loot != null && _loot.Revision != _lastLootRevision) return true;
            if (_target != null && _target.Revision != _lastTargetRevision) return true;
            return false;
        }

        private void RecordRevisions()
        {
            if (_quests != null) _lastQuestRevision = _quests.Revision;
            if (_loot != null) _lastLootRevision = _loot.Revision;
            if (_target != null) _lastTargetRevision = _target.Revision;
        }

        private void RefreshLoot()
        {
            if (lootView == null) return;

            if (_loot == null)
            {
                lootView.Hide();
                return;
            }

            WorldViewAdapter.BuildLoot(_loot, ViewContext, _lootEntries);
            lootView.Show(_lootEntries, "Loot", Icons);
        }

        private void RefreshTarget()
        {
            if (targetHealthBar == null) return;

            targetHealthBar.Show(WorldViewAdapter.BuildMonsterHealth(_target));
        }

        // ---- selection -----------------------------------------------------------------

        /// <summary>Shows one quest in the detail panel.</summary>
        public void SelectQuest(DefinitionId questId)
        {
            SelectedQuest = questId;

            if (detailView == null) return;

            detailView.Show(questId.IsValid
                ? WorldViewAdapter.BuildQuest(_quests, questId, ViewContext)
                : QuestViewData.None);
        }

        /// <summary>Points the health bar at a monster, or at nothing.</summary>
        public void SetTarget(MonsterRuntimeState monster)
        {
            _target = monster;
            RefreshTarget();
            RecordRevisions();
        }

        /// <summary>Shows a loot pile, or closes the panel.</summary>
        public void SetLoot(LootObjectState loot)
        {
            _loot = loot;
            RefreshLoot();
            RecordRevisions();
        }

        // ---- commands ------------------------------------------------------------------

        /// <summary>Asks gameplay to take a quest.</summary>
        public QuestResult SubmitAccept(DefinitionId questId)
        {
            LastQuestResult = QuestService.TryAccept(_quests, questId, QuestContext);

            if (LastQuestResult.IsAccepted) SelectedQuest = questId;

            Refresh();
            Report(LastQuestResult);
            return LastQuestResult;
        }

        /// <summary>Asks gameplay to hand a quest in.</summary>
        public QuestResult SubmitTurnIn(DefinitionId questId)
        {
            LastQuestResult = QuestService.TryTurnIn(_quests, questId, _inventory, QuestContext);

            Refresh();
            Report(LastQuestResult);
            return LastQuestResult;
        }

        /// <summary>Asks gameplay to abandon a quest.</summary>
        public QuestResult SubmitAbandon(DefinitionId questId)
        {
            LastQuestResult = QuestService.TryAbandon(_quests, questId, QuestContext);

            Refresh();
            Report(LastQuestResult);
            return LastQuestResult;
        }

        /// <summary>Asks gameplay to take one entry from the open pile.</summary>
        public LootPickupResult SubmitPickUp(int index)
        {
            var context = new LootPickupService.Context(_items, _owner, _character);

            LastPickupResult = LootPickupService.TryPickUp(_loot, index, _inventory, context);

            // A pickup can satisfy a collect objective. Reporting it is how quest progress
            // happens: nothing scans the bag to find out.
            if (LastPickupResult.IsAccepted && LastPickupResult.QuantityTaken > 0)
            {
                QuestService.ReportProgress(_quests, QuestObjectiveType.CollectItem,
                    LastPickupResult.Item, LastPickupResult.QuantityTaken, QuestContext);
            }

            Refresh();
            return LastPickupResult;
        }

        /// <summary>
        /// Tells the quest system that a monster was defeated.
        /// </summary>
        /// <remarks>Called by whatever observed the kill, with the result the defeat service
        /// already produced. The UI does not decide that a monster died and does not go
        /// looking for one that has.</remarks>
        public int ReportKill(DefinitionId monsterDefinitionId, int amount = 1)
        {
            int advanced = QuestService.ReportProgress(_quests, QuestObjectiveType.KillMonster,
                monsterDefinitionId, amount, QuestContext);

            if (advanced > 0) Refresh();
            return advanced;
        }

        /// <summary>Tells the quest system that an NPC was spoken to.</summary>
        public int ReportTalk(DefinitionId npcId)
        {
            int advanced = QuestService.ReportProgress(_quests, QuestObjectiveType.TalkToNpc,
                npcId, 1, QuestContext);

            if (advanced > 0) Refresh();
            return advanced;
        }

        private void OnAcceptRequested()
        {
            if (SelectedQuest.IsValid) SubmitAccept(SelectedQuest);
        }

        private void OnTurnInRequested()
        {
            if (SelectedQuest.IsValid) SubmitTurnIn(SelectedQuest);
        }

        private void OnLootClicked(int index)
        {
            SubmitPickUp(index);
        }

        /// <summary>Puts the service's own reason on screen, rather than inventing one.</summary>
        private void Report(QuestResult result)
        {
            if (detailView == null) return;

            detailView.ShowResult(result.IsAccepted
                ? result.Status.ToString()
                : result.Reason.ToString());
        }
    }
}
