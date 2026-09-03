using System.Collections.Generic;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// Decides whether a chance succeeded.
    /// </summary>
    /// <remarks>
    /// <b>Injected, never ambient.</b> Nothing in Gameplay calls
    /// <c>UnityEngine.Random</c>: this assembly is engine-free, and an ambient generator
    /// would make every enhancement and fusion untestable and unreproducible. A caller
    /// supplies the source, which is exactly the seam a server needs -- when the server
    /// becomes authoritative, it passes its own and the client passes none at all.
    ///
    /// <b>The chance is the argument, not the implementation's business.</b> An
    /// implementation may honour it, ignore it, or replay a recorded sequence. That is what
    /// lets a test force a success at 1% odds and a failure at 99%, and it is why boundary
    /// behaviour is decided here rather than at each call site.
    /// </remarks>
    public interface IRandomResultSource
    {
        /// <summary>
        /// Whether an attempt at the given odds succeeded.
        /// </summary>
        /// <param name="successChance">
        /// Authored probability. Zero or less means certain -- an unauthored chance must
        /// not read as "never", or every item authored before odds existed would be
        /// unusable.
        /// </param>
        bool Succeeds(float successChance);
    }

    /// <summary>
    /// Picks a number in a range.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IRandomResultSource"/> because it answers a different
    /// question: "did it happen" and "how many" are independent rolls, and a drop needs
    /// both. Splitting them keeps each implementation trivial and lets a test force a
    /// certain drop of a specific quantity.
    ///
    /// Inclusive at both ends, because an authored range of one to three means all three
    /// are possible.
    /// </remarks>
    public interface IRandomRangeSource
    {
        int Range(int minInclusive, int maxInclusive);
    }

    /// <summary>Always succeeds, and always picks the top of a range.</summary>
    /// <remarks>The default for a caller with no source: a service must not silently fail
    /// a player's materials because nobody wired a generator.</remarks>
    public sealed class AlwaysSucceeds : IRandomResultSource, IRandomRangeSource
    {
        public static readonly AlwaysSucceeds Instance = new AlwaysSucceeds();

        public bool Succeeds(float successChance)
        {
            return true;
        }

        public int Range(int minInclusive, int maxInclusive)
        {
            return maxInclusive >= minInclusive ? maxInclusive : minInclusive;
        }
    }

    /// <summary>Always fails, and always picks the bottom of a range.</summary>
    public sealed class AlwaysFails : IRandomResultSource, IRandomRangeSource
    {
        public static readonly AlwaysFails Instance = new AlwaysFails();

        public bool Succeeds(float successChance)
        {
            return false;
        }

        public int Range(int minInclusive, int maxInclusive)
        {
            return minInclusive;
        }
    }

    /// <summary>
    /// Replays a fixed sequence of outcomes.
    /// </summary>
    /// <remarks>
    /// For exercising a run of attempts -- succeed, succeed, fail, downgrade -- as one
    /// deterministic story. Past the end it repeats the last outcome rather than throwing,
    /// so a test that makes one extra call gets a defined answer instead of an exception
    /// that hides what it was really checking.
    /// </remarks>
    public sealed class ScriptedResultSource : IRandomResultSource, IRandomRangeSource
    {
        private readonly List<bool> _outcomes = new List<bool>();
        private readonly List<int> _numbers = new List<int>();
        private int _next;
        private int _nextNumber;

        public ScriptedResultSource(params bool[] outcomes)
        {
            if (outcomes == null) return;
            for (int i = 0; i < outcomes.Length; i++) _outcomes.Add(outcomes[i]);
        }

        /// <summary>How many times an outcome has been asked for.</summary>
        public int Calls { get; private set; }

        /// <summary>How many times a number has been asked for.</summary>
        public int RangeCalls { get; private set; }

        /// <summary>Scripts the numbers <see cref="Range"/> returns, clamped into range.</summary>
        public ScriptedResultSource WithNumbers(params int[] numbers)
        {
            if (numbers == null) return this;
            for (int i = 0; i < numbers.Length; i++) _numbers.Add(numbers[i]);
            return this;
        }

        public bool Succeeds(float successChance)
        {
            Calls++;

            if (_outcomes.Count == 0) return true;

            int index = _next < _outcomes.Count ? _next : _outcomes.Count - 1;
            if (_next < _outcomes.Count) _next++;

            return _outcomes[index];
        }

        /// <summary>
        /// The next scripted number, clamped.
        /// </summary>
        /// <remarks>With nothing scripted it returns the bottom of the range, which is the
        /// least surprising default: a caller that forgot to script quantities gets the
        /// smallest legal drop rather than the largest.</remarks>
        public int Range(int minInclusive, int maxInclusive)
        {
            RangeCalls++;

            if (maxInclusive < minInclusive) maxInclusive = minInclusive;
            if (_numbers.Count == 0) return minInclusive;

            int index = _nextNumber < _numbers.Count ? _nextNumber : _numbers.Count - 1;
            if (_nextNumber < _numbers.Count) _nextNumber++;

            int value = _numbers[index];
            return value < minInclusive ? minInclusive
                : value > maxInclusive ? maxInclusive : value;
        }
    }

    /// <summary>
    /// Compares the authored chance against a fixed roll.
    /// </summary>
    /// <remarks>
    /// The only implementation that actually reads <paramref name="successChance"/>, which
    /// makes it the one that pins the boundary: the comparison is <c>roll &lt; chance</c>,
    /// so a roll of exactly the chance fails. That convention matters -- it means a 0.0
    /// chance can never succeed on a 0.0 roll, and a 1.0 chance always succeeds because no
    /// roll reaches 1.0.
    /// </remarks>
    public sealed class ThresholdResultSource : IRandomResultSource, IRandomRangeSource
    {
        private readonly float _roll;

        public ThresholdResultSource(float roll)
        {
            _roll = roll;
        }

        public bool Succeeds(float successChance)
        {
            if (successChance <= 0f) return true;
            return _roll < successChance;
        }

        /// <summary>
        /// The same roll, read as a position within the range.
        /// </summary>
        /// <remarks>A roll of 0 gives the minimum and a roll approaching 1 gives the
        /// maximum, so one number drives both questions consistently -- which is what makes
        /// a 1-in-a-million drop testable with a single fixture.</remarks>
        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;

            int span = maxInclusive - minInclusive + 1;
            int offset = (int)(_roll * span);

            if (offset < 0) offset = 0;
            if (offset >= span) offset = span - 1;

            return minInclusive + offset;
        }
    }
}
