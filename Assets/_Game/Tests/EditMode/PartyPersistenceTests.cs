using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ChibiFantasy.Backend;
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

            /// <summary>Refuse the next writes with this, rather than with Unreachable.</summary>
            public PartyPersistenceFailure RefuseSavesWith { get; set; }
                = PartyPersistenceFailure.None;

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

                if (RefuseSavesWith != PartyPersistenceFailure.None)
                {
                    return PartyPersistenceResult.Failed(RefuseSavesWith, "refused on purpose");
                }

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
                    party.Members, party.Revision + 1, party.Cursor);

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

            // Cursor joins the list because whose turn it is outlives a session exactly
            // as membership does. The forbidden list above is unchanged: it is still a
            // party, and it still carries nothing about a body or a connection.
            Assert.That(members, Is.EquivalentTo(new[]
            {
                "party", "leader", "lootpolicy", "members", "revision", "exists",
                "cursor", "iscursorvalid",
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
    

        // ---- whose turn it is ---------------------------------------------------------

        /// <summary>A transport that answers with whatever body a test hands it.</summary>
        private sealed class CannedTransport : IHttpTransport, HttpCharacterStateStore.ITokenSource
        {
            private readonly string _body;
            private readonly int _status;

            public CannedTransport(string body, int status = 200)
            {
                _body = body;
                _status = status;
            }

            public HttpExchange Send(string method, string path, string jsonBody,
                string bearerToken)
            {
                Sent = jsonBody;

                return HttpExchange.Responded(_status, _body);
            }

            /// <summary>The last body posted, so a test can read what went out.</summary>
            public string Sent { get; private set; }

            public bool TryGetToken(SessionId session, out string token)
            {
                token = "a-token-invented-here-only";

                return true;
            }
        }

        private static PartyState PartyOf(params CharacterId[] members)
        {
            var party = new PartyState(new PartyId("p-1"), members[0],
                PartyLootPolicy.RoundRobin);

            for (var i = 1; i < members.Length; i++) party.TryAdd(members[i]);

            return party;
        }

        [Test]
        public void ARestoredPartyResumesAtTheStoredTurnRatherThanTheFirstMember()
        {
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 3, 2));

            var registry = new WorldPartyRegistry();

            PartyState party = registry.Restore(Session(Ben), Ben, store);

            Assert.That(party, Is.Not.Null);
            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(2),
                "the rotation restarted at the first member");

            // The point of the number: it has to name the same person it named before.
            Assert.That(PartyLootPolicyService.MemberOnTurn(party,
                registry.RotationOf(party.Id)), Is.EqualTo(Cal));
        }

        [Test]
        public void APartyStoredBeforeThereWasATurnStartsAtTheFirstMember()
        {
            // A row written by an earlier build has no cursor, which reads as zero. That
            // is a real position rather than a missing one, so it must simply work.
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 1));

            var registry = new WorldPartyRegistry();

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            Assert.That(registry.RotationOf(party.Id), Is.Zero);
            Assert.That(PartyLootPolicyService.MemberOnTurn(party, 0), Is.EqualTo(Ann));
        }

        [Test]
        public void AStoredTurnThatAddressesNoMemberIsRefusedRatherThanWrapped()
        {
            var store = new FakeStore();

            // Three members, position three. Folding it back into range would give the
            // drop to Ann and look like a clean restore; that is the corruption.
            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 3, 3));

            var registry = new WorldPartyRegistry();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("addresses no member"));

            Assert.That(registry.Restore(Session(Ann), Ann, store), Is.Null,
                "a corrupt turn was restored anyway");

            Assert.That(registry.Count, Is.Zero, "a refused restore registered a party");
        }

        [Test]
        public void ANegativeStoredTurnIsRefusedForTheSameReason()
        {
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 1, -1));

            var registry = new WorldPartyRegistry();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("addresses no member"));

            Assert.That(registry.Restore(Session(Ann), Ann, store), Is.Null);
        }

        [Test]
        public void TheStoredTurnIsAnIndexIntoTheMembersAndNotACountOfDrops()
        {
            // Both halves of the same rule: what is written must address a member, and it
            // must address the member the running world would have picked. If Persist and
            // MemberOnTurn ever disagree about the arithmetic, this fails.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben, Cal);

            registry.Register(party);

            for (var spent = 0; spent < 8; spent++)
            {
                Assert.That(registry.Persist(Session(Ann), party, store).IsOk, Is.True);

                var reader = new WorldPartyRegistry();
                PartyState back = reader.Restore(Session(Ann), Ann, store);

                Assert.That(back, Is.Not.Null,
                    "a turn this world wrote came back unreadable after " + spent);

                Assert.That(PartyLootPolicyService.MemberOnTurn(back,
                        reader.RotationOf(back.Id)),
                    Is.EqualTo(PartyLootPolicyService.MemberOnTurn(party,
                        registry.RotationOf(party.Id))),
                    "a restart changed whose turn it was after " + spent + " drops");

                registry.AdvanceRotation(party.Id);
            }
        }

        [Test]
        public void ATurnSpentDuringCombatIsWrittenWhereItIsSpent()
        {
            // Nothing calls Persist when a monster dies -- the turn moves inside the
            // reward path. If it were only saved on a membership change, a party that
            // looted all week without anyone joining would restart at the first member.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            int before = store.Saves;

            registry.AdvanceRotation(party.Id);

            Assert.That(store.Saves, Is.GreaterThan(before),
                "the turn moved without being written down");

            var reader = new WorldPartyRegistry();
            PartyState back = reader.Restore(Session(Ben), Ben, store);

            Assert.That(reader.RotationOf(back.Id), Is.EqualTo(1),
                "a restart did not resume where the rotation had reached");

            Assert.That(PartyLootPolicyService.MemberOnTurn(back,
                reader.RotationOf(back.Id)), Is.EqualTo(Ben));
        }

        [Test]
        public void ARotationSurvivesRestartAfterRestartWithoutSkippingOrRepeating()
        {
            // A, B, C, then back to A -- across a fresh registry every single time, which
            // is what a world restart actually is.
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1));

            var order = new List<CharacterId>();

            for (var restart = 0; restart < 4; restart++)
            {
                var registry = new WorldPartyRegistry();
                PartyState party = registry.Restore(Session(Ann), Ann, store);

                Assert.That(party, Is.Not.Null, "restart " + restart + " lost the party");

                order.Add(PartyLootPolicyService.MemberOnTurn(party,
                    registry.RotationOf(party.Id)));

                registry.AdvanceRotation(party.Id);
            }

            Assert.That(order, Is.EqualTo(new[] { Ann, Ben, Cal, Ann }));
        }

        [Test]
        public void SixMembersReconnectingAtOnceShareOneTurn()
        {
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1, 1));

            var registry = new WorldPartyRegistry();

            foreach (CharacterId member in new[] { Ann, Ben, Cal })
            {
                registry.Restore(Session(member), member, store);
            }

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.RotationOf(new PartyId("p-1")), Is.EqualTo(1),
                "a second arrival re-read the turn and moved it");
        }

        [Test]
        public void APartyThatShrankWritesATurnThatStillAddressesAMember()
        {
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben, Cal);

            registry.Register(party);
            registry.AdvanceRotation(party.Id);
            registry.AdvanceRotation(party.Id);

            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(2));

            // Cal leaves. Position two no longer exists.
            party.TryRemove(Cal);

            Assert.That(registry.Persist(Session(Ann), party, store).IsOk, Is.True);

            PartyState back = new WorldPartyRegistry().Restore(Session(Ann), Ann, store);

            Assert.That(back, Is.Not.Null,
                "a shrunk party wrote a turn its own loader refuses");
        }

        [Test]
        public void ATurnIsNotLostWhenTheMemberWhoRestoredThePartyHasGone()
        {
            // Any member's session can write the party, so Ann logging out must not make
            // the party's turn unsaveable for everybody left in it.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 1));

            PartyState party = registry.Restore(Session(Ben), Ben, store);

            int before = store.Saves;

            registry.AdvanceRotation(party.Id);

            Assert.That(store.Saves, Is.GreaterThan(before));
        }

        [Test]
        public void ABackendThatCannotBeReachedDoesNotStopThePartyLooting()
        {
            // The pile has already been handed over by the time the turn moves. Failing
            // the drop because a database blinked would be the worse outcome.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 1));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            store.Broken = true;

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("could not save the loot turn"));

            Assert.That(registry.AdvanceRotation(party.Id), Is.EqualTo(1),
                "the rotation stalled because the backend was down");
        }

        [Test]
        public void ARegistryWithNoStoreStillRotates()
        {
            // Every EditMode test written before parties were persisted composes a
            // registry with no store at all, and they must go on working.
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben);

            registry.Register(party);

            Assert.That(registry.AdvanceRotation(party.Id), Is.EqualTo(1));
            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(1));
        }

        // ---- a row nobody understands -------------------------------------------------

        [Test]
        public void AnUnknownStoredPolicyIsRefusedRatherThanBecomingPersonal()
        {
            // The bug this closes: the default case used to answer Personal and report
            // success, so a corrupt row looted by a rule nobody chose and the next write
            // stamped the substitution over the evidence.
            var transport = new CannedTransport(
                "{\"party_id\":\"p-1\",\"leader_character_id\":\"char-ann\","
                + "\"loot_policy\":9,\"round_robin_cursor\":0,\"revision\":2,"
                + "\"members\":[{\"character_id\":\"char-ann\"}]}");

            var store = new HttpPartyStateStore(transport, transport);

            PartyPersistenceResult result = store.Load(Session(Ann));

            Assert.That(result.IsOk, Is.False, "an unauthored policy loaded as a party");
            Assert.That(result.Failure, Is.EqualTo(PartyPersistenceFailure.Corrupt));
            Assert.That(result.Party.Exists, Is.False,
                "a refused load handed back a party anyway");
        }

        [Test]
        public void EveryAuthoredPolicyStillLoads()
        {
            // Refusing the unknown must not have made Personal -- which is zero, and so
            // the easiest one to lose to a truthiness test -- unreadable.
            foreach (PartyLootPolicy policy in new[]
            {
                PartyLootPolicy.Personal, PartyLootPolicy.RoundRobin,
                PartyLootPolicy.NeedGreed,
            })
            {
                var transport = new CannedTransport(
                    "{\"party_id\":\"p-1\",\"leader_character_id\":\"char-ann\","
                    + "\"loot_policy\":" + (int)policy
                    + ",\"round_robin_cursor\":0,\"revision\":2,"
                    + "\"members\":[{\"character_id\":\"char-ann\"}]}");

                PartyPersistenceResult result =
                    new HttpPartyStateStore(transport, transport).Load(Session(Ann));

                Assert.That(result.IsOk, Is.True, policy + " would not load");
                Assert.That(result.Party.LootPolicy, Is.EqualTo(policy));
            }
        }

        [Test]
        public void AnUnreadableTurnIsRefusedAtTheStoreAsWellAsAtTheRegistry()
        {
            var transport = new CannedTransport(
                "{\"party_id\":\"p-1\",\"leader_character_id\":\"char-ann\","
                + "\"loot_policy\":1,\"round_robin_cursor\":5,\"revision\":2,"
                + "\"members\":[{\"character_id\":\"char-ann\"},"
                + "{\"character_id\":\"char-ben\"}]}");

            PartyPersistenceResult result =
                new HttpPartyStateStore(transport, transport).Load(Session(Ann));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Failure, Is.EqualTo(PartyPersistenceFailure.Corrupt));
        }

        [Test]
        public void TheTurnGoesOutOnTheWireWithTheParty()
        {
            var transport = new CannedTransport("{\"revision\":4}");

            new HttpPartyStateStore(transport, transport).Save(Session(Ann),
                new PersistedParty(new PartyId("p-1"), Ann, PartyLootPolicy.RoundRobin,
                    new[] { Ann, Ben }, 3, 1));

            Assert.That(transport.Sent, Does.Contain("\"round_robin_cursor\":1"),
                "the turn was not sent");
        }
    

        // ---- a turn is not spent until it is written down -----------------------------

        [Test]
        public void TheNextTurnCanBeReadWithoutSpendingIt()
        {
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben, Cal);

            registry.Register(party);

            Assert.That(registry.NextRotation(party.Id), Is.EqualTo(1));

            // Asked twice, and still nobody's turn has moved.
            Assert.That(registry.NextRotation(party.Id), Is.EqualTo(1));
            Assert.That(registry.RotationOf(party.Id), Is.Zero,
                "reading the next turn spent it");
        }

        [Test]
        public void TheNextTurnWrapsAtTheEndOfThePartyRatherThanCounting()
        {
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben);

            registry.Register(party);
            registry.AdvanceRotation(party.Id);

            Assert.That(registry.NextRotation(party.Id), Is.Zero,
                "the next turn ran off the end of the party");
        }

        [Test]
        public void ACommittedTurnMovesTheRuntimeCursorExactlyOnce()
        {
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            Assert.That(registry.TryCommitNextRotation(party.Id).IsOk, Is.True);

            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(1));

            // And storage agrees, which is the only reason the runtime number moved.
            Assert.That(new WorldPartyRegistry().Restore(Session(Ben), Ben, store), Is.Not.Null);
            Assert.That(store.Load(Session(Ben)).Party.Cursor, Is.EqualTo(1));
        }

        [Test]
        public void AFailedWriteLeavesTheRuntimeCursorExactlyWhereItWas()
        {
            // The defect this gate closes: before it, the turn moved in memory and the
            // failure was only logged, so a restart offered the same member the same turn.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            store.Broken = true;

            PartyPersistenceResult refused = registry.TryCommitNextRotation(party.Id);

            Assert.That(refused.IsOk, Is.False);
            Assert.That(registry.RotationOf(party.Id), Is.Zero,
                "the runtime turn ran ahead of the durable one");

            store.Broken = false;

            Assert.That(store.Load(Session(Ann)).Party.Cursor, Is.Zero,
                "storage moved despite the refusal");
        }

        [Test]
        public void AStaleRevisionIsNotSuccessAndSpendsNoTurn()
        {
            // Somebody else wrote this party first. Overwriting their cursor because this
            // world wanted a turn would silently discard whatever they recorded.
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 4));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            store.RefuseSavesWith = PartyPersistenceFailure.StaleRevision;

            PartyPersistenceResult refused = registry.TryCommitNextRotation(party.Id);

            Assert.That(refused.IsOk, Is.False);
            Assert.That(refused.Failure, Is.EqualTo(PartyPersistenceFailure.StaleRevision));
            Assert.That(registry.RotationOf(party.Id), Is.Zero);
        }

        [Test]
        public void RetryingACommitDoesNotAdvanceTheTurnTwice()
        {
            var store = new FakeStore();
            var registry = new WorldPartyRegistry();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben, Cal }, 1));

            PartyState party = registry.Restore(Session(Ann), Ann, store);

            store.Broken = true;

            // Three refused attempts, as a world retrying every few seconds would make.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Assert.That(registry.TryCommitNextRotation(party.Id).IsOk, Is.False);
            }

            Assert.That(registry.RotationOf(party.Id), Is.Zero);

            store.Broken = false;

            Assert.That(registry.TryCommitNextRotation(party.Id).IsOk, Is.True);

            // One turn for however many attempts it took to write one down.
            Assert.That(registry.RotationOf(party.Id), Is.EqualTo(1),
                "the retries added up into more than one turn");

            Assert.That(new WorldPartyRegistry().Restore(Session(Cal), Cal, store), Is.Not.Null);
            Assert.That(store.Load(Session(Cal)).Party.Cursor, Is.EqualTo(1));
        }

        [Test]
        public void AWorldWithNoStoreCannotCommitATurnAndSaysSo()
        {
            // Better than silently succeeding: a world composed without persistence must
            // not be able to spend a RoundRobin turn it can never write down.
            var registry = new WorldPartyRegistry();
            PartyState party = PartyOf(Ann, Ben);

            registry.Register(party);

            PartyPersistenceResult refused = registry.TryCommitNextRotation(party.Id);

            Assert.That(refused.IsOk, Is.False);
            Assert.That(refused.Failure, Is.EqualTo(PartyPersistenceFailure.Unreachable));
            Assert.That(registry.RotationOf(party.Id), Is.Zero);
        }

        [Test]
        public void AWorldIsDurableExactlyWhenItWasComposedWithAStore()
        {
            // What tells a reward apart from one that must wait. A world with no party
            // store loses its parties when it stops, so there is no durable turn for a
            // runtime one to contradict -- and withholding loot to protect a cursor that
            // does not exist would break every RoundRobin drop in such a world.
            Assert.That(new WorldPartyRegistry().IsDurable, Is.False);
            Assert.That(new WorldPartyRegistry(new FakeStore()).IsDurable, Is.True);

            // A bare registry that has since spoken to a store counts too, which is how a
            // party restored on a member's arrival becomes persistable.
            var late = new WorldPartyRegistry();
            var store = new FakeStore();

            store.Seed(new PersistedParty(new PartyId("p-1"), Ann,
                PartyLootPolicy.RoundRobin, new[] { Ann, Ben }, 1));

            late.Restore(Session(Ann), Ann, store);

            Assert.That(late.IsDurable, Is.True);
        }

        [Test]
        public void CommittingATurnForAPartyThisWorldDoesNotRunIsRefused()
        {
            var registry = new WorldPartyRegistry();

            Assert.That(registry.TryCommitNextRotation(new PartyId("p-nowhere")).IsOk,
                Is.False);

            Assert.That(registry.TryCommitNextRotation(default).IsOk, Is.False);
        }

        [Test]
        public void ThereIsStillOneRotationAndOnePartyDirectory()
        {
            // The durable path must not have grown a second cursor beside the first.
            string[] fields = typeof(WorldPartyRegistry)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.Name.ToLowerInvariant()).ToArray();

            Assert.That(fields.Count(f => f.Contains("rotation") || f.Contains("cursor")),
                Is.EqualTo(1), "a second rotation state appeared: "
                    + string.Join(", ", fields));
        }

        // ---- the problem document separates two different 409s -------------------------

        [Test]
        public void AMemberAlreadyInAnotherPartyIsNotReportedAsALostRace()
        {
            // Both come back as 409. Telling them apart matters because one is worth
            // retrying and the other will be refused for as long as it stays true.
            var transport = new CannedTransport(
                "{\"code\":\"character_already_in_a_party\","
                + "\"message_key\":\"error.party.character_already_in_a_party\","
                + "\"request_id\":\"req-1\"}", 409);

            PartyPersistenceResult result =
                new HttpPartyStateStore(transport, transport).Save(Session(Ann),
                    new PersistedParty(new PartyId("p-1"), Ann, PartyLootPolicy.Personal,
                        new[] { Ann }, 0));

            Assert.That(result.Failure,
                Is.EqualTo(PartyPersistenceFailure.AlreadyInAParty));
        }

        [Test]
        public void APlainConflictIsStillAStaleRevision()
        {
            var transport = new CannedTransport(
                "{\"code\":\"stale_revision\",\"message_key\":\"error.party.stale_revision\","
                + "\"request_id\":\"req-1\"}", 409);

            PartyPersistenceResult result =
                new HttpPartyStateStore(transport, transport).Save(Session(Ann),
                    new PersistedParty(new PartyId("p-1"), Ann, PartyLootPolicy.Personal,
                        new[] { Ann }, 0));

            Assert.That(result.Failure,
                Is.EqualTo(PartyPersistenceFailure.StaleRevision));
        }

        [Test]
        public void AConflictWithNoReadableBodyStillMapsToSomething()
        {
            // The status already said what happened; the code only ever refines it, so an
            // unparseable body must not turn a refusal into an exception.
            var transport = new CannedTransport("not json at all", 409);

            PartyPersistenceResult result =
                new HttpPartyStateStore(transport, transport).Save(Session(Ann),
                    new PersistedParty(new PartyId("p-1"), Ann, PartyLootPolicy.Personal,
                        new[] { Ann }, 0));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Failure,
                Is.EqualTo(PartyPersistenceFailure.StaleRevision));
        }
    }
}
