using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// The results of WaitAsync, its deadline and its cancellation (rules 4 and 6 of concept section 3, and the rules of
// WaitAsync in the contract), through the in-process ISignals of a host.
public sealed class WaitTests
{
    [Fact]
    public async Task The_first_wait_after_subscribing_returns_Joined()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");

        SignalWaitResultEnum result = await lane.WaitAsync(until: null, TestContext.Current.CancellationToken);

        Assert.Equal(SignalWaitResultEnum.Joined, result);
    }

    [Fact]
    public async Task The_first_wait_returns_Joined_even_when_its_deadline_has_passed()
    {
        var clock = new FakeClock();
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        await using var host = new SignalHost(options);
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");

        SignalWaitResultEnum result = await lane.WaitAsync(
            clock.GetUtcNow().AddSeconds(-1),
            TestContext.Current.CancellationToken);

        Assert.Equal(SignalWaitResultEnum.Joined, result);
    }

    // After the deadline the member holds no signal: a set leaves a signal pending instead of handing it to the wait
    // that has ended.
    [Fact]
    public async Task A_wait_returns_DeadlineReached_when_its_deadline_comes_first()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        await using var host = new SignalHost(options);
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        DateTimeOffset deadline = clock.GetUtcNow().AddSeconds(5);

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(deadline, cancellationToken);

        Assert.False(wait.IsCompleted);

        await clock.WaitForTimerAsync(deadline, cancellationToken);

        Assert.False(wait.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(SignalWaitResultEnum.DeadlineReached, await wait);

        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));
    }

    // The pending signal is left as it was: the next wait takes it.
    [Fact]
    public async Task A_wait_whose_deadline_has_passed_returns_DeadlineReached_at_once_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        await using var host = new SignalHost(options);
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        await host.SetAsync("jobs", cancellationToken);

        SignalWaitResultEnum result = await lane.WaitAsync(clock.GetUtcNow().AddSeconds(-1), cancellationToken);

        Assert.Equal(SignalWaitResultEnum.DeadlineReached, result);
        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));
    }

    // A TimeProvider refuses a timer longer than its longest one, so a deadline taken as one would fail the wait.
    [Fact]
    public async Task A_deadline_further_away_than_the_longest_timer_counts_as_none()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        await using var host = new SignalHost(options);
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(clock.GetUtcNow().AddDays(100), cancellationToken);
        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // A deadline belongs to the wait it was given to. The later wait has no token, so a wait the deadline ended would
    // have completed inside Advance, which fires the clock's timers before it returns.
    [Fact]
    public async Task The_deadline_of_an_earlier_wait_does_not_end_a_later_wait()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        SignalHostOptions options = LoopbackOptions.Host();
        options.TimeProvider = clock;
        await using var host = new SignalHost(options);
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> earlier = lane.WaitAsync(clock.GetUtcNow().AddSeconds(5), cancellationToken);
        await host.SetAsync("jobs", cancellationToken);
        await earlier;

        ValueTask<SignalWaitResultEnum> later = lane.WaitAsync(until: null, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.False(later.IsCompleted);

        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await later);
    }

    // After the cancellation the member holds no signal: a set leaves a signal pending instead of handing it to the
    // wait that has ended.
    [Fact]
    public async Task A_cancelled_wait_ends_with_OperationCanceledException()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);
        await stopWaiting.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);

        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));
    }

    [Fact]
    public async Task A_wait_that_was_handed_a_signal_before_its_cancellation_returns_Signaled()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);
        await host.SetAsync("jobs", cancellationToken);
        await stopWaiting.CancelAsync();

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // The pending signal is left as it was: the next wait takes it.
    [Fact]
    public async Task A_wait_with_a_token_already_cancelled_ends_cancelled_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        await host.SetAsync("jobs", cancellationToken);

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));
    }

    [Fact]
    public async Task A_second_wait_while_one_is_pending_is_refused_and_leaves_the_first_alone()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);

        ValueTask<SignalWaitResultEnum> first = lane.WaitAsync(until: null, cancellationToken);

        Assert.Throws<InvalidOperationException>(() => lane.WaitAsync(until: null, cancellationToken));

        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await first);
    }
}
