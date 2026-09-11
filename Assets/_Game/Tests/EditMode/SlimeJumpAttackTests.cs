using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The Training Slime's attack as something a player can watch and react to.
    /// </summary>
    /// <remarks>
    /// <b>What was wrong.</b> The attack worked and was correctly spaced, and was still
    /// unreadable: it was 0.77 s long, barely left the ground, travelled almost nowhere, and
    /// -- the part no amount of retiming would have fixed -- the Animator cancelled it into
    /// the Hit state the instant the player landed a blow, which while fighting is
    /// constantly. What a player saw was a twitch.
    ///
    /// <b>What these hold.</b> The properties that make it readable: it is long enough to
    /// follow, it leaves the ground, it travels forward, it returns to exactly where it
    /// started, nothing cancels it, and -- the one that matters for fairness -- the moment
    /// the server deals damage is the frame the player sees contact.
    ///
    /// The numbers below are measured from the shipped clip rather than restated from the
    /// authoring script, so re-authoring the animation without re-binding the server timing
    /// fails here instead of in somebody's face.
    /// </remarks>
    [TestFixture]
    internal sealed class SlimeJumpAttackTests : MonsterTestBase
    {
        private const string Slime = "monster.training_slime";

        private const string Fbx =
            "Assets/_Game/Art/Monsters/TrainingSlime/Monster_TrainingSlime.fbx";

        private const string Controller =
            "Assets/_Game/Art/Monsters/TrainingSlime/AC_Monster_TrainingSlime.controller";

        private const string ContentPath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        /// <summary>The pose the animation makes contact on, as a fraction of the clip.</summary>
        /// <remarks>
        /// Measured from the shipped clip, not carried over. The body is rebuilt and the
        /// attack re-authored, so the old figure is worthless: contact is now frame 24 of a
        /// 40-frame clip at 30 fps, which is 0.800 s in and 0.6154 of the way through.
        /// Recalculating this and re-binding the server to it is the whole point of the two
        /// tests below.
        /// </remarks>
        private const float MeasuredImpactNormalised = 24f / 39f;

        private static AnimationClip Attack()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var clip = asset as AnimationClip;

                if (clip != null && clip.name == "Slime_Attack") return clip;
            }

            Assert.Fail("Slime_Attack is not in " + Fbx);

            return null;
        }

        private static MonsterDefinition Authored()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(ContentPath);

            Assert.That(catalogue, Is.Not.Null);
            Assert.That(catalogue.BuildMonsters().TryGet(new DefinitionId(Slime),
                out MonsterDefinition slime), Is.True);

            return slime;
        }

        /// <summary>Samples the clip and reports where the body is on every frame.</summary>
        private static void Sweep(out float[] lift, out float[] forward, out float[] squash)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/Presentation/Monster/Monster_TrainingSlime.prefab");

            Assert.That(prefab, Is.Not.Null);

            AnimationClip clip = Attack();
            var frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;

            lift = new float[frames];
            forward = new float[frames];
            squash = new float[frames];

            var instance = (GameObject)Object.Instantiate(prefab);

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                Transform body = null;

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Body") body = t;
                }

                Assert.That(body, Is.Not.Null, "the rig no longer has a Body bone");

                for (var i = 0; i < frames; i++)
                {
                    clip.SampleAnimation(instance, clip.length * i / (frames - 1f));

                    lift[i] = body.position.y;
                    forward[i] = body.position.z;
                    squash[i] = body.localScale.y;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---- the animation itself ---------------------------------------------------------

        [Test]
        public void TheAttackIsLongEnoughToFollow()
        {
            AnimationClip clip = Attack();

            Assert.That(clip.length, Is.GreaterThanOrEqualTo(1.05f).And.LessThanOrEqualTo(1.4f),
                "the attack lasts " + clip.length + "s, which is outside the range a player "
                + "can read a whole action in");
        }

        [Test]
        public void TheSlimeVisiblyLeavesTheGround()
        {
            Sweep(out float[] lift, out _, out _);

            var apex = 0f;

            foreach (float y in lift)
            {
                if (y > apex) apex = y;
            }

            Assert.That(apex, Is.GreaterThanOrEqualTo(0.18f),
                "the jump only reaches " + apex + " m, which at gameplay distance is a bump");
            Assert.That(apex, Is.LessThanOrEqualTo(0.40f),
                "it jumps " + apex + " m, which for a knee-high slime is a cartoon");

            // and clearly higher than the ordinary hop, or the attack does not read as a
            // different action from walking
            AnimationClip move = null;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var clip = asset as AnimationClip;

                if (clip != null && clip.name == "Slime_Move") move = clip;
            }

            Assert.That(move, Is.Not.Null, "Slime_Move is missing");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/Presentation/Monster/Monster_TrainingSlime.prefab");

            var instance = (GameObject)Object.Instantiate(prefab);
            var hop = 0f;

            try
            {
                instance.transform.position = Vector3.zero;

                Transform body = null;

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Body") body = t;
                }

                var frames = Mathf.RoundToInt(move.length * move.frameRate) + 1;

                for (var i = 0; i < frames; i++)
                {
                    move.SampleAnimation(instance, move.length * i / (frames - 1f));

                    if (body.position.y > hop) hop = body.position.y;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Assert.That(apex, Is.GreaterThan(hop * 2.5f),
                "the attack reaches " + apex.ToString("0.000") + " m and the walking hop "
                + hop.ToString("0.000") + " m, which is not obviously a different action");
        }

        [Test]
        public void ItTravelsForwardTowardWhateverItIsHitting()
        {
            Sweep(out _, out float[] forward, out _);

            var reach = 0f;

            foreach (float z in forward)
            {
                if (z > reach) reach = z;
            }

            Assert.That(reach, Is.GreaterThanOrEqualTo(0.35f),
                "it lunges " + reach + " m, which does not read as travelling at anybody");
        }

        [Test]
        public void ItRisesBeforeItArrives()
        {
            // An arc, not a slide. The apex has to come before the furthest point forward,
            // or the shape is a horizontal charge with a hop bolted on.
            Sweep(out float[] lift, out float[] forward, out _);

            var apexAt = 0;
            var reachAt = 0;

            for (var i = 0; i < lift.Length; i++)
            {
                if (lift[i] > lift[apexAt]) apexAt = i;
                if (forward[i] > forward[reachAt]) reachAt = i;
            }

            Assert.That(apexAt, Is.LessThan(reachAt),
                "it reaches its furthest point forward at frame " + reachAt + " and its "
                + "highest at frame " + apexAt + ", so it is sliding rather than arcing");
        }

        [Test]
        public void ItPreparesBeforeItJumps()
        {
            // The warning. Without a visible crouch there is nothing to react to.
            Sweep(out float[] lift, out _, out float[] squash);

            var apexAt = 0;

            for (var i = 0; i < lift.Length; i++)
            {
                if (lift[i] > lift[apexAt]) apexAt = i;
            }

            var lowest = 1f;

            for (var i = 0; i < apexAt; i++)
            {
                if (squash[i] < lowest) lowest = squash[i];
            }

            Assert.That(lowest, Is.LessThanOrEqualTo(0.86f),
                "it barely squashes before launching, so there is no anticipation to see");
            Assert.That(lowest, Is.GreaterThanOrEqualTo(0.65f),
                "it squashes to " + lowest + " of its height, which stops being a fat jelly");
        }

        [Test]
        public void ItStaysRoundedThroughout()
        {
            Sweep(out _, out _, out float[] squash);

            var flattest = 9f;
            var tallest = 0f;

            foreach (float s in squash)
            {
                if (s < flattest) flattest = s;
                if (s > tallest) tallest = s;
            }

            Assert.That(flattest, Is.GreaterThanOrEqualTo(0.65f),
                "flattens to " + flattest + ", past the safe deformation limit");
            Assert.That(tallest, Is.LessThanOrEqualTo(1.40f),
                "stretches to " + tallest + ", which reads as a projectile rather than a jelly");
        }

        [Test]
        public void TheLungeLeavesNothingBehindWhenItEnds()
        {
            // The whole reason the lunge is a bone offset rather than root motion. If the
            // clip did not finish exactly where it started, every attack would shunt the
            // model further from the monster it belongs to.
            Sweep(out float[] lift, out float[] forward, out float[] squash);

            int last = lift.Length - 1;

            Assert.That(lift[0], Is.EqualTo(0f).Within(0.0005f));
            Assert.That(forward[0], Is.EqualTo(0f).Within(0.0005f));

            Assert.That(lift[last], Is.EqualTo(0f).Within(0.0005f),
                "the attack ends " + lift[last] + " m off the ground");
            Assert.That(forward[last], Is.EqualTo(0f).Within(0.0005f),
                "the attack ends " + forward[last] + " m in front of where it started, so "
                + "every swing would walk the model away from its monster");
            Assert.That(squash[last], Is.EqualTo(1f).Within(0.005f));
        }

        [Test]
        public void NothingCancelsTheAttackExceptDying()
        {
            // The defect that made every other number pointless: being struck sent the
            // Animator straight to Hit, and a player fighting the slime strikes it
            // constantly, so the attack was cut roughly a frame in, every time.
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                Controller);

            Assert.That(controller, Is.Not.Null);

            var found = false;

            foreach (UnityEditor.Animations.AnimatorControllerLayer layer in controller.layers)
            {
                foreach (UnityEditor.Animations.ChildAnimatorState child in
                    layer.stateMachine.states)
                {
                    if (child.state.name != "Attack") continue;

                    found = true;

                    foreach (UnityEditor.Animations.AnimatorStateTransition transition in
                        child.state.transitions)
                    {
                        Assert.That(transition.destinationState.name, Is.Not.EqualTo("Hit"),
                            "a hit cancels the attack animation, so it can never be watched");

                        Assert.That(transition.hasExitTime, Is.True,
                            "the attack can be left before it finishes");
                        Assert.That(transition.exitTime, Is.GreaterThanOrEqualTo(0.95f),
                            "the attack is cut at " + transition.exitTime + " of its length, "
                            + "which loses the landing and the settle");
                    }
                }
            }

            Assert.That(found, Is.True, "the controller has no Attack state");
        }

        // ---- and the server agreeing with it ------------------------------------------------

        [Test]
        public void TheDamageLandsOnTheFrameContactIsDrawn()
        {
            // The fairness property. The client starts the clip when the monster commits, and
            // the server deals damage one authored windup later -- so the windup has to be
            // the clip's own time to contact. Re-author the animation without re-binding
            // this and the slime hits you before or after it touches you.
            AnimationClip clip = Attack();
            MonsterDefinition slime = Authored();

            float contactAt = clip.length * MeasuredImpactNormalised;

            Assert.That(slime.AttackWindupSeconds, Is.EqualTo(contactAt).Within(0.05f),
                "contact is drawn at " + contactAt.ToString("0.000") + "s but the server "
                + "strikes at " + slime.AttackWindupSeconds + "s");
        }

        [Test]
        public void TheRecoveryCoversTheRestOfTheClip()
        {
            // So the monster is held still until it has finished landing, rather than
            // snapping into a walk with its own landing still playing.
            AnimationClip clip = Attack();
            MonsterDefinition slime = Authored();

            float afterContact = clip.length - clip.length * MeasuredImpactNormalised;

            Assert.That(slime.AttackRecoverySeconds, Is.EqualTo(afterContact).Within(0.08f),
                "there is " + afterContact.ToString("0.000") + "s of animation left after "
                + "contact but the monster is only committed for "
                + slime.AttackRecoverySeconds + "s");
        }

        [Test]
        public void ThereIsAVisiblePauseBetweenAttacks()
        {
            // Duration and frequency are different things, and both have to be readable. The
            // gap measured here is the time the slime is standing still doing nothing between
            // one attack finishing and the next one starting.
            AnimationClip clip = Attack();
            MonsterDefinition slime = Authored();

            float gap = slime.AttackWindupSeconds + slime.AttackCooldownSeconds - clip.length;

            Assert.That(gap, Is.GreaterThanOrEqualTo(1.4f),
                "only " + gap.ToString("0.00") + "s between the end of one attack and the "
                + "start of the next, which reads as chain-jumping");
        }

        [Test]
        public void AMissedAttackTakesNoHealth()
        {
            // What makes moving out of the way worth doing. The range is checked again when
            // the swing resolves, not only when it was decided, so a player who left during
            // the wind-up is not hit from where they used to be.
            MonsterDefinition definition = AddMonster("monster.leaper",
                aggression: MonsterAggressionType.Aggressive, detection: 30f,
                attackRange: 2f, cooldown: 5f, leash: 100f);

            SetPrivate(definition, "_attackWindupSeconds", 0.75f);

            Assert.That(definition.TryGetStat(new DefinitionId(MaxHp), out int health), Is.True);

            var state = new MonsterRuntimeState(new InstanceId("m:leaper"), definition,
                new CombatPosition(0f, 0f, 0f), health, Enemies);
            var ai = new MonsterAiController(state);

            FakeCombatant player = Player(1f, 0f, 0f);
            var candidates = new ICombatant[] { player };

            // It notices, closes, and starts winding up.
            for (var i = 0; i < 12 && !ai.IsCommittedToASwing; i++) ai.Tick(0.05f, candidates);

            Assert.That(ai.IsCommittedToASwing, Is.True, "fixture: it never committed");

            int before = player.CurrentHealth;

            // The player leaves while it is still in the air.
            player.Position = new CombatPosition(40f, 0f, 0f);

            for (var i = 0; i < 40; i++) ai.Tick(0.05f, candidates);

            Assert.That(player.CurrentHealth, Is.EqualTo(before),
                "the swing landed on somebody who was no longer there");
        }

        [Test]
        public void TheFacingIsDecidedByTheServerRatherThanGuessedFromTravel()
        {
            // A monster about to leap is standing still, so the direction it last walked says
            // nothing about what it is aiming at -- and the leap is the one moment facing has
            // to be right.
            string presenter = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Client/World/WorldMonsterPresenter.cs");

            Assert.That(presenter, Does.Contain("monster.Facing"),
                "the presentation still guesses facing instead of reading it");

            string runtime = System.IO.File.ReadAllText(
                "Assets/_Game/Scripts/Server/MonsterWorldRuntime.cs");

            Assert.That(runtime, Does.Contain("IsCommittedToASwing) return"),
                "the facing is not held while a swing is committed, so a monster would spin "
                + "in mid-air to follow a player running around it");
        }
    }
}
