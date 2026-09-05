using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// A party that outlives the session it was made in.
    /// </summary>
    /// <remarks>
    /// <b>The failure this guards against is duplication.</b> Six members reconnecting at
    /// the same moment must find one party, not six; a party already running must not be
    /// overwritten by an older row; and a party saved twice must not grow. Every one of
    /// those is the same mistake — treating "restore" as "create" — so most of this file
    /// is about restoring something that is already there.
    ///
    /// <b>Nothing here re-decides a party rule.</b> Membership, size and policy are Phase
    /// 13's; these tests are about the journey to storage and back.
    /// </remarks>
    [TestFixture]
    internal sealed class PartyPersistenceTests
    {
        private static readonly CharacterId Ann = new CharacterId("char-ann");
        private static readonly CharacterId Ben = new CharacterId("char-ben");
        private static readonly CharacterId Cal = new CharacterId("char-cal");

        /// <summary>A store that remembers exactly what it was told, and counts reads.</summary>
        private sealed class FakeStore : IPartyStateStore
        {
            private readonly Dictionary<string, PersistedParty> _byCharacter =
                new Dictionary<string, PersistedParty>();

            public int Loads { get; private set; }

            public int Saves { get; private set; }

            public bool Broken { get; set; }

            /// <summary>The session id is the character id, so a fake can key on it.</summary>
            public PartyPersistenceResult Load(SessionId session)
            {
                Loads++;

                if (Broken)
                {
                    return PartyPersistenceResult.Failed(
                        PartyPersistenceFailure.Unreachable, "backend down");
                }

                return _byCharacter.TryGetValue(session.Value, out PersistedParty party)
                    ? PartyPersistenceResult.Loaded(party)
                    : PartyPersistenceResult.None();
            }

            public PartyPersistenceResult Save(SessionId session, PersistedParty party)
            {
                Saves++;

                if (Broken)
                {
                    return PartyPersistenceResult.Failed(
                        PartyPersistenceFailure.Unreachable, "backend down");
                }

                foreach (string key in _byCharacter.Keys.ToArray())
                {
                    if (_byCharacter[key].Party == party.Party) _byCharacter.Remove(key);
                }

                if (party.Members.Count == 0) return PartyPersistenceResult.Saved(0);

                var stored = new PersistedParty(party.Party, party.Leader, party.LootPolicy,
                    party.Members, party.Revision + 1);

                foreach (CharacterId member in party.Members)
                {
                    _byCharacter[member.Value] = stored;
                }

                return PartyPersistenceResult.Saved(stored.Revision);
            }

            public void Seed(PersistedParty party)
            {
                foreach (CharacterId member in party.Members)
                {
                    _byCharacter[member.Value] = party;
                }
            }
        }

        // ---- what is stored -----------------------------------------------------------------

        [Test]
        public void APersistedPartyCarriesOnlyDurableFacts()
        {
            // Anything about a connection, a body or an inventory would be a second copy
            // of state character persistence already owns.
            string[] members = typeof(PersistedParty).GetProperties()
                .Select(p => p.Name.ToLowerInvariant()).ToArray();

            foreach (string forbidden in new[]
            {
                "connection", "networkobject", "position", "health", "mana", "level",
                "inventory", "devilfruit", "experience", "alive",
            })
            {
                Assert.That(members.Any(m => m.Contains(forbidden)), Is.False,
                    "a persisted party carries '" + forbidden + "'");
            }

            Assert.That(members, Is.EquivalentTo(new[]
            {
                "party", "leader", "lootpolicy", "members", "revision", "exists",
            }));
        }

        [Test]
        public void AnEmptyPartyIsNotAParty()
        {
            Assert.That(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.Personal, new CharacterId[0], 1).Exists, Is.False);

            Assert.That(new PersistedParty(default, Ann, PartyLootPolicy.Personal,
                new[] { Ann }, 1).Exists, Is.False);
        }

        // ---- restore --------------------------------------------------------------------------

        [Test]
        public void ACharacterInNoStoredPartyRestoresToNothing()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            Assert.That(registry.Restore(Session(Ann), Ann, store), Is.Null);
            Assert.That(registry.Count, Is.Zero);
        }

        [Test]
        public void AStoredPartyComesBackWithItsLeaderPolicyAndOrder()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ben,
                PartyLootPolicy.RoundRobin, new[] { Ben, Ann, Cal }, 7));

            PartyState restored = registry.Restore(Session(Ann), Ann, store);

            Assert.That(restored, Is.Not.Null, "a stored party did not come back");
            Assert.That(restored.Id.Value, Is.EqualTo("p-1"));
            Assert.That(restored.Leader, Is.EqualTo(Ben));
            Assert.That(restored.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin));

            // Order matters: round robin walks this list.
            Assert.That(restored.Members, Is.EqualTo(new[] { Ben, Ann, Cal }));

            Assert.That(registry.RevisionOf(restored.Id), Is.EqualTo(7));
        }

        [Test]
        public void SixMembersReconnectingAtOnceShareOnePartyObject()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            CharacterId[] six = Enumerable.Range(0, 6)
                .Select(i => new CharacterId("char-" + i)).ToArray();

            store.Seed(new PersistedParty(new PartyId("p-1"), six[0],
                PartyLootPolicy.RoundRobin, six, 1));

            var restored = new List<PartyState>();

            foreach (CharacterId member in six)
            {
                restored.Add(registry.Restore(Session(member), member, store));
            }

            Assert.That(restored, Has.None.Null);

            Assert.That(restored.Distinct().Count(), Is.EqualTo(1),
                "six reconnects built " + restored.Distinct().Count() + " party objects");

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(restored[0].MemberCount, Is.EqualTo(6),
                "restoring repeatedly grew the party");
        }

        [Test]
        public void APartyAlreadyRunningIsNotReadFromStorageAgain()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.Personal, new[] { Ann, Ben }, 1));

            registry.Restore(Session(Ann), Ann, store);

            int loads = store.Loads;

            // Ann reconnects. The world already has her party.
            registry.Restore(Session(Ann), Ann, store);

            Assert.That(store.Loads, Is.EqualTo(loads),
                "a member already in a running party re-read it from storage, which is "
                + "how a live membership gets overwritten by an older row");
        }

        [Test]
        public void RestoringIsIdempotent()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 3));

            for (var i = 0; i < 5; i++) registry.Restore(Session(Ann), Ann, store);

            Assert.That(registry.Count, Is.EqualTo(1));

            registry.TryGetPartyOf(Ann, out PartyState party);

            Assert.That(party.MemberCount, Is.EqualTo(2));
            Assert.That(party.Members.Count(m => m == Ann), Is.EqualTo(1),
                "a member was added twice");
        }

        [Test]
        public void ABackendThatCannotAnswerLeavesTheCharacterPartylessRatherThanGuessing()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.Personal, new[] { Ann }, 1));

            store.Broken = true;

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not restore"));

            Assert.That(registry.Restore(Session(Ann), Ann, store), Is.Null);
            Assert.That(registry.Count, Is.Zero, "a failed read invented a party");
        }

        [Test]
        public void AMemberNamingACharacterThisWorldDoesNotHaveStillRestores()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            // A member who is offline, deleted, or on another server. The party is still
            // the party; refusing to load it would strand everybody else.
            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.Personal, new[] { Ann, new CharacterId("char-ghost") }, 1));

            PartyState restored = registry.Restore(Session(Ann), Ann, store);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.MemberCount, Is.EqualTo(2));
        }

        // ---- persist ------------------------------------------------------------------------------

        [Test]
        public void APartyIsWrittenBackWholeAndComesBackTheSame()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.RoundRobin);
            party.TryAdd(Ben);
            party.TryAdd(Cal);

            registry.Register(party);

            Assert.That(registry.Persist(Session(Ann), party, store).IsOk, Is.True);

            // A fresh world, as a server restart gives.
            var restarted = new WorldPartyRegistry();

            PartyState restored = restarted.Restore(Session(Cal), Cal, store);

            Assert.That(restored, Is.Not.Null, "the party did not survive a restart");
            Assert.That(restored.Id, Is.EqualTo(party.Id));
            Assert.That(restored.Leader, Is.EqualTo(Ann));
            Assert.That(restored.LootPolicy, Is.EqualTo(PartyLootPolicy.RoundRobin));
            Assert.That(restored.Members, Is.EqualTo(new[] { Ann, Ben, Cal }));
        }

        [Test]
        public void SavingTheSamePartyRepeatedlyDoesNotGrowIt()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.Personal);
            party.TryAdd(Ben);

            registry.Register(party);

            for (var i = 0; i < 4; i++)
            {
                Assert.That(registry.Persist(Session(Ann), party, store).IsOk, Is.True);
            }

            PartyState restored = new WorldPartyRegistry().Restore(Session(Ben), Ben, store);

            Assert.That(restored.MemberCount, Is.EqualTo(2),
                "repeated saves duplicated the membership");
        }

        [Test]
        public void APartyEndsByBeingStoredWithNoMembersAndNeverComesBack()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore();

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.Personal);
            party.TryAdd(Ben);

            registry.Register(party);
            registry.Persist(Session(Ann), party, store);

            // A member leaving is not the party ending. Phase 13 will not let the leader
            // be removed this way at all -- a leader leaves by transfer or by disband --
            // so a party never quietly empties itself into nothing.
            Assert.That(party.TryRemove(Ben), Is.True);
            Assert.That(party.TryRemove(Ann), Is.False, "the leader was removed by leaving");

            registry.Persist(Session(Ann), party, store);

            PartyState afterLeave = new WorldPartyRegistry()
                .Restore(Session(Ann), Ann, store);

            Assert.That(afterLeave, Is.Not.Null, "one member leaving ended the party");
            Assert.That(afterLeave.MemberCount, Is.EqualTo(1));

            Assert.That(new WorldPartyRegistry().Restore(Session(Ben), Ben, store), Is.Null,
                "the member who left is still in the party");

            // Disband is an explicit empty membership, which is the shape the API defines.
            Assert.That(store.Save(Session(Ann), new PersistedParty(party.Id, Ann,
                party.LootPolicy, new CharacterId[0], 0)).IsOk, Is.True);

            Assert.That(new WorldPartyRegistry().Restore(Session(Ann), Ann, store), Is.Null,
                "a disbanded party came back");
        }

        [Test]
        public void AFailedWriteIsReportedRatherThanAssumed()
        {
            var registry = new WorldPartyRegistry();
            var store = new FakeStore { Broken = true };

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.Personal);

            registry.Register(party);

            PartyPersistenceResult result = registry.Persist(Session(Ann), party, store);

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Failure, Is.EqualTo(PartyPersistenceFailure.Unreachable));

            // And the revision this world believes is unchanged, so the next write is not
            // silently made stale by a failure.
            Assert.That(registry.RevisionOf(party.Id), Is.Zero);
        }

        [Test]
        public void AWorldWithNoStoreSimplyDoesNotPersist()
        {
            var registry = new WorldPartyRegistry();

            Assert.That(registry.Restore(Session(Ann), Ann, null), Is.Null);

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.Personal);

            Assert.That(registry.Persist(Session(Ann), party, null).IsOk, Is.False);
        }

        // ---- rotation across a restart ------------------------------------------------------------

        [Test]
        public void MemberOrderSurvivesARestartSoTheRotationIsUnchanged()
        {
            var store = new FakeStore();

            var party = new PartyState(new PartyId("p-1"), Ann, PartyLootPolicy.RoundRobin);
            party.TryAdd(Ben);
            party.TryAdd(Cal);

            var before = new WorldPartyRegistry();
            before.Register(party);
            before.Persist(Session(Ann), party, store);

            // Turn one belongs to Ben, both before and after.
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 1), Is.EqualTo(Ben));

            PartyState after = new WorldPartyRegistry().Restore(Session(Ann), Ann, store);

            Assert.That(PartyLootPolicyService.MemberOnTurn(after, 1), Is.EqualTo(Ben),
                "the rotation points at a different member after a restart");

            Assert.That(PartyLootPolicyService.MemberOnTurn(after, 2), Is.EqualTo(Cal));
        }

        // ---- architecture -----------------------------------------------------------------------------

        [Test]
        public void ThereIsExactlyOnePartyStateTypeAndOneStore()
        {
            Assembly gameplay = typeof(PartyState).Assembly;
            Assembly server = typeof(WorldPartyRegistry).Assembly;
            Assembly contracts = typeof(IPartyStateStore).Assembly;

            string[] states = gameplay.GetTypes().Concat(server.GetTypes())
                .Where(t => t.Name.Contains("Party") && t.Name.EndsWith("State")
                    && !t.IsInterface && !t.IsEnum)
                .Select(t => t.FullName).ToArray();

            Assert.That(states.Length, Is.EqualTo(1), string.Join(", ", states));

            string[] stores = contracts.GetTypes()
                .Where(t => t.Name.Contains("Party") && t.Name.EndsWith("Store"))
                .Select(t => t.FullName).ToArray();

            Assert.That(stores.Length, Is.EqualTo(1), string.Join(", ", stores));

            foreach (string forbidden in new[]
            {
                "PersistentPartyState", "DatabasePartyService", "PartySessionState",
                "PartyRuntimeStore", "SecondPartyService",
            })
            {
                Assert.That(gameplay.GetTypes().Concat(server.GetTypes())
                    .Any(t => t.Name == forbidden), Is.False, forbidden + " exists");
            }
        }

        [Test]
        public void NoClientCodeReadsOrWritesPartyPersistence()
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
                    "IPartyStateStore", "HttpPartyStateStore", "PersistedParty",
                    "WorldPartyRegistry", "PartyPersistenceResult",
                })
                {
                    Assert.That(source.Contains(forbidden), Is.False,
                        path + " contains '" + forbidden + "'");
                }
            }
        }

        [Test]
        public void TheStoreCarriesNoCredentialAndNoAddress()
        {
            // Comments stripped: the file documents that it holds no credential, and a
            // guard that punished it for saying so would reward saying nothing.
            string source = Code("Assets/_Game/Scripts/Backend/HttpPartyStateStore.cs");

            foreach (string forbidden in new[]
            {
                "password", "mysql", "pdo", "http://", "https://", "DB_", "secret",
            })
            {
                Assert.That(source.ToLowerInvariant().Contains(forbidden.ToLowerInvariant()),
                    Is.False, "the party store contains '" + forbidden + "'");
            }
        }

        [Test]
        public void TheMaximumPartySizeIsStillSix()
        {
            Assert.That(SocialConfiguration.Default.MaxPartySize, Is.EqualTo(6));
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        /// <summary>A file's code, with its comments removed.</summary>
        private static string Code(string path)
        {
            var code = new System.Text.StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;

                int comment = line.IndexOf("//", System.StringComparison.Ordinal);

                code.AppendLine(comment >= 0 ? line.Substring(0, comment) : line);
            }

            return code.ToString();
        }

        /// <summary>The fake store keys on the session, so one per character.</summary>
        private static SessionId Session(CharacterId character)
        {
            return new SessionId(character.Value);
        }
    }
}
