using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The waits of a client's member against a raw host. A Joined comes only once the host has answered the SUB that opens
// the member on a connection. A wait that its deadline or its token ends is ended with the host by UNWAIT, and its
// result waits for the host's answer: a SIG written before that answer crossed the UNWAIT, and the wait returns
// Signaled.
public sealed class WaitTests
{
    // Long enough that no heartbeat falls due while a test advances the clock to a deadline or to the next attempt.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_member_subscribed_while_connected_returns_Joined_only_once_the_host_has_answered_its_SUB()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);

        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);

        Assert.False(wait.IsCompleted);

        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Joined, await wait);
    }

    // The member waits when the host ends the connection. Its wait returns Joined from the next connection, once the
    // host has answered the SUB that reopens it there; the client reports Connected before.
    [Fact]
    public async Task A_member_reopened_after_a_reconnect_returns_Joined_only_once_the_host_has_answered_its_SUB()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        RawConnection first = await host.AcceptAsync(changes, cancellationToken);
        long sid = await first.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await first.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);
        Assert.Equal($"WAIT {sid}", await first.ReadLineAsync(cancellationToken));

        first.Dispose();
        Assert.Equal(SignalConnectionStateEnum.Disconnected, (await changes.NextAsync(cancellationToken)).State);
        await clock.WaitForTimerAsync(clock.GetUtcNow() + ReconnectDelay, cancellationToken);
        clock.Advance(ReconnectDelay);
        using RawConnection second = await host.AcceptAsync(changes, cancellationToken);
        long reopened = await second.ReadIdAsync("SUB", "jobs workers", cancellationToken);

        Assert.False(wait.IsCompleted);

        await second.SendAsync($"OK {reopened}\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Joined, await wait);
    }

    // First without a SIG: the host answers the UNWAIT alone. Then the host had handed the member a signal before it
    // read the UNWAIT, so it writes the SIG before its answer.
    [Fact]
    public async Task A_wait_ended_by_its_deadline_returns_DeadlineReached_or_Signaled_when_a_SIG_crosses_its_UNWAIT()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        long sid = await JoinAsync(connection, lane, cancellationToken);

        ValueTask<SignalWaitResultEnum> wait = await WaitUntilDeadlineAsync(clock, connection, lane, sid, cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.DeadlineReached, await wait);

        wait = await WaitUntilDeadlineAsync(clock, connection, lane, sid, cancellationToken);
        await connection.SendAsync($"SIG {sid}\r\nOK {sid}\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // As with the deadline: first without a SIG, then with one that crossed the UNWAIT.
    [Fact]
    public async Task A_wait_ended_by_its_token_ends_cancelled_or_returns_Signaled_when_a_SIG_crosses_its_UNWAIT()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        long sid = await JoinAsync(connection, lane, cancellationToken);

        using var firstWait = new CancellationTokenSource();
        ValueTask<SignalWaitResultEnum> wait = await WaitUntilCancelledAsync(firstWait, connection, lane, sid, cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);

        using var secondWait = new CancellationTokenSource();
        wait = await WaitUntilCancelledAsync(secondWait, connection, lane, sid, cancellationToken);
        await connection.SendAsync($"SIG {sid}\r\nOK {sid}\r\n", cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await wait);
    }

    // Opens the member on the connection and takes its Joined; returns its sid.
    private static async Task<long> JoinAsync(RawConnection connection, ISignalSubscription lane, CancellationToken cancellationToken)
    {
        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);

        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        return sid;
    }

    // A wait with a deadline, armed on the host, whose deadline the clock then reaches: the client ends it with UNWAIT.
    private static async Task<ValueTask<SignalWaitResultEnum>> WaitUntilDeadlineAsync(
        FakeClock clock,
        RawConnection connection,
        ISignalSubscription lane,
        long sid,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = clock.GetUtcNow() + Deadline;
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(deadline, cancellationToken);
        Assert.Equal($"WAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(deadline, cancellationToken);
        clock.Advance(Deadline);
        Assert.Equal($"UNWAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        return wait;
    }

    // A wait with a token, armed on the host, whose token is then cancelled: the client ends it with UNWAIT.
    private static async Task<ValueTask<SignalWaitResultEnum>> WaitUntilCancelledAsync(
        CancellationTokenSource stopWaiting,
        RawConnection connection,
        ISignalSubscription lane,
        long sid,
        CancellationToken cancellationToken)
    {
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, stopWaiting.Token);
        Assert.Equal($"WAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        await stopWaiting.CancelAsync();
        Assert.Equal($"UNWAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        return wait;
    }

    private static void Configure(SignalClientOptions options)
    {
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
        options.ReconnectDelay = ReconnectDelay;
    }
}
