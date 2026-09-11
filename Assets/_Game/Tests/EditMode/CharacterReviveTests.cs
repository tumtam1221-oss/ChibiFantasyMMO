using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Getting up again after losing a fight.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists at all.</b> Nothing in this project had ever reduced a player to
    /// zero health until monsters could attack, so nothing handled it -- and the first time
    /// it happened the character was stuck permanently. Combat refuses a dead attacker,
    /// monsters stop targeting a corpse, and current health is persisted, so signing out and
    /// back in restored exactly the zero they left. The only way back was editing the
    /// database by hand.
    ///
    /// <b>What these hold.</b> That being dead is the entire entitlement (so the button
    /// cannot be a heal), that the place they wake up is authored content rather than a
    /// coordinate in code, and that a client contributes nothing but the wish.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterReviveTests
    {
        private const string HomeMap = "map.home";
        private const string EmptyMap = "map.nowhere";
        private const int Connection = 11;

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r) =>
                CharacterPersistenceResult.Saved(r + 1);
        }

        private FakeStore _store;
        private DefinitionRegistry<SpawnPointDefinition> _spawns;
        private WorldCharacterRegistry _players;
        private CharacterReviveAuthority _revive;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();
            _spawns = new DefinitionRegistry<SpawnPointDefinition>();

            // The town square, standing on the ground. Y is part of the fixture because a
            // spawn point's height is baked from the map and a revive has to use it.
            _spawns.Register(Spawn("spawn.home.plaza", HomeMap, 12f, 3.5f, -8f));

            _players = new WorldCharacterRegistry(_store, _spawns);
            _revive = new CharacterReviveAuthority(_players, _spawns, 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private SpawnPointDefinition Spawn(string id, string map, float x, float y, float z)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":" + F(x) + ",\"_y\":" + F(y) + ",\"_z\":" + F(z) + "}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private static string F(float value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private LivingCharacter Admit(string character, string map = HomeMap)
        {
            string session = "session-" + character;

            _store.Rows[session] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(map),
                default, null, null, null, 1);

            WorldSpawnResult result = _players.Spawn(Connection,
                WorldAdmission.Admitted(new SessionId(session), new AccountId("acc-" + character),
                    new CharacterId(character), new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(map), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(1));

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        private static void Kill(LivingCharacter character)
        {
            character.Combatant.ApplyHealthDelta(-character.Combatant.CurrentHealth);

            Assert.That(character.Combatant.CurrentHealth, Is.Zero, "fixture");
        }

        // ---- the thing that was impossible ------------------------------------------------

        [Test]
        public void AFallenCharacterCanGetUpAgain()
        {
            LivingCharacter player = Admit("char-fallen");

            player.Combatant.Position = new CombatPosition(80f, 0f, 60f);

            Kill(player);

            ReviveResult result = _revive.Revive(Connection);

            Assert.That(result.IsRevived, Is.True, result.Rejection.ToString());
            Assert.That(player.Combatant.CurrentHealth, Is.GreaterThan(0));
        }

        [Test]
        public void TheyWakeUpAtTheMapsAuthoredSpawnPoint()
        {
            // Not a coordinate written here. The spawn point's height is baked from the
            // map's ground, which is why a revive that invented a position would put
            // somebody underground or in the air.
            LivingCharacter player = Admit("char-lost");

            player.Combatant.Position = new CombatPosition(-95f, 12f, 40f);
            player.Location.Position = player.Combatant.Position;

            Kill(player);

            _revive.Revive(Connection);

            Assert.That(player.Combatant.Position, Is.EqualTo(new CombatPosition(12f, 3.5f, -8f)));
            Assert.That(player.Location.Position, Is.EqualTo(new CombatPosition(12f, 3.5f, -8f)),
                "combat and persistence read different position fields, and both must move");
        }

        [Test]
        public void TheyGetBackTheAuthoredFractionOfTheirHealth()
        {
            LivingCharacter player = Admit("char-half");

            int ceiling = player.Combatant.MaxHealth;

            Assert.That(ceiling, Is.GreaterThan(0), "fixture");

            Kill(player);

            _revive.Revive(Connection);

            Assert.That(player.Combatant.CurrentHealth, Is.EqualTo(ceiling / 2));
        }

        [Test]
        public void TheFractionIsAWorldRuleRatherThanAConstant()
        {
            var generous = new CharacterReviveAuthority(_players, _spawns, 1f);

            LivingCharacter player = Admit("char-full");

            int ceiling = player.Combatant.MaxHealth;

            Kill(player);

            generous.Revive(Connection);

            Assert.That(player.Combatant.CurrentHealth, Is.EqualTo(ceiling));
        }

        // ---- and what it must never become --------------------------------------------------

        [Test]
        public void ALivingCharacterCannotUseItToHeal()
        {
            // The whole guard. Without it the button is a free full heal on a cooldown of
            // however fast a client can press it.
            LivingCharacter player = Admit("char-cheat");

            player.Combatant.ApplyHealthDelta(-60);

            int wounded = player.Combatant.CurrentHealth;

            Assert.That(wounded, Is.GreaterThan(0), "fixture");

            ReviveResult result = _revive.Revive(Connection);

            Assert.That(result.IsRevived, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(ReviveRejection.StillAlive));
            Assert.That(player.Combatant.CurrentHealth, Is.EqualTo(wounded));
        }

        [Test]
        public void PressingItTwiceDoesNotHealTwice()
        {
            LivingCharacter player = Admit("char-eager");

            int ceiling = player.Combatant.MaxHealth;

            Kill(player);

            _revive.Revive(Connection);
            _revive.Revive(Connection);
            _revive.Revive(Connection);

            Assert.That(player.Combatant.CurrentHealth, Is.EqualTo(ceiling / 2));
            Assert.That(_revive.Revivals, Is.EqualTo(1));
        }

        [Test]
        public void ItDoesNotMoveACharacterWhoIsNotOnTheConnection()
        {
            ReviveResult result = _revive.Revive(connectionId: 9999);

            Assert.That(result.IsRevived, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(ReviveRejection.NoCharacter));
        }

        [Test]
        public void AMapWithNoPlayerSpawnRefusesRatherThanDroppingThemAtTheOrigin()
        {
            // Refusing is visible. Waking somebody at (0, 0, 0) on a map whose town is
            // elsewhere would look like the revive worked.
            var barren = new DefinitionRegistry<SpawnPointDefinition>();
            var authority = new CharacterReviveAuthority(_players, barren, 0.5f);

            LivingCharacter player = Admit("char-nowhere");

            Kill(player);

            ReviveResult result = authority.Revive(Connection);

            Assert.That(result.IsRevived, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(ReviveRejection.NowhereToWakeUp));
            Assert.That(player.Combatant.CurrentHealth, Is.Zero,
                "a refused revive must change nothing");
        }

        [Test]
        public void TheRequestCarriesNothingAClientCouldLieWith()
        {
            // Not a position, not an amount, not a claim to be dead. If any of those were
            // parameters, a tampered client would be choosing where it woke up and on how
            // much health.
            System.Reflection.MethodInfo submit =
                typeof(ICharacterReviveRequestSink).GetMethod("Submit");

            Assert.That(submit, Is.Not.Null);

            System.Reflection.ParameterInfo[] parameters = submit.GetParameters();

            Assert.That(parameters.Length, Is.EqualTo(2));
            Assert.That(parameters[0].Name, Is.EqualTo("connectionId"));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(long)));
        }

        [Test]
        public void TheServerRpcTakesTheConnectionFromTheOwnerRatherThanTheCaller()
        {
            // Otherwise one client could revive another, or name a connection that is not
            // its own.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Network/CharacterNetworkEntity.cs");

            int at = source.IndexOf("public void RequestRevive(", System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThanOrEqualTo(0), "the revive request is gone");

            string body = source.Substring(at, 600);

            Assert.That(body, Does.Contain("Owner == null ? -1 : Owner.ClientId"),
                "the connection is not taken from the object's owner");
        }

        [Test]
        public void TheWorldServerActuallyComposesIt()
        {
            // The shape of failure this guards against has happened twice in this project:
            // a complete, correct piece of server code that nothing ever constructed.
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs");

            Assert.That(source, Does.Contain("new CharacterReviveAuthority("),
                "the server builds no revive authority, so the button would do nothing");
            Assert.That(source, Does.Contain("replication.UseRevive("),
                "nothing points a spawned character at it");
        }
    }
}
