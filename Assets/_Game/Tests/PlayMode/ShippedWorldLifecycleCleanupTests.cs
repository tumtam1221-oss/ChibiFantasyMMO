// Editor-only for the same reason the other shipped-scene fixtures are: it loads the
// committed production scene, because the point is to prove the SHIPPED world does this.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// What a long-running world must not accumulate.
    /// </summary>
    /// <remarks>
    /// <b>A server is judged over hours, not frames.</b> Everything here repeats something
    /// ordinary -- somebody logging in and out, a monster dying and coming back, a reward
    /// being paid -- enough times that anything left behind each cycle would be obvious.
    ///
    /// <b>Counts, not timings.</b> These are leak tests: they assert that the world holds
    /// the same number of things after a hundred cycles as after one. Timing lives in the
    /// EditMode performance fixture, where it can be measured deterministically.
    /// </remarks>
    [TestFixture]
    internal sealed class ShippedWorldLifecycleCleanupTests
    {
        private const string ServerScene = "Assets/_Game/Scenes/World/World_Server.unity";

        private const string StarterMap = "map.harbor_town";
        private const string StarterClass = "class.swordsman";
        private const string Boss = "monster.ancient_slime_king";
        private const string LumiSlime = "pet.lumi_slime";

        private sealed class CharacterStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }

            /// <summary>Drops one application, as the stamp's own transaction does.</summary>
            public void Retire(string rewardId, CharacterId character, InstanceId pet)
            {
                string session = "session-" + character.Value;

                if (!Rows.TryGetValue(session, out PersistedCharacter row)) return;

                var kept = new List<PersistedRewardApplication>();

                foreach (PersistedRewardApplication applied in row.RewardApplications)
                {
                    if (applied.RewardId == rewardId && applied.Pet == pet) continue;

                    kept.Add(applied);
                }

                Rows[session] = new PersistedCharacter(row.Character, row.Account,
                    row.Server, row.Name, row.Gender, row.Level, row.Experience,
                    row.CurrentHealth, row.CurrentMana, row.Class, row.Job, row.Map,
                    row.Spawn, row.Stats, row.Appearance, row.Skills, row.SaveRevision,
                    row.Items, row.InventoryCapacity, row.DevilFruit, row.DevilFruitSource,
                    row.Pets, row.ActivePet, kept);
            }
        }

        private sealed class Outbox : IMonsterRewardOutbox
        {
            private readonly Dictionary<string, PersistedMonsterReward> _byDefeat =
                new Dictionary<string, PersistedMonsterReward>();

            private readonly CharacterStore _store;

            /// <summary>
            /// Wired to the store on purpose.
            /// </summary>
            /// <remarks>The backend retires a reward's application evidence in the same
            /// transaction that stamps its delivery. A double that stamped without retiring
            /// would leave residue this fixture would then blame on the world.</remarks>
            public Outbox(CharacterStore store) => _store = store;

            public IReadOnlyList<PersistedMonsterReward> All() => _byDefeat.Values.ToList();

            public int Pending_ => _byDefeat.Values.Count(r => !r.IsComplete);

            public MonsterRewardOutboxResult Record(SessionId session,
                PersistedMonsterReward reward)
            {
                if (_byDefeat.TryGetValue(reward.Defeat.Value,
                    out PersistedMonsterReward already))
                {
                    return MonsterRewardOutboxResult.Recorded(already.RewardId,
                        already.Revision, true);
                }

                _byDefeat[reward.Defeat.Value] = Copy(reward, reward.Experience,
                    reward.PetExperience, false, 1);

                return MonsterRewardOutboxResult.Recorded(reward.RewardId, 1, false);
            }

            public IReadOnlyList<PersistedMonsterReward> Pending(SessionId session)
            {
                var pending = new List<PersistedMonsterReward>();

                foreach (PersistedMonsterReward stored in _byDefeat.Values)
                {
                    if (!stored.IsComplete) pending.Add(stored);
                }

                return pending;
            }

            public MonsterRewardOutboxResult Progress(SessionId session, string rewardId,
                int revision, IReadOnlyList<CharacterId> experienceDelivered,
                IReadOnlyList<MonsterRewardLootEntry> lootClaimed,
                bool? cursorCommitted, bool? lootPublished, bool complete,
                IReadOnlyList<InstanceId> petExperienceDelivered = null)
            {
                foreach (string key in _byDefeat.Keys.ToList())
                {
                    PersistedMonsterReward stored = _byDefeat[key];

                    if (stored.RewardId != rewardId) continue;

                    if (stored.Revision != revision)
                    {
                        return MonsterRewardOutboxResult.Failed(
                            MonsterRewardOutboxFailure.StaleRevision, "somebody wrote first");
                    }

                    var grants = new List<MonsterRewardGrant>();

                    foreach (MonsterRewardGrant grant in stored.Experience)
                    {
                        bool paidNow = grant.IsDelivered || (experienceDelivered != null
                            && experienceDelivered.Contains(grant.Character));

                        if (paidNow && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Character, default);
                        }

                        grants.Add(new MonsterRewardGrant(grant.Character, grant.Experience,
                            paidNow));
                    }

                    var pets = new List<MonsterRewardPetGrant>();

                    foreach (MonsterRewardPetGrant grant in stored.PetExperience)
                    {
                        bool paid = grant.IsDelivered;

                        for (var i = 0; !paid && petExperienceDelivered != null
                            && i < petExperienceDelivered.Count; i++)
                        {
                            paid = petExperienceDelivered[i] == grant.Pet;
                        }

                        if (paid && !grant.IsDelivered)
                        {
                            _store.Retire(rewardId, grant.Owner, grant.Pet);
                        }

                        pets.Add(new MonsterRewardPetGrant(grant.Owner, grant.Pet,
                            grant.Experience, paid));
                    }

                    _byDefeat[key] = Copy(stored, grants, pets,
                        complete || stored.IsComplete, revision + 1);

                    return MonsterRewardOutboxResult.Recorded(rewardId, revision + 1, false);
                }

                return MonsterRewardOutboxResult.Failed(
                    MonsterRewardOutboxFailure.UnknownReward, "no such reward");
            }

            private static PersistedMonsterReward Copy(PersistedMonsterReward reward,
                IReadOnlyList<MonsterRewardGrant> experience,
                IReadOnlyList<MonsterRewardPetGrant> pets, bool complete, int revision)
            {
                return new PersistedMonsterReward(reward.RewardId, reward.Defeat,
                    reward.Monster, reward.Map, reward.Killer, reward.Loot,
                    reward.LootPolicy, reward.Claimant, reward.X, reward.Y, reward.Z,
                    reward.Party, reward.Cursor, reward.HasCursor,
                    new List<MonsterRewardGrant>(experience),
                    new List<MonsterRewardLootEntry>(reward.Entries),
                    reward.IsCursorCommitted, reward.IsLootPublished, complete, revision,
                    new List<MonsterRewardPetGrant>(pets));
            }
        }

        private sealed class NeverDrops : IRandomResultSource, IRandomRangeSource
        {
            public bool Succeeds(float chance) => false;

            public int Range(int min, int max) => min;
        }

        private sealed class AlwaysAdmits : IWorldSessionAuthority
        {
            public WorldAdmission Admit(WorldJoinClaim claim) => default;

            public bool ConfirmArrival(SessionId session) => true;

            public bool Release(SessionId session) => true;
        }

        private Scene _scene;
        private WorldServerBootstrap _bootstrap;
        private CharacterStore _characters;
        private Outbox _outbox;
        private long _sequence;

        [SetUp]
        public void SetUp()
        {
            _characters = new CharacterStore();
            _outbox = new Outbox(_characters);
            _sequence = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return TearDownWorld();
        }

        // ---- logging in and out, over and over ------------------------------------------------

        [UnityTest]
        public IEnumerator ARoundOfConnectAndDisconnectLeavesTheWorldTheSizeItWas()
        {
            yield return LoadWorld();

            // One pass first, so anything allocated once is already allocated.
            Cycle("char-ann", 1);

            int characters = _bootstrap.Characters.Count;

            Assert.That(characters, Is.Zero, "precondition: nobody is left in the world");

            for (var i = 0; i < 100; i++)
            {
                Cycle("char-ann", 1);

                if (i % 20 == 0) yield return null;
            }

            Assert.That(_bootstrap.Characters.Count, Is.Zero,
                "characters accumulated in the registry across a hundred logins");

            // And the world still works for the next person through the door.
            LivingCharacter last = Admit("char-ann", 1);

            Assert.That(last, Is.Not.Null);
            Assert.That(_bootstrap.Characters.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator APetOwnerLoggingOutLeavesNoFollowerBehind()
        {
            yield return LoadWorld();

            for (var i = 0; i < 25; i++)
            {
                LivingCharacter hero = Admit("char-ann", 1, "pet-1");

                ChibiFantasy.Network.ICharacterPetRequestSink pets = _bootstrap.PetAuthority;

                pets.Activate(1, new InstanceId("pet-1"));

                Assert.That(hero.Companion.IsSummoned, Is.True);

                Assert.That(_bootstrap.Simulation.Release(1).IsOk, Is.True);

                // Nothing in the world should still be able to find a follow point for a
                // character who left.
                Assert.That(_bootstrap.PetAuthority.TryFollowPoint(1,
                    out CombatPosition _), Is.False,
                    "a follower outlived the character who owned it");

                if (i % 5 == 0) yield return null;
            }

            Assert.That(_bootstrap.Characters.Count, Is.Zero);
        }

        // ---- monsters dying and coming back ----------------------------------------------------

        [UnityTest]
        public IEnumerator RepeatedDefeatsDoNotAccumulateMonsterRuntimeState()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1);

            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            // A fresh nest per cycle rather than waiting for the authored respawn, which
            // for a world boss is minutes: what is under test is whether the corpse of the
            // last one is still in the world, not how long the next one takes.
            const int Cycles = 12;

            for (var i = 0; i < Cycles; i++)
            {
                monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                    new CombatPosition(i * 5f, 0f, 0f), 0f, 1, 0f,
                    new DefinitionId(StarterMap)));

                monsters.PopulateAll();

                LivingMonster victim = FirstAlive(monsters);

                Assert.That(victim, Is.Not.Null, "the world stopped spawning at cycle " + i);

                Kill(hero, victim);

                yield return Tick(3);

                // The corpse of every previous cycle must be gone: what is left alive is
                // whatever the nests currently hold, never a pile of bodies.
                Assert.That(monsters.All().Count, Is.LessThanOrEqualTo(i + 1),
                    "dead monsters accumulated by cycle " + i + ": "
                    + monsters.All().Count);
            }

            yield return Tick(5);

            Assert.That(monsters.All().Count, Is.LessThanOrEqualTo(Cycles),
                "the world holds more monsters than its spawn points allow");

            // And every reward those defeats produced finished.
            Assert.That(_outbox.All().Count, Is.EqualTo(Cycles),
                "each defeat did not produce exactly one reward");
            Assert.That(_outbox.Pending_, Is.Zero,
                "rewards accumulated as pending across repeated defeats");
            Assert.That(_bootstrap.Rewards.HeldCount, Is.Zero,
                "the reward authority is still holding finished defeats");
        }

        [UnityTest]
        public IEnumerator RepeatedDefeatsLeaveNoUnfinishedRewardWork()
        {
            yield return LoadWorld();

            LivingCharacter hero = Admit("char-ann", 1, "pet-1");

            _bootstrap.PetAuthority.Activate(1, new InstanceId("pet-1"));

            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            const int Defeats = 10;

            for (var i = 0; i < Defeats; i++)
            {
                monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                    new CombatPosition(i * 5f, 0f, 0f), 0f, 1, 0f,
                    new DefinitionId(StarterMap)));

                monsters.PopulateAll();

                LivingMonster victim = FirstAlive(monsters);

                Assert.That(victim, Is.Not.Null, "the world stopped producing monsters");

                Kill(hero, victim);

                yield return Tick(3);
            }

            Assert.That(_outbox.All().Count, Is.EqualTo(Defeats),
                "ten defeats did not produce ten rewards");

            foreach (PersistedMonsterReward reward in _outbox.All())
            {
                Assert.That(reward.IsComplete, Is.True,
                    "reward " + reward.RewardId + " never finished");
            }

            // The application ledger is retired with the stamps, so nothing is in flight.
            PersistedCharacter stored = _characters.Rows["session-char-ann"];

            Assert.That(stored.RewardApplications.Count, Is.Zero,
                "application evidence outlived the deliveries it belonged to");

            Assert.That(_bootstrap.Rewards.HeldCount, Is.Zero);
        }

        // ---- a bounded soak in the shipped world -----------------------------------------------------

        [UnityTest]
        public IEnumerator ABoundedSoakDoesNotGrowTheWorldWithoutBound()
        {
            yield return LoadWorld();

            var heroes = new List<LivingCharacter>();

            for (var i = 0; i < 4; i++)
            {
                heroes.Add(Admit("char-" + (char)('a' + i), i + 1, "pet-" + i));

                _bootstrap.PetAuthority.Activate(i + 1, new InstanceId("pet-" + i));
            }

            MonsterWorldRuntime monsters = _bootstrap.Simulation.Monsters();

            for (var i = 0; i < 8; i++)
            {
                monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                    new CombatPosition(i * 3f, 0f, i * 3f), 4f, 2, 1f,
                    new DefinitionId(StarterMap)));
            }

            monsters.PopulateAll();

            int monstersAtStart = monsters.All().Count;

            // Movement, combat, defeats, rewards and pet state, repeatedly.
            for (var round = 0; round < 40; round++)
            {
                for (var i = 0; i < heroes.Count; i++)
                {
                    heroes[i].Combatant.Position =
                        new CombatPosition(round % 10, 0f, i * 2f);
                }

                LivingMonster victim = FirstAlive(monsters);

                if (victim == null && round % 4 == 0)
                {
                    // The authored respawn for a boss is long. A new nest keeps the soak
                    // doing combat work rather than idling once the first wave is dead.
                    monsters.AddSpawnPoint(new MonsterSpawnPoint(new DefinitionId(Boss),
                        new CombatPosition(round, 0f, 0f), 0f, 1, 0f,
                        new DefinitionId(StarterMap)));

                    monsters.PopulateAll();

                    victim = FirstAlive(monsters);
                }

                if (victim != null) Kill(heroes[round % heroes.Count], victim);


                if (round % 7 == 0)
                {
                    _bootstrap.PetAuthority.Deactivate(1);
                    _bootstrap.PetAuthority.Activate(1, new InstanceId("pet-0"));
                }

                yield return Tick(3);
            }

            yield return Tick(5);

            Assert.That(monsters.All().Count, Is.LessThanOrEqualTo(monstersAtStart + 10),
                "the soak accumulated monsters: " + monsters.All().Count
                + " from " + monstersAtStart + " and at most ten added nests");

            Assert.That(_bootstrap.Characters.Count, Is.EqualTo(heroes.Count),
                "the soak changed how many characters are in the world");

            Assert.That(_bootstrap.Rewards.HeldCount, Is.Zero,
                "the soak left unfinished reward work behind");
            Assert.That(_outbox.Pending_, Is.Zero,
                "the soak left pending rewards behind");

            foreach (LivingCharacter hero in heroes)
            {
                Assert.That(hero.Status.ActiveCount, Is.LessThanOrEqualTo(2),
                    "status effects accumulated on " + hero.Character);
            }
        }

        // ---- harness ---------------------------------------------------------------------------------

        private void Cycle(string character, int connection)
        {
            Admit(character, connection);

            Assert.That(_bootstrap.Simulation.Release(connection).IsOk, Is.True);
        }

        private static LivingMonster FirstAlive(MonsterWorldRuntime monsters)
        {
            IReadOnlyList<LivingMonster> all = monsters.All();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].IsAlive) return all[i];
            }

            return null;
        }

        private static LivingMonster Living(MonsterWorldRuntime monsters)
        {
            LivingMonster alive = FirstAlive(monsters);

            Assert.That(alive, Is.Not.Null, "nothing alive to kill");

            return alive;
        }

        private IEnumerator LoadWorld()
        {
            _scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ServerScene, new LoadSceneParameters(LoadSceneMode.Additive));

            yield return Until(() => _scene.isLoaded);
            yield return null;

            WorldServerBootstrap[] found = Object.FindObjectsByType<WorldServerBootstrap>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.That(found.Length, Is.EqualTo(1));

            _bootstrap = found[0];

            _bootstrap.StopServer();

            var rolls = new NeverDrops();

            _bootstrap.UseRandom(rolls, rolls);
            _bootstrap.Compose(new AlwaysAdmits(), default, _characters, null, null,
                _outbox);

            Assert.That(_bootstrap.IsWorldReady, Is.True,
                "shipped content faults: " + string.Join("; ", _bootstrap.ContentFaults));

            Assert.That(_bootstrap.StartServer(), Is.True);

            yield return null;
        }

        private IEnumerator TearDownWorld()
        {
            if (_bootstrap != null)
            {
                _bootstrap.StopServer();
                Object.DestroyImmediate(_bootstrap.gameObject);
                _bootstrap = null;
            }

            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private static IEnumerator Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++) yield return null;
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private LivingCharacter Admit(string character, int connection, string pet = null)
        {
            string session = "session-" + character;

            if (!_characters.Rows.ContainsKey(session))
            {
                _characters.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 30, 0, 104, 35,
                    new DefinitionId(StarterClass), default, new DefinitionId(StarterMap),
                    default, new[]
                    {
                        new PersistedStat(new DefinitionId("stat.str"), 10),
                        new PersistedStat(new DefinitionId("stat.vit"), 8),
                        new PersistedStat(new DefinitionId("stat.int"), 3),
                    }, null, null, 1, null, 0, default, null,
                    pet == null
                        ? null
                        : new[]
                        {
                            new PersistedPet(new InstanceId(pet),
                                new DefinitionId(LumiSlime), 1, 0, 0),
                        },
                    default);
            }

            WorldSpawnResult spawned = _bootstrap.Simulation.Admit(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(StarterMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new CombatTeam(1));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            if (_bootstrap.Rewards != null && _bootstrap.RewardOutbox != null)
            {
                _bootstrap.Rewards.RecoverPending();
            }

            return spawned.Character;
        }

        private void Kill(LivingCharacter hero, LivingMonster monster)
        {
            _bootstrap.Simulation.Monsters().TryResolve(monster.Instance,
                out ICombatant target);

            // Melee reach is authored and enforced. A test that swung from across the map
            // would be testing the range check, not the lifecycle.
            hero.Combatant.Position = monster.State.Position;

            for (var i = 0; i < 400 && target.CurrentHealth > 0; i++)
            {
                _bootstrap.Simulation.Combat().Tick(10f);

                ServerCombatResult result = _bootstrap.Simulation.Combat().Execute(
                    hero.ConnectionId, new CombatCommand(hero.Character, monster.Instance,
                        default, 0, ++_sequence));

                if (!result.IsAccepted) break;
            }

            Assert.That(target.CurrentHealth, Is.Zero, "the monster would not die");
        }
    }
}

#endif
