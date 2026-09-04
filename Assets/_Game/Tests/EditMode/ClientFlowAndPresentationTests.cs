using System.Collections.Generic;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Where a player is, and what their screens are told.
    /// </summary>
    /// <remarks>
    /// <b>Two properties carry this suite.</b> That the session decides which screen the
    /// player is on -- so a screen can never strand somebody somewhere they are no longer
    /// entitled to be -- and that what the screens draw is the server's picture rather than
    /// one the client maintains.
    ///
    /// The screens themselves are exercised in PlayMode, where there is a canvas and a real
    /// socket. What is here is the projection and the mapping, which are decisions rather
    /// than rendering.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientFlowAndPresentationTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- the session decides the screen --------------------------------------------------

        [TestCase(SessionState.Unauthenticated, ClientScreen.Login)]
        [TestCase(SessionState.Authenticated, ClientScreen.ServerSelect)]
        [TestCase(SessionState.ServerSelected, ClientScreen.ChannelSelect)]
        [TestCase(SessionState.ChannelSelected, ClientScreen.CharacterSelect)]
        [TestCase(SessionState.CharacterSelected, ClientScreen.CharacterSelect)]
        [TestCase(SessionState.EnteringWorld, ClientScreen.World)]
        [TestCase(SessionState.Active, ClientScreen.World)]
        public void EachSessionStateHasOneScreen(SessionState state, ClientScreen expected)
        {
            Assert.That(ClientFlowCoordinator.ScreenFor(state), Is.EqualTo(expected));
        }

        [TestCase(SessionState.Expired)]
        [TestCase(SessionState.Revoked)]
        public void AnEndedSessionGoesBackToLoginFromAnywhere(SessionState state)
        {
            Assert.That(ClientFlowCoordinator.ScreenFor(state),
                Is.EqualTo(ClientScreen.Login),
                "a player whose session ended must not keep looking at a character list");
        }

        [Test]
        public void AnUnrecognisedStateGoesSomewhereThatRequiresNothing()
        {
            // A value added to the enum later must not leave a player on a screen they may
            // not be entitled to.
            Assert.That(ClientFlowCoordinator.ScreenFor((SessionState)99),
                Is.EqualTo(ClientScreen.Login));
        }

        [Test]
        public void EveryScreenNamesAProductionSceneThatExists()
        {
            foreach (string scene in ClientScenes.All)
            {
                string path = ClientScenes.PathOf(scene);

                Assert.That(System.IO.File.Exists(path), Is.True, "no scene at " + path);
            }
        }

        [Test]
        public void EveryProductionSceneIsInTheBuildAndLoginIsFirst()
        {
            var paths = new List<string>();

            foreach (UnityEditor.EditorBuildSettingsScene scene in
                UnityEditor.EditorBuildSettings.scenes)
            {
                if (scene.enabled) paths.Add(scene.path.Replace('\\', '/'));
            }

            foreach (string scene in ClientScenes.All)
            {
                Assert.That(paths, Contains.Item(ClientScenes.PathOf(scene)),
                    scene + " is not in the build, so the flow cannot reach it");
            }

            Assert.That(paths[0], Is.EqualTo(ClientScenes.PathOf(ClientScenes.Login)),
                "the first scene is what a build starts on");
            Assert.That(paths[0], Does.Not.Contain("SampleScene"));
        }

        [Test]
        public void TheFlowDriverFollowsTheSessionRatherThanRememberingAPath()
        {
            var host = new GameObject("Flow");
            _created.Add(host);

            var driver = host.AddComponent<ClientFlowDriver>();
            driver.LoadScenes = false;

            // Nothing bound at all is the login screen, not a crash.
            driver.Evaluate();

            Assert.That(driver.CurrentScreen, Is.EqualTo(ClientScreen.Login));
            Assert.That(driver.CurrentScene, Is.EqualTo(ClientScenes.Login));
        }

        [Test]
        public void NothingOnTheDriverCanAdvanceASession()
        {
            // Every public member either observes or loads. A method that changed a session
            // would be a second way into the world that skipped the server.
            foreach (System.Reflection.MethodInfo method in typeof(ClientFlowDriver)
                .GetMethods(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                string name = method.Name.ToLowerInvariant();

                Assert.That(name, Does.Not.Contain("login"), method.Name);
                Assert.That(name, Does.Not.Contain("select"), method.Name);
                Assert.That(name, Does.Not.Contain("enter"), method.Name);
                Assert.That(name, Does.Not.Contain("authorise"), method.Name);
            }
        }

        // ---- the inventory a client is shown ---------------------------------------------------

        private static InventorySnapshot Snapshot(int capacity,
            params InventoryItemSnapshot[] items)
        {
            return new InventorySnapshot
            {
                CharacterId = "char-a",
                Capacity = capacity,
                Revision = 4,
                Items = items,
            };
        }

        private static InventoryItemSnapshot Item(string instance, string definition,
            int slot, int quantity = 1, int equipmentSlot = 0, int enhancement = 0,
            int enchants = 0, int cards = 0)
        {
            return new InventoryItemSnapshot
            {
                InstanceId = instance,
                DefinitionId = definition,
                Slot = slot,
                Quantity = quantity,
                EquipmentSlot = equipmentSlot,
                EnhancementLevel = enhancement,
                EnchantCount = enchants,
                CardCount = cards,
                RarityId = string.Empty,
            };
        }

        /// <summary>Projects a snapshot without a network object, which is what a test needs.</summary>
        private static NetworkInventoryPresenter Project(InventorySnapshot snapshot,
            IDefinitionRegistry<ItemDefinition> items = null)
        {
            var presenter = new NetworkInventoryPresenter(items);

            System.Reflection.MethodInfo apply = typeof(NetworkInventoryPresenter)
                .GetMethod("OnInventoryChanged",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);

            apply.Invoke(presenter, new object[] { snapshot });

            return presenter;
        }

        [Test]
        public void TheBagIsProjectedSlotForSlotIncludingTheEmptyOnes()
        {
            NetworkInventoryPresenter presenter = Project(Snapshot(6,
                Item("item-1", "item.coin", 0, 25),
                Item("item-2", "item.sword", 4)));

            Assert.That(presenter.Bag, Has.Count.EqualTo(6),
                "a bag is drawn as squares, so the empty ones are part of the picture");
            Assert.That(presenter.Bag[0].InstanceId.Value, Is.EqualTo("item-1"));
            Assert.That(presenter.Bag[0].Quantity, Is.EqualTo(25));
            Assert.That(presenter.Bag[1].IsEmpty, Is.True);
            Assert.That(presenter.Bag[4].InstanceId.Value, Is.EqualTo("item-2"));
        }

        [Test]
        public void TheItemIdentityShownIsTheOneTheServerSent()
        {
            NetworkInventoryPresenter presenter = Project(Snapshot(4,
                Item("server-minted-id", "item.coin", 2, 7)));

            Assert.That(presenter.Bag[2].InstanceId.Value, Is.EqualTo("server-minted-id"),
                "a client that renamed an item could not then ask the server about it");
        }

        [Test]
        public void AWornPieceGoesToThePaperdollAndNotTheBag()
        {
            NetworkInventoryPresenter presenter = Project(Snapshot(4,
                Item("item-1", "item.sword", -1,
                    equipmentSlot: (int)EquipmentSlot.MainHand, enhancement: 7)));

            foreach (ItemSlotViewData square in presenter.Bag)
            {
                Assert.That(square.IsEmpty, Is.True, "a worn piece is in no bag square");
            }

            var found = false;

            foreach (EquipmentSlotViewData worn in presenter.Worn)
            {
                if (worn.Slot != EquipmentSlot.MainHand) continue;

                found = true;

                Assert.That(worn.InstanceId.Value, Is.EqualTo("item-1"));
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void ThePaperdollAlwaysDrawsEverySlot()
        {
            NetworkInventoryPresenter presenter = Project(Snapshot(2));

            Assert.That(presenter.Worn, Has.Count.EqualTo(9),
                "an empty slot is still a square somebody can drop into");

            foreach (EquipmentSlotViewData worn in presenter.Worn)
            {
                Assert.That(worn.IsEmpty, Is.True);
            }
        }

        [Test]
        public void AnUnknownDefinitionStillDrawsAnOccupiedSquare()
        {
            // Content removed by a patch while a save still references it. A square with no
            // name is visible; a silently dropped row hides the problem.
            NetworkInventoryPresenter presenter = Project(Snapshot(2,
                Item("item-1", "item.removed", 0)));

            Assert.That(presenter.Bag[0].IsOccupied, Is.True);
            Assert.That(presenter.Bag[0].DefinitionId.Value, Is.EqualTo("item.removed"));
        }

        [Test]
        public void ASnapshotReplacesTheViewRatherThanAddingToIt()
        {
            var presenter = new NetworkInventoryPresenter(null);

            System.Reflection.MethodInfo apply = typeof(NetworkInventoryPresenter)
                .GetMethod("OnInventoryChanged",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);

            apply.Invoke(presenter, new object[]
            {
                Snapshot(4, Item("item-1", "item.coin", 0, 10)),
            });

            apply.Invoke(presenter, new object[] { Snapshot(4) });

            Assert.That(presenter.Bag[0].IsEmpty, Is.True,
                "a client that merged snapshots would be keeping an inventory of its own");
        }

        [Test]
        public void NothingIsDrawnBeforeTheFirstSnapshot()
        {
            var presenter = new NetworkInventoryPresenter(null);

            Assert.That(presenter.HasSnapshot, Is.False);
            Assert.That(presenter.Bag, Is.Empty);
            Assert.That(presenter.Worn, Is.Empty,
                "an empty bag drawn before anything arrived is not the same as an empty bag");
        }

        [Test]
        public void ARequestWithNothingBoundGoesNowhere()
        {
            var presenter = new NetworkInventoryPresenter(null);

            Assert.That(presenter.RequestEquip(0), Is.False);
            Assert.That(presenter.RequestUnequip(EquipmentSlot.Head), Is.False);
            Assert.That(presenter.RequestMove(0, 1), Is.False);
            Assert.That(presenter.RequestSplit(0, 1, 2), Is.False);
        }

        [Test]
        public void NothingOnThePresenterMutatesAnything()
        {
            // Every public method either projects or asks. A setter for a quantity or a slot
            // would be the client keeping its own inventory.
            foreach (System.Reflection.MethodInfo method in
                typeof(NetworkInventoryPresenter).GetMethods(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly))
            {
                string name = method.Name;

                bool allowed = name.StartsWith("Request") || name == "Bind"
                    || name == "Unbind" || name.StartsWith("get_")
                    || name.StartsWith("add_") || name.StartsWith("remove_");

                Assert.That(allowed, Is.True, name + " is neither a projection nor a request");
            }
        }

        // ---- the heads-up display ----------------------------------------------------------------

        [Test]
        public void AnUnboundHudShowsNothingRatherThanZeroes()
        {
            var presenter = new CharacterHudPresenter();

            Assert.That(presenter.IsBound, Is.False);
            Assert.That(presenter.Read().IsBound, Is.False);
            Assert.That(presenter.Current.HealthLabel, Is.Empty,
                "a health bar reading 0/0 looks like a dead character, not an absent one");
            Assert.That(presenter.Current.LevelLabel, Is.Empty);
        }

        [Test]
        public void HudValuesAreArithmeticOnReplicatedNumbersAndNothingElse()
        {
            var data = (HudViewData)typeof(HudViewData)
                .GetMethod("From", System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { null });

            Assert.That(data.IsBound, Is.False, "a null character binds to nothing");
            Assert.That(data.HealthFraction, Is.Zero);
        }

        [Test]
        public void BindingRefusesACharacterThisClientDoesNotOwn()
        {
            var presenter = new CharacterHudPresenter();

            // A null entity stands in for anything unowned: the binding is by ownership and
            // there is no other way in.
            Assert.That(presenter.Bind(null), Is.False);
            Assert.That(presenter.IsBound, Is.False);
        }

        // ---- what the client can never do ------------------------------------------------------------

        [Test]
        public void NoClientCodeTouchesServerAuthorityOrAuthoritativeState()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);
                string named = file.Replace('\\', '/');

                if (named.Contains("/Prototype/")) continue;

                Assert.That(source, Does.Not.Contain("CharacterInventoryAuthority"), named);
                Assert.That(source, Does.Not.Contain("CharacterMovementAuthority"), named);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), named);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), named);
                Assert.That(source, Does.Not.Contain("ServerPublishState"), named);
                Assert.That(source, Does.Not.Contain("ServerPublishInventory"), named);
                Assert.That(source, Does.Not.Contain("WorldCharacterRegistry"), named);
            }
        }

        [Test]
        public void TheProductionScreensNameNoServerChannelOrCharacter()
        {
            // A hard-coded id is a screen that works on one machine. Every id a screen uses
            // comes from the list the authority returned.
            foreach (string file in new[]
                     {
                         "Assets/_Game/Scripts/Client/UI/SessionScreens.cs",
                         "Assets/_Game/Scripts/Client/UI/WorldScreens.cs",
                         "Assets/_Game/Scripts/Client/UI/ClientFlowDriver.cs",
                     })
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("new ServerId("), file);
                Assert.That(source, Does.Not.Contain("new ChannelId("), file);
                Assert.That(source, Does.Not.Contain("new CharacterId("), file);
                Assert.That(source, Does.Not.Contain("127.0.0.1"), file);
            }
        }

        [Test]
        public void NoScreenEverLogsAnything()
        {
            // A password or a token in a log file is a password or a token on a support
            // ticket. The simplest guarantee is that these files log nothing at all.
            foreach (string file in new[]
                     {
                         "Assets/_Game/Scripts/Client/UI/SessionScreens.cs",
                         "Assets/_Game/Scripts/Client/UI/WorldScreens.cs",
                         "Assets/_Game/Scripts/Client/UI/NetworkInventoryPresenter.cs",
                         "Assets/_Game/Scripts/Client/UI/CharacterHudPresenter.cs",
                     })
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("Debug.Log"), file);
                Assert.That(source, Does.Not.Contain("print("), file);
            }
        }

        [Test]
        public void TheLoginScreenKeepsNoCredentialAndNamesNoTransport()
        {
            string source = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/UI/SessionScreens.cs");

            // It hands the typed values to a delegate the composition supplies and clears
            // the field. There is no stored password and no HTTP anywhere in the screen.
            Assert.That(source, Does.Contain("_password.text = string.Empty"),
                "a password left on screen after a failed attempt ends up in a screenshot");
            Assert.That(source, Does.Not.Contain("UnityWebRequest"));
            Assert.That(source, Does.Not.Contain("HttpAccountApi"));

            foreach (System.Reflection.FieldInfo field in typeof(LoginScreen).GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance))
            {
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(string)),
                    field.Name + " holds a string on the login screen");
            }
        }

        [Test]
        public void NoSessionTokenIsAnywhereInTheScreens()
        {
            foreach (string file in System.IO.Directory.GetFiles(
                "Assets/_Game/Scripts/Client/UI", "*.cs"))
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("SessionToken"),
                    file.Replace('\\', '/') + " can put a token on screen");
            }
        }
    }
}
