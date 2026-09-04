using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Whether one player may attack another.
    /// </summary>
    /// <remarks>
    /// The property that matters most is the default: <b>refusing everywhere</b>. A server
    /// that permitted player combat because something was unconfigured would be a bug
    /// discovered by players killing each other in a starting town, so every "missing" case
    /// below is checked to refuse rather than to fall through.
    /// </remarks>
    [TestFixture]
    internal sealed class PkPolicyTests
    {
        private readonly List<MapDefinition> _created = new List<MapDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (MapDefinition map in _created)
            {
                if (map != null) Object.DestroyImmediate(map);
            }

            _created.Clear();
        }

        private MapDefinition Map(bool pkAllowed = true, bool safeZone = false,
            bool isTown = false)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"map.field\"},"
                + "\"_pkAllowed\":" + (pkAllowed ? "true" : "false") + ","
                + "\"_isSafeZone\":" + (safeZone ? "true" : "false") + ","
                + "\"_isTown\":" + (isTown ? "true" : "false") + "}", map);

            _created.Add(map);

            return map;
        }

        /// <summary>A channel with PK on and no other restriction.</summary>
        private static PkSettings Open => new PkSettings(channelEnabled: true);

        private PkVerdict Evaluate(PkSettings settings, MapDefinition map,
            int attackerLevel = 30, int targetLevel = 30, bool sameMap = true,
            bool sameCharacter = false, bool sameParty = false, bool sameGuild = false)
        {
            return PkPolicy.Evaluate(settings, map, attackerLevel, targetLevel, sameMap,
                sameCharacter, sameParty, sameGuild);
        }

        // ---- the permitted case -------------------------------------------------------------

        [Test]
        public void TwoUnrelatedPlayersOnAPkFieldMayFight()
        {
            Assert.That(Evaluate(Open, Map()).IsAllowed, Is.True);
        }

        // ---- refusing is the default -----------------------------------------------------------

        [Test]
        public void AnUnconfiguredSettingRefuses()
        {
            // default(PkSettings) is PK off. A struct whose default permitted combat would
            // be a bug found by players killing each other in a starting town.
            Assert.That(PkSettings.Disabled.ChannelEnabled, Is.False);
            Assert.That(Evaluate(default, Map()).Reason, Is.EqualTo(PkRejection.ChannelDisabled));
        }

        [Test]
        public void NoMapAtAllRefuses()
        {
            Assert.That(Evaluate(Open, null).Reason, Is.EqualTo(PkRejection.MissingContext));
        }

        [Test]
        public void TheChannelSwitchIsCheckedBeforeAnythingElse()
        {
            // A PvE server answers with one boolean and consults nothing further.
            PkVerdict verdict = Evaluate(new PkSettings(channelEnabled: false),
                Map(pkAllowed: false, safeZone: true, isTown: true));

            Assert.That(verdict.Reason, Is.EqualTo(PkRejection.ChannelDisabled));
        }

        // ---- 17.22: a client cannot enable PK ------------------------------------------------------

        [Test]
        public void ThereIsNoWayToTurnPkOnFromCode()
        {
            // Every setting arrives through the constructor from a database row and
            // authored content. A method that enabled PK would be the thing a compromised
            // client would call, so there isn't one.
            System.Type settings = typeof(PkSettings);

            Assert.That(settings.GetMethod("Enable"), Is.Null);
            Assert.That(settings.GetMethod("SetChannelEnabled"), Is.Null);

            foreach (System.Reflection.PropertyInfo property in settings.GetProperties())
            {
                Assert.That(property.CanWrite, Is.False,
                    property.Name + " must not be settable after construction");
            }
        }

        [Test]
        public void ThePolicyHoldsNoStateAClientCouldInfluence()
        {
            // Static and pure: there is nothing to poison between two evaluations.
            Assert.That(typeof(PkPolicy).IsAbstract && typeof(PkPolicy).IsSealed, Is.True,
                "a static class has no instance state to corrupt");
            Assert.That(typeof(PkPolicy).GetFields().Length, Is.Zero);
        }

        // ---- map rules -------------------------------------------------------------------------------

        [Test]
        public void AMapThatForbidsPkRefuses()
        {
            Assert.That(Evaluate(Open, Map(pkAllowed: false)).Reason,
                Is.EqualTo(PkRejection.MapDisabled));
        }

        [Test]
        public void ASafeZoneRefusesEvenWhenTheMapAllowsPk()
        {
            Assert.That(Evaluate(Open, Map(pkAllowed: true, safeZone: true)).Reason,
                Is.EqualTo(PkRejection.SafeZone));
        }

        [Test]
        public void ATownRefusesEvenWhenAuthoredAsPkAllowed()
        {
            // A town that is not safe is almost always an authoring mistake rather than an
            // intent, so the safer reading wins.
            Assert.That(Evaluate(Open, Map(pkAllowed: true, isTown: true)).Reason,
                Is.EqualTo(PkRejection.Town));
        }

        [Test]
        public void PlayersOnDifferentMapsCannotFight()
        {
            Assert.That(Evaluate(Open, Map(), sameMap: false).Reason,
                Is.EqualTo(PkRejection.DifferentMap));
        }

        [Test]
        public void APlayerCannotAttackThemselves()
        {
            Assert.That(Evaluate(Open, Map(), sameCharacter: true).Reason,
                Is.EqualTo(PkRejection.Self));
        }

        // ---- level floor --------------------------------------------------------------------------------

        [Test]
        public void ALowLevelTargetIsProtected()
        {
            PkVerdict verdict = Evaluate(new PkSettings(true, minimumLevel: 20),
                Map(), attackerLevel: 50, targetLevel: 5);

            Assert.That(verdict.Reason, Is.EqualTo(PkRejection.BelowMinimumLevel));
        }

        [Test]
        public void ALowLevelAttackerIsAlsoStopped()
        {
            // Protecting only the victim would let a level-one alt attack with impunity.
            PkVerdict verdict = Evaluate(new PkSettings(true, minimumLevel: 20),
                Map(), attackerLevel: 5, targetLevel: 50);

            Assert.That(verdict.Reason, Is.EqualTo(PkRejection.BelowMinimumLevel));
        }

        [Test]
        public void AZeroFloorDisablesTheRuleRatherThanBlockingEverybody()
        {
            Assert.That(Evaluate(new PkSettings(true, minimumLevel: 0), Map(),
                attackerLevel: 1, targetLevel: 1).IsAllowed, Is.True);
        }

        [Test]
        public void ANegativeFloorIsTreatedAsNone()
        {
            Assert.That(new PkSettings(true, minimumLevel: -10).MinimumLevel, Is.Zero);
        }

        [Test]
        public void PlayersAtExactlyTheFloorMayFight()
        {
            Assert.That(Evaluate(new PkSettings(true, minimumLevel: 20), Map(),
                attackerLevel: 20, targetLevel: 20).IsAllowed, Is.True);
        }

        // ---- social relationships ---------------------------------------------------------------------------

        [Test]
        public void PartyMembersCannotFightByDefault()
        {
            Assert.That(Evaluate(Open, Map(), sameParty: true).Reason,
                Is.EqualTo(PkRejection.SameParty));
        }

        [Test]
        public void GuildMembersCannotFightByDefault()
        {
            Assert.That(Evaluate(Open, Map(), sameGuild: true).Reason,
                Is.EqualTo(PkRejection.SameGuild));
        }

        [Test]
        public void FriendlyFireIsPermittedWhenAuthored()
        {
            Assert.That(Evaluate(new PkSettings(true, 0, allowSameParty: true), Map(),
                sameParty: true).IsAllowed, Is.True);

            Assert.That(Evaluate(new PkSettings(true, 0, allowSameGuild: true), Map(),
                sameGuild: true).IsAllowed, Is.True);
        }

        [Test]
        public void AllowingPartyFireDoesNotAllowGuildFire()
        {
            Assert.That(Evaluate(new PkSettings(true, 0, allowSameParty: true), Map(),
                sameGuild: true).Reason, Is.EqualTo(PkRejection.SameGuild));
        }

        // ---- order ---------------------------------------------------------------------------------------------

        [Test]
        public void TheBroadestApplicableReasonIsReported()
        {
            // A safe-zone town with a low-level pair reports the safe zone: the reason a
            // player can act on is the one that would still apply if everything else changed.
            PkVerdict verdict = Evaluate(new PkSettings(true, minimumLevel: 99),
                Map(pkAllowed: true, safeZone: true, isTown: true),
                attackerLevel: 1, targetLevel: 1);

            Assert.That(verdict.Reason, Is.EqualTo(PkRejection.SafeZone));
        }
    }
}
