using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// One thing a kill produced.
    /// </summary>
    /// <remarks>
    /// <b>A result, not an item.</b> Nothing has been created and no container has been
    /// touched: this says what <em>would</em> be handed over. Keeping the roll separate
    /// from the handover is what leaves room for world loot, personal loot, party rolls
    /// and boss distribution without any of them reaching back into the drop calculation.
    /// </remarks>
    public readonly struct LootResult
    {
        public LootResult(InstanceId source, DefinitionId item, int quantity,
            DefinitionId rarityOverride = default)
        {
            Source = source;
            Item = item;
            Quantity = quantity;
            RarityOverride = rarityOverride;
        }

        /// <summary>What it came off. Kept so loot can be attributed and expired.</summary>
        public InstanceId Source { get; }

        public DefinitionId Item { get; }

        public int Quantity { get; }

        /// <summary>Tier to stamp on dropped equipment. Invalid leaves the authored one.</summary>
        public DefinitionId RarityOverride { get; }

        public bool IsValid => Item.IsValid && Quantity > 0;

        public override string ToString()
        {
            return Item + " x" + Quantity;
        }
    }

    /// <summary>
    /// Rolls a drop table.
    /// </summary>
    /// <remarks>
    /// <b>It produces results and mutates nothing.</b> No inventory, no world object, no
    /// item instance. A caller decides what to do with what comes out, which is the seam
    /// every later loot mode plugs into.
    ///
    /// <b>Every roll is injected.</b> See <see cref="IRandomResultSource"/> and
    /// <see cref="IRandomRangeSource"/>: whether an entry lands and how many of it are two
    /// separate questions, both asked of the caller. That is what makes a one-in-a-million
    /// drop testable, and it is the seam a server takes over.
    ///
    /// <b>Nothing here knows an item.</b> A copper coin and an ultra-rare card differ only
    /// by the chance a designer typed. No <see cref="DefinitionId"/> is compared to a
    /// literal.
    /// </remarks>
    public static class DropResolver
    {
        /// <summary>Everything a roll needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<ItemDefinition> items,
                IDefinitionRegistry<DropTableDefinition> tables,
                IRandomResultSource results = null,
                IRandomRangeSource ranges = null,
                int killerLevel = 0,
                // `default` rather than the ordinary rank by name: naming a rank value here
                // would be indistinguishable from the special-case branch this file is
                // deliberately free of. Zero is the ordinary rank, which is the safe
                // default -- a caller that does not say what died gets no restricted drops.
                MonsterRank rank = default)
            {
                Items = items;
                Tables = tables;
                Results = results ?? AlwaysSucceeds.Instance;
                Ranges = ranges ?? AlwaysSucceeds.Instance;
                KillerLevel = killerLevel;
                Rank = rank;
            }

            public IDefinitionRegistry<ItemDefinition> Items { get; }

            public IDefinitionRegistry<DropTableDefinition> Tables { get; }

            /// <summary>Decides whether an entry lands.</summary>
            public IRandomResultSource Results { get; }

            /// <summary>Decides how many.</summary>
            public IRandomRangeSource Ranges { get; }

            /// <summary>Level of whoever landed the kill, for level-banded entries.</summary>
            public int KillerLevel { get; }

            /// <summary>
            /// What kind of monster is dropping, for rank-restricted entries.
            /// </summary>
            /// <remarks>Defaults to <c>Normal</c>, which is the safe direction: a caller
            /// that does not say what died gets no restricted drops rather than all of
            /// them.</remarks>
            public MonsterRank Rank { get; }

            public bool IsUsable => Items != null && Tables != null;
        }

        /// <summary>
        /// Rolls the table a monster references and appends what fell.
        /// </summary>
        /// <remarks>Appends rather than clears, so several monsters dying together can be
        /// resolved into one list.</remarks>
        public static int Resolve(MonsterRuntimeState monster, in Context context,
            List<LootResult> into)
        {
            if (into == null || monster == null || !context.IsUsable) return 0;

            // The monster's own authored rank, never the caller's opinion of it. This is
            // what makes a World Boss-only entry impossible to obtain from a rat.
            var ranked = new Context(context.Items, context.Tables, context.Results,
                context.Ranges, context.KillerLevel, monster.Definition.Rank);

            return Resolve(monster.InstanceId, monster.Definition.LootTable, ranked, into);
        }

        /// <summary>Rolls a named table on behalf of a source.</summary>
        public static int Resolve(InstanceId source, DefinitionId tableId, in Context context,
            List<LootResult> into)
        {
            if (into == null || !context.IsUsable || !tableId.IsValid) return 0;

            DropTableDefinition table;
            if (!context.Tables.TryGet(tableId, out table) || table == null) return 0;

            DropEntry[] entries = table.Entries;
            int dropped = 0;

            for (int i = 0; i < entries.Length; i++)
            {
                if (table.MaxEntries > 0 && dropped >= table.MaxEntries) break;

                DropEntry entry = entries[i];

                // A malformed row is skipped rather than dropping nothing-times-nothing.
                // IsValid covers a probability that is not a usable number, so a NaN from a
                // bad import cannot masquerade as a drop that merely never happens.
                if (!entry.IsValid) continue;

                // Turned off by configuration. A row, not an item: nothing here knows what
                // kind of thing was switched off.
                if (!entry.Enabled) continue;

                if (!entry.AppliesTo(context.KillerLevel)) continue;

                // Restricted to something rarer than what died.
                if (!entry.AppliesToRank(context.Rank)) continue;

                // The item must exist before it can be promised. Content removed by a patch
                // must not produce loot nobody can pick up.
                ItemDefinition item;
                if (!context.Items.TryGet(entry.Item, out item) || item == null) continue;

                // Each entry is its own roll. A monster is not "one item".
                if (!entry.IsGuaranteed && !context.Results.Succeeds(entry.Chance)) continue;

                int quantity = entry.MinQuantity == entry.MaxQuantity
                    ? entry.MinQuantity
                    : context.Ranges.Range(entry.MinQuantity, entry.MaxQuantity);

                if (quantity <= 0) continue;

                into.Add(new LootResult(source, entry.Item, quantity, entry.RarityOverride));
                dropped++;
            }

            return dropped;
        }

        /// <summary>Convenience overload that allocates.</summary>
        public static List<LootResult> Resolve(MonsterRuntimeState monster, in Context context)
        {
            var list = new List<LootResult>();
            Resolve(monster, context, list);
            return list;
        }
    }
}
