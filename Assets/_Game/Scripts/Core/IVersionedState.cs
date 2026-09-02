namespace ChibiFantasy.Core
{
    /// <summary>
    /// State that tracks how many times it has changed.
    /// </summary>
    /// <remarks>
    /// The shared root of the state classification. It exists so persistent and runtime
    /// state agree on one change-tracking convention instead of each system inventing its
    /// own, and so a caller can ask "has this changed" without knowing which kind it holds.
    ///
    /// Reuses the existing <see cref="Core.Revision"/>. No second version, revision or
    /// timestamp concept is introduced, because none has a different meaning here.
    /// </remarks>
    public interface IVersionedState
    {
        Revision Revision { get; }
    }
}
