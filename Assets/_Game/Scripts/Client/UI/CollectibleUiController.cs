using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Wires the Devil Fruit, card and pet panels to gameplay.
    /// </summary>
    /// <remarks>
    /// <b>The command boundary for collectibles.</b> Every change these panels can cause goes
    /// through a submit method here, and each calls an existing service --
    /// <see cref="ItemUseService"/>, <see cref="CardSocketService"/> or
    /// <see cref="PetService"/>. No view holds a container, a pet or a fruit state, so there
    /// is nowhere else an activation could start. The same shape
    /// <c>InventoryUiController</c>, <c>QuestUiController</c> and <c>WorldUiController</c>
    /// already keep.
    ///
    /// <b>It decides nothing.</b> Not one rule about eating a fruit, socketing a card or
    /// evolving a pet appears below; each submit forwards to the service that owns the rule
    /// and reports what came back. That is why a panel cannot authorise anything by being
    /// wrong about state.
    ///
    /// <b>Nothing is polled.</b> Panels rebuild when a revision moves, not every frame.
    /// </remarks>
    public sealed class CollectibleUiController : MonoBehaviour
    {
        private readonly List<DevilFruitViewData> _fruits = new List<DevilFruitViewData>();
        private readonly List<CardViewData> _cards = new List<CardViewData>();
        private readonly List<PetViewData> _pets = new List<PetViewData>();
        private readonly List<PetInstance> _ownedPets = new List<PetInstance>();

        private ItemContainerState _inventory;
        private CharacterResourceState _resources;
        private ResourceLimits _limits;
        private CharacterDevilFruitState _fruitState;
        private PetCompanionState _companion;
        private StatusEffectRuntimeState _status;

        private IDefinitionRegistry<ItemDefinition> _items;
        private IDefinitionRegistry<DevilFruitDefinition> _fruitDefinitions;
        private IDefinitionRegistry<CardDefinition> _cardDefinitions;
        private IDefinitionRegistry<PetDefinition> _petDefinitions;
        private IDefinitionRegistry<StatusEffectDefinition> _statusEffects;
        private IDefinitionRegistry<SkillDefinition> _skills;

        private OwnerId _owner;
        private bool _bound;
        private Revision _lastFruitRevision;
        private Revision _lastCompanionRevision;

        /// <summary>Where keys are translated. Optional.</summary>
        public ILocalizedTextSource Text { get; set; }

        /// <summary>The answer to the last item use submitted.</summary>
        public ItemUseResult LastUseResult { get; private set; }

        /// <summary>The answer to the last card operation submitted.</summary>
        public CardSocketResult LastCardResult { get; private set; }

        /// <summary>The answer to the last pet operation submitted.</summary>
        public PetResult LastPetResult { get; private set; }

        public IReadOnlyList<DevilFruitViewData> Fruits => _fruits;

        public IReadOnlyList<CardViewData> Cards => _cards;

        public IReadOnlyList<PetViewData> Pets => _pets;

        /// <summary>Every pet this character owns.</summary>
        public IReadOnlyList<PetInstance> OwnedPets => _ownedPets;

        /// <summary>
        /// Raised for anything presentation should react to.
        /// </summary>
        /// <remarks>Carries a <see cref="CollectiblePresentationEvent"/>, which holds ids and
        /// asset references and no gameplay object, so a presenter cannot reach through it.</remarks>
        public event System.Action<CollectiblePresentationEvent> Presented;

        /// <summary>Points the UI at a character's state and the content registries.</summary>
        public void Bind(ItemContainerState inventory, CharacterResourceState resources,
            ResourceLimits limits,
            IDefinitionRegistry<ItemDefinition> items,
            CharacterDevilFruitState fruitState = null,
            PetCompanionState companion = null,
            StatusEffectRuntimeState status = null,
            IDefinitionRegistry<DevilFruitDefinition> fruits = null,
            IDefinitionRegistry<CardDefinition> cards = null,
            IDefinitionRegistry<PetDefinition> pets = null,
            IDefinitionRegistry<StatusEffectDefinition> statusEffects = null,
            IDefinitionRegistry<SkillDefinition> skills = null,
            OwnerId owner = default)
        {
            _inventory = inventory;
            _resources = resources;
            _limits = limits;
            _items = items;
            _fruitState = fruitState;
            _companion = companion;
            _status = status;
            _fruitDefinitions = fruits;
            _cardDefinitions = cards;
            _petDefinitions = pets;
            _statusEffects = statusEffects;
            _skills = skills;
            _owner = owner;

            _bound = true;
            Refresh();
        }

        /// <summary>The registries the adapter reads through.</summary>
        public CollectibleAdapter.Context ViewContext =>
            new CollectibleAdapter.Context(_items, _fruitDefinitions, _cardDefinitions,
                _petDefinitions);

        private ItemUseService.Context UseContext =>
            new ItemUseService.Context(_items, _resources, _limits, _statusEffects, null, null,
                _owner, null, _fruitDefinitions, _fruitState, _petDefinitions, _skills, _status);

        private CardSocketService.Context CardContext =>
            new CardSocketService.Context(_items, _cardDefinitions, null, _owner);

        private PetService.Context PetContext =>
            new PetService.Context(_petDefinitions, _items, _statusEffects, _status, _owner);

        // ---- refresh -------------------------------------------------------------------

        /// <summary>Redraws every panel from current gameplay state.</summary>
        public void Refresh()
        {
            if (!_bound) return;

            CollectibleAdapter.BuildFruits(_fruitState, ViewContext, _fruits);
            CollectibleAdapter.BuildPets(_ownedPets, _companion, _inventory, ViewContext, _pets);

            if (_fruitState != null) _lastFruitRevision = _fruitState.Revision;
            if (_companion != null) _lastCompanionRevision = _companion.Revision;
        }

        /// <summary>
        /// Redraws only if something actually changed.
        /// </summary>
        /// <remarks>A revision comparison rather than a per-frame rebuild, matching every
        /// other controller in this assembly.</remarks>
        public bool RefreshIfChanged()
        {
            if (!_bound) return false;

            bool fruitMoved = _fruitState != null && _fruitState.Revision != _lastFruitRevision;
            bool companionMoved = _companion != null
                && _companion.Revision != _lastCompanionRevision;

            if (!fruitMoved && !companionMoved) return false;

            Refresh();
            return true;
        }

        /// <summary>Rebuilds the card list for one piece of equipment.</summary>
        public void ShowCardsFor(EquipmentInstance equipment)
        {
            CollectibleAdapter.BuildSocketedCards(equipment, ViewContext, _cards);
        }

        // ---- commands ------------------------------------------------------------------

        /// <summary>
        /// Uses whatever is in a slot.
        /// </summary>
        /// <remarks>
        /// Eating a fruit and taming a pet are both this: one call into the one item-use
        /// pipeline. There is no fruit button path and no pet button path, so a fruit obeys
        /// every ownership and quantity rule an ordinary potion does.
        /// </remarks>
        public ItemUseResult SubmitUse(int slotIndex)
        {
            LastUseResult = ItemUseService.Use(_inventory, slotIndex, UseContext);

            if (!LastUseResult.IsAccepted) return LastUseResult;

            if (LastUseResult.PetGranted != null) _ownedPets.Add(LastUseResult.PetGranted);

            Refresh();

            if (LastUseResult.DevilFruitActivated.IsValid) RaiseFruitActivated();

            return LastUseResult;
        }

        /// <summary>Sockets a card from the bag into a piece of equipment.</summary>
        public CardSocketResult SubmitInsertCard(int slotIndex, EquipmentInstance equipment,
            int socketIndex = -1)
        {
            LastCardResult = CardSocketService.TryInsert(_inventory, slotIndex, equipment,
                CardContext, socketIndex);

            if (!LastCardResult.IsAccepted) return LastCardResult;

            ShowCardsFor(equipment);
            Raise(CollectiblePresentationEvent.CardInserted(LastCardResult,
                equipment.InstanceId));

            return LastCardResult;
        }

        /// <summary>Takes a card back out and returns it to the bag.</summary>
        public CardSocketResult SubmitRemoveCard(EquipmentInstance equipment, int socketIndex)
        {
            LastCardResult = CardSocketService.TryRemove(equipment, socketIndex, _inventory,
                CardContext);

            if (!LastCardResult.IsAccepted) return LastCardResult;

            ShowCardsFor(equipment);
            Raise(CollectiblePresentationEvent.CardRemoved(LastCardResult, equipment.InstanceId));

            return LastCardResult;
        }

        /// <summary>Awards a pet experience.</summary>
        public PetResult SubmitPetExperience(PetInstance pet, int amount)
        {
            LastPetResult = PetService.TryGrantExperience(pet, amount, PetContext);

            if (!LastPetResult.IsAccepted) return LastPetResult;

            Refresh();

            if (LastPetResult.LevelsGained > 0)
            {
                Raise(CollectiblePresentationEvent.PetLevelUp(LastPetResult));
            }

            return LastPetResult;
        }

        /// <summary>
        /// Evolves a pet.
        /// </summary>
        /// <remarks>When the evolved form is an aura, the companion record is updated so a
        /// pet already out changes how it appears without being dismissed and re-summoned.</remarks>
        public PetResult SubmitEvolvePet(PetInstance pet)
        {
            LastPetResult = PetService.TryEvolve(pet, _inventory, PetContext);

            if (!LastPetResult.IsAccepted) return LastPetResult;

            if (_companion != null && _companion.IsSummoned && _companion.Summoned == pet)
            {
                _companion.SetAuraForm(LastPetResult.IsAuraForm);
            }

            Refresh();

            Raise(CollectiblePresentationEvent.PetEvolved(LastPetResult, EvolvedVisual(pet),
                EvolvedSound(pet)));

            if (LastPetResult.IsAuraForm)
            {
                Raise(CollectiblePresentationEvent.PetAuraActivated(LastPetResult,
                    EvolvedVisual(pet)));
            }

            return LastPetResult;
        }

        /// <summary>Brings a pet out.</summary>
        public PetResult SubmitSummonPet(PetInstance pet)
        {
            LastPetResult = PetService.TrySummon(_companion, pet, PetContext);

            if (!LastPetResult.IsAccepted) return LastPetResult;

            Refresh();

            Raise(CollectiblePresentationEvent.PetSummoned(LastPetResult, EvolvedVisual(pet),
                EvolvedSound(pet)));

            return LastPetResult;
        }

        /// <summary>Puts the summoned pet away.</summary>
        public bool SubmitDismissPet()
        {
            if (_companion == null || !_companion.IsSummoned) return false;

            PetInstance pet = _companion.Summoned;
            DefinitionId definition = pet.DefinitionId;
            InstanceId instance = pet.InstanceId;

            if (!PetService.Dismiss(_companion, PetContext)) return false;

            Refresh();
            Raise(CollectiblePresentationEvent.PetDismissed(definition, instance));
            return true;
        }

        /// <summary>Changes what a summoned pet is doing.</summary>
        public bool SubmitFollowMode(PetFollowMode mode)
        {
            return _companion != null && _companion.SetMode(mode);
        }

        // ---- events --------------------------------------------------------------------

        private void RaiseFruitActivated()
        {
            DevilFruitDefinition fruit;
            if (_fruitDefinitions == null
                || !_fruitDefinitions.TryGet(LastUseResult.DevilFruitActivated, out fruit)
                || fruit == null)
            {
                return;
            }

            Raise(CollectiblePresentationEvent.DevilFruitActivated(fruit.Id,
                LastUseResult.InstanceId, fruit.VisualEffect, fruit.SoundEffect));

            if (fruit.ActiveAbility.IsValid)
            {
                Raise(CollectiblePresentationEvent.DevilFruitSkillGranted(fruit.Id,
                    fruit.ActiveAbility));
            }
        }

        private AssetRef EvolvedVisual(PetInstance pet)
        {
            PetDefinition definition;
            if (pet == null || _petDefinitions == null
                || !_petDefinitions.TryGet(pet.DefinitionId, out definition) || definition == null)
            {
                return default;
            }

            return definition.Model;
        }

        private AssetRef EvolvedSound(PetInstance pet)
        {
            PetDefinition definition;
            if (pet == null || _petDefinitions == null
                || !_petDefinitions.TryGet(pet.DefinitionId, out definition) || definition == null)
            {
                return default;
            }

            return definition.SoundEffect;
        }

        private void Raise(CollectiblePresentationEvent published)
        {
            if (!published.IsValid) return;

            var handler = Presented;
            if (handler != null) handler(published);
        }
    }
}
