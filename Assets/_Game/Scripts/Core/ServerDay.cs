namespace ChibiFantasy.Core
{
    /// <summary>
    /// A calendar day, counted the way the database counts them.
    /// </summary>
    /// <remarks>
    /// <b>Why a day number and not a date.</b> The only question the game ever asks of a
    /// completion time is whether today is a different day from the day something was
    /// finished. Two integers answer that with no timezone, no parsing and no formatting --
    /// and the integers come out of MySQL's <c>TO_DAYS</c>, so the day a quest was stamped
    /// in and the day it is compared against are counted by one clock.
    ///
    /// <b>The alternative is a bug the project has already had once.</b> Every datetime
    /// column in this schema is a naked <c>DATETIME</c> with no offset, and a world server
    /// deciding "today" from its own machine would reset dailies at a different midnight
    /// than the completions were written in. Sending a number sidesteps the whole class of
    /// failure -- see the migration's notes and <c>OneClockTest</c>.
    ///
    /// <b>The epoch constant is measured, not remembered.</b> <c>TO_DAYS('1970-01-01')</c>
    /// is 719528 on the deployment's MySQL, and a test pins that alongside two other dates
    /// so a wrong constant fails loudly rather than shifting every event by a year.
    /// </remarks>
    public static class ServerDay
    {
        /// <summary>What MySQL's TO_DAYS returns for 1970-01-01.</summary>
        public const int UnixEpochDay = 719528;

        /// <summary>The value meaning "never", which no real day can collide with.</summary>
        public const int Never = 0;

        private static readonly System.DateTime Epoch =
            new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        /// <summary>The day number for a calendar date.</summary>
        public static int FromDate(int year, int month, int day)
        {
            if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31) return Never;

            System.DateTime date;

            try
            {
                date = new System.DateTime(year, month, day, 0, 0, 0, System.DateTimeKind.Utc);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                // A date that does not exist -- the 30th of February in a content file --
                // is "never" rather than an exception a content load would die on.
                return Never;
            }

            return UnixEpochDay + (int)(date - Epoch).TotalDays;
        }

        /// <summary>
        /// Reads a date authored as <c>yyyy-MM-dd</c>.
        /// </summary>
        /// <remarks>An empty or malformed value is <see cref="Never"/> rather than a throw:
        /// a typo in an event window must leave the quest unavailable and visible to a test,
        /// not take the content loader down with it.</remarks>
        public static bool TryParse(string text, out int day)
        {
            day = Never;

            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('-');

            if (parts.Length != 3) return false;

            int year, month, dayOfMonth;

            if (!int.TryParse(parts[0], out year)) return false;
            if (!int.TryParse(parts[1], out month)) return false;
            if (!int.TryParse(parts[2], out dayOfMonth)) return false;

            day = FromDate(year, month, dayOfMonth);

            return day != Never;
        }

        /// <summary>Whether a day number names a real day rather than "never".</summary>
        public static bool IsRealDay(int day)
        {
            return day > Never;
        }
    }
}
