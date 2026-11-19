using Moltaro.Signals.Tests.HostOverTcp;
using Moltaro.Signals.TestSupport;

namespace Moltaro.Signals.Tests.ClientOverTcp;

// The heartbeats of a connected client and the dead peer, against a raw host and with a fake clock. The client sends
// PING when it has sent nothing, or received nothing, for HeartbeatInterval, and at most one PING in that time; it
// declares the connection dead when it has received nothing for DeadPeerTimeout.
//
// Every connection here is accepted at the instant t0 of the clock. While connected, the only timer of the client is
// that of its connection. The clock moves only after the raw host has read each line the client wrote, so a line was
// sent at the instant at which the client wrote it; and only after the client has shown that it read each line the
// raw host wrote, so a line was received at the instant at which the raw host wrote it.
public sealed class LivenessTests
{
    // Not the defaults, so that a time the client did not take from its options shows.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan DeadPeerTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    // A host that answers nothing after OK 0. Two sets wait for answers that never come: the first is the next line the
    // host reads, so no PING was sent within HeartbeatInterval of the first one; the second shows that the client is
    // connected just before DeadPeerTimeout. Both end Unavailable when the connection is declared dead.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_silent_host_gets_one_PING_in_each_HeartbeatInterval_and_is_declared_dead_after_DeadPeerTimeout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        DateTimeOffset t0 = clock.GetUtcNow();

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval);
        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(t0 + (2 * HeartbeatInterval), cancellationToken);
        clock.Advance(HeartbeatInterval - Millisecond);
        ValueTask<SignalSetResult> first = client.SetAsync("jobs", cancellationToken);
        await connection.ReadIdAsync("SET", "jobs", cancellationToken);

        await clock.WaitForTimerAsync(t0 + (2 * HeartbeatInterval), cancellationToken);
        clock.Advance(Millisecond);
        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(t0 + DeadPeerTimeout, cancellationToken);
        clock.Advance(DeadPeerTimeout - (2 * HeartbeatInterval) - Millisecond);
        ValueTask<SignalSetResult> second = client.SetAsync("mail", cancellationToken);
        await connection.ReadIdAsync("SET", "mail", cancellationToken);

        await clock.WaitForTimerAsync(t0 + DeadPeerTimeout, cancellationToken);
        clock.Advance(Millisecond);

        Assert.Equal(SignalSetStatusEnum.Unavailable, (await first).Status);
        Assert.Equal(SignalSetStatusEnum.Unavailable, (await second).Status);
        SignalConnectionChangedEventArgs end = await changes.NextAsync(cancellationToken);
        Assert.Equal(SignalConnectionStateEnum.Disconnected, end.State);
        Assert.NotNull(end.Reason);
        Assert.Null(await connection.ReadLineAsync(cancellationToken));
    }

    // The host keeps the client receiving: it answers the client's last line, a set sent at t0, half an interval later.
    // A client that counted only what it received would send its PING half an interval later than this.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_PING_is_sent_after_HeartbeatInterval_with_nothing_sent_while_the_host_keeps_sending()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        DateTimeOffset t0 = clock.GetUtcNow();
        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        long rid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval / 2);
        await connection.SendAsync($"OK {rid} 0\r\n", cancellationToken);
        Assert.Equal(SignalSetStatusEnum.Applied, (await set).Status);

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval / 2);

        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));
    }

    // The client keeps sending: its member sends a WAIT half an interval after t0, which the host never answers. A
    // client that counted only what it sent would send no PING at t0 + HeartbeatInterval, and would declare the
    // connection dead at t0 + DeadPeerTimeout. The host answers the PING; the answer to a set, written right after the
    // PONG, shows that the client has read the PONG before the clock moves on.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_client_that_keeps_sending_WAITs_sends_PING_after_HeartbeatInterval_with_nothing_received_and_stays_connected_while_its_PINGs_are_answered()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        using RawHost host = RawHost.Start();
        await using SignalClient client = host.CreateClient(clock, Configure);
        var changes = new StateChanges(client);
        using ISignalSubscription lane = client.Subscribe("jobs", "workers");
        client.Start();
        using RawConnection connection = await host.AcceptAsync(changes, cancellationToken);
        DateTimeOffset t0 = clock.GetUtcNow();
        long sid = await connection.ReadIdAsync("SUB", "jobs workers", cancellationToken);
        await connection.SendAsync($"OK {sid}\r\n", cancellationToken);
        Assert.Equal(SignalWaitResultEnum.Joined, await lane.WaitAsync(until: null, cancellationToken));

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval / 2);
        ValueTask<SignalWaitResultEnum> wait = lane.WaitAsync(until: null, cancellationToken);
        Assert.Equal($"WAIT {sid}", await connection.ReadLineAsync(cancellationToken));

        await clock.WaitForTimerAsync(t0 + HeartbeatInterval, cancellationToken);
        clock.Advance(HeartbeatInterval / 2);
        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));

        ValueTask<SignalSetResult> set = client.SetAsync("jobs", cancellationToken);
        long rid = await connection.ReadIdAsync("SET", "jobs", cancellationToken);
        await connection.SendAsync($"PONG\r\nOK {rid} 1\r\n", cancellationToken);
        Assert.Equal(SignalSetStatusEnum.Applied, (await set).Status);

        // The next PING falls due within this advance and is not answered.
        await clock.WaitForTimerAsync(t0 + (2 * HeartbeatInterval), cancellationToken);
        clock.Advance(DeadPeerTimeout - HeartbeatInterval);
        ValueTask<SignalSetResult> probe = client.SetAsync("mail", cancellationToken);
        Assert.Equal("PING", await connection.ReadLineAsync(cancellationToken));
        long probeRid = await connection.ReadIdAsync("SET", "mail", cancellationToken);
        await connection.SendAsync($"OK {probeRid} 0\r\n", cancellationToken);

        Assert.Equal(SignalSetStatusEnum.Applied, (await probe).Status);
        Assert.Equal(SignalConnectionStateEnum.Connected, client.State);
        Assert.False(wait.IsCompleted);
    }

    private static void Configure(SignalClientOptions options)
    {
        options.HeartbeatInterval = HeartbeatInterval;
        options.DeadPeerTimeout = DeadPeerTimeout;
    }
}
