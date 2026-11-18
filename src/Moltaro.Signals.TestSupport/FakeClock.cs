using Microsoft.Extensions.Time.Testing;

namespace Moltaro.Signals.TestSupport;

// A fake clock: its time moves only when a test advances it. It wraps FakeTimeProvider and watches every timer that
// the code under test creates, changes or disposes, so that a test can wait until a timer is armed before it advances
// the time. Advanced too early, the time would pass before the timer is armed, and the timer would fire late.
public sealed class FakeClock : TimeProvider
{
    private readonly FakeTimeProvider _time = new();

    // Guards _armed and _waits. FakeTimeProvider is never called under it.
    private readonly Lock _lock = new();

    // Every armed timer: when it is first due, and its period.
    private readonly Dictionary<ITimer, (DateTimeOffset DueAt, TimeSpan Period)> _armed = [];

    private readonly List<(DateTimeOffset DueBy, TaskCompletionSource Armed)> _waits = [];

    public override long TimestampFrequency => _time.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _time.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();

    public override long GetTimestamp() => _time.GetTimestamp();

    // Moves the time forward. Every timer that becomes due fires on the calling thread before this returns.
    public void Advance(TimeSpan delta) => _time.Advance(delta);

    // Completes once a timer of this clock is armed to fire at or before dueBy; at once if one already is.
    public Task WaitForTimerAsync(DateTimeOffset dueBy, CancellationToken cancellationToken)
    {
        DateTimeOffset now = GetUtcNow();

        // Continuations run asynchronously, so that a test never advances the time inside the call that armed a timer.
        var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (IsAnyTimerDueBy(dueBy, now))
            {
                return Task.CompletedTask;
            }

            _waits.Add((dueBy, armed));
        }

        return armed.Task.WaitAsync(cancellationToken);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeClockTimer(this, _time.CreateTimer(callback, state, dueTime, period));

        Arm(timer, dueTime, period);

        return timer;
    }

    // Called after the timer of FakeTimeProvider was armed, so that a wait completes only once advancing fires it.
    internal void Arm(ITimer timer, TimeSpan dueTime, TimeSpan period)
    {
        DateTimeOffset now = GetUtcNow();

        lock (_lock)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                _armed.Remove(timer);
            }
            else
            {
                _armed[timer] = (now + dueTime, period);
            }

            for (int index = _waits.Count - 1; index >= 0; index--)
            {
                if (IsAnyTimerDueBy(_waits[index].DueBy, now))
                {
                    _waits[index].Armed.SetResult();
                    _waits.RemoveAt(index);
                }
            }
        }
    }

    internal void Disarm(ITimer timer)
    {
        lock (_lock)
        {
            _armed.Remove(timer);
        }
    }

    private bool IsAnyTimerDueBy(DateTimeOffset dueBy, DateTimeOffset now)
    {
        foreach ((DateTimeOffset dueAt, TimeSpan period) in _armed.Values)
        {
            if (NextDue(dueAt, period, now) <= dueBy)
            {
                return true;
            }
        }

        return false;
    }

    // A timer whose due instant has come has fired, because FakeTimeProvider fires timers as the time passes them. A
    // one-shot timer is then due no more; a periodic one is due at the next multiple of its period.
    private static DateTimeOffset? NextDue(DateTimeOffset dueAt, TimeSpan period, DateTimeOffset now)
    {
        if (dueAt > now)
        {
            return dueAt;
        }

        if (period == TimeSpan.Zero || period == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        long periodsPassed = (now - dueAt).Ticks / period.Ticks;

        return dueAt + (period * (periodsPassed + 1));
    }
}
