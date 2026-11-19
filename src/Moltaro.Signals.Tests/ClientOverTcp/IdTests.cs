using Moltaro.Signals.Tests.RawProtocol;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The ids a client chooses for its subscriptions and its requests, against a raw host: none is used twice on a
// connection, and a SIG for a <sid> the client has closed is ignored, since the host handed that signal on when it
// removed the member; an answer for an open <sid> that has nothing outstanding breaks the protocol and closes the
// connection. While connected, the lines of a client reach the host in the order of its calls.
public sealed class IdTests
{
    // Long enough that no heartbeat falls due while a test advances the clock to the next attempt.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    // On the first connection: a member opened at connecting, a second one that is closed, a third one opened after it,
    // and a set. On the next connection, after the host ended the first: the two open members, opened again, and
    // another set.
    [Fact]
    public async Task No_id_is_used_twice_on_a_connection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription first = client.Subscribe("jobs", "workers");
        client.Start();
        RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        long firstSid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        ISignalSubscription closed = client.Subscribe("jobs", "workers");
        long closedSid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        closed.Dispose();
        Assert.Equal($"UNSUB {closedSid}", await connection.ReadLineAsync(cancellationToken));
        using ISignalSubscription third = client.Subscribe("jobs", "workers");
        long thirdSid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        long rid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);

        Assert.Equal(4, new HashSet<long> { firstSid, closedSid, thirdSid, rid }.Count);

        connection.Dispose();
        Assert.Equal(SignalConnectionStateEnum.Disconnected, (await changes.NextAsync(cancellationToken)).State);
        await clock.WaitForTimerAsync(clock.GetUtcNow() + ReconnectDelay, cancellationToken);
        clock.Advance(ReconnectDelay);
        using RawConnection next = await host.AcceptAsync(changes, cancellationToken);
        long firstReopened = await next.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        long thirdReopened = await next.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        ValueTask<SignalSetResult> later = client.SetAsync("jobs", cancellationToken);
        long laterRid = await next.ReadIdAsync("SET", "jobs", cancellationToken);

        Assert.Equal(3, new HashSet<long> { firstReopened, thirdReopened, laterRid }.Count);
    }

    // The member waits when it is closed, so a SIG that the host wrote before it read the UNSUB can still arrive. Here
    // it comes just before the answer to a set, which shows that the client read the SIG and kept the connection.
    [Fact]
    public async Task A_SIG_for_a_sid_the_client_has_closed_is_ignored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var changes = new StateChanges(client);
        ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);
        Assert.Equal($"WAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        lane.Dispose();

        Assert.Equal($"UNSUB {sid}", await connection.ReadLineAsync(cancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await wait);

        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        long rid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);
        await connection.SendAsync($"SIG {sid}\r\nOK {rid} 0\r\n", cancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, (await set).Status);
        Assert.Equal(SignalConnectionStateEnum.Connected, client.State);
    }

    // The member is open and does not wait, so the host has nothing of it to answer.
    [Fact]
    public async Task An_answer_for_an_open_sid_with_nothing_outstanding_closes_the_connection_with_a_reason()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(new FakeClock(), Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);

        SignalConnectionChangedEventArgs end = await changes.NextAsync(cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, end.State);
        Assert.NotNull(end.Reason);
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    private static void Configure(SignalClientOptions options)
    {
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
        options.ReconnectDelay = ReconnectDelay;
    }
}
