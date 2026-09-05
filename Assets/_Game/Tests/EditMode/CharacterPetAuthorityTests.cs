using System.Collections.Generic;
using System.Reflection;
using ChibiFantasy.Client.World;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Server;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Which pet a character owns, which one is out, and where it stands.
    /// </summary>
    /// <remarks>
    /// <b>Phase 12 is not retested here.</b> Whether a pet may be acquired, what a summon
    /// does to the one already out, how experience becomes a level and which buff a stage
    /// grants are all <see cref="PetService"/>'s, tested in <c>PetTests</c>, and unchanged.
    /// What is tested below is the production layer: that ownership comes from the
    /// connection, that a character has at most one pet out, that both survive a reconnect,
    /// and that the follower is derived rather than stored.
    ///
    /// <b>A pet is owned, not carried.</b> Nothing here puts one in a bag, and the
    /// persistence shape is asserted to keep it that way.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterPetAuthorityTests : CollectibleTestBase
    {
        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public readonly List<PersistedCharacter> Saved = new List<PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId session)
            {
                return Rows.TryGetValue(session.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;
                Saved.Add(c);

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private const string HomeMap = "map.home";
        private const int Connection = 7;
        private const int Other = 8;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private CharacterPetAuthority _authority;
        private CombatTeam _team;
        private readonly List<Object> _local = new List<Object>();

        [SetUp]
        public void SetUpPetAuthority()
        {
            _store = new FakeStore();
            _team = new CombatTeam(1);

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn("spawn.home", HomeMap));

            _players = new WorldCharacterRegistry(_store, spawns, Items, 30, null,
                Pets, Effects);

            _authority = new CharacterPetAuthority(_players, Pets, Items, Effects);
        }

        [TearDown]
        public void TearDownPetAuthority()
        {
            foreach (Object created in _local)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _local.Clear();
        }

        private SpawnPointDefinition PlayerSpawn(string id, string map)
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_map\":{\"_value\":\"" + map + "\"},"
                + "\"_spawnType\":" + (int)SpawnType.Player + ",\"_x\":3,\"_y\":0,\"_z\":-4}",
                spawn);

            _local.Add(spawn);

            return spawn;
        }

        private static PersistedPet Row(string instance, string pet, int level = 1,
            int experience = 0, int stage = 0)
        {
            return new PersistedPet(new InstanceId(instance), new DefinitionId(pet),
                level, experience, stage);
        }

        /// <summary>The row a character loads from, and the spawn that reads it.</summary>
        private void Store(string character, PersistedPet[] pets, string active = null)
        {
            _store.Rows["session-" + character] = new PersistedCharacter(
                new CharacterId(character), new AccountId("acc-" + character),
                new ServerId("srv-1"), character, 2, 5, 0, 100, 50,
                new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                default, null, null, null, 1, null, 30, default, null,
                pets, active == null ? default : new InstanceId(active));
        }

        private WorldSpawnResult Spawn(string character, int connection)
        {
            return _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId("session-" + character),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"), new DefinitionId(HomeMap),
                    new Revision(1), new Revision(1), SessionState.EnteringWorld),
                new ResourceLimits(100, 50), _team);
        }

        private LivingCharacter AddPlayer(string character = "char-a",
            int connection = Connection, PersistedPet[] pets = null, string active = null)
        {
            Store(character, pets, active);

            WorldSpawnResult result = Spawn(character, connection);

            Assert.That(result.IsSpawned, Is.True, result.Detail);

            return result.Character;
        }

        // ---- owning ---------------------------------------------------------------------

        [Test]
        public void Owned_pets_are_restored_from_storage()
        {
            LivingCharacter living = AddPlayer(pets: new[]
            {
                Row("pet-1", PetA, 3, 260, 1), Row("pet-2", PetB)
            });

            Assert.That(living.Pets.Count, Is.EqualTo(2));
            Assert.That(living.TryGetPet(new InstanceId("pet-1"), out PetInstance first),
                Is.True);
            Assert.That(first.DefinitionId, Is.EqualTo(new DefinitionId(PetA)));
            Assert.That(first.Level, Is.EqualTo(3));
            Assert.That(first.Experience, Is.EqualTo(260));
            Assert.That(first.EvolutionStage, Is.EqualTo(1));
        }

        [Test]
        public void A_pet_at_level_one_with_nothing_earned_is_still_a_pet()
        {
            // The Phase 18.16A defect in its pet-shaped form: a pet exists because the
            // character owns one, never because its numbers are interesting.
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            Assert.That(living.Pets.Count, Is.EqualTo(1),
                "a default-valued pet was dropped on the way in");

            PersistedCharacter written = PersistedCharacterMapper.ToPersisted(living.Domain,
                living.Skills, living.Location, living.Server, living.Account,
                living.SaveRevision, living.Inventory, living.Equipment, living.DevilFruit,
                living.Pets, living.Companion);

            Assert.That(written.Pets.Count, Is.EqualTo(1),
                "a default-valued pet was dropped on the way out");
            Assert.That(written.Pets[0].Level, Is.EqualTo(1));
            Assert.That(written.Pets[0].Experience, Is.EqualTo(0));
            Assert.That(written.Pets[0].EvolutionStage, Is.EqualTo(0));
        }

        [Test]
        public void Two_pets_of_one_kind_are_two_pets()
        {
            LivingCharacter living = AddPlayer(pets: new[]
            {
                Row("pet-1", PetA, 4, 400), Row("pet-2", PetA)
            });

            Assert.That(living.Pets.Count, Is.EqualTo(2));

            living.TryGetPet(new InstanceId("pet-1"), out PetInstance first);
            living.TryGetPet(new InstanceId("pet-2"), out PetInstance second);

            Assert.That(ReferenceEquals(first, second), Is.False,
                "two copies of one kind collapsed into one instance");
            Assert.That(first.Level, Is.EqualTo(4));
            Assert.That(second.Level, Is.EqualTo(1));
        }

        [Test]
        public void A_pet_this_world_does_not_know_refuses_the_spawn()
        {
            Store("char-x", new[] { Row("pet-1", "pet.notcontent") });

            WorldSpawnResult result = Spawn("char-x", Connection);

            Assert.That(result.IsSpawned, Is.False);
            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
        }

        [Test]
        public void Two_rows_naming_one_instance_refuse_the_spawn()
        {
            Store("char-x", new[] { Row("pet-1", PetA), Row("pet-1", PetB) });

            WorldSpawnResult result = Spawn("char-x", Connection);

            Assert.That(result.IsSpawned, Is.False);
            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
        }

        [Test]
        public void A_granted_pet_is_minted_by_phase_twelve_and_owned_by_the_asker()
        {
            LivingCharacter living = AddPlayer();

            CharacterPetResult result = _authority.Grant(Connection, new DefinitionId(PetA));

            Assert.That(result.IsAccepted, Is.True, result.ToString());
            Assert.That(living.Pets.Count, Is.EqualTo(1));
            Assert.That(living.Pets[0].Owner, Is.EqualTo(living.Owner));
        }

        // ---- one out at a time ------------------------------------------------------------

        [Test]
        public void Activating_puts_that_pet_out()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            Assert.That(_authority.Activate(Connection, new InstanceId("pet-1")).IsAccepted,
                Is.True);
            Assert.That(living.Companion.IsSummoned, Is.True);
            Assert.That(living.Companion.Summoned.InstanceId,
                Is.EqualTo(new InstanceId("pet-1")));
        }

        [Test]
        public void Only_one_pet_is_ever_out()
        {
            LivingCharacter living = AddPlayer(pets: new[]
            {
                Row("pet-1", PetA), Row("pet-2", PetB)
            });

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.Activate(Connection, new InstanceId("pet-2"));

            // PetCompanionState holds one summoned pet by construction; what matters here
            // is that the second request replaced the first rather than being refused or
            // leaving the first one out beside it.
            Assert.That(living.Companion.Summoned.InstanceId,
                Is.EqualTo(new InstanceId("pet-2")));
        }

        [Test]
        public void Deactivating_puts_the_pet_away()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(_authority.Deactivate(Connection).IsAccepted, Is.True);
            Assert.That(living.Companion.IsSummoned, Is.False);
        }

        [Test]
        public void Putting_away_nothing_is_not_an_error()
        {
            AddPlayer(pets: new[] { Row("pet-1", PetA) });

            // A harmless repeat: the world is already the way they asked for it to be.
            Assert.That(_authority.Deactivate(Connection).IsAccepted, Is.True);
        }

        // ---- whose pet ---------------------------------------------------------------------

        [Test]
        public void A_pet_somebody_else_owns_cannot_be_put_out()
        {
            LivingCharacter mine = AddPlayer("char-a", Connection,
                new[] { Row("pet-1", PetA) });

            AddPlayer("char-b", Other, new[] { Row("pet-b", PetB) });

            CharacterPetResult result =
                _authority.Activate(Connection, new InstanceId("pet-b"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(mine.Companion.IsSummoned, Is.False,
                "a character was handed somebody else's pet");
        }

        [Test]
        public void A_connection_with_no_character_here_gets_nothing()
        {
            AddPlayer();

            CharacterPetResult result =
                _authority.Activate(4242, new InstanceId("pet-1"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(PetRequestRejection.NoCharacter));
        }

        [Test]
        public void A_pet_that_does_not_exist_is_refused()
        {
            AddPlayer(pets: new[] { Row("pet-1", PetA) });

            Assert.That(_authority.Activate(Connection, new InstanceId("pet-nowhere"))
                .IsAccepted, Is.False);
        }

        // ---- across a reconnect --------------------------------------------------------------

        [Test]
        public void The_active_pet_is_restored_on_reconnect()
        {
            LivingCharacter living = AddPlayer(
                pets: new[] { Row("pet-1", PetA), Row("pet-2", PetB) }, active: "pet-2");

            Assert.That(living.Companion.IsSummoned, Is.True,
                "the pet that was out did not come back");
            Assert.That(living.Companion.Summoned.InstanceId,
                Is.EqualTo(new InstanceId("pet-2")));
        }

        [Test]
        public void Owning_pets_with_none_out_restores_none_out()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            Assert.That(living.Companion.IsSummoned, Is.False);
        }

        [Test]
        public void An_active_pet_this_character_does_not_own_refuses_the_spawn()
        {
            // Corrupt rather than guessed at: putting out a different pet, or silently
            // putting out none, both hide a broken row from whoever has to fix it.
            Store("char-x", new[] { Row("pet-1", PetA) }, "pet-somebody-elses");

            WorldSpawnResult result = Spawn("char-x", Connection);

            Assert.That(result.IsSpawned, Is.False);
            Assert.That(result.Reason, Is.EqualTo(WorldSpawnRejection.CorruptCharacter));
        }

        [Test]
        public void What_is_saved_is_the_pets_and_which_one_is_out()
        {
            LivingCharacter living = AddPlayer(
                pets: new[] { Row("pet-1", PetA), Row("pet-2", PetB) });

            _authority.Activate(Connection, new InstanceId("pet-2"));

            _players.Save(living);

            Assert.That(_store.Saved.Count, Is.GreaterThan(0), "nothing was written");

            PersistedCharacter written = _store.Saved[_store.Saved.Count - 1];

            Assert.That(written.Pets.Count, Is.EqualTo(2));
            Assert.That(written.ActivePet, Is.EqualTo(new InstanceId("pet-2")));
        }

        [Test]
        public void Putting_a_pet_away_is_what_gets_saved()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) },
                active: "pet-1");

            _authority.Deactivate(Connection);
            _players.Save(living);

            PersistedCharacter written = _store.Saved[_store.Saved.Count - 1];

            Assert.That(written.Pets.Count, Is.EqualTo(1),
                "the pet was released rather than put away");
            Assert.That(written.ActivePet.IsValid, Is.False);
        }

        // ---- where it stands ---------------------------------------------------------------

        [Test]
        public void A_follower_stands_where_its_owner_does()
        {
            AddPet("pet.grounded", buff: PetVigour, thresholds: new[] { 10 },
                verticalOffset: 0f);

            LivingCharacter living = AddPlayer(pets: new[]
            {
                Row("pet-1", "pet.grounded")
            });

            _authority.Activate(Connection, new InstanceId("pet-1"));

            Assert.That(_authority.TryFollowPoint(Connection, out CombatPosition point),
                Is.True);

            CombatPosition owner = living.Combatant.Position;

            Assert.That(point.X, Is.EqualTo(owner.X));
            Assert.That(point.Z, Is.EqualTo(owner.Z));
        }

        [Test]
        public void A_floating_follower_is_lifted_by_its_own_authored_offset()
        {
            AddPet("pet.floater", buff: PetVigour, thresholds: new[] { 10 },
                verticalOffset: 1.25f);

            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", "pet.floater") });

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _authority.TryFollowPoint(Connection, out CombatPosition point);

            Assert.That(point.Y,
                Is.EqualTo(living.Combatant.Position.Y + 1.25f).Within(0.0001f),
                "the offset came from somewhere other than the definition");
        }

        [Test]
        public void The_follower_moves_with_its_owner_and_can_never_be_left_behind()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            _authority.Activate(Connection, new InstanceId("pet-1"));

            living.Combatant.Position = new CombatPosition(120f, 0f, -95f);

            Assert.That(_authority.TryFollowPoint(Connection, out CombatPosition point),
                Is.True);
            Assert.That(point.X, Is.EqualTo(120f));
            Assert.That(point.Z, Is.EqualTo(-95f));
        }

        [Test]
        public void Nothing_is_out_means_no_follow_point()
        {
            AddPlayer(pets: new[] { Row("pet-1", PetA) });

            Assert.That(_authority.TryFollowPoint(Connection, out CombatPosition _),
                Is.False);
        }

        // ---- what a viewer is told ------------------------------------------------------

        /// <summary>A presenter with a follower root and an owner to follow.</summary>
        private PetPresentationController Presenter(out Transform follower)
        {
            var root = new GameObject("owner");
            var followerObject = new GameObject("follower");

            followerObject.transform.SetParent(root.transform, false);

            _local.Add(root);

            follower = followerObject.transform;

            PetPresentationController controller =
                root.AddComponent<PetPresentationController>();

            SetSerialized(controller, "owner", root.transform);
            SetSerialized(controller, "follower", follower);

            return controller;
        }

        private static void SetSerialized(PetPresentationController controller, string field,
            Transform value)
        {
            typeof(PetPresentationController)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, value);
        }

        [Test]
        public void A_viewer_draws_the_pet_the_server_named()
        {
            AddPet("pet.seen", buff: PetVigour, thresholds: new[] { 10 },
                verticalOffset: 0.5f);

            PetPresentationController presenter = Presenter(out Transform follower);

            presenter.PresentReplicated("pet.seen", Pets);

            Assert.That(presenter.IsOut, Is.True);
            Assert.That(follower.gameObject.activeSelf, Is.True);
            Assert.That(presenter.VerticalOffset, Is.EqualTo(0.5f).Within(0.0001f),
                "the offset came from somewhere other than the authored definition");
        }

        [Test]
        public void A_viewer_told_nothing_is_out_draws_nothing()
        {
            PetPresentationController presenter = Presenter(out Transform follower);

            presenter.PresentReplicated("pet.seen", Pets);
            presenter.PresentReplicated(string.Empty, Pets);

            Assert.That(presenter.IsOut, Is.False);
            Assert.That(follower.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void What_a_viewer_is_told_is_the_definition_and_never_a_position()
        {
            // A pet id is content; a position would be the server's answer arriving as a
            // client's claim. Nothing on the presentation seam accepts one.
            foreach (ParameterInfo parameter in typeof(PetPresentationController)
                .GetMethod("PresentReplicated").GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(Vector3)),
                    "a viewer can be told where somebody's pet is");
                Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(Transform)),
                    "a viewer can be told where somebody's pet is");
            }
        }

        // ---- shape ---------------------------------------------------------------------------

        [Test]
        public void The_authority_stores_no_pet_state_of_its_own()
        {
            // A second ownership model would show up here as somewhere to keep pets. The
            // canonical owner is the character; this class must be discardable between
            // calls without losing anything.
            foreach (FieldInfo field in typeof(CharacterPetAuthority).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.That(typeof(PetInstance).IsAssignableFrom(field.FieldType), Is.False,
                    field.Name + " keeps a pet outside the character that owns it");
                Assert.That(typeof(PetCompanionState).IsAssignableFrom(field.FieldType),
                    Is.False, field.Name + " keeps a second active-pet record");
            }
        }

        [Test]
        public void No_pet_request_can_carry_a_position()
        {
            // A client that could name where its pet stands would be a client moving
            // something the server owns. The follow point is derived, so there is nothing
            // to send -- the out parameter on TryFollowPoint is the answer, not a request.
            foreach (MethodInfo method in typeof(CharacterPetAuthority).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name == "TryFollowPoint") continue;

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType,
                        Is.Not.EqualTo(typeof(CombatPosition)),
                        method.Name + " lets a caller say where a pet is");
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(Vector3)),
                        method.Name + " lets a caller say where a pet is");
                }
            }
        }

        [Test]
        public void A_pet_is_not_an_item()
        {
            LivingCharacter living = AddPlayer(pets: new[] { Row("pet-1", PetA) });

            _authority.Activate(Connection, new InstanceId("pet-1"));
            _players.Save(living);

            PersistedCharacter written = _store.Saved[_store.Saved.Count - 1];

            for (var i = 0; i < written.Items.Count; i++)
            {
                Assert.That(written.Items[i].Item.Value, Does.Not.StartWith("pet."),
                    "a pet was written into the bag");
            }

            Assert.That(living.Inventory.FreeSlots, Is.EqualTo(30),
                "owning a pet consumed a bag slot");
        }

        [Test]
        public void There_is_exactly_one_pet_service()
        {
            // Phase 12 owns every rule. A second system would show up as another type that
            // can summon; the authority is only allowed to be a seam onto this one.
            Assembly gameplay = typeof(PetService).Assembly;
            var summoners = new List<string>();

            foreach (System.Type type in gameplay.GetTypes())
            {
                if (type.GetMethod("TrySummon", BindingFlags.Public | BindingFlags.Static
                    | BindingFlags.Instance) == null)
                {
                    continue;
                }

                summoners.Add(type.FullName);
            }

            Assert.That(summoners, Is.EquivalentTo(new[] { typeof(PetService).FullName }),
                "a second pet system can summon: " + string.Join(", ", summoners.ToArray()));
        }
    }
}
