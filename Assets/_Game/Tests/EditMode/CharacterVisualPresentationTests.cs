using System.Collections.Generic;
using ChibiFantasy.Client.World;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The decisions the character presentation makes, and the assets it makes them about.
    /// </summary>
    /// <remarks>
    /// <b>Two kinds of test, deliberately together.</b> The rules below are arithmetic and
    /// need no socket; the asset checks below are structural and need no socket either. What
    /// does need one -- a model actually appearing on a replicated character -- is in the
    /// PlayMode suite against real FishNet, because that is the only place it means anything.
    ///
    /// <b>The avatar checks are a regression, not a formality.</b> A humanoid rig whose Chest
    /// stops mapping is a rig that silently retargets wrong: the walk still plays, the
    /// character still moves, and the torso is subtly broken in every frame of the game. It
    /// is exactly the kind of failure nobody notices until it is in a build.
    /// </remarks>
    [TestFixture]
    internal sealed class CharacterVisualPresentationTests
    {
        private const string MaleModel =
            "Assets/_Game/Art/Characters/Production/Male/CHR_Base_Male_LOD0.fbx";

        private const string FemaleModel =
            "Assets/_Game/Art/Characters/Production/Female/CHR_Base_Female_LOD0.fbx";

        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/CharacterVisualCatalogue.asset";

        private const string CharacterPrefab =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string WorldScene = "Assets/_Game/Scenes/Client/GameWorld.unity";

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

        // ---- which model ---------------------------------------------------------------------

        [Test]
        public void EachGenderGetsItsOwnApprovedModel()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.ModelFor(CharacterGender.Male), Is.SameAs(catalogue.Male));
            Assert.That(catalogue.ModelFor(CharacterGender.Female),
                Is.SameAs(catalogue.Female));
            Assert.That(catalogue.Male, Is.Not.SameAs(catalogue.Female),
                "one model for both would make the choice meaningless");
        }

        [Test]
        public void AnUnsetGenderIsNotSilentlyMale()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.ModelFor(CharacterGender.Unspecified),
                Is.SameAs(catalogue.Fallback),
                "a zero-valued enum reading as Male is the bug Unspecified exists to prevent");
        }

        [Test]
        public void AGenderCodeThisBuildDoesNotKnowFallsBackRatherThanGuessing()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(CharacterVisualCatalogue.GenderOf(0),
                Is.EqualTo(CharacterGender.Unspecified));
            Assert.That(CharacterVisualCatalogue.GenderOf(1), Is.EqualTo(CharacterGender.Male));
            Assert.That(CharacterVisualCatalogue.GenderOf(2),
                Is.EqualTo(CharacterGender.Female));

            // An older client meeting a newer server.
            Assert.That(CharacterVisualCatalogue.GenderOf(97),
                Is.EqualTo(CharacterGender.Unspecified));
            Assert.That(catalogue.ModelFor(97), Is.SameAs(catalogue.Fallback));
            Assert.That(CharacterVisualCatalogue.GenderOf(-4),
                Is.EqualTo(CharacterGender.Unspecified));
        }

        [Test]
        public void TheShippedCatalogueNamesTheApprovedAssetsAndTheExistingController()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Male),
                Is.EqualTo(MaleModel));
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Female),
                Is.EqualTo(FemaleModel));

            Assert.That(catalogue.Locomotion, Is.Not.Null,
                "with no controller nothing animates");
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue.Locomotion),
                Is.EqualTo("Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller"),
                "the existing validated controller, not a second one");
        }

        // ---- the shipped assets are actually shipped -----------------------------------------

        [Test]
        public void NothingTheGameShipsResolvesIntoTheValidationFolder()
        {
            // The catalogue and the character prefab are the two roots a running client
            // pulls character art through. Everything either of them reaches, transitively,
            // has to be content a fresh clone actually receives -- and the validation folder
            // is deliberately not that. A reference into it is a model that exists on the
            // machine it was authored on and nowhere else.
            var offenders = new List<string>();

            foreach (string root in new[] { CataloguePath, CharacterPrefab })
            {
                foreach (string dependency in
                    UnityEditor.AssetDatabase.GetDependencies(root, true))
                {
                    if (dependency.Replace("\\", "/").Contains("/Validation/"))
                    {
                        offenders.Add(root + " -> " + dependency);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "a shipped asset reaching into the validation folder is a missing model on "
                + "every machine but one");
        }

        [Test]
        public void EveryCharacterAssetTheGameNeedsExistsWhereItSaysItDoes()
        {
            CharacterVisualCatalogue catalogue = Catalogue();

            Assert.That(catalogue.Male, Is.Not.Null, "no approved male model is configured");
            Assert.That(catalogue.Female, Is.Not.Null,
                "no approved female model is configured");
            Assert.That(catalogue.Fallback, Is.Not.Null);
            Assert.That(catalogue.Locomotion, Is.Not.Null);

            foreach (string path in RequiredCharacterAssets())
            {
                Assert.That(System.IO.File.Exists(path), Is.True, "missing " + path);
                Assert.That(System.IO.File.Exists(path + ".meta"), Is.True,
                    "missing importer settings for " + path
                    + " -- without the .meta the avatar is regenerated and the humanoid "
                    + "mapping is whatever Unity guesses");
                Assert.That(path, Does.Not.Contain("/Validation/"), path);
            }
        }

        [Test]
        public void TheCharacterPrefabStillResolvesTheCatalogueAfterTheAssetsMoved()
        {
            var presenter = Load(CharacterPrefab).GetComponent<CharacterVisualPresenter>();

            var catalogue = new UnityEditor.SerializedObject(presenter)
                .FindProperty("_catalogue").objectReferenceValue
                as CharacterVisualCatalogue;

            Assert.That(catalogue, Is.Not.Null);
            Assert.That(catalogue.Male, Is.Not.Null,
                "the prefab reaches a catalogue whose male model no longer resolves");
            Assert.That(catalogue.Female, Is.Not.Null);

            // And the models it reaches are the humanoids the animator expects.
            Assert.That(catalogue.Male.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Null);
            Assert.That(catalogue.Female.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Null);
        }

        /// <summary>
        /// Every file the shipped character presentation cannot run without.
        /// </summary>
        /// <remarks>The minimum set, not the folder: the two approved models, their body
        /// textures, and the walk clip the existing locomotion controller blends to. The idle
        /// clip and the controller itself already live in tracked content.</remarks>
        private static string[] RequiredCharacterAssets()
        {
            const string production = "Assets/_Game/Art/Characters/Production/";

            return new[]
            {
                production + "Male/CHR_Base_Male_LOD0.fbx",
                production + "Male/Textures/CHR_Base_Male_BodyColor_2K.png",
                production + "Male/Textures/CHR_Base_Male_BodyColor_2K_Retopo.png",
                production + "Female/CHR_Base_Female_LOD0.fbx",
                production + "Female/Textures/CHR_Base_Female_BodyColor_2K.png",
                production + "Female/Textures/CHR_Base_Female_BodyColor_2K_Retopo.png",
                production + "Animation/EXT_Walk_Loop_VALIDATION.anim",
            };
        }

        // ---- walking ------------------------------------------------------------------------

        [Test]
        public void StandingStillIsExactlyZeroAndWalkingIsOne()
        {
            // A whole metre in a whole second, against a reference walk of one metre.
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(1f, 0f, 0f), 1f, 0.05f, 1f),
                Is.EqualTo(1f).Within(0.0001f));

            // A twitch. Fed to a blend tree this is a character shuffling on the spot
            // forever, so it has to be zero rather than nearly zero.
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0.001f, 0f, 0f), 1f, 0.05f,
                1f), Is.Zero);

            Assert.That(CharacterVisualRules.SpeedFor(Vector3.zero, 1f, 0.05f, 1f), Is.Zero);
        }

        [Test]
        public void FallingIsNotWalking()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0f, -5f, 0f), 1f, 0.05f, 1f),
                Is.Zero, "a character dropping down a slope must not run on the spot");
        }

        [Test]
        public void RunningFasterThanTheClipDepictsStillClampsToOne()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(0f, 0f, 40f), 1f, 0.05f, 1f),
                Is.EqualTo(1f), "there is no run animation to blend towards");
        }

        [Test]
        public void ANonAdvancingFrameReportsNothingRatherThanDividingByZero()
        {
            Assert.That(CharacterVisualRules.SpeedFor(new Vector3(5f, 0f, 0f), 0f, 0.05f, 1f),
                Is.Zero);
        }

        // ---- facing --------------------------------------------------------------------------

        [Test]
        public void TheVisualFacesTheWayItIsActuallyMoving()
        {
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 0f, 1f), 1f, 0.05f, 123f),
                Is.EqualTo(0f).Within(0.01f));
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(1f, 0f, 0f), 1f, 0.05f, 123f),
                Is.EqualTo(90f).Within(0.01f));
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 0f, -1f), 1f, 0.05f, 123f),
                Is.EqualTo(180f).Within(0.01f));
        }

        [Test]
        public void StoppingKeepsTheLastFacingRatherThanSnappingToNorth()
        {
            Assert.That(CharacterVisualRules.FacingFor(Vector3.zero, 1f, 0.05f, 217f),
                Is.EqualTo(217f));

            // Height alone is not a direction to face.
            Assert.That(CharacterVisualRules.FacingFor(new Vector3(0f, 3f, 0f), 1f, 0.05f, 217f),
                Is.EqualTo(217f));
        }

        // ---- the snap threshold ------------------------------------------------------------------

        [Test]
        public void ARespawnIsPlacedRatherThanFlownAcrossTheMap()
        {
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 200f), 4f), Is.True);

            // Ordinary movement between two packets is eased.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 0.3f), 4f), Is.False);

            // Exactly at the threshold snaps: the boundary belongs to the safe side.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 4f), 4f), Is.True);

            // Disabled means never, not always.
            Assert.That(CharacterVisualRules.ShouldSnap(Vector3.zero,
                new Vector3(0f, 0f, 900f), 0f), Is.False);
        }

        [Test]
        public void TheProductionMovementComponentUsesThatSameThreshold()
        {
            var host = new GameObject("Snap");
            _created.Add(host);

            host.AddComponent<ChibiFantasy.Network.CharacterNetworkEntity>();

            var input = host.AddComponent<ChibiFantasy.Client.CharacterMovementInput>();

            Assert.That(input.SnapDistance, Is.GreaterThan(0f),
                "a reconnect with no threshold glides the character across the world");
            Assert.That(input.ShouldSnap(new Vector3(0f, 0f, input.SnapDistance + 1f)),
                Is.True);
            Assert.That(input.ShouldSnap(new Vector3(0f, 0f, input.SnapDistance * 0.5f)),
                Is.False);
        }

        // ---- the nameplate ---------------------------------------------------------------------

        [Test]
        public void TheNameplateShowsTheNameAndNothingElse()
        {
            Assert.That(CharacterVisualRules.NameplateFor("Ayla"), Is.EqualTo("Ayla"));
            Assert.That(CharacterVisualRules.NameplateFor("  Ayla  "), Is.EqualTo("Ayla"));
        }

        [Test]
        public void ANamelessCharacterShowsNothingRatherThanAnIdentifier()
        {
            Assert.That(CharacterVisualRules.NameplateFor(null), Is.Empty);
            Assert.That(CharacterVisualRules.NameplateFor(string.Empty), Is.Empty);
            Assert.That(CharacterVisualRules.NameplateFor("   "), Is.Empty,
                "a blank plate is a cosmetic bug; a leaked character id is not");
        }

        [Test]
        public void NothingInThePresentationCanPutAnIdentifierAboveAHead()
        {
            string source = Read(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs")
                + Read("Assets/_Game/Scripts/Client/World/CharacterVisualRules.cs");

            // The nameplate is fed from DisplayName. Any of these reaching it would put an
            // account-locating identifier in every screenshot of the game.
            Assert.That(source, Does.Not.Contain("Character.Value"));
            Assert.That(source, Does.Not.Contain("OwnerId"));
            Assert.That(source, Does.Not.Contain("ClientId"));
            Assert.That(source, Does.Not.Contain("ConnectionId"));
            Assert.That(source, Does.Not.Contain("SessionToken"));
        }

        [Test]
        public void TheNameplateOnlyRewritesItsStringWhenTheNameChanges()
        {
            var host = new GameObject("Plate host");
            _created.Add(host);

            CharacterNameplate plate = CharacterNameplate.Create(host.transform, 1.9f);

            plate.Refresh("Ayla");
            plate.Refresh("Ayla");
            plate.Refresh("Ayla");

            Assert.That(plate.Text, Is.EqualTo("Ayla"));
            Assert.That(plate.WriteCount, Is.EqualTo(1),
                "rebuilding a string per character per frame is how a hundred players "
                + "becomes a garbage collection problem");

            plate.Refresh("Borin");

            Assert.That(plate.WriteCount, Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyNameplateIsHiddenRatherThanDrawnBlank()
        {
            var host = new GameObject("Plate host");
            _created.Add(host);

            CharacterNameplate plate = CharacterNameplate.Create(host.transform, 1.9f);

            plate.Refresh("Ayla");

            Assert.That(plate.gameObject.activeSelf, Is.True);

            plate.Refresh(string.Empty);

            Assert.That(plate.gameObject.activeSelf, Is.False);
        }

        // ---- composition ----------------------------------------------------------------------

        [Test]
        public void TheProductionCharacterPrefabCarriesTheNetworkIdentityAndThePresentation()
        {
            GameObject prefab = Load(CharacterPrefab);

            Assert.That(prefab.GetComponent<FishNet.Object.NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ChibiFantasy.Network.CharacterNetworkEntity>(),
                Is.Not.Null);
            Assert.That(prefab.GetComponent<ChibiFantasy.Client.CharacterMovementInput>(),
                Is.Not.Null, "without this the character never follows the server's position");

            var presenter = prefab.GetComponent<CharacterVisualPresenter>();

            Assert.That(presenter, Is.Not.Null);

            var catalogue = new UnityEditor.SerializedObject(presenter)
                .FindProperty("_catalogue").objectReferenceValue;

            Assert.That(catalogue, Is.Not.Null, "a presenter with no catalogue draws nothing");
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(catalogue),
                Is.EqualTo(CataloguePath));
        }

        [Test]
        public void ThereIsExactlyOneCharacterNetworkObjectAndTheVisualIsNotAnother()
        {
            GameObject prefab = Load(CharacterPrefab);

            Assert.That(prefab.GetComponentsInChildren<FishNet.Object.NetworkObject>(true).Length,
                Is.EqualTo(1), "one identity per character, on the root");
            Assert.That(
                prefab.GetComponentsInChildren<ChibiFantasy.Network.CharacterNetworkEntity>(true)
                    .Length, Is.EqualTo(1));

            // Presentation is a MonoBehaviour. The moment it is a NetworkBehaviour it has a
            // wire of its own, and a client with a wire is a client with an opinion.
            Assert.That(typeof(CharacterVisualPresenter).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
            Assert.That(typeof(WorldCameraDirector).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
            Assert.That(typeof(CharacterNameplate).IsSubclassOf(
                typeof(FishNet.Object.NetworkBehaviour)), Is.False);
        }

        [Test]
        public void TheWorldSceneCanActuallyRender()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(WorldScene,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

            try
            {
                var cameras = new List<Camera>();
                var missing = new List<string>();

                var hud = 0;
                var bags = 0;
                var binders = 0;
                var directors = 0;
                var lights = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    cameras.AddRange(root.GetComponentsInChildren<Camera>(true));
                    lights += root.GetComponentsInChildren<Light>(true).Length;
                    hud += root.GetComponentsInChildren<ChibiFantasy.Client.UI.WorldHudScreen>(
                        true).Length;
                    bags += root.GetComponentsInChildren<ChibiFantasy.Client.UI.InventoryScreen>(
                        true).Length;
                    binders += root
                        .GetComponentsInChildren<ChibiFantasy.Client.UI.WorldPresentationBinder>(
                            true).Length;
                    directors += root.GetComponentsInChildren<WorldCameraDirector>(true).Length;

                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        foreach (Component c in t.GetComponents<Component>())
                        {
                            if (c == null) missing.Add(t.name);
                        }
                    }
                }

                Assert.That(missing, Is.Empty,
                    "a GameObject with a missing script is a screen that silently does nothing");

                Assert.That(cameras.Count, Is.EqualTo(1),
                    "no camera is a black screen and an editor warning; two is a fight");
                Assert.That(cameras[0].gameObject.activeInHierarchy, Is.True);
                Assert.That(cameras[0].GetComponent<
                    ChibiFantasy.Client.Prototype.ProtoThirdPersonCamera>(), Is.Not.Null,
                    "the Phase 07.1 rig, rather than a second camera framework");

                Assert.That(lights, Is.GreaterThanOrEqualTo(1),
                    "an unlit world renders the approved models as silhouettes");

                Assert.That(hud, Is.EqualTo(1));
                Assert.That(bags, Is.EqualTo(1));
                Assert.That(binders, Is.EqualTo(1));
                Assert.That(directors, Is.EqualTo(1), "one camera director, not one per player");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void EveryProductionScreenSceneStillHasItsScreen()
        {
            // The login scene shipped in 18.5 with a missing script, so the whole flow began
            // at a blank screen. Checked for all of them rather than only the one that broke.
            var expected = new Dictionary<string, System.Type>
            {
                { "Login", typeof(ChibiFantasy.Client.UI.LoginScreen) },
                { "ServerSelect", typeof(ChibiFantasy.Client.UI.ServerSelectScreen) },
                { "ChannelSelect", typeof(ChibiFantasy.Client.UI.ChannelSelectScreen) },
                { "CharacterSelect", typeof(ChibiFantasy.Client.UI.CharacterSelectScreen) },
            };

            foreach (KeyValuePair<string, System.Type> pair in expected)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    ChibiFantasy.Client.UI.ClientScenes.PathOf(pair.Key),
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

                try
                {
                    var found = 0;

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        found += root.GetComponentsInChildren(pair.Value, true).Length;

                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        {
                            foreach (Component c in t.GetComponents<Component>())
                            {
                                Assert.That(c, Is.Not.Null,
                                    pair.Key + " has a missing script on " + t.name);
                            }
                        }
                    }

                    Assert.That(found, Is.EqualTo(1), pair.Key + " has no " + pair.Value.Name);
                }
                finally
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // ---- the approved rigs ------------------------------------------------------------------

        [Test]
        public void TheMaleAvatarIsStillAValidHumanoid()
        {
            AssertHumanoid(MaleModel);
        }

        [Test]
        public void TheFemaleAvatarIsStillAValidHumanoid()
        {
            AssertHumanoid(FemaleModel);
        }

        [Test]
        public void TheFemaleRigStillMapsChestExplicitly()
        {
            // The one mapping Unity's auto-mapper has previously dropped on this rig. Losing
            // it does not break the import: it retargets a torso wrongly in every frame of
            // the game, which is not something anybody notices until it ships.
            AssertBoneMapping(FemaleModel, "Chest", "chest");
        }

        [Test]
        public void TheMaleRigStillMapsChestExplicitly()
        {
            AssertBoneMapping(MaleModel, "Chest", "chest");
        }

        [Test]
        public void BothApprovedRigsAgreeOnTheirHumanBoneCount()
        {
            int male = HumanBones(MaleModel).Length;
            int female = HumanBones(FemaleModel).Length;

            Assert.That(male, Is.GreaterThanOrEqualTo(15),
                "a humanoid missing required bones cannot retarget the shared clips");
            Assert.That(female, Is.EqualTo(male),
                "two rigs sharing one animator controller must share a skeleton mapping");
        }

        // ---- guards ------------------------------------------------------------------------------

        [Test]
        public void ThePresentationCannotReachAnyServerAuthority()
        {
            foreach (string file in PresentationFiles())
            {
                string source = Code(file);

                Assert.That(source, Does.Not.Contain("ServerRpc"), file);
                Assert.That(source, Does.Not.Contain("ObserversRpc"), file);
                Assert.That(source, Does.Not.Contain("[Server]"), file);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), file);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), file);
                Assert.That(source, Does.Not.Contain("CharacterMovementAuthority"), file);
                Assert.That(source, Does.Not.Contain("CharacterInventoryAuthority"), file);
                Assert.That(source, Does.Not.Contain("ServerPublishState"), file);
                Assert.That(source, Does.Not.Contain("ServerPublishIdentity"), file);
            }
        }

        [Test]
        public void NoClientFileReachesTheServerAssemblysAuthorities()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                "*.cs", System.IO.SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                Assert.That(source, Does.Not.Contain("using ChibiFantasy.Server"), file);
                Assert.That(source, Does.Not.Contain("ServerCombatPipeline"), file);
                Assert.That(source, Does.Not.Contain("MonsterRewardAuthority"), file);
                Assert.That(source, Does.Not.Contain("WorldCharacterRegistry"), file);
            }
        }

        [Test]
        public void AnimationNeverMovesAnybodyAndNeverAwardsAnything()
        {
            string presenter = Code(
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs");

            // Root motion is a clip writing a transform, which is a client writing its own
            // position one frame at a time.
            Assert.That(presenter, Does.Contain("applyRootMotion = false"));

            // An animation event that damaged, healed, paid or looted would be gameplay
            // decided by a clip's timeline on one machine.
            Assert.That(presenter, Does.Not.Contain("AddComponent<AnimationEvent"));
            Assert.That(presenter, Does.Not.Contain("OnAnimatorMove"));
            Assert.That(presenter, Does.Not.Contain("RequestAttack"));
            Assert.That(presenter, Does.Not.Contain("RequestMove"));
            Assert.That(presenter, Does.Not.Contain("RequestInventoryAction"));
        }

        [Test]
        public void ThePresentationDoesNotSearchOrAllocatePerFrame()
        {
            foreach (string file in PresentationFiles())
            {
                string source = Code(file);

                Assert.That(source, Does.Not.Contain("GameObject.Find"), file);
                Assert.That(source, Does.Not.Contain("FindObjectsOfType"), file);
                Assert.That(source, Does.Not.Contain("FindObjectsByType"), file);

                // Animator parameters are hashed once into a static, never per call.
                Assert.That(source, Does.Not.Contain("SetFloat(\""), file);
                Assert.That(source, Does.Not.Contain("SetBool(\""), file);
            }

            // Camera.main is a tagged search. Once, cached, on the nameplate.
            string plate = Code("Assets/_Game/Scripts/Client/World/CharacterNameplate.cs");

            Assert.That(Occurrences(plate, "Camera.main"), Is.EqualTo(1),
                "found once and cached, not searched for every frame per character");
            Assert.That(plate, Does.Contain("if (_camera == null) _camera = Camera.main"));
        }

        [Test]
        public void EveryClientMonoBehaviourLivesInAFileNamedAfterIt()
        {
            // This is why the login screen shipped in 18.5 as a missing script. Unity writes
            // exactly one MonoScript per .cs file -- for the type whose name matches the file
            // -- so a MonoBehaviour declared beside another one has no asset for a scene to
            // reference. It compiles, it passes every unit test, and the component is simply
            // absent at runtime.
            var offenders = new List<string>();

            foreach (System.Type type in typeof(CharacterVisualPresenter).Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(MonoBehaviour))) continue;
                if (type.IsAbstract || type.IsGenericType) continue;

                string[] matches = System.IO.Directory.GetFiles("Assets/_Game/Scripts/Client",
                    type.Name + ".cs", System.IO.SearchOption.AllDirectories);

                if (matches.Length == 0) offenders.Add(type.FullName);
            }

            Assert.That(offenders, Is.Empty,
                "a MonoBehaviour with no file of its own cannot be put in a scene");
        }

        [Test]
        public void ThereIsOneCameraDirectorAndOneCharacterVisualPresenter()
        {
            string[] files = System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs",
                System.IO.SearchOption.AllDirectories);

            var directors = 0;
            var presenters = 0;

            foreach (string file in files)
            {
                string source = System.IO.File.ReadAllText(file);

                if (source.Contains("class WorldCameraDirector")) directors++;
                if (source.Contains("class CharacterVisualPresenter")) presenters++;
            }

            Assert.That(directors, Is.EqualTo(1), "a second camera owner would fight the first");
            Assert.That(presenters, Is.EqualTo(1),
                "one presenter with a local branch, not a local one and a remote one");
        }

        // ---- helpers ------------------------------------------------------------------------------

        private static string[] PresentationFiles()
        {
            return new[]
            {
                "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs",
                "Assets/_Game/Scripts/Client/World/CharacterVisualRules.cs",
                "Assets/_Game/Scripts/Client/World/CharacterVisualCatalogue.cs",
                "Assets/_Game/Scripts/Client/World/CharacterNameplate.cs",
                "Assets/_Game/Scripts/Client/World/WorldCameraDirector.cs",
            };
        }

        /// <summary>
        /// A file with its comments removed.
        /// </summary>
        /// <remarks>Guards below assert that certain names do not appear. Read whole, a file
        /// that <em>explains</em> why it never calls a ServerRpc would fail a guard about
        /// calling ServerRpcs -- which teaches the next author to stop writing the
        /// explanation, exactly backwards.</remarks>
        private static string Code(string path)
        {
            Assert.That(System.IO.File.Exists(path), Is.True, "no file at " + path);

            var kept = new List<string>();

            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("///") || trimmed.StartsWith("//")) continue;

                kept.Add(line);
            }

            return string.Join(" ", kept);
        }

        private static string Read(string path)
        {
            Assert.That(System.IO.File.Exists(path), Is.True, "no file at " + path);

            return System.IO.File.ReadAllText(path);
        }

        private static int Occurrences(string source, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = source.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }

        private static CharacterVisualCatalogue Catalogue()
        {
            var catalogue = UnityEditor.AssetDatabase
                .LoadAssetAtPath<CharacterVisualCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, "no catalogue at " + CataloguePath);

            return catalogue;
        }

        private static GameObject Load(string path)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(asset, Is.Not.Null, "no asset at " + path);

            return asset;
        }

        private static UnityEditor.ModelImporter Importer(string path)
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(path)
                as UnityEditor.ModelImporter;

            Assert.That(importer, Is.Not.Null, "no model importer at " + path);

            return importer;
        }

        private static HumanBone[] HumanBones(string path)
        {
            HumanBone[] bones = Importer(path).humanDescription.human;

            Assert.That(bones, Is.Not.Null, "no human description on " + path);

            return bones;
        }

        /// <summary>
        /// Asserts the model still imports as a valid humanoid.
        /// </summary>
        /// <remarks>Read from the importer and the Avatar the import produced. Nothing here
        /// reflects into internal scale fields: <c>humanScale</c> has no supported public API
        /// in this editor version, and a test that claimed to check it would be a test that
        /// checked nothing.</remarks>
        private static void AssertHumanoid(string path)
        {
            UnityEditor.ModelImporter importer = Importer(path);

            Assert.That(importer.animationType, Is.EqualTo(UnityEditor.ModelImporterAnimationType.Human),
                path + " is no longer imported as a humanoid");

            var avatars = 0;

            foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var avatar = asset as Avatar;

                if (avatar == null) continue;

                avatars++;

                Assert.That(avatar.isValid, Is.True, path + ": avatar is invalid");
                Assert.That(avatar.isHuman, Is.True, path + ": avatar is not human");
            }

            Assert.That(avatars, Is.EqualTo(1), path + ": expected exactly one avatar");
        }

        private static void AssertBoneMapping(string path, string humanName, string boneName)
        {
            foreach (HumanBone bone in HumanBones(path))
            {
                if (bone.humanName != humanName) continue;

                Assert.That(bone.boneName, Is.EqualTo(boneName),
                    path + ": " + humanName + " maps to '" + bone.boneName + "'");

                return;
            }

            Assert.Fail(path + ": " + humanName + " is not mapped at all -- Unity's "
                + "auto-mapper has dropped it");
        }
    }
}
