using Moltaro.Signals.Protocol;

namespace Moltaro.Signals;

// The argument checks that the public members of the host and of the client share, so that each lives once, and the
// longest timer of TimeProvider with the due times of timers. Each check reports the ParamName it is given.
internal static class SignalChecks
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

    internal static void CheckName(string? name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);

        if (!ProtocolRules.IsName(name))
        {
            throw new ArgumentException(
                $"A name is 1 to {ProtocolRules.MaxNameLength} characters, each one of A-Z a-z 0-9 . _ : -, and no segment between the dots is empty.",
                paramName);
        }
    }

    // A token left null is a required property left null, so it gives ArgumentException as a broken token does.
    internal static void CheckToken(string? token, string paramName)
    {
        if (token is null || !ProtocolRules.IsToken(token))
        {
            throw new ArgumentException(
                $"The token is required: 1 to {ProtocolRules.MaxTokenLength} characters, each from U+0021 to U+007E.",
                paramName);
        }
    }

    internal static void CheckTimeSpan(TimeSpan value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, LongestTimer, paramName);
    }
}
