using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Who a kill pays, and who may take what it dropped.
    /// </summary>
    /// <remarks>
    /// <b>Everything here is decided at the moment of the defeat.</b> That is the single
    /// idea the whole gate rests on: a party can be joined, left or disbanded between a boss
    /// dying and its loot being picked up, and if eligibility were recomputed at pickup time
    /// then a player who was nowhere near the fight could join the killer's party afterwards
    /// and walk away with the drop. Most of this file is that one property, approached from
    /// different directions.
    ///
    /// <b>Nothing here re-implements a party rule.</b> The split, the rotation and the
    /// policy all come from Phase 13's own services; these tests check that the reward path
    /// asks them, and asks them once.
    /// </remarks>
    [TestFixture]
    internal sealed class PartyRewardTests
    {
        private static readonly CharacterId Ann = new CharacterId("char-ann");
        private static readonly CharacterId Ben = new CharacterId("char-ben");
        private static readonly CharacterId Cal = new CharacterId("char-cal");

        // ---- the split ---------------------------------------------------------------------

        [Test]
        public void ASoloKillPaysTheWholeReward()
        {
            List<PartyExperienceShare> shares = Split(900, Ann);

            Assert.That(shares.Count, Is.EqualTo(1));
            Assert.That(shares[0].Character, Is.EqualTo(Ann));
            Assert.That(shares[0].Experience, Is.EqualTo(900));
        }

        [Test]
        public void APartyDividesTheRewardAndLosesNothingToRounding()
        {
            List<PartyExperienceShare> shares = Split(100, Ann, Ben, Cal);

            Assert.That(shares.Select(s => s.Experience), Is.EqualTo(new[] { 34, 33, 33 }));

            Assert.That(shares.Sum(s => s.Experience), Is.EqualTo(100),
                "experience was lost between the monster and the party");
        }

        [Test]
        public void EveryPartySizeUpToSixDividesTheRewardExactly()
        {
            CharacterId[] members = Enumerable.Range(0, 6)
                .Select(i => new CharacterId("char-" + i)).ToArray();

            for (var size = 1; size <= 6; size++)
            {
                foreach (int total in new[] { 900, 100, 7, 1, 0 })
                {
                    List<PartyExperienceShare> shares = Split(total, members.Take(size)
                        .ToArray());

                    Assert.That(shares.Sum(s => s.Experience), Is.EqualTo(total),
                        "party of " + size + " lost experience from " + total);

                    if (total > 0)
                    {
                        // Nobody is paid twice, and nobody is skipped.
                        Assert.That(shares.Select(s => s.Character).Distinct().Count(),
                            Is.EqualTo(shares.Count));
                    }
                }
            }
        }

        [Test]
        public void ARewardOfNothingPaysNobodyRatherThanEveryoneZero()
        {
            Assert.That(Split(0, Ann, Ben), Is.Empty);
        }

        // ---- the loot claim ------------------------------------------------------------------

        [Test]
        public void ThePersonalPolicyGivesTheDropToWhoeverEarnedIt()
        {
            PartyState party = Party(PartyLootPolicy.Personal, Ann, Ben, Cal);

            Assert.That(PartyLootPolicyService.CanClaim(party, Ann, Ann), Is.True);
            Assert.That(PartyLootPolicyService.CanClaim(party, Ann, Ben), Is.False);
        }

        [Test]
        public void TheRoundRobinPolicyTakesTurnsInJoinOrder()
        {
            PartyState party = Party(PartyLootPolicy.RoundRobin, Ann, Ben, Cal);

            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 0), Is.EqualTo(Ann));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 1), Is.EqualTo(Ben));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 2), Is.EqualTo(Cal));
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 3), Is.EqualTo(Ann),
                "the rotation did not come back around");

            // Only the member whose turn it is may claim.
            Assert.That(PartyLootPolicyService.CanClaim(party, Ann, Ben, 1), Is.True);
            Assert.That(PartyLootPolicyService.CanClaim(party, Ann, Ann, 1), Is.False,
                "the killer took a drop that was somebody else's turn");
        }

        [Test]
        public void AStrangerIsNeverAClaimantUnderAnyPolicy()
        {
            var stranger = new CharacterId("char-stranger");

            foreach (PartyLootPolicy policy in new[]
            {
                PartyLootPolicy.Personal, PartyLootPolicy.RoundRobin,
                PartyLootPolicy.NeedGreed,
            })
            {
                PartyState party = Party(policy, Ann, Ben, Cal);

                Assert.That(PartyLootPolicyService.CanClaim(party, Ann, stranger, 0),
                    Is.False, policy + " let a non-member claim");

                var claimants = new List<CharacterId>();

                PartyLootPolicyService.EligibleClaimants(party, Ann, 0, claimants);

                Assert.That(claimants, Has.No.Member(stranger), policy.ToString());
            }
        }

        // ---- the rotation cursor -----------------------------------------------------------------

        [Test]
        public void TheRotationLivesWithThePartyAndMovesOnlyWhenAdvanced()
        {
            var registry = new WorldPartyRegistry();
            PartyState party = Party(PartyLootPolicy.RoundRobin, Ann, Ben, Cal);

            Assert.That(registry.Register(party), Is.True);
            Assert.That(registry.RotationOf(party.Id), Is.Zero);

            // Reading it, repeatedly, changes nothing: a refused or replayed pickup must
            // never cost a member their turn.
            for (var i = 0; i < 5; i++)
            {
                Assert.That(registry.RotationOf(party.Id), Is.Zero);
            }

            Assert.That(registry.AdvanceRotation(party.Id), Is.EqualTo(1));
            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(1));

            Assert.That(PartyLootPolicyService.MemberOnTurn(party,
                registry.RotationOf(party.Id)), Is.EqualTo(Ben));
        }

        [Test]
        public void EachPartyKeepsItsOwnTurn()
        {
            var registry = new WorldPartyRegistry();

            PartyState first = Party(PartyLootPolicy.RoundRobin, Ann, Ben);
            PartyState second = Party(PartyLootPolicy.RoundRobin,
                new CharacterId("char-x"), new CharacterId("char-y"));

            registry.Register(first);
            registry.Register(second);

            registry.AdvanceRotation(first.Id);
            registry.AdvanceRotation(first.Id);

            Assert.That(registry.RotationOf(first.Id), Is.EqualTo(2));
            Assert.That(registry.RotationOf(second.Id), Is.Zero,
                "one party's turn moved another party's");
        }

        // ---- membership resolution -------------------------------------------------------------------

        [Test]
        public void TheRegistryAnswersWhichPartyACharacterIsIn()
        {
            var registry = new WorldPartyRegistry();
            PartyState party = Party(PartyLootPolicy.Personal, Ann, Ben);

            registry.Register(party);

            Assert.That(registry.TryGetPartyOf(Ann, out PartyState found), Is.True);
            Assert.That(found, Is.SameAs(party));

            Assert.That(registry.TryGetPartyOf(new CharacterId("char-nobody"), out PartyState _),
                Is.False);
        }

        [Test]
        public void ADisbandedPartyReadsAsNoPartyRatherThanAnEmptyOne()
        {
            var registry = new WorldPartyRegistry();
            PartyState party = Party(PartyLootPolicy.RoundRobin, Ann, Ben);

            registry.Register(party);

            Assert.That(registry.Forget(party.Id), Is.True);

            Assert.That(registry.TryGetPartyOf(Ann, out PartyState _), Is.False,
                "a disbanded party still claims its members");

            // And a solo kill is then paid the way a solo kill always was.
            Assert.That(Split(900, Ann).Single().Experience, Is.EqualTo(900));
        }

        [Test]
        public void APartyWithNoMembersIsNotAParty()
        {
            PartyState empty = Party(PartyLootPolicy.RoundRobin);

            Assert.That(empty.IsActive, Is.False);

            // Falls through to the solo rule, which is the stricter answer.
            Assert.That(PartyLootPolicyService.CanClaim(empty, Ann, Ann), Is.True);
            Assert.That(PartyLootPolicyService.CanClaim(empty, Ann, Ben), Is.False);
        }

        // ---- the defeat snapshot -----------------------------------------------------------------------

        [Test]
        public void TheDefeatContextCarriesIdsAndNotTheParty()
        {
            // If it held the party object, later membership changes would rewrite history.
            foreach (PropertyInfo property in typeof(DefeatRewardContext).GetProperties())
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(PartyState)),
                    "the snapshot points at live party state");
            }

            var context = new DefeatRewardContext(new InstanceId("m-1"), Ann,
                new PartyId("p-1"), new[] { Ann, Ben }, new DefinitionId("map.x"), 3);

            Assert.That(context.Eligible.Count, Is.EqualTo(2));
            Assert.That(context.Rotation, Is.EqualTo(3));
            Assert.That(context.IsParty, Is.True);
        }

        [Test]
        public void ASnapshotOfOneIsASoloKill()
        {
            var context = new DefeatRewardContext(new InstanceId("m-1"), Ann, PartyId.None,
                new[] { Ann }, new DefinitionId("map.x"), 0);

            Assert.That(context.IsParty, Is.False);
        }

        // ---- architecture ---------------------------------------------------------------------------------

        [Test]
        public void ThereIsExactlyOnePartyServiceAndOneLootPolicyService()
        {
            Assembly gameplay = typeof(PartyService).Assembly;
            Assembly server = typeof(WorldPartyRegistry).Assembly;

            string[] parties = gameplay.GetTypes().Concat(server.GetTypes())
                .Where(t => t.Name.EndsWith("PartyService")
                    || t.Name.EndsWith("PartyLootPolicyService"))
                .Select(t => t.FullName).ToArray();

            Assert.That(parties.Length, Is.EqualTo(2), string.Join(", ", parties));

            foreach (string forbidden in new[]
            {
                "PartyRewardService", "PartyLootService", "SecondPartyService",
                "PartyExperienceService", "RaidService", "PartyLootRegistry",
            })
            {
                Assert.That(gameplay.GetTypes().Concat(server.GetTypes())
                    .Any(t => t.Name == forbidden), Is.False, forbidden + " exists");
            }
        }

        [Test]
        public void TheRewardAuthorityAsksThePartyServicesRatherThanRepeatingThem()
        {
            string source = File.ReadAllText(
                "Assets/_Game/Scripts/Server/MonsterRewardAuthority.cs");

            Assert.That(source.Contains("PartyExperiencePolicy.Share"), Is.True,
                "the reward authority divides experience itself");

            Assert.That(source.Contains("PartyLootPolicyService.EligibleClaimants"), Is.True,
                "the reward authority decides claimants itself");

            // No second arithmetic: no division of the reward anywhere in this file.
            Assert.That(source.Contains("experience /") || source.Contains("/ members.Count")
                || source.Contains("/ eligible.Count"), Is.False,
                "the reward authority has its own split");
        }

        [Test]
        public void NoClientCodeDecidesMembershipRewardsOrClaims()
        {
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts/Client", "*.cs",
                SearchOption.AllDirectories))
            {
                if (path.Replace(Path.DirectorySeparatorChar, '/').Contains("/Prototype/"))
                {
                    continue;
                }

                string source = File.ReadAllText(path);

                foreach (string forbidden in new[]
                {
                    "PartyLootPolicyService", "PartyExperiencePolicy", "WorldPartyRegistry",
                    "MonsterRewardAuthority", "DefeatRewardContext", "AdvanceRotation",
                })
                {
                    Assert.That(source.Contains(forbidden), Is.False,
                        path + " contains '" + forbidden + "'");
                }
            }
        }

        [Test]
        public void APickupRequestStillNamesOnlyAPileAndASlot()
        {
            // Party integration must not have widened the wire. A client that could send a
            // party id or a winner could award itself the drop.
            MethodInfo submit = typeof(ChibiFantasy.Network.ICharacterLootRequestSink)
                .GetMethod("Submit");

            string[] names = submit.GetParameters()
                .Select(p => p.Name.ToLowerInvariant()).ToArray();

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "connectionid", "lootid", "index", "sequence",
            }));

            foreach (string forbidden in new[]
            {
                "party", "winner", "policy", "member", "eligible", "rotation", "experience",
            })
            {
                Assert.That(names.Any(n => n.Contains(forbidden)), Is.False,
                    "a client can send '" + forbidden + "'");
            }
        }

        [Test]
        public void TheMaximumPartySizeIsStillSix()
        {
            Assert.That(SocialConfiguration.Default.MaxPartySize, Is.EqualTo(6));
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private static List<PartyExperienceShare> Split(int total, params CharacterId[] members)
        {
            var shares = new List<PartyExperienceShare>();

            PartyExperiencePolicy.Share(total, members, shares);

            return shares;
        }

        private static PartyState Party(PartyLootPolicy policy, params CharacterId[] members)
        {
            var party = new PartyState(new PartyId("p-" + System.Guid.NewGuid()),
                members.Length > 0 ? members[0] : default, policy);

            for (var i = 1; i < members.Length; i++) party.TryAdd(members[i]);

            return party;
        }
    }
}
