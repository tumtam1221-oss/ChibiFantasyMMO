using System.IO;
using ChibiFantasy.Client.Combat;
using ChibiFantasy.Client.World;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The first combat presentation: a swing you can see, a blow that lands when it looks
    /// like it lands, a number, a spark, a flash -- and none of it deciding anything.
    /// </summary>
    /// <remarks>
    /// <b>Two kinds of test, on purpose.</b> The timing rules live in
    /// <see cref="MonsterHitQueue"/>, which is plain C# with time as an argument, so they
    /// are asserted directly. The presenters sit on networked objects whose health only the
    /// server can write, so they are held the way this project already holds them: by the
    /// shape of their source -- what they subscribe to, what they read, and what they are
    /// forbidden to touch.
    /// </remarks>
    [TestFixture]
    public sealed class CombatPresentationTests
    {
        private const string CharacterPresenter =
            "Assets/_Game/Scripts/Client/World/CharacterVisualPresenter.cs";

        private const string MonsterPresenter =
            "Assets/_Game/Scripts/Client/World/WorldMonsterPresenter.cs";

        private const string Feedback = "Assets/_Game/Scripts/Client/World/CombatFeedback.cs";

        private const string Entity = "Assets/_Game/Scripts/Network/CharacterNetworkEntity.cs";

        private const float Impact = CombatFeedback.BasicAttackImpactSeconds;

        private static string Source(string path)
        {
            Assert.That(File.Exists(path), path + " is missing");

            return File.ReadAllText(path);
        }

        // ---- the swing ------------------------------------------------------------------

        [Test]
        public void ThePlayerSwingIsDrawnFromTheServersReportAndNothingElse()
        {
            string presenter = Source(CharacterPresenter);

            Assert.That(presenter, Does.Contain("AttackPerformed += OnAttackPerformed"),
                "the presenter no longer listens for the accepted swing");
            Assert.That(presenter, Does.Contain("_facing = _entity.SwingFacing;"),
                "the swing is not turned towards the heading the server accepted it along");

            // the seam carries no result: nothing downstream of it could apply damage
            Assert.That(presenter, Does.Not.Match(@"\.Health\s*=[^=]"),
                "the presentation writes health");
            Assert.That(presenter, Does.Not.Contain("RequestAttack"),
                "the presentation asks for an attack, so drawing one could cause one");
            Assert.That(presenter, Does.Not.Contain("AnimationEvent"),
                "an animation event is not allowed to be the thing that deals damage");
        }

        [Test]
        public void TheSwingDrivesTheParameterTheControllerActuallyReads()
        {
            // The controller's only transitions into and out of the attack state are on the
            // AttackPhase integer. A presenter that pulled the Attack trigger alone drew
            // nothing, for as long as it did, and this is what stops that coming back.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/_Game/Prefabs/Prototype/Proto_Locomotion.controller");

            Assert.That(controller, Is.Not.Null);

            var intoAttack = 0;
            var onTrigger = 0;

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                foreach (UnityEditor.Animations.AnimatorStateTransition t in child.state.transitions)
                {
                    if (t.destinationState == null || t.destinationState.name != "BasicPunch") continue;

                    intoAttack++;

                    foreach (UnityEditor.Animations.AnimatorCondition c in t.conditions)
                    {
                        if (c.parameter == "Attack") onTrigger++;
                    }
                }
            }

            Assert.That(intoAttack, Is.GreaterThan(0), "nothing leads into the attack state");
            Assert.That(onTrigger, Is.EqualTo(0),
                "the controller now reads the Attack trigger; the presenter's phase handling "
                + "and this test both assume it does not");

            string presenter = Source(CharacterPresenter);

            Assert.That(presenter, Does.Contain("SetInteger(AttackPhaseHash, 1)"),
                "the presenter does not raise the phase, so the swing is invisible");
            Assert.That(presenter, Does.Contain("SetInteger(AttackPhaseHash, 0)"),
                "the presenter never lowers the phase, so the swing never ends");
        }

        [Test]
        public void EveryClientThatCanSeeTheCharacterSeesTheSwing()
        {
            // An observer's client draws the same swing as the attacker's. That is a property
            // of the RPC being to observers, and of the presenter not caring who owns it.
            string entity = Source(Entity);

            int rpc = entity.IndexOf("private void ObserversAttackPerformed()", System.StringComparison.Ordinal);

            Assert.That(rpc, Is.GreaterThan(0));

            string above = entity.Substring(System.Math.Max(0, rpc - 400), 400);

            Assert.That(above, Does.Contain("[ObserversRpc]"),
                "the swing is not published to observers, so a second client sees nothing");
            Assert.That(above, Does.Not.Contain("[TargetRpc]"));

            string presenter = Source(CharacterPresenter);
            int handler = presenter.IndexOf("private void OnAttackPerformed(", System.StringComparison.Ordinal);
            string body = presenter.Substring(handler, 900);

            Assert.That(body, Does.Not.Contain("IsOwner"),
                "the swing is gated on ownership, so an observer would not see it");
        }

        [Test]
        public void TheImpactMomentIsTheClipsMeasuredContactFrame()
        {
            // Measured off the rig, not assumed: the fist of MeshyCrossPunch is at its furthest
            // on frame 21 of 26 (the arm locked out from frame 19, plus the one frame of
            // blend-in lag measured in play). Held here so that a re-timed clip has to come
            // back through this number rather than drift away from it. The lock-out hold and
            // the retract take the remaining five frames; the blend into the guard loop does
            // the rest of the return.
            Assert.That(Impact, Is.EqualTo(21f / 30f).Within(1e-4f));
            Assert.That(CombatFeedback.BasicAttackClipSeconds - Impact, Is.EqualTo(5f / 30f).Within(1e-4f),
                "the swing returns to the guard before the lock-out and retract can be seen");
            Assert.That(CombatFeedback.BasicAttackClipSeconds, Is.LessThanOrEqualTo(1.0f),
                "the character is frozen in the swing for longer than the brief allows");
        }

        // ---- the hit, held to the moment of contact ---------------------------------------

        [Test]
        public void ADropInHealthIsDrawnOnceAtTheMomentOfContactWithTheServersFigure()
        {
            var queue = new MonsterHitQueue(40);

            queue.Observe(28, now: 10f, delay: Impact);

            Assert.That(queue.ShownHealth, Is.EqualTo(40), "the bar dropped before the blade landed");
            Assert.That(queue.TryDraw(10f + Impact - 0.01f, out _), Is.False, "drawn early");

            Assert.That(queue.TryDraw(10f + Impact, out DrawnHit hit), Is.True);
            Assert.That(hit.Amount, Is.EqualTo(12), "the number is not what the server took");
            Assert.That(hit.HealthAfter, Is.EqualTo(28));
            Assert.That(hit.Kills, Is.False);
            Assert.That(queue.ShownHealth, Is.EqualTo(28));

            Assert.That(queue.TryDraw(20f, out _), Is.False,
                "one drop in health produced a second presentation");
        }

        [Test]
        public void BlowsAreDrawnInTheOrderTheyLandedAndNoneAreLost()
        {
            var queue = new MonsterHitQueue(100);

            queue.Observe(90, 0f, Impact);
            queue.Observe(75, 0.1f, Impact);
            queue.Observe(70, 0.2f, Impact);

            var drawn = 0;
            var total = 0;

            for (float now = 0f; now < 2f; now += 1f / 60f)
            {
                while (queue.TryDraw(now, out DrawnHit hit))
                {
                    drawn++;
                    total += hit.Amount;
                }
            }

            Assert.That(drawn, Is.EqualTo(3));
            Assert.That(total, Is.EqualTo(30), "the numbers do not add up to the health lost");
            Assert.That(queue.ShownHealth, Is.EqualTo(70));
        }

        [Test]
        public void APileOnFoldsBlowsTogetherRatherThanDroppingAny()
        {
            var queue = new MonsterHitQueue(100);

            for (var i = 1; i <= MonsterHitQueue.Capacity + 2; i++)
            {
                queue.Observe(100 - i * 5, 0f, Impact);
            }

            var total = 0;

            while (queue.TryDraw(5f, out DrawnHit hit)) total += hit.Amount;

            Assert.That(total, Is.EqualTo((MonsterHitQueue.Capacity + 2) * 5),
                "damage went unshown under a pile-on");
            Assert.That(queue.ShownHealth, Is.EqualTo(100 - (MonsterHitQueue.Capacity + 2) * 5));
        }

        [Test]
        public void TheKillingBlowIsDrawnAsADeathAndNothingAfterItIsDrawnAtAll()
        {
            var queue = new MonsterHitQueue(10);

            queue.Observe(0, 0f, Impact);

            Assert.That(queue.ShownDead, Is.False, "shown dead before the blade landed");

            Assert.That(queue.TryDraw(1f, out DrawnHit last), Is.True);
            Assert.That(last.Kills, Is.True);
            Assert.That(last.Amount, Is.EqualTo(10));
            Assert.That(queue.ShownDead, Is.True);

            // the server's word is the server's: a further drop is applied but not drawn
            queue.Observe(-5, 1f, Impact);

            Assert.That(queue.TryDraw(2f, out DrawnHit after), Is.True);
            Assert.That(after.Amount, Is.EqualTo(0), "a corpse flinched");
            Assert.That(after.Kills, Is.False, "a corpse died twice");
            Assert.That(queue.ShownDead, Is.True);
            Assert.That(queue.ShownHealth, Is.EqualTo(0), "the bar went negative");
        }

        [Test]
        public void ARiseInHealthIsShownAtOnceAndForgetsAnyPendingBlows()
        {
            var queue = new MonsterHitQueue(0);

            Assert.That(queue.ShownDead, Is.True);

            // respawn: nothing to wait for
            queue.Observe(40, 0f, Impact);

            Assert.That(queue.ShownHealth, Is.EqualTo(40));
            Assert.That(queue.ShownDead, Is.False);
            Assert.That(queue.PendingCount, Is.EqualTo(0));

            // a blow queued, then a heal: the blow is superseded
            queue.Observe(30, 1f, Impact);
            queue.Observe(40, 1.1f, Impact);

            Assert.That(queue.PendingCount, Is.EqualTo(0));
            Assert.That(queue.ShownHealth, Is.EqualTo(40));
            Assert.That(queue.TryDraw(5f, out _), Is.False);
        }

        [Test]
        public void TheMonsterPresenterDrawsTheHeldHealthAndOutranksHitWithDeath()
        {
            string presenter = Source(MonsterPresenter);

            Assert.That(presenter, Does.Contain("shown.Hits.Observe(monster.Health, Time.time, hold)"),
                "the monster's health is not held to the moment of contact");
            Assert.That(presenter, Does.Contain("Mathf.Clamp01(shown.Hits.ShownHealth / (float)max)"),
                "the bar reads the server's health directly, so it drops before the blade lands");
            Assert.That(presenter, Does.Not.Contain("Mathf.Clamp01(monster.Health / (float)max)"));

            // a killing blow lowers the flag, clears the flinch and stops the hop
            int kill = presenter.IndexOf("if (hit.Kills)", System.StringComparison.Ordinal);

            Assert.That(kill, Is.GreaterThan(0));

            string body = presenter.Substring(kill, 300);

            Assert.That(body, Does.Contain("ResetTrigger(\"Hit\")"));
            Assert.That(body, Does.Contain("SetBool(\"Move\", false)"));
            Assert.That(body, Does.Contain("SetBool(\"Dead\", true)"));

            // and the animator's own graph agrees: nothing leaves Death
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/_Game/Art/Monsters/TrainingSlime/AC_Monster_TrainingSlime.controller");

            foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                if (child.state.name != "Death") continue;

                Assert.That(child.state.transitions.Length, Is.EqualTo(0),
                    "the slime's Death state has a way out, so a dead slime can get up");
            }
        }

        [Test]
        public void TheSlimesBlowOnThePlayerIsDrawnFromTheReplicatedHealthAtOnce()
        {
            // The slime's damage is applied at its own clip's contact frame, so there is
            // nothing to hold back on this side: the drop in the player's health IS the
            // moment of impact, and the number, the flash and the shove are drawn from it.
            string presenter = Source(CharacterPresenter);

            Assert.That(presenter, Does.Contain("private void PresentHitsTaken()"));
            Assert.That(presenter, Does.Contain("int health = _entity.Health;"),
                "the blow taken is not read from the replicated health");
            Assert.That(presenter, Does.Contain("DamageNumberKind.Taken"));
            Assert.That(presenter, Does.Contain("_recoil = RecoilMetres;"));

            // the shove is on the visual root; the network object is never moved
            int recoil = presenter.IndexOf("private void SettleRecoil(", System.StringComparison.Ordinal);
            string body = presenter.Substring(recoil, 700);

            Assert.That(body, Does.Contain("_visualRoot.localPosition"));
            Assert.That(body, Does.Not.Contain("transform.position ="));
            Assert.That(body, Does.Not.Contain("transform.localPosition ="));
        }

        // ---- the feedback itself --------------------------------------------------------

        [Test]
        public void OneShowIsOneNumberAndTheNumberGoesAwayOnItsOwn()
        {
            var host = new GameObject("Feedback");

            try
            {
                var feedback = host.AddComponent<CombatFeedback>();

                feedback.Compose(null);
                feedback.ShowDamage(Vector3.zero, 12, DamageNumberKind.Dealt);

                Assert.That(feedback.NumbersShown, Is.EqualTo(1));
                Assert.That(feedback.LiveNumbers, Is.EqualTo(1));

                feedback.Tick(0.4f);

                Assert.That(feedback.LiveNumbers, Is.EqualTo(1), "gone too soon to be read");

                feedback.Tick(0.6f);

                Assert.That(feedback.LiveNumbers, Is.EqualTo(0), "the number never left");

                // nothing was instantiated per show: the same pool serves a hundred blows
                int before = host.transform.childCount;

                for (var i = 0; i < 100; i++) feedback.ShowDamage(Vector3.zero, 1, DamageNumberKind.Dealt);

                Assert.That(host.transform.childCount, Is.EqualTo(before),
                    "a hundred blows grew the hierarchy, so numbers are not pooled");

                // a blow for nothing is not a number
                int shown = feedback.NumbersShown;

                feedback.ShowDamage(Vector3.zero, 0, DamageNumberKind.Dealt);

                Assert.That(feedback.NumbersShown, Is.EqualTo(shown), "a zero was printed");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheFlashLeavesNoMaterialBehindAndPutsTheColourBack()
        {
            var host = new GameObject("Feedback");
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            try
            {
                var feedback = host.AddComponent<CombatFeedback>();

                feedback.Compose(null);

                var renderer = target.GetComponent<Renderer>();
                Material shared = renderer.sharedMaterial;
                int materials = Resources.FindObjectsOfTypeAll<Material>().Length;

                var block = new MaterialPropertyBlock();

                feedback.Flash(new[] { renderer }, Color.white * 2f, 0.1f);

                Assert.That(feedback.LiveFlashes, Is.EqualTo(1));
                Assert.That(renderer.sharedMaterial, Is.SameAs(shared),
                    "the flash swapped the material");

                // restarting a flash does not stack a second one
                feedback.Flash(new[] { renderer }, Color.white * 2f, 0.1f);

                Assert.That(feedback.LiveFlashes, Is.EqualTo(1));

                feedback.Tick(0.2f);

                Assert.That(feedback.LiveFlashes, Is.EqualTo(0));

                renderer.GetPropertyBlock(block);

                Color restored = block.GetColor("_BaseColor");
                Color rest = shared.HasProperty("_BaseColor") ? shared.GetColor("_BaseColor") : Color.white;

                Assert.That(restored.r, Is.EqualTo(rest.r).Within(1e-3f), "the colour was not put back");
                Assert.That(restored.g, Is.EqualTo(rest.g).Within(1e-3f));
                Assert.That(restored.b, Is.EqualTo(rest.b).Within(1e-3f));

                // no new material was created by the flash; the spark's own runtime material
                // is made once when the pools are built and is accounted for here
                int after = Resources.FindObjectsOfTypeAll<Material>().Length;

                // the pools make exactly two runtime materials, once: one outlined font
                // material every number shares, and the spark's. Neither is per flash.
                Assert.That(after - materials, Is.LessThanOrEqualTo(2),
                    "the flash is instancing materials");

                for (var i = 0; i < 50; i++)
                {
                    feedback.Flash(new[] { renderer }, Color.white * 2f, 0.05f);
                    feedback.Tick(0.1f);
                }

                Assert.That(Resources.FindObjectsOfTypeAll<Material>().Length, Is.EqualTo(after),
                    "fifty flashes leaked materials");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ImpactsArePooledAndSoundsWithNoClipAreSilentNotErrors()
        {
            var host = new GameObject("Feedback");

            try
            {
                var feedback = host.AddComponent<CombatFeedback>();

                feedback.Compose(null);
                feedback.ShowImpact(Vector3.zero, ImpactKind.Physical);

                int children = host.transform.childCount;

                for (var i = 0; i < 40; i++) feedback.ShowImpact(Vector3.one * i, ImpactKind.Soft);

                Assert.That(host.transform.childCount, Is.EqualTo(children),
                    "forty impacts grew the hierarchy, so sparks are not pooled");
                Assert.That(feedback.ImpactsPlayed, Is.EqualTo(41));

                feedback.Play(CombatSound.PlayerSwing);
                feedback.Play(CombatSound.MonsterDeath);

                Assert.That(feedback.SoundsRequested, Is.EqualTo(2));
                Assert.That(feedback.SoundsPlayed, Is.EqualTo(0),
                    "a sound played with no clip authored -- something substituted a placeholder");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheFeedbackDecidesNothing()
        {
            string feedback = Source(Feedback);

            foreach (string forbidden in new[]
                { "ChibiFantasy.Server", "ChibiFantasy.Network", "RequestAttack", "SyncVar",
                  "ServerRpc", "IsServer", ".Health", "AttackResult", "Execute(" })
            {
                Assert.That(feedback, Does.Not.Contain(forbidden),
                    "the feedback reaches into " + forbidden + ", which is not presentation");
            }

            // and it is the same feedback for everybody in the world, ticked in one place
            Assert.That(feedback, Does.Contain("public static CombatFeedback Current"));
            Assert.That(feedback, Does.Contain("private void LateUpdate()"));
            Assert.That(feedback, Does.Not.Contain("AddComponent<CombatFeedback>"));
        }

        [Test]
        public void TheServerComputesTheHeadingAndItIsTheOnlyThingAdded()
        {
            // The heading is worked out where both positions are, once, and is a number. A
            // target id would let a client resolve the heading and a great deal else.
            string pipeline = Source("Assets/_Game/Scripts/Server/ServerCombatPipeline.cs");

            Assert.That(pipeline, Does.Contain("CombatFacing.YawDegrees(resolution.AttackerCombatant.Position"));
            Assert.That(pipeline, Does.Contain("public float FacingDegrees { get; }"));

            string entity = Source(Entity);

            // the message is bare -- a payload on a broadcast is the project's definition of a
            // leak -- and the heading is state, written first
            Assert.That(entity, Does.Contain("public event System.Action AttackPerformed;"));
            Assert.That(entity, Does.Contain("private void ObserversAttackPerformed()"));
            Assert.That(entity, Does.Contain("private readonly SyncVar<float> _swingFacing"));

            int publish = entity.IndexOf("public void ServerPublishAttack(float facingDegrees)", System.StringComparison.Ordinal);
            string body = entity.Substring(publish, 500);

            Assert.That(body.IndexOf("_swingFacing.Value = facingDegrees;", System.StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("ObserversAttackPerformed();", System.StringComparison.Ordinal)),
                "the heading is written after the swing is sent");

            // the arithmetic
            Assert.That(CombatFacing.YawDegrees(new CombatPosition(0, 0, 0), new CombatPosition(0, 0, 1), 7f),
                Is.EqualTo(0f).Within(1e-4f));
            Assert.That(CombatFacing.YawDegrees(new CombatPosition(0, 0, 0), new CombatPosition(1, 0, 0), 7f),
                Is.EqualTo(90f).Within(1e-4f));
            Assert.That(CombatFacing.YawDegrees(new CombatPosition(0, 0, 0), new CombatPosition(-1, 0, 0), 7f),
                Is.EqualTo(-90f).Within(1e-4f));
            Assert.That(CombatFacing.YawDegrees(new CombatPosition(2, 0, 2), new CombatPosition(2, 0, 2), 7f),
                Is.EqualTo(7f), "point-blank has no heading; the fallback keeps the old one");
        }

        // ---- what this gate must not have touched ---------------------------------------

        [Test]
        public void ThePlayersMovementNeverLearnedAboutCombatPresentation()
        {
            foreach (string locked in new[]
                {
                    "Assets/_Game/Scripts/Client/CharacterMovementInput.cs",
                    "Assets/_Game/Scripts/Client/World/WorldPointerInput.cs",
                    "Assets/_Game/Scripts/Client/World/WorldCameraDirector.cs",
                })
            {
                if (!File.Exists(locked)) continue;

                string source = File.ReadAllText(locked);

                foreach (string type in new[]
                    { "CombatFeedback", "MonsterHitQueue", "DamageNumber", "ShowImpact", "CombatSound" })
                {
                    Assert.That(source, Does.Not.Contain(type),
                        locked + " reaches into the combat presentation");
                }
            }

            foreach (string locked in Directory.GetFiles("Assets/_Game/Scripts", "CharacterMovement*.cs",
                SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(locked);

                Assert.That(source, Does.Not.Contain("CombatFeedback"),
                    locked + " reaches into the combat presentation");
            }
        }

        [Test]
        public void TheProductionHookAssetIsWiredAndEmptyRatherThanFilledWithPlaceholders()
        {
            var config = AssetDatabase.LoadAssetAtPath<CombatPresentationConfig>(
                "Assets/_Game/Prefabs/Presentation/CombatPresentationConfig.asset");

            Assert.That(config, Is.Not.Null, "the production combat presentation hooks are missing");

            // no combat audio has been authored; a hook filled with a wrong sound teaches the
            // ear the wrong thing, so empty is the honest state until an asset arrives
            Assert.That(config.attackSfx, Is.Null);
            Assert.That(config.hitSfx, Is.Null);
            Assert.That(config.hurtSfx, Is.Null);
            Assert.That(config.monsterAttackSfx, Is.Null);
            Assert.That(config.deathSfx, Is.Null);
        }
    }
}
