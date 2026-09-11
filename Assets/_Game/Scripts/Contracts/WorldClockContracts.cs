using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>
    /// A world's calendar as it was written down, and the rate it was measured against.
    /// </summary>
    /// <remarks>
    /// <b>The pair travels together on purpose.</b> An elapsed total is meaningless without
    /// the day length it was counted under: a total saved when a day was an hour, read back
    /// into a world where a day is two, would put the world weeks away from where it stopped.
    /// Carrying the rate lets the reader notice and refuse rather than silently teleport.
    ///
    /// <b>Elapsed rather than time of day.</b> The date is derived from the total by
    /// division, so an hour on its own loses the day every midnight.
    /// </remarks>
    public readonly struct WorldClockState
    {
        public WorldClockState(double elapsedSeconds, double secondsPerDay)
        {
            ElapsedSeconds = elapsedSeconds;
            SecondsPerDay = secondsPerDay;
        }

        public double ElapsedSeconds { get; }

        public double SecondsPerDay { get; }

        /// <summary>Whether this is a clock a world could actually be resumed from.</summary>
        /// <remarks>A saved row that fails this is treated as no row at all: a world that
        /// cannot read its calendar should open at the authored hour rather than at a
        /// nonsense one.</remarks>
        public bool IsUsable =>
            !double.IsNaN(ElapsedSeconds) && !double.IsInfinity(ElapsedSeconds)
            && ElapsedSeconds >= 0.0
            && !double.IsNaN(SecondsPerDay) && !double.IsInfinity(SecondsPerDay)
            && SecondsPerDay > 0.0;
    }

    /// <summary>
    /// Where a world server writes its calendar down so a restart resumes it.
    /// </summary>
    /// <remarks>
    /// <b>Both halves may fail, and neither failure is fatal.</b> A world that cannot read
    /// its calendar starts the day at the authored hour; a world that cannot write it loses
    /// the last few minutes on the next restart. Both are worse than working and both are far
    /// better than a server that refuses to start because the database was briefly away.
    ///
    /// <b>Keyed by server and channel.</b> That is the unit one world server process serves.
    /// Two channels are two worlds and may honestly be at different hours.
    /// </remarks>
    public interface IWorldClockStore
    {
        /// <summary>What this world was last saved at, or null if it has never been saved.</summary>
        WorldClockState? Load(ServerId server, ChannelId channel);

        /// <summary>Writes where the world has got to. False when it could not be written.</summary>
        bool Save(ServerId server, ChannelId channel, WorldClockState state);
    }
}
