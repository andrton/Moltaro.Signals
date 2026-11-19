using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// A clock whose wall time a test can move apart from its timestamps: the timestamps and the timers of a FakeClock, with
// its wall time moved by Shift. FakeTimeProvider takes its timestamps from its wall time, so the two cannot part
// there; here a test sets the wall clock back or forward while the timestamps go on.
internal sealed class ShiftedWallClock(FakeClock clock) : TimeProvider
{
    private long _shiftTicks;

    public TimeSpan Shift
    {
        get => new(Volatile.Read(ref _shiftTicks));
        set => Volatile.Write(ref _shiftTicks, value.Ticks);
    }

    public override long TimestampFrequency => clock.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + Shift;

    public override long GetTimestamp() => clock.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        clock.CreateTimer(callback, state, dueTime, period);
}
