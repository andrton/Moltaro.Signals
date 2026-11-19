using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.Remote;

// Members of one group on clients of one host, over loopback. A member's Joined comes only once the host has opened
// it, and a member is busy after its Joined. A wait that is cancelled ends with OperationCanceledException only when
// no signal was handed to it.
public sealed class MemberTests
{
    [Fact]
    public async Task A_set_of_a_client_wakes_one_waiting_member_of_a_group_across_clients()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        await using var clientA = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        await using var clientB = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientA.StateChanged += (_, change) =>
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                connected.TrySetResult();
            }
        };
        clientA.Start();
        clientB.Start();
        using ISignalSubscription memberA = clientA.Subscribe("jobs", "workers");
        using ISignalSubscription memberB = clientB.Subscribe("jobs", "workers");
        await memberA.WaitAsync(until: null, cancellationToken);
        await memberB.WaitAsync(until: null, cancellationToken);
        await connected.Task.WaitAsync(cancellationToken);

        using var stopWaiting = new CancellationTokenSource();
        Task<SignalWaitResultEnum> first = memberA.WaitAsync(until: null, stopWaiting.Token).AsTask();
        Task<SignalWaitResultEnum> second = memberB.WaitAsync(until: null, stopWaiting.Token).AsTask();

        SignalSetResult result = await clientA.SetAsync("jobs", cancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, result.Status);
        Assert.Equal(1, result.Groups);

        Task<SignalWaitResultEnum> woken = await Task.WhenAny(first, second);
        Task<SignalWaitResultEnum> other = woken == first ? second : first;
        await stopWaiting.CancelAsync();

        Assert.Equal(SignalWaitResultEnum.Signaled, await woken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
    }

    [Fact]
    public async Task A_busy_member_whose_client_disconnects_hands_its_signal_to_a_waiting_member()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        var leavingClient = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        await using var stayingClient = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        leavingClient.Start();
        stayingClient.Start();
        ISignalSubscription leaving = leavingClient.Subscribe("jobs", "workers");
        using ISignalSubscription staying = stayingClient.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);

        await leavingClient.DisposeAsync();

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    [Fact]
    public async Task A_busy_member_that_unsubscribes_hands_its_signal_to_a_waiting_member()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var host = new SignalHost(LoopbackOptions.Host());
        host.Start();
        await using var leavingClient = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        await using var stayingClient = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        leavingClient.Start();
        stayingClient.Start();
        ISignalSubscription leaving = leavingClient.Subscribe("jobs", "workers");
        using ISignalSubscription staying = stayingClient.Subscribe("jobs", "workers");
        await leaving.WaitAsync(until: null, cancellationToken);
        await staying.WaitAsync(until: null, cancellationToken);
        ValueTask<SignalWaitResultEnum> wait = staying.WaitAsync(until: null, cancellationToken);

        leaving.Dispose();

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // The first member makes the connection hold as many open subscriptions as the host allows; the host refuses the
    // second, and the refusal is final for it.
    [Fact]
    public async Task A_subscription_beyond_MaxSubscriptionsPerConnection_reports_InvalidOperationException_through_its_waits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignalHostOptions options = LoopbackOptions.Host();
        options.MaxSubscriptionsPerConnection = 1;
        await using var host = new SignalHost(options);
        host.Start();
        await using var client = new SignalClient(LoopbackOptions.Client(host.LocalEndPoint!));
        client.Start();
        using ISignalSubscription admitted = client.Subscribe("jobs", "workers");
        await admitted.WaitAsync(until: null, cancellationToken);
        using ISignalSubscription refused = client.Subscribe("mail", "senders");

        ValueTask<SignalWaitResultEnum> wait = refused.WaitAsync(until: null, cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await wait);

        ValueTask<SignalWaitResultEnum> laterWait = refused.WaitAsync(until: null, cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await laterWait);
    }
}
