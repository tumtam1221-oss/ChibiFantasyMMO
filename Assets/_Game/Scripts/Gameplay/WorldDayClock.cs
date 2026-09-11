namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// What day it is, according to the database, on a server that has been up for days.
    /// </summary>
    /// <remarks>
    /// <b>The problem this solves.</b> The day number a daily quest is measured against
    /// comes from the database, because the completion times were stamped by the database.
    /// But a world server is handed that number once, when a character loads, and then runs
    /// through midnight -- possibly through several. Keeping the number it was given would
    /// mean dailies that never reset on a server nobody restarts.
    ///
    /// <b>Why it does not simply read the machine clock.</b> That would be a second clock.
    /// Every datetime column in this schema is a naked <c>DATETIME</c> with no offset, and a
    /// world server on a machine an hour off would roll its dailies over an hour away from
    /// the midnight the completions were written in. The project has had that bug once
    /// already, between MySQL and PHP, and settled it by letting one clock write the times.
    ///
    /// <b>What it uses instead.</b> Two facts from the database -- the day number, and how
    /// many seconds remain until that number changes -- plus elapsed time, which is a
    /// duration and belongs to no timezone. Counting forward from a boundary the database
    /// chose lands on the database's midnight without ever asking what a timezone is.
    ///
    /// <b>It only ever moves forward.</b> A fresh sync that disagreed downward would let a
    /// daily be claimed twice across the seam; the later of the two answers wins.
    /// </remarks>
    public sealed class WorldDayClock
    {
        private const double SecondsPerDay = 86400.0;

        private int _baseDay;
        private double _secondsToBoundary;
        private double _syncedAtSeconds;
        private bool _known;

        /// <summary>
        /// Supplies the elapsed-seconds reading. Required: this has no default.
        /// </summary>
        /// <remarks>
        /// <b>Injected rather than read from the engine.</b> Gameplay in this project holds
        /// no reference to UnityEngine at all -- several tests scan the folder and fail on
        /// one -- and that rule is what lets these rules run in a test, on a server, or in a
        /// tool without a player loop. The world composes this with the engine's clock; a
        /// test composes it with a number it controls.
        ///
        /// <b>A duration, never a wall clock.</b> Whoever supplies it must return seconds
        /// that only increase. The whole point of this class is that no calendar arithmetic
        /// happens outside the database, so a source that returned a date would defeat it.
        /// </remarks>
        public System.Func<double> ElapsedSeconds { get; set; }

        /// <summary>Whether anything has told this what day it is.</summary>
        public bool IsKnown => _known;

        /// <summary>The day the database last reported. For diagnostics.</summary>
        public int SyncedDay => _baseDay;

        /// <summary>
        /// Records what the database said, and when it said it.
        /// </summary>
        /// <param name="day">The database's day number at the moment of the reply.</param>
        /// <param name="secondsUntilNextDay">How long until that number increments.</param>
        public void Sync(int day, int secondsUntilNextDay)
        {
            if (day <= 0) return;

            double now = Now();

            // Forward only. A stale reply arriving late must not walk the day backwards and
            // re-open a daily somebody has already claimed today.
            if (_known && DayAt(now) > day) return;

            _baseDay = day;
            _secondsToBoundary = secondsUntilNextDay < 0 ? 0 : secondsUntilNextDay;
            _syncedAtSeconds = now;
            _known = true;
        }

        /// <summary>Today's day number, or zero when nothing has said.</summary>
        /// <remarks>Zero is the value the quest rules read as "no date", which leaves a
        /// daily behaving like a one-time quest rather than handing out a second reward.</remarks>
        public int Today => _known ? DayAt(Now()) : 0;

        private int DayAt(double now)
        {
            double elapsed = now - _syncedAtSeconds;

            if (elapsed < _secondsToBoundary) return _baseDay;

            // Past the first boundary; every further whole day adds one.
            double past = elapsed - _secondsToBoundary;

            return _baseDay + 1 + (int)(past / SecondsPerDay);
        }

        private double Now()
        {
            System.Func<double> source = ElapsedSeconds;

            // No engine fallback. A clock nobody wired reports no elapsed time, which
            // leaves the day exactly where the database put it -- a daily behaves like a
            // one-time quest until somebody notices, rather than rolling over on a clock
            // this assembly is not allowed to read.
            return source == null ? 0.0 : source();
        }

        /// <summary>Forgets the sync, so a test can start from nothing.</summary>
        public void Forget()
        {
            _known = false;
            _baseDay = 0;
            _secondsToBoundary = 0;
            _syncedAtSeconds = 0;
        }
    }
}
