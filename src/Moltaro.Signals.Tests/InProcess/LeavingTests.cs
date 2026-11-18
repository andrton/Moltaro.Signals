using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// Rule 7 of concept section 3 and the Dispose of a subscription, through the in-process ISignals of a host. A member
// is busy after its Joined, so a member that leaves right after it holds a signal to hand on. A wait that is
// cancelled ends with OperationCanceledException only when no signal was handed to it.
public sealed class LeavingTests
{
    [Fact]
    public async Task A_busy_member_that_leaves_hands_its_signal_to_a_waiting_member_of_its_group()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription leaving = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);

        leaving.Dispose();

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    [Fact]
    public async Task A_busy_member_that_leaves_hands_its_signal_on_as_pending_for_a_busy_member_of_its_group()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription leaving = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);

        leaving.Dispose();

        Assert.Equal(SignalWaitResultEnum.Signaled, await staying.WaitAsync(until: null, cancellationToken));
    }

    [Fact]
    public async Task A_waiting_member_that_leaves_ends_its_wait_with_ObjectDisposedException_and_hands_nothing_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription leaving = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying = host.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> leavingWait = leaving.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> stayingWait = staying.WaitAsync(until: null, stopWaiting.Token);

        leaving.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await leavingWait);
        Assert.Throws<ObjectDisposedException>(() => leaving.WaitAsync(until: null, cancellationToken));

        await stopWaiting.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stayingWait);
    }

    [Fact]
    public async Task Disposing_a_member_again_does_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        ISignalSubscription leaving = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying1 = host.Subscribe("jobs", "workers");
        using ISignalSubscription staying2 = host.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying1.WaitAsync(until: null, cancellationToken);
        await staying2.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();
        Task<SignalWaitResultEnum> first = staying1.WaitAsync(until: null, stopWaiting.Token).AsTask();
        Task<SignalWaitResultEnum> second = staying2.WaitAsync(until: null, stopWaiting.Token).AsTask();

        leaving.Dispose();
        leaving.Dispose();

        Task<SignalWaitResultEnum> woken = await Task.WhenAny(first, second);
        Task<SignalWaitResultEnum> other = woken == first ? second : first;
        await stopWaiting.CancelAsync();

        Assert.Equal(SignalWaitResultEnum.Signaled, await woken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
    }
}
