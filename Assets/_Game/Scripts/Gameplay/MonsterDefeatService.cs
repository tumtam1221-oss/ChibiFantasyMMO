using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What a monster's defeat is worth.
    /// </summary>
    /// <remarks>
    /// <b>A result, not a payout.</b> No experience was granted, no item created and no
    /// quest advanced. This says what is owed; who receives it is the caller's decision,
    /// which is what leaves room for party distribution without any of this changing.
    ///
    /// <b>It carries the participants.</b> Only the killer is recorded today, but the list
    /// is the seam a party split plugs into -- adding one later must not change the shape
    /// of what a kill produces.
    /// </remarks>
    public readonly struct MonsterDefeatResult
    {
        private static readonly InstanceId[] NoParticipants = new InstanceId[0];

        private readonly InstanceId[] _participants;

        private MonsterDefeatResult(bool claimed, InstanceId monsterId, DefinitionId definitionId,
            int level, int experience, int currency, InstanceId killer,
            InstanceId[] participants, int lootCount)
        {
            IsClaimed = claimed;
            MonsterInstanceId = monsterId;
            MonsterDefinitionId = definitionId;
            MonsterLevel = level;
            ExperienceReward = experience;
            CurrencyReward = currency;
            Killer = killer;
            _participants = participants ?? NoParticipants;
            LootCount = lootCount;
        }

        /// <summary>
        /// Whether this call was the one that claimed the defeat.
        /// </summary>
        /// <remarks>False for every subsequent call and for a monster still standing. Every
        /// reward hangs off this, so a second caller gets an empty result rather than a
        /// second payout.</remarks>
        public bool IsClaimed { get; }

        public InstanceId MonsterInstanceId { get; }

        public DefinitionId MonsterDefinitionId { get; }

        public int MonsterLevel { get; }

        /// <summary>Authored experience. Splitting it is the caller's business.</summary>
        public int ExperienceReward { get; }

        public int CurrencyReward { get; }

        /// <summary>Who landed the killing blow.</summary>
        public InstanceId Killer { get; }

        /// <summary>Everyone who took part. The seam a party split plugs into.</summary>
        public IReadOnlyList<InstanceId> Participants => _participants;

        /// <summary>How many loot entries were rolled into the caller's list.</summary>
        public int LootCount { get; }

        /// <summary>Nothing was owed, because nothing was claimed.</summary>
        public static MonsterDefeatResult NotClaimed => default;

        public static MonsterDefeatResult Claimed(MonsterRuntimeState monster, InstanceId killer,
            InstanceId[] participants, int lootCount)
        {
            return new MonsterDefeatResult(true, monster.InstanceId, monster.DefinitionId,
                monster.Level, monster.Definition.ExperienceReward,
                monster.Definition.CurrencyReward, killer, participants, lootCount);
        }

        public override string ToString()
        {
            if (!IsClaimed) return "not claimed";

            return MonsterDefinitionId + " defeated: exp " + ExperienceReward
                + ", currency " + CurrencyReward + ", loot " + LootCount;
        }
    }

    /// <summary>
    /// Turns a monster's death into what it owes.
    /// </summary>
    /// <remarks>
    /// <b>One claim, one payout.</b> The guard is
    /// <see cref="MonsterRuntimeState.TryClaimDefeat"/>, asked once here rather than once
    /// per reward system. Experience, loot and quest credit therefore cannot disagree about
    /// whether a kill happened, and two killing blows in the same frame pay out once.
    ///
    /// <b>It resolves; it does not grant.</b> Loot is rolled into the caller's list and the
    /// experience figure is reported. Nothing is added to an inventory, no character gains a
    /// level and no quest is advanced -- those are separate calls a caller makes with this
    /// result, which is what keeps party distribution, personal loot and quest evaluation
    /// out of the death path.
    /// </remarks>
    public static class MonsterDefeatService
    {
        /// <summary>
        /// Claims a defeat and reports what it is worth.
        /// </summary>
        /// <param name="monster">The defeated monster.</param>
        /// <param name="killer">Who landed the blow.</param>
        /// <param name="drops">Drop context. Loot is rolled only if it is usable.</param>
        /// <param name="loot">Caller-owned list the loot is appended to.</param>
        /// <param name="participants">Everyone involved. Null means the killer alone.</param>
        public static MonsterDefeatResult Resolve(MonsterRuntimeState monster, InstanceId killer,
            in DropResolver.Context drops, List<LootResult> loot,
            InstanceId[] participants = null)
        {
            if (monster == null) return MonsterDefeatResult.NotClaimed;

            // The single guard. Everything below happens exactly once per life.
            if (!monster.TryClaimDefeat()) return MonsterDefeatResult.NotClaimed;

            int lootCount = 0;

            if (loot != null && drops.IsUsable)
            {
                lootCount = DropResolver.Resolve(monster, drops, loot);
            }

            InstanceId[] resolved = participants;

            if (resolved == null)
            {
                resolved = killer.IsValid ? new[] { killer } : new InstanceId[0];
            }

            return MonsterDefeatResult.Claimed(monster, killer, resolved, lootCount);
        }

        /// <summary>
        /// Builds the world loot a defeat produced.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Resolve"/> because a game may not want world loot at
        /// all: personal loot hands the results straight to a bag, and a boss may pile
        /// everything into one object with its own policy. Splitting them keeps that
        /// decision with the caller.
        ///
        /// Returns null when there is nothing to drop, so an empty pile never appears in
        /// the world.
        /// </remarks>
        public static LootObjectState CreateLoot(in MonsterDefeatResult defeat,
            IReadOnlyList<LootResult> contents, CombatPosition position,
            LootPolicy policy = LootPolicy.FreeForAll, CharacterId eligible = default,
            float lifetimeSeconds = 0f, float personalWindowSeconds = 0f)
        {
            if (!defeat.IsClaimed || contents == null || contents.Count == 0) return null;

            return new LootObjectState(InstanceId.New(), defeat.MonsterInstanceId, position,
                contents, policy, eligible, lifetimeSeconds, personalWindowSeconds);
        }
    }
}
