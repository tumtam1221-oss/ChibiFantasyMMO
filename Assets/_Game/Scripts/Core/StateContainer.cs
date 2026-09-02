using System;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Holds a piece of state and makes every change to it explicit and counted.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> Owned instances already carry their own revision and typed
    /// setters. This is for the state that is not an owned instance: current health,
    /// position, an active follow target, a presentation cache. Without a shared container
    /// each of those systems would invent its own mutation and versioning convention.
    ///
    /// <b>Mutation semantics.</b> A change advances the revision exactly once, and only
    /// when it completes. If the delegate throws, the exception propagates unchanged and
    /// the revision is left alone, so a failed change is never mistaken for a real one.
    /// Reading never advances anything.
    ///
    /// <b>Enforcing the boundary.</b> With an immutable <typeparamref name="T"/>, using
    /// <see cref="Replace(Func{T, T})"/>, the boundary is airtight: there is no way to
    /// alter state except through this container, so the revision cannot lie. With a
    /// mutable T, <see cref="Mutate"/> is available for convenience, but a caller holding
    /// the reference from <see cref="State"/> can change it behind the container's back.
    /// The container cannot prevent that without copying on every read. Prefer immutable
    /// state where the revision matters.
    ///
    /// Constrained to reference types on purpose. A struct T would hand out copies, and
    /// container.State.Field++ would silently modify a temporary while the real state and
    /// its revision stayed untouched.
    ///
    /// Plain C#: no Unity object, no scene, no networking, no persistence. The future
    /// Network assembly can read state and turn it into snapshots without this layer
    /// knowing networking exists. Not thread-safe; the expected model is a single
    /// authoritative server loop, and locking can be added if that ever stops being true.
    /// </remarks>
    public sealed class StateContainer<T> : IVersionedState where T : class
    {
        private T _state;
        private Revision _revision;

        public StateContainer(T initialState)
        {
            if (initialState == null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            _state = initialState;
            _revision = Revision.Initial;
        }

        /// <summary>
        /// Read access to the current state.
        /// </summary>
        /// <remarks>Reading does not advance the revision. Treat the result as read-only;
        /// see the type remarks for what the container can and cannot enforce.</remarks>
        public T State => _state;

        public Revision Revision => _revision;

        /// <summary>
        /// Applies a change to the current state, advancing the revision once on success.
        /// </summary>
        /// <remarks>For mutable state. If <paramref name="mutation"/> throws, the exception
        /// is not caught and the revision does not move.</remarks>
        public void Mutate(Action<T> mutation)
        {
            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }

            mutation(_state);
            _revision = _revision.Next();
        }

        /// <summary>Swaps in new state, advancing the revision once.</summary>
        public void Replace(T nextState)
        {
            if (nextState == null)
            {
                throw new ArgumentNullException(nameof(nextState));
            }

            _state = nextState;
            _revision = _revision.Next();
        }

        /// <summary>
        /// Derives new state from the current state, advancing the revision once on success.
        /// </summary>
        /// <remarks>The path intended for immutable state. If the transform throws or
        /// returns null, nothing is replaced and the revision does not move.</remarks>
        public void Replace(Func<T, T> transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            T next = transform(_state);

            if (next == null)
            {
                throw new InvalidOperationException(
                    "A state transform must return state, not null.");
            }

            _state = next;
            _revision = _revision.Next();
        }
    }
}
