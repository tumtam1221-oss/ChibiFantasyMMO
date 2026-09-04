namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// The production generator: a real random source for a real server.
    /// </summary>
    /// <remarks>
    /// <b>Why this had to exist.</b> Every service that rolls -- drops, enhancement,
    /// enchanting -- takes an <see cref="IRandomResultSource"/> and falls back to
    /// <see cref="AlwaysSucceeds"/> when given none. That default is right for a caller
    /// mid-refactor and catastrophic for a server: a world composed without a generator
    /// would drop every item on every table on every kill, at every authored chance,
    /// including the one-in-ten-million ones. Until this class there was nothing but test
    /// doubles to hand it.
    ///
    /// <b>The comparison is <c>roll &lt; chance</c>, matching
    /// <see cref="ThresholdResultSource"/>.</b> That convention is what makes a chance of
    /// zero impossible and a chance of one certain, and having two implementations disagree
    /// about the boundary would make a one-in-a-million drop behave differently in a test
    /// than in production. <c>NextDouble</c> returns a value in [0,1), so no roll ever
    /// reaches one.
    ///
    /// <b>Double, not float.</b> An authored chance of 1e-7 is below the spacing of a float
    /// near typical roll values, so comparing in float would round a one-in-ten-million drop
    /// into either "never" or something far more common. The chance is widened to double
    /// before the comparison and the roll is drawn as a double.
    ///
    /// <b>Not thread-safe, and deliberately not shared.</b> <c>System.Random</c> is not, and
    /// a lock here would be a lie about how it is used: a world server rolls on its own tick.
    /// Every caller that needs one holds its own.
    ///
    /// <b>Seedable, so a run can be reproduced.</b> An unseeded instance is genuinely
    /// unpredictable; a seeded one replays exactly, which is what an investigation into "the
    /// server dropped this" needs.
    /// </remarks>
    public sealed class SystemRandomSource : IRandomResultSource, IRandomRangeSource
    {
        private readonly System.Random _random;

        /// <summary>A generator seeded from the clock.</summary>
        public SystemRandomSource()
        {
            _random = new System.Random();
        }

        /// <summary>A generator with a known seed, so a sequence can be replayed.</summary>
        public SystemRandomSource(int seed)
        {
            _random = new System.Random(seed);
        }

        public bool Succeeds(float successChance)
        {
            // Zero or less is certain, not impossible: an unauthored chance must not read
            // as "never", or every entry authored before odds existed would stop dropping.
            if (successChance <= 0f) return true;

            if (float.IsNaN(successChance)) return false;

            if (successChance >= 1f) return true;

            return _random.NextDouble() < successChance;
        }

        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;

            // System.Random.Next is exclusive at the top; an authored range of one to three
            // means all three are possible.
            return _random.Next(minInclusive, maxInclusive + 1);
        }
    }
}
