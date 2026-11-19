namespace Moltaro.Signals;

// The arithmetic of time that the timers of the host and of the client share, so that each rule lives once: the longest
// timer of TimeProvider, the due time of a timer for a deadline or for the rest of a time, and the time between two
// timestamps.
internal static class Timing
{
    // The longest timer of TimeProvider: the most a TimeSpan option may be, and the bound beyond which a deadline of
    // WaitAsync counts as none.
    internal static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    // The due time of a timer for a deadline, as DueTime gives it. Zero when the deadline has come; null when it is
    // further away than LongestTimer, where it counts as none.
    internal static TimeSpan? TimeUntil(DateTimeOffset deadline, DateTimeOffset now)
    {
        TimeSpan due = DueTime(deadline - now);

        return due <= LongestTimer ? due : null;
    }

    // The time from timestamp to now, both timestamps of timeProvider; zero when timestamp is the later one, as when
    // another thread took it after now was read.
    internal static TimeSpan Elapsed(TimeProvider timeProvider, long timestamp, long now)
    {
        TimeSpan elapsed = timeProvider.GetElapsedTime(timestamp, now);

        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }

    // The due time of a timer that is to fire once rest has passed, rounded up to whole milliseconds, because a timer
    // counts whole milliseconds and would otherwise fire early. Zero when no time is left.
    internal static TimeSpan DueTime(TimeSpan rest)
    {
        long ticks = rest.Ticks;

        if (ticks <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds((ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond);
    }
}
