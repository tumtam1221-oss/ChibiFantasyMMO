using System;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    public sealed class StateContainerTests
    {
        [Test]
        public void NewContainer_StartsAtInitialRevision()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(5));

            Assert.AreEqual(Revision.Initial, container.Revision);
            Assert.AreEqual(0, container.Revision.Value);
            Assert.AreEqual(5, container.State.Value);
        }

        [Test]
        public void Constructor_RejectsNullState()
        {
            Assert.Throws<ArgumentNullException>(() => new StateContainer<MutableTestState>(null));
        }

        [Test]
        public void Reading_DoesNotAdvanceRevision()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(1));

            int ignored = container.State.Value;
            ignored += container.State.Value;
            Revision after = container.Revision;

            Assert.AreEqual(Revision.Initial, after);
            Assert.AreEqual(2, ignored);
        }

        [Test]
        public void Mutate_AdvancesRevisionExactlyOnce()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(10));

            container.Mutate(state => state.Value += 10);

            Assert.AreEqual(20, container.State.Value);
            Assert.AreEqual(1, container.Revision.Value);
        }

        [Test]
        public void Mutate_AdvancesSequentiallyAcrossCalls()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(0));

            container.Mutate(state => state.Value = 1);
            container.Mutate(state => state.Value = 2);
            container.Mutate(state => state.Value = 3);

            Assert.AreEqual(3, container.Revision.Value);
            Assert.AreEqual(3, container.State.Value);
        }

        [Test]
        public void Mutate_ThatThrows_PropagatesAndLeavesRevisionAlone()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(7));
            Revision before = container.Revision;

            Assert.Throws<InvalidOperationException>(
                () => container.Mutate(state => throw new InvalidOperationException("boom")));

            Assert.AreEqual(before, container.Revision, "A failed change must not count as a change.");
        }

        [Test]
        public void Mutate_RejectsNullDelegate()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(1));

            Assert.Throws<ArgumentNullException>(() => container.Mutate(null));
            Assert.AreEqual(Revision.Initial, container.Revision);
        }

        [Test]
        public void Replace_SwapsStateAndAdvancesOnce()
        {
            var container = new StateContainer<ImmutableTestState>(new ImmutableTestState(1));

            container.Replace(new ImmutableTestState(42));

            Assert.AreEqual(42, container.State.Value);
            Assert.AreEqual(1, container.Revision.Value);
        }

        [Test]
        public void ReplaceWithTransform_AdvancesOnce()
        {
            var container = new StateContainer<ImmutableTestState>(new ImmutableTestState(1));

            container.Replace(state => state.WithValue(state.Value + 4));

            Assert.AreEqual(5, container.State.Value);
            Assert.AreEqual(1, container.Revision.Value);
        }

        [Test]
        public void ReplaceWithTransform_ThatThrows_LeavesStateAndRevisionAlone()
        {
            var original = new ImmutableTestState(9);
            var container = new StateContainer<ImmutableTestState>(original);

            Assert.Throws<InvalidOperationException>(
                () => container.Replace((Func<ImmutableTestState, ImmutableTestState>)(
                    state => throw new InvalidOperationException("boom"))));

            Assert.AreSame(original, container.State);
            Assert.AreEqual(Revision.Initial, container.Revision);
        }

        [Test]
        public void ReplaceWithTransform_ReturningNull_IsRejectedWithoutAdvancing()
        {
            var original = new ImmutableTestState(9);
            var container = new StateContainer<ImmutableTestState>(original);

            Assert.Throws<InvalidOperationException>(() => container.Replace(state => null));

            Assert.AreSame(original, container.State);
            Assert.AreEqual(Revision.Initial, container.Revision);
        }

        [Test]
        public void Replace_RejectsNullArguments()
        {
            var container = new StateContainer<ImmutableTestState>(new ImmutableTestState(1));

            Assert.Throws<ArgumentNullException>(() => container.Replace((ImmutableTestState)null));
            Assert.Throws<ArgumentNullException>(
                () => container.Replace((Func<ImmutableTestState, ImmutableTestState>)null));
            Assert.AreEqual(Revision.Initial, container.Revision);
        }

        [Test]
        public void Container_ReusesTheExistingRevisionType()
        {
            var container = new StateContainer<MutableTestState>(new MutableTestState(1));

            Assert.AreEqual(typeof(Revision), container.Revision.GetType());
            Assert.IsInstanceOf<IVersionedState>(container);
        }

        [Test]
        public void Container_WorksWithoutSceneOrUnityObject()
        {
            var container = new StateContainer<ImmutableTestState>(new ImmutableTestState(3));
            container.Replace(state => state.WithValue(4));

            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(StateContainer<ImmutableTestState>)));
            Assert.AreEqual(4, container.State.Value);
        }
    }
}
