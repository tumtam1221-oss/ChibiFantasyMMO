using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why a fusion was refused.</summary>
    public enum FusionRejection
    {
        /// <summary>Not a rejection.</summary>
        None = 0,

        /// <summary>No inventory or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such recipe could be resolved.</summary>
        InvalidRecipe = 2,

        /// <summary>The recipe consumes nothing, so it would create something from nothing.</summary>
        NoInputs = 3,

        /// <summary>An input names no item, or the item could not be resolved.</summary>
        MissingInput = 4,

        /// <summary>There is not enough of an input.</summary>
        InsufficientQuantity = 5,

        /// <summary>An input is a stone that content marked as not fusable.</summary>
        InputNotFusable = 6,

        /// <summary>The currency cost cannot be paid.</summary>
        InsufficientCost = 7,

        /// <summary>The recipe's result names no item, or it could not be resolved.</summary>
        InvalidOutput = 8,

        /// <summary>There is nowhere to put what the fusion would produce.</summary>
        InsufficientCapacity = 9
    }

    /// <summary>What a fusion attempt did.</summary>
    public enum FusionOutcome
    {
        Rejected = 0,

        /// <summary>The result was produced.</summary>
        Fused = 1,

        /// <summary>The roll failed and the consolation result was produced.</summary>
        FailedWithConsolation = 2,

        /// <summary>The roll failed and nothing was produced.</summary>
        FailedEmpty = 3
    }

    /// <summary>What fusing produced.</summary>
    public readonly struct FusionResult
    {
        private FusionResult(bool accepted, FusionRejection reason, FusionOutcome outcome,
            DefinitionId recipe, DefinitionId produced, int producedQuantity, int currencySpent,
            int inputsConsumed)
        {
            IsAccepted = accepted;
            Reason = reason;
            Outcome = outcome;
            Recipe = recipe;
            Produced = produced;
            ProducedQuantity = producedQuantity;
            CurrencySpent = currencySpent;
            InputsConsumed = inputsConsumed;
        }

        /// <summary>Whether the attempt ran. Not whether it produced the good result.</summary>
        public bool IsAccepted { get; }

        public FusionRejection Reason { get; }

        public FusionOutcome Outcome { get; }

        public DefinitionId Recipe { get; }

        /// <summary>What came out. Invalid when nothing did.</summary>
        public DefinitionId Produced { get; }

        public int ProducedQuantity { get; }

        public int CurrencySpent { get; }

        /// <summary>Total item count consumed across every input.</summary>
        public int InputsConsumed { get; }

        public bool WasFused => Outcome == FusionOutcome.Fused;

        public static FusionResult Accepted(FusionOutcome outcome, DefinitionId recipe,
            DefinitionId produced, int producedQuantity, int currency, int inputs)
        {
            return new FusionResult(true, FusionRejection.None, outcome, recipe, produced,
                producedQuantity, currency, inputs);
        }

        public static FusionResult Rejected(FusionRejection reason, DefinitionId recipe = default)
        {
            return new FusionResult(false, reason, FusionOutcome.Rejected, recipe, default,
                0, 0, 0);
        }

        public override string ToString()
        {
            if (!IsAccepted) return "rejected: " + Reason;

            return Outcome + " -> " + Produced + " x" + ProducedQuantity
                + " (consumed " + InputsConsumed + ", currency " + CurrencySpent + ")";
        }
    }

    /// <summary>
    /// Fuses items into other items, by recipe.
    /// </summary>
    /// <remarks>
    /// <b>Nothing about grades is in code.</b> "Three of these make one of those" is
    /// entirely the recipe. There is no tier arithmetic and no rank maths here: a recipe
    /// combining three different stones into something unrelated runs through exactly the
    /// same path as three identical ones.
    ///
    /// <b>It uses the inventory that already exists.</b> Counting, removing and adding all
    /// go through <see cref="ItemContainerState"/>, including its stacking and its
    /// remainder reporting. No fusion-specific container, no second stacking rule.
    ///
    /// <b>Validate fully, then mutate -- including the destination.</b> Room for the result
    /// is checked <em>before</em> the inputs are consumed. Checking afterwards is the
    /// classic way to destroy a player's materials and hand back nothing, and there is no
    /// rollback to save it.
    /// </remarks>
    public static class StoneFusionService
    {
        /// <summary>Everything a fusion needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<StoneFusionDefinition> recipes,
                IRandomResultSource results = null,
                OwnerId owner = default)
            {
                Items = items;
                Recipes = recipes;
                Results = results ?? AlwaysSucceeds.Instance;
                Owner = owner;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<StoneFusionDefinition> Recipes { get; }

            public IRandomResultSource Results { get; }

            /// <summary>Stamped on what the fusion produces, so output ownership is explicit.</summary>
            public OwnerId Owner { get; }
        }

        /// <summary>
        /// Runs a recipe against a container.
        /// </summary>
        /// <param name="inventory">Where the inputs come from and the result goes.</param>
        /// <param name="recipeId">Which recipe.</param>
        /// <param name="context">Registries, the roll source and the owner to stamp.</param>
        public static FusionResult TryFuse(ItemContainerState inventory, DefinitionId recipeId,
            in Context context)
        {
            if (inventory == null || context.Items == null || context.Recipes == null)
                return FusionResult.Rejected(FusionRejection.MissingContext, recipeId);

            StoneFusionDefinition recipe;
            if (!recipeId.IsValid || !context.Recipes.TryGet(recipeId, out recipe) || recipe == null)
                return FusionResult.Rejected(FusionRejection.InvalidRecipe, recipeId);

            FusionIngredient[] inputs = recipe.Inputs;
            if (inputs.Length == 0)
                return FusionResult.Rejected(FusionRejection.NoInputs, recipeId);

            // ---- inputs ----------------------------------------------------------------

            int totalInputs = 0;

            for (int i = 0; i < inputs.Length; i++)
            {
                FusionIngredient input = inputs[i];

                if (!input.IsValid)
                    return FusionResult.Rejected(FusionRejection.MissingInput, recipeId);

                ItemDefinition item;
                if (!context.Items.TryGet(input.Item, out item) || item == null)
                    return FusionResult.Rejected(FusionRejection.MissingInput, recipeId);

                // Authored, not inferred: an item is fusable because content said so.
                if (item.IsStatusStone && !item.StoneConfig.Fusable)
                    return FusionResult.Rejected(FusionRejection.InputNotFusable, recipeId);

                int needed = input.Quantity + SameItemElsewhere(inputs, i);

                if (inventory.CountOf(input.Item) < needed)
                    return FusionResult.Rejected(FusionRejection.InsufficientQuantity, recipeId);

                totalInputs += input.Quantity;
            }

            // ---- cost ------------------------------------------------------------------

            int currencyNeeded = recipe.CurrencyCost > 0 ? recipe.CurrencyCost : 0;
            DefinitionId currencyItem = recipe.CurrencyItem;

            if (currencyNeeded > 0)
            {
                if (!currencyItem.IsValid)
                    return FusionResult.Rejected(FusionRejection.InsufficientCost, recipeId);

                // The currency may also be one of the inputs; both claims must be met.
                int alsoNeeded = QuantityOf(inputs, currencyItem);

                if (inventory.CountOf(currencyItem) < currencyNeeded + alsoNeeded)
                    return FusionResult.Rejected(FusionRejection.InsufficientCost, recipeId);
            }

            // ---- output ----------------------------------------------------------------

            bool succeeded = context.Results.Succeeds(recipe.SuccessChance);

            DefinitionId outputId = succeeded ? recipe.Result : recipe.FailureResult;
            int outputQuantity = succeeded ? recipe.ResultQuantity : recipe.FailureResultQuantity;

            if (succeeded && !outputId.IsValid)
                return FusionResult.Rejected(FusionRejection.InvalidOutput, recipeId);

            ItemDefinition output = null;

            if (outputId.IsValid && (!context.Items.TryGet(outputId, out output) || output == null))
            {
                // A success that cannot be delivered is refused. A failure whose consolation
                // is missing simply produces nothing, which is what an unauthored one means.
                if (succeeded) return FusionResult.Rejected(FusionRejection.InvalidOutput, recipeId);

                outputId = DefinitionId.None;
                output = null;
            }

            bool consumesInputs = succeeded || recipe.ConsumeInputsOnFailure;

            // Room is checked before anything is spent, and against the container as it will
            // be: the inputs coming out is what frees the slots the result may need.
            if (output != null && !HasRoomAfterConsumption(inventory, inputs, consumesInputs,
                    currencyItem, currencyNeeded, outputId, outputQuantity, context))
            {
                return FusionResult.Rejected(FusionRejection.InsufficientCapacity, recipeId);
            }

            // ---- mutation boundary -----------------------------------------------------

            if (consumesInputs)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inventory.RemoveByDefinition(inputs[i].Item, inputs[i].Quantity);
                }
            }

            if (currencyNeeded > 0) inventory.RemoveByDefinition(currencyItem, currencyNeeded);

            int produced = 0;

            if (output != null)
            {
                var instance = new ItemInstance(InstanceId.New(), outputId, context.Owner,
                    outputQuantity);

                ItemContainerResult added = inventory.Add(instance, context.Items);
                produced = added.IsAccepted ? outputQuantity - added.Remainder : 0;
            }

            if (succeeded)
            {
                return FusionResult.Accepted(FusionOutcome.Fused, recipeId, outputId, produced,
                    currencyNeeded, consumesInputs ? totalInputs : 0);
            }

            return FusionResult.Accepted(
                output != null ? FusionOutcome.FailedWithConsolation : FusionOutcome.FailedEmpty,
                recipeId, outputId, produced, currencyNeeded,
                consumesInputs ? totalInputs : 0);
        }

        /// <summary>
        /// Whether the result will fit once the inputs have left.
        /// </summary>
        /// <remarks>
        /// Simulated rather than attempted, because attempting is exactly the mistake: the
        /// only way to find out by attempting is to have already spent the inputs.
        ///
        /// The simulation counts the slots the consumption frees, then asks the container
        /// how much of the output its existing stacks can absorb. Both figures come from
        /// the container's own rules, so no stacking logic is restated here.
        /// </remarks>
        private static bool HasRoomAfterConsumption(ItemContainerState inventory,
            FusionIngredient[] inputs, bool consumesInputs, DefinitionId currencyItem,
            int currencyNeeded, DefinitionId outputId, int outputQuantity, in Context context)
        {
            int roomInStacks = inventory.RoomFor(outputId, context.Items);
            if (roomInStacks >= outputQuantity) return true;

            int freedSlots = 0;

            if (consumesInputs)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    freedSlots += SlotsFreedBy(inventory, inputs[i].Item, inputs[i].Quantity);
                }
            }

            if (currencyNeeded > 0)
            {
                freedSlots += SlotsFreedBy(inventory, currencyItem, currencyNeeded);
            }

            // A freed slot can hold a whole stack of the output.
            ItemDefinition output;
            context.Items.TryGet(outputId, out output);

            int perSlot = output != null && output.Stackable && output.MaxStackSize > 0
                ? output.MaxStackSize
                : 1;

            return roomInStacks + (long)freedSlots * perSlot >= outputQuantity;
        }

        /// <summary>
        /// How many slots removing a quantity of an item would empty.
        /// </summary>
        /// <remarks>Counts from the back, because <c>RemoveByDefinition</c> is the operation
        /// being predicted and a slot only frees when it is emptied completely.</remarks>
        private static int SlotsFreedBy(ItemContainerState inventory, DefinitionId item,
            int quantity)
        {
            if (!item.IsValid || quantity <= 0) return 0;

            int remaining = quantity;
            int freed = 0;

            for (int i = 0; i < inventory.Capacity && remaining > 0; i++)
            {
                ItemSlot slot = inventory.GetSlot(i);
                if (slot.IsEmpty || slot.DefinitionId != item) continue;

                if (slot.Quantity <= remaining)
                {
                    remaining -= slot.Quantity;
                    freed++;
                    continue;
                }

                remaining = 0;
            }

            return freed;
        }

        /// <summary>How much of an item the other inputs also claim.</summary>
        /// <remarks>A recipe may list the same item twice; both rows are real demands and
        /// checking them separately would let half the requirement pass.</remarks>
        private static int SameItemElsewhere(FusionIngredient[] inputs, int index)
        {
            int total = 0;

            for (int i = 0; i < inputs.Length; i++)
            {
                if (i == index) continue;
                if (inputs[i].Item != inputs[index].Item) continue;

                total += inputs[i].Quantity;
            }

            return total;
        }

        private static int QuantityOf(FusionIngredient[] inputs, DefinitionId item)
        {
            int total = 0;

            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].Item == item) total += inputs[i].Quantity;
            }

            return total;
        }
    }
}
