using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The Phase 20B weapon-presentation foundation: a weapon model attaches to a character's
    /// hand, the approved unarmed cross punch is untouched, and none of the presentation layer
    /// can reach gameplay.
    /// </summary>
    /// <remarks>
    /// <b>Presentation cannot become gameplay.</b> Several of these read the source of the new
    /// presentation files and assert that no path from a rendered weapon, a cast trigger or a
    /// visual projectile reaches damage, health, the combat pipeline, the network or the PK
    /// gate. That boundary is the whole point of the gate, and a source check is what keeps a
    /// later edit from quietly crossing it.
    /// </remarks>
    internal sealed class WeaponPresentationTests
    {
        private const string MaleModel = "Assets/_Game/Art/Characters/Production/MaleMeshy/CHR_Male_Meshy.fbx";
        private const string FemaleModel = "Assets/_Game/Art/Characters/Production/FemaleMeshy/CHR_Female_Meshy.fbx";
        private const string SwordPrefab = "Assets/_Game/Art/Weapons/Swords/SapphireGuardian/WPN_SapphireGuardian.prefab";
        private const string SwordItem = "item.sapphire_guardian";
        private const string WeaponCat = "Assets/_Game/Prefabs/Presentation/WeaponVisualCatalogue.asset";
        private const string CombatCat = "Assets/_Game/Prefabs/Presentation/CombatPresentationCatalogue.asset";

        private const string SocketSrc = "Assets/_Game/Scripts/Client/World/WeaponSocket.cs";
        private const string WeaponCatSrc = "Assets/_Game/Scripts/Client/World/WeaponVisualCatalogue.cs";
        private const string PresDefSrc = "Assets/_Game/Scripts/Client/World/CombatPresentationDefinition.cs";
        private const string PresCatSrc = "Assets/_Game/Scripts/Client/World/CombatPresentationCatalogue.cs";
        private const string ProjectileSrc = "Assets/_Game/Scripts/Client/World/PresentationProjectile.cs";
        private const string PresenterSrc = "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs";

        // ---- 1-3: the approved cross punch is untouched --------------------------------------

        [Test]
        public void TheUnarmedAttackStillResolvesTheApprovedCrossPunchTiming()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<CombatPresentationCatalogue>(CombatCat);

            Assert.That(catalogue, Is.Not.Null, "the combat presentation catalogue is missing");

            CombatPresentationDefinition unarmed = catalogue.Resolve(default);

            Assert.That(unarmed, Is.Not.Null, "unarmed resolves to nothing");
            Assert.That(unarmed.AnimatorTrigger, Is.EqualTo("Attack"),
                "the unarmed attack no longer uses the cross-punch trigger");

            // Zero means "use the shipped constant": the presenter falls back to CombatFeedback,
            // so the unarmed swing is byte-identical to Phase 19E.
            Assert.That(unarmed.ClipSeconds, Is.EqualTo(0f), "the unarmed clip time overrides the approved constant");
            Assert.That(unarmed.ImpactSeconds, Is.EqualTo(0f), "the unarmed impact time overrides the approved constant");
        }

        [Test]
        public void TheApprovedCrossPunchConstantsAreUnchanged()
        {
            Assert.That(CombatFeedback.BasicAttackImpactSeconds, Is.EqualTo(21f / 30f).Within(1e-6f));
            Assert.That(CombatFeedback.BasicAttackClipSeconds, Is.EqualTo(26f / 30f).Within(1e-6f));
            Assert.That(CombatFeedback.MaxPlaybackRate, Is.EqualTo(1.75f).Within(1e-6f));
        }

        [Test]
        public void TheCrossPunchAspdScalingIsUnchanged()
        {
            float clip = CombatFeedback.BasicAttackClipSeconds;
            float max = CombatFeedback.MaxPlaybackRate;

            Assert.That(AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(100), clip, max),
                Is.EqualTo(1f), "the baseline swing no longer plays as authored");
            Assert.That(AttackSpeed.PlaybackRate(AttackSpeed.IntervalSeconds(50), clip, max),
                Is.EqualTo(1f), "a slow character is played in slow motion");
        }

        // ---- 4-8, 15-16: the weapon model resolves, attaches, and cleans up ------------------

        [Test]
        public void TheMainHandModelReferenceResolvesFromTheCatalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponVisualCatalogue>(WeaponCat);

            Assert.That(catalogue, Is.Not.Null, "the weapon visual catalogue is missing");
            Assert.That(catalogue.TryGet(new DefinitionId(SwordItem), out WeaponVisualCatalogue.Entry e),
                Is.True, "the sword item resolves to no model");
            Assert.That(e.Prefab, Is.Not.Null, "the sword entry points at no prefab");

            // A missing model fails safely rather than throwing.
            Assert.That(catalogue.TryGet(new DefinitionId("item.does_not_exist"), out _), Is.False);
            Assert.That(catalogue.TryGet(default, out _), Is.False);
        }

        [Test]
        public void EquippingAttachesTheWeapon_UnequippingRemovesIt_AndRepeatsDoNotDuplicate()
        {
            RunOnRig(MaleModel, socket =>
            {
                var sword = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPrefab);

                Assert.That(socket.HasSocket, Is.True, "no hand socket was resolved");

                socket.Equip(new DefinitionId(SwordItem), sword, Vector3.zero, Vector3.zero, 1f);

                Assert.That(socket.Weapon, Is.Not.Null, "the sword did not attach");
                Assert.That(socket.Equipped.Value, Is.EqualTo(SwordItem));

                GameObject first = socket.Weapon;

                // Equipping the same weapon again is a no-op: no second sword, same object.
                socket.Equip(new DefinitionId(SwordItem), sword, Vector3.zero, Vector3.zero, 1f);
                socket.Equip(new DefinitionId(SwordItem), sword, Vector3.zero, Vector3.zero, 1f);

                Assert.That(socket.Weapon, Is.SameAs(first), "a repeated equip rebuilt or stacked the weapon");

                // Unequip clears it.
                socket.Unequip();

                Assert.That(socket.Weapon, Is.Null, "the sword was not removed on unequip");
                Assert.That(socket.Equipped.IsValid, Is.False);

                // A missing prefab fails safely (clears rather than throwing).
                Assert.DoesNotThrow(() => socket.Equip(new DefinitionId(SwordItem), null, Vector3.zero, Vector3.zero, 1f));
                Assert.That(socket.Weapon, Is.Null);
            });
        }

        [Test]
        public void TheMaleRightHandSocketResolves()
        {
            RunOnRig(MaleModel, socket => Assert.That(socket.HasSocket, Is.True,
                "the male rig's right hand did not resolve a socket"));
        }

        [Test]
        public void TheFemaleRightHandSocketResolves()
        {
            RunOnRig(FemaleModel, socket => Assert.That(socket.HasSocket, Is.True,
                "the female rig's right hand did not resolve a socket"));
        }

        // ---- 11-14, 17: presentation can never become gameplay -------------------------------

        [Test]
        public void NothingInTheWeaponPresentationLayerTouchesGameplay()
        {
            // The forbidden vocabulary of gameplay: if a presentation file mentions any of
            // these, the boundary this gate exists to hold has been crossed.
            string[] forbidden =
            {
                "ApplyHealthDelta", "CurrentHealth", "CombatCommand", "ServerCombatPipeline",
                "RequestAttack", "PkPolicy", "MonsterDefeat", "BasicDamageFormula",
                "SkillExecutor", "ExperienceReward", ".Damage", "stat.atk",
            };

            foreach (string path in new[] { SocketSrc, WeaponCatSrc, PresDefSrc, PresCatSrc, ProjectileSrc })
            {
                string src = System.IO.File.ReadAllText(path);

                foreach (string bad in forbidden)
                {
                    Assert.That(src, Does.Not.Contain(bad),
                        System.IO.Path.GetFileName(path) + " reaches into gameplay via '" + bad + "'");
                }
            }
        }

        [Test]
        public void ThePresentationProjectileCarriesNoColliderOrDamage()
        {
            var host = new GameObject("proj");

            try
            {
                var p = host.AddComponent<PresentationProjectile>();
                p.Destination = new Vector3(0f, 0f, 5f);

                Assert.That(host.GetComponent<Collider>(), Is.Null,
                    "a presentation projectile has a collider, so it could trigger physics/damage");

                string src = System.IO.File.ReadAllText(ProjectileSrc);

                Assert.That(src, Does.Not.Contain("Collider").And.Not.Contain("OnTrigger").And.Not.Contain("OnCollision"),
                    "the projectile references collision, the first step to dealing damage");
                Assert.That(src, Does.Contain("_maxLifetimeSeconds"),
                    "the projectile has no lifetime backstop, so a stuck one would never clean up");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PresentationSelectionIsDataDrivenNotAClassSwitch()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<CombatPresentationCatalogue>(CombatCat);

            // The sword resolves to its own presentation; anything else falls back to unarmed.
            CombatPresentationDefinition sword = catalogue.Resolve(new DefinitionId(SwordItem));
            CombatPresentationDefinition unarmed = catalogue.Resolve(new DefinitionId("item.nothing"));

            Assert.That(sword, Is.Not.Null);
            Assert.That(unarmed, Is.SameAs(catalogue.Unarmed), "an unknown weapon does not fall back to unarmed");
            Assert.That(sword, Is.Not.SameAs(unarmed), "the sword is not selected by data");

            // The presenter picks by data, never by a hard-coded class or weapon name.
            string presenter = System.IO.File.ReadAllText(PresenterSrc);

            Assert.That(presenter, Does.Not.Contain("class.swordsman").And.Not.Contain("class.archer")
                .And.Not.Contain("Swordsman").And.Not.Contain("== \"item"),
                "the presenter branches on a hard-coded class/weapon rather than the catalogue");
        }

        [Test]
        public void TheCastPathIsPresentationOnly()
        {
            string presenter = System.IO.File.ReadAllText(PresenterSrc);

            int cast = presenter.IndexOf("public void TriggerCast()", System.StringComparison.Ordinal);

            Assert.That(cast, Is.GreaterThan(0), "the cast presentation entry point is gone");

            // The body of TriggerCast only sets an animator trigger -- no gameplay.
            string body = presenter.Substring(cast, 400);

            Assert.That(body, Does.Contain("SetTrigger"), "TriggerCast does not fire the animator");
            Assert.That(body, Does.Not.Contain("Health").And.Not.Contain("Damage").And.Not.Contain("Request"),
                "TriggerCast reaches into gameplay");
        }

        // ---- the equipped "holding" stance (Phase 20B pose correction) -----------------------

        [Test]
        public void TheEquippedStanceOnlyBiasesTheFourArmBones()
        {
            string body = PoseMethods();

            // The only bones the hold pose is allowed to move are the two arms, addressed through
            // the four cached arm transforms.
            foreach (string armField in new[] { "_rUpperArm", "_rLowerArm", "_lUpperArm", "_lLowerArm" })
            {
                Assert.That(body, Does.Contain(armField),
                    "the hold pose no longer biases " + armField);
            }

            // No leg, foot, spine, hips, neck, head or root appears here: the locked
            // movement/posture is never touched by the pose.
            foreach (string forbiddenBone in new[]
                { "Hips", "Spine", "UpperLeg", "LowerLeg", "Foot", "Toe", "Neck", "Head", "Root" })
            {
                Assert.That(body, Does.Not.Contain(forbiddenBone),
                    "the hold pose reaches a locked bone (" + forbiddenBone + ")");
            }

            // The four cached transforms are bound to exactly the four arm humanoid bones --
            // never a leg or the spine.
            string presenter = System.IO.File.ReadAllText(PresenterSrc);

            Assert.That(presenter, Does.Contain("_rUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm)"));
            Assert.That(presenter, Does.Contain("_rLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm)"));
            Assert.That(presenter, Does.Contain("_lUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm)"));
            Assert.That(presenter, Does.Contain("_lLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)"));
        }

        [Test]
        public void TheEquippedStanceIsGatedOffWhenUnarmedAttackingGuardingOrDead()
        {
            string body = PoseMethods();

            // The pose is applied only while a weapon is actually in the hand and the character
            // is not doing something the approved animation already owns. Unarmed, the cross
            // punch, the guard and the death pose are therefore untouched.
            Assert.That(body, Does.Contain("_mainHandWeapon.IsValid"),
                "the hold pose is not gated on a weapon being equipped");
            Assert.That(body, Does.Contain("!_swinging"),
                "the hold pose is not suppressed during a swing (it would fight the cross punch)");
            Assert.That(body, Does.Contain("!_guarding"),
                "the hold pose is not suppressed during the guard");
            Assert.That(body, Does.Contain("!_presentedDead"),
                "the hold pose is not suppressed while dead");
            Assert.That(body, Does.Contain("_weaponSocket.Weapon != null"),
                "the hold pose can apply before the weapon model actually exists");
        }

        [Test]
        public void TheEquippedStanceIsAdditiveAndBlended()
        {
            string body = PoseMethods();

            // Additive over the base animation: the bias is multiplied onto the bone's animated
            // localRotation, never assigned absolutely, so idle and run keep playing underneath.
            Assert.That(body, Does.Contain("bone.localRotation = bone.localRotation * Quaternion.Euler"),
                "the hold pose overwrites the animated pose instead of biasing it");

            // Weighted and eased in/out with MoveTowards, so equip/unequip is a blend, not a pop.
            Assert.That(body, Does.Contain("_swordPoseWeight"),
                "the hold pose is not weighted, so it cannot blend in or out");
            Assert.That(body, Does.Contain("MoveTowards"),
                "the hold pose snaps on/off rather than blending");
            Assert.That(body, Does.Contain("holdEuler * weight"),
                "the bias is not scaled by the blend weight");
        }

        [Test]
        public void TheEquippedStanceCannotAffectGameplay()
        {
            string body = PoseMethods();

            // The stance is pure presentation: it reads a few presentation flags and writes only
            // bone rotations. It must never touch health, damage, the combat pipeline, movement
            // speed, the network or the root/position.
            foreach (string bad in new[]
            {
                "Health", "Damage", "Request", "CombatCommand", "SkillExecutor",
                "MeasuredSpeed", "Speed01", "_shownSpeed", "transform.position",
                "transform.Translate", "Rigidbody", "ServerManager", "SyncVar",
            })
            {
                Assert.That(body, Does.Not.Contain(bad),
                    "the hold pose reaches beyond presentation via '" + bad + "'");
            }
        }

        // Extracts the source of ApplyEquippedArmPose + BiasArm so the assertions above are
        // scoped to the pose layer rather than the whole presenter.
        private static string PoseMethods()
        {
            string src = System.IO.File.ReadAllText(PresenterSrc);

            int start = src.IndexOf("private void ApplyEquippedArmPose", System.StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThan(0), "ApplyEquippedArmPose is gone");

            int end = src.IndexOf("private void BuildNameplate", start, System.StringComparison.Ordinal);

            Assert.That(end, Is.GreaterThan(start), "could not bound the pose methods");

            return src.Substring(start, end - start);
        }

        // ---- rig harness --------------------------------------------------------------------

        private static void RunOnRig(string modelPath, System.Action<WeaponSocket> body)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            Assert.That(model, Is.Not.Null, "missing model " + modelPath);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);

            try
            {
                instance.transform.position = new Vector3(0f, 1000f, 0f);

                var animator = instance.GetComponent<Animator>();

                Assert.That(animator, Is.Not.Null, "the model has no animator");

                var socket = instance.AddComponent<WeaponSocket>();
                socket.Bind(animator);

                body(socket);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
