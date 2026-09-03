using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Who a party's policy says may claim a piece of loot.
    /// </summary>
    /// <remarks>
    /// <b>It decides eligibility and nothing else.</b> No loot object is created, copied,
    /// moved or claimed here. <c>LootObjectState</c> from Phase 10 remains the single
    /// authority on what dropped and whether it has been taken, and
    /// <c>LootPickupService</c> remains the only thing that hands it over. This answers one
    /// question -- "may this character claim it" -- and a caller feeds the answer to the
    /// existing pickup path.
    ///
    /// That split is the whole reason party loot needed no new loot type. A policy that
    /// produced its own loot objects would give a party six copies of one drop.
    ///
    /// <b>Deterministic.</b> Round-robin turns on a counter the caller supplies, so the same
    /// sequence of kills produces the same assignment on a server and on a client without
    /// replicating a random choice. Need-and-greed deliberately does <em>not</em> roll here:
    /// see <see cref="EligibleClaimants"/>.
    /// </remarks>
    public static class PartyLootPolicyService
    {
        /// <summary>
        /// Whether a character may claim a drop.
        /// </summary>
        /// <param name="party">The party. Null means solo, which is always personal.</param>
        /// <param name="attributedTo">Who the loot object was attributed to on drop.</param>
        /// <param name="claimant">Who is trying to take it.</param>
        /// <param name="rotation">
        /// How many drops this party has already assigned, for round-robin. The caller keeps
        /// it, because it is a property of the run rather than of the loot.
        /// </param>
        /// <remarks>
        /// Solo play and a personal-loot party give the same answer as Phase 10 did, so
        /// nothing about existing behaviour changes when a policy is not in use.
        /// </remarks>
        public static bool CanClaim(PartyState party, CharacterId attributedTo,
            CharacterId claimant, int rotation = 0)
        {
            if (!claimant.IsValid) return false;

            // No party, or one that has been disbanded: Phase 10's rule, unchanged.
            if (party == null || !party.IsActive) return attributedTo == claimant;

            if (!party.Contains(claimant)) return false;

            switch (party.LootPolicy)
            {
                case PartyLootPolicy.RoundRobin:
                {
                    CharacterId turn = MemberOnTurn(party, rotation);
                    return turn.IsValid && turn == claimant;
                }

                case PartyLootPolicy.NeedGreed:
                    // Every member is eligible; which of them wins is a roll the caller runs.
                    return true;

                default:
                    return attributedTo == claimant;
            }
        }

        /// <summary>
        /// Whose turn it is under round-robin.
        /// </summary>
        /// <remarks>Plain modulo over the join-ordered member list. Negative rotations are
        /// folded rather than throwing, because a counter that went backwards is a caller
        /// bug that should not become a crash in a loot path.</remarks>
        public static CharacterId MemberOnTurn(PartyState party, int rotation)
        {
            if (party == null || !party.IsActive) return CharacterId.None;

            IReadOnlyList<CharacterId> members = party.Members;
            if (members.Count == 0) return CharacterId.None;

            int index = rotation % members.Count;
            if (index < 0) index += members.Count;

            return members[index];
        }

        /// <summary>
        /// Everyone the policy allows to claim a drop.
        /// </summary>
        /// <remarks>
        /// What a need-and-greed prompt is built from. It returns the candidates and stops
        /// there: choosing among them needs a roll, a timer and a UI, and building a partial
        /// version of that here would be a system that looked finished and was not. The
        /// caller rolls with the injected random sources the rest of this assembly already
        /// uses, and hands the winner back to <see cref="CanClaim"/>'s caller.
        /// </remarks>
        public static void EligibleClaimants(PartyState party, CharacterId attributedTo,
            int rotation, List<CharacterId> into)
        {
            if (into == null) return;

            into.Clear();

            if (party == null || !party.IsActive)
            {
                if (attributedTo.IsValid) into.Add(attributedTo);
                return;
            }

            switch (party.LootPolicy)
            {
                case PartyLootPolicy.RoundRobin:
                {
                    CharacterId turn = MemberOnTurn(party, rotation);
                    if (turn.IsValid) into.Add(turn);
                    break;
                }

                case PartyLootPolicy.NeedGreed:
                {
                    IReadOnlyList<CharacterId> members = party.Members;
                    for (int i = 0; i < members.Count; i++) into.Add(members[i]);
                    break;
                }

                default:
                    if (attributedTo.IsValid && party.Contains(attributedTo))
                    {
                        into.Add(attributedTo);
                    }

                    break;
            }
        }
    }

    /// <summary>One member's share of an experience award.</summary>
    /// <remarks>Flat, and ids only: a share names a character and an amount. Applying it to
    /// a character's progression is the caller's job, through whatever Phase 05 already
    /// provides -- nothing here writes a level.</remarks>
    public readonly struct PartyExperienceShare
    {
        public PartyExperienceShare(CharacterId character, int experience)
        {
            Character = character;
            Experience = experience;
        }

        public CharacterId Character { get; }

        public int Experience { get; }

        public override string ToString()
        {
            return Character + " +" + Experience + "xp";
        }
    }

    /// <summary>
    /// How a party's experience award is divided.
    /// </summary>
    /// <remarks>
    /// <b>A seam, and an honest one.</b> This computes shares and returns them. It does not
    /// touch <c>CharacterProgressionState</c>, and Phase 05 is not modified -- awarding the
    /// shares is the caller's step, using whatever progression path already exists. That
    /// boundary is deliberate: integrating party experience into character progression would
    /// have meant reopening Phase 05, and the brief said to stop rather than do that.
    ///
    /// <b>No invented MMO formula.</b> The division is an even split with the remainder
    /// distributed deterministically, and a caller that wants level-weighting, distance
    /// falloff or a party bonus supplies its own eligibility list and multiplier. Nothing
    /// here consults a class, a job or a level, and no coefficient is hard-coded.
    ///
    /// <b>Integer arithmetic.</b> Experience is a count. Dividing it in floating point and
    /// rounding at the end is how six party members receive five points between them.
    /// </remarks>
    public static class PartyExperiencePolicy
    {
        /// <summary>
        /// Splits an award among eligible members.
        /// </summary>
        /// <param name="totalExperience">What the kill was worth. Zero or less shares nothing.</param>
        /// <param name="eligible">
        /// Who receives a share. The caller decides eligibility -- being alive, being in
        /// range, being on the same map -- because those are rules this file cannot see.
        /// </param>
        /// <param name="into">Where the shares are written. Cleared first.</param>
        /// <remarks>
        /// Every point is distributed: the remainder after an even split goes one point each
        /// to the first members in order, so the shares always sum to exactly the award. A
        /// split that dropped the remainder would quietly destroy experience, and one that
        /// rounded each share up would create it.
        /// </remarks>
        public static void Share(int totalExperience, IReadOnlyList<CharacterId> eligible,
            List<PartyExperienceShare> into)
        {
            if (into == null) return;

            into.Clear();

            if (eligible == null || eligible.Count == 0 || totalExperience <= 0) return;

            int count = eligible.Count;
            int each = totalExperience / count;
            int remainder = totalExperience - each * count;

            for (int i = 0; i < count; i++)
            {
                int share = each + (i < remainder ? 1 : 0);
                into.Add(new PartyExperienceShare(eligible[i], share));
            }
        }

        /// <summary>
        /// The members a caller would normally treat as eligible.
        /// </summary>
        /// <remarks>Every member of an active party, or the solo character. Offered as the
        /// default so a caller with no extra rules does not have to build the list, and
        /// deliberately not consulted by <see cref="Share"/> -- a caller with rules passes
        /// its own.</remarks>
        public static void DefaultEligible(PartyState party, CharacterId soloCharacter,
            List<CharacterId> into)
        {
            if (into == null) return;

            into.Clear();

            if (party == null || !party.IsActive)
            {
                if (soloCharacter.IsValid) into.Add(soloCharacter);
                return;
            }

            IReadOnlyList<CharacterId> members = party.Members;
            for (int i = 0; i < members.Count; i++) into.Add(members[i]);
        }
    }
}
