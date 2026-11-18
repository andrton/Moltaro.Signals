namespace Moltaro.Signals.TestSupport;

// A timer of a FakeClock: the timer of FakeTimeProvider, with every change reported to the clock after it was made.
internal sealed class FakeClockTimer(FakeClock clock, ITimer timer) : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        bool changed = timer.Change(dueTime, period);

        clock.Arm(this, dueTime, period);

        return changed;
    }

    public void Dispose()
    {
        timer.Dispose();
        clock.Disarm(this);
    }

    public async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync();
        clock.Disarm(this);
    }
}
