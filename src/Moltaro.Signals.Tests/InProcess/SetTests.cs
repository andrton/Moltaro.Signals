using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.InProcess;

// Rules 1, 2, 3 and 5 of concept section 3, through the in-process ISignals of a host. A member first does its pass
// for Joined, so that no Joined is due when the test begins. A wait that is cancelled ends with
// OperationCanceledException only when no signal was handed to it, so cancelling one shows that it received none.
public sealed class SetTests
{
    [Fact]
    public async Task A_set_hands_the_signal_to_one_waiting_member_of_each_group()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription worker1 = host.Subscribe("jobs", "workers");
        using ISignalSubscription worker2 = host.Subscribe("jobs", "workers");
        using ISignalSubscription cache = host.Subscribe("jobs", "cache");
        await worker1.WaitAsync(until: null, cancellationToken);
        await worker2.WaitAsync(until: null, cancellationToken);
        await cache.WaitAsync(until: null, cancellationToken);

        using var stopWaiting = new CancellationTokenSource();
        Task<SignalWaitResultEnum> first = worker1.WaitAsync(until: null, stopWaiting.Token).AsTask();
        Task<SignalWaitResultEnum> second = worker2.WaitAsync(until: null, stopWaiting.Token).AsTask();
        ValueTask<SignalWaitResultEnum> cacheWait = cache.WaitAsync(until: null, cancellationToken);

        await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await cacheWait);

        Task<SignalWaitResultEnum> woken = await Task.WhenAny(first, second);
        Task<SignalWaitResultEnum> other = woken == first ? second : first;
        await stopWaiting.CancelAsync();

        Assert.Equal(SignalWaitResultEnum.Signaled, await woken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
    }

    [Fact]
    public async Task A_set_returns_Applied_with_the_number_of_groups_that_have_members_on_its_key()
    {
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription worker1 = host.Subscribe("jobs", "workers");
        using ISignalSubscription worker2 = host.Subscribe("jobs", "workers");
        using ISignalSubscription cache = host.Subscribe("jobs", "cache");
        using ISignalSubscription sender = host.Subscribe("mail", "senders");

        SignalSetResult result = await host.SetAsync("jobs", TestContext.Current.CancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, result.Status);
        Assert.Equal(2, result.Groups);
    }

    [Fact]
    public async Task A_set_that_nobody_listens_to_returns_zero_groups_and_is_not_remembered()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());

        SignalSetResult result = await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, result.Status);
        Assert.Equal(0, result.Groups);

        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        using var stopWaiting = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);
        await stopWaiting.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
    }

    [Fact]
    public async Task A_set_while_a_member_is_busy_makes_its_next_wait_return_Signaled()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);

        SignalSetResult result = await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(1, result.Groups);
        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));
    }

    [Fact]
    public async Task Sets_while_a_member_is_busy_give_it_one_pass_and_no_more()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);

        for (int set = 0; set < 20; set++)
        {
            await host.SetAsync("jobs", cancellationToken);
        }

        Assert.Equal(SignalWaitResultEnum.Signaled, await lane.WaitAsync(until: null, cancellationToken));

        using var stopWaiting = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);
        await stopWaiting.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
    }

    [Fact]
    public async Task A_set_with_a_token_already_cancelled_ends_cancelled_and_sets_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lane = host.Subscribe("jobs", "workers");
        await lane.WaitAsync(until: null, cancellationToken);
        using var stopWaiting = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);

        ValueTask<SignalSetResult> set = host.SetAsync("jobs", new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await set);

        await stopWaiting.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
    }

    [Fact]
    public async Task Keys_and_groups_are_compared_exactly_case_included()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        using ISignalSubscription lower = host.Subscribe("jobs", "workers");
        using ISignalSubscription upper = host.Subscribe("jobs", "Workers");

        SignalSetResult otherKey = await host.SetAsync("Jobs", cancellationToken);
        SignalSetResult sameKey = await host.SetAsync("jobs", cancellationToken);

        Assert.Equal(0, otherKey.Groups);
        Assert.Equal(2, sameKey.Groups);
    }
}
